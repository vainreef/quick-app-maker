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
        // Wait until either the summary page (redirected, questionnaire done) or the
        // edit questionnaire is on screen. The /ageratings URL redirects to
        // /summary after completion, and that redirect can land AFTER a cold observe.
        await _waiter.RequireAsync(async () =>
        {
            bool summary = await _client.EvaluateAsync<bool>("location.href ? location.href.indexOf('/ageratings/summary') >= 0 : false");
            var hasMode = await _client.EvaluateAsync<bool>("document.querySelector('input[name=\"inputMode\"]') !== null");
            return summary || hasMode;
        }, TimeSpan.FromSeconds(45), "Wait for age ratings questionnaire or completed summary");

        // Re-check for the summary page now that the redirect has settled.
        bool summary = await _client.EvaluateAsync<bool>("""
        (() => {
          const text=(document.body?.innerText||'');
          return /\/ageratings\/summary/i.test(location.href) || /分级\s*ID|当前分级|rating\s*id/i.test(text);
        })()
        """);
        if (summary)
        {
            return new ObservedAgeRatings
            {
                InputMode = "questionnaire",
                ApplicationType = "2558",
                QuestionnaireCompleted = true,
                IsCompleted = true
            };
        }

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

        // A save button being enabled only proves that this questionnaire can be
        // submitted; it is not proof that the product's age-rating module is
        // complete. Completion is assigned only by the summary-page branch above
        // and by the final overview verifier.
        obs.IsCompleted = false;

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedAgeRatings observed)
    {
        var plan = new ReconcilePlan { Phase = "ageRatings" };

        if (observed.IsCompleted) return plan;

        if (observed.InputMode != "questionnaire")
        {
            plan.AddChange("ageRatings.inputMode", observed.InputMode, "questionnaire", "Select IARC questionnaire mode");
        }

        if (observed.ApplicationType != "2558")
        {
            plan.AddChange("ageRatings.applicationType", observed.ApplicationType, "2558", "Select All Other Application Types (2558)");
        }

        // The questionnaire is not complete until the answers are saved. Even when
        // input mode / app type already match, force an apply so the 9 answers,
        // terms and save run (ApplyChangesAsync is idempotent).
        if (!plan.HasDifferences)
        {
            plan.AddChange("ageRatings.complete", "pending", "complete", "Complete IARC questionnaire answers");
        }

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        await _native.SetRadioAsync("input[name=\"inputMode\"][value=\"questionnaire\"]", "IARC questionnaire");
        await _native.SetRadioAsync("input[name=\"question#1109\"][value=\"2558\"]", "other application type 2558");
        await _native.SetRadioAsync("#radioGroup input#noVal, input#noVal", "physical media no");

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
            "button[data-l10n-key=\"AppSubmission_AgeRating_SaveButton\"]",
            "he-button[data-l10n-key=\"appsubmission_agerating_savebutton\"]",
            "button[data-l10n-key=\"appsubmission_agerating_savebutton\"]"
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
          const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
          if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
          return { x: cx, y: cy, width: r.width, height: r.height };
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
        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>($"document.querySelector('input[name=\"question#{questionId}\"]') !== null");
        }, TimeSpan.FromSeconds(15), $"Wait for age rating question #{questionId}");

        var rect = await _client.EvaluateAsync<JsElementRect>($$"""
        (() => {
          const target = {{JsonSerializer.Serialize(answerText)}};
          const radios = Array.from(document.querySelectorAll('input[type="radio"][name="question#{{questionId}}"]')).filter(e => {
            const label = (e.closest('label')?.querySelector('.response-text')?.innerText || e.closest('label')?.innerText || e.value || '').replace(/\s+/g, '').trim();
            return label.includes(target) || e.value === target;
          });
          if (radios.length !== 1) return null;
          const e = radios[0];
          const targetEl = e.closest('label') || e;
          targetEl.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = targetEl.getBoundingClientRect();
          if (r.width <= 0 || r.height <= 0) return null;
          const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
          if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
          return { x: cx, y: cy, width: r.width, height: r.height };
        })()
        """);

        if (rect != null)
        {
            await _input.ClickCoordinatesAsync(rect.X, rect.Y, $"Age question #{questionId} -> {answerText}");
            await Task.Delay(100);
        }
        else
        {
            string diag = await _client.EvaluateAsync<string>($$"""
            (() => {
              const target = {{JsonSerializer.Serialize(answerText)}};
              const radios = Array.from(document.querySelectorAll('input[type="radio"][name="question#{{questionId}}"]'));
              const filtered = radios.filter(e => {
                const label = (e.closest('label')?.querySelector('.response-text')?.innerText || e.closest('label')?.innerText || e.value || '').replace(/\s+/g, '').trim();
                return label.includes(target) || e.value === target;
              });
              const details = radios.map(e => ({val:e.value, label:(e.closest('label')?.querySelector('.response-text')?.innerText||'').replace(/\s+/g,'').trim(), hit: ((e.closest('label')?.querySelector('.response-text')?.innerText||'').replace(/\s+/g,'').trim()).includes(target)}));
              return JSON.stringify({target, count:radios.length, filteredCount:filtered.length, details});
            })()
            """) ?? "{}";
            throw new InvalidOperationException($"Question #{questionId} exists but answer [{answerText}] has no unique visible radio target. Diag={diag}");
        }
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        var plan = PlanDiff(desired, observed);
        if (plan.HasDifferences)
            throw new InvalidOperationException($"Age ratings cold-load verification failed:\n{plan}");
        await _native.AssertNoVisibleErrorsAsync();
    }
}
