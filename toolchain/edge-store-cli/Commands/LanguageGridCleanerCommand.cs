using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class LanguageGridCleanerCommand
{
    public static async Task<int> ExecuteAsync(DesiredState desired, string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));

        string currentUrl = await client.EvaluateAsync<string>("location.href") ?? "";
        string baseUrl = desired.Site.BaseUrl.TrimEnd('/');
        string targetGridUrl = $"{baseUrl}/{desired.ProductId}/submissions/{desired.SubmissionId}/managelanguages?producttype=app";

        if (!currentUrl.Contains("managelanguages", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[NAV] Navigating to manage languages: {targetGridUrl}");
            await waiter.NavigateAsync(targetGridUrl, "Manage Store Listing Languages");
            await Task.Delay(2000);
        }

        Console.WriteLine("[INFO] Deleting non-target languages from language grid...");

        int deletedCount = await client.EvaluateAsync<int>("""
        (() => {
          let count = 0;
          const links = Array.from(document.querySelectorAll('a[href*="languagecode="]'));
          for (const a of links) {
            const text = (a.innerText || '').trim();
            if (text.includes('中文') || text.includes('Chinese') || (a.href || '').includes('zh-cn')) {
              continue;
            }
            let container = a.parentElement;
            for (let i = 0; i < 5 && container; i++) {
              const delBtn = Array.from(container.querySelectorAll('he-button, button')).find(b => (b.innerText || '').trim() === '删除');
              if (delBtn) {
                delBtn.click();
                if (delBtn.shadowRoot) {
                  const inner = delBtn.shadowRoot.querySelector('button');
                  if (inner) inner.click();
                }
                delBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
                count++;
                break;
              }
              container = container.parentElement;
            }
          }
          return count;
        })()
        """);

        Console.WriteLine($"[INFO] Triggered delete on {deletedCount} non-target languages.");
        await Task.Delay(2500);

        // Click Save on the language grid page
        Console.WriteLine("[INFO] Clicking Save on manage languages page...");
        bool saveClicked = await client.EvaluateAsync<bool>("""
        (() => {
          const saveBtn = Array.from(document.querySelectorAll('he-button, button, input[type="button"], input[type="submit"]')).find(e => {
            const t = (e.innerText || e.value || e.getAttribute('aria-label') || '').trim();
            const r = e.getBoundingClientRect();
            return /^(保存|Save|保存并继续)$/i.test(t) && r.width > 0 && r.height > 0 && !e.disabled;
          });
          if (saveBtn) {
            saveBtn.click();
            if (saveBtn.shadowRoot) {
              const inner = saveBtn.shadowRoot.querySelector('button');
              if (inner) inner.click();
            }
            saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
            return true;
          }
          return false;
        })()
        """);

        if (saveClicked)
        {
            Console.WriteLine("[PASS] Clicked Save on language grid.");
            await Task.Delay(4000);
        }
        else
        {
            Console.WriteLine("[WARN] Save button on language grid not found or disabled.");
        }

        return 0;
    }
}
