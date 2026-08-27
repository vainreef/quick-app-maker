using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class DiagnoseListingCommand
{
    public static async Task<int> ExecuteAsync(DesiredState desired, string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var dom = new DomDriver(client);
        var input = new InputDriver(client, new AxLocator(client, dom));

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

        // 1. Check managelanguages rows
        string managelangsUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/managelanguages?producttype=app";
        Console.WriteLine($"[NAV] Navigating to: {managelangsUrl}");
        await waiter.NavigateAsync(managelangsUrl, "Manage Store Languages");
        await Task.Delay(2000);

        var languagesInfo = await client.EvaluateAsync<Dictionary<string, object>>("""
        (() => {
          const links = Array.from(document.querySelectorAll('a[href*="listings"]')).map(a => {
            let row = a.parentElement;
            let rowText = '';
            for (let i = 0; i < 6 && row; i++) {
              const t = (row.innerText || '').trim();
              if (t.length > 2 && t.length < 150) {
                rowText = t.replace(/\s+/g, ' ');
                break;
              }
              row = row.parentElement;
            }
            return {
              text: (a.innerText || '').trim(),
              href: a.href,
              rowSummary: rowText
            };
          });
          return {
            languagesCount: links.length,
            languages: links
          };
        })()
        """);

        Console.WriteLine("[INFO] Languages in grid:");
        Console.WriteLine(JsonSerializer.Serialize(languagesInfo, Program.JsonIndented));

        // 2. Check the Chinese listing form directly
        string? zhHref = await client.EvaluateAsync<string?>("""
        (() => {
          const links = Array.from(document.querySelectorAll('a[href*="listings"]'));
          const zh = links.find(a => (a.innerText || '').includes('中文') || (a.href || '').includes('zh-cn') || (a.href || '').includes('languageid=7') || (a.href || '').includes('languageid=5'));
          return zh ? zh.href : null;
        })()
        """);

        if (!string.IsNullOrWhiteSpace(zhHref))
        {
            Console.WriteLine($"[NAV] Navigating to Chinese form: {zhHref}");
            await waiter.NavigateAsync(zhHref, "Chinese Listing Form");
            await Task.Delay(3000);

            var formValidation = await client.EvaluateAsync<Dictionary<string, object>>("""
            (() => {
              const alerts = Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"], .has-error, .field-validation-error, [class*="error"]'))
                .map(e => (e.innerText || '').trim())
                .filter(t => t && t.length > 2);

              const missingAssets = Array.from(document.querySelectorAll('.img-uploader, [class*="upload"]'))
                .map(e => ({ text: (e.innerText || '').replace(/\s+/g, ' ').slice(0, 100) }))
                .slice(0, 10);

              const emptyRequired = Array.from(document.querySelectorAll('input[required], textarea[required], select[required]'))
                .filter(e => !e.value)
                .map(e => e.id || e.name || e.getAttribute('aria-label') || '');

              return {
                url: location.href,
                alerts: alerts,
                emptyRequired: emptyRequired,
                uploadBoxes: missingAssets
              };
            })()
            """);

            Console.WriteLine("[INFO] Chinese Form Validation / Error Status:");
            Console.WriteLine(JsonSerializer.Serialize(formValidation, Program.JsonIndented));
        }

        return 0;
    }
}
