using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;

namespace Vainreef.EdgeStore.Commands;

public static class DomDumpCommand
{
    public static async Task<int> ExecuteAsync(string stateRoot, string baseDir)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        string url = await client.EvaluateAsync<string>("location.href") ?? "";
        string title = await client.EvaluateAsync<string>("document.title") ?? "";

        var dump = await client.EvaluateAsync<Dictionary<string, object>>("""
        (() => {
          const collectAll = (root) => {
            const nodes = [root], acc = [];
            while(nodes.length) {
              const n = nodes.shift();
              acc.push(n);
              if (n.shadowRoot) nodes.push(n.shadowRoot);
              for (const c of n.children || []) nodes.push(c);
            }
            return acc;
          };
          const all = collectAll(document.body || document.documentElement);

          const controls = [];
          for (const e of all) {
            const tag = (e.tagName || '').toLowerCase();
            const r = e.getBoundingClientRect ? e.getBoundingClientRect() : { width: 0, height: 0 };
            const vis = r.width > 0 && r.height > 0;
            if (['input', 'button', 'select', 'textarea', 'he-select', 'he-checkbox', 'he-button'].includes(tag)) {
              controls.push({
                tag: tag,
                type: e.type || '',
                id: e.id || '',
                name: e.name || '',
                label: e.getAttribute('aria-label') || e.innerText || e.value || '',
                checked: !!e.checked,
                disabled: !!e.disabled,
                visible: vis
              });
            }
          }

          return {
            url: location.href,
            title: document.title,
            controlsCount: controls.length,
            controls: controls.slice(0, 100)
          };
        })()
        """);

        string outPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "dom-dump-LIVE.html");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var lines = new List<string>
        {
            $"url={url}",
            $"title={title}",
            "---- CONTROLS ----"
        };

        if (dump != null && dump.TryGetValue("controls", out var ctrlObj) && ctrlObj is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in elem.EnumerateArray())
            {
                string tag = item.GetProperty("tag").GetString() ?? "";
                string label = item.GetProperty("label").GetString() ?? "";
                bool vis = item.GetProperty("visible").GetBoolean();
                bool dis = item.GetProperty("disabled").GetBoolean();
                lines.Add($"{tag.ToUpperInvariant()}: label=\"{label}\" disabled={dis} vis={vis}");
            }
        }

        File.WriteAllLines(outPath, lines, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"[PASS] DOM dumped ({lines.Count} items) to: {outPath}");
        Console.WriteLine($"[INFO] Current URL: {url}");
        return 0;
    }
}
