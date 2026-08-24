using System.Text.Json;
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
        await _waiter.NavigateAsync(overviewUrl, "Product Overview for Discovery");

        // Wait for overview page SPA body to render
        await _waiter.WaitUntilAsync(async () =>
        {
            var len = await _client.EvaluateAsync<int>("document.body ? document.body.innerText.length : 0");
            return len > 200;
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for overview SPA body content");

        var result = await ProbeOverviewDomAsync();
        if (!string.IsNullOrEmpty(result.SubmissionId))
        {
            return result;
        }

        // If no active draft submission exists and autoCreate is allowed, click Start Submission
        if (result.CanStartSubmission && autoCreateIfMissing)
        {
            await _native.ClickStrictAsync(["he-button[data-l10n-key=\"Start_Submission\"]", "button[data-l10n-key=\"Start_Submission\"]", "[data-automation-id=\"Start_Submission\"]"], "Start Submission");
            await _waiter.WaitForUrlAsync("/submissions/");
            await Task.Delay(1500);
            result = await ProbeOverviewDomAsync();
        }

        return result;
    }

    private async Task<DiscoveryResult> ProbeOverviewDomAsync()
    {
        var json = await _client.EvaluateAsync<string>("""
        (() => {
          const result = { submissionId: '', hrefs: {}, canStartSubmission: false };
          const startBtn = document.querySelector('he-button[data-l10n-key="Start_Submission"], button[data-l10n-key="Start_Submission"], [data-automation-id="Start_Submission"]');
          if (startBtn) result.canStartSubmission = true;

          const links = Array.from(document.querySelectorAll('a[href*="/submissions/"]'));
          for (const a of links) {
            const href = a.href || '';
            const m = href.match(/\/submissions\/([^\/?#]+)/);
            if (m && !result.submissionId) result.submissionId = m[1];

            const name = a.getAttribute('name') || '';
            if (name === 'princingAndAvailability' || href.includes('/availability')) result.hrefs['availability'] = href;
            else if (name === 'properties' || href.includes('/properties')) result.hrefs['properties'] = href;
            else if (name === 'ageRatings' || href.includes('/ageratings')) result.hrefs['ageRatings'] = href;
            else if (name === 'packages' || href.includes('/packages')) result.hrefs['packages'] = href;
            else if (name === 'storeListing' || href.includes('/listings') || href.includes('/managelanguages')) result.hrefs['listing'] = href;
            else if (href.includes('/options')) result.hrefs['options'] = href;
          }
          return JSON.stringify(result);
        })()
        """);

        if (string.IsNullOrEmpty(json))
        {
            return new DiscoveryResult();
        }

        return JsonSerializer.Deserialize<DiscoveryResult>(json) ?? new DiscoveryResult();
    }
}

public class DiscoveryResult
{
    public string SubmissionId { get; set; } = string.Empty;
    public Dictionary<string, string> Hrefs { get; set; } = [];
    public bool CanStartSubmission { get; set; }
}
