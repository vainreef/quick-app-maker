using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Orchestration;

public class StoreOrchestrator
{
    private readonly DesiredState _desired;
    private readonly string _stateRoot;
    private readonly string _logRoot;
    private readonly bool _apply;
    private readonly bool _submit;
    private readonly bool _confirmSubmit;
    private readonly bool _reloadVerify;

    public StoreOrchestrator(DesiredState desired, string stateRoot, bool apply, bool submit, bool confirmSubmit, bool reloadVerify = true)
    {
        _desired = desired;
        _stateRoot = stateRoot;
        _logRoot = Path.Combine(stateRoot, "logs");
        _apply = apply;
        _submit = submit;
        _confirmSubmit = confirmSubmit;
        _reloadVerify = reloadVerify;

        Directory.CreateDirectory(_stateRoot);
        Directory.CreateDirectory(_logRoot);
    }

    public void RunPreflight()
    {
        Log("INFO", "--- Starting STORE 0: Offline Static Preflight Quality Inspection ---");

        if (!string.IsNullOrWhiteSpace(_desired.Assets.Msix))
        {
            if (!File.Exists(_desired.Assets.Msix))
            {
                throw new FileNotFoundException($"MSIX package not found at: {_desired.Assets.Msix}");
            }

            using var archive = ZipFile.OpenRead(_desired.Assets.Msix);
            var entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, "AppxManifest.xml", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("AppxManifest.xml missing inside MSIX.");

            using var reader = new StreamReader(entry.Open());
            string xml = reader.ReadToEnd();

            if (!string.IsNullOrEmpty(_desired.ProductName))
            {
                if (!xml.Contains($"<DisplayName>{_desired.ProductName}</DisplayName>") &&
                    !xml.Contains($"DisplayName=\"{_desired.ProductName}\""))
                {
                    throw new InvalidOperationException($"MSIX DisplayName does not match product name [{_desired.ProductName}].");
                }
                Log("PASS", $"MSIX DisplayName matches [{_desired.ProductName}]");
            }

            if (xml.Contains("Name=\"Windows.Universal\""))
            {
                throw new InvalidOperationException("MSIX contains Windows.Universal TargetDeviceFamily dependency. Desktop apps must only declare Windows.Desktop.");
            }
            Log("PASS", "MSIX TargetDeviceFamily verified: Desktop only");
        }

        if (_desired.Values.Keywords.Count > 7)
        {
            throw new InvalidOperationException($"Keywords count exceeds Microsoft Store limit of 7 (currently {_desired.Values.Keywords.Count}).");
        }
        Log("PASS", $"Keywords count verified ({_desired.Values.Keywords.Count} <= 7)");

        Log("PASS", "STORE 0: Static preflight passed successfully.");
    }

    public async Task RunPipelineAsync(string targetPhase = "all")
    {
        RunPreflight();

        Log("INFO", "--- Starting STORE 1: Session Discovery ---");
        var conn = await CdpConnection.StartOrReuseAsync(_stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();

        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var dom = new DomDriver(client);
        var ax = new AxLocator(client, dom);
        var input = new InputDriver(client, ax);
        var waiter = new Waiter(client);
        var native = new NativeFormAdapter(client, input);
        var checkbox = new HeCheckboxAdapter(client, input);
        var heSelect = new HeSelectAdapter(client, ax, input, waiter);

        await EnsureSignedInAsync(client, waiter);

        Log("INFO", "--- Starting STORE 2: Live Compatibility Probe ---");
        var discovery = new SubmissionDiscovery(client, waiter, native);
        var discResult = await discovery.DiscoverAsync(_desired.Site.BaseUrl, _desired.ProductId, autoCreateIfMissing: _apply);

        string submissionId = discResult.SubmissionId;
        if (string.IsNullOrEmpty(submissionId))
        {
            throw new InvalidOperationException("Failed to discover active submission ID from Partner Center overview page.");
        }
        Log("PASS", $"Live submission discovered: {submissionId}");

        var availabilityAdapter = new AvailabilityAdapter(client, waiter, heSelect, native);
        var propertiesAdapter = new PropertiesAdapter(client, waiter, native, checkbox);
        var ageRatingsAdapter = new AgeRatingsAdapter(client, waiter, native, input);
        var packagesAdapter = new PackagesAdapter(client, dom, waiter, native, checkbox);
        var listingAdapter = new ListingAdapter(client, dom, waiter, native, input);
        var optionsAdapter = new OptionsAdapter(client, waiter, native);

        string GetUrl(string phase)
        {
            if (discResult.Hrefs.TryGetValue(phase, out var href)) return href;
            string b = _desired.Site.BaseUrl.TrimEnd('/');
            return phase switch
            {
                "availability" => $"{b}/{_desired.ProductId}/submissions/{submissionId}/availability",
                "properties" => $"{b}/{_desired.ProductId}/submissions/{submissionId}/properties",
                "ageRatings" => $"{b}/{_desired.ProductId}/submissions/{submissionId}/ageratings",
                "packages" => $"{b}/{_desired.ProductId}/submissions/{submissionId}/packages",
                "listing" => $"{b}/{_desired.ProductId}/submissions/{submissionId}/listings?languageid={_desired.Site.LanguageId}&languagecode={_desired.Site.LanguageCode}",
                "options" => $"{b}/{_desired.ProductId}/submissions/{submissionId}/options",
                _ => $"{b}/{_desired.ProductId}/submissions/{submissionId}/overview"
            };
        }

        async Task ReconcilePhaseAsync(string phaseName, Func<Task> execute)
        {
            Log("INFO", $"=== Reconciling Phase: [{phaseName}] ===");
            await waiter.NavigateAsync(GetUrl(phaseName), phaseName);
            await execute();
            Log("PASS", $"Phase [{phaseName}] CONVERGED");
        }

        if (targetPhase is "all" or "availability")
        {
            await ReconcilePhaseAsync("availability", async () =>
            {
                var obs = await availabilityAdapter.ObserveAsync();
                var plan = availabilityAdapter.PlanDiff(_desired, obs);
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences)
                {
                    if (!_apply) return;
                    await availabilityAdapter.ApplyChangesAsync(plan, _desired);
                    if (_reloadVerify) await availabilityAdapter.VerifyAsync(_desired);
                }
            });
        }

        if (targetPhase is "all" or "properties")
        {
            await ReconcilePhaseAsync("properties", async () =>
            {
                var obs = await propertiesAdapter.ObserveAsync();
                var plan = propertiesAdapter.PlanDiff(_desired, obs);
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences)
                {
                    if (!_apply) return;
                    await propertiesAdapter.ApplyChangesAsync(plan, _desired);
                    if (_reloadVerify) await propertiesAdapter.VerifyAsync(_desired);
                }
            });
        }

