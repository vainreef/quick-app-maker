using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class FullDomDumpCommand
{
    public static async Task<int> ExecuteAsync(string stateRoot, string baseDir)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        string url = await client.EvaluateAsync<string>("location.href") ?? "";
        string title = await client.EvaluateAsync<string>("document.title") ?? "";
        string bodyText = await client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";
        string fullHtml = await client.EvaluateAsync<string>("document.documentElement.outerHTML") ?? "";

        var controls = await client.EvaluateAsync<List<Dictionary<string, object>>>("""
        (() => {
          const out = [];
          for (const e of document.querySelectorAll('*')) {
            const tag = e.tagName.toLowerCase();
            if (['input', 'button', 'select', 'textarea', 'a', 'he-button', 'he-select', 'he-checkbox'].includes(tag)) {
              out.push({
                tag: tag,
                type: e.type || '',
                id: e.id || '',
                name: e.name || '',
                value: (e.value || '').slice(0, 100),
                innerText: (e.innerText || '').trim().replace(/\s+/g, ' ').slice(0, 100),
                href: e.href || '',
                disabled: !!e.disabled,
                visible: e.getBoundingClientRect().width > 0
              });
            }
          }
          return out;
        })()
        """) ?? [];

        string outHtmlPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "FULL-RAW-DOM.html");
        string outTextPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "FULL-RAW-TEXT.txt");
        string outControlsPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "FULL-RAW-CONTROLS.json");

        File.WriteAllText(outHtmlPath, fullHtml, new System.Text.UTF8Encoding(false));
        File.WriteAllText(outTextPath, $"URL: {url}\nTITLE: {title}\n\n=== FULL BODY TEXT ===\n{bodyText}", new System.Text.UTF8Encoding(false));
        File.WriteAllText(outControlsPath, JsonSerializer.Serialize(controls, Program.JsonIndented), new System.Text.UTF8Encoding(false));

        Console.WriteLine($"[PASS] Complete Unfiltered HTML saved to: {outHtmlPath} ({fullHtml.Length} bytes)");
        Console.WriteLine($"[PASS] Complete Raw Text saved to: {outTextPath}");
        Console.WriteLine($"[PASS] Total Controls captured: {controls.Count}");
        Console.WriteLine("\n================== FULL VISIBLE TEXT ON PAGE ==================");
        Console.WriteLine(bodyText);
        Console.WriteLine("===============================================================");

        return 0;
    }
}
