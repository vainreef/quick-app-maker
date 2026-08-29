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
        var items = await _client.EvaluateAsync<List<LanguageItem>>("""
        (() => {
          const nameSlots = Array.from(document.querySelectorAll('div[slot^="Name-"]'));
          const results = [];

          for (const nameSlot of nameSlots) {
            const slotAttr = nameSlot.getAttribute('slot') || '';
            const id = slotAttr.replace('Name-', '').trim();
            const name = (nameSlot.innerText || nameSlot.textContent || '').trim();

            const actionSlot = document.querySelector(`div[slot="Action-${id}"]`);
            const actionText = actionSlot ? (actionSlot.innerText || actionSlot.textContent || '').trim() : '';

            const isChinese = id === '5' || name.includes('中文(中国)') || name.includes('中文（中国）');
            results.push({
              id: id,
              code: isChinese ? 'zh-cn' : 'other',
              name: name,
              href: '',
              action: actionText,
              shouldKeep: isChinese
            });
          }
          return results;
        })()
        """) ?? [];

        Console.WriteLine("\n=======================================================");
        Console.WriteLine($"[WORKFLOW STEP 1] DOM 语言检测完成：当前共检测到 {items.Count} 种语言槽位");
        var toKeep = items.Where(x => x.ShouldKeep).ToList();
        var toDelete = items.Where(x => !x.ShouldKeep).ToList();
        Console.WriteLine($"[WORKFLOW STEP 1] 保留目标 ({toKeep.Count}项): {string.Join(", ", toKeep.Select(x => x.Name))}");
        Console.WriteLine($"[WORKFLOW STEP 1] 非保留语言 ({toDelete.Count}项)");
        Console.WriteLine("=======================================================\n");

        return items;
    }

    public async Task<int> DeleteUnwantedLanguagesAsync(DesiredState desired)
    {
        Console.WriteLine("[WORKFLOW STEP 2] 正在执行自收敛删除循环（彻底清空非中文语言）...");
        int deleted = await _client.EvaluateAsync<int>("""
        (async () => {
          let count = 0;
          for (let round = 0; round < 25; round++) {
            const remainingSpans = Array.from(document.querySelectorAll('[data-l10n-key="appsubmission_manage_languages_remove"], [data-l10n-key*="remove"]')).filter(span => {
              const slot = span.closest('[slot^="Action-"]');
              const id = slot ? (slot.getAttribute('slot') || '').replace('Action-', '').trim() : '';
              return id && id !== '5'; // 坚决保留中文(中国) (ID=5)
            });

            if (remainingSpans.length === 0) break;

            for (const span of remainingSpans) {
              span.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(r => setTimeout(r, 40));

              const heBtn = span.closest('he-button, button');
              if (heBtn) {
                if (heBtn.shadowRoot) {
                  const inner = heBtn.shadowRoot.querySelector('button');
                  if (inner) inner.click();
                }
                heBtn.click();
                heBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
              }
              span.click();
              span.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
              count++;
            }

            window.scrollTo(0, document.body.scrollHeight);
            await new Promise(r => setTimeout(r, 500));
          }
          return count;
        })()
        """);

        Console.WriteLine($"[WORKFLOW STEP 2] 自收敛删除执行完毕，累计处理 {deleted} 次删除点击。");
        return deleted;
    }

    public async Task<bool> SaveLanguagesAsync()
    {
        Console.WriteLine("[WORKFLOW STEP 3] 保存管理语言页面更改...");
        var saveDiagnostics = await _client.EvaluateAsync<string>("""
        (() => {
          window.scrollTo(0, document.body.scrollHeight);
          const allRoots = [document];
          for (let i = 0; i < allRoots.length; i++) {
            try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
          }

          const foundButtons = [];
          let clicked = false;

          for (const r of allRoots) {
            const btns = Array.from(r.querySelectorAll('he-button, button, input[type="button"], input[type="submit"], a[role="button"]'));
            for (const b of btns) {
              const t = (b.innerText || b.value || b.getAttribute('aria-label') || '').trim();
              const key = (b.getAttribute('data-l10n-key') || '').toLowerCase();
              const rct = b.getBoundingClientRect();
              const isSave = t.includes('保存') || t.toLowerCase().includes('save') || key.includes('save');
              const isDraft = t.includes('草稿') || key.includes('draft');

              foundButtons.push({
                text: t,
                key: key,
                tag: b.tagName,
                disabled: b.disabled || b.getAttribute('disabled') !== null,
                isSave: isSave,
                isDraft: isDraft,
                width: rct.width,
                height: rct.height
              });

              if (isSave && !isDraft && !clicked) {
                b.scrollIntoView({ block: 'center', behavior: 'instant' });
                if (b.shadowRoot) {
                  const inner = b.shadowRoot.querySelector('button');
                  if (inner) inner.click();
                }
                b.click();
                b.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
                clicked = true;
              }
            }
          }

          return JSON.stringify({ clicked, foundButtons }, null, 2);
        })()
        """);

        Console.WriteLine($"[DEBUG-SAVE] 底部按钮探测与保存结果:\n{saveDiagnostics}");
        await Task.Delay(3000);
        return saveDiagnostics.Contains("\"clicked\": true");
    }
}
