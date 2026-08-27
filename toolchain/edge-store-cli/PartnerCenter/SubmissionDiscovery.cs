using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;

namespace Vainreef.EdgeStore.PartnerCenter;

public class SubmissionDiscovery
{
    private readonly CdpClient _client;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;

    public SubmissionDiscovery(CdpClient client, Waiter waiter, NativeFormAdapter native)
    {
        _client = client;
        _waiter = waiter;
        _native = native;
    }

    public async Task<DiscoveryResult> DiscoverAsync(string baseUrl, string productId, bool autoCreateIfMissing = true)
    {
        string overviewUrl = $"{baseUrl.TrimEnd('/')}/{productId}/overview";
        Console.WriteLine($"[NAV] Navigating to Product Overview: {overviewUrl}");
        await _waiter.NavigateAsync(overviewUrl, "Product Overview for Discovery");

        await _waiter.RequireAsync(async () =>
        {
            var len = await _client.EvaluateAsync<int>("document.body ? document.body.innerText.length : 0");
            return len > 100;
        }, TimeSpan.FromSeconds(30), "Wait for overview SPA content");

        // Wait up to 15 seconds for either the 6 module links to mount, or the '开始提交' button to appear
        DiscoveryResult current = new();
        bool hasStartBtn = false;
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (DateTime.UtcNow < deadline)
        {
            current = await ExtractFromDomAsync();
            if (!string.IsNullOrEmpty(current.SubmissionId) && current.Hrefs.Count >= 2)
            {
                Console.WriteLine($"[PASS] Found existing active submissionId: {current.SubmissionId}");
                return current;
            }

            hasStartBtn = await _client.EvaluateAsync<bool>("""
            (() => {
                const text = (document.body ? document.body.innerText : '') || '';
                return text.includes('开始提交') || text.includes('Start Submission') || text.includes('Start submission');
            })()
            """);

            if (hasStartBtn)
            {
                break;
            }

            await Task.Delay(800);
        }

        if (!string.IsNullOrEmpty(current.SubmissionId) && current.Hrefs.Count >= 2)
        {
            Console.WriteLine($"[PASS] Found existing active submissionId: {current.SubmissionId}");
            return current;
        }

        // If '开始提交' is present, click it to create the first draft submission
        if (hasStartBtn && autoCreateIfMissing)
        {
            Console.WriteLine("[INFO] No active draft submission found. Locating and clicking '开始提交' (Start Submission)...");
            await _native.ClickOptionByDeepTextAsync(["开始提交", "Start Submission", "Start submission", "创建新提交", "继续提交"], "Start Submission");

            Console.WriteLine("[INFO] Waiting for submission overview page to be created...");
            await _waiter.RequireAsync(async () =>
            {
                var res = await ExtractFromDomAsync();
                return !string.IsNullOrEmpty(res.SubmissionId) && res.Hrefs.Count >= 2;
            }, TimeSpan.FromSeconds(45), "Wait for new submission draft to be generated");

            current = await ExtractFromDomAsync();
        }

        if (string.IsNullOrEmpty(current.SubmissionId))
        {
            throw new InvalidOperationException("Failed to discover submission links or create new submission draft.");
        }

        Console.WriteLine($"[PASS] Active submissionId: {current.SubmissionId}");
        return current;
    }

    private async Task<DiscoveryResult> ExtractFromDomAsync()
    {
        var raw = await _client.EvaluateAsync<DiscoveryResultWire>("""
        (() => {
          const allRoots = [document];
          for (let i = 0; i < allRoots.length; i++) {
            try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
          }
          const deepAll = (selector) => {
            const out = [], seen = new Set();
            for (const root of allRoots) {
              try { for (const e of root.querySelectorAll(selector)) if (!seen.has(e)) { seen.add(e); out.push(e); } } catch (_) {}
            }
            return out;
          };

          let submissionId = '';
          const hrefs = {};

          const links = deepAll('a[href*="/submissions/"]');
          for (const a of links) {
            const href = a.href || '';
            const m = href.match(/\/submissions\/([^\/?#]+)/);
            if (m && !submissionId) submissionId = m[1];

            const name = (a.getAttribute('name') || '').toLowerCase();
            const h = href.toLowerCase();
            if (name === 'pricingandavailability' || name === 'princingandavailability' || h.includes('/availability')) hrefs['availability'] = href;
            else if (name === 'properties' || h.includes('/properties')) hrefs['properties'] = href;
            else if (name === 'ageratings' || h.includes('/ageratings')) hrefs['ageRatings'] = href;
            else if (name === 'packages' || h.includes('/packages')) hrefs['packages'] = href;
            else if (name === 'storelisting' || h.includes('/listings') || h.includes('/managelanguages')) hrefs['listing'] = href;
            else if (h.includes('/options')) hrefs['options'] = href;
          }

          if (!submissionId) {
            const m = location.href.match(/\/submissions\/([^\/?#]+)/);
            if (m) submissionId = m[1];
          }

          return { submissionId, hrefs };
        })()
        """);

        var res = new DiscoveryResult();
        if (raw != null)
        {
            res.SubmissionId = raw.SubmissionId ?? "";
            if (raw.Hrefs != null)
            {
                foreach (var kv in raw.Hrefs) res.Hrefs[kv.Key] = kv.Value;
            }
        }
        return res;
    }
}

public class DiscoveryResultWire
{
    public string? SubmissionId { get; set; }
    public Dictionary<string, string>? Hrefs { get; set; }
}

public class DiscoveryResult
{
    public string SubmissionId { get; set; } = string.Empty;
    public Dictionary<string, string> Hrefs { get; set; } = [];
}
