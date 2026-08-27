using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class InspectOptionsCommand
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
        var native = new NativeFormAdapter(client, input);

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

        if (string.IsNullOrWhiteSpace(submissionId))
        {
            var discovery = new SubmissionDiscovery(client, waiter, native);
            var discResult = await discovery.DiscoverAsync(baseUrl, desired.ProductId, autoCreateIfMissing: false);
            submissionId = discResult.SubmissionId;
        }

        string optionsUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/options";
        Console.WriteLine($"[NAV] Navigating directly to Submission Options page: {optionsUrl}");
        await waiter.NavigateAsync(optionsUrl, "Submission Options Page");
        await waiter.RequireAsync(async () =>
        {
            return await client.EvaluateAsync<bool>("document.querySelector('textarea.text-area-width, [dcl10n=\"optionsSave\"], button[data-l10n-key=\"optionsSave\"]') !== null");
        }, TimeSpan.FromSeconds(30), "Wait for options form controls");
        await Task.Delay(1500);

        var report = await client.EvaluateAsync<Dictionary<string, object>>("""
        (() => {
          const radios = Array.from(document.querySelectorAll('input[type="radio"]')).map(r => {
            const label = r.closest('label') ? r.closest('label').innerText.trim() : '';
            return {
              id: r.id || '',
              name: r.name || '',
              checked: r.checked,
              value: r.value || '',
              label: label.replace(/\s+/g, ' ')
            };
          });

          const ta = document.querySelector('textarea.text-area-width, section textarea');
          const isRestrictedSection = !!document.querySelector('h2[data-l10n-key="resCapSectionHeader"], h2[dcl10n="resCapSectionHeader"]');
          const saveBtn = document.querySelector('button[data-l10n-key="optionsSave"], button[dcl10n="optionsSave"]');

          return {
            url: location.href,
            hasRestrictedCapabilitiesSection: isRestrictedSection,
            requiresRunFullTrust: isRestrictedSection,
            runFullTrustCurrentValue: ta ? ta.value : '',
            runFullTrustPlaceholderOrLabel: '为何需要使用 runFullTrust 功能，如何在产品中使用？*',
            publishOptions: radios,
            hasSaveButton: !!saveBtn,
            saveButtonText: saveBtn ? (saveBtn.innerText || '').trim() : ''
          };
        })()
        """);

        Console.WriteLine("\n=======================================================");
        Console.WriteLine("[INFO] 提交选项 (Submission Options) 现场 DOM 精准诊断报告：");
        Console.WriteLine(JsonSerializer.Serialize(report, Program.JsonIndented));
        Console.WriteLine("=======================================================\n");

        return 0;
    }
}
