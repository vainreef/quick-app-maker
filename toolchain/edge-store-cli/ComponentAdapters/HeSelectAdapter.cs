using System.Text.Json;
using Vainreef.EdgeStore.Cdp;

namespace Vainreef.EdgeStore.ComponentAdapters;

public class HeSelectAdapter
{
    private readonly CdpClient _client;
    private readonly AxLocator _locator;
    private readonly InputDriver _input;
    private readonly Waiter _waiter;

    public HeSelectAdapter(CdpClient client, AxLocator locator, InputDriver input, Waiter waiter)
    {
        _client = client;
        _locator = locator;
        _input = input;
        _waiter = waiter;
    }

    public async Task<string> ObserveValueAsync(string hostSelector)
    {
        var val = await _client.EvaluateAsync<string>($$"""
        (() => {
          const e = document.querySelector('{{hostSelector}}');
          if (!e) return '';
          return (e.getAttribute('value') || e.value || e.innerText || '').trim();
        })()
        """);

        return val ?? string.Empty;
    }

    public async Task SetValueAsync(string hostSelector, string optionText, string label = "")
    {
        string current = await ObserveValueAsync(hostSelector);
        if (string.Equals(current, optionText, StringComparison.OrdinalIgnoreCase))
        {
            return; // Already desired value
        }

        // 1. Locate host he-select (wait until layout settles), then click shadow trigger
        ResolvedNode? hostNode = null;
        bool hostFound = await _waiter.WaitUntilAsync(async () =>
        {
            var rect = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              const els = Array.from(document.querySelectorAll('{{hostSelector}}')).filter(e => {
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
              });
              if (els.length === 0) return null;
              const host = els[0];
              const trigger = (host.shadowRoot && host.shadowRoot.querySelector('input[role="combobox"]')) || host;
              const tr = trigger.getBoundingClientRect();
              if (tr.width <= 0 || tr.height <= 0) return null; // layout not ready yet
              trigger.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(r => setTimeout(r, 150));
              const r2 = trigger.getBoundingClientRect();
              if (r2.width <= 0 || r2.height <= 0) return null;
              const cx = r2.left + r2.width / 2, cy = r2.top + r2.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r2.width, height: r2.height };
            })()
            """);

            if (rect == null) return false;
            hostNode = new ResolvedNode
            {
                DirectCoordinates = (rect.X, rect.Y),
                Source = "HeSelectTrigger:" + hostSelector
            };
            return true;
        }, timeout: TimeSpan.FromSeconds(15), description: $"Wait for visible host [{label}]");

        if (!hostFound || hostNode == null)
        {
            throw new InvalidOperationException($"Cannot locate visible he-select host [{label} ({hostSelector})]");
        }

        // 1b. Open dropdown and wait for aria-expanded === true
        await _waiter.RequireAsync(async () =>
        {
            string ready = await _client.EvaluateAsync<string>("document.readyState") ?? "";
            return ready == "complete";
        }, TimeSpan.FromSeconds(10), $"Wait for document ready before opening [{label}]");

        bool dropdownOpen = false;
        for (int attempt = 0; attempt < 4 && !dropdownOpen; attempt++)
        {
            var rect = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              const els = Array.from(document.querySelectorAll('{{hostSelector}}')).filter(e => {
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
              });
              if (els.length === 0) return null;
              const host = els[0];
              const trigger = (host.shadowRoot && host.shadowRoot.querySelector('input[role="combobox"]')) || host;
              const tr = trigger.getBoundingClientRect();
              if (tr.width <= 0 || tr.height <= 0) return null;
              trigger.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(r => setTimeout(r, 200));
              const r2 = trigger.getBoundingClientRect();
              if (r2.width <= 0 || r2.height <= 0) return null;
              const cx = r2.left + r2.width / 2, cy = r2.top + r2.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r2.width, height: r2.height };
            })()
            """);

            if (rect != null)
            {
                hostNode = new ResolvedNode { DirectCoordinates = (rect.X, rect.Y), Source = "HeSelectTrigger:" + hostSelector };
                await _input.ClickNodeAsync(hostNode, label: $"Open dropdown for {label} (input attempt {attempt + 1})");

                dropdownOpen = await _waiter.WaitUntilAsync(async () =>
                {
                    bool expanded = await _client.EvaluateAsync<bool>($$"""
                    (() => {
                      const e = document.querySelector('{{hostSelector}}');
                      if (!e || !e.shadowRoot) return false;
                      const input = e.shadowRoot.querySelector('input[role="combobox"]');
                      return input ? input.getAttribute('aria-expanded') === 'true' : false;
                    })()
                    """);
                    return expanded;
                }, timeout: TimeSpan.FromSeconds(4), description: $"Wait for dropdown [{label}] to expand");

                if (dropdownOpen) break;
            }

            // Fallback: click toggle button inside shadow root
            var btnRect = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              const els = Array.from(document.querySelectorAll('{{hostSelector}}')).filter(e => {
                const r = e.getBoundingClientRect();
                return r.width > 0 && r.height > 0;
              });
              if (els.length === 0) return null;
              const host = els[0];
              const btn = host.shadowRoot && host.shadowRoot.querySelector('button.text-field__button');
              if (!btn) return null;
              btn.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(r => setTimeout(r, 200));
              const r = btn.getBoundingClientRect();
              const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r.width, height: r.height };
            })()
            """);

            if (btnRect != null)
            {
                var btnNode = new ResolvedNode { DirectCoordinates = (btnRect.X, btnRect.Y), Source = "HeSelectButton:" + hostSelector };
                await _input.ClickNodeAsync(btnNode, label: $"Open dropdown for {label} (button attempt {attempt + 1})");

                dropdownOpen = await _waiter.WaitUntilAsync(async () =>
                {
                    bool expanded = await _client.EvaluateAsync<bool>($$"""
                    (() => {
                      const e = document.querySelector('{{hostSelector}}');
                      if (!e || !e.shadowRoot) return false;
                      const input = e.shadowRoot.querySelector('input[role="combobox"]');
                      return input ? input.getAttribute('aria-expanded') === 'true' : false;
                    })()
                    """);
                    return expanded;
                }, timeout: TimeSpan.FromSeconds(4), description: $"Wait for dropdown [{label}] to expand via button");

                if (dropdownOpen) break;
            }
        }

        if (!dropdownOpen)
        {
            throw new InvalidOperationException($"Dropdown [{label}] could not be opened after retries");
        }

        // 2. Wait for option to become available in AXTree or DOM
        ResolvedNode? optionNode = null;
        bool opened = await _waiter.WaitUntilAsync(async () =>
        {
            optionNode = await _locator.FindByRoleAndNameAsync("option", optionText);
            if (optionNode != null) return true;

            var jsOption = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              const target = {{JsonSerializer.Serialize(optionText)}}.toLowerCase();
              const opts = Array.from(document.querySelectorAll('he-option, [role="option"]')).filter(e => {
                const t = (e.innerText || e.getAttribute('label') || e.getAttribute('value') || '').toLowerCase();
                const r = e.getBoundingClientRect();
                return t.includes(target) && r.width > 0 && r.height > 0;
              });
              if (opts.length === 0) return null;
              const el = opts[0];
              el.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(r => setTimeout(r, 150));
              const r = el.getBoundingClientRect();
              const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r.width, height: r.height };
            })()
            """);

            if (jsOption != null)
            {
                optionNode = new ResolvedNode
                {
                    DirectCoordinates = (jsOption.X, jsOption.Y),
                    Source = "ShadowOption:" + optionText
                };
                return true;
            }

            return false;
        }, timeout: TimeSpan.FromSeconds(15), description: $"Wait for option [{optionText}] in [{label}]");

        if (!opened || optionNode == null)
        {
            throw new InvalidOperationException($"Option [{optionText}] not found in dropdown [{label}]");
        }

        // 3. Click the option
        await _input.ClickNodeAsync(optionNode, label: $"Select option [{optionText}] in {label}");
        await Task.Delay(300);

        // 4. Verify value has converged
        string after = await ObserveValueAsync(hostSelector);
        if (!string.IsNullOrEmpty(after) && !after.Contains(optionText, StringComparison.OrdinalIgnoreCase) && !optionText.Contains(after, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(200);
        }
    }
}
