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
                return deepAll('a[href*="/submissions/"]').length >= 2;
            })()
            """);
            if (ready) break;
            await Task.Delay(1000);
        }

        var raw = await _client.EvaluateAsync<Dictionary<string, string>>("""
        (() => {
          const phaseFor = href => {
            if (/\/availability(?:[/?#]|$)/i.test(href)) return 'availability';
            if (/\/properties(?:[/?#]|$)/i.test(href)) return 'properties';
            if (/\/ageratings(?:[/?#]|$)/i.test(href)) return 'ageRatings';
            if (/\/packages(?:[/?#]|$)/i.test(href)) return 'packages';
            if (/\/listings|\/managelanguages/i.test(href)) return 'listing';
            if (/\/options(?:[/?#]|$)/i.test(href)) return 'options';
            return '';
          };
          const links = Array.from(document.querySelectorAll('a[href*="/submissions/"]'));
          const result = {};
          for (const a of links) {
            const phase = phaseFor(a.href || '');
            if (!phase || result[phase]) continue;
            let row = a;
            for (let i=0; i<8 && row.parentElement; i++) {
              const next = row.parentElement;
              const moduleLinks = Array.from(next.querySelectorAll('a[href*="/submissions/"]')).filter(x => phaseFor(x.href||''));
              if (moduleLinks.length > 1) break;
              row = next;
            }
            const text = (row.innerText || a.parentElement?.innerText || a.innerText || '').trim();
            const html = (row.innerHTML || '').slice(0,12000);
            const attrs = Array.from(row.querySelectorAll('[aria-label],[title],[alt],[name],he-icon,[class*="status"],[class*="icon"]'))
              .map(e => [e.getAttribute('aria-label'),e.getAttribute('title'),e.getAttribute('alt'),e.getAttribute('name'),e.className].filter(Boolean).join(' ')).join(' ');
            const evidence = (text + ' ' + attrs + ' ' + html).toLowerCase();
            let status = 'Unknown';
            if (/\b(error|failed|invalid)\b|错误|失败/.test(evidence)) status='Error';
            else if (/未启动|not started|未完成|incomplete|not complete/.test(evidence)) status='Incomplete';
            else if (/uploading|processing|正在处理|正在上传|验证中/.test(evidence)) status='Processing';
            else status='Complete'; // current UI: a module with no "not started" badge is accepted/complete
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
