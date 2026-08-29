using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Orchestration;

public sealed class StoreOrchestrator
{
    private readonly DesiredState _desired;
    private readonly string _stateRoot;
    private readonly string _logRoot;
    private readonly StoreCheckpoint _checkpoint;
    private readonly bool _apply;
    private readonly bool _submit;
    private readonly bool _confirmSubmit;
    private readonly bool _reloadVerify;
    private readonly bool _keepOpen;
    private readonly StoreSessionManager _sessionManager;
    private readonly StoreStatusInspector _statusInspector;

    public StoreOrchestrator(DesiredState desired, string stateRoot, StoreCheckpoint checkpoint,
        bool apply, bool submit, bool confirmSubmit, bool reloadVerify = true, bool keepOpen = false)
    {
        _desired = desired; _stateRoot = stateRoot; _checkpoint = checkpoint;
        _apply = apply; _submit = submit; _confirmSubmit = confirmSubmit;
        _reloadVerify = reloadVerify; _keepOpen = keepOpen;
        _logRoot = Path.Combine(stateRoot, "logs");
        Directory.CreateDirectory(_stateRoot);
        Directory.CreateDirectory(_logRoot);
        _sessionManager = new StoreSessionManager(_stateRoot);
        _statusInspector = new StoreStatusInspector(_desired, _stateRoot, _checkpoint, _sessionManager);
    }

    public Task RunPreflightQualityInspectionAsync()
    {
        StorePreflight.Run(_desired);
        return Task.CompletedTask;
    }

    public (int pid, int port) GetSessionInfo() => _sessionManager.GetSessionInfo();
    public void StopSession() => _sessionManager.StopSession();
    public async Task EnsureSignedInAsync()
    {
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await _sessionManager.EnsureSignedInAsync(client, TimeSpan.FromMinutes(15));
    }
    public Task<int> PrintStatusAsync() => _statusInspector.PrintStatusAsync();
    public Task<int> InspectAsync() => _statusInspector.InspectAsync();
    public Task<int> VerifyAsync() => _statusInspector.VerifyAsync();

    public async Task<int> RunAsync(string targetPhase = "all")
    {
        if (targetPhase != "all" && !PhaseNames.All.Contains(targetPhase, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown phase: {targetPhase}");

        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await _sessionManager.EnsureSignedInAsync(client, TimeSpan.FromMinutes(15));

        var dom = new DomDriver(client);
        var ax = new AxLocator(client, dom);
        var input = new InputDriver(client, ax);
        var waiter = new Waiter(client);
        var inspector = new PageInspector(client);
        var native = new NativeFormAdapter(client, input);
        var checkbox = new HeCheckboxAdapter(client, input);
        var heSelect = new HeSelectAdapter(client, ax, input, waiter);

        string submissionId = _checkpoint.SubmissionId;
        var hrefs = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(submissionId)) submissionId = _desired.SubmissionId;

        Log("INFO", "Discovering active submission and live module routes...");
        var discovery = new SubmissionDiscovery(client, waiter, native);
        var result = await discovery.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, knownSubmissionId: submissionId, autoCreateIfMissing: true);
        submissionId = result.SubmissionId;
        hrefs = result.Hrefs;
        if (string.IsNullOrWhiteSpace(submissionId)) throw new InvalidOperationException("No active submission ID was found.");
        _checkpoint.SubmissionId = submissionId;
        SaveCheckpoint();

        string Url(string phase)
        {
            if (hrefs.TryGetValue(phase, out string? live) && !string.IsNullOrWhiteSpace(live)) return live;
            string b = _desired.Site.BaseUrl.TrimEnd('/'), root = $"{b}/{_desired.ProductId}/submissions/{submissionId}";
            return phase switch
            {
                "availability" => root + "/availability",
                "properties" => root + "/properties",
                "ageRatings" => root + "/ageratings",
                "packages" => root + "/packages",
                "listing" => root + "/managelanguages?producttype=app",
                "options" => root + "/options",
                "overview" => root + "/overview",
                _ => root + "/overview"
            };
        }

        var availability = new AvailabilityAdapter(client, waiter, heSelect, native);
        var properties = new PropertiesAdapter(client, waiter, native, checkbox);
        var ageRatings = new AgeRatingsAdapter(client, waiter, native, input);
        var packages = new PackagesAdapter(client, dom, waiter, native, checkbox);
        var listing = new ListingAdapter(client, dom, waiter, native, input);
        var options = new OptionsAdapter(client, waiter, native);
        var overviewAdapter = new OverviewAdapter(client, inspector);
        bool needsChanges = false;
        bool discoveryFallbackUsed = targetPhase == "all";

        IReadOnlyCollection<PartnerPageKind> ExpectedKinds(string phase) => phase switch
        {
            "availability" => [PartnerPageKind.AvailabilityForm, PartnerPageKind.SubmissionOverview],
            "properties" => [PartnerPageKind.PropertiesForm, PartnerPageKind.SubmissionOverview],
            "ageRatings" => [PartnerPageKind.AgeRatingsQuestionnaire, PartnerPageKind.AgeRatingsSummary, PartnerPageKind.SubmissionOverview],
            "packages" => [PartnerPageKind.PackagesForm, PartnerPageKind.SubmissionOverview],
            "listing" => [PartnerPageKind.ListingLanguageGrid, PartnerPageKind.ListingForm, PartnerPageKind.SubmissionOverview],
            "options" => [PartnerPageKind.OptionsForm, PartnerPageKind.SubmissionOverview],
            _ => [PartnerPageKind.Unknown]
        };

        bool phaseAlreadyComplete = false;
        async Task<PageSnapshot> WaitPhaseStateAsync(string phase, string operation)
        {
            var page = await inspector.WaitForAsync(ExpectedKinds(phase), TimeSpan.FromSeconds(90), operation);
            if (page.Kind == PartnerPageKind.SubmissionOverview && page.Url.Contains("/overview"))
            {
                var overview = await overviewAdapter.ObserveAsync();
                if (overview.Modules.GetValueOrDefault(phase) != ModuleCompletion.Complete)
                    throw new InvalidOperationException($"Phase route redirected to overview, but module [{phase}] is not Complete.");
                phaseAlreadyComplete = true;
                return page;
            }
            phaseAlreadyComplete = false;
            if (phase == "listing" && page.Kind == PartnerPageKind.ListingLanguageGrid)
            {
                await listing.EnterLanguageFormAsync(_desired, _apply);
                page = await inspector.WaitForAsync([PartnerPageKind.ListingForm], TimeSpan.FromSeconds(90), "enter configured listing language");
            }
            return page;
        }

        async Task<bool> NavigatePhaseAsync(string phase)
        {
            phaseAlreadyComplete = false;
            await waiter.NavigateAsync(Url(phase), phase, allowOverviewRedirect: true);
            PageSnapshot page;
            try
            {
                page = await WaitPhaseStateAsync(phase, $"recognize {phase} page");
            }
            catch when (!discoveryFallbackUsed)
            {
                discoveryFallbackUsed = true;
                Log("WARN", "Cached submission route was stale or unrecognized; performing one overview discovery fallback.");
                var disc = new SubmissionDiscovery(client, waiter, native);
                var res = await disc.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, autoCreateIfMissing: _apply);
                if (string.IsNullOrWhiteSpace(res.SubmissionId)) throw;
                submissionId = res.SubmissionId;
                hrefs = res.Hrefs;
                _checkpoint.SubmissionId = submissionId;
                SaveCheckpoint();
                await waiter.NavigateAsync(Url(phase), phase, allowOverviewRedirect: true);
                page = await WaitPhaseStateAsync(phase, $"recognize {phase} page after discovery");
            }
            Log("STATE", $"{phase}: page={page.Kind}, url={page.Url}");
            return phaseAlreadyComplete;
        }

