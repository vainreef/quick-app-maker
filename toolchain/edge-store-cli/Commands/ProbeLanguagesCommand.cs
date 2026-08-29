using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class ProbeLanguagesCommand
{
    public static async Task<int> ExecuteAsync(string stateRoot, DesiredState desired, StoreCheckpoint checkpoint)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        string submissionId = checkpoint.SubmissionId;
        string targetUrl = $"{desired.Site.BaseUrl.TrimEnd('/')}/{desired.ProductId}/submissions/{submissionId}/managelanguages?producttype=app";
        Console.WriteLine($"[NAV] 正在导航到语言管理页面: {targetUrl}");
        await waiter.NavigateAsync(targetUrl, "Manage Store Listing Languages");
        await Task.Delay(3000);

        var data = await client.EvaluateAsync<Dictionary<string, object>>("""
        (() => {
          const allRoots = [document];
          for (let i = 0; i < allRoots.length; i++) {
            try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
          }

          const rows = [];
          for (const r of allRoots) {
            const btns = Array.from(r.querySelectorAll('button, he-button, a, [role="button"]')).filter(e => {
              const t = (e.innerText || e.value || e.getAttribute('aria-label') || '').trim();
              const rect = e.getBoundingClientRect();
              return /^(删除|Delete|Remove)$/i.test(t) && rect.width > 0 && rect.height > 0;
            });

            for (const b of btns) {
              const row = b.closest('tr, li, .list-group-item, .row, .grid-row, div') || b.parentElement;
              const rowText = (row ? row.innerText : '').trim().replace(/\s+/g, ' ');
              rows.push({
                rowText: rowText,
                buttonText: (b.innerText || b.value || '').trim(),
                disabled: b.disabled || b.getAttribute('disabled') !== null
              });
            }
          }

          const allButtons = [];
          for (const r of allRoots) {
            const btns = Array.from(r.querySelectorAll('button, he-button, input[type="button"], input[type="submit"]')).map(b => ({
              text: (b.innerText || b.value || b.getAttribute('aria-label') || '').trim(),
              tag: b.tagName,
              disabled: b.disabled || b.getAttribute('disabled') !== null,
              visible: b.getBoundingClientRect().width > 0
            })).filter(x => x.text && x.visible);
            allButtons.push(...btns);
          }

          return {
            url: location.href,
            title: document.title,
            languagesWithDeleteCount: rows.length,
            languagesWithDelete: rows,
            allButtonsCount: allButtons.length,
            allButtons: allButtons
          };
        })()
        """);

        Console.WriteLine("\n=======================================================");
        Console.WriteLine("[PROBE DATA] 语言管理页面实时现场 DOM 探测报告：");
        Console.WriteLine(JsonSerializer.Serialize(data, Program.JsonIndented));
        Console.WriteLine("=======================================================\n");
        return 0;
    }
}
