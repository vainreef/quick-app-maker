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

    public async Task<ProductIdentityResult> CreateAndReserveProductAsync(string baseUrl, string appName)
    {
        string appsUrl = "https://partner.microsoft.com/zh-cn/dashboard/apps-and-games/overview";
        Console.WriteLine($"[INFO] Navigating to Partner Center Apps & Games overview: {appsUrl}");
        await _waiter.NavigateAsync(appsUrl, "Apps and Games Overview", allowOverviewRedirect: true);
        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("(document.body ? document.body.innerText : '').includes('新产品') || (document.body ? document.body.innerText : '').includes('应用和游戏')");
        }, TimeSpan.FromSeconds(30), "Wait for Apps & Games overview");
        await Task.Delay(2000);

        async Task<bool> IsNameInputVisibleAsync()
        {
            return await _client.EvaluateAsync<bool>("""
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
                const inps = deepAll('input[name="productName"], input#product-name, input[uitestid="productNameInput"], input[type="text"]').filter(visible);
                return inps.length > 0;
            })()
            """);
        }

        bool onNameInputPage = await IsNameInputVisibleAsync();

        if (!onNameInputPage)
        {
            Console.WriteLine("[INFO] Step 1: Locating and physical clicking '+ 新产品'...");
            bool clickedNew = await PhysicalClickElementAsync("""
            () => {
                return deepAll('[data-automation-id="create-new-Application"],[data-automation-id="create-new-application"],[uitestid="createNewApplicationButton"]')
                    .concat(deepAll('button,he-button,a,[role="button"],span'))
                    .find(e => {
                      if (!visible(e)) return false;
                      const t = (((e.innerText||'') + ' ' + (e.getAttribute('aria-label')||'') + ' ' + (e.getAttribute('title')||''))).trim();
                      return /新建产品|新产品/.test(t) && t.length < 25;
                    });
            }
            """, "新产品 按钮");

            if (!clickedNew)
            {
                throw new InvalidOperationException("Failed to click '新产品' button on Apps and Games overview.");
            }

            await Task.Delay(1200);

            Console.WriteLine("[INFO] Step 2: Physical clicking 'MSIX 或 PWA 应用'...");
            bool clickedMsix = await PhysicalClickElementAsync("""
            () => {
                return deepAll('button,he-button,a,[role="button"],li,div[role="option"],div[role="menuitem"],he-menu-item,span')
                    .find(e => visible(e) && (e.innerText || '').includes('MSIX 或 PWA'));
            }
            """, "MSIX 或 PWA 应用 菜单项");

            if (!clickedMsix)
            {
                // Fallback to broader search
                clickedMsix = await PhysicalClickElementAsync("""
                () => {
                    return deepAll('*').find(e => visible(e) && (e.innerText || '').trim() === 'MSIX 或 PWA 应用');
                }
                """, "MSIX 或 PWA 应用 文本");
            }

            if (!clickedMsix)
            {
                throw new InvalidOperationException("Failed to click 'MSIX 或 PWA 应用' menu item.");
            }

            Console.WriteLine("[PASS] Clicked 'MSIX 或 PWA 应用', waiting for Name input form to open...");
            await _waiter.RequireAsync(IsNameInputVisibleAsync, TimeSpan.FromSeconds(20), "Wait for Product Name input field");
        }

        Console.WriteLine($"[INFO] Step 3: Entering product name [{appName}]...");
        await _native.SetFieldAsync([
            "input[name=\"productName\"]",
            "input#product-name",
            "input[uitestid=\"productNameInput\"]",
            "input[type=\"text\"]"
        ], appName, "Product Name");

        await Task.Delay(800);

        Console.WriteLine("[INFO] Step 4: Checking name availability...");
        bool clickedCheck = await PhysicalClickElementAsync("""
        () => {
            return deepAll('button,he-button,a,[role="button"]')
                .find(e => visible(e) && (/检查可用性|检查名称可用性/.test(e.innerText || '')));
        }
        """, "检查可用性 按钮");
        if (!clickedCheck)
        {
            await _native.ClickOptionByDeepTextAsync(["检查可用性", "检查名称可用性"], "检查可用性");
        }

        Console.WriteLine("[INFO] Step 5: Waiting for name availability verification...");
        var checkDeadline = DateTime.UtcNow.AddSeconds(25);
        AvailabilityCheckResult? checkResult = null;
        while (DateTime.UtcNow < checkDeadline)
        {
            checkResult = await _client.EvaluateAsync<AvailabilityCheckResult>("""
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

                // 1. 优先检测失败报警 (名称不可用 / 已被保留 / alert-danger / AlreadyReserved)
                const errorEls = deepAll('.alert-error, .alert-danger, [role="alert"], [data-l10n-key*="AlreadyReserved"], [data-l10n-key*="NotAvailable"], .has-error')
                    .filter(visible);
                for (const el of errorEls) {
                    const txt = (el.innerText || el.textContent || '').trim().replace(/\s+/g, ' ');
                    if (txt.includes('名称不可用') || txt.includes('已被保留') || txt.includes('Already reserved') || txt.includes('Not available') || txt.includes('已被使用') || txt.includes('不可用')) {
                        return { checked: true, available: false, errorMessage: txt };
                    }
                }

                // 2. 检测成功信号 (保留产品名称 按钮已解除禁用 或 绿勾出现)
                const btn = deepAll('button,he-button,a,[role="button"]').find(e => visible(e) && (/保留产品名称|保留名称|Reserve product name/.test(e.innerText || '')));
                if (btn) {
                    const isDisabled = btn.disabled || btn.getAttribute('disabled') !== null || btn.getAttribute('aria-disabled') === 'true' || btn.classList.contains('disabled');
                    if (!isDisabled) {
                        return { checked: true, available: true, errorMessage: '' };
                    }
                }

                const checkMark = deepAll('.win-icon-CheckMark, .win-color-fg-green, [data-l10n-key*="Available"]').find(visible);
                if (checkMark) {
                    return { checked: true, available: true, errorMessage: '' };
                }

                return { checked: false, available: false, errorMessage: '' };
            })()
            """);

            if (checkResult != null && checkResult.Checked)
            {
                break;
            }
            await Task.Delay(400);
        }

        if (checkResult == null || !checkResult.Checked)
        {
            throw new InvalidOperationException("Name availability verification timed out after 25s.");
        }

        if (!checkResult.Available)
        {
            Console.WriteLine($"\n[NAME-UNAVAILABLE] ❌ 微软商店产品名称 [{appName}] 不可用！");
            Console.WriteLine($"[NAME-UNAVAILABLE] 微软后台提示: {checkResult.ErrorMessage}");
            throw new ProductNameUnavailableException(appName, checkResult.ErrorMessage);
        }

        Console.WriteLine("[PASS] Name is available! Proceeding to reserve...");
        await Task.Delay(600);

        Console.WriteLine("[INFO] Step 6: Clicking '保留产品名称' (Reserve product name)...");
        bool clickedReserve = await PhysicalClickElementAsync("""
        () => {
            return deepAll('button,he-button,a,[role="button"]')
                .find(e => visible(e) && (/保留产品名称|保留名称|Reserve product name/.test(e.innerText || '')));
        }
        """, "保留产品名称 按钮");
        if (!clickedReserve)
        {
            await _native.ClickOptionByDeepTextAsync(["保留产品名称", "保留名称", "Reserve product name"], "保留产品名称");
        }

        Console.WriteLine("[INFO] Step 7: Waiting for product reservation to complete and overview page to load...");
        await _waiter.RequireAsync(async () =>
        {
            string url = await _client.EvaluateAsync<string>("location.href") ?? "";
            return (url.Contains("/products/") || url.Contains("/apps/")) && !url.Contains("/create");
        }, TimeSpan.FromSeconds(45), "Wait for Product Overview URL after reservation");

        string? currentUrl = await _client.EvaluateAsync<string>("location.href");
        var match = Regex.Match(currentUrl ?? "", @"/products/([^/?#]+)");
        if (!match.Success)
        {
            match = Regex.Match(currentUrl ?? "", @"/apps/([^/?#]+)");
        }
        if (!match.Success)
        {
            throw new InvalidOperationException($"Failed to extract new ProductId from URL: {currentUrl}");
        }

        string productId = match.Groups[1].Value;
        Console.WriteLine($"[PASS] New product reserved successfully! ProductId: {productId}");

        return await ScrapeIdentityAsync(baseUrl, productId);
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
                identityName: getValByLabel('Package/Identity/Name'),
                publisher: getValByLabel('Package/Identity/Publisher'),
                publisherDisplayName: getValByLabel('Package/Properties/PublisherDisplayName') || getValByLabel('PublisherDisplayName')
            };
        })()
        """);
        if (result == null || string.IsNullOrWhiteSpace(result.IdentityName) || string.IsNullOrWhiteSpace(result.Publisher))
        {
            throw new InvalidOperationException("Failed to scrape Product Identity credentials from Partner Center.");
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
