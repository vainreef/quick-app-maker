using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.Commands;

public static class SubmitCommand
{
    public static async Task<int> ExecuteAsync(DesiredState desired, string stateRoot, bool confirmSubmit)
    {
        if (!confirmSubmit)
        {
            Console.WriteLine("[WARN] -Action submit requires -ConfirmSubmit switch to execute final submission.");
            return 1;
        }

        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var dom = new DomDriver(client);
        var input = new InputDriver(client, new AxLocator(client, dom));
        var native = new NativeFormAdapter(client, input);
        var inspector = new PageInspector(client);

        string baseUrl = desired.Site.BaseUrl.TrimEnd('/');
        string overviewUrl = $"{baseUrl}/{desired.ProductId}/overview";

        Console.WriteLine($"[NAV] Navigating to Overview page: {overviewUrl}");
        await waiter.NavigateAsync(overviewUrl, "Product Overview Page");
        await Task.Delay(2000);

        // Wait for submit button to be visible and ready
        Console.WriteLine("[INFO] Locating '提交进行认证' (Submit for certification) button...");
        await waiter.RequireAsync(async () =>
        {
            return await client.EvaluateAsync<bool>("""
            (() => {
                const allRoots=[document];
                for(let i=0;i<allRoots.length;i++){ try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){} }
                for(const root of allRoots){
                    for(const b of root.querySelectorAll('button, he-button, [role="button"], a')){
                        const t = (b.innerText || b.getAttribute('aria-label') || '').trim();
                        if (/提交进行认证|Submit for certification/i.test(t)) return true;
                    }
                }
                return false;
            })()
            """);
        }, TimeSpan.FromSeconds(30), "Wait for '提交进行认证' button");

        // 1. Click 【提交进行认证】
        Console.WriteLine("[INFO] Clicking '提交进行认证' (Submit for certification)...");
        bool clicked = await client.EvaluateAsync<bool>("""
        (() => {
            const allRoots=[document];
            for(let i=0;i<allRoots.length;i++){ try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){} }
            for(const root of allRoots){
                for(const b of root.querySelectorAll('button, he-button, [role="button"], a')){
                    const t = (b.innerText || b.getAttribute('aria-label') || '').trim();
                    if (/提交进行认证|Submit for certification/i.test(t)) {
                        const target = b.shadowRoot ? b.shadowRoot.querySelector('button') || b : b;
                        target.scrollIntoView({ block: 'center', inline: 'center' });
                        target.click();
                        return true;
                    }
                }
            }
            return false;
        })()
        """);

        if (!clicked)
        {
            await native.ClickByTextAsync(["提交进行认证", "Submit for certification"], "Submit for certification");
        }

        // 2. Wait for confirmation dialog/modal or status redirect
        Console.WriteLine("[INFO] Waiting for submission confirmation dialog or status redirect...");
        await Task.Delay(3000);

        // Click confirm in modal if modal appears
        await client.EvaluateAsync<bool>("""
        (() => {
            const allRoots=[document];
            for(let i=0;i<allRoots.length;i++){ try { for(const e of allRoots[i].querySelectorAll('*')) if(e.shadowRoot) allRoots.push(e.shadowRoot); } catch(_){} }
            for(const root of allRoots){
                for(const d of root.querySelectorAll('[role="dialog"], .modal-dialog, he-dialog, .ms-Dialog, .ms-Modal')){
                    for(const b of d.querySelectorAll('button, he-button, [role="button"]')){
                        const t = (b.innerText || b.getAttribute('aria-label') || '').trim();
                        if (t === '提交' || t === 'Submit' || t === '确认提交' || t === 'Confirm') {
                            const target = b.shadowRoot ? b.shadowRoot.querySelector('button') || b : b;
                            target.click();
                            return true;
                        }
                    }
                }
            }
            return false;
        })()
        """);

        // 3. Wait for final certification status
        Console.WriteLine("[INFO] Waiting for final certification status...");
        await Task.Delay(5000);

        string finalUrl = await client.EvaluateAsync<string>("location.href") ?? "";
        string bodyText = await client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n================================================================================");
        Console.WriteLine("[SUCCESS] 🎉 微软应用商店提审已成功提交！");
        Console.WriteLine($"[SUCCESS] 产品 ID: {desired.ProductId}");
        Console.WriteLine($"[SUCCESS] 当前页面 URL: {finalUrl}");
        Console.WriteLine("================================================================================\n");
        Console.ResetColor();

        return 0;
    }
}
