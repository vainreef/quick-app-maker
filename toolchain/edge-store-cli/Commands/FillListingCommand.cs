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

        // 1. Resolve submissionId
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

        // 2. Navigate to managelanguages to clean any unwanted languages and save
        string targetGridUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/managelanguages?producttype=app";
        Console.WriteLine($"[NAV] Navigating to manage languages: {targetGridUrl}");
        await waiter.NavigateAsync(targetGridUrl, "Manage Store Listing Languages");
        await Task.Delay(2500);

        var gridManager = new ListingLanguageGridManager(client, input);
        Console.WriteLine("[INFO] Clearing any non-Chinese languages...");
        await gridManager.DeleteUnwantedLanguagesAsync(desired);
        await Task.Delay(4000);

        // 3. Enter the active listing form
        var adapter = new ListingAdapter(client, dom, waiter, native, input);
        Console.WriteLine("[INFO] Entering active Store Listing form...");
        await adapter.EnterLanguageFormAsync(desired, applyLanguageChanges: false);
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

        Console.WriteLine("[INFO] Waiting for listing save and backend synchronization...");
        await Task.Delay(10000);
        await native.AssertNoVisibleErrorsAsync();

        // 4. Navigate strictly back to Overview and verify
        string overviewUrl = $"{baseUrl}/{desired.ProductId}/overview";
        Console.WriteLine($"[INFO] Navigating strictly to Product Overview: {overviewUrl}");
        await waiter.NavigateAsync(overviewUrl, "Product Overview after listing save");
        await Task.Delay(3500);

        var inspector = new PageInspector(client);
        var snapshot = await inspector.CaptureAsync();
        Console.WriteLine("[DOM-EXTRACTED] Post-Listing Overview DOM Snapshot:");
        Console.WriteLine(JsonSerializer.Serialize(snapshot, Program.JsonIndented));

        return 0;
    }
}
