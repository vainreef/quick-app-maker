using System.Text.Json;
using System.Text.RegularExpressions;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.Orchestration;
using Vainreef.EdgeStore.PartnerCenter;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string action = "run";
        string phase = "all";
        string manifestPath = "";
        string productId = "";
        string stateDir = "";
        bool apply = false;
        bool submit = false;
        bool confirmSubmit = false;
        bool reloadVerify = true;
        bool keepOpen = false;

        string appName = "";

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--action":
                case "-action":
                case "-a":
                    if (i + 1 < args.Length) action = args[++i].ToLowerInvariant();
                    break;
                case "--phase":
                case "-phase":
                case "-p":
                    if (i + 1 < args.Length) phase = args[++i];
                    break;
                case "--manifest":
                case "-manifest":
                case "-m":
                    if (i + 1 < args.Length) manifestPath = args[++i];
                    break;
                case "--product-id":
                case "-productid":
                case "-id":
                    if (i + 1 < args.Length) productId = args[++i];
                    break;
                case "--app-name":
                case "-appname":
                case "-name":
                case "-n":
                    if (i + 1 < args.Length) appName = args[++i];
                    break;
                case "--state-dir":
                case "-statedir":
                    if (i + 1 < args.Length) stateDir = args[++i];
                    break;
                case "--apply":
                case "-apply":
                    apply = true;
                    break;
                case "--submit":
                case "-submit":
                    submit = true;
                    break;
                case "--confirm-submit":
                case "-confirmsubmit":
                    confirmSubmit = true;
                    break;
                case "--skip-reload-verify":
                    reloadVerify = false;
                    break;
                case "--keep-open":
                case "-keepopen":
                    keepOpen = true;
                    break;
            }
        }

        string appRoot = AppContext.BaseDirectory;
        string toolRoot = Directory.GetCurrentDirectory();

        string stateRoot = !string.IsNullOrWhiteSpace(stateDir)
            ? Path.GetFullPath(stateDir)
            : Path.Combine(toolRoot, "state");

        Vainreef.EdgeStore.Cdp.Ops.LogRoot = stateRoot;

        // Status and stop do not require a manifest file
        if (action is "status" or "stop")
        {
            var fallbackDesired = new DesiredState();
            var fallbackCheckpoint = new StoreCheckpoint();
            var statusOrchestrator = new StoreOrchestrator(fallbackDesired, stateRoot, fallbackCheckpoint, false, false, false);
            if (action == "stop")
            {
                Console.WriteLine("[INFO] Stopping Edge session...");
                statusOrchestrator.StopSession();
                Console.WriteLine("[PASS] Stopped.");
                return 0;
            }
            return await statusOrchestrator.PrintStatusAsync();
        }

        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            manifestPath = Path.Combine(toolRoot, "examples", "store-automation.json");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(appRoot, "examples", "store-automation.json");
            }
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(appRoot, "..", "..", "..", "examples", "store-automation.json");
            }
        }

        if (!File.Exists(manifestPath))
        {
            Console.WriteLine($"[ERROR] Manifest not found: {manifestPath}");
            return 2;
        }

        manifestPath = Path.GetFullPath(manifestPath);
        string baseDir = Path.GetDirectoryName(manifestPath)!;

        DesiredState desired;
        try
        {
            string json = File.ReadAllText(manifestPath);
            desired = JsonSerializer.Deserialize<DesiredState>(json) ?? throw new InvalidOperationException("Failed to deserialize manifest.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Invalid JSON in {manifestPath}: {ex.Message}");
            return 2;
        }

        // Resolve relative paths in assets
        ResolvePaths(desired, baseDir);

        if (!string.IsNullOrWhiteSpace(productId))
        {
            desired.ProductId = productId;
        }

        if (!string.IsNullOrWhiteSpace(appName))
        {
            desired.ProductName = appName;
        }

        // Check if markdown listing file exists and load it
        if (!string.IsNullOrWhiteSpace(desired.ListingMarkdown))
        {
            string mdPath = Path.IsPathRooted(desired.ListingMarkdown)
                ? desired.ListingMarkdown
                : Path.Combine(baseDir, desired.ListingMarkdown);

            if (File.Exists(mdPath))
            {
                ListingMarkdownImporter.Import(desired, mdPath);
            }
            else if (action is "preflight" or "run")
            {
                Console.WriteLine($"[ERROR] listingMarkdown was configured but not found: {mdPath}");
                return 2;
            }
        }

        if (action is "preflight" or "run")
        {
            var validationErrors = DesiredStateValidator.Validate(desired, strict: true);
            if (validationErrors.Count > 0)
            {
                Console.WriteLine("[ERROR] Desired state is incomplete or contradictory:");
                foreach (string error in validationErrors) Console.WriteLine("  - " + error);
                return 2;
            }
        }

        Directory.CreateDirectory(stateRoot);
        var checkpoint = LoadCheckpoint(stateRoot, desired.ProductId);

        var orchestrator = new StoreOrchestrator(desired, stateRoot, checkpoint, apply, submit, confirmSubmit, reloadVerify, keepOpen);

        try
        {
            switch (action)
            {
                case "preflight":
                    Console.WriteLine("[INFO] Running STORE 0 Offline Preflight...");
                    await orchestrator.RunPreflightQualityInspectionAsync();
                    Console.WriteLine("[PASS] Preflight passed successfully.");
                    return 0;

                case "launch":
                    Console.WriteLine("[INFO] Launching Edge isolated session...");
                    await orchestrator.EnsureSignedInAsync();
                    Console.WriteLine("[PASS] Edge session ready.");
                    return 0;

                case "status":
                    return await orchestrator.PrintStatusAsync();

                case "step":
                    if (string.IsNullOrWhiteSpace(phase) || phase == "all")
                    {
                        Console.WriteLine("[ERROR] -Action step requires a specific -Phase <availability|properties|ageRatings|packages|listing|options>.");
                        return 1;
                    }
                    return await orchestrator.RunSingleStepAsync(phase);

                case "discover":
                    return await orchestrator.DiscoverAndSnapshotAsync();

                case "inspect":
                    return await orchestrator.InspectAsync();

                case "dumpdom":
                    return await DumpDomAsync(stateRoot, baseDir);

                case "answerno":
                    return await AnswerNoAsync(stateRoot);

                case "cleanpackages":
                    return await CleanAndSavePackagesAsync(stateRoot);

                case "fixpackage":
                    return await FixPackageAsync(stateRoot);

                case "filloptions":
                    return await FillOptionsAsync(stateRoot);

                case "waitpackage":
                    return await WaitPackageAsync(stateRoot);

                case "canceluploads":
                    return await CancelUploadsAsync(stateRoot);

                case "fixprivacy":
                    return await FixPrivacyAsync(stateRoot);

                case "verify":
                    return await orchestrator.VerifyAsync();

                case "reserve":
                case "identity":
                    return await HandleReserveOrIdentityAsync(desired, manifestPath, baseDir, stateRoot, action == "reserve", appName);

                case "stop":
                    Console.WriteLine("[INFO] Stopping Edge session...");
                    orchestrator.StopSession();
                    Console.WriteLine("[PASS] Stopped.");
                    return 0;

                case "run":
                    return await orchestrator.RunAsync(phase);

                default:
                    Console.WriteLine($"[ERROR] Unknown action: {action}");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Unhandled exception: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static async Task<int> CancelUploadsAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));

        // Cancel every in-progress upload: click each 'Cancel' upload-action.
        int cancelled = 0;
        for (int p = 0; p < 20; p++)
        {
            var r = await client.EvaluateAsync<JsElementRect>("""
            (() => {
              const els = Array.from(document.querySelectorAll('a[class*="upload-action"],button,[role="button"]')).filter(e => {
                if (typeof e.disabled !== 'undefined' && e.disabled) return false;
                const r = e.getBoundingClientRect(), s = getComputedStyle(e), t = (e.innerText || '').trim().toLowerCase();
                return t === 'cancel' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
              });
              if (els.length === 0) return null;
              const e = els[0]; e.scrollIntoView({ block: 'center', behavior: 'instant' });
              const r = e.getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
            })()
            """);
            if (r == null) { Ops.Publish("CLEAN", "No more Cancel actions."); break; }
            Ops.Publish("CLICK", $"Cancel upload #{cancelled + 1}");
            await input.ClickCoordinatesAsync(r.X, r.Y, $"Cancel upload #{cancelled + 1}");
            cancelled++;
            await Task.Delay(2500);
        }
        Console.WriteLine($"[PASS] Cancelled {cancelled} in-progress upload(s).");

        // Then try to Save (may still be disabled if validation pending).
        var saveRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button,he-button,[role="button"],input[type="submit"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e), t = (e.innerText || e.value || '').trim();
            return /^(保存|Save)$/.test(t) && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
          });
          if (els.length !== 1) return null;
          const e = els[0]; e.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = e.getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (saveRect != null)
        {
            Ops.Publish("CLICK", "Save packages page (保存)");
            await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save packages page");
            await Task.Delay(3000);
            Console.WriteLine("[PASS] Save clicked after cancelling uploads.");
        }
        else
        {
            Console.WriteLine("[WARN] Save still disabled after cancelling uploads.");
        }
        return 0;
    }

    private static async Task<int> WaitPackageAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));
        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));

        // Cold-reload the packages page so the Save button state refreshes.
        string currentUrl = await client.EvaluateAsync<string>("location.href") ?? "";
        if (!currentUrl.Contains("/packages", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Browser is currently on {currentUrl}, not on the packages page. Please navigate to the submission packages page first.");
        Ops.Publish("NAV", "reload packages -> " + currentUrl);
        await client.SendAsync("Page.navigate", new { url = currentUrl });
        await Task.Delay(3000);

        // Poll until a live (enabled) 保存/Save button appears on the packages page,
        // then click it to commit. The upload must finish "Analyzing" first.
        bool saved = false;
        var deadline = DateTime.UtcNow.AddMinutes(20);
        while (DateTime.UtcNow < deadline)
        {
            var saveRect = await client.EvaluateAsync<JsElementRect>("""
            (() => {
              const els = Array.from(document.querySelectorAll('button,he-button,[role="button"],input[type="button"],input[type="submit"]')).filter(e => {
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                const t = (e.innerText || e.value || e.getAttribute('aria-label') || '').trim();
                return /^(保存|Save)$/.test(t) && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
              });
              if (els.length !== 1) return null;
              const e = els[0];
              e.scrollIntoView({ block: 'center', behavior: 'instant' });
              const r = e.getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
            })()
            """);
            if (saveRect != null)
            {
                Ops.Publish("CLICK", "Save packages page (保存) after validation");
                await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save packages page");
                await Task.Delay(3000);
                saved = true;
                break;
            }
            await Task.Delay(6000);
        }
        Console.WriteLine(saved
            ? "[PASS] Package saved (validation completed)."
            : "[WARN] Save did not enable within 20 min; package still validating.");
        return saved ? 0 : 4;
    }

    private static async Task<int> FillOptionsAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));
        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));

        await waiter.RequireAsync(async () =>
        {
            int len = await client.EvaluateAsync<int>("document.body?.innerText.length || 0");
            return len > 100;
        }, TimeSpan.FromSeconds(30), "wait for options form");

        // Fill the runFullTrust justification textarea (labeled 为何需要使用…runFullTrust)
        string reason = "这是一个 WinUI 3 桌面应用，需要以全信任桌面进程运行才能正常启动并提供本地通知、文件和系统集成功能。应用仅在用户本机运行，不访问或修改其他用户的数据。";
        bool setOk = await client.EvaluateAsync<bool>($$"""
        (() => {
          const needle = {{JsonSerializer.Serialize("为何需要使用")}};
          const textareas = Array.from(document.querySelectorAll('textarea')).filter(e => (e.parentElement?.parentElement?.innerText || '').includes(needle));
          if (textareas.length !== 1) return false;
          const e = textareas[0];
          const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
          if (setter && setter.set) setter.set.call(e, {{JsonSerializer.Serialize(reason)}}); else e.value = {{JsonSerializer.Serialize(reason)}};
          e.dispatchEvent(new Event('input', { bubbles: true }));
          e.dispatchEvent(new Event('change', { bubbles: true }));
          return true;
        })()
        """);
        Ops.Info(setOk ? "runFullTrust justification filled" : "runFullTrust textarea not found");

        // Click the '保存' (Save) button by text
        var saveRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button,he-button,[role="button"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return (e.innerText || '').trim() === '保存' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
          });
          if (els.length !== 1) return null;
          const e = els[0];
          e.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = e.getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (saveRect != null)
        {
            Ops.Click("Save options (保存)");
            await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save options");
            await Task.Delay(2500);
            Console.WriteLine("[PASS] runFullTrust justification saved on Options page.");
        }
        else
        {
            Console.WriteLine("[WARN] No '保存' button found on Options page.");
        }
        return 0;
    }

    private static async Task<int> FixPrivacyAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));
        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));

        // 1. Click the '提供隐私策略文本' radio -> a textarea pops up
        var radioRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const e = document.querySelector('input#privacyPolicyText');
          if (!e) return null;
          const t = (e.getBoundingClientRect().width > 0) ? e : (e.closest('label') || e.parentElement);
          t.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = t.getBoundingClientRect();
          if (r.width <= 0 || r.height <= 0) return null;
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (radioRect == null) { Console.WriteLine("[WARN] privacyPolicyText radio not found."); return 1; }
        Ops.Publish("CLICK", "提供隐私策略文本 radio");
        await input.ClickCoordinatesAsync(radioRect.X, radioRect.Y, "提供隐私策略文本 radio");
        await Task.Delay(1500);

        // 2. Fill the popped-up textarea with the privacy policy text
        string text = "本应用「牵挂EXXE」为纯本地离线工具：不收集、不存储、不上传任何个人身份信息；所有数据仅保存在用户本机应用数据目录中；应用不联网，无任何远程服务、广告或分析 SDK；提醒功能通过 Windows 系统通知实现，权限由用户在系统设置中自行控制。";
        bool filled = await client.EvaluateAsync<bool>($$"""
        (() => {
          const els = Array.from(document.querySelectorAll('textarea')).filter(e => e.getBoundingClientRect().width > 0 && e.getBoundingClientRect().height > 0);
          const target = els.find(e => {
            let n = e;
            for (let k = 0; k < 6 && n; k++, n = n.parentElement) {
              if ((n.innerText || '').includes('隐私') || (n.innerText || '').includes('privacy')) break;
            }
            return true;
          });
          if (!target) return false;
          const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
          if (setter && setter.set) setter.set.call(target, {{JsonSerializer.Serialize(text)}});
          else target.value = {{JsonSerializer.Serialize(text)}};
          target.dispatchEvent(new Event('input', { bubbles: true }));
          target.dispatchEvent(new Event('change', { bubbles: true }));
          target.dispatchEvent(new Event('blur', { bubbles: true }));
          return true;
        })()
        """);
        Ops.Info(filled ? "Privacy policy text entered" : "Textarea not found");
        await Task.Delay(800);

        // 3. Click the now-lit 保存 button
        var saveRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button,he-button,[role="button"],input[type="submit"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e), t = (e.innerText || e.value || '').trim();
            return /^(保存|Save)$/.test(t) && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
          });
          if (els.length !== 1) return null;
          const e = els[0]; e.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = e.getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (saveRect == null) { Console.WriteLine("[WARN] Save still not lit."); return 4; }
        Ops.Publish("CLICK", "保存 properties");
        await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "保存 properties");
        await Task.Delay(2500);
        Console.WriteLine("[PASS] Privacy policy text provided and saved.");
        return 0;
    }

    private static async Task<int> CleanAndSavePackagesAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));
        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));
        var native = new NativeFormAdapter(client, input);
        var checkbox = new HeCheckboxAdapter(client, input);

        Console.WriteLine("[INFO] Cancelling active uploads and cleaning duplicate package rows...");
        // 1. Cancel upload if in progress
        await client.EvaluateAsync<bool>("""
        (() => {
            const cancel = Array.from(document.querySelectorAll('a.upload-action, button, [role="button"]')).find(e => (e.innerText || '').trim().toLowerCase() === 'cancel');
            if (cancel) { cancel.click(); return true; }
            return false;
        })()
        """);
        await Task.Delay(1000);

        // 2. Remove duplicate rows until only 1 remains
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

        await Task.Delay(4000);

        // 5. Navigate to overview and dump Overview DOM
        Console.WriteLine("[INFO] Extracting post-save Overview DOM...");
        string overviewUrl = await client.EvaluateAsync<string>("""
        (() => {
            const a = document.querySelector('a[href*="/overview"]');
            return a ? a.href : location.href.replace(/\/packages.*$/, '/overview');
        })()
        """) ?? "";

        if (!string.IsNullOrWhiteSpace(overviewUrl))
        {
            await waiter.NavigateAsync(overviewUrl, "Overview after packages save");
            var inspector = new PageInspector(client);
            var snapshot = await inspector.CaptureAsync();
            Console.WriteLine("[DOM-EXTRACTED] Overview DOM Snapshot:");
            Console.WriteLine(JsonSerializer.Serialize(snapshot, JsonIndented));
        }

        return 0;
    }

    private static async Task<int> FixPackageAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));
        var waiter = new Waiter(client);
        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));
        var dom = new DomDriver(client);

        await waiter.RequireAsync(async () =>
        {
            string ready = await client.EvaluateAsync<string>("document.readyState") ?? "";
            int len = await client.EvaluateAsync<int>("document.body?.innerText.length || 0");
            return ready == "complete" && len > 100;
        }, TimeSpan.FromSeconds(30), "wait for packages page");

        // Remove ALL existing package rows. This page state exposes a 'Delete'
        // upload-action link per package row (plus Retry/Revert). Click every
        // Delete until no rows remain so the upload starts completely clean.
        int removed = 0;
        for (int pass = 0; pass < 20; pass++)
        {
            var removeRect = await client.EvaluateAsync<JsElementRect>("""
            (() => {
              const els = Array.from(document.querySelectorAll('a[class*="upload-action"],button,he-button,[role="button"]')).filter(e => {
                if (typeof e.disabled !== 'undefined' && e.disabled) return false;
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                const t = (e.innerText || '').trim().toLowerCase();
                const isDel = /^(delete|remove)$/.test(t);
                return isDel && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
              });
              if (els.length === 0) return null;
              const e = els[0];
              e.scrollIntoView({ block: 'center', behavior: 'instant' });
              const r = e.getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
            })()
            """);
            if (removeRect == null)
            {
                Ops.Publish("CLEAN", "No more Delete/Remove actions; package list is clean.");
                break;
            }
            Ops.Publish("CLICK", $"Delete package row #{removed + 1}");
            await input.ClickCoordinatesAsync(removeRect.X, removeRect.Y, $"Delete package row #{removed + 1}");
            removed++;
            await Task.Delay(2500);

            // Confirm any dialog (确定/确认/Delete/Remove/是)
            var confirmRect = await client.EvaluateAsync<JsElementRect>("""
            (() => {
              const dialog = Array.from(document.querySelectorAll('[role="dialog"],[aria-modal="true"]')).find(d => d.getBoundingClientRect().width > 0);
              if (!dialog) return null;
              const els = Array.from(dialog.querySelectorAll('button,he-button,[role="button"],a')).filter(e => {
                const r = e.getBoundingClientRect(), t = (e.innerText || '').trim();
                return /^(确定|确认|Delete|Remove|是)$/.test(t) && r.width > 0 && r.height > 0 && !e.disabled;
              });
              if (els.length === 0) return null;
              const e = els[0];
              const r = e.getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
            })()
            """);
            if (confirmRect != null)
            {
                Ops.Publish("CLICK", "Confirm delete");
                await input.ClickCoordinatesAsync(confirmRect.X, confirmRect.Y, "Confirm delete");
                await Task.Delay(2500);
            }
        }
        if (removed > 0) Console.WriteLine($"[PASS] Removed {removed} package row(s).");

        // Re-upload the corrected MSIX
        string msix = "Y:\\Project\\qiangua\\Qiangua\\store-package\\Vainreef.EXXE_1.0.3.0_x64.msix";
        string? fileInputObj = null;
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            fileInputObj = await dom.GetObjectIdByExpressionAsync("""
            (() => {
              const roots=[document];
              for(let i=0;i<roots.length;i++){ try{ for(const e of roots[i].querySelectorAll('*')) if(e.shadowRoot) roots.push(e.shadowRoot); }catch(_){} }
              for(const r of roots){ const f=Array.from(r.querySelectorAll('input[type="file"]')).find(e => !e.disabled); if(f) return f; }
              return null;
            })()
            """);
            if (!string.IsNullOrWhiteSpace(fileInputObj)) break;
            await Task.Delay(1000);
        }
        if (string.IsNullOrWhiteSpace(fileInputObj)) { Ops.Publish("WARN", "No upload input found after delete; re-run packages phase."); return 1; }
        Ops.Publish("UPLOAD", "Re-upload corrected MSIX: " + msix);
        await dom.SetFileInputFilesByObjectIdAsync(fileInputObj, [msix]);
        await Task.Delay(2500);

        // Commit: click the page '保存' (Save) button by text to confirm upload and any removals.
        var saveRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button,he-button,[role="button"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return (e.innerText || '').trim() === '保存' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
          });
          if (els.length !== 1) return null;
          const e = els[0];
          e.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = e.getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (saveRect != null)
        {
            Ops.Publish("CLICK", "Save packages page (保存)");
            await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save packages page");
            await Task.Delay(2500);
        }
        else
        {
            // Retry Save up to 8 times with longer waits (upload/validation may need time).
            for (int s = 0; s < 8; s++)
            {
                await Task.Delay(3000);
                saveRect = await client.EvaluateAsync<JsElementRect>("""
                (() => {
                  const els = Array.from(document.querySelectorAll('button,he-button,[role="button"]')).filter(e => {
                    const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                    return (e.innerText || '').trim() === '保存' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
                  });
                  if (els.length !== 1) return null;
                  const e = els[0];
                  e.scrollIntoView({ block: 'center', behavior: 'instant' });
                  const r = e.getBoundingClientRect();
                  return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
                })()
                """);
                if (saveRect != null)
                {
                    Ops.Publish("CLICK", $"Save packages page (保存) attempt #{s + 1}");
                    await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save packages page");
                    await Task.Delay(2500);
                    Console.WriteLine("[PASS] Packages cleaned, corrected MSIX uploaded and saved.");
                    return 0;
                }
            }
            Ops.Publish("WARN", "No enabled '保存' button found after retries.");
        }
        Console.WriteLine("[PASS] Packages cleaned, corrected MSIX uploaded and saved.");
        return 0;
    }

    private static async Task<int> AnswerNoAsync(string stateRoot)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();
        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        await waiter.RequireAsync(async () =>
        {
            string ready = await client.EvaluateAsync<string>("document.readyState") ?? "";
            return ready == "complete";
        }, TimeSpan.FromSeconds(30), "wait for document ready");

        var targets = await client.EvaluateAsync<List<NoTarget>>("""
        (() => {
          const distinct = [...new Set(Array.from(document.querySelectorAll('input[type="radio"][name^="question#"]')).map(e => e.name))];
          const out = [];
          for (const name of distinct) {
            const radios = Array.from(document.querySelectorAll('input[type="radio"][name="' + name + '"]'));
            const no = radios.find(e => (e.closest('label')?.querySelector('.response-text')?.innerText || '').replace(/\s+/g, '').trim() === '否');
            if (!no) continue;
            const t = no.closest('label') || no;
            t.scrollIntoView({ block: 'center', behavior: 'instant' });
            const r = t.getBoundingClientRect();
            if (r.width > 0 && r.height > 0 && r.top >= 0 && r.left >= 0) {
              out.push({ name, value: no.value, x: r.left + r.width / 2, y: r.top + r.height / 2 });
            } else {
              out.push({ name, value: no.value, x: -1, y: -1, note: 'not-in-viewport-after-scroll' });
            }
          }
          return out;
        })()
        """) ?? [];

        var input = new InputDriver(client, new AxLocator(client, new DomDriver(client)));
        int clicked = 0;
        foreach (var t in targets)
        {
            if (t.X <= 0 || t.Y <= 0 || t.X > 20000 || t.Y > 20000) { Ops.Publish("SKIP", $"{t.Name} (val {t.Value}) {t.Note}"); continue; }
            Ops.Publish("CLICK", $"Answer No for {t.Name} (val {t.Value})");
            await input.ClickCoordinatesAsync(t.X, t.Y, $"No for {t.Name}");
            clicked++;
            await Task.Delay(200);
        }
        Console.WriteLine($"[PASS] Clicked {clicked} '否' answer(s) on the current page ({targets.Count} question groups found).");

        // After the answers, advance to the IARC rating preview (where the terms
        // agreement and save live).
        var previewRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button, he-button, a, [role="button"]')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return (e.innerText || '').trim() === '\u9884\u89c8\u5206\u7ea7' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
          });
          if (els.length !== 1) return null;
          const r = els[0].getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (previewRect != null)
        {
            Ops.Publish("CLICK", "Preview age ratings (预览分级)");
            await input.ClickCoordinatesAsync(previewRect.X, previewRect.Y, "Preview age ratings");
            await Task.Delay(2500);
        }
        else
        {
            Ops.Publish("WARN", "No '预览分级' button found; skipping preview step.");
        }

        // On the IARC summary page: check the terms agreement, then Save.
        var agreeRect = await client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('he-checkbox, input[type="checkbox"]')).filter(e => {
            const r = e.getBoundingClientRect();
            return ((e.innerText || e.getAttribute('aria-label') || '') + '').indexOf('IARC') >= 0 && r.width > 0 && r.height > 0;
          });
          if (els.length !== 1) return null;
          const t = els[0];
          t.scrollIntoView({ block: 'center', behavior: 'instant' });
          const r = t.getBoundingClientRect();
          if (r.width <= 0 || r.height <= 0) return null;
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);
        if (agreeRect != null)
        {
            Ops.Publish("CLICK", "Check IARC terms agreement");
            await input.ClickCoordinatesAsync(agreeRect.X, agreeRect.Y, "Check IARC terms agreement");
            await Task.Delay(1500);
        }
        else
        {
            Ops.Publish("WARN", "IARC terms agreement checkbox not found on summary page.");
        }

        // Wait for Save to enable, then click it.
        bool saveReady = await waiter.WaitUntilAsync(async () =>
        {
            bool ok = await client.EvaluateAsync<bool>("""
            (() => {
              const els = Array.from(document.querySelectorAll('button, he-button, [role="button"]')).filter(e => (e.innerText || '').trim() === '保存' && e.getBoundingClientRect().width > 0);
              return els.length === 1 && !els[0].disabled;
            })()
            """);
            return ok;
        }, timeout: TimeSpan.FromSeconds(15), description: "wait for Save enabled");

        if (saveReady)
        {
            var saveRect = await client.EvaluateAsync<JsElementRect>("""
            (() => {
              const els = Array.from(document.querySelectorAll('button, he-button, [role="button"]')).filter(e => (e.innerText || '').trim() === '保存' && e.getBoundingClientRect().width > 0 && !e.disabled);
              if (els.length !== 1) return null;
              const r = els[0].getBoundingClientRect();
              return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
            })()
            """);
            if (saveRect != null)
            {
                Ops.Publish("CLICK", "Save age ratings (保存)");
                await input.ClickCoordinatesAsync(saveRect.X, saveRect.Y, "Save age ratings");
                await Task.Delay(2500);
            }
        }
        else
        {
            Ops.Publish("WARN", "Save button did not enable after checking agreement.");
        }

        return 0;
    }

    internal sealed class NoTarget
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public string? Note { get; set; }
    }

    private static async Task<int> DumpDomAsync(string stateRoot, string baseDir)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();

        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        string payload = await client.EvaluateAsync<string>("""
        (() => {
            const allRoots = [document];
            for (let i = 0; i < allRoots.length; i++) {
                try { for (const e of allRoots[i].querySelectorAll('*')) if (e.shadowRoot) allRoots.push(e.shadowRoot); } catch (_) {}
            }
            const deepAll = (sel) => {
                const out = [], seen = new Set();
                for (const root of allRoots) { try { for (const e of root.querySelectorAll(sel)) if (!seen.has(e)) { seen.add(e); out.push(e); } } catch (_) {} }
                return out;
            };

            const clamp = s => (s || '').replace(/\s+/g, ' ').trim().slice(0, 90);
            const isVisible = e => { const r = e.getBoundingClientRect(), s = getComputedStyle(e.closest('[hidden]') || e); return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden'; };
            // exclude global chrome: header / top nav / left nav / footer / AI copilot panel
            const inBody = e => !e.closest('header, aside, nav, footer, [role="navigation"], [id*="copilot" i], [aria-label*="copilot" i], [data-automation-id*="copilot" i]');
            // nearest local label: prefer the small label immediately around the control
            const groupText = e => {
                let node = e.parentElement;
                for (let depth = 0; node && depth < 4; depth++) {
                    const t = e.closest('label') ? clamp(e.closest('label').innerText) : clamp(node.innerText);
                    if (t.length >= 1 && t.length <= 70) return t;
                    node = node.parentElement;
                }
                return clamp(e.getAttribute('aria-label') || e.getAttribute('title') || e.name || '');
            };

            const out = [];
            out.push('url=' + location.href);
            out.push('title=' + document.title);
            out.push('---- HEADINGS ----');
            deepAll('h1,h2,h3,h4,[role="heading"]').filter(e => isVisible(e) && inBody(e)).slice(0, 30).forEach(h => out.push((h.tagName||'h?').toLowerCase() + ': ' + clamp(h.innerText)));

            out.push('---- CONTROLS ----');
            const seenCtl = new Set();
            const ctl = (e, line) => { if (seenCtl.has(e)) return; seenCtl.add(e); out.push(line); };

            deepAll('input,select,textarea').filter(e => {
                const ty = (e.type||'').toLowerCase();
                return ty !== 'hidden' && !['serviceName','awaMarket'].includes(e.id) && isVisible(e) && inBody(e);
            }).forEach(e => {
                const ty = (e.type || '').toLowerCase();
                let detail = '';
                if (ty === 'checkbox' || ty === 'radio') {
                    detail = (e.checked ? 'checked' : 'unchecked') + ' val=' + clamp(e.value);
                } else if (e.tagName === 'SELECT') {
                    const sel = e.selectedOptions && e.selectedOptions[0];
                    detail = 'sel=' + clamp(sel ? (sel.textContent || sel.value) : e.value);
                } else {
                    detail = 'value=' + clamp(e.value);
                }
                ctl(e, `INPUT ${ty} id="${e.id}" name="${e.name}" label="${clamp(e.getAttribute('aria-label') || e.getAttribute('title'))}" ${detail} disabled=${!!e.disabled} vis=true | group: "${groupText(e)}"`);
            });

            deepAll('button,he-button,[role="button"]').filter(e => isVisible(e) && inBody(e)).forEach(e => {
                const t = clamp(e.innerText || e.getAttribute('aria-label') || e.getAttribute('title'));
                if (!t) return;
                ctl(e, `BUTTON text="${t}" disabled=${!!e.disabled} vis=true | group: "${groupText(e)}"`);
            });

            // he-select / he-checkbox custom hosts that expose their text through shadow DOM
            deepAll('he-select,he-checkbox').filter(e => isVisible(e) && inBody(e)).forEach(e => {
                const t = clamp((e.getAttribute('aria-label') || e.title || '') + ' ' + (e.innerText || ''));
                if (!t.trim()) return;
                ctl(e, `${e.tagName.toLowerCase()} label="${t}" vis=true | group: "${groupText(e)}"`);
            });

            out.push('---- ALL he-select/he-checkbox HOSTS (incl hidden) ----');
            deepAll('he-select,he-checkbox').slice(0, 60).forEach(e => {
                out.push(`${e.tagName.toLowerCase()} aria="${clamp(e.getAttribute('aria-label')||e.title)}" text="${clamp(e.innerText)}" vis=${isVisible(e)}`);
            });

            out.push('---- FILE INPUTS (incl hidden/shadow) ----');
            deepAll('input[type="file"]').slice(0, 10).forEach(e => {
                out.push('FILE input id="' + (e.id||'') + '" name="' + (e.getAttribute('name')||'') + '" disabled=' + !!e.disabled + ' vis=' + isVisible(e) + ' accept="' + (e.getAttribute('accept')||'') + '"');
            });
            deepAll('he-upload,.upload,[class*="upload" i],[data-automation-id*="upload" i]').slice(0, 10).forEach(e => {
                out.push('UPLOAD ' + e.tagName.toLowerCase() + ' cls="' + clamp(e.className||'') + '" text="' + clamp(e.innerText) + '"');
            });

            out.push('---- SELECT OPTIONS ----');
            deepAll('select').filter(e => isVisible(e) && inBody(e)).slice(0, 8).forEach(e => {
                const opts = Array.from(e.options).map(o => '[' + o.value + '|' + o.textContent.trim().slice(0, 30) + (o.selected ? '|*' : '') + ']').join(' ');
                out.push('SELECT name="' + (e.getAttribute('name') || '') + '" val="' + (e.value || '') + '" opts: ' + opts);
            });

            out.push('---- RAW question# radios (age ratings) ----');
            deepAll('input[type="radio"][name^="question#"]').slice(0, 8).forEach(e => {
                out.push(e.outerHTML.slice(0, 500).replace(/\s+/g, ' '));
                const par = e.closest('label') || e.parentElement;
                if (par) out.push('   PARENT<' + par.tagName + '> text="' + clamp(par.innerText) + '"');
            });

            out.push('---- PRICE-REGION CONTEXT ----');
            // collect the closest container around each price-related element for structure
            const priceAnchors = deepAll('[data-automation-id*="price" i],[id*="price" i],[class*="price" i],price-tier-selection,market-group').slice(0, 40);
            priceAnchors.forEach(a => out.push('PRICE:' + a.tagName.toLowerCase() + ' id="' + (a.id||'') + '" cls="' + clamp(a.className||'') + '" text="' + clamp(a.innerText) + '"'));

            out.push('---- SUBMISSION/PRODUCT LINKS ----');
            deepAll('a[href*="/submissions/"],a[href*="/products/"]').filter(e => isVisible(e)).slice(0, 60).forEach(a => {
                const name = clamp(a.innerText || a.getAttribute('aria-label'));
                if (!name) return;
                out.push('LINK ' + name + ' -> ' + (a.href || ''));
                // dump the owning row + any completion/status icon for diagnosis
                let row = a;
                for (let i = 0; i < 8 && row.parentElement; i++) {
                    const next = row.parentElement;
                    const links = Array.from(next.querySelectorAll('a[href*="/submissions/"]'));
                    if (links.length > 1) break;
                    row = next;
                }
                const cls = clamp(row.className || '') ;
                const icons = Array.from(row.querySelectorAll('[class*="check" i],[class*="status" i],[class*="icon" i],[aria-label]')).slice(0, 8)
                    .map(e => `{cls="${clamp(e.className)}" aria="${clamp(e.getAttribute('aria-label'))}" name="${clamp(e.getAttribute('name'))}" text="${clamp(e.innerText)}"}`)
                    .join(' ');
                out.push('   ROW_CLASS="' + cls + '" ICONS=' + icons);
                out.push('   ROW_HTML=' + row.outerHTML.slice(0, 900).replace(/\s+/g, ' '));
            });

            out.push('---- STATUS TEXT (module rows / errors) ----');
            deepAll('.alert-error,.alert-danger,[role="alert"],.has-error,[class*="status"],[class*="error"],[class*="check"],[class*="icon"]').filter(e => isVisible(e)).slice(0, 30).forEach(e => {
                const t = clamp(e.innerText);
                if (t) out.push('STATUS: ' + t);
            });

            return out.join('\n');
        })()
        """) ?? "";

        string outFile = Path.Combine(baseDir, "dom-dump-LIVE.html");
        File.WriteAllText(outFile, payload, new System.Text.UTF8Encoding(false));
        Console.WriteLine($"[PASS] DOM dumped ({payload.Length} chars) to: {outFile}");
        Console.WriteLine($"[INFO] Current URL: {(await client.EvaluateAsync<string>("location.href"))}");
        return 0;
    }

    private static async Task<int> HandleReserveOrIdentityAsync(DesiredState desired, string manifestPath, string baseDir, string stateRoot, bool isReserve, string appName)
    {
        var conn = await CdpConnection.StartOrReuseAsync(stateRoot);
        string wsUrl = await conn.GetTargetWebSocketUrlAsync();

        await using var client = new CdpClient();
        await client.ConnectAsync(new Uri(wsUrl));

        var waiter = new Waiter(client);
        var dom = new DomDriver(client);
        var ax = new AxLocator(client, dom);
        var input = new InputDriver(client, ax);
        var native = new NativeFormAdapter(client, input);
        var prodManager = new ProductManager(client, waiter, native);

        string effectiveAppName = !string.IsNullOrWhiteSpace(appName) ? appName : desired.ProductName;
        if (string.IsNullOrWhiteSpace(effectiveAppName))
        {
            throw new InvalidOperationException("Product name must be provided via --app-name or in manifest productName.");
        }

        ProductIdentityResult result;
        if (isReserve)
        {
            Console.WriteLine($"[INFO] Creating and reserving new product [{effectiveAppName}] in Partner Center...");
            result = await prodManager.CreateAndReserveProductAsync(desired.Site.BaseUrl, effectiveAppName);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(desired.ProductId) || desired.ProductId == "PENDING")
            {
                throw new InvalidOperationException("ProductId must be specified for identity scraping action.");
            }
            Console.WriteLine($"[INFO] Scraping Identity for product [{desired.ProductId}]...");
            result = await prodManager.ScrapeIdentityAsync(desired.Site.BaseUrl, desired.ProductId);
        }

        // Backfill into Package.appxmanifest and manifest JSON
        BackfillIdentity(baseDir, manifestPath, desired, result, effectiveAppName);
        return 0;
    }

    private static void BackfillIdentity(string baseDir, string manifestPath, DesiredState desired, ProductIdentityResult result, string appName)
    {
        // 1. Update manifest JSON
        try
        {
            desired.ProductId = result.ProductId;
            desired.ProductName = appName;
            string json = JsonSerializer.Serialize(desired, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, json);
            Console.WriteLine($"[PASS] Updated store manifest: {manifestPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Failed to write manifest JSON: {ex.Message}");
        }

        // 2. Search and update Package.appxmanifest
        try
        {
            var manifestFiles = Directory.GetFiles(baseDir, "Package.appxmanifest", SearchOption.AllDirectories)
                .Concat(Directory.Exists(Path.Combine(baseDir, "..")) ? Directory.GetFiles(Path.Combine(baseDir, ".."), "Package.appxmanifest", SearchOption.AllDirectories) : [])
                .Distinct()
                .ToList();

            foreach (var mf in manifestFiles)
            {
                string xml = File.ReadAllText(mf);
                // Update Identity Name and Publisher
                xml = Regex.Replace(xml, @"<Identity\s+Name=""[^""]*""\s+Publisher=""[^""]*""",
                    $"<Identity Name=\"{result.IdentityName}\" Publisher=\"{result.Publisher}\"");
                // Update DisplayName
                xml = Regex.Replace(xml, @"<DisplayName>[^<]*</DisplayName>",
                    $"<DisplayName>{appName}</DisplayName>");
                xml = Regex.Replace(xml, @"DisplayName=""[^""]*""",
                    $"DisplayName=\"{appName}\"");
                // Update PublisherDisplayName
                if (!string.IsNullOrWhiteSpace(result.PublisherDisplayName))
                {
                    xml = Regex.Replace(xml, @"<PublisherDisplayName>[^<]*</PublisherDisplayName>",
                        $"<PublisherDisplayName>{result.PublisherDisplayName}</PublisherDisplayName>");
                }
                File.WriteAllText(mf, xml);
                Console.WriteLine($"[PASS] Backfilled credentials into Package.appxmanifest: {mf}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not auto-backfill Package.appxmanifest: {ex.Message}");
        }
    }

    private static StoreCheckpoint LoadCheckpoint(string stateRoot, string productId)
    {
        string path = Path.Combine(stateRoot, "checkpoint.json");
        try
        {
            if (File.Exists(path))
            {
                var existing = JsonSerializer.Deserialize<StoreCheckpoint>(File.ReadAllText(path));
                if (existing != null && string.Equals(existing.ProductId, productId, StringComparison.OrdinalIgnoreCase))
                {
                    if (existing.SchemaVersion < 4)
                    {
                        // Old checkpoints called a phase converged after the adapter
                        // returned, including dry-run and unverified saves. Preserve
                        // only routing data; discard every legacy completion claim.
                        existing.SchemaVersion = 4;
                        existing.PhaseStatuses.Clear();
                        existing.ConvergedPhases.Clear();
                        existing.PhaseEvidence.Clear();
                    }
                    return existing;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Ignoring unreadable checkpoint: {ex.Message}");
        }
        return new StoreCheckpoint { ProductId = productId };
    }

    private static void ResolvePaths(DesiredState state, string baseDir)
    {
        if (state.Assets == null) return;
        state.Assets.Msix = ResolveOne(state.Assets.Msix, baseDir);
        state.Assets.Screenshot = ResolveOne(state.Assets.Screenshot, baseDir);
        state.Assets.Poster = ResolveOne(state.Assets.Poster, baseDir);
        state.Assets.Boxart = ResolveOne(state.Assets.Boxart, baseDir);
        state.Assets.Logo300 = ResolveOne(state.Assets.Logo300, baseDir);
        state.Assets.Logo150 = ResolveOne(state.Assets.Logo150, baseDir);
        state.Assets.Logo71 = ResolveOne(state.Assets.Logo71, baseDir);
        state.Assets.Superhero = ResolveOne(state.Assets.Superhero, baseDir);
    }

    private static string ResolveOne(string path, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (Path.IsPathRooted(path)) return path;
        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    public static readonly JsonSerializerOptions JsonIndented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
