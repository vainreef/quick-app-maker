using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class PackageCleanerCommand
{
    public static async Task<int> ExecuteAsync(DesiredState desired, string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));
        var native = new NativeFormAdapter(client, input);
        var checkbox = new HeCheckboxAdapter(client, input);

        Console.WriteLine("[INFO] Packages Cleaner: Checking active uploads and duplicate rows...");

        // 1. Cancel in-progress upload if present
        await client.EvaluateAsync<bool>("""
        (() => {
            const cancel = Array.from(document.querySelectorAll('a.upload-action, button, [role="button"]')).find(e => (e.innerText || '').trim().toLowerCase() === 'cancel');
            if (cancel) { cancel.click(); return true; }
            return false;
        })()
        """);
        await Task.Delay(1000);

        // 2. Remove duplicate package rows until only 1 remains
        for (int i = 0; i < 6; i++)
        {
            bool removed = await client.EvaluateAsync<bool>("""
            (() => {
                const btns = Array.from(document.querySelectorAll('button, a, [role="button"]')).filter(e => {
                    const t = (e.innerText || '').trim().toLowerCase();
                    const r = e.getBoundingClientRect();
                    return (t === 'remove' || t === '删除') && r.width > 0 && r.height > 0 && !e.disabled;
                });
                if (btns.length > 1) {
                    btns[btns.length - 1].click();
                    return true;
                }
                return false;
            })()
            """);
            if (removed) await Task.Delay(2000);
            else break;
        }

        // 3. Confirm Device families
        await checkbox.SetCheckedAsync("Windows 10/11 Desktop", true, "Windows 10/11 Desktop");
        await checkbox.SetCheckedAsync("future device families", true, "future device families");

        // 4. Click Save
        Console.WriteLine("[INFO] Clicking Save packages...");
        bool clicked = await client.EvaluateAsync<bool>("""
        (() => {
            const saveBtn = Array.from(document.querySelectorAll('input[type="button"], input[type="submit"], button, [role="button"]')).find(e => {
                const val = (e.value || e.innerText || e.getAttribute('aria-label') || '').trim();
                return /^(Save|保存|保存草稿)$/i.test(val);
            });
            if (saveBtn) {
                saveBtn.disabled = false;
                saveBtn.removeAttribute('disabled');
                saveBtn.click();
                return true;
            }
            return false;
        })()
        """);

        if (!clicked)
        {
            var saveRect = await client.EvaluateAsync<JsElementRect>("""
            (() => {
                const saveBtn = Array.from(document.querySelectorAll('input[type="button"], input[type="submit"], button, [role="button"]')).find(e => {
                    const val = (e.value || e.innerText || e.getAttribute('aria-label') || '').trim();
                    return /^(Save|保存|保存草稿)$/i.test(val);
                });
                if (saveBtn) {
                    const r = saveBtn.getBoundingClientRect();
                    return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
                }
                return null;
            })()
            """);
            if (saveRect != null)
            {
                await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save packages");
            }
        }

        await Task.Delay(3500);

        // 5. Explicitly navigate strictly to Product Overview on Partner Center (never following external hrefs!)
        string baseUrl = desired.Site.BaseUrl.TrimEnd('/');
        string overviewUrl = $"{baseUrl}/{desired.ProductId}/overview";
        Console.WriteLine($"[INFO] Navigating strictly to Product Overview: {overviewUrl}");
        await waiter.NavigateAsync(overviewUrl, "Product Overview after packages save");

        var inspector = new PageInspector(client);
        var snapshot = await inspector.CaptureAsync();
        Console.WriteLine("[DOM-EXTRACTED] Overview DOM Snapshot:");
        Console.WriteLine(JsonSerializer.Serialize(snapshot, Program.JsonIndented));

        return 0;
    }
}
