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
            string appsUrl = "https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview";
            if (!currentUrl.Contains("/apps-and-games/overview"))
            {
                Console.WriteLine($"[INFO] 正在导航至合作伙伴中心应用总览: {appsUrl}");
                await _waiter.NavigateAsync(appsUrl, "Apps & Games Overview", allowOverviewRedirect: true);
            }

            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("[INFO] 开始检测 Partner Center 页面加载状态...");

            // 阶段 1：等待 document.readyState === 'complete'
            Console.WriteLine("[LOAD-CHECK 1/3] 检测 DOM readyState 状态...");
            await _waiter.RequireAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("document.readyState === 'complete'");
            }, TimeSpan.FromSeconds(30), "等待 document.readyState === 'complete'");
            Console.WriteLine($"[LOAD-CHECK 1/3] [OK] DOM readyState 已完成 ({loadStopwatch.Elapsed.TotalSeconds:F1}s)");

            // 阶段 2：等待主内容区与页面文本挂载完毕
            Console.WriteLine("[LOAD-CHECK 2/3] 等待主内容区与标题挂载...");
            await _waiter.RequireAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("""
                (() => {
                    const text = (document.body ? document.body.innerText : '') || '';
                    return text.includes('应用和游戏') || text.includes('产品') || text.includes('Apps & games');
                })()
                """);
            }, TimeSpan.FromSeconds(30), "等待主内容区与标题挂载");
            Console.WriteLine($"[LOAD-CHECK 2/3] [OK] 主内容区与标题已挂载 ({loadStopwatch.Elapsed.TotalSeconds:F1}s)");

            // 阶段 3：等待【+ 新产品】按钮与搜索栏渲染就绪
            Console.WriteLine("[LOAD-CHECK 3/3] 等待【+ 新产品】按钮与操作栏渲染就绪...");
            await _waiter.RequireAsync(async () =>
            {
                return await _client.EvaluateAsync<bool>("""
                (() => {
                    const btns = Array.from(document.querySelectorAll('button, he-button, [role="button"]'));
                    return btns.some(b => {
                        const t = (b.innerText || b.getAttribute('aria-label') || '').trim();
                        const r = b.getBoundingClientRect();
                        return (t.includes('新产品') || t.includes('新建产品') || t.includes('New product')) && r.width > 0 && r.height > 0 && !b.disabled;
                    });
                })()
                """);
            }, TimeSpan.FromSeconds(30), "等待【+ 新产品】按钮就绪");
            Console.WriteLine($"[LOAD-CHECK 3/3] [OK] 【+ 新产品】按钮已渲染就绪 ({loadStopwatch.Elapsed.TotalSeconds:F1}s)");

            // 额外留出 3 秒确保 Angular 指令与事件监听器全部绑定完毕
            Console.WriteLine("[INFO] 正在等待 3 秒以确保 Angular 事件监听全部绑定完成...");
            await Task.Delay(3000);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[CONFIRM] ================================================================================");
            Console.WriteLine($"[CONFIRM] [OK] 确认页面已完全加载完毕并就绪！总耗时: {loadStopwatch.Elapsed.TotalSeconds:F1} 秒");
            Console.WriteLine($"[CONFIRM] ================================================================================\n");
            Console.ResetColor();

            // 2. 循环触发【+ 新产品】下拉菜单展开，直到菜单项切实可见
            Console.WriteLine("[INFO] 正在触发【+ 新产品】按钮并确认下拉菜单展开...");
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                bool isItemVisible = await _client.EvaluateAsync<bool>("""
                (() => {
                    const allRoots = [document];
                    for (let i = 0; i < allRoots.length; i++) {
                        try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
                    }
                    for (const r of allRoots) {
                        const items = Array.from(r.querySelectorAll('button, [role="menuitem"], li, a, span'));
                        const target = items.find(x => {
                            const t = (x.innerText || x.getAttribute('aria-label') || '').trim();
                            const rect = x.getBoundingClientRect();
                            return t.includes('MSIX') && (t.includes('应用') || t.includes('App')) && rect.width > 50 && rect.height > 15;
                        });
                        if (target) return true;
                    }
                    return false;
                })()
                """);

                if (isItemVisible)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[CONFIRM] [OK] 确认【+ 新产品】下拉菜单已成功展开！");
                    Console.ResetColor();
                    break;
                }

                Console.WriteLine($"[INFO] 正在执行第 {attempt} 次点击【+ 新产品】按钮（坐标派发 + 按键交互）...");
                try
                {
                    await PhysicalClickElementAsync("""
                    () => {
                        const btns = deepAll('button, he-button, [role="button"], a');
                        return btns.find(x => {
                            const t = (((x.innerText||'') + ' ' + (x.getAttribute('aria-label')||'') + ' ' + (x.getAttribute('title')||''))).trim();
                            return /新建产品|新产品|New product/i.test(t) && visible(x);
                        });
                    }
                    """, "【+ 新产品】按钮");
                }
                catch
                {
                    // Fallback to JS click
                    await _client.EvaluateAsync<bool>("""
                    (() => {
                        const btns = Array.from(document.querySelectorAll('button, he-button, [role="button"], a'));
                        const b = btns.find(x => /新建产品|新产品|New product/i.test((x.innerText || x.getAttribute('aria-label') || '').trim()));
                        if (b) { b.click(); return true; }
                        return false;
                    })()
                    """);
                }

                await Task.Delay(1000);
            }

            // 3. 循环点击【MSIX 或 PWA 应用】菜单项，直到真实弹窗（包含检查可用性按钮与输入框）出现在屏幕上
            Console.WriteLine("[INFO] 正在点击【MSIX 或 PWA 应用】菜单项并确权弹窗出现...");
            bool modalOpened = false;
            for (int attempt = 1; attempt <= 10; attempt++)
            {
                bool isDialogTrulyVisible = await _client.EvaluateAsync<bool>("""
                (() => {
                    const allRoots = [document];
                    for (let i = 0; i < allRoots.length; i++) {
                        try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
                    }
                    for (const r of allRoots) {
                        const dialogs = Array.from(r.querySelectorAll('[role="dialog"], .ms-Dialog, .modal-dialog, name-input, he-dialog, .ms-Modal'));
                        for (const d of dialogs) {
                            const rect = d.getBoundingClientRect();
                            if (rect.width > 200 && rect.height > 150) {
                                const text = (d.innerText || '').trim();
                                const inputs = Array.from(d.querySelectorAll('input:not([type="hidden"])'));
                                const hasCheckBtn = text.includes('检查可用性') || text.includes('Check availability') || text.includes('保留产品名称') || text.includes('Reserve product name');
                                if (inputs.length > 0 && hasCheckBtn) {
                                    return true;
                                }
                            }
                        }
                    }
                    return false;
                })()
                """);

                if (isDialogTrulyVisible)
                {
                    modalOpened = true;
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[CONFIRM] [OK] 确认名称预留对话框（含输入框与【检查可用性】按钮）已切实渲染在屏幕正中央！");
                    Console.ResetColor();
                    break;
                }

                Console.WriteLine($"[INFO] 正在执行第 {attempt} 次点击【MSIX 或 PWA 应用】菜单项...");
                try
                {
                    await PhysicalClickElementAsync("""
                    () => {
                        const items = deepAll('button, [role="menuitem"], li, a, span');
                        return items.find(x => {
                            const t = (x.innerText || x.getAttribute('aria-label') || '').trim();
                            return t.includes('MSIX') && (t.includes('应用') || t.includes('App')) && visible(x);
                        });
                    }
                    """, "【MSIX 或 PWA 应用】菜单项");
                }
                catch
                {
                    await _client.EvaluateAsync<bool>("""
                    (() => {
                        const items = Array.from(document.querySelectorAll('button, [role="menuitem"], li, a, span'));
                        const msixItem = items.find(x => (x.innerText || x.getAttribute('aria-label') || '').trim().includes('MSIX'));
                        if (msixItem) {
                            (msixItem.closest('button, a, [role="menuitem"]') || msixItem).click();
                            return true;
                        }
                        return false;
                    })()
                    """);
                }

                await Task.Delay(1200);
            }

            // 4. 强制断言弹窗输入框必须真实存在
            if (!modalOpened)
            {
                throw new InvalidOperationException("经过多次重试，未能成功弹出【名称预留对话框】。请检查页面 DOM 状态。");
            }

            // 5. 自动将光标聚焦到弹窗内的名称输入框
            await _client.EvaluateAsync<bool>("""
            (() => {
                const allRoots = [document];
                for (let i = 0; i < allRoots.length; i++) {
                    try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
                }
                for (const r of allRoots) {
                    const dialogs = Array.from(r.querySelectorAll('[role="dialog"], .ms-Dialog, .modal-dialog, name-input, he-dialog, .ms-Modal'));
                    for (const d of dialogs) {
                        const input = d.querySelector('input:not([type="hidden"])');
                        if (input && !input.disabled) {
                            input.focus();
                            input.click();
                            return true;
                        }
                    }
                }
                return false;
            })()
            """);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n================================================================================");
            Console.WriteLine("【用户操作指引：请在已打开的 Edge 浏览器中完成名称预留】");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("1. 浏览器中名称预留弹窗已 100% 确认打开，光标已聚焦到输入框；");
            Console.WriteLine("2. 请在输入框中输入您期望的应用名称（如：Qiangua - 牵挂桌面记事与倒数日）；");
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
