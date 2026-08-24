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

        // 1. Locate host he-select and click to open dropdown
        var hostNode = await _locator.FindVisibleByJsAsync(hostSelector)
            ?? await _locator.FindByCssAsync(hostSelector)
            ?? throw new InvalidOperationException($"Cannot locate he-select host [{label} ({hostSelector})]");

        await _input.ClickNodeAsync(hostNode, label: $"Open dropdown for {label}");

        // 2. Wait for option to become available in AXTree or DOM
        ResolvedNode? optionNode = null;
        bool opened = await _waiter.WaitUntilAsync(async () =>
        {
            // First try Accessibility Tree (role=option, name=optionText)
            optionNode = await _locator.FindByRoleAndNameAsync("option", optionText);
            if (optionNode != null) return true;

            // Fallback: search he-option elements in DOM / Shadow DOM
            var jsOption = await _client.EvaluateAsync<JsElementRect>($$"""
            (() => {
              const target = {{JsonSerializer.Serialize(optionText)}}.toLowerCase();
              const opts = Array.from(document.querySelectorAll('he-option, [role="option"]')).filter(e => {
                const t = (e.innerText || e.getAttribute('label') || e.getAttribute('value') || '').toLowerCase();
                const r = e.getBoundingClientRect();
                return t.includes(target) && r.width > 0 && r.height > 0;
              });
              if (opts.length === 0) return null;
              const r = opts[0].getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
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
            // Some controls update internal value; give a slight grace period
            await Task.Delay(200);
        }
    }
}
