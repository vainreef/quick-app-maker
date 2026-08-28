using System.Text.Json;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;

namespace Vainreef.EdgeStore.PartnerCenter;

public class ProductManager
{
    private readonly CdpClient _client;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;

    public ProductManager(CdpClient client, Waiter waiter, NativeFormAdapter native)
    {
        _client = client;
        _waiter = waiter;
        _native = native;
    }

    public async Task<ProductIdentityResult> UserAssistedReserveProductAsync(string baseUrl, string? suggestedAppName = null)
    {
        string currentUrl = await _client.EvaluateAsync<string>("location.href") ?? "";
        var initialMatch = Regex.Match(currentUrl, @"/products/([^/?#]+)");
        if (!initialMatch.Success) initialMatch = Regex.Match(currentUrl, @"/apps/([^/?#]+)");

        if (!initialMatch.Success)
        {
            if (!currentUrl.Contains("/apps-and-games/overview"))
            {
                string appsUrl = "https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview";
                Console.WriteLine($"[INFO] Navigating to Partner Center Apps & Games overview: {appsUrl}");
                await _waiter.NavigateAsync(appsUrl, "Apps and Games Overview", allowOverviewRedirect: true);
                await _waiter.RequireAsync(async () =>
                {
                    return await _client.EvaluateAsync<bool>("(document.body ? document.body.innerText : '').includes('新产品') || (document.body ? document.body.innerText : '').includes('应用和游戏')");
                }, TimeSpan.FromSeconds(30), "Wait for Apps & Games overview");
                await Task.Delay(1500);
            }

            // 检查弹窗是否已经打开，若未打开则帮用户点开
            bool isModalOpen = await _client.EvaluateAsync<bool>("""
            (() => {
                return document.querySelector('name-input, .modal-dialog, [role="dialog"]') !== null;
            })()
            """);

            if (!isModalOpen)
            {
                Console.WriteLine("[INFO] 正在为您点开【+ 新产品】 -> 【MSIX 或 PWA 应用】名称预留弹窗...");
                await PhysicalClickElementAsync("""
                () => {
                    return deepAll('button[data-automation-id="create-new-button"], [data-automation-id="create-new-Application"], [data-automation-id="create-new-application"], [uitestid="createNewApplicationButton"]')
                        .concat(deepAll('button,he-button,a,[role="button"],span'))
                        .find(e => {
                          if (!visible(e)) return false;
                          const t = (((e.innerText||'') + ' ' + (e.getAttribute('aria-label')||'') + ' ' + (e.getAttribute('title')||''))).trim();
                          return /新建产品|新产品/.test(t) && t.length < 25;
                        });
                }
                """, "新产品 按钮");
                await Task.Delay(1000);
                await PhysicalClickElementAsync("""
                () => {
                    return deepAll('button[value="MSIX_PWA_App"], [data-automation-id="create-new-Application"] button, button[value*="MSIX"]')
                        .concat(deepAll('button[role="menuitem"], li[role="none"] > button'))
                        .find(e => visible(e) && (e.innerText || '').includes('MSIX 或 PWA'));
                }
                """, "MSIX 或 PWA 应用 菜单项");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("【用户操作指引：请在已打开的 Edge 浏览器中完成名称预留】");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("1. 浏览器中名称预留弹窗已准备就绪；");
            Console.WriteLine("2. 请在输入框中输入您期望的应用名称（建议加副标题/定位，如：牵挂 - 桌面便签...）；");
            Console.WriteLine("3. 点击【检查可用性】确认绿标通过；");
            Console.WriteLine("4. 点击【保留产品名称】创建应用；");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine(">>> 脚本正在后台自动监听，您保留成功并进入产品页面后将自动接管后续所有提审流程！");
            Console.WriteLine("================================================================================\n");
            Console.ResetColor();

            // 监听进入产品详情页（最长等待 10 分钟）
            await _waiter.RequireAsync(async () =>
            {
                string url = await _client.EvaluateAsync<string>("location.href") ?? "";
                return (url.Contains("/products/") || url.Contains("/apps/")) && !url.Contains("/create") && !url.Contains("/apps-and-games/overview");
            }, TimeSpan.FromMinutes(10), "等待用户在浏览器中完成名称预留并进入产品页面");
        }

        currentUrl = await _client.EvaluateAsync<string>("location.href") ?? "";
        var match = Regex.Match(currentUrl, @"/products/([^/?#]+)");
        if (!match.Success)
        {
            match = Regex.Match(currentUrl, @"/apps/([^/?#]+)");
        }
        if (!match.Success)
        {
            throw new InvalidOperationException($"无法从当前 URL [{currentUrl}] 中提取 ProductId。");
        }

        string productId = match.Groups[1].Value;
        Console.WriteLine($"\n[PASS] 成功检测到新建产品！ProductId: {productId}");

        // 提取页面上的产品名称
        string actualProductName = await _client.EvaluateAsync<string>("""
        (() => {
            const el = document.querySelector('he-page-title, h1, [slot="active-title"], .page-title');
            return (el ? (el.innerText || el.textContent || '') : '').trim();
        })()
        """) ?? "";

        if (string.IsNullOrWhiteSpace(actualProductName))
        {
            actualProductName = suggestedAppName ?? "牵挂";
        }
        Console.WriteLine($"[PASS] 捕获到产品名称: {actualProductName}");

        Console.WriteLine($"[INFO] 正在抓取官方 Identity (Package/Identity/Name, Publisher, PublisherDisplayName)...");
        var result = await ScrapeIdentityAsync(baseUrl, productId);
        result.ProductName = actualProductName;
        return result;
    }

    public async Task<ProductIdentityResult> CreateAndReserveProductAsync(string baseUrl, string appName)
    {
        return await UserAssistedReserveProductAsync(baseUrl, appName);
    }

    public async Task<ProductIdentityResult> ScrapeIdentityAsync(string baseUrl, string productId)
    {
        string identityUrl = $"{baseUrl.TrimEnd('/')}/{productId}/identity";
        Console.WriteLine($"[INFO] Navigating to Product Identity page: {identityUrl}");
        await _waiter.NavigateAsync(identityUrl, "Product Identity Page");

        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("""
            (() => {
                const bodyText = (document.body ? document.body.innerText : '') || '';
                const allRoots = [document];
                for (let i = 0; i < allRoots.length; i++) {
                  try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
                }
                let allText = bodyText;
                for (const root of allRoots) {
                  try {
                    for (const el of root.querySelectorAll('*')) {
                      allText += ' ' + (el.innerText || el.textContent || '');
                    }
                  } catch (_) {}
                }
                return (allText.includes("Package/Identity/Name") || allText.includes("Identity/Name")) &&
                       !allText.includes("Skip to main content\n\n");
            })()
            """);
        }, TimeSpan.FromSeconds(30), "Wait for Identity table labels to load");

