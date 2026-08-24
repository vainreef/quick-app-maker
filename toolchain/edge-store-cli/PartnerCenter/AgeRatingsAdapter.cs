using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class AgeRatingsAdapter
{
    private readonly CdpClient _client;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;
    private readonly InputDriver _input;

    public AgeRatingsAdapter(CdpClient client, Waiter waiter, NativeFormAdapter native, InputDriver input)
    {
        _client = client;
        _waiter = waiter;
        _native = native;
        _input = input;
    }

    public async Task<ObservedAgeRatings> ObserveAsync()
    {
        await _waiter.WaitUntilAsync(async () =>
        {
            var hasMode = await _client.EvaluateAsync<bool>("document.querySelector('input[name=\"inputMode\"]') !== null");
            return hasMode;
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for age ratings inputMode");

        var obs = new ObservedAgeRatings();

        obs.InputMode = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('input[name="inputMode"]:checked');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.ApplicationType = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('input[name="question#1109"]:checked');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.IsCompleted = await _client.EvaluateAsync<bool>("""
        (() => {
          const btn = document.querySelector('he-button[data-l10n-key="AppSubmission_AgeRating_SaveButton"], button[data-l10n-key="AppSubmission_AgeRating_SaveButton"]');
          return btn ? !btn.disabled : false;
        })()
        """);

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedAgeRatings observed)
    {
        var plan = new ReconcilePlan { Phase = "ageRatings" };

        if (observed.InputMode != "questionnaire")
        {
            plan.AddChange("ageRatings.inputMode", observed.InputMode, "questionnaire", "Select IARC questionnaire mode");
        }

        if (observed.ApplicationType != "2558")
        {
            plan.AddChange("ageRatings.applicationType", observed.ApplicationType, "2558", "Select All Other Application Types (2558)");
        }

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        await _native.SetRadioAsync("input[name=\"inputMode\"][value=\"questionnaire\"]", "IARC questionnaire");
        await _native.SetRadioAsync("input[name=\"question#1109\"][value=\"2558\"]", "other application type 2558");
        await _native.SetRadioAsync("#radioGroup input#noVal", "physical media no");

        var questionIds = new[] { "1152", "1188", "1193", "1037", "1194", "1195", "1375", "1196", "1197" };
        foreach (var qid in questionIds)
        {
            await SetAgeAnswerAsync(qid, "否");
        }

        // Agree terms
        await _native.ClickStrictAsync(["he-checkbox[required]", "he-checkbox"], "IARC terms agreement");

        // Save draft
        await _native.ClickStrictAsync([
            "he-button[data-l10n-key=\"AppSubmission_AgeRating_SaveButton\"]",
            "button[data-l10n-key=\"AppSubmission_AgeRating_SaveButton\"]"
        ], "Age ratings preview save");

        await Task.Delay(2500);

        // Click Continue
        var contRect = await _client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button, a, he-button, span')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return (e.innerText || '').trim() === '\u7ee7\u7eed' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
          });
          if (els.length === 0) return null;
          const r = els[0].getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);

        if (contRect != null)
        {
            await _input.ClickCoordinatesAsync(contRect.X, contRect.Y, "Continue after age ratings");
            await Task.Delay(1500);
        }

        await _native.AssertNoVisibleErrorsAsync();
    }

    private async Task SetAgeAnswerAsync(string questionId, string answerText)
    {
        await _waiter.WaitUntilAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>($"document.querySelector('[role=\"radiogroup\"][aria-labelledby=\"question#{questionId}\"]') !== null");
        }, timeout: TimeSpan.FromSeconds(15), description: $"Wait for age rating question #{questionId}");

        var rect = await _client.EvaluateAsync<JsElementRect>($$"""
        (() => {
          const group = document.querySelector('[role="radiogroup"][aria-labelledby="question#{{questionId}}"]');
          if (!group) return null;
          const target = {{JsonSerializer.Serialize(answerText)}};
          const radios = Array.from(group.querySelectorAll('input[type="radio"]')).filter(e => {
            const label = e.parentElement?.innerText || e.closest('label')?.innerText || '';
            return label.includes(target) || e.value === target;
          });
          if (radios.length !== 1) return null;
          const r = radios[0].getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);

        if (rect != null)
        {
            await _input.ClickCoordinatesAsync(rect.X, rect.Y, $"Age question #{questionId} -> {answerText}");
            await Task.Delay(100);
        }
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _native.AssertNoVisibleErrorsAsync();
    }
}
