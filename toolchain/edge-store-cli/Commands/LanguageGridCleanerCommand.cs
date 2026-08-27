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

        string baseUrl = desired.Site.BaseUrl.TrimEnd('/');
        string submissionId = desired.SubmissionId;
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            string cpPath = Path.Combine(stateRoot, "checkpoint.json");
            if (File.Exists(cpPath))
            {
                try
                {
                    var cp = JsonSerializer.Deserialize<StoreCheckpoint>(File.ReadAllText(cpPath));
                    if (!string.IsNullOrWhiteSpace(cp?.SubmissionId)) submissionId = cp.SubmissionId;
                }
                catch { }
            }
        }

        string targetGridUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/managelanguages?producttype=app";
        string currentUrl = await client.EvaluateAsync<string>("location.href") ?? "";

        if (!currentUrl.Contains("managelanguages", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[NAV] Navigating to manage languages: {targetGridUrl}");
            await waiter.NavigateAsync(targetGridUrl, "Manage Store Listing Languages");
            await Task.Delay(2000);
        }

        Console.WriteLine("[INFO] Iteratively deleting all non-Chinese languages from language grid...");

        int deletedTotal = await client.EvaluateAsync<int>("""
        (async () => {
          let deletedCount = 0;
          for (let pass = 0; pass < 150; pass++) {
            const links = Array.from(document.querySelectorAll('a[href*="languagecode="], a[href*="listings"]'));
            let targetBtn = null;
            let targetName = '';

            for (const a of links) {
              const text = (a.innerText || '').trim();
              if (text === '中文(中国)' || (a.href || '').includes('languageid=5') || (a.href || '').includes('languagecode=zh-cn')) {
                continue; // Keep Chinese (China)
              }
              let container = a.parentElement;
              for (let i = 0; i < 5 && container; i++) {
                const btn = Array.from(container.querySelectorAll('he-button, button')).find(b => (b.innerText || '').trim() === '删除');
                if (btn) {
                  targetBtn = btn;
                  targetName = text;
                  break;
                }
                container = container.parentElement;
              }
              if (targetBtn) break;
            }

            if (!targetBtn) break;

            if (targetBtn.shadowRoot) {
              const inner = targetBtn.shadowRoot.querySelector('button');
              if (inner) inner.click();
            }
            targetBtn.click();
            targetBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
            deletedCount++;
            await new Promise(r => setTimeout(r, 80));
          }
          return deletedCount;
        })()
        """);

        Console.WriteLine($"[PASS] Deleted {deletedTotal} non-Chinese languages.");
        await Task.Delay(2000);

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
            if (saveBtn.shadowRoot) {
              const inner = saveBtn.shadowRoot.querySelector('button');
              if (inner) inner.click();
            }
            saveBtn.click();
            saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
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
