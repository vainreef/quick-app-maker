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
        await _waiter.WaitUntilAsync(async () =>
        {
            var hasCat = await _client.EvaluateAsync<bool>("document.querySelector('select[name=\"CategorySelect\"]') !== null");
            return hasCat;
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for properties CategorySelect");

        var obs = new ObservedProperties();

        obs.Category = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('select[name="CategorySelect"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.PrivacyAnswer = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('select[name="privacyPolicySelection"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.PrivacyPolicyText = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('support-info textarea, textarea[aria-label="提供隐私策略文本"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.StorageDeclaration = await _checkbox.ObserveCheckedAsync("storage") ?? false;
        obs.BackupsDeclaration = await _checkbox.ObserveCheckedAsync("backups") ?? false;
        obs.WindowsDeclaration = await _checkbox.ObserveCheckedAsync("windows") ?? false;
        obs.UsesGenAi = await _checkbox.ObserveCheckedAsync("usesGenAI") ?? false;

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

        if (desired.Properties.Privacy == "No" && !string.IsNullOrEmpty(desired.Properties.PrivacyPolicyText))
        {
            if (observed.PrivacyPolicyText.Trim() != desired.Properties.PrivacyPolicyText.Trim())
            {
                plan.AddChange("properties.privacyPolicyText", observed.PrivacyPolicyText.Length > 20 ? observed.PrivacyPolicyText[..20] + "..." : observed.PrivacyPolicyText, "...", "Update privacy policy text");
            }
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

        if (desired.Properties.Privacy == "No")
        {
            await _waiter.WaitUntilAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("document.querySelector('#privacyPolicyText') !== null");
            }, timeout: TimeSpan.FromSeconds(10), description: "Wait for #privacyPolicyText radio");

            await _native.SetRadioAsync("#privacyPolicyText", "provide privacy policy text");

            await _waiter.WaitUntilAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("document.querySelector('support-info textarea, textarea[aria-label=\"提供隐私策略文本\"]') !== null");
            }, timeout: TimeSpan.FromSeconds(10), description: "Wait for privacy policy textarea");

            await _native.SetFieldAsync([
                "support-info textarea[aria-label=\"提供隐私策略文本\"]",
                "textarea[aria-label=\"提供隐私策略文本\"]",
                "support-info textarea"
            ], desired.Properties.PrivacyPolicyText, "privacy policy text");
        }

        await _checkbox.SetCheckedAsync("storage", true, "storage declaration");
        await _checkbox.SetCheckedAsync("backups", true, "backups declaration");
        await _checkbox.SetCheckedAsync("windows", true, "windows declaration");
        await _checkbox.SetCheckedAsync("usesGenAI", false, "usesGenAI declaration");

        // Save
        await _native.ClickStrictAsync([
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
