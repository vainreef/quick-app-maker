using System.Text.Json;
using Vainreef.EdgeStore.Cdp;

namespace Vainreef.EdgeStore.ComponentAdapters;

public class NativeFormAdapter
{
    private readonly CdpClient _client;
    private readonly InputDriver _input;

    public NativeFormAdapter(CdpClient client, InputDriver input)
    {
        _client = client;
        _input = input;
    }

    public async Task SetFieldAsync(string[] selectors, string value, string label = "")
    {
        var result = await _client.EvaluateAsync<JsOperationResult>($$"""
        (() => {
          const selectors = {{JsonSerializer.Serialize(selectors)}};
          const value = {{JsonSerializer.Serialize(value)}};
          const visible = e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
          };
          let found = [];
          for (const selector of selectors) {
            for (const e of document.querySelectorAll(selector)) {
              if (visible(e)) found.push(e);
            }
          }
          if (found.length !== 1) return { ok: false, count: found.length };
          const e = found[0];
          if (e.tagName === 'SELECT') {
            const option = Array.from(e.options).find(o => o.value === value || o.textContent.trim() === value || o.label === value);
            if (!option) return { ok: false, count: 1, detail: 'option not found' };
            e.value = option.value;
          } else {
            const proto = e.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            const setter = Object.getOwnPropertyDescriptor(proto, 'value');
            if (setter && setter.set) setter.set.call(e, value); else e.value = value;
          }
          e.dispatchEvent(new Event('input', { bubbles: true }));
          e.dispatchEvent(new Event('change', { bubbles: true }));
          return { ok: true, value: e.value };
        })()
        """);

        if (result?.Ok != true)
        {
            throw new InvalidOperationException($"Failed to set field [{label}]: {result?.Detail} (matched={result?.Count ?? 0})");
        }
    }

    public async Task SetRadioAsync(string selector, string label = "")
    {
        var rect = await _client.EvaluateAsync<JsElementRect>($$"""
        (() => {
          const e = document.querySelector({{JsonSerializer.Serialize(selector)}});
          if (!e) return null;
          const r = e.getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);

        if (rect == null)
        {
            throw new InvalidOperationException($"Cannot locate radio [{label} ({selector})]");
        }

        await _input.ClickCoordinatesAsync(rect.X, rect.Y, label: $"Select radio [{label}]");
    }

    public async Task ClickStrictAsync(string[] selectors, string label = "")
    {
        var rect = await _client.EvaluateAsync<JsElementRect>($$"""
        (() => {
          const selectors = {{JsonSerializer.Serialize(selectors)}};
          const seen = new Set(), found = [];
          const visible = e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
          };
          for (const selector of selectors) {
            for (const e of document.querySelectorAll(selector)) {
              if (visible(e) && !seen.has(e)) {
                seen.add(e);
                found.push(e);
              }
            }
          }
          if (found.length !== 1) return null;
          const r = found[0].getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);

        if (rect == null)
        {
            throw new InvalidOperationException($"Cannot find single visible clickable element for [{label}]");
        }

        await _input.ClickCoordinatesAsync(rect.X, rect.Y, label: $"Click [{label}]");
    }

    public async Task<List<string>> GetVisibleErrorsAsync()
    {
        var errors = await _client.EvaluateAsync<List<string>>("""
        (() => {
          const els = Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"].alert-error, .has-error')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e), t = (e.innerText || '').trim();
            return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' &&
              /\u4e0d\u80fd\u4e3a\u7a7a|\u5fc5\u987b|\u9519\u8bef|error|failed|invalid|\u81f3\u5c11\u4e00\u5f20|\u552f\u4e00\u6807\u8bc6|\u5220\u9664\u5176\u4e2d/i.test(t);
          });
          return els.map(e => (e.innerText || '').trim()).filter(Boolean).slice(0, 20);
        })()
        """);

        return errors ?? [];
    }

    public async Task AssertNoVisibleErrorsAsync()
    {
        var errors = await GetVisibleErrorsAsync();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Partner Center validation errors:\n" + string.Join("\n", errors.Select(e => "  ! " + e)));
        }
    }
}

public class JsOperationResult
{
    public bool Ok { get; set; }
    public int Count { get; set; }
    public string? Detail { get; set; }
    public string? Value { get; set; }
}
