using System.Text.Json;
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

        await Task.Delay(1500);

        var obs = new ObservedProperties();

        obs.Category = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('select[name="CategorySelect"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.PrivacyAnswer = await _client.EvaluateAsync<string>("""
        (() => {
          const sel = document.querySelector('select[name="privacyPolicySelection"]');
          if (sel) {
            const v = (sel.value || '').trim();
            if (['yes', 'true', '1'].includes(v.toLowerCase())) return 'Yes';
            if (['no', 'false', '0'].includes(v.toLowerCase())) return 'No';
            const opt = sel.selectedOptions && sel.selectedOptions[0] ? sel.selectedOptions[0].text : '';
            if (opt.includes('不使用') || opt.includes('否')) return 'No';
            if (opt.includes('使用个人信息') || opt.includes('是')) return 'Yes';
            return v;
          }
          const r = document.querySelector('input[name="privacyPolicySelection"]:checked');
          if (r) return r.id === 'privacyPolicyURL' ? 'Yes' : 'No';
          return '';
        })()
        """) ?? "";

        obs.PrivacyPolicyText = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('support-info textarea, textarea[aria-label*="隐私" i], textarea[aria-label*="Privacy" i], textarea');
          return e ? e.value : '';
        })()
        """) ?? "";
        obs.PrivacyPolicyUrl = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('input[type="url"], #privacyPolicyUrl, input[aria-label*="URL" i], input[placeholder*="URL" i]');
          return e ? e.value : '';
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

        if (!string.IsNullOrWhiteSpace(desired.Properties.PrivacyPolicyText))
        {
            if (observed.PrivacyPolicyText.Trim() != desired.Properties.PrivacyPolicyText.Trim())
            {
                plan.AddChange("properties.privacyPolicyText", observed.PrivacyPolicyText.Length > 20 ? observed.PrivacyPolicyText[..20] + "..." : observed.PrivacyPolicyText, desired.Properties.PrivacyPolicyText.Length > 20 ? desired.Properties.PrivacyPolicyText[..20] + "..." : desired.Properties.PrivacyPolicyText, "Set privacy policy text");
            }
        }
        else if (!string.IsNullOrWhiteSpace(desired.Properties.PrivacyPolicyUrl))
        {
            if (observed.PrivacyPolicyUrl.Trim() != desired.Properties.PrivacyPolicyUrl.Trim())
            {
                plan.AddChange("properties.privacyPolicyUrl", observed.PrivacyPolicyUrl, desired.Properties.PrivacyPolicyUrl, "Set privacy policy URL");
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
            await _client.EvaluateAsync<object>($$"""
            (() => {
              const sel = document.querySelector('select[name="CategorySelect"]');
              if (sel) {
                const target = '{{desired.Properties.Category}}';
                const opt = Array.from(sel.options).find(o => o.value.toLowerCase() === target.toLowerCase() || o.text.includes(target) || (o.value === 'Productivity' && o.text.includes('生产率')));
                if (opt) {
                  sel.value = opt.value;
                  sel.dispatchEvent(new Event('change', { bubbles: true }));
                  sel.dispatchEvent(new Event('input', { bubbles: true }));
                }
              }
              return null;
            })()
            """);
        }

        if (plan.Actions.Any(a => a.Field == "properties.privacy"))
        {
            await _client.EvaluateAsync<object>("""
            (() => {
              const sel = document.querySelector('select[name="privacyPolicySelection"]');
              if (sel) {
                const opt = Array.from(sel.options).find(o => o.value === 'Yes' || o.text.includes('使用个人信息') || o.text.includes('是'));
                if (opt) {
                  sel.value = opt.value;
                  sel.dispatchEvent(new Event('change', { bubbles: true }));
                  sel.dispatchEvent(new Event('input', { bubbles: true }));
                }
              }
              return null;
            })()
            """);
        }

        if (!string.IsNullOrWhiteSpace(desired.Properties.PrivacyPolicyText))
        {
            await _client.EvaluateAsync<object>("""
            (() => {
                const r = document.querySelector('#privacyPolicyText, input[type="radio"][id*="Text" i]');
                if (r) { r.click(); r.dispatchEvent(new Event('change', {bubbles: true})); }
                return null;
            })()
            """);
            await Task.Delay(500);
            await _client.EvaluateAsync<object>($$"""
            (() => {
                const ta = document.querySelector('support-info textarea, textarea[aria-label*="隐私" i], textarea[aria-label*="Privacy" i], textarea');
                if (ta) {
                    ta.value = {{JsonSerializer.Serialize(desired.Properties.PrivacyPolicyText)}};
                    ta.dispatchEvent(new Event('change', {bubbles: true}));
                    ta.dispatchEvent(new Event('input', {bubbles: true}));
                }
                return null;
            })()
            """);
        }
        else if (!string.IsNullOrWhiteSpace(desired.Properties.PrivacyPolicyUrl))
        {
            await _client.EvaluateAsync<object>("""
            (() => {
                const r = document.querySelector('#privacyPolicyURL, input[type="radio"][id*="URL" i]');
                if (r) { r.click(); r.dispatchEvent(new Event('change', {bubbles: true})); }
                return null;
            })()
            """);
            await Task.Delay(500);
            await _client.EvaluateAsync<object>($$"""
            (() => {
                const inp = document.querySelector('input[type="url"], #privacyPolicyUrl, input[aria-label*="URL" i], input[placeholder*="URL" i]');
                if (inp) {
                    inp.value = {{JsonSerializer.Serialize(desired.Properties.PrivacyPolicyUrl)}};
                    inp.dispatchEvent(new Event('change', {bubbles: true}));
                    inp.dispatchEvent(new Event('input', {bubbles: true}));
                }
                return null;
            })()
            """);
        }

        await _checkbox.SetCheckedAsync("storage-checkbox", true, "storage declaration");
        await _checkbox.SetCheckedAsync("backups-checkbox", true, "backups declaration");
        await _checkbox.SetCheckedAsync("windows-checkbox", true, "windows declaration");
        await _checkbox.SetCheckedAsync("usesGenAI-checkbox", false, "usesGenAI declaration");

        // Save
        await _native.ClickStrictAsync([
            "input[type=\"button\"][value=\"保存\"]",
            "input[type=\"button\"][value=\"Save\"]",
            "input.btn-primary[value=\"保存\"]",
            "input.btn-primary[value=\"Save\"]",
            "button[name=\"save_button\"]",
            "button[data-l10n-key=\"AppSubmission_SaveButton\"]",
            "button[data-l10n-key=\"appsubmission_savebutton\"]",
            "button[uitestid=\"saveButtonProperties\"]",
            "input#saveButtonProperties",
            "button#saveButtonProperties"
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
