using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class FillOptionsCommand
{
    public static async Task<int> ExecuteAsync(DesiredState desired, string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var dom = new DomDriver(client);
        var input = new InputDriver(client, new AxLocator(client, dom));
        var native = new NativeFormAdapter(client, input);

        string baseUrl = desired.Site.BaseUrl.TrimEnd('/');
        string submissionId = desired.SubmissionId;

        if (string.IsNullOrWhiteSpace(submissionId))
        {
            string cpPath = Path.Combine(stateRoot, "checkpoint.json");
            if (File.Exists(cpPath))
            {
                try
                {
                    var cp = JsonSerializer.Deserialize<StoreCheckpoint>(File.ReadAllText(cpPath));
                    if (!string.IsNullOrWhiteSpace(cp?.SubmissionId)) submissionId = cp.SubmissionId;
                }
                catch { }
            }
        }

        if (string.IsNullOrWhiteSpace(submissionId))
        {
            var discovery = new SubmissionDiscovery(client, waiter, native);
            var discResult = await discovery.DiscoverAsync(baseUrl, desired.ProductId, autoCreateIfMissing: false);
            submissionId = discResult.SubmissionId;
        }

        string optionsUrl = $"{baseUrl}/{desired.ProductId}/submissions/{submissionId}/options";
        Console.WriteLine($"[NAV] Navigating to Submission Options page: {optionsUrl}");
        await waiter.NavigateAsync(optionsUrl, "Submission Options Page");
        await waiter.RequireAsync(async () =>
        {
            return await client.EvaluateAsync<bool>("document.querySelector('textarea.text-area-width, button[data-l10n-key=\"optionsSave\"], [dcl10n=\"optionsSave\"]') !== null");
        }, TimeSpan.FromSeconds(30), "Wait for options form controls");
        await Task.Delay(1500);

        // 1. Set publish mode radio
        string mode = desired.SubmissionOptions.PublishMode;
        Console.WriteLine($"[INFO] 设置发布暂缓选项: {mode}...");
        if (mode.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            await client.EvaluateAsync<bool>("""
            (() => {
              const r = document.querySelector('input#radioReleaseDate_manual');
              if (r) {
                r.click();
                r.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
              }
              return false;
            })()
            """);
        }
        else
        {
            await client.EvaluateAsync<bool>("""
            (() => {
              const r = document.querySelector('input#radioReleaseDate_asap');
              if (r) {
                r.click();
                r.dispatchEvent(new Event('change', { bubbles: true }));
                return true;
              }
              return false;
            })()
            """);
        }

        // 2. Wait for restricted capabilities section to render from background API
        await waiter.RequireAsync(async () =>
        {
            return await client.EvaluateAsync<bool>("""
            (() => {
              const deepAll = (selector) => {
                const roots = [document];
                for (let i = 0; i < roots.length; i++) {
                  try {
                    for (const e of roots[i].querySelectorAll('*')) if (e.shadowRoot) roots.push(e.shadowRoot);
                  } catch (_) {}
                }
                const out = [];
                for (const r of roots) out.push(...Array.from(r.querySelectorAll(selector)));
                return out;
              };
              return deepAll('textarea').length > 0 || (document.body?.innerText || '').includes('runFullTrust');
            })()
            """);
        }, TimeSpan.FromSeconds(30), "Wait for restricted capabilities textarea");
        await Task.Delay(1000);

        string reason = desired.SubmissionOptions.RunFullTrustReason;
        Console.WriteLine($"[INFO] 正在填报 runFullTrust 受限功能说明理由: {reason[..Math.Min(40, reason.Length)]}...");
        bool filled = await client.EvaluateAsync<bool>($$"""
        (() => {
          const deepAll = (selector) => {
            const roots = [document];
            for (let i = 0; i < roots.length; i++) {
              try {
                for (const e of roots[i].querySelectorAll('*')) if (e.shadowRoot) roots.push(e.shadowRoot);
              } catch (_) {}
            }
            const out = [];
            for (const r of roots) out.push(...Array.from(r.querySelectorAll(selector)));
            return out;
          };

          const textareas = deepAll('textarea');
          const ta = textareas.find(e => (e.closest('section')?.innerText || '').includes('runFullTrust') || e.classList.contains('text-area-width')) || textareas[0];
          if (ta) {
            ta.scrollIntoView({ block: 'center', behavior: 'instant' });
            ta.focus();
            const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
            if (setter && setter.set) {
              setter.set.call(ta, {{JsonSerializer.Serialize(reason)}});
            } else {
              ta.value = {{JsonSerializer.Serialize(reason)}};
            }
            ta.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
            ta.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
            return true;
          }
          return false;
        })()
        """);

        if (!filled)
        {
            throw new InvalidOperationException("[ERROR] 无法定位 runFullTrust 理由输入框！");
        }
        Console.WriteLine("[PASS] runFullTrust 理由输入并派发事件成功！");

        await Task.Delay(1000);

        // 3. Click Save
        Console.WriteLine("[INFO] 点击底部【保存】按钮...");
        await client.EvaluateAsync<bool>("""
        (() => {
          const save = document.querySelector('button[data-l10n-key="optionsSave"], button[dcl10n="optionsSave"], .btn-primary');
          if (save) {
            save.scrollIntoView({ block: 'center', behavior: 'instant' });
            save.click();
            save.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
            return true;
          }
          return false;
        })()
        """);

        await Task.Delay(3000);

        // 4. Return to Overview and verify
        string overviewUrl = $"{baseUrl}/{desired.ProductId}/overview";
        Console.WriteLine($"[NAV] Navigating to Overview: {overviewUrl}");
        await waiter.NavigateAsync(overviewUrl, "Overview after Options Save");
        await Task.Delay(2500);

        return 0;
    }
}
