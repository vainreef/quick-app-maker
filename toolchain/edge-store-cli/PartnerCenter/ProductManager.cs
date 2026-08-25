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
        string appsUrl = Regex.Replace(baseUrl.TrimEnd('/'), @"/products$", "") + "/apps-and-games/overview";
        Console.WriteLine($"[INFO] Navigating to Partner Center Apps & Games dashboard: {appsUrl}");
        await _waiter.NavigateAsync(appsUrl, "Apps and Games Overview", allowOverviewRedirect: true);

        // Click '+ 新产品' -> 'MSIX 或 PWA 应用'
        Console.WriteLine("[INFO] Locating '+ 新产品' (Create New Application) menu...");
        bool clickedNew = false;
        var clickDeadline = DateTime.UtcNow.AddSeconds(30);
        while (!clickedNew && DateTime.UtcNow < clickDeadline)
        {
            clickedNew = await _client.EvaluateAsync<bool>("""
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
                if (btn) { btn.scrollIntoView({ block: 'center', inline: 'center' }); btn.click(); return true; }
                return false;
            })()
            """);
            if (!clickedNew) await Task.Delay(700);
        }

        if (!clickedNew)
        {
            throw new InvalidOperationException("Could not locate and click the '+ 新产品' (Create New Application) button on the Apps & Games overview page.");
        }

        // Choose 'MSIX 或 PWA 应用' from the type dropdown that opened
        Console.WriteLine("[INFO] Selecting 'MSIX 或 PWA 应用' product type...");
        await _native.ClickOptionByDeepTextAsync(["MSIX 或 PWA 应用"], "MSIX 或 PWA 应用");

        // Wait for product name text field
        Console.WriteLine($"[INFO] Entering product name [{appName}]...");
        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("""
            (() => {
                const inp = document.querySelector('input[name="productName"], input#product-name, input[uitestid="productNameInput"], input[type="text"]');
                return inp !== null && inp.getBoundingClientRect().width > 0;
            })()
            """);
        }, TimeSpan.FromSeconds(30), "Wait for Product Name input field");

        await _native.SetFieldAsync([
            "input[name=\"productName\"]",
            "input#product-name",
            "input[uitestid=\"productNameInput\"]",
            "input[type=\"text\"]"
        ], appName, "Product Name");

        // Click '检查可用性' (Check availability)
        Console.WriteLine("[INFO] Checking name availability...");
        await _native.ClickOptionByDeepTextAsync(["检查可用性"], "检查可用性");

        // Wait for availability confirmation text
        Console.WriteLine("[INFO] Waiting for availability confirmation...");
        await _waiter.RequireAsync(async () =>
        {
            string body = await _client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";
            return body.Contains("可用") || body.Contains("Available") || body.Contains("保留产品名称");
        }, TimeSpan.FromSeconds(30), "Wait for name availability verification");

        // Click '保留产品名称' (Reserve product name)
        Console.WriteLine("[INFO] Clicking '保留产品名称' (Reserve product name)...");
        await _native.ClickOptionByDeepTextAsync(["保留产品名称"], "保留产品名称");

        // Wait for navigation to overview / product management
        Console.WriteLine("[INFO] Waiting for product reservation to complete and overview page to load...");
        await _waiter.RequireAsync(async () =>
        {
            string url = await _client.EvaluateAsync<string>("location.href") ?? "";
            return url.Contains("/products/") && !url.Contains("/create");
        }, TimeSpan.FromSeconds(45), "Wait for Product Overview URL after reservation");

        string? currentUrl = await _client.EvaluateAsync<string>("location.href");
        var match = Regex.Match(currentUrl ?? "", @"/products/([^/?#]+)");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Failed to extract new ProductId from URL: {currentUrl}");
        }

        string productId = match.Groups[1].Value;
        Console.WriteLine($"[PASS] New product reserved successfully! ProductId: {productId}");

        // Navigate to product identity page to extract credentials
        return await ScrapeIdentityAsync(baseUrl, productId);
    }

    public async Task<ProductIdentityResult> ScrapeIdentityAsync(string baseUrl, string productId)
    {
        string identityUrl = $"{baseUrl.TrimEnd('/')}/{productId}/identity";
        Console.WriteLine($"[INFO] Navigating to Product Identity page: {identityUrl}");
        await _waiter.NavigateAsync(identityUrl, "Product Identity Page");

        // Wait for identity rows AND for the value cells to render (values render slightly after the labels)
        await _waiter.RequireAsync(async () =>
        {
            string body = await _client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";
            return (body.Contains("Package/Identity/Name") || body.Contains("Package/Identity/Publisher")) &&
                   !body.Contains("Skip to main content\n\n");
        }, TimeSpan.FromSeconds(30), "Wait for Identity table labels to load");

        {
            var idDeadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < idDeadline)
            {
                bool hasValue = await _client.EvaluateAsync<bool>("""
                (() => {
                    const rows = Array.from(document.querySelectorAll('table.app-identity tr'));
                    for (const r of rows) {
                        const tds = Array.from(r.children).filter(c => c.tagName === 'TD');
                        if (tds.length >= 2) {
                            const val = (tds[tds.length-1].innerText || '').trim();
                            if (val) return true;
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
            function getValByLabel(label) {
                const trs = Array.from(document.querySelectorAll('tr'));
                for (const tr of trs) {
                    const tds = Array.from(tr.children).filter(c => c.tagName === 'TD');
                    if (tds.length >= 2) {
                        const lab = (tds[0].innerText || '').trim();
                        if (!lab) continue;
                        const normalized = lab.replace(/\s+/g, ' ').trim();
                        if (normalized === label || normalized.endsWith(label)) {
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
