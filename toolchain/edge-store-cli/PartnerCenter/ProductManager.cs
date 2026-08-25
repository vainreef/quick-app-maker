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
        string appsUrl = $"{baseUrl.TrimEnd('/')}/apps-and-games";
        Console.WriteLine($"[INFO] Navigating to Partner Center Apps & Games dashboard: {appsUrl}");
        await _waiter.NavigateAsync(appsUrl, "Apps and Games Overview", allowOverviewRedirect: true);

        // Click '+ 新产品' -> 'MSIX 或 PWA 应用'
        Console.WriteLine("[INFO] Locating '+ 新产品' (Create New Application) menu...");
        bool clickedNew = await _client.EvaluateAsync<bool>("""
        (() => {
            const btn = document.querySelector('[data-automation-id="create-new-Application"]') ||
                        Array.from(document.querySelectorAll('button, he-button, a')).find(e => {
                            const t = (e.innerText || '').trim();
                            return t.includes('新建产品') || (t.includes('新产品') && t.length < 15);
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
            // Try expanding dropdown first if needed
            await _native.ClickStrictAsync([
                "button[data-automation-id=\"create-new-product\"]",
                "he-button[data-automation-id=\"create-new-product\"]",
                "button:has-text(\"+ 新产品\")",
                "button:has-text(\"新产品\")"
            ], "Open '+ 新产品' menu");
            await Task.Delay(500);

            await _native.ClickStrictAsync([
                "[data-automation-id=\"create-new-Application\"]",
                "button[data-automation-id=\"create-new-Application\"]",
                "a[data-automation-id=\"create-new-Application\"]",
                "a:has-text(\"MSIX 或 PWA\")"
            ], "Select 'MSIX 或 PWA 应用'");
        }

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
        await _native.ClickStrictAsync([
            "button[data-l10n-key=\"CheckAvailability\"]",
            "button[uitestid=\"checkAvailabilityButton\"]",
            "button:has-text(\"检查可用性\")",
            "he-button:has-text(\"检查可用性\")"
        ], "Check availability button");

        // Wait for availability confirmation text
        Console.WriteLine("[INFO] Waiting for availability confirmation...");
        await _waiter.RequireAsync(async () =>
        {
            string body = await _client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";
            return body.Contains("可用") || body.Contains("Available") || body.Contains("保留产品名称");
        }, TimeSpan.FromSeconds(30), "Wait for name availability verification");

        // Click '保留产品名称' (Reserve product name)
        Console.WriteLine("[INFO] Clicking '保留产品名称' (Reserve product name)...");
        await _native.ClickStrictAsync([
            "button[data-l10n-key=\"ReserveName\"]",
            "button[uitestid=\"reserveProductNameButton\"]",
            "button:has-text(\"保留产品名称\")",
            "he-button:has-text(\"保留产品名称\")"
        ], "Reserve product name button");

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

        // Wait for identity data rows to render
        await _waiter.RequireAsync(async () =>
        {
            string body = await _client.EvaluateAsync<string>("document.body ? document.body.innerText : ''") ?? "";
            return (body.Contains("Package/Identity/Name") || body.Contains("Package/Identity/Publisher")) &&
                   !body.Contains("Skip to main content\n\n");
        }, TimeSpan.FromSeconds(30), "Wait for Identity table values to load");

        var result = await _client.EvaluateAsync<ProductIdentityResult>("""
        (() => {
            function getValByLabel(label) {
                const trs = Array.from(document.querySelectorAll('tr, .row, [role="row"], div.field'));
                for (const tr of trs) {
                    const t = tr.innerText || '';
                    if (t.includes(label)) {
                        const code = tr.querySelector('code, .value, td:last-child, span.selectable');
                        if (code) return (code.innerText || '').trim();
                        const parts = t.split(/[:\t\n]+/).map(s => s.trim()).filter(Boolean);
                        if (parts.length >= 2) return parts[parts.length - 1];
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
                publisherDisplayName: getValByLabel('PublisherDisplayName') || getValByLabel('Package/Properties/PublisherDisplayName')
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
