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

        string currentUrl = await client.EvaluateAsync<string>("location.href") ?? "";
        Console.WriteLine($"[LANG-CLEANER] 当前页面: {currentUrl}");

        if (!currentUrl.Contains("managelanguages", StringComparison.OrdinalIgnoreCase))
        {
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
            Console.WriteLine($"[NAV] Navigating to manage languages: {targetGridUrl}");
            await waiter.NavigateAsync(targetGridUrl, "Manage Store Listing Languages");
            await Task.Delay(2000);
        }

        Console.WriteLine("[INFO] 正在执行自收敛多轮删除（彻底清除所有非中文语言）...");
        int deleted = await gridManager.DeleteUnwantedLanguagesAsync(desired);
        Console.WriteLine($"[PASS] 成功触发多轮删除点击: {deleted} 次");

        Console.WriteLine("[INFO] 正在点击页面底部的【保存】按钮...");
        bool saved = await gridManager.SaveLanguagesAsync();
        if (saved)
        {
            Console.WriteLine("[PASS] 语言网格保存成功！");
        }
        else
        {
            Console.WriteLine("[WARN] 保存按钮未触发或不可用。");
        }

        return 0;
    }
}

