using System.Text.Json;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.Orchestration;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
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
                    if (i + 1 < args.Length) productId = args[++i];
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

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            manifestPath = Path.Combine(toolRoot, "examples", "store-automation.json");
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

        // Check if markdown listing file exists and load it
        if (!string.IsNullOrWhiteSpace(desired.ListingMarkdown))
        {
            string mdPath = Path.IsPathRooted(desired.ListingMarkdown)
                ? desired.ListingMarkdown
                : Path.Combine(baseDir, desired.ListingMarkdown);

            if (File.Exists(mdPath))
            {
                ImportMarkdownListing(desired, mdPath);
            }
        }

        Directory.CreateDirectory(stateRoot);
        var checkpoint = new StoreCheckpoint
        {
            ProductId = desired.ProductId
        };

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
                    Console.WriteLine("[INFO] Checking Edge Store session status...");
                    var (livePid, livePort) = orchestrator.GetSessionInfo();
                    Console.WriteLine($"PID: {livePid}, Port: {livePort}");
                    return 0;

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

    private static void ImportMarkdownListing(DesiredState state, string mdPath)
    {
        string text = File.ReadAllText(mdPath);

        var shortDescMatch = Regex.Match(text, @"##\s*Short Description\s*\n+([\s\S]*?)(?=\n##|$)", RegexOptions.IgnoreCase);
        if (shortDescMatch.Success && !string.IsNullOrWhiteSpace(shortDescMatch.Groups[1].Value))
        {
            state.Values.ShortDescription = shortDescMatch.Groups[1].Value.Trim();
        }

        var descMatch = Regex.Match(text, @"##\s*Description\s*\n+([\s\S]*?)(?=\n##|$)", RegexOptions.IgnoreCase);
        if (descMatch.Success && !string.IsNullOrWhiteSpace(descMatch.Groups[1].Value))
        {
            state.Values.Description = descMatch.Groups[1].Value.Trim();
        }

        var featMatch = Regex.Match(text, @"##\s*Features\s*\n+([\s\S]*?)(?=\n##|$)", RegexOptions.IgnoreCase);
        if (featMatch.Success)
        {
            var items = Regex.Matches(featMatch.Groups[1].Value, @"^[-*]\s*(.+)$", RegexOptions.Multiline)
                             .Select(m => m.Groups[1].Value.Trim())
                             .Where(s => !string.IsNullOrWhiteSpace(s))
                             .ToList();
            if (items.Count > 0) state.Values.Features = items;
        }

        var kwMatch = Regex.Match(text, @"##\s*Keywords\s*\n+([\s\S]*?)(?=\n##|$)", RegexOptions.IgnoreCase);
        if (kwMatch.Success)
        {
            var items = Regex.Matches(kwMatch.Groups[1].Value, @"^[-*]\s*(.+)$", RegexOptions.Multiline)
                             .Select(m => m.Groups[1].Value.Trim())
                             .Where(s => !string.IsNullOrWhiteSpace(s))
                             .ToList();
            if (items.Count > 0) state.Values.Keywords = items;
        }
    }
}
