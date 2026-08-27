using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public sealed class OverviewAdapter
{
    private readonly CdpClient _client;
    private readonly PageInspector _inspector;

    public OverviewAdapter(CdpClient client, PageInspector inspector)
    {
        _client = client;
        _inspector = inspector;
    }

    public async Task<PageSnapshot> ObserveAsync()
    {
        var page = await _inspector.WaitForAsync(
            [PartnerPageKind.SubmissionOverview, PartnerPageKind.ProductOverview],
            TimeSpan.FromSeconds(90),
            "wait for submission overview");

        // Wait for the six submission module links to render (SPA lazy render).
        var linksDeadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < linksDeadline)
        {
            bool ready = await _client.EvaluateAsync<bool>("""
            (() => {
                const allRoots=[document];
                for(let i=0;i<allRoots.length;i++){ try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){} }
                const deepAll = (selector) => { const out=[], seen=new Set(); for(const root of allRoots){ try{ for(const e of root.querySelectorAll(selector)) if(!seen.has(e)){ seen.add(e); out.push(e); } }catch(_){} } return out; };
                return deepAll('a[href*="/submissions/"]').length >= 6;
            })()
            """);
            if (ready) break;
            await Task.Delay(1000);
        }

        var raw = await _client.EvaluateAsync<Dictionary<string, string>>("""
        (() => {
          const phaseMap = {
            'availability': ['/availability', 'pricingandavailability', '定价和可用性'],
            'properties': ['/properties', '属性'],
            'ageRatings': ['/ageratings', '年龄分级'],
            'packages': ['/packages', '程序包', '包'],
            'listing': ['/listings', '/managelanguages', 'store 一览', 'store listing'],
            'options': ['/options', 'submissionoptions', '提交选项']
          };

          const result = {};
          const fullText = (document.body ? document.body.innerText : '').toLowerCase();

          for (const [phase, matchers] of Object.entries(phaseMap)) {
            const link = Array.from(document.querySelectorAll('a, button, he-button, [role="link"], div')).find(a => {
              const href = (a.href || a.getAttribute('href') || '').toLowerCase();
              const name = (a.getAttribute('name') || '').toLowerCase();
              const text = (a.innerText || '').trim().toLowerCase();
              return matchers.some(m => href.includes(m) || name.includes(m) || text === m);
            });

            if (!link) {
              if (matchers.some(m => fullText.includes(m))) {
                result[phase] = 'Complete';
              } else {
                result[phase] = 'Unknown';
              }
              continue;
            }

            let container = link;
            for (let i = 0; i < 5 && container.parentElement; i++) {
              if (container.parentElement.tagName === 'BODY') break;
              container = container.parentElement;
            }

            const text = (container.innerText || '').trim();
            const html = (container.innerHTML || '').slice(0, 8000);
            const evidence = (text + ' ' + html).toLowerCase();

            let status = 'Complete';
            if (/\b(error|failed|invalid)\b|错误|失败/.test(evidence)) status = 'Error';
            else if (/未启动|not started|未完成|incomplete|not complete/.test(evidence)) status = 'Incomplete';
            else if (/uploading|processing|正在处理|正在上传|验证中/.test(evidence)) status = 'Processing';

            result[phase] = status;
          }
          return result;
        })()
        """) ?? [];

        foreach (var phase in PhaseNames.All)
        {
            page.Modules[phase] = raw.TryGetValue(phase, out var value)
                && Enum.TryParse<ModuleCompletion>(value, true, out var status)
                    ? status
                    : ModuleCompletion.Unknown;
        }
        return page;
    }

    public static void AssertComplete(PageSnapshot overview, string phase)
    {
        var status = overview.Modules.GetValueOrDefault(phase, ModuleCompletion.Unknown);
        if (status != ModuleCompletion.Complete)
            throw new InvalidOperationException($"Overview verification rejected phase [{phase}]: module status is {status}. Form equality is only intermediate evidence.");
    }

    public static void AssertAllComplete(PageSnapshot overview)
    {
        var bad = PhaseNames.All.Where(p => overview.Modules.GetValueOrDefault(p) != ModuleCompletion.Complete).ToList();
        if (bad.Count > 0)
            throw new InvalidOperationException("Submission overview is not complete: " + string.Join(", ", bad.Select(p => $"{p}={overview.Modules.GetValueOrDefault(p)}")));
    }
}

public static class PhaseNames
{
    public static readonly string[] All = ["availability", "properties", "ageRatings", "packages", "listing", "options"];
}
