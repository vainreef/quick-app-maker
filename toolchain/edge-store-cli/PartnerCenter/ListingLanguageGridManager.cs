using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class LanguageItem
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool ShouldKeep { get; set; }
}

public class ListingLanguageGridManager
{
    private readonly CdpClient _client;
    private readonly InputDriver _input;

    public ListingLanguageGridManager(CdpClient client, InputDriver input)
    {
        _client = client;
        _input = input;
    }

    public async Task<List<string>> ReadLanguageGridCodesAsync()
    {
        return await _client.EvaluateAsync<List<string>>("""
        (() => Array.from(document.querySelectorAll('a[href*="languagecode="]'))
          .map(a => { const m=(a.href||'').match(/[?&]languagecode=([^&#]+)/i); return m ? decodeURIComponent(m[1]).toLowerCase() : ''; })
          .filter(Boolean).filter((x,i,a)=>a.indexOf(x)===i))()
        """) ?? [];
    }

    public async Task<List<LanguageItem>> DetectLanguagesAsync(DesiredState desired)
    {
        var wanted = desired.Site.SupportedLanguageCodes.Count > 0
            ? desired.Site.SupportedLanguageCodes
            : [desired.Site.LanguageCode];

        var items = await _client.EvaluateAsync<List<LanguageItem>>($$"""
        (() => {
          const wanted = {{JsonSerializer.Serialize(wanted.Select(x => x.ToLowerInvariant()).ToArray())}};
          const actionSlots = Array.from(document.querySelectorAll('[slot^="Action-"]'));
          const results = [];
          for (const slotDiv of actionSlots) {
            const slot = slotDiv.getAttribute('slot') || '';
            const id = slot.replace(/^Action-/, '');
            const nameDiv = document.querySelector('[slot="Name-' + CSS.escape(id) + '"]');
            const a = nameDiv ? nameDiv.querySelector('a') : null;
            if (!a) continue;
            const m = (a.href || '').match(/[?&]languagecode=([^&#]+)/i);
            const code = m ? decodeURIComponent(m[1]).toLowerCase() : '';
            const name = (a.innerText || a.textContent || '').trim();
            const b = slotDiv.querySelector('he-button, button') || slotDiv;
            const action = (b.innerText || b.textContent || '').trim();
            const isChinese = code === "zh-cn" || name === "中文(中国)" || /(?:[?&])languageid=5(?:&|$)/.test(a.href || "") || /(?:[?&])languagecode=zh-cn(?:&|$)/i.test(a.href || "");
            const shouldKeep = wanted.includes(code) || (wanted.includes("zh-cn") && isChinese);
            results.push({ id, code, name, href: a.href || '', action, shouldKeep });
          }
          return results;
        })()
        """) ?? [];

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($"[WORKFLOW STEP 1] DOM 语言检测完成：当前共检测到 {items.Count} 种语言");
        var toKeep = items.Where(x => x.ShouldKeep).ToList();
        var toDelete = items.Where(x => !x.ShouldKeep).ToList();
        Console.WriteLine($"[WORKFLOW STEP 1] 保留目标 ({toKeep.Count}项): {string.Join(", ", toKeep.Select(x => $"{x.Name}({x.Code}, ID:{x.Id})"))}");
        Console.WriteLine($"[WORKFLOW STEP 1] 待删语言 ({toDelete.Count}项)");
        Console.WriteLine("=======================================================\n");

        return items;
    }

    public async Task<int> DeleteUnwantedLanguagesAsync(DesiredState desired)
    {
        var items = await DetectLanguagesAsync(desired);
        var toDelete = items.Where(x => !x.ShouldKeep).ToList();
        if (toDelete.Count == 0)
        {
            Console.WriteLine("[WORKFLOW STEP 2] 没有需要删除的语言，当前语言列表已收敛。");
            return 0;
        }

        Console.WriteLine($"[WORKFLOW STEP 2] 开始批量执行删除 {toDelete.Count} 项语言...");
        int deletedCount = 0;

        for (int i = 0; i < toDelete.Count; i++)
        {
            var item = toDelete[i];
            bool clicked = await _client.EvaluateAsync<bool>($$"""
            (() => {
              const slotDiv = document.querySelector('[slot="Action-{{item.Id}}"]');
              if (!slotDiv) return false;
              const b = slotDiv.querySelector('he-button, button') || slotDiv;
              if (b.shadowRoot) {
                const inner = b.shadowRoot.querySelector('button');
                if (inner) inner.click();
              }
              b.click();
              b.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
              return true;
            })()
            """);

            if (clicked)
            {
                deletedCount++;
                Console.WriteLine($"  [DELETE {deletedCount}/{toDelete.Count}] 成功触发删除: {item.Name} ({item.Code}, ID: {item.Id})");
            }
            await Task.Delay(100);
        }

        Console.WriteLine($"[WORKFLOW STEP 2] 语言删除触发完成，共删除 {deletedCount} 种语言。");
        return deletedCount;
    }
}
