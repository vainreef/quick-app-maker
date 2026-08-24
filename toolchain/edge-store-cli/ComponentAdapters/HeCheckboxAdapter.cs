using System.Text.Json;
using Vainreef.EdgeStore.Cdp;

namespace Vainreef.EdgeStore.ComponentAdapters;

public class HeCheckboxAdapter
{
    private readonly CdpClient _client;
    private readonly InputDriver _input;

    public HeCheckboxAdapter(CdpClient client, InputDriver input)
    {
        _client = client;
        _input = input;
    }

    public async Task<bool?> ObserveCheckedAsync(string textOrIdentifier)
    {
        var result = await _client.EvaluateAsync<bool?>($$"""
        (() => {
          const target = {{JsonSerializer.Serialize(textOrIdentifier)}}.toLowerCase();
          const boxes = Array.from(document.querySelectorAll('he-checkbox, input[type="checkbox"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            if (!(r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden')) return false;
            const t = ((e.innerText || e.parentElement?.innerText || '') + ' ' + (e.getAttribute('name') || '') + ' ' + (e.id || '')).toLowerCase();
            return t.includes(target);
          });
          if (boxes.length === 0) return null;
          const e = boxes[0];
          return e.checked === true || e.hasAttribute('checked') || e.getAttribute('aria-checked') === 'true';
        })()
        """);

        return result;
    }

    public async Task SetCheckedAsync(string textOrIdentifier, bool wantChecked, string label = "")
    {
        var current = await ObserveCheckedAsync(textOrIdentifier);
        if (current.HasValue && current.Value == wantChecked)
        {
            return; // Already desired state
        }

        var rects = await _client.EvaluateAsync<List<JsElementRect>>($$"""
        (() => {
          const target = {{JsonSerializer.Serialize(textOrIdentifier)}}.toLowerCase();
          const want = {{wantChecked.ToString().ToLowerInvariant()}};
          const boxes = Array.from(document.querySelectorAll('he-checkbox, input[type="checkbox"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            if (!(r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden')) return false;
            const t = ((e.innerText || e.parentElement?.innerText || '') + ' ' + (e.getAttribute('name') || '') + ' ' + (e.id || '')).toLowerCase();
            return t.includes(target);
          });

          const clicks = [];
          for (const e of boxes) {
            const isChecked = e.checked === true || e.hasAttribute('checked') || e.getAttribute('aria-checked') === 'true';
            if (isChecked !== want) {
              const r = e.getBoundingClientRect();
              clicks.push({ x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height });
            }
          }
          return clicks;
        })()
        """);

        if (rects != null)
        {
            foreach (var r in rects)
            {
                await _input.ClickCoordinatesAsync(r.X, r.Y, label: $"Toggle checkbox [{label ?? textOrIdentifier}] -> {wantChecked}");
            }
        }
    }
}
