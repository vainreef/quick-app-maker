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

        string zhUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/listings?languageid=5&languagecode=zh-cn";
        Console.WriteLine($"[NAV] Navigating directly to Chinese Listing form: {zhUrl}");
        await waiter.NavigateAsync(zhUrl, "Chinese Listing Form");
        await waiter.RequireAsync(async () =>
        {
            return await client.EvaluateAsync<bool>("document.querySelector('#description-required, #shortDescription') !== null");
        }, TimeSpan.FromSeconds(30), "Wait for listing form elements");
        await Task.Delay(2000);

        var report = await client.EvaluateAsync<Dictionary<string, object>>("""
        (() => {
          const descEl = document.querySelector('#description-required');
          const shortDescEl = document.querySelector('#shortDescription');
          const features = Array.from(document.querySelectorAll('input[id^="feature-"]')).map(x => x.value.trim()).filter(Boolean);
          const keywords = Array.from(document.querySelectorAll('#search-terms he-option, he-select[multiple] he-option'))
            .filter(e => (e.getAttribute('slot') || '').startsWith('selected-') || e.getAttribute('role') === 'listitem')
            .map(e => (e.innerText || e.getAttribute('value') || '').trim())
            .filter(Boolean);

          const allInputs = Array.from(document.querySelectorAll('input, textarea')).map(e => ({
            id: e.id || '',
            name: e.name || '',
            type: e.type || '',
            value: (e.value || '').trim()
          })).filter(x => x.id || x.name);

          const images = Array.from(document.querySelectorAll('img')).map(img => ({
            src: img.src ? (img.src.startsWith('data:') ? 'data:image...' : img.src) : '',
            alt: img.alt || '',
            width: img.naturalWidth || img.width,
            height: img.naturalHeight || img.height,
            visible: img.getBoundingClientRect().width > 0
          }));

          const alerts = Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"], .validation-error, .field-validation-error'))
            .map(e => (e.innerText || '').trim())
            .filter(Boolean);

          const text = (document.body.innerText || '');
          const screenshotCountMatch = text.match(/桌面\s*\((\d+)\)/);
          const screenshotCount = screenshotCountMatch ? parseInt(screenshotCountMatch[1], 10) : 0;

          return {
            url: location.href,
            description: descEl ? descEl.value : null,
            descriptionLength: descEl ? descEl.value.length : 0,
            shortDescription: shortDescEl ? shortDescEl.value : null,
            featuresCount: features.length,
            features: features,
            keywordsCount: keywords.length,
            keywords: keywords,
            screenshotCount: screenshotCount,
            totalImagesOnPage: images.length,
            alerts: alerts,
            inputs: allInputs
          };
        })()
        """);

        Console.WriteLine("\n=======================================================");
        Console.WriteLine("[INFO] Store 一览【中文(中国)】DOM 现场检测报告：");
        Console.WriteLine(JsonSerializer.Serialize(report, Program.JsonIndented));
        Console.WriteLine("=======================================================\n");

        return 0;
    }
}
