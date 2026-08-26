using System.Text.Json;
using Vainreef.EdgeStore.Commands;
using Vainreef.EdgeStore.Orchestration;
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
        string appName = "";
        bool apply = false;
        bool submit = false;
        bool confirmSubmit = false;
        bool reloadVerify = true;
        bool keepOpen = false;

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
                case "-s":
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
                case "--reload-verify":
                case "-reloadverify":
                    reloadVerify = true;
                    break;
                case "--skip-reload-verify":
                case "-skipreloadverify":
                    reloadVerify = false;
                    break;
                case "--keep-open":
                case "-keepopen":
                    keepOpen = true;
                    break;
            }
        }

        string baseDir = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        }

        string stateRoot = !string.IsNullOrWhiteSpace(stateDir)
            ? Path.GetFullPath(stateDir)
            : Path.Combine(baseDir, ".cache", "edge-store-state");

        var desired = new DesiredState();
        if (!string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
        {
            string manifestText = File.ReadAllText(manifestPath);
            desired = JsonSerializer.Deserialize<DesiredState>(manifestText) ?? new DesiredState();
            ResolvePaths(desired, baseDir);

            if (!string.IsNullOrWhiteSpace(desired.ListingMarkdown))
            {
                string mdPath = Path.IsPathRooted(desired.ListingMarkdown)
                    ? desired.ListingMarkdown
                    : Path.GetFullPath(Path.Combine(baseDir, desired.ListingMarkdown));
                if (File.Exists(mdPath))
                {
                    ListingMarkdownImporter.Import(desired, mdPath);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(productId)) desired.ProductId = productId;

        if (action == "preflight" || action == "run" || action == "step")
        {
            var validationErrors = DesiredStateValidator.Validate(desired, strict: false);
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

                case "step":
                    if (string.IsNullOrWhiteSpace(phase) || phase == "all")
                    {
                        Console.WriteLine("[ERROR] -Action step requires a specific -Phase <availability|properties|ageRatings|packages|listing|options>.");
                        return 1;
                    }
                    return await orchestrator.RunSingleStepAsync(phase);

                case "discover":
                    return await orchestrator.DiscoverAndSnapshotAsync();

                case "inspect":
                    return await orchestrator.InspectAsync();

                case "dumpdom":
                    return await DomDumpCommand.ExecuteAsync(stateRoot, baseDir);

                case "cleanpackages":
                    return await PackageCleanerCommand.ExecuteAsync(desired, stateRoot);

                case "verify":
                    return await orchestrator.VerifyAsync();

                case "reserve":
                case "identity":
                    return await ReserveCommand.ExecuteAsync(desired, manifestPath, baseDir, stateRoot, action == "reserve", appName);

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

    public static readonly JsonSerializerOptions JsonIndented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
