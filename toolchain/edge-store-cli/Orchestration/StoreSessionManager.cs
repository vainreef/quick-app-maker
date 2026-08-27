using System.Diagnostics;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;

namespace Vainreef.EdgeStore.Orchestration;

public class StoreSessionManager
{
    private readonly string _stateRoot;

    public StoreSessionManager(string stateRoot)
    {
        _stateRoot = stateRoot;
    }

    public (int pid, int port) GetSessionInfo()
    {
        string pidFile = Path.Combine(_stateRoot, "edge.pid");
        string portFile = Path.Combine(_stateRoot, "edge.port");
        int pid = File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out int p) ? p : 0;
        int port = File.Exists(portFile) && int.TryParse(File.ReadAllText(portFile).Trim(), out int pt) ? pt : 0;
        return (pid, port);
    }

    public void StopSession()
    {
        var (pid, _) = GetSessionInfo();
        try
        {
            if (pid > 0) Process.GetProcessById(pid).Kill(entireProcessTree: true);
        }
        catch { }

        try { File.Delete(Path.Combine(_stateRoot, "edge.pid")); } catch { }
        try { File.Delete(Path.Combine(_stateRoot, "edge.port")); } catch { }
    }

    public async Task EnsureSignedInAsync(CdpClient client, TimeSpan timeout)
    {
        var waiter = new Waiter(client);
        await waiter.RequireAsync(async () =>
        {
            string url = await client.EvaluateAsync<string>("location.href") ?? "";
            bool login = Regex.IsMatch(url, @"login\.microsoftonline|login\.live\.com|signin|aad/authPostGateway|oauth2", RegexOptions.IgnoreCase);
            if (login) return false;
            if (!url.Contains("partner.microsoft.com", StringComparison.OrdinalIgnoreCase)) return false;

            bool hasAuthMarker = await client.EvaluateAsync<bool>("""
            (() => {
                const url = location.href.toLowerCase();
                const text = document.body?.innerText || '';
                if (text.includes('登录到您的帐户') || text.includes('Sign in to your account') || text.includes('选取帐户') || text.includes('Pick an account')) return false;
                const isDashboard = url.includes('/dashboard/') || url.includes('/products/');
                const hasAppOrHome = text.includes('工作区') || text.includes('应用程序和游戏') || text.includes('Apps and games') || text.includes('+ 新产品') || text.includes('+ New product') || text.includes('产品概述') || text.includes('应用程序概述') || !!document.querySelector('partner-nav, .dashboard, [data-bi-area="AppOverview"], .partner-header-user-profile, .me-control');
                return isDashboard && hasAppOrHome;
            })()
            """);
            return hasAuthMarker;
        }, timeout, "Wait for signed-in Partner Center page");

        Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PASS] Partner Center session is active");
    }
}
