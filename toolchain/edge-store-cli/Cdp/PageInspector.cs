using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Cdp;

public sealed class PageInspector
{
    private readonly CdpClient _client;

    public PageInspector(CdpClient client) => _client = client;

    public async Task<PageSnapshot> CaptureAsync()
    {
        var wire = await _client.EvaluateAsync<PageSnapshotWire>("""
        (() => {
          const allRoots=[document];
          for(let i=0;i<allRoots.length;i++){
            try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){}
          }
          const deepAll = (selector) => {
            const out = [], seen = new Set();
            for (const root of allRoots) {
              try {
                for (const e of root.querySelectorAll(selector)) if (!seen.has(e)) { seen.add(e); out.push(e); }
              } catch (_) {}
            }
            return out;
          };
          const visible = e => {
            const r=e.getBoundingClientRect(), s=getComputedStyle(e);
            return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden';
          };
          const has = s => deepAll(s).some(visible);
          const hasAny = s => deepAll(s).length > 0;
          const url = location.href || '';
          const text = (document.body && document.body.innerText || '').replace(/\u00a0/g,' ').trim();
          const signals = {
            signIn: /login\.microsoftonline|login\.live\.com|signin/i.test(url),
            shellOnly: text.length < 80 && !hasAny('input,textarea,select,button,a[href*="/submissions/"]'),
            submissionLinks: deepAll('a[href*="/submissions/"]').length >= 2,
            availability: hasAny('input[name="marketSelection"],#saveButtonPricing'),
            properties: hasAny('select[name="CategorySelect"],input[name="privacyPolicySelection"]'),
            ageQuestionnaire: hasAny('input[name="inputMode"],[name="question#1109"]'),
            ageSummary: /\/ageratings\/summary/i.test(url) || /分级\s*ID|当前分级|rating\s*id/i.test(text),
            packages: /\/packages(?:[/?#]|$)/i.test(url) && (hasAny('input[type="file"]') || /Validated|验证|程序包/i.test(text)),
            listingGrid: hasAny('submission-listing-summary,a[href*="listings?languageid="],he-data-grid'),
            listingForm: hasAny('#description-required,textarea[name="description"],button[name="save_button"]') && /\/listings/i.test(url),
            options: /\/options(?:[/?#]|$)/i.test(url) && hasAny('input#radioReleaseDate_manual,input#radioReleaseDate_asap,textarea'),
            modal: has('[role="dialog"],[aria-modal="true"]'),
            certification: /正在认证|in certification|certification in progress/i.test(text),
            fatal: has('.error-page,[data-automation-id="error-page"]') || /something went wrong|出现问题/i.test(text)
          };

          let kind = 'Unknown';
          if (signals.signIn) kind='SignIn';
          else if (signals.fatal) kind='ErrorPage';
          else if (signals.shellOnly) kind='LoadingShell';
          else if (signals.certification) kind='CertificationStatus';
          else if (signals.modal) kind='SubmissionConfirmation';
          else if (signals.ageSummary) kind='AgeRatingsSummary';
          else if (signals.ageQuestionnaire) kind='AgeRatingsQuestionnaire';
          else if (signals.listingForm) kind='ListingForm';
          else if (signals.listingGrid && /\/listings|\/managelanguages/i.test(url)) kind='ListingLanguageGrid';
          else if (signals.availability) kind='AvailabilityForm';
          else if (signals.properties) kind='PropertiesForm';
          else if (signals.packages) kind='PackagesForm';
          else if (signals.options) kind='OptionsForm';
          else if (/\/submissions\/[^/?#]+\/overview/i.test(url) || signals.submissionLinks) kind='SubmissionOverview';
          else if (/\/products\/[^/?#]+\/overview/i.test(url)) kind='ProductOverview';

          const errors = deepAll('[role="alert"],.alert-error,.alert-danger,.validation-error,.field-validation-error')
            .filter(visible).map(e => (e.innerText||'').trim()).filter(Boolean).slice(0,20);
          const buttons = deepAll('button,he-button,[role="button"]')
            .filter(visible).map(e => (e.innerText||e.getAttribute('aria-label')||'').trim())
            .filter(Boolean).slice(0,40);
          const heading = (deepAll('main h1,main h2,h1').find(visible)?.innerText || '').trim();
          return { kind, ready: kind !== 'Unknown' && kind !== 'LoadingShell', url, title: document.title || '', heading,
                   textPreview: text.slice(0,1800), signals, buttons, visibleErrors: errors };
        })()
        """) ?? new PageSnapshotWire();

        _ = Enum.TryParse<PartnerPageKind>(wire.Kind, ignoreCase: true, out var kind);
        return new PageSnapshot
        {
            Kind = kind,
            Ready = wire.Ready,
            Url = wire.Url,
            Title = wire.Title,
            Heading = wire.Heading,
            TextPreview = wire.TextPreview,
            Signals = wire.Signals,
            Buttons = wire.Buttons,
            VisibleErrors = wire.VisibleErrors
        };
    }

    public async Task<PageSnapshot> WaitForAsync(
        IReadOnlyCollection<PartnerPageKind> accepted,
        TimeSpan timeout,
        string operation)
    {
        PageSnapshot last = new();
        Exception? firstError = null;
        var start = DateTime.UtcNow;
        var nextLog = start;
        PartnerPageKind stableUnexpected = PartnerPageKind.Unknown;
        int unexpectedSamples = 0;

        while (DateTime.UtcNow - start < timeout)
        {
            try
            {
                last = await CaptureAsync();
                if (accepted.Contains(last.Kind)) return last;
                if (last.Kind is PartnerPageKind.SignIn or PartnerPageKind.ErrorPage)
                    throw new InvalidOperationException($"{operation} reached terminal page state {last.Kind}: {last.Url}");
                if (last.Ready && last.Kind != PartnerPageKind.Unknown)
                {
                    unexpectedSamples = stableUnexpected == last.Kind ? unexpectedSamples + 1 : 1;
                    stableUnexpected = last.Kind;
                    if (unexpectedSamples >= 10)
                        throw new InvalidOperationException($"{operation} reached stable incompatible page {last.Kind}: {last.Url}");
                }
                else
                {
                    unexpectedSamples = 0;
                    stableUnexpected = PartnerPageKind.Unknown;
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException ||
                (!ex.Message.Contains("terminal page state") && !ex.Message.Contains("stable incompatible page")))
            {
                firstError ??= ex;
            }

            if (DateTime.UtcNow >= nextLog)
            {
                Console.WriteLine($"[WAIT] {operation}: page={last.Kind}, url={last.Url}");
                nextLog = DateTime.UtcNow.AddSeconds(5);
            }
            await Task.Delay(400);
        }

        string error = firstError == null ? "" : $" First probe error: {firstError.Message}";
        throw new TimeoutException($"{operation} timed out after {timeout.TotalSeconds:0}s. Last page={last.Kind}, url={last.Url}.{error}");
    }

    private sealed class PageSnapshotWire
    {
        public string Kind { get; set; } = "Unknown";
        public bool Ready { get; set; }
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Heading { get; set; } = string.Empty;
        public string TextPreview { get; set; } = string.Empty;
        public Dictionary<string, bool> Signals { get; set; } = [];
        public List<string> Buttons { get; set; } = [];
        public List<string> VisibleErrors { get; set; } = [];
    }
}
