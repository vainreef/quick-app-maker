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
        Ops.Type(label.Length > 0 ? "field [" + label + "]" : $"field {string.Join(",", selectors)}", value);
        var result = await _client.EvaluateAsync<JsOperationResult>($$"""
        (() => {
          const selectors = {{JsonSerializer.Serialize(selectors)}};
          const value = {{JsonSerializer.Serialize(value)}};
          const visible = e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
          };
          let found = [], seen = new Set();
          const qsa = s => { try { return Array.from(document.querySelectorAll(s)); } catch { return []; } };
          for (const selector of selectors) {
            for (const e of qsa(selector)) {
              if (visible(e) && !seen.has(e)) { seen.add(e); found.push(e); }
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
          e.dispatchEvent(new Event('blur', { bubbles: true }));
          return { ok: true, value: e.value };
        })()
        """);

        if (result?.Ok != true)
        {
            throw new InvalidOperationException($"Failed to set field [{label}]: {result?.Detail} (matched={result?.Count ?? 0})");
        }
        Ops.Publish("TYPE-OK", $"field [{label}] = \"{value}\"");
    }

    public async Task SetRadioAsync(string selector, string label = "")
    {
        Ops.Publish("EVAL", $"select radio [{label}] via {selector}");
        JsElementRect? rect = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            rect = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              let e = document.querySelector({{JsonSerializer.Serialize(selector)}});
              if (!e) return null;
              let target = e;
              const r0 = e.getBoundingClientRect();
              if (r0.width <= 0 || r0.height <= 0) {
                // Native control is visually hidden (custom-styled radio): target its label instead
                const id = e.id;
                const lbl = id ? document.querySelector('label[for="' + id + '"]') : null;
                const wrapped = e.closest('label');
                target = lbl || wrapped;
                if (!target) return null;
              }
              const r = target.getBoundingClientRect();
              if (r.width <= 0 || r.height <= 0) return null; // layout not ready yet
              target.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(res => setTimeout(res, 150));
              const r2 = target.getBoundingClientRect();
              if (r2.width <= 0 || r2.height <= 0) return null;
              const cx = r2.left + r2.width / 2, cy = r2.top + r2.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r2.width, height: r2.height };
            })()
            """);
            if (rect != null) break;
            await Task.Delay(300);
        }

        if (rect == null)
        {
            throw new InvalidOperationException($"Cannot locate radio [{label} ({selector})] after waiting for layout");
        }

        await _input.ClickCoordinatesAsync(rect.X, rect.Y, label: $"Select radio [{label}]");
    }

    public async Task ClickStrictAsync(string[] selectors, string label = "")
    {
        Ops.Click(label, string.Join(",", selectors));
        JsElementRect? rect = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            rect = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              const selectors = {{JsonSerializer.Serialize(selectors)}};
              const seen = new Set(), found = [];
              const visible = e => {
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
              };
              const qsa = s => { try { return Array.from(document.querySelectorAll(s)); } catch { return []; } };
              for (const selector of selectors) {
                for (const e of qsa(selector)) {
                  if (visible(e) && !seen.has(e)) {
                    seen.add(e);
                    found.push(e);
                  }
                }
              }
              if (found.length !== 1) return null;
              const target = found[0];
              target.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(res => setTimeout(res, 150));
              const r = target.getBoundingClientRect();
              if (r.width <= 0 || r.height <= 0) return null;
              const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r.width, height: r.height };
            })()
            """);
            if (rect != null) break;
            await Task.Delay(300);
        }

        if (rect == null)
        {
            throw new InvalidOperationException($"Cannot find single visible clickable element for [{label}]");
        }

        await _input.ClickCoordinatesAsync(rect.X, rect.Y, label: $"Click [{label}]");
    }

    public async Task ClickOptionByDeepTextAsync(string[] prefixTexts, string label = "")
    {
        Ops.Click(label, string.Join(",", prefixTexts));
        JsElementRect? rect = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            rect = await _client.EvaluateAsync<JsElementRect>($$"""
            (async () => {
              const prefixes = {{JsonSerializer.Serialize(prefixTexts)}};
              const allRoots=[document];
              for(let i=0;i<allRoots.length;i++){
                try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){}
              }
              const deepAll = (selector) => {
                const out=[], seen=new Set();
                for(const root of allRoots){
                  try { for(const e of root.querySelectorAll(selector)) if(!seen.has(e)){ seen.add(e); out.push(e); } } catch(_){}
                }
                return out;
              };
              const visible = e => { const r=e.getBoundingClientRect(), s=getComputedStyle(e); return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden' && !e.disabled; };
              const el = deepAll('button,he-button,a,[role="button"],li,div[role="option"],he-menu-item')
                      .find(e => {
                        if (!visible(e)) return false;
                        const t = ((e.innerText||e.getAttribute('aria-label')||'') + ' ').trim().replace(/\s+/g,' ');
                        return prefixes.some(p => t.startsWith(p));
                      });
              if (!el) return null;
              el.scrollIntoView({ block: 'center', behavior: 'instant' });
              await new Promise(res => setTimeout(res, 200));
              const r = el.getBoundingClientRect();
              if (r.width <= 0 || r.height <= 0) return null;
              const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
              if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
              return { x: cx, y: cy, width: r.width, height: r.height };
            })()
            """);
            if (rect != null) break;
            await Task.Delay(300);
        }
        if (rect == null) throw new InvalidOperationException($"Cannot find single visible option for [{label}]");
        await _input.ClickCoordinatesAsync(rect.X, rect.Y, label: $"Select [{label}]");
    }

    public async Task ClickByTextAsync(string[] texts, string label = "")
    {
        Ops.Click("by text [" + label + "]", string.Join(",", texts));
        var rect = await _client.EvaluateAsync<JsElementRect>($$"""
        (async () => {
          const wanted={{JsonSerializer.Serialize(texts)}}.map(x=>x.trim().toLowerCase());
          const candidates=Array.from(document.querySelectorAll('button,he-button,a,[role="button"]')).filter(e=>{
            const t=(e.innerText||e.getAttribute('aria-label')||'').trim().toLowerCase();
            const r=e.getBoundingClientRect(),s=getComputedStyle(e);
            return wanted.includes(t)&&r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&!e.disabled;
          });
          if(candidates.length!==1) return null;
          const e=candidates[0]; e.scrollIntoView({block:'center',behavior:'instant'});
          await new Promise(r=>setTimeout(r,120));
          const r=e.getBoundingClientRect();
          return {x:r.left+r.width/2,y:r.top+r.height/2,width:r.width,height:r.height};
        })()
        """);
        if (rect == null) throw new InvalidOperationException($"Expected one visible text button for [{label}], found a different count.");
        await _input.ClickCoordinatesAsync(rect.X, rect.Y, $"Click [{label}]");
    }

    public async Task ClickDialogButtonAsync(string[] texts, string label = "")
    {
        Ops.Click("dialog button [" + label + "]", string.Join(",", texts));
        var rect = await _client.EvaluateAsync<JsElementRect>($$"""
        (async () => {
          const wanted={{JsonSerializer.Serialize(texts)}}.map(x=>x.trim().toLowerCase());
          const dialogs=Array.from(document.querySelectorAll('[role="dialog"],[aria-modal="true"]')).filter(d=>{
            const r=d.getBoundingClientRect(),s=getComputedStyle(d); return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden';
          });
          const found=dialogs.flatMap(d=>Array.from(d.querySelectorAll('button,he-button,[role="button"]'))).filter(e=>{
            const t=(e.innerText||e.getAttribute('aria-label')||'').trim().toLowerCase(),r=e.getBoundingClientRect();
            return wanted.includes(t)&&r.width>0&&r.height>0&&!e.disabled;
          });
          if(found.length!==1) return null;
          const e=found[0]; e.scrollIntoView({block:'center',behavior:'instant'}); await new Promise(r=>setTimeout(r,80));
          const r=e.getBoundingClientRect(); return {x:r.left+r.width/2,y:r.top+r.height/2,width:r.width,height:r.height};
        })()
        """);
        if (rect == null) throw new InvalidOperationException($"Expected one visible dialog button for [{label}].");
        await _input.ClickCoordinatesAsync(rect.X, rect.Y, $"Click dialog [{label}]");
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
