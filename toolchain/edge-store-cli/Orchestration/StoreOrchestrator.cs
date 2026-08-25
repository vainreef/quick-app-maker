using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public StoreOrchestrator(DesiredState desired, string stateRoot, StoreCheckpoint checkpoint,
        bool apply, bool submit, bool confirmSubmit, bool reloadVerify = true, bool keepOpen = false)
    {
        _desired = desired; _stateRoot = stateRoot; _checkpoint = checkpoint;
        _apply = apply; _submit = submit; _confirmSubmit = confirmSubmit;
        _reloadVerify = reloadVerify; _keepOpen = keepOpen;
        _logRoot = Path.Combine(stateRoot, "logs");
        Directory.CreateDirectory(_stateRoot);
        Directory.CreateDirectory(_logRoot);
    }

    public Task RunPreflightQualityInspectionAsync()
    {
        Log("INFO", "--- STORE 0: Offline static preflight ---");
        var errors = DesiredStateValidator.Validate(_desired, strict: true);
        if (errors.Count > 0) throw new InvalidOperationException("Preflight failed:\n" + string.Join("\n", errors.Select(x => "  - " + x)));

        using var archive = ZipFile.OpenRead(_desired.Assets.Msix);
        var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, "AppxManifest.xml", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("AppxManifest.xml is missing inside MSIX.");
        using var reader = new StreamReader(entry.Open());
        string xml = reader.ReadToEnd();
        if (!xml.Contains($"<DisplayName>{_desired.ProductName}</DisplayName>") && !xml.Contains($"DisplayName=\"{_desired.ProductName}\""))
            throw new InvalidOperationException($"MSIX DisplayName does not match productName [{_desired.ProductName}].");
        if (xml.Contains("Name=\"Windows.Universal\""))
            throw new InvalidOperationException("MSIX declares Windows.Universal; this workflow requires Windows.Desktop only.");

        Log("PASS", $"Desired state is complete: description={_desired.Values.Description.Length} chars, features={_desired.Values.Features.Count}, keywords={_desired.Values.Keywords.Count}.");
        Log("PASS", "MSIX identity/display name/device family checks passed.");
        return Task.CompletedTask;
    }

    public (int pid, int port) GetSessionInfo()
    {
        string pidFile = Path.Combine(_stateRoot, "edge.pid"), portFile = Path.Combine(_stateRoot, "edge.port");
        int pid = File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out int p) ? p : 0;
        int port = File.Exists(portFile) && int.TryParse(File.ReadAllText(portFile).Trim(), out int pt) ? pt : 0;
        return (pid, port);
    }

    public void StopSession()
    {
        var (pid, _) = GetSessionInfo();
        try { if (pid > 0) Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch { }
        try { File.Delete(Path.Combine(_stateRoot, "edge.pid")); } catch { }
        try { File.Delete(Path.Combine(_stateRoot, "edge.port")); } catch { }
    }

    public async Task EnsureSignedInAsync()
    {
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await EnsureSignedInCoreAsync(client, TimeSpan.FromMinutes(15));
    }

    public async Task<int> PrintStatusAsync()
    {
        var (pid, port) = GetSessionInfo();
        bool active = false;
        try { active = pid > 0 && !Process.GetProcessById(pid).HasExited; } catch { }
        Console.WriteLine(JsonSerializer.Serialize(new { pid, port, active, checkpoint = _checkpoint }, JsonIndented));
        if (!active) return 3;
        return await InspectAsync();
    }

    public async Task<int> InspectAsync()
    {
        var (pid, port) = GetSessionInfo();
        if (pid <= 0 || port <= 0) { Console.WriteLine("[STATUS] No active isolated Edge session."); return 3; }
        try
        {
            if (Process.GetProcessById(pid).HasExited) return 3;
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
        await EnsureSignedInCoreAsync(client, TimeSpan.FromSeconds(20));

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

    public async Task<int> RunAsync(string targetPhase = "all")
    {
        if (targetPhase != "all" && !PhaseNames.All.Contains(targetPhase, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown phase: {targetPhase}");

        // A run action performs only the requested browser phase(s). Preflight is
        // its own action, so targeted resume never replays Store 0.
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(await conn.GetTargetWebSocketUrlAsync()));
        await EnsureSignedInCoreAsync(client, TimeSpan.FromSeconds(20));

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

        // Full runs discover once. Targeted runs use the persisted ID and go
        // directly to their phase; discovery is a fallback only when no ID exists.
        if (targetPhase == "all" || string.IsNullOrWhiteSpace(submissionId))
        {
            Log("INFO", "Discovering the active submission once...");
            var discovery = new SubmissionDiscovery(client, waiter, native);
            var result = await discovery.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, autoCreateIfMissing: _apply);
            submissionId = result.SubmissionId;
            hrefs = result.Hrefs;
        }
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
                "listing" => root + "/listings",
                "options" => root + "/options",
                _ => $"{b}/{_desired.ProductId}/overview"
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
            // Only a real overview page counts as a "redirected to overview". During
            // SPA load a phase URL (e.g. /availability) can be transiently classified
            // as SubmissionOverview because of nav links; do not treat that as a
            // redirect or the overview-verify will read modules from the wrong page.
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
                var discovery = new SubmissionDiscovery(client, waiter, native);
                var result = await discovery.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, autoCreateIfMissing: _apply);
                if (string.IsNullOrWhiteSpace(result.SubmissionId)) throw;
                submissionId = result.SubmissionId;
                hrefs = result.Hrefs;
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
                if (plan.HasDifferences)
                {
                    needsChanges = true;
                    _checkpoint.Mark(phase, PhaseStatus.NeedsChanges, plan.ToString());
                    SaveCheckpoint();
                    if (!_apply) return;
                    _checkpoint.Mark(phase, PhaseStatus.Applying, plan.ToString());
                    SaveCheckpoint();
                    await apply(plan);
                    _checkpoint.Mark(phase, PhaseStatus.AppliedUnverified, "UI action completed; persistence not yet proven.");
                    SaveCheckpoint();
                    if (_reloadVerify)
                    {
                        // Save may redirect to overview. Return to the phase first,
                        // then force a cache-bypassing reload and recompute the full
                        // diff. Immediate same-DOM reads are never persistence proof.
                        alreadyComplete = await NavigatePhaseAsync(phase);
                        await waiter.ReloadAsync(ignoreCache: true);
                        await WaitPhaseStateAsync(phase, $"recognize {phase} after cold reload");
                        var verifyPlan = phaseAlreadyComplete ? new ReconcilePlan { Phase = phase } : await observePlan();
                        if (verifyPlan.HasDifferences)
                            throw new InvalidOperationException($"{phase} cold-load verification failed:\n{verifyPlan}");
                        await native.AssertNoVisibleErrorsAsync();
                    }
                }

                // Product truth: a cold navigation to overview and an explicit
                // complete status are required even when form diff is zero.
                await waiter.NavigateAsync(Url("overview"), "overview truth verification");
                var overview = await overviewAdapter.ObserveAsync();
                OverviewAdapter.AssertComplete(overview, phase);
                _checkpoint.MarkConverged(phase, $"Overview module={overview.Modules[phase]}", overview.Url);
                SaveCheckpoint();
                Log("PASS", $"Phase [{phase}] PRODUCT_VERIFIED");
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

    private async Task EnsureSignedInCoreAsync(CdpClient client, TimeSpan timeout)
    {
        var waiter = new Waiter(client);
        await waiter.RequireAsync(async () =>
        {
            string url = await client.EvaluateAsync<string>("location.href") ?? "";
            bool login = Regex.IsMatch(url, "login\\.microsoftonline|login\\.live\\.com|signin", RegexOptions.IgnoreCase);
            return !login && url.Contains("partner.microsoft.com", StringComparison.OrdinalIgnoreCase);
        }, timeout, "Wait for signed-in Partner Center page");
        Log("PASS", "Partner Center session is active");
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

    private static readonly JsonSerializerOptions JsonIndented = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}
