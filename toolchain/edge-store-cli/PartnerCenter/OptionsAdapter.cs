using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class OptionsAdapter
{
    private readonly CdpClient _client;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;

    public OptionsAdapter(CdpClient client, Waiter waiter, NativeFormAdapter native)
    {
        _client = client;
        _waiter = waiter;
        _native = native;
    }

    public async Task<ObservedOptions> ObserveAsync()
    {
        await _waiter.WaitUntilAsync(async () =>
        {
            var len = await _client.EvaluateAsync<int>("document.body ? document.body.innerText.length : 0");
            return len > 100;
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for options page");

        var obs = new ObservedOptions();

        obs.PublishMode = await _client.EvaluateAsync<string>("""
        (() => {
          const m = document.querySelector('input#radioReleaseDate_manual');
          if (m && m.checked) return 'Manual';
          const a = document.querySelector('input#radioReleaseDate_asap');
          if (a && a.checked) return 'ASAP';
          return 'Unknown';
        })()
        """) ?? "Unknown";

        obs.HasFullTrustBox = await _client.EvaluateAsync<bool>("""
        (() => {
          return Array.from(document.querySelectorAll('textarea')).some(e => (e.parentElement?.parentElement?.innerText || '').includes('\u4e3a\u4f55\u9700\u8981\u4f7f\u7528'));
        })()
        """);

        obs.FullTrustReasonText = await _client.EvaluateAsync<string>("""
        (() => {
          const els = Array.from(document.querySelectorAll('textarea')).filter(e => (e.parentElement?.parentElement?.innerText || '').includes('\u4e3a\u4f55\u9700\u8981\u4f7f\u7528'));
          return els.length === 1 ? els[0].value : '';
        })()
        """) ?? "";

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedOptions observed)
    {
        var plan = new ReconcilePlan { Phase = "options" };

        if (desired.SubmissionOptions.PublishMode != observed.PublishMode)
        {
            plan.AddChange("options.publishMode", observed.PublishMode, desired.SubmissionOptions.PublishMode, $"Set publish mode to {desired.SubmissionOptions.PublishMode}");
        }

        if (observed.HasFullTrustBox && string.IsNullOrWhiteSpace(observed.FullTrustReasonText))
        {
            plan.AddChange("options.runFullTrustReason", "(empty)", "...", "Fill runFullTrust justification text");
        }

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        if (desired.SubmissionOptions.PublishMode == "Manual")
        {
            await _native.SetRadioAsync("input#radioReleaseDate_manual", "manual publish mode");
        }
        else
        {
            await _native.SetRadioAsync("input#radioReleaseDate_asap", "ASAP publish mode");
        }

        bool hasFullTrust = await _client.EvaluateAsync<bool>("""
        (() => {
          return Array.from(document.querySelectorAll('textarea')).some(e => (e.parentElement?.parentElement?.innerText || '').includes('\u4e3a\u4f55\u9700\u8981\u4f7f\u7528'));
        })()
        """);

        if (hasFullTrust)
        {
            await _client.EvaluateAsync<bool>($$"""
            (() => {
              const needle = '\u4e3a\u4f55\u9700\u8981\u4f7f\u7528';
              const els = Array.from(document.querySelectorAll('textarea')).filter(e => (e.parentElement?.parentElement?.innerText || '').includes(needle));
              if (els.length !== 1) return false;
              const e = els[0];
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
              if (setter && setter.set) setter.set.call(e, {{System.Text.Json.JsonSerializer.Serialize(desired.SubmissionOptions.RunFullTrustReason)}});
              e.dispatchEvent(new Event('input', { bubbles: true }));
              e.dispatchEvent(new Event('change', { bubbles: true }));
              return true;
            })()
            """);
        }

        // Save
        await _native.ClickStrictAsync([
            "button[data-l10n-key=\"optionsSave\"]",
            "button[data-l10n-key=\"appsubmission_savebutton\"]"
        ], "Save options");

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