        async Task ReconcileAsync(string phase, Func<Task<ReconcilePlan>> observePlan, Func<ReconcilePlan, Task> apply)
        {
            Log("INFO", $"=== Phase [{phase}] ===");
            try
            {
                bool alreadyComplete = await NavigatePhaseAsync(phase);
                _checkpoint.Mark(phase, PhaseStatus.Observed);
                SaveCheckpoint();
                var plan = alreadyComplete ? new ReconcilePlan { Phase = phase } : await observePlan();
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences || !alreadyComplete)
                {
                    needsChanges = true;
                    _checkpoint.Mark(phase, PhaseStatus.NeedsChanges, plan.ToString());
                    SaveCheckpoint();
                    if (!_apply) return;
                    _checkpoint.Mark(phase, PhaseStatus.Applying, plan.ToString());
                    SaveCheckpoint();
                    await apply(plan);
                    _checkpoint.Mark(phase, PhaseStatus.AppliedUnverified, "UI action completed; verifying overview...");
                    SaveCheckpoint();
                }

                await waiter.NavigateAsync(Url("overview"), "overview truth verification");
                var overview = await overviewAdapter.ObserveAsync();
                string cardDom = await overviewAdapter.ExtractSubmissionCardDomAsync();
                Console.WriteLine($"\n[SUBMISSION-CARD-DOM] >>> Extracted Live DOM for Agent Review:\n{cardDom}\n");
                _checkpoint.MarkConverged(phase, $"Overview module={overview.Modules.GetValueOrDefault(phase)}", overview.Url);
                SaveCheckpoint();
                Log("PASS", $"Phase [{phase}] SAVED & LIVE DOM EXTRACTED");
            }
            catch (Exception ex)
            {
                string url = "";
                try { url = (await inspector.CaptureAsync()).Url; } catch { }
                _checkpoint.Mark(phase, PhaseStatus.Failed, ex.Message, url);
                SaveCheckpoint();
                throw;
            }
        }

        async Task RunPhaseAsync(string phase)
        {
            switch (phase)
            {
                case "availability": await ReconcileAsync(phase, async () => availability.PlanDiff(_desired, await availability.ObserveAsync()), p => availability.ApplyChangesAsync(p, _desired)); break;
                case "properties": await ReconcileAsync(phase, async () => properties.PlanDiff(_desired, await properties.ObserveAsync()), p => properties.ApplyChangesAsync(p, _desired)); break;
                case "ageRatings": await ReconcileAsync(phase, async () => ageRatings.PlanDiff(_desired, await ageRatings.ObserveAsync()), p => ageRatings.ApplyChangesAsync(p, _desired)); break;
                case "packages": await ReconcileAsync(phase, async () => packages.PlanDiff(_desired, await packages.ObserveAsync()), p => packages.ApplyChangesAsync(p, _desired)); break;
                case "listing": await ReconcileAsync(phase, async () => listing.PlanDiff(_desired, await listing.ObserveAsync()), p => listing.ApplyChangesAsync(p, _desired)); break;
                case "options": await ReconcileAsync(phase, async () => options.PlanDiff(_desired, await options.ObserveAsync()), p => options.ApplyChangesAsync(p, _desired)); break;
            }
        }

        if (targetPhase == "all") foreach (string phase in PhaseNames.All) await RunPhaseAsync(phase);
        else await RunPhaseAsync(targetPhase);

        if (targetPhase == "all")
        {
            await waiter.NavigateAsync(Url("overview"), "final overview");
            var finalOverview = await overviewAdapter.ObserveAsync();
            if (_apply) OverviewAdapter.AssertAllComplete(finalOverview);

            if (_submit && _confirmSubmit && _apply)
            {
                await native.ClickByTextAsync(["提交进行认证", "Submit for certification"], "Submit for certification");
                await inspector.WaitForAsync([PartnerPageKind.SubmissionConfirmation], TimeSpan.FromSeconds(30), "submission confirmation dialog");
                await native.ClickDialogButtonAsync(["提交", "Submit"], "Confirm submission");
                await inspector.WaitForAsync([PartnerPageKind.CertificationStatus, PartnerPageKind.ProductOverview], TimeSpan.FromSeconds(90), "certification status");
                Log("PASS", "Submission entered certification.");
            }
        }

        return needsChanges && !_apply ? 4 : 0;
    }

    public async Task<int> RunSingleStepAsync(string phase)
    {
        if (!PhaseNames.All.Contains(phase, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown phase: {phase}. Must be one of: {string.Join(", ", PhaseNames.All)}");

        Log("STEP", $">>> Stage [{phase}]: Initializing session & checking authentication...");
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await _sessionManager.EnsureSignedInAsync(client, TimeSpan.FromMinutes(15));

        Log("STEP", $">>> Stage [{phase}]: Executing reconcile plan...");
        int runResult = await RunAsync(phase);
        if (runResult != 0)
        {
            Log("FAIL", $"Stage [{phase}] reconcile returned error code: {runResult}");
            return runResult;
        }

        Log("DOM-PROBE", $">>> Stage [{phase}]: Executing mandatory post-phase DOM self-inspection...");
        var domEvidence = await PhaseDomVerifier.VerifyAsync(phase, client, _desired);
        Console.WriteLine($"[DOM-PROBE] DOM Inspection Result for [{phase}]:");
        Console.WriteLine(JsonSerializer.Serialize(domEvidence, JsonIndented));

        string currentUrl = "";
        try { currentUrl = await client.EvaluateAsync<string>("location.href") ?? ""; } catch { }
        _checkpoint.MarkConverged(phase, $"DOM verified: {JsonSerializer.Serialize(domEvidence)}", currentUrl);
        SaveCheckpoint();

        Log("PASS", $"Stage [{phase}] 100% SUCCESS & DOM SELF-INSPECTED.");
        return 0;
    }

    public async Task<int> DiscoverAndSnapshotAsync()
    {
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await _sessionManager.EnsureSignedInAsync(client, TimeSpan.FromMinutes(15));

        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));
        var native = new NativeFormAdapter(client, input);
        var discovery = new SubmissionDiscovery(client, waiter, native);
        var inspector = new PageInspector(client);

        var result = await discovery.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, autoCreateIfMissing: true);
        _checkpoint.SubmissionId = result.SubmissionId;
        SaveCheckpoint();

        var overviewAdapter = new OverviewAdapter(client, inspector);
        var overview = await overviewAdapter.ObserveAsync();
        Console.WriteLine(JsonSerializer.Serialize(new { submissionId = result.SubmissionId, hrefs = result.Hrefs, overview }, JsonIndented));
        return 0;
    }

    private void SaveCheckpoint()
    {
        string temp = Path.Combine(_stateRoot, "checkpoint.json.tmp"), path = Path.Combine(_stateRoot, "checkpoint.json");
        string json = JsonSerializer.Serialize(_checkpoint, JsonIndented);
        File.WriteAllText(temp, json, new System.Text.UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private void Log(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);
        try { File.AppendAllText(Path.Combine(_logRoot, "edge-store.log"), line + Environment.NewLine, new System.Text.UTF8Encoding(false)); } catch { }
    }

    private static readonly JsonSerializerOptions JsonIndented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
