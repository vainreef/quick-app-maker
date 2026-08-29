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

    public async Task<DiscoveryResult> DiscoverAsync(string baseUrl, string productId, string knownSubmissionId = "", bool autoCreateIfMissing = true)
    {
        string currentUrl = await _client.EvaluateAsync<string>("location.href") ?? "";
        string submissionId = knownSubmissionId;

        // 1. 如果当前页面已经在 submission 路由下，直接提取 submissionId
        var m = System.Text.RegularExpressions.Regex.Match(currentUrl, @"/submissions/(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            submissionId = m.Groups[1].Value;
        }

        // 2. 如果已知 submissionId，直接加载确定的 6 大表单直达 URL
        if (!string.IsNullOrEmpty(submissionId))
        {
            string b = baseUrl.TrimEnd('/'), root = $"{b}/{productId}/submissions/{submissionId}";
            var known = new DiscoveryResult
            {
                SubmissionId = submissionId,
                Hrefs = new Dictionary<string, string>
                {
                    ["availability"] = root + "/availability",
                    ["properties"] = root + "/properties",
                    ["ageRatings"] = root + "/ageratings",
                    ["packages"] = root + "/packages",
                    ["listing"] = root + "/managelanguages?producttype=app",
                    ["options"] = root + "/options"
                }
            };
            Console.WriteLine($"[PASS] 已加载已知提审 SubmissionId: {submissionId}");
            return known;
        }

        // 3. 只有在完全没有 submissionId 时，才访问产品总概览去寻找或创建
        string overviewUrl = $"{baseUrl.TrimEnd('/')}/{productId}/overview";
        Console.WriteLine($"[NAV] 未检测到活跃 SubmissionId，导航至产品概览: {overviewUrl}");
        await _waiter.NavigateAsync(overviewUrl, "Product Overview for Discovery");

        DiscoveryResult current = new();
        bool hasStartBtn = false;
        var deadline = DateTime.UtcNow.AddSeconds(20);

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
