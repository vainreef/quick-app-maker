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
        await Task.Delay(3000);

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
            Console.WriteLine("[INFO] Step 1: Locating and clicking '+ 新产品' (Create New Application)...");
            bool step1Ok = false;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                bool clickedNew = await _client.EvaluateAsync<bool>("""
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
                    const btn = deepAll('[data-automation-id="create-new-Application"],[data-automation-id="create-new-application"],[uitestid="createNewApplicationButton"]')
                            .concat(deepAll('button,he-button,a,[role="button"]'))
                            .find(e => {
                              if (!visible(e)) return false;
                              const t = (((e.innerText||'') + ' ' + (e.getAttribute('aria-label')||'') + ' ' + (e.getAttribute('title')||''))).trim();
                              return /新建产品|新产品/.test(t) && t.length < 25;
                            });
                    if (btn) {
                        btn.scrollIntoView({ block: 'center', inline: 'center' });
                        btn.click();
                        return true;
                    }
                    return false;
                })()
                """);

                if (!clickedNew)
                {
                    await Task.Delay(1000);
                    continue;
                }

                Console.WriteLine("[INFO] Step 2: Verifying menu opened & selecting 'MSIX 或 PWA 应用'...");
                bool menuOpened = false;
                var menuDeadline = DateTime.UtcNow.AddSeconds(6);
                while (DateTime.UtcNow < menuDeadline)
                {
                    menuOpened = await _client.EvaluateAsync<bool>("""
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
                        const opt = deepAll('button,he-button,a,[role="button"],li,div[role="option"],he-menu-item')
                            .find(e => visible(e) && (e.innerText || '').includes('MSIX'));
                        return opt !== undefined;
                    })()
                    """);
                    if (menuOpened) break;
                    await Task.Delay(400);
                }

                if (menuOpened)
                {
                    Console.WriteLine("[PASS] Menu opened successfully. Clicking 'MSIX 或 PWA 应用'...");
                    await _native.ClickOptionByDeepTextAsync(["MSIX 或 PWA 应用", "MSIX"], "MSIX 或 PWA 应用");

                    var inputDeadline = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < inputDeadline)
                    {
                        if (await IsNameInputVisibleAsync())
                        {
                            step1Ok = true;
                            Console.WriteLine("[PASS] Product Name form / dialog is now active!");
                            break;
                        }
                        await Task.Delay(400);
                    }
                    if (step1Ok) break;
                }
                else
                {
                    Console.WriteLine("[WARN] '+ 新产品' menu did not expand, retrying...");
                    await Task.Delay(1000);
                }
            }
        }

        Console.WriteLine($"[INFO] Step 3: Entering product name [{appName}]...");
        await _waiter.RequireAsync(IsNameInputVisibleAsync, TimeSpan.FromSeconds(30), "Wait for Product Name input field");

        await _native.SetFieldAsync([
            "input[name=\"productName\"]",
            "input#product-name",
            "input[uitestid=\"productNameInput\"]",
            "input[type=\"text\"]"
        ], appName, "Product Name");

        await Task.Delay(800);

        Console.WriteLine("[INFO] Step 4: Checking name availability...");
        await _native.ClickOptionByDeepTextAsync(["检查可用性", "检查名称可用性"], "检查可用性");

        Console.WriteLine("[INFO] Step 5: Waiting for availability confirmation...");
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
                return allText.includes('可用') || allText.includes('Available') || allText.includes('保留产品名称') || allText.includes('Reserve');
            })()
            """);
        }, TimeSpan.FromSeconds(30), "Wait for name availability verification");

        Console.WriteLine("[INFO] Step 6: Clicking '保留产品名称' (Reserve product name)...");
        await _native.ClickOptionByDeepTextAsync(["保留产品名称", "保留名称", "Reserve product name"], "保留产品名称");

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
}

public class ProductIdentityResult
{
    public string ProductId { get; set; } = string.Empty;
    public string IdentityName { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string PublisherDisplayName { get; set; } = string.Empty;
}
