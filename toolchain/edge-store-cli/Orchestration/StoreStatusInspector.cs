using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Orchestration;

public class StoreStatusInspector
{
    private readonly DesiredState _desired;
    private readonly string _stateRoot;
    private readonly StoreCheckpoint _checkpoint;
    private readonly StoreSessionManager _sessionManager;

    public StoreStatusInspector(DesiredState desired, string stateRoot, StoreCheckpoint checkpoint, StoreSessionManager sessionManager)
    {
        _desired = desired;
        _stateRoot = stateRoot;
        _checkpoint = checkpoint;
        _sessionManager = sessionManager;
    }

    public async Task<int> PrintStatusAsync()
    {
        var (pid, port) = _sessionManager.GetSessionInfo();
        bool active = false;
        if (port > 0)
        {
            try
            {
                using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var resp = await probe.GetStringAsync($"http://127.0.0.1:{port}/json/version");
                active = !string.IsNullOrEmpty(resp);
            }
            catch { }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { pid, port, active, checkpoint = _checkpoint }, JsonIndented));
        if (!active) return 3;
        return await InspectAsync();
    }

    public async Task<int> InspectAsync()
    {
        var (pid, port) = _sessionManager.GetSessionInfo();
        if (port <= 0) { Console.WriteLine("[STATUS] No active isolated Edge session."); return 3; }
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            _ = await probe.GetStringAsync($"http://127.0.0.1:{port}/json/version");
        }
        catch
        {
            Console.WriteLine("[STATUS] Saved Edge session is stale; inspect made no browser changes.");
            return 3;
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
            await using var client = new CdpClient();
            string wsUrl = await conn.GetTargetWebSocketUrlAsync();
            await client.ConnectAsync(new Uri(wsUrl));
            var snapshot = await new PageInspector(client).CaptureAsync();
            Console.WriteLine(JsonSerializer.Serialize(snapshot, JsonIndented));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[STATUS] Active Edge session exists (pid={pid}, port={port}) but page inspection probe timed out or returned: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> VerifyAsync()
    {
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await _sessionManager.EnsureSignedInAsync(client, TimeSpan.FromSeconds(20));

        var inspector = new PageInspector(client);
        var overviewAdapter = new OverviewAdapter(client, inspector);
        var waiter = new Waiter(client);

        string submissionId = _checkpoint.SubmissionId;
        if (string.IsNullOrWhiteSpace(submissionId)) submissionId = _desired.SubmissionId;
        if (string.IsNullOrWhiteSpace(submissionId))
        {
            var native = new NativeFormAdapter(client, new InputDriver(client, new AxLocator(client, new DomDriver(client))));
            var discovery = new SubmissionDiscovery(client, waiter, native);
            var result = await discovery.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, autoCreateIfMissing: false);
            submissionId = result.SubmissionId;
        }

        string overviewUrl = $"{_desired.Site.BaseUrl.TrimEnd('/')}/{_desired.ProductId}/overview";
        await waiter.NavigateAsync(overviewUrl, "Overview Verification");
        var overview = await overviewAdapter.ObserveAsync();
        Console.WriteLine(JsonSerializer.Serialize(overview, JsonIndented));

        bool allComplete = true;
        foreach (string phase in PhaseNames.All)
        {
            var status = overview.Modules.GetValueOrDefault(phase);
            if (status != ModuleCompletion.Complete)
            {
                allComplete = false;
                Console.WriteLine($"[INCOMPLETE] Phase [{phase}] status is {status}.");
            }
            else
            {
                Console.WriteLine($"[PASS] Phase [{phase}] is Complete.");
            }
        }

        return allComplete ? 0 : 4;
    }

    private static readonly JsonSerializerOptions JsonIndented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
