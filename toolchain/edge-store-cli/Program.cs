using System.Text.Json;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.Orchestration;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string action = "run";
        string phase = "all";
        string manifestPath = "";
        string productId = "";
        string stateDir = "";
        bool apply = false;
        bool submit = false;
        bool confirmSubmit = false;
        bool reloadVerify = true;
        bool keepOpen = false;

        string appName = "";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--action":
                case "-action":
                case "-a":
                    if (i + 1 < args.Length) action = args[++i].ToLowerInvariant();
                    break;
                case "--phase":
                case "-phase":
                case "-p":
                    if (i + 1 < args.Length) phase = args[++i];
                    break;
                case "--manifest":
                case "-manifest":
                case "-m":
                    if (i + 1 < args.Length) manifestPath = args[++i];
                    break;
                case "--product-id":
                case "-productid":
                case "-id":
                    if (i + 1 < args.Length) productId = args[++i];
                    break;
                case "--app-name":
                case "-appname":
                case "-name":
                case "-n":
                    if (i + 1 < args.Length) appName = args[++i];
                    break;
                case "--state-dir":
                case "-statedir":
                    if (i + 1 < args.Length) stateDir = args[++i];
                    break;
                case "--apply":
                case "-apply":
                    apply = true;
                    break;
                case "--submit":
                case "-submit":
                    submit = true;
                    break;
                case "--confirm-submit":
                case "-confirmsubmit":
                    confirmSubmit = true;
                    break;
                case "--skip-reload-verify":
                    reloadVerify = false;
                    break;
                case "--keep-open":
                case "-keepopen":
                    keepOpen = true;
                    break;
            }
        }

        string appRoot = AppContext.BaseDirectory;
        string toolRoot = Directory.GetCurrentDirectory();

        string stateRoot = !string.IsNullOrWhiteSpace(stateDir)
            ? Path.GetFullPath(stateDir)
            : Path.Combine(toolRoot, "state");

        // Status and stop do not require a manifest file
        if (action is "status" or "stop")
        {
            var fallbackDesired = new DesiredState();
            var fallbackCheckpoint = new StoreCheckpoint();
            var statusOrchestrator = new StoreOrchestrator(fallbackDesired, stateRoot, fallbackCheckpoint, false, false, false);
            if (action == "stop")
            {
                Console.WriteLine("[INFO] Stopping Edge session...");
                statusOrchestrator.StopSession();
                Console.WriteLine("[PASS] Stopped.");
                return 0;
            }
            return await statusOrchestrator.PrintStatusAsync();
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            manifestPath = Path.Combine(toolRoot, "examples", "store-automation.json");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(appRoot, "examples", "store-automation.json");
            }
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(appRoot, "..", "..", "..", "examples", "store-automation.json");
            }
        }

        if (!File.Exists(manifestPath))
        {
            Console.WriteLine($"[ERROR] Manifest not found: {manifestPath}");
            return 2;
        }

        manifestPath = Path.GetFullPath(manifestPath);
        string baseDir = Path.GetDirectoryName(manifestPath)!;

        DesiredState desired;
        try
        {
            string json = File.ReadAllText(manifestPath);
            desired = JsonSerializer.Deserialize<DesiredState>(json) ?? throw new InvalidOperationException("Failed to deserialize manifest.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Invalid JSON in {manifestPath}: {ex.Message}");
            return 2;
        }

        // Resolve relative paths in assets
        ResolvePaths(desired, baseDir);

        if (!string.IsNullOrWhiteSpace(productId))
        {
            desired.ProductId = productId;
        }

        if (!string.IsNullOrWhiteSpace(appName))
        {
            desired.ProductName = appName;
        }

        // Check if markdown listing file exists and load it
        if (!string.IsNullOrWhiteSpace(desired.ListingMarkdown))
        {
            string mdPath = Path.IsPathRooted(desired.ListingMarkdown)
                ? desired.ListingMarkdown
                : Path.Combine(baseDir, desired.ListingMarkdown);

            if (File.Exists(mdPath))
            {
                ListingMarkdownImporter.Import(desired, mdPath);
            }
            else if (action is "preflight" or "run")
            {
                Console.WriteLine($"[ERROR] listingMarkdown was configured but not found: {mdPath}");
                return 2;
            }
        }

        if (action is "preflight" or "run")
        {
            var validationErrors = DesiredStateValidator.Validate(desired, strict: true);
            if (validationErrors.Count > 0)
            {
                Console.WriteLine("[ERROR] Desired state is incomplete or contradictory:");
                foreach (string error in validationErrors) Console.WriteLine("  - " + error);
                return 2;
            }
        }

        Directory.CreateDirectory(stateRoot);
        var checkpoint = LoadCheckpoint(stateRoot, desired.ProductId);

        var orchestrator = new StoreOrchestrator(desired, stateRoot, checkpoint, apply, submit, confirmSubmit, reloadVerify, keepOpen);

        try
        {
            switch (action)
            {
                case "preflight":
                    Console.WriteLine("[INFO] Running STORE 0 Offline Preflight...");
                    await orchestrator.RunPreflightQualityInspectionAsync();
                    Console.WriteLine("[PASS] Preflight passed successfully.");
                    return 0;

                case "launch":
                    Console.WriteLine("[INFO] Launching Edge isolated session...");
                    await orchestrator.EnsureSignedInAsync();
                    Console.WriteLine("[PASS] Edge session ready.");
                    return 0;

                case "status":
                    return await orchestrator.PrintStatusAsync();

                case "inspect":
                    return await orchestrator.InspectAsync();

                case "verify":
                    return await orchestrator.VerifyAsync();

                case "reserve":
                case "identity":
                    return await HandleReserveOrIdentityAsync(desired, manifestPath, baseDir, stateRoot, action == "reserve", appName);

                case "stop":
                    Console.WriteLine("[INFO] Stopping Edge session...");
                    orchestrator.StopSession();
                    Console.WriteLine("[PASS] Stopped.");
                    return 0;

                case "run":
                    return await orchestrator.RunAsync(phase);

                default:
                    Console.WriteLine($"[ERROR] Unknown action: {action}");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Unhandled exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task<int> HandleReserveOrIdentityAsync(DesiredState desired, string manifestPath, string baseDir, string stateRoot, bool isReserve, string appName)
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
            Console.WriteLine($"[INFO] Creating and reserving new product [{effectiveAppName}] in Partner Center...");
            result = await prodManager.CreateAndReserveProductAsync(desired.Site.BaseUrl, effectiveAppName);
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
        BackfillIdentity(baseDir, manifestPath, desired, result, effectiveAppName);
        return 0;
    }

    private static void BackfillIdentity(string baseDir, string manifestPath, DesiredState desired, ProductIdentityResult result, string appName)
    {
        // 1. Update manifest JSON
        try
        {
            desired.ProductId = result.ProductId;
            desired.ProductName = appName;
            string json = JsonSerializer.Serialize(desired, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);
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
                File.WriteAllText(mf, xml);
                Console.WriteLine($"[PASS] Backfilled credentials into Package.appxmanifest: {mf}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not auto-backfill Package.appxmanifest: {ex.Message}");
        }
    }

    private static StoreCheckpoint LoadCheckpoint(string stateRoot, string productId)
    {
        string path = Path.Combine(stateRoot, "checkpoint.json");
        try
        {
            if (File.Exists(path))
            {
                var existing = JsonSerializer.Deserialize<StoreCheckpoint>(File.ReadAllText(path));
                if (existing != null && string.Equals(existing.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                {
                    if (existing.SchemaVersion < 4)
                    {
                        // Old checkpoints called a phase converged after the adapter
                        // returned, including dry-run and unverified saves. Preserve
                        // only routing data; discard every legacy completion claim.
                        existing.SchemaVersion = 4;
                        existing.PhaseStatuses.Clear();
                        existing.ConvergedPhases.Clear();
                        existing.PhaseEvidence.Clear();
                    }
                    return existing;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Ignoring unreadable checkpoint: {ex.Message}");
        }
        return new StoreCheckpoint { ProductId = productId };
    }

    private static void ResolvePaths(DesiredState state, string baseDir)
    {
        if (state.Assets == null) return;
        state.Assets.Msix = ResolveOne(state.Assets.Msix, baseDir);
        state.Assets.Screenshot = ResolveOne(state.Assets.Screenshot, baseDir);
        state.Assets.Poster = ResolveOne(state.Assets.Poster, baseDir);
        state.Assets.Boxart = ResolveOne(state.Assets.Boxart, baseDir);
        state.Assets.Logo300 = ResolveOne(state.Assets.Logo300, baseDir);
        state.Assets.Logo150 = ResolveOne(state.Assets.Logo150, baseDir);
        state.Assets.Logo71 = ResolveOne(state.Assets.Logo71, baseDir);
        state.Assets.Superhero = ResolveOne(state.Assets.Superhero, baseDir);
    }

    private static string ResolveOne(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

}
