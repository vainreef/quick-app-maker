using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class ProbeLanguagesCommand
{
    public static async Task<int> ExecuteAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var data = await client.EvaluateAsync<Dictionary<string, object>>("""
        (() => {
          const links = Array.from(document.querySelectorAll('a')).map(a => ({
            text: (a.innerText || '').trim(),
            href: a.href,
            tag: a.tagName,
            parentText: (a.parentElement?.innerText || '').trim().replace(/\s+/g, ' ')
          }));

          const buttons = Array.from(document.querySelectorAll('button, he-button, [role="button"]')).map(b => ({
            text: (b.innerText || b.value || '').trim(),
            tag: b.tagName,
            slot: b.getAttribute('slot') || '',
            parentText: (b.parentElement?.innerText || '').trim().replace(/\s+/g, ' ')
          }));

          return {
            url: location.href,
            title: document.title,
            linksCount: links.length,
            links: links.slice(0, 30),
            buttonsCount: buttons.length,
            buttons: buttons.slice(0, 30)
          };
        })()
        """);

        Console.WriteLine("[PROBE DATA]:");
        Console.WriteLine(JsonSerializer.Serialize(data, Program.JsonIndented));
        return 0;
    }
}
