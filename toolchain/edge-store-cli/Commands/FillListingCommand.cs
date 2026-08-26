using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class FillListingCommand
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

        // 1. Resolve submissionId if empty
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
            Console.WriteLine("[INFO] Discovering submissionId from Overview page...");
            var discovery = new SubmissionDiscovery(client, waiter, native);
            var discResult = await discovery.DiscoverAsync(baseUrl, desired.ProductId, autoCreateIfMissing: false);
            submissionId = discResult.SubmissionId;
        }

        if (string.IsNullOrWhiteSpace(submissionId))
        {
            throw new InvalidOperationException("Could not resolve active Submission ID for Store Listing.");
        }

        // 2. Discover exact listing URL for Chinese or target language
        string targetLanguageCode = !string.IsNullOrWhiteSpace(desired.Site.LanguageCode) ? desired.Site.LanguageCode : "zh-cn";
        string listingUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/listings?languageid={desired.Site.LanguageId}&languagecode={targetLanguageCode}";

        Console.WriteLine($"[NAV] Navigating directly to Store Listing form: {listingUrl}");
        await waiter.NavigateAsync(listingUrl, "Store Listing Form");
        await Task.Delay(3000);

        var adapter = new ListingAdapter(client, dom, waiter, native, input);
        Console.WriteLine("[INFO] Observing listing form state...");
        var observed = await adapter.ObserveAsync();
        var plan = adapter.PlanDiff(desired, observed);

        Console.WriteLine($"[INFO] Listing reconcile plan has {plan.Actions.Count} action(s).");
        foreach (var a in plan.Actions)
        {
            Console.WriteLine($"  - {a.Field}: [{a.CurrentValue}] -> [{a.DesiredValue}] ({a.Description})");
        }

        Console.WriteLine("[INFO] Applying listing changes and uploading assets...");
        await adapter.ApplyChangesAsync(plan, desired);

        await Task.Delay(4000);

        // 3. Navigate strictly back to Overview
        string overviewUrl = $"{baseUrl}/{desired.ProductId}/overview";
        Console.WriteLine($"[INFO] Navigating strictly to Product Overview: {overviewUrl}");
        await waiter.NavigateAsync(overviewUrl, "Product Overview after listing save");
        await Task.Delay(2000);

        var inspector = new PageInspector(client);
        var snapshot = await inspector.CaptureAsync();
        Console.WriteLine("[DOM-EXTRACTED] Post-Listing Overview DOM Snapshot:");
        Console.WriteLine(JsonSerializer.Serialize(snapshot, Program.JsonIndented));

        return 0;
    }
}
