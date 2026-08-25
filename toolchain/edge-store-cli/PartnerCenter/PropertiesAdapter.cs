using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class PropertiesAdapter
{
    private readonly CdpClient _client;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;
    private readonly HeCheckboxAdapter _checkbox;

    public PropertiesAdapter(CdpClient client, Waiter waiter, NativeFormAdapter native, HeCheckboxAdapter checkbox)
    {
        _client = client;
        _waiter = waiter;
        _native = native;
        _checkbox = checkbox;
    }

    public async Task<ObservedProperties> ObserveAsync()
    {
        await _waiter.RequireAsync(async () =>
        {
            var hasCat = await _client.EvaluateAsync<bool>("document.querySelector('select[name=\"CategorySelect\"]') !== null");
            return hasCat;
        }, TimeSpan.FromSeconds(60), "Wait for properties CategorySelect");

        var obs = new ObservedProperties();

        obs.Category = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('select[name="CategorySelect"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.PrivacyAnswer = await _client.EvaluateAsync<string>("""
        (() => {
          const r = document.querySelector('input[name="privacyPolicySelection"]:checked');
          if (r) return r.id === 'privacyPolicyURL' ? 'Yes' : 'No';
          const e = document.querySelector('select[name="privacyPolicySelection"]');
          if (!e) return '';
          const v=(e.value||'').trim().toLowerCase();
          if (['yes','true','1','url'].includes(v)) return 'Yes';
          if (['no','false','0','none'].includes(v)) return 'No';
          return e.value;
        })()
        """) ?? "";

        obs.PrivacyPolicyText = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('support-info textarea, textarea[aria-label="提供隐私策略文本"]');
          return e ? e.value : '';
        })()
        """) ?? "";
        obs.PrivacyPolicyUrl = await _client.EvaluateAsync<string>("""
        (() => {
          const e=document.querySelector('input[type="url"],#privacyPolicyUrl,input[name*="privacyPolicyUrl" i]');
          return e?.value || '';
        })()
        """) ?? "";
        obs.HasPrivacyTextChoice = await _client.EvaluateAsync<bool>("document.querySelector('#privacyPolicyText') !== null");

        obs.StorageDeclaration = await _checkbox.ObserveCheckedAsync("storage-checkbox") ?? false;
        obs.BackupsDeclaration = await _checkbox.ObserveCheckedAsync("backups-checkbox") ?? false;
        obs.WindowsDeclaration = await _checkbox.ObserveCheckedAsync("windows-checkbox") ?? false;
        obs.UsesGenAi = await _checkbox.ObserveCheckedAsync("usesGenAI-checkbox") ?? false;

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedProperties observed)
    {
        var plan = new ReconcilePlan { Phase = "properties" };

        if (!string.IsNullOrEmpty(desired.Properties.Category) && observed.Category != desired.Properties.Category)
        {
            plan.AddChange("properties.category", observed.Category, desired.Properties.Category, $"Set category to {desired.Properties.Category}");
        }

        if (!string.IsNullOrEmpty(desired.Properties.Privacy) && observed.PrivacyAnswer != desired.Properties.Privacy)
        {
            plan.AddChange("properties.privacy", observed.PrivacyAnswer, desired.Properties.Privacy, $"Set privacy answer to {desired.Properties.Privacy}");
        }

        // Privacy policy text/URL fields ONLY exist when privacy answer is "Yes"
        // (uses personal info). When "No", there is no extra field below and none
        // may be touched.
        if (desired.Properties.Privacy == "Yes")
        {
            if (observed.HasPrivacyTextChoice && observed.PrivacyPolicyText.Trim() != desired.Properties.PrivacyPolicyText.Trim())
                plan.AddChange("properties.privacyPolicyText", observed.PrivacyPolicyText.Length > 20 ? observed.PrivacyPolicyText[..20] + "..." : observed.PrivacyPolicyText, "...", "Update privacy policy text");
            if (observed.PrivacyPolicyUrl.Trim() != desired.Properties.PrivacyPolicyUrl.Trim())
                plan.AddChange("properties.privacyPolicyUrl", observed.PrivacyPolicyUrl, desired.Properties.PrivacyPolicyUrl, "Set privacy policy URL");
        }

        if (!observed.StorageDeclaration) plan.AddChange("properties.storage", false, true, "Check storage declaration");
        if (!observed.BackupsDeclaration) plan.AddChange("properties.backups", false, true, "Check backups declaration");
        if (!observed.WindowsDeclaration) plan.AddChange("properties.windows", false, true, "Check windows declaration");
        if (observed.UsesGenAi) plan.AddChange("properties.usesGenAI", true, false, "Uncheck usesGenAI declaration");

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        if (plan.Actions.Any(a => a.Field == "properties.category"))
        {
            await _native.SetFieldAsync(["select[name=\"CategorySelect\"]"], desired.Properties.Category, "category");
        }

        if (plan.Actions.Any(a => a.Field == "properties.privacy"))
        {
            await _native.SetFieldAsync(["select[name=\"privacyPolicySelection\"]"], desired.Properties.Privacy, "privacy answer");
        }

        if (desired.Properties.Privacy == "No" && plan.Actions.Any(a => a.Field == "properties.privacyPolicyText"))
        {
            bool legacyShape = await _client.EvaluateAsync<bool>("document.querySelector('#privacyPolicyText') !== null");
            if (!legacyShape)
                throw new InvalidOperationException("The current privacy UI has no privacy-text choice. Desired-state validation must not require privacyPolicyText when privacy=No.");

            await _waiter.RequireAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("document.querySelector('#privacyPolicyText') !== null");
            }, TimeSpan.FromSeconds(10), "Wait for #privacyPolicyText radio");

            await _native.SetRadioAsync("#privacyPolicyText", "provide privacy policy text");

            await _waiter.RequireAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("document.querySelector('support-info textarea, textarea[aria-label=\"提供隐私策略文本\"]') !== null");
            }, TimeSpan.FromSeconds(10), "Wait for privacy policy textarea");

            await _native.SetFieldAsync([
                "support-info textarea[aria-label=\"提供隐私策略文本\"]",
                "textarea[aria-label=\"提供隐私策略文本\"]",
                "support-info textarea"
            ], desired.Properties.PrivacyPolicyText, "privacy policy text");
        }

        if (desired.Properties.Privacy == "Yes" && plan.Actions.Any(a => a.Field == "properties.privacyPolicyUrl"))
        {
            await _waiter.RequireAsync(async () => await _client.EvaluateAsync<bool>("document.querySelector('input[type=\"url\"],#privacyPolicyUrl,input[name*=\"privacyPolicyUrl\" i]') !== null"),
                TimeSpan.FromSeconds(15), "Wait for privacy policy URL field");
            await _native.SetFieldAsync(["input[type=\"url\"]", "#privacyPolicyUrl", "input[name*=\"privacyPolicyUrl\" i]"], desired.Properties.PrivacyPolicyUrl, "privacy policy URL");
        }

        await _checkbox.SetCheckedAsync("storage-checkbox", true, "storage declaration");
        await _checkbox.SetCheckedAsync("backups-checkbox", true, "backups declaration");
        await _checkbox.SetCheckedAsync("windows-checkbox", true, "windows declaration");
        await _checkbox.SetCheckedAsync("usesGenAI-checkbox", false, "usesGenAI declaration");

        // Save
        await _native.ClickStrictAsync([
            "button[name=\"save_button\"]",
            "button[data-l10n-key=\"AppSubmission_SaveButton\"]",
            "button[data-l10n-key=\"appsubmission_savebutton\"]",
            "button[uitestid=\"saveButtonProperties\"]",
            "input#saveButtonProperties",
            "button#saveButtonProperties",
            "input[value=\"\u4fdd\u5b58\"]",
            "button[value=\"\u4fdd\u5b58\"]"
        ], "Save properties");

        await Task.Delay(2000);
        await _native.AssertNoVisibleErrorsAsync();
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        var plan = PlanDiff(desired, observed);
        if (plan.HasDifferences)
        {
            throw new InvalidOperationException($"Properties reload verification failed to converge:\n{plan}");
        }
        await _native.AssertNoVisibleErrorsAsync();
    }
}
