using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public sealed class PackagesAdapter
{
    private readonly CdpClient _client;
    private readonly DomDriver _dom;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;
    private readonly HeCheckboxAdapter _checkbox;

    public PackagesAdapter(CdpClient client, DomDriver dom, Waiter waiter, NativeFormAdapter native, HeCheckboxAdapter checkbox)
    {
        _client = client; _dom = dom; _waiter = waiter; _native = native; _checkbox = checkbox;
    }

    public async Task<ObservedPackages> ObserveAsync()
    {
        await _waiter.RequireAsync(async () =>
        {
            string url = await _client.EvaluateAsync<string>("location.href") ?? "";
            int len = await _client.EvaluateAsync<int>("document.body?.innerText.length || 0");
            return url.Contains("/packages", StringComparison.OrdinalIgnoreCase) && len > 100;
        }, TimeSpan.FromSeconds(90), "Wait for packages page state");

        var entries = await ObserveEntriesOnlyAsync();
        return new ObservedPackages
        {
            Entries = entries,
            UploadedPackageNames = entries.Select(x => x.FileName).ToList(),
            DesktopFamily = await _checkbox.ObserveCheckedAsync("Windows 10/11 Desktop") ?? false,
            MobileFamily = await _checkbox.ObserveCheckedAsync("Windows 10 Mobile") ?? false,
            XboxFamily = await _checkbox.ObserveCheckedAsync("Windows 10/11 Xbox") ?? false,
            TeamFamily = await _checkbox.ObserveCheckedAsync("Windows 10 Team") ?? false,
            MixedRealityFamily = await _checkbox.ObserveCheckedAsync("Windows 10 Mixed Reality") ?? false,
            FutureDeviceFamilies = await _checkbox.ObserveCheckedAsync("future device families") ?? false
        };
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedPackages observed)
    {
        var plan = new ReconcilePlan { Phase = "packages" };
        string fileName = Path.GetFileName(desired.Assets.Msix);
        var same = observed.Entries.Where(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase)).ToList();

        if (same.Count == 0)
            plan.AddChange("packages.msix", "(absent)", fileName, "Upload package once");
        else if (same.Count != 1 || same.Any(x => x.IsError))
            plan.AddChange("packages.conflict", string.Join(",", same.Select(x => x.Status)), "one Validated row", "Duplicate/error package rows require cleanup before any upload");
        else if (!same[0].IsValidated)
            plan.AddChange("packages.processing", same[0].Status, "Validated", "Wait for server validation; filename visibility is not completion");

        if (!observed.DesktopFamily) plan.AddChange("packages.desktop", false, true, "Check Windows 10/11 Desktop");
        if (observed.MobileFamily) plan.AddChange("packages.mobile", true, false, "Uncheck Windows 10 Mobile");
        if (observed.XboxFamily) plan.AddChange("packages.xbox", true, false, "Uncheck Windows 10/11 Xbox");
        if (observed.TeamFamily) plan.AddChange("packages.team", true, false, "Uncheck Windows 10 Team");
        if (observed.MixedRealityFamily) plan.AddChange("packages.mixedReality", true, false, "Uncheck Windows 10 Mixed Reality");
        if (!observed.FutureDeviceFamilies) plan.AddChange("packages.future", false, true, "Check future device families");
        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        if (string.IsNullOrWhiteSpace(desired.Assets.Msix) || !File.Exists(desired.Assets.Msix))
            throw new FileNotFoundException($"MSIX package not found: {desired.Assets.Msix}");
        if (plan.Actions.Any(a => a.Field == "packages.conflict"))
            throw new InvalidOperationException("Package upload blocked: the same filename already has duplicate/error rows. No new upload was attempted.");

        string fileName = Path.GetFileName(desired.Assets.Msix);
        if (plan.Actions.Any(a => a.Field == "packages.msix"))
        {
            // Query the live control at the moment of use. The upload input is a
            // hidden native input (often inside a shadow component); resolve its
            // objectId by shadow-piercing Runtime.evaluate and set files via
            // objectId (DOM.requestNode can fail on these hidden/shadow inputs).
            string? fileInputObj = null;
            var inputDeadline = DateTime.UtcNow.AddSeconds(40);
            while (DateTime.UtcNow < inputDeadline)
            {
                fileInputObj = await _dom.GetObjectIdByExpressionAsync("""
                (() => {
                  const roots=[document];
                  for(let i=0;i<roots.length;i++){ try{ for(const e of roots[i].querySelectorAll('*')) if(e.shadowRoot) roots.push(e.shadowRoot); }catch(_){} }
                  for(const r of roots){ const f=Array.from(r.querySelectorAll('input[type="file"]')).find(e => !e.disabled); if(f) return f; }
                  return null;
                })()
                """);
                if (!string.IsNullOrWhiteSpace(fileInputObj)) break;
                await Task.Delay(1000);
            }
            if (string.IsNullOrWhiteSpace(fileInputObj)) throw new InvalidOperationException("No live package file input is present.");
            await _dom.SetFileInputFilesByObjectIdAsync(fileInputObj, [desired.Assets.Msix]);
        }

        await _waiter.RequireAsync(async () =>
        {
            var current = await ObserveEntriesOnlyAsync();
            var same = current.Where(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (same.Any(x => x.IsError)) throw new InvalidOperationException($"Package validation returned Error for {fileName}.");
            return same.Count == 1 && same[0].IsValidated;
        }, TimeSpan.FromMinutes(12), $"Wait for package {fileName} to reach Validated");

        await _checkbox.SetCheckedAsync("Windows 10/11 Desktop", true, "desktop device family");
        await _checkbox.SetCheckedAsync("Windows 10 Mobile", false, "mobile device family");
        await _checkbox.SetCheckedAsync("Windows 10/11 Xbox", false, "xbox device family");
        await _checkbox.SetCheckedAsync("Windows 10 Team", false, "team device family");
        await _checkbox.SetCheckedAsync("Windows 10 Mixed Reality", false, "mixed reality device family");
        await _checkbox.SetCheckedAsync("future device families", true, "future device families");

        await _native.ClickStrictAsync([
            "button[name=\"save_button\"]", "button[data-l10n-key=\"AppSubmission_SaveButton\"]",
            "button[uitestid=\"saveButtonPackages\"]", "input#saveButtonPackages", "button#saveButtonPackages"
        ], "Save packages");
        await _native.AssertNoVisibleErrorsAsync();
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        var plan = PlanDiff(desired, observed);
        if (plan.HasDifferences) throw new InvalidOperationException($"Packages cold-load verification failed:\n{plan}");
        await _native.AssertNoVisibleErrorsAsync();
    }

    private async Task<List<PackageEntry>> ObserveEntriesOnlyAsync()
    {
        return await _client.EvaluateAsync<List<PackageEntry>>("""
        (() => {
          const roots=[document], rows=[], seen=new Set();
          while(roots.length){
            const root=roots.shift();
            for(const e of root.querySelectorAll('*')) if(e.shadowRoot) roots.push(e.shadowRoot);
            for(const e of root.querySelectorAll('tr,[role="row"],.package-row,[class*="package-item"]')){
              const text=(e.innerText||'').trim();
              if(/\.(msix|msixbundle|appx|appxbundle)\b/i.test(text) && !seen.has(text)){seen.add(text);rows.push(text);}
            }
          }
          if(!rows.length){
            const text=document.body?.innerText||'';
            for(const line of text.split(/\n+/)) if(/\.(msix|msixbundle|appx|appxbundle)\b/i.test(line)) rows.push(line.trim());
          }
          return rows.map(text => {
            const m=text.match(/[^\\/:*?"<>|\r\n]+\.(?:msixbundle|appxbundle|msix|appx)\b/i);
            const status=/validated|已验证/i.test(text)?'Validated':/error|错误|失败/i.test(text)?'Error':/upload|processing|验证中|正在/i.test(text)?'Processing':'Unknown';
            return {fileName:m?m[0].trim():'',status};
          }).filter(x=>x.fileName);
        })()
        """) ?? [];
    }
}
