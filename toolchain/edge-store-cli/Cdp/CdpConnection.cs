using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;

namespace Vainreef.EdgeStore.Cdp;

public class CdpConnection
{
    public int Port { get; private set; }
    public Process? EdgeProcess { get; private set; }
    public bool StartedByUs { get; private set; }
    public string ProfilePath { get; private set; } = string.Empty;

    public static async Task<CdpConnection> StartOrReuseAsync(string stateRoot, string? customProfilePath = null, string? customEdgePath = null)
    {
        var conn = new CdpConnection();
        string pidFile = Path.Combine(stateRoot, "edge.pid");
        string portFile = Path.Combine(stateRoot, "edge.port");

        // Try reusing existing Edge process
        if (File.Exists(pidFile) && File.Exists(portFile))
        {
            try
            {
                int savedPid = int.Parse(File.ReadAllText(pidFile).Trim());
                int savedPort = int.Parse(File.ReadAllText(portFile).Trim());
                var proc = Process.GetProcessById(savedPid);
                if (!proc.HasExited)
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                    var versionResp = await http.GetStringAsync($"http://127.0.0.1:{savedPort}/json/version");
                    if (!string.IsNullOrEmpty(versionResp))
                    {
                        conn.Port = savedPort;
                        conn.EdgeProcess = proc;
                        conn.StartedByUs = false;
                        return conn;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SESSION] Saved Edge session is stale: {ex.Message}");
            }
        }

        // Allocate a free TCP port
        conn.Port = GetFreeTcpPort();
        conn.ProfilePath = string.IsNullOrWhiteSpace(customProfilePath)
            ? Path.Combine(stateRoot, "edge-profile")
            : customProfilePath;

        Directory.CreateDirectory(conn.ProfilePath);

        string edgeExe = ResolveEdgeExecutable(customEdgePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = edgeExe,
            Arguments = $"--user-data-dir=\"{conn.ProfilePath}\" --remote-debugging-port={conn.Port} --remote-debugging-address=127.0.0.1 --remote-allow-origins=* --no-first-run --no-default-browser-check --start-maximized https://partner.microsoft.com/zh-cn/dashboard/home",
            UseShellExecute = true
        };

        conn.EdgeProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch Microsoft Edge.");
        conn.StartedByUs = true;

        File.WriteAllText(pidFile, conn.EdgeProcess.Id.ToString());
        File.WriteAllText(portFile, conn.Port.ToString());

        // Wait for DevTools endpoint to become ready
        using var client = new HttpClient();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resp = await client.GetStringAsync($"http://127.0.0.1:{conn.Port}/json/version");
                if (!string.IsNullOrEmpty(resp)) break;
            }
            catch
            {
                await Task.Delay(250);
            }
        }

        return conn;
    }

    public async Task<string> GetTargetWebSocketUrlAsync()
    {
        using var http = new HttpClient();
        var resp = await http.GetStringAsync($"http://127.0.0.1:{Port}/json/list");
        using var doc = JsonDocument.Parse(resp);

        var pages = doc.RootElement.EnumerateArray()
            .Where(e => e.TryGetProperty("type", out var t) && t.GetString() == "page" && e.TryGetProperty("webSocketDebuggerUrl", out _))
            .ToList();

        if (pages.Count == 0)
        {
            throw new InvalidOperationException("No Edge page target available.");
        }

        // Prefer Partner Center tab
        var partner = pages.FirstOrDefault(p => p.TryGetProperty("url", out var u) && u.GetString()?.Contains("partner.microsoft.com") == true);
        var target = partner.ValueKind != JsonValueKind.Undefined ? partner : pages[0];

        return target.GetProperty("webSocketDebuggerUrl").GetString()
            ?? throw new InvalidOperationException("Page target has no WebSocket debugger URL.");
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string ResolveEdgeExecutable(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        throw new FileNotFoundException("Microsoft Edge Stable executable not found.");
    }
}
