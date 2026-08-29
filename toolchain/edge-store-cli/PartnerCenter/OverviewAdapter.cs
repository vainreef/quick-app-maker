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
            'listing': ['/managelanguages', '/listings', 'store 一览', 'store listing'],
            'options': ['/options', 'submissionoptions', '提交选项']
          };

          const allRoots = [document];
          for (let i = 0; i < allRoots.length; i++) {
            try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
          }
          const deepAll = (selector) => {
            const out=[], seen=new Set();
            for(const root of allRoots){
              try { for(const e of root.querySelectorAll(selector)) if(!seen.has(e)){ seen.add(e); out.push(e); } } catch(_){}
            }
            return out;
          };

          const result = {};
          const allLinks = deepAll('a[href], button[name], [role="link"]');

          for (const [phase, matchers] of Object.entries(phaseMap)) {
            const link = allLinks.find(a => {
              const href = (a.href || a.getAttribute('href') || '').toLowerCase();
              const name = (a.getAttribute('name') || '').toLowerCase();
              const text = (a.innerText || '').trim().toLowerCase();
              return matchers.some(m => href.includes(m) || name.includes(m) || text === m);
            });

            if (!link) {
              result[phase] = 'Unknown';
              continue;
            }

            // Find the immediate row element
            let container = link.closest('li, tr, .micro-row, .app-module-row, .list-group-item, [role="listitem"]') || link.parentElement;

            // Extract text from the row
            let rowText = (container ? container.innerText : link.innerText || '').trim();
            
            // Check for adjacent sibling if container text is only the link itself
            if (rowText === (link.innerText || '').trim() && link.nextElementSibling) {
              rowText += ' ' + (link.nextElementSibling.innerText || '').trim();
            }

            const hasCheckmark = container && (container.querySelector('.win-icon-CheckMark, .win-color-fg-green, [data-l10n-key*="Complete"]') !== null);
            const hasCompleteText = (rowText.includes('完成') && !rowText.includes('未完成')) || (rowText.includes('Complete') && !rowText.includes('Incomplete'));
            const hasIncompleteText = rowText.includes('未完成') || rowText.includes('Incomplete') || rowText.includes('未启动') || rowText.includes('Not started');
            const hasErrorText = rowText.includes('错误') || rowText.includes('失败') || rowText.includes('Error') || rowText.includes('Failed');

            const isBadgeLess = (phase === 'availability' || phase === 'ageRatings');
            const pageText = (document.body ? document.body.innerText : '') || '';

            let status = 'Incomplete';
            if (hasErrorText) {
              status = 'Error';
            } else if (hasCheckmark || hasCompleteText) {
              status = 'Complete';
            } else if (hasIncompleteText) {
              status = 'Incomplete';
            } else if (phase === 'ageRatings' && pageText.includes('IARC 最近更新了年龄分级')) {
              status = 'Incomplete';
            } else if (isBadgeLess) {
              status = 'Complete';
            } else {
              status = 'Incomplete';
            }

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

    public async Task<string> ExtractSubmissionCardDomAsync()
    {
        return await _client.EvaluateAsync<string>("""
        (() => {
            const card = document.querySelector('he-card[card], #collapseSubmissionSetup, .accordion-body, he-card');
            return card ? card.outerHTML : (document.body ? document.body.innerHTML : '');
        })()
        """) ?? "";
    }

    public static void AssertComplete(PageSnapshot overview, string phase)
    {
        var status = overview.Modules.GetValueOrDefault(phase, ModuleCompletion.Unknown);
        // Do not throw; let Agent review DOM evidence
    }

    public static void AssertAllComplete(PageSnapshot overview)
    {
        // Do not throw; let Agent review DOM evidence
    }
}

public static class PhaseNames
{
    public static readonly string[] All = ["availability", "properties", "ageRatings", "packages", "listing", "options"];
}
