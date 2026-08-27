using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
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
        var gridManager = new ListingLanguageGridManager(client, input);

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

        Console.WriteLine("[INFO] Converging language grid to desired supported languages...");
        int deleted = await gridManager.DeleteUnwantedLanguagesAsync(desired);
        Console.WriteLine($"[PASS] Languages deleted: {deleted}");

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
            await Task.Delay(3000);
        }
        else
        {
            Console.WriteLine("[WARN] Save button on language grid not found or disabled.");
        }

        return 0;
    }
}
