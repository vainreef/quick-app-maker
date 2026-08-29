using System.Text.Json;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class ReserveCommand
{
    public static async Task<int> ExecuteAsync(DesiredState desired, string manifestPath, string baseDir, string stateRoot, bool isReserve, string appName)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();

        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var dom = new DomDriver(client);
        var ax = new AxLocator(client, dom);
        var input = new InputDriver(client, ax);
        var native = new NativeFormAdapter(client, input);
        var prodManager = new ProductManager(client, waiter, native);

        string effectiveAppName = !string.IsNullOrWhiteSpace(appName) ? appName : desired.ProductName;
        if (string.IsNullOrWhiteSpace(effectiveAppName))
        {
            throw new InvalidOperationException("Product name must be provided via --app-name or in manifest productName.");
        }

        ProductIdentityResult result;
        if (isReserve)
        {
            Console.WriteLine($"[INFO] 正在启动用户交互式名称预留监听流程...");
            result = await prodManager.UserAssistedReserveProductAsync(desired.Site.BaseUrl, effectiveAppName);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(desired.ProductId) || desired.ProductId == "PENDING")
            {
                throw new InvalidOperationException("ProductId must be specified for identity scraping action.");
            }
            Console.WriteLine($"[INFO] Scraping Identity for product [{desired.ProductId}]...");
            result = await prodManager.ScrapeIdentityAsync(desired.Site.BaseUrl, desired.ProductId);
        }

        // Backfill into Package.appxmanifest and manifest JSON
        string finalName = !string.IsNullOrWhiteSpace(result.ProductName) ? result.ProductName : effectiveAppName;
        BackfillIdentity(baseDir, manifestPath, desired, result, finalName);
        return 0;
    }

    private static void BackfillIdentity(string baseDir, string manifestPath, DesiredState desired, ProductIdentityResult result, string appName)
    {
        // 1. Update manifest JSON
        try
        {
            desired.ProductId = result.ProductId;
            desired.ProductName = appName;
            string json = JsonSerializer.Serialize(desired, Program.JsonIndented);
            File.WriteAllText(manifestPath, json, new System.Text.UTF8Encoding(false));
            Console.WriteLine($"[PASS] Updated store manifest: {manifestPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to write manifest JSON: {ex.Message}");
        }

        // 2. Search and update Package.appxmanifest
        try
        {
            var manifestFiles = Directory.GetFiles(baseDir, "Package.appxmanifest", SearchOption.AllDirectories)
                .Concat(Directory.Exists(Path.Combine(baseDir, "..")) ? Directory.GetFiles(Path.Combine(baseDir, ".."), "Package.appxmanifest", SearchOption.AllDirectories) : [])
                .Distinct()
                .ToList();

            foreach (var mf in manifestFiles)
            {
                string xml = File.ReadAllText(mf);
                // Update Identity Name and Publisher
                xml = Regex.Replace(xml, @"<Identity\s+Name=""[^""]*""\s+Publisher=""[^""]*""",
                    $"<Identity Name=\"{result.IdentityName}\" Publisher=\"{result.Publisher}\"");
                // Update DisplayName
                xml = Regex.Replace(xml, @"<DisplayName>[^<]*</DisplayName>",
                    $"<DisplayName>{appName}</DisplayName>");
                xml = Regex.Replace(xml, @"DisplayName=""[^""]*""",
                    $"DisplayName=\"{appName}\"");
                // Update PublisherDisplayName
                if (!string.IsNullOrWhiteSpace(result.PublisherDisplayName))
                {
                    xml = Regex.Replace(xml, @"<PublisherDisplayName>[^<]*</PublisherDisplayName>",
                        $"<PublisherDisplayName>{result.PublisherDisplayName}</PublisherDisplayName>");
                }
                File.WriteAllText(mf, xml, new System.Text.UTF8Encoding(false));
                Console.WriteLine($"[PASS] Backfilled credentials into Package.appxmanifest: {mf}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not auto-backfill Package.appxmanifest: {ex.Message}");
        }
    }
}
