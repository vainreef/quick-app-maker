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
        bool isQuestionnaire = await _client.EvaluateAsync<bool>("""
        (() => {
          return document.querySelector('input[name="inputMode"], input[name="question#1109"], input#noVal') !== null;
        })()
        """);

        if (!isQuestionnaire)
        {
            bool summary = await _client.EvaluateAsync<bool>("""
            (() => {
              const text = (document.body?.innerText || '');
              if (text.includes('更新了年龄分级') || text.includes('回答其他问题') || text.includes('未完成') || text.includes('Incomplete')) return false;
              return /\/ageratings\/summary/i.test(location.href) || document.querySelector('age-rating-summary, .rating-summary-table') !== null;
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
        // 1. 如果在 summary 页面上，先尝试点击编辑/修改调查表/重新生成按钮
        await _client.EvaluateAsync<bool>("""
        (() => {
            const editBtns = Array.from(document.querySelectorAll('a, button, he-button')).filter(e => {
                const t = (e.innerText || e.getAttribute('aria-label') || '').trim();
                return /^(修改|编辑|重新生成|继续|重新开始|Edit|Update|Retake)$/i.test(t);
            });
            for (const b of editBtns) {
                b.click();
                b.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
                return true;
            }
            return false;
        })()
        """);
        await Task.Delay(1000);

        // 2. 选择 IARC 问卷模式
        Console.WriteLine("[INFO] 选择 IARC 调查表模式...");
        await _client.EvaluateAsync<bool>("""
        (() => {
            const r = document.querySelector('input[name="inputMode"][value="questionnaire"]');
            if (r) {
                (r.closest('label') || r).click();
                r.click();
                r.checked = true;
                r.dispatchEvent(new Event('input', { bubbles: true }));
                r.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
            }
            return false;
        })()
        """);
        await Task.Delay(1000);

        // 3. 选择【其他所有应用类型 (2558)】
        Console.WriteLine("[INFO] 选择应用类型：其他所有应用类型 (2558)...");
        await _client.EvaluateAsync<bool>("""
        (() => {
            const r = document.querySelector('input[name="question#1109"][value="2558"]');
            if (r) {
                (r.closest('label') || r).click();
                r.click();
                r.checked = true;
                r.dispatchEvent(new Event('input', { bubbles: true }));
                r.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
            }
            return false;
        })()
        """);
        await Task.Delay(1500);

        // 4. 遍历并回答页面上所有的 div[role="radiogroup"] 子问题，全部勾选【否】
        Console.WriteLine("[INFO] 正在精准遍历所有子问题组并全选【否】...");
        int totalAnswered = await _client.EvaluateAsync<int>("""
        (() => {
            let count = 0;
            const radiogroups = Array.from(document.querySelectorAll('div[role="radiogroup"]'));
            for (const rg of radiogroups) {
                const radios = Array.from(rg.querySelectorAll('input[type="radio"]'));
                if (radios.length === 0) continue;

                // 优先找带有“否”字样的 radio，或者取最后一项（通常为“否”）
                const noRadio = radios.find(r => {
                    const parentText = (r.closest('label')?.innerText || r.closest('.radio')?.innerText || '').trim();
                    return parentText.includes('否') || parentText.toLowerCase().includes('no') || r.id === 'noVal';
                }) || (radios.length >= 2 ? radios[radios.length - 1] : null);

                if (noRadio) {
                    (noRadio.closest('label') || noRadio).click();
                    noRadio.click();
                    noRadio.checked = true;
                    noRadio.dispatchEvent(new Event('input', { bubbles: true }));
                    noRadio.dispatchEvent(new Event('change', { bubbles: true }));
                    count++;
                }
            }
            return count;
        })()
        """);
        Console.WriteLine($"[PASS] 成功勾选了 {totalAnswered} 个子题目的【否】选项。");
        await Task.Delay(1000);

        // 5. 物理介质选“否”
        Console.WriteLine("[INFO] 选择物理介质分发：否 (#noVal)...");
        await _client.EvaluateAsync<bool>("""
        (() => {
            const r = document.querySelector('#radioGroup input#noVal, input#noVal');
            if (r) {
                (r.closest('label') || r).click();
                r.click();
                r.checked = true;
                r.dispatchEvent(new Event('input', { bubbles: true }));
                r.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
            }
            return false;
        })()
        """);
        await Task.Delay(1000);

        // 6. 检查页面上是否还有任何未完成指示器
        int remainingIncomplete = await _client.EvaluateAsync<int>("""
        (() => {
            const badges = Array.from(document.querySelectorAll('.win-color-fg-yellow, [data-l10n-key="AppSubmission_AppProperty_QuestionIncomplete"]'))
                .filter(e => (e.innerText || '').includes('未完成') && e.getBoundingClientRect().width > 0);
            return badges.length;
        })()
        """);
        Console.WriteLine($"[STATUS] 现场黄色【未完成】剩余数量: {remainingIncomplete}");

        string currentUrl = await _client.EvaluateAsync<string>("location.href") ?? "";
        bool onSummary = currentUrl.Contains("summary", StringComparison.OrdinalIgnoreCase) || await _client.EvaluateAsync<bool>("document.querySelector('summary-page, rating-table') !== null");
        if (onSummary)
        {
            await HandleSummaryPageAsync();
            return;
        }

        // 7. 点击【预览分级】主要按钮
        Console.WriteLine("[INFO] 正在点击【预览分级】以生成 IARC 分级证书...");
        bool clickedPreview = await _client.EvaluateAsync<bool>("""
        (() => {
            const allRoots = [document];
            for (let i = 0; i < allRoots.length; i++) {
                try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
            }

            for (const r of allRoots) {
                // 优先点击 预览分级 (AppSubmission_AgeRating_PreviewRatingsButton)，其次点击 保存/Save
                const btns = Array.from(r.querySelectorAll('he-button, button, input[type="button"], input[type="submit"]')).filter(e => {
                    const t = (e.innerText || e.value || e.getAttribute('aria-label') || '').trim();
                    const key = (e.getAttribute('data-l10n-key') || '').toLowerCase();
                    const isPreview = t.includes('预览') || t.toLowerCase().includes('preview') || key.includes('preview');
                    const isSave = t.includes('保存') || t.toLowerCase().includes('save') || key.includes('save');
                    const rect = e.getBoundingClientRect();
                    return (isPreview || isSave) && rect.width > 0 && rect.height > 0 && !e.disabled;
                });

                // 优先取 appearance="primary" 或包含“预览”的按钮
                btns.sort((a, b) => {
                    const aPri = (a.getAttribute('appearance') === 'primary' || (a.innerText || '').includes('预览')) ? 1 : 0;
                    const bPri = (b.getAttribute('appearance') === 'primary' || (b.innerText || '').includes('预览')) ? 1 : 0;
                    return bPri - aPri;
                });

                for (const b of btns) {
                    b.scrollIntoView({ block: 'center' });
                    if (b.shadowRoot) {
                        const inner = b.shadowRoot.querySelector('button');
                        if (inner) inner.click();
                    }
                    b.click();
                    b.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
                    return true;
                }
            }
            return false;
        })()
        """);

        if (clickedPreview)
        {
            Console.WriteLine("[PASS] 成功点击【预览分级】按钮！");
        }
        else
        {
            Console.WriteLine("[WARN] 未找到可用的【预览分级/保存】按钮。");
        }

        await Task.Delay(5000);

        // 8. 生成分级证书后，进入摘要页处理条款勾选与最终保存
        await HandleSummaryPageAsync();
        await _native.AssertNoVisibleErrorsAsync();
    }

    private async Task<bool> HandleSummaryPageAsync()
    {
        Console.WriteLine("[INFO] 正在处理分级证书摘要页 (summary-page)...");
        // 1. 勾选条款 checkbox (he-checkbox)
        bool checkboxChecked = await _client.EvaluateAsync<bool>("""
        (() => {
            const allRoots = [document];
            for (let i = 0; i < allRoots.length; i++) {
                try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
            }
            for (const r of allRoots) {
                const cbs = Array.from(r.querySelectorAll('he-checkbox, input[type="checkbox"]'));
                for (const cb of cbs) {
                    if (cb.shadowRoot) {
                        const inner = cb.shadowRoot.querySelector('input[type="checkbox"]');
                        if (inner && !inner.checked) {
                            inner.click();
                            inner.checked = true;
                            inner.dispatchEvent(new Event('input', { bubbles: true }));
                            inner.dispatchEvent(new Event('change', { bubbles: true }));
                        }
                    }
                    if (!cb.checked) {
                        cb.click();
                        cb.checked = true;
                        cb.dispatchEvent(new Event('input', { bubbles: true }));
                        cb.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    return true;
                }
            }
            return false;
        })()
        """);

        if (checkboxChecked) Console.WriteLine("[PASS] 成功勾选 IARC 使用条款复选框！");
        await Task.Delay(1500);

        // 2. 点击解锁的【保存】按钮 (AppSubmission_AgeRating_SaveButton)
        bool saved = await _client.EvaluateAsync<bool>("""
        (() => {
            const allRoots = [document];
            for (let i = 0; i < allRoots.length; i++) {
                try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
            }
            for (const r of allRoots) {
                const saveBtns = Array.from(r.querySelectorAll('he-button, button, input[type="button"]')).filter(e => {
                    const t = (e.innerText || e.value || e.getAttribute('aria-label') || '').trim();
                    const key = (e.getAttribute('data-l10n-key') || '').toLowerCase();
                    const isSave = t.includes('保存') || t.toLowerCase().includes('save') || key.includes('savebutton');
                    const rect = e.getBoundingClientRect();
                    return isSave && !t.includes('草稿') && rect.width > 0 && rect.height > 0 && !e.disabled;
                });

                for (const b of saveBtns) {
                    b.scrollIntoView({ block: 'center' });
                    if (b.shadowRoot) {
                        const inner = b.shadowRoot.querySelector('button');
                        if (inner) inner.click();
                    }
                    b.click();
                    b.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true }));
                    return true;
                }
            }
            return false;
        })()
        """);

        if (saved) Console.WriteLine("[PASS] 成功点击分级摘要页的【保存】按钮！");
        await Task.Delay(5000);
        return saved;
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