        if (targetPhase is "all" or "ageRatings")
        {
            await ReconcilePhaseAsync("ageRatings", async () =>
            {
                var obs = await ageRatingsAdapter.ObserveAsync();
                var plan = ageRatingsAdapter.PlanDiff(_desired, obs);
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences)
                {
                    if (!_apply) return;
                    await ageRatingsAdapter.ApplyChangesAsync(plan, _desired);
                    if (_reloadVerify) await ageRatingsAdapter.VerifyAsync(_desired);
                }
            });
        }

        if (targetPhase is "all" or "packages")
        {
            await ReconcilePhaseAsync("packages", async () =>
            {
                var obs = await packagesAdapter.ObserveAsync();
                var plan = packagesAdapter.PlanDiff(_desired, obs);
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences)
                {
                    if (!_apply) return;
                    await packagesAdapter.ApplyChangesAsync(plan, _desired);
                    if (_reloadVerify) await packagesAdapter.VerifyAsync(_desired);
                }
            });
        }

        if (targetPhase is "all" or "listing")
        {
            await ReconcilePhaseAsync("listing", async () =>
            {
                var obs = await listingAdapter.ObserveAsync();
                var plan = listingAdapter.PlanDiff(_desired, obs);
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences)
                {
                    if (!_apply) return;
                    await listingAdapter.ApplyChangesAsync(plan, _desired);
                    if (_reloadVerify) await listingAdapter.VerifyAsync(_desired);
                }
            });
        }

        if (targetPhase is "all" or "options")
        {
            await ReconcilePhaseAsync("options", async () =>
            {
                var obs = await optionsAdapter.ObserveAsync();
                var plan = optionsAdapter.PlanDiff(_desired, obs);
                Log("PLAN", plan.ToString());
                if (plan.HasDifferences)
                {
                    if (!_apply) return;
                    await optionsAdapter.ApplyChangesAsync(plan, _desired);
                    if (_reloadVerify) await optionsAdapter.VerifyAsync(_desired);
                }
            });
        }

        if (targetPhase == "all" && _submit && _confirmSubmit && _apply)
        {
            Log("WARN", "--- Starting STORE 7: Explicit Final Submission ---");
            await waiter.NavigateAsync(GetUrl("overview"), "submission overview");
            await native.AssertNoVisibleErrorsAsync();
            await native.ClickStrictAsync([
                "button[data-l10n-key=\"AppSubmission_PublishButton\"]",
                "button[data-l10n-key=\"SubmitToStore\"]",
                "a[data-l10n-key=\"AppSubmission_PublishButton\"]"
            ], "Submit to Store");
            Log("PASS", "Submitted successfully to Microsoft Store review.");
        }
    }

    private async Task EnsureSignedInAsync(CdpClient client, Waiter waiter)
    {
        bool signedIn = await waiter.WaitUntilAsync(async () =>
        {
            string? url = await client.EvaluateAsync<string>("location.href");
            bool hasLogin = Regex.IsMatch(url ?? "", "login\\.microsoftonline|login\\.live\\.com|signin", RegexOptions.IgnoreCase);
            return !hasLogin && url?.Contains("partner.microsoft.com") == true;
        }, timeout: TimeSpan.FromMinutes(15), description: "Wait for user sign-in");

        if (!signedIn)
        {
            throw new TimeoutException("Partner Center session was not established within login timeout.");
        }
        Log("PASS", "Partner Center session is active");
    }

    private void Log(string level, string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(Path.Combine(_logRoot, "edge-store.log"), line + Environment.NewLine);
        }
        catch { }
    }
}
