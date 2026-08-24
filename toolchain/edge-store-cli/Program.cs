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
        string stateRoot = Path.Combine(toolRoot, "state");

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

        // Import markdown listing if provided
        ImportListingMarkdown(desired, baseDir);

        if (!string.IsNullOrEmpty(productId))
        {
            desired.ProductId = productId;
        }

        var orchestrator = new StoreOrchestrator(desired, stateRoot, apply, submit, confirmSubmit, reloadVerify);

        try
        {
            if (action == "preflight")
            {
                orchestrator.RunPreflight();
                return 0;
            }

            if (action == "launch")
            {
                var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
                Console.WriteLine($"[PASS] Isolated Edge process ready. PID={conn.EdgeProcess?.Id} Port={conn.Port}");
                return 0;
            }

            if (action == "stop")
            {
                string pidFile = Path.Combine(stateRoot, "edge.pid");
                if (File.Exists(pidFile))
                {
                    int pid = int.Parse(File.ReadAllText(pidFile).Trim());
                    try
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(pid);
                        proc.Kill();
                        Console.WriteLine($"[PASS] Stopped isolated Edge process pid={pid}");
                    }
                    catch { }
                }
                return 0;
            }

            await orchestrator.RunPipelineAsync(phase);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            Console.WriteLine($"[STACK] {ex.StackTrace}");
            return 1;
        }
    }

    private static void ResolvePaths(DesiredState desired, string baseDir)
    {
        string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(baseDir, path));
        }

        desired.Assets.Msix = Resolve(desired.Assets.Msix);
        desired.Assets.Screenshot = Resolve(desired.Assets.Screenshot);
        desired.Assets.Poster = Resolve(desired.Assets.Poster);
        desired.Assets.Boxart = Resolve(desired.Assets.Boxart);
        desired.Assets.Logo300 = Resolve(desired.Assets.Logo300);
        desired.Assets.Logo150 = Resolve(desired.Assets.Logo150);
        desired.Assets.Logo71 = Resolve(desired.Assets.Logo71);
        desired.Assets.Superhero = Resolve(desired.Assets.Superhero);

        if (!string.IsNullOrEmpty(desired.ListingMarkdown))
        {
            desired.ListingMarkdown = Resolve(desired.ListingMarkdown);
        }
    }

    private static void ImportListingMarkdown(DesiredState desired, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(desired.ListingMarkdown) || !File.Exists(desired.ListingMarkdown)) return;

        string text = File.ReadAllText(desired.ListingMarkdown);

        var shortMatch = Regex.Match(text, @"(?ms)^##\s*简短摘要.*?\r?\n\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*完整描述)");
        var fullMatch = Regex.Match(text, @"(?ms)^##\s*完整描述.*?\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*产品功能)");
        var featuresMatch = Regex.Match(text, @"(?ms)^##\s*产品功能.*?\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*搜索关键词)");
        var keywordsMatch = Regex.Match(text, @"(?ms)^##\s*搜索关键词.*?\r?\n\r?\n(?<value>[^\r\n]+)");

        if (shortMatch.Success && string.IsNullOrWhiteSpace(desired.Values.ShortDescription))
        {
            desired.Values.ShortDescription = shortMatch.Groups["value"].Value.Trim();
        }
        if (fullMatch.Success && string.IsNullOrWhiteSpace(desired.Values.Description))
        {
            desired.Values.Description = fullMatch.Groups["value"].Value.Trim();
        }
        if (featuresMatch.Success && desired.Values.Features.Count == 0)
        {
            desired.Values.Features = featuresMatch.Groups["value"].Value
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => Regex.IsMatch(line, @"^\s*-\s+"))
                .Select(line => Regex.Replace(line, @"^\s*-\s+", "").Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }
        if (keywordsMatch.Success && desired.Values.Keywords.Count == 0)
        {
            desired.Values.Keywords = keywordsMatch.Groups["value"].Value
                .Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrEmpty(k))
                .ToList();
        }
    }
}
