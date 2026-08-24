using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class PackagesAdapter
{
    private readonly CdpClient _client;
    private readonly DomDriver _dom;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;
    private readonly HeCheckboxAdapter _checkbox;

    public PackagesAdapter(CdpClient client, DomDriver dom, Waiter waiter, NativeFormAdapter native, HeCheckboxAdapter checkbox)
    {
        _client = client;
        _dom = dom;
        _waiter = waiter;
        _native = native;
        _checkbox = checkbox;
    }

    public async Task<ObservedPackages> ObserveAsync()
    {
        await _waiter.WaitUntilAsync(async () =>
        {
            var len = await _client.EvaluateAsync<int>("document.body ? document.body.innerText.length : 0");
            return len > 100;
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for packages page");

        var obs = new ObservedPackages();

        obs.DesktopFamily = await _checkbox.ObserveCheckedAsync("Windows 10/11 Desktop") ?? false;
        obs.MobileFamily = await _checkbox.ObserveCheckedAsync("Windows 10 Mobile") ?? false;
        obs.XboxFamily = await _checkbox.ObserveCheckedAsync("Windows 10/11 Xbox") ?? false;
        obs.TeamFamily = await _checkbox.ObserveCheckedAsync("Windows 10 Team") ?? false;
        obs.MixedRealityFamily = await _checkbox.ObserveCheckedAsync("Windows 10 Mixed Reality") ?? false;
        obs.FutureDeviceFamilies = await _checkbox.ObserveCheckedAsync("future device families") ?? false;

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedPackages observed)
    {
        var plan = new ReconcilePlan { Phase = "packages" };

        if (!string.IsNullOrEmpty(desired.Assets.Msix))
        {
            string fileName = Path.GetFileName(desired.Assets.Msix);
            plan.AddChange("packages.msix", "(check upload)", fileName, $"Upload MSIX package {fileName}");
        }

        if (!observed.DesktopFamily) plan.AddChange("packages.desktop", false, true, "Check Windows 10/11 Desktop");
        if (observed.MobileFamily) plan.AddChange("packages.mobile", true, false, "Uncheck Windows 10 Mobile");
        if (observed.XboxFamily) plan.AddChange("packages.xbox", true, false, "Uncheck Windows 10/11 Xbox");
        if (observed.TeamFamily) plan.AddChange("packages.team", true, false, "Uncheck Windows 10 Team");
        if (observed.MixedRealityFamily) plan.AddChange("packages.mixedReality", true, false, "Uncheck Windows 10 Mixed Reality");

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        if (string.IsNullOrWhiteSpace(desired.Assets.Msix) || !File.Exists(desired.Assets.Msix))
        {
            throw new FileNotFoundException($"MSIX package not found at: {desired.Assets.Msix}");
        }

        string fileName = Path.GetFileName(desired.Assets.Msix);
        bool alreadyPresent = await _client.EvaluateAsync<bool>($$"""
        (() => {
          const t = (document.body && document.body.innerText) || '';
          return t.includes({{System.Text.Json.JsonSerializer.Serialize(fileName)}});
        })()
        """);

        if (!alreadyPresent)
        {
            int rootId = await _dom.GetRootNodeIdAsync();
            var fileInputs = await _dom.QuerySelectorAllAsync(rootId, "input[type=\"file\"]");
            if (fileInputs.Count == 0)
            {
                throw new InvalidOperationException("No file upload input found on packages page.");
            }

            await _dom.SetFileInputFilesAsync(fileInputs[0], [desired.Assets.Msix]);
            await _waiter.WaitForTextAsync(fileName, timeout: TimeSpan.FromMinutes(2));
        }

        await _waiter.WaitForTextAsync("Windows 10/11 Desktop", timeout: TimeSpan.FromSeconds(30));

        await _checkbox.SetCheckedAsync("Windows 10/11 Desktop", true, "desktop device family");
        await _checkbox.SetCheckedAsync("Windows 10 Mobile", false, "mobile device family");
        await _checkbox.SetCheckedAsync("Windows 10/11 Xbox", false, "xbox device family");
        await _checkbox.SetCheckedAsync("Windows 10 Team", false, "team device family");
        await _checkbox.SetCheckedAsync("Windows 10 Mixed Reality", false, "mixed reality device family");
        await _checkbox.SetCheckedAsync("future device families", true, "future device families");

        // Save
        await _native.ClickStrictAsync([
            "button[data-l10n-key=\"AppSubmission_SaveButton\"]",
            "button[data-l10n-key=\"appsubmission_savebutton\"]",
            "button[uitestid=\"saveButtonPackages\"]",
            "input#saveButtonPackages",
            "button#saveButtonPackages",
            "input[value=\"Save\"]",
            "button[value=\"Save\"]",
            "input[value=\"\u4fdd\u5b58\"]",
            "button[value=\"\u4fdd\u5b58\"]"
        ], "Save packages");

        await Task.Delay(2000);
        await _native.AssertNoVisibleErrorsAsync();
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        await _native.AssertNoVisibleErrorsAsync();
    }
}