        {
            var idDeadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < idDeadline)
            {
                bool hasValue = await _client.EvaluateAsync<bool>("""
                (() => {
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
                    const rows = deepAll('table.app-identity tr, tr');
                    for (const r of rows) {
                        const tds = Array.from(r.children).filter(c => c.tagName === 'TD');
                        if (tds.length >= 2) {
                            const val = (tds[tds.length-1].innerText || '').trim();
                            if (val && !val.includes('Identity/Name')) return true;
                        }
                    }
                    return false;
                })()
                """);
                if (hasValue) break;
                await Task.Delay(1200);
            }
        }

        var result = await _client.EvaluateAsync<ProductIdentityResult>("""
        (() => {
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

            function getValByLabel(label) {
                const trs = deepAll('tr');
                for (const tr of trs) {
                    const tds = Array.from(tr.children).filter(c => c.tagName === 'TD');
                    if (tds.length >= 2) {
                        const lab = (tds[0].innerText || '').trim();
                        if (!lab) continue;
                        const normalized = lab.replace(/\s+/g, ' ').trim();
                        if (normalized === label || normalized.endsWith(label) || normalized.includes(label)) {
                            return (tds[tds.length - 1].innerText || '').trim();
                        }
                    }
                }
                return '';
            }

            const urlMatch = location.href.match(/\/products\/([^\/?#]+)/);
            const pid = urlMatch ? urlMatch[1] : '';

            return {
                productId: pid,
                productName: '',
                identityName: getValByLabel('Package/Identity/Name'),
                publisher: getValByLabel('Package/Identity/Publisher'),
                publisherDisplayName: getValByLabel('Package/Properties/PublisherDisplayName') || getValByLabel('PublisherDisplayName')
            };
        })()
        """);
        if (result == null || string.IsNullOrWhiteSpace(result.IdentityName) || string.IsNullOrWhiteSpace(result.Publisher))
        {
            throw new InvalidOperationException($"Failed to scrape identity fields for ProductId [{productId}]. Make sure Product Identity table has loaded.");
        }

        Console.WriteLine($"[PASS] Scraped Package Identity:");
        Console.WriteLine($"  * Product ID:               {result.ProductId}");
        Console.WriteLine($"  * Package/Identity/Name:    {result.IdentityName}");
        Console.WriteLine($"  * Package/Identity/Publisher: {result.Publisher}");
        Console.WriteLine($"  * PublisherDisplayName:     {result.PublisherDisplayName}");

        return result;
    }

    private async Task<bool> PhysicalClickElementAsync(string jsFinder, string label)
    {
        var rect = await _client.EvaluateAsync<ElementRect>($$"""
        (() => {
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
            const visible = e => { const r=e.getBoundingClientRect(), s=getComputedStyle(e); return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden'; };
            const el = ({{jsFinder}})();
            if (!el) return null;
            el.scrollIntoView({ block: 'center', inline: 'center' });
            const r = el.getBoundingClientRect();
            return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);

        if (rect == null || rect.Width <= 0 || rect.Height <= 0) return false;

        int ix = (int)Math.Round(rect.X);
        int iy = (int)Math.Round(rect.Y);

        await _client.SendAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x = ix, y = iy });
        await Task.Delay(40);
        await _client.SendAsync("Input.dispatchMouseEvent", new { type = "mousePressed", button = "left", clickCount = 1, x = ix, y = iy });
        await Task.Delay(40);
        await _client.SendAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", button = "left", clickCount = 1, x = ix, y = iy });
        Console.WriteLine($"[PHYSICAL-CLICK] Clicked [{label}] at ({ix}, {iy})");
        return true;
    }
}

public class ElementRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ProductIdentityResult
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string IdentityName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string PublisherDisplayName { get; set; } = string.Empty;
}

public class AvailabilityCheckResult
{
    public bool Checked { get; set; }
    public bool Available { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ProductNameUnavailableException : Exception
{
    public string AppName { get; }
    public string Detail { get; }

    public ProductNameUnavailableException(string appName, string detail)
        : base($"Microsoft Store Product Name '{appName}' is unavailable: {detail}")
    {
        AppName = appName;
        Detail = detail;
    }
}
