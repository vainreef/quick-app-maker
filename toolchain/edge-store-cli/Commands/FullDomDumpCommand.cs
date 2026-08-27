using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class FullDomDumpCommand
{
    public static async Task<int> ExecuteAsync(string stateRoot, string baseDir, string? targetUrl = null)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            Console.WriteLine($"[NAV] Navigating directly to: {targetUrl}");
            await waiter.NavigateAsync(targetUrl, "Direct Page Navigation");
            await Task.Delay(3000);
        }

        string url = await client.EvaluateAsync<string>("location.href") ?? "";
        string title = await client.EvaluateAsync<string>("document.title") ?? "";

        // 如果页面包含新产品按钮，先点击它展开菜单
        await client.EvaluateAsync<bool>("""
        (() => {
            const allRoots=[document];
            for(let i=0;i<allRoots.length;i++){
              try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){}
            }
            for(const root of allRoots){
              for(const b of root.querySelectorAll('button,he-button,a,[role="button"],span')){
                if((b.innerText||'').trim() === '新产品'){
                  b.scrollIntoView({block:'center',inline:'center'});
                  b.click();
                  return true;
                }
              }
            }
            return false;
        })()
        """);
        await Task.Delay(1500);

        string bodyText = await client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";
        string fullHtml = await client.EvaluateAsync<string>("document.documentElement.outerHTML") ?? "";

        var controls = await client.EvaluateAsync<List<Dictionary<string, object>>>("""
        (() => {
          const allRoots = [document];
          for (let i = 0; i < allRoots.length; i++) {
            try {
              for (const e of allRoots[i].querySelectorAll('*')) {
                if (e.shadowRoot) allRoots.push(e.shadowRoot);
              }
            } catch (_) {}
          }

          const out = [];
          for (const root of allRoots) {
            for (const e of root.querySelectorAll('*')) {
              const tag = e.tagName.toLowerCase();
              const r = e.getBoundingClientRect();
              const isInteractive = ['input', 'button', 'select', 'textarea', 'a', 'he-button', 'he-select', 'he-checkbox', 'he-option', 'label', 'tr', 'table', 'form'].includes(tag) || e.getAttribute('role') || e.id;
              if (isInteractive) {
                out.push({
                  tag: tag,
                  type: e.getAttribute('type') || e.type || '',
                  id: e.id || '',
                  name: e.getAttribute('name') || e.name || '',
                  className: (e.className && typeof e.className === 'string') ? e.className : '',
                  role: e.getAttribute('role') || '',
                  value: (e.value || e.getAttribute('value') || '').slice(0, 100),
                  innerText: (e.innerText || e.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 200),
                  checked: !!e.checked || e.getAttribute('checked') !== null || e.getAttribute('aria-checked') === 'true',
                  disabled: !!e.disabled || e.getAttribute('aria-disabled') === 'true',
                  visible: r.width > 0 && r.height > 0
                });
              }
            }
          }
          return out;
        })()
        """) ?? [];

        string outHtmlPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "FULL-RAW-DOM.html");
        string outTextPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "FULL-RAW-TEXT.txt");
        string outControlsPath = Path.Combine(baseDir, "quick-app-maker", "toolchain", "edge-store-cli", "examples", "FULL-RAW-CONTROLS.json");

        Directory.CreateDirectory(Path.GetDirectoryName(outHtmlPath)!);

        File.WriteAllText(outHtmlPath, fullHtml, new System.Text.UTF8Encoding(false));
        File.WriteAllText(outTextPath, $"URL: {url}\nTITLE: {title}\n\n=== FULL BODY TEXT ===\n{bodyText}", new System.Text.UTF8Encoding(false));
        File.WriteAllText(outControlsPath, JsonSerializer.Serialize(controls, Program.JsonIndented), new System.Text.UTF8Encoding(false));

        Console.WriteLine($"[PASS] Complete Raw HTML saved: {outHtmlPath} ({fullHtml.Length} bytes)");
        Console.WriteLine($"[PASS] Complete Raw Text saved: {outTextPath}");
        Console.WriteLine($"[PASS] Total Interactive & Semantic Elements: {controls.Count}");
        Console.WriteLine($"\n================== PAGE: {url} ==================");
        Console.WriteLine(bodyText);
        Console.WriteLine("=========================================================================");

        return 0;
    }
}
