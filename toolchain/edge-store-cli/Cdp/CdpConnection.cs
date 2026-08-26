using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Vainreef.EdgeStore.Cdp;

public class CdpConnection
{
    public int Port { get; private set; }
    public Process? EdgeProcess { get; private set; }
    public bool StartedByUs { get; private set; }
    public string ProfilePath { get; private set; } = string.Empty;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static Process StartProcessOnDefaultDesktop(string exePath, string arguments)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var si = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                lpDesktop = @"WinSta0\Default",
                wShowWindow = 3 /* SW_SHOWMAXIMIZED */,
                dwFlags = 1 /* STARTF_USESHOWWINDOW */
            };
            string cmdLine = $"\"{exePath}\" {arguments}";
            uint flags = 0x00000200 /* CREATE_NEW_PROCESS_GROUP */;
            if (CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, null, ref si, out var pi))
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
                return Process.GetProcessById(pi.dwProcessId);
            }
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Maximized
        };
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to launch Microsoft Edge.");
    }

    private static void LaunchViaTaskScheduler(string exePath, string arguments, string stateRoot)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                string batPath = Path.Combine(stateRoot, "launch-edge.bat");
                File.WriteAllText(batPath, $"@start \"\" \"{exePath}\" {arguments}\r\n");

                var psiCreate = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/create /tn \"EdgeStoreSession\" /tr \"{batPath}\" /sc once /st 23:59 /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psiCreate)) p?.WaitForExit(5000);

                var psiRun = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/run /tn \"EdgeStoreSession\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var pRun = Process.Start(psiRun)) pRun?.WaitForExit(5000);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SESSION] Task scheduler launch failed, falling back: {ex.Message}");
            }
        }

        StartProcessOnDefaultDesktop(exePath, arguments);
    }

    public static async Task<CdpConnection> StartOrReuseAsync(string stateRoot, string? customProfilePath = null, string? customEdgePath = null)
    {
        var conn = new CdpConnection();
        string pidFile = Path.Combine(stateRoot, "edge.pid");
        string portFile = Path.Combine(stateRoot, "edge.port");

        // Try reusing existing Edge process
        if (File.Exists(portFile))
        {
            try
            {
                int savedPort = int.Parse(File.ReadAllText(portFile).Trim());
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var versionResp = await http.GetStringAsync($"http://127.0.0.1:{savedPort}/json/version");
                if (!string.IsNullOrEmpty(versionResp))
                {
                    conn.Port = savedPort;
                    if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out int savedPid))
                    {
                        try { conn.EdgeProcess = Process.GetProcessById(savedPid); } catch { }
                    }
                    conn.StartedByUs = false;
                    return conn;
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
        string edgeArgs = $"--user-data-dir=\"{conn.ProfilePath}\" --remote-debugging-port={conn.Port} --remote-debugging-address=127.0.0.1 --remote-allow-origins=* --no-first-run --no-default-browser-check --start-maximized https://partner.microsoft.com/zh-cn/dashboard/home";

        LaunchViaTaskScheduler(edgeExe, edgeArgs, stateRoot);
        conn.StartedByUs = true;

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
