using System.Text.Json;
using Vainreef.EdgeStore.Cdp;

namespace Vainreef.EdgeStore.ComponentAdapters;

public sealed class HeCheckboxAdapter
{
    private readonly CdpClient _client;
    private readonly InputDriver _input;

    public HeCheckboxAdapter(CdpClient client, InputDriver input) { _client = client; _input = input; }

    public async Task<bool?> ObserveCheckedAsync(string textOrIdentifier)
    {
        var probe = await ProbeAsync(textOrIdentifier, scroll: false);
        if (probe == null || probe.Count == 0) return null;
        if (probe.Count != 1) throw new InvalidOperationException($"Checkbox locator [{textOrIdentifier}] is ambiguous: {probe.Count} matches.");
        return probe.Checked;
    }

    public async Task SetCheckedAsync(string textOrIdentifier, bool wantChecked, string label = "")
    {
        Ops.Check(label, wantChecked);
        var probe = await ProbeAsync(textOrIdentifier, scroll: true)
            ?? throw new InvalidOperationException($"Checkbox [{label}] probe returned no result.");
        if (probe.Count == 0) return; // optional declaration is absent in this UI shape
        if (probe.Count != 1) throw new InvalidOperationException($"Checkbox [{label}] is ambiguous: {probe.Count} matches.");
        if (probe.Checked == wantChecked) return;
        await _input.ClickCoordinatesAsync(probe.X, probe.Y, $"Toggle checkbox [{label}] -> {wantChecked}");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var after = await ProbeAsync(textOrIdentifier, scroll: false);
            if (after?.Count == 1 && after.Checked == wantChecked) return;
            await Task.Delay(150);
        }
        throw new InvalidOperationException($"Checkbox [{label}] did not reach checked={wantChecked} after one click.");
    }

    private Task<CheckboxProbe?> ProbeAsync(string value, bool scroll) => _client.EvaluateAsync<CheckboxProbe>($$"""
    (async () => {
      const target={{JsonSerializer.Serialize(value)}}.toLowerCase(), doScroll={{scroll.ToString().ToLowerInvariant()}};
      const boxes=Array.from(document.querySelectorAll('he-checkbox,input[type="checkbox"]')).filter(e=>{
        const r=e.getBoundingClientRect(),s=getComputedStyle(e);
        const t=((e.innerText||e.parentElement?.innerText||'')+' '+(e.getAttribute('name')||'')+' '+(e.id||'')).toLowerCase();
        return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&t.includes(target);
      });
      if(boxes.length!==1) return {count:boxes.length,checked:false,x:0,y:0};
      const e=boxes[0]; if(doScroll){e.scrollIntoView({block:'center',behavior:'instant'});await new Promise(r=>setTimeout(r,80));}
      const r=e.getBoundingClientRect();
      const checked=e.checked===true||e.hasAttribute('checked')||e.getAttribute('aria-checked')==='true';
      return {count:1,checked,x:r.left+r.width/2,y:r.top+r.height/2};
    })()
    """);
}

public sealed class CheckboxProbe
{
    public int Count { get; set; }
    public bool Checked { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
}
