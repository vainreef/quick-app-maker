using System.Text.Json;
using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class ListingAdapter
{
    private readonly CdpClient _client;
    private readonly DomDriver _dom;
    private readonly Waiter _waiter;
    private readonly NativeFormAdapter _native;
    private readonly InputDriver _input;
    private readonly ListingLanguageGridManager _languageGrid;
    private List<string> _languageGridCodes = [];

    public ListingAdapter(CdpClient client, DomDriver dom, Waiter waiter, NativeFormAdapter native, InputDriver input)
    {
        _client = client;
        _dom = dom;
        _waiter = waiter;
        _native = native;
        _input = input;
        _languageGrid = new ListingLanguageGridManager(client, input);
    }

    public async Task EnterLanguageFormAsync(DesiredState desired, bool applyLanguageChanges)
    {
        bool form = await _client.EvaluateAsync<bool>("""
        (() => {
            const url = location.href || '';
            return /[?&]languageid=/i.test(url) || document.querySelector('#description-required, #shortDescription') !== null;
        })()
        """);
        if (form) return;

        // If on manage languages page, wait for the table rows to render
        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("document.querySelector('[slot^=\"Action-\"], [slot^=\"Name-\"]') !== null");
        }, TimeSpan.FromSeconds(30), "Wait for manage languages rows");

        await Task.Delay(1500);

        // STEP 1: Detect DOM languages
        var items = await _languageGrid.DetectLanguagesAsync(desired);

        // STEP 2: Delete unwanted languages and save if applying
        if (applyLanguageChanges)
        {
            await _languageGrid.DeleteUnwantedLanguagesAsync(desired);

            Console.WriteLine("[WORKFLOW STEP 3] 保存语言配置更改...");
            await _client.EvaluateAsync<object>("""
            (() => {
              const saveBtn = Array.from(document.querySelectorAll('he-button, button, input[type="button"], input[type="submit"]')).find(e => {
                const t = (e.innerText || e.value || e.getAttribute('aria-label') || '').trim();
                const r = e.getBoundingClientRect();
                return /^(保存|Save|保存并继续)$/i.test(t) && r.width > 0 && r.height > 0 && !e.disabled;
              });
              if (saveBtn) {
                if (saveBtn.shadowRoot) {
                  const inner = saveBtn.shadowRoot.querySelector('button');
                  if (inner) inner.click();
                }
                saveBtn.click();
                saveBtn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
              }
              return null;
            })()
            """);
            await Task.Delay(3000);
        }

        // STEP 4: Navigate / Enter Chinese listing details form
        Console.WriteLine("[WORKFLOW STEP 4] 点击【中文(中国)】进入详情填写页面...");
        string languageId = desired.Site.LanguageId;
        string languageCode = desired.Site.LanguageCode;
        
        bool clicked = await _client.EvaluateAsync<bool>("""
        (() => {
          const a = document.querySelector('[slot="Name-5"] a') ||
                    Array.from(document.querySelectorAll('a')).find(x => (x.innerText || '').trim() === '中文(中国)') ||
                    document.querySelector('[slot^="Name-"] a');
          if (a) {
            a.scrollIntoView({ block: 'center', behavior: 'instant' });
            a.click();
            a.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
            return true;
          }
          return false;
        })()
        """);

        if (!clicked)
        {
            string? directHref = await _client.EvaluateAsync<string?>($$"""
            (() => {
              const a = document.querySelector('[slot="Name-5"] a, [slot^="Name-"] a');
              return a ? a.href : null;
            })()
            """);
            if (!string.IsNullOrWhiteSpace(directHref))
            {
                await _waiter.NavigateAsync(directHref, $"listing {languageCode}");
            }
            else
            {
                string currentUrl = await _client.EvaluateAsync<string>("location.href") ?? "";
                var m = System.Text.RegularExpressions.Regex.Match(currentUrl, @"/products/([^/]+)/submissions/([^/]+)/");
                if (m.Success)
                {
                    string target = $"https://partner.microsoft.com/zh-cn/dashboard/products/{m.Groups[1].Value}/submissions/{m.Groups[2].Value}/listings?languageid={languageId}&languagecode={languageCode}";
                    await _waiter.NavigateAsync(target, $"listing {languageCode}");
                }
            }
        }

        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("document.querySelector('#description-required, #shortDescription') !== null");
        }, TimeSpan.FromSeconds(60), "Wait for Chinese listing details form");
    }

    public async Task<ObservedListing> ObserveAsync()
    {
        await _waiter.RequireAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("document.querySelector('#description-required') !== null");
        }, TimeSpan.FromSeconds(90), "Wait for listing form (navigation is handled separately)");

        var obs = new ObservedListing();
        obs.Description = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('#description-required');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.ShortDescription = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('#shortDescription');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.Keywords = await _client.EvaluateAsync<List<string>>("""
        (() => {
          const root = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
          if (!root) return [];
          return Array.from(root.querySelectorAll('he-option'))
            .filter(e => (e.getAttribute('slot') || '').startsWith('selected-') || e.getAttribute('role') === 'listitem')
            .map(e => (e.innerText || e.getAttribute('value') || '').trim())
            .filter(Boolean);
        })()
        """) ?? [];

        obs.Features = await _client.EvaluateAsync<List<string>>("""
        Array.from(document.querySelectorAll('input[id^="feature-"]')).map(x=>x.value.trim()).filter(Boolean)
        """) ?? [];
        obs.FormReady = true;
        obs.LanguageCodes = _languageGridCodes.ToList();
        obs.HasScreenshot = await SectionHasImageAsync(["屏幕截图", "screenshot", "desktop"], -1);
        obs.RequiredScreenshotSatisfied = obs.HasScreenshot;
        obs.HasBoxart = await SectionHasImageAsync(["1:1", "酷图"], -1);
        obs.HasLogo300 = await SectionHasImageAsync(["300x300", "300 x 300"], -1);
        obs.HasLogo150 = await SectionHasImageAsync(["150x150", "150 x 150"], -1);
        obs.HasLogo71 = await SectionHasImageAsync(["71x71", "71 x 71"], -1);

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedListing observed)
    {
        var plan = new ReconcilePlan { Phase = "listing" };

        if (!string.IsNullOrEmpty(desired.Values.Description) && observed.Description.Trim() != desired.Values.Description.Trim())
            plan.AddChange("listing.description", observed.Description.Length > 20 ? observed.Description[..20] + "..." : observed.Description, "...", "Update description");

        if (!string.IsNullOrEmpty(desired.Values.ShortDescription) && observed.ShortDescription.Trim() != desired.Values.ShortDescription.Trim())
            plan.AddChange("listing.shortDescription", observed.ShortDescription, desired.Values.ShortDescription, "Update short description");

        if (desired.Values.Keywords.Count > 0)
        {
            var missing = desired.Values.Keywords.Except(observed.Keywords).ToList();
            if (missing.Count > 0)
                plan.AddChange("listing.keywords", string.Join(",", observed.Keywords), string.Join(",", desired.Values.Keywords), $"Add keywords: {string.Join(", ", missing)}");
        }

        if (!desired.Values.Features.SequenceEqual(observed.Features))
            plan.AddChange("listing.features", string.Join(" | ", observed.Features), string.Join(" | ", desired.Values.Features), "Synchronize product features");

        var wantedLanguages = (desired.Site.SupportedLanguageCodes.Count > 0
            ? desired.Site.SupportedLanguageCodes
            : [desired.Site.LanguageCode]).Select(x => x.ToLowerInvariant()).ToHashSet();
        var extraLanguages = observed.LanguageCodes.Where(x => !wantedLanguages.Contains(x.ToLowerInvariant())).ToList();
        if (extraLanguages.Count > 0)
            plan.AddChange("listing.languages", string.Join(",", observed.LanguageCodes), string.Join(",", wantedLanguages), "Remove undeclared Store languages");

        if (desired.Listing.Screenshot && !observed.HasScreenshot) plan.AddChange("listing.asset.screenshot", "missing", Path.GetFileName(desired.Assets.Screenshot), "Upload required desktop screenshot");
        if (desired.Listing.Boxart && !observed.HasBoxart) plan.AddChange("listing.asset.boxart", "missing", Path.GetFileName(desired.Assets.Boxart), "Upload boxart");
        if (desired.Listing.Logo300 && !observed.HasLogo300) plan.AddChange("listing.asset.logo300", "missing", Path.GetFileName(desired.Assets.Logo300), "Upload 300 logo");
        if (desired.Listing.Logo150 && !observed.HasLogo150) plan.AddChange("listing.asset.logo150", "missing", Path.GetFileName(desired.Assets.Logo150), "Upload 150 logo");
        if (desired.Listing.Logo71 && !observed.HasLogo71) plan.AddChange("listing.asset.logo71", "missing", Path.GetFileName(desired.Assets.Logo71), "Upload 71 logo");

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        if (!string.IsNullOrEmpty(desired.Values.Description))
            await _native.SetFieldAsync(["#description-required"], desired.Values.Description, "description");

        if (!string.IsNullOrEmpty(desired.Values.ShortDescription))
            await _native.SetFieldAsync(["#shortDescription"], desired.Values.ShortDescription, "short description");

        await ConvergeFeatureFieldCountAsync(desired.Values.Features.Count);
        for (int i = 0; i < desired.Values.Features.Count; i++)
        {
            string selector = $"#feature-{i}";
            bool exists = await _client.EvaluateAsync<bool>($"document.querySelector('{selector}') !== null");
            if (!exists)
            {
                await ClickAddFeatureAsync();
                await _waiter.RequireAsync(async () =>
                {
                    return await _client.EvaluateAsync<bool>($"document.querySelector('{selector}') !== null");
                }, TimeSpan.FromSeconds(10), $"Wait for feature input #{i}");
            }
            await _native.SetFieldAsync([selector], desired.Values.Features[i], $"feature #{i + 1}");
        }

        if (desired.Values.Keywords.Count > 0)
        {
            await SetKeywordsAsync(desired.Values.Keywords);
        }

        await UploadVisualAssetsAsync(desired);

        // Explicit pre-save validation: Ensure screenshot is present before clicking Save!
        if (desired.Listing.Screenshot)
        {
            bool hasScreenshot = await SectionHasImageAsync(["屏幕截图", "screenshot", "desktop", "桌面"], -1);
            if (!hasScreenshot)
            {
                throw new InvalidOperationException("[ERROR] 必须确保桌面屏幕截图上传成功才能点击保存！当前截图数量为 0。");
            }
            Console.WriteLine("[PASS] 桌面屏幕截图上传验证通过！");
        }

        Console.WriteLine("[INFO] 点击底部【保存】按钮保存 Store 一览详情...");
        await _native.ClickStrictAsync([
            "button[name=\"save_button\"]",
            "button[data-l10n-key=\"AppSubmission_SaveButton\"]",
            "button[data-l10n-key=\"appsubmission_savebutton\"]",
            "button[uitestid=\"saveButtonListing\"]",
            "input#saveButtonListing",
            "button#saveButtonListing",
            "input[value=\"\u4fdd\u5b58\"]",
            "button[value=\"\u4fdd\u5b58\"]"
        ], "Save listing");

        await Task.Delay(3000);
        await _native.AssertNoVisibleErrorsAsync();
    }

    private async Task ClickAddFeatureAsync()
    {
        await _native.ClickByTextAsync(["添加其他项目", "Add another item"], "Add product feature");
        await Task.Delay(200);
    }

    private async Task ConvergeFeatureFieldCountAsync(int desiredCount)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int count = await _client.EvaluateAsync<int>("document.querySelectorAll('input[id^=\"feature-\"]').length");
            if (count == desiredCount) return;
            if (count < desiredCount)
            {
                await ClickAddFeatureAsync();
                continue;
            }

            var rect = await _client.EvaluateAsync<JsElementRect>("""
            (async () => {
              const inputs=Array.from(document.querySelectorAll('input[id^="feature-"]'));
              const last=inputs[inputs.length-1]; if(!last) return null;
              const index=(last.id.match(/(\d+)$/)||[])[1];
              const b=document.querySelector('#delete-feature-'+index); if(!b) return null;
              b.scrollIntoView({block:'center',behavior:'instant'}); await new Promise(r=>setTimeout(r,80));
              const r=b.getBoundingClientRect(); return {x:r.left+r.width/2,y:r.top+r.height/2,width:r.width,height:r.height};
            })()
            """);
            if (rect == null) throw new InvalidOperationException("Extra feature fields exist but their delete control was not found.");
            await _input.ClickCoordinatesAsync(rect.X, rect.Y, "Remove extra product feature");
            await Task.Delay(120);
        }
        throw new InvalidOperationException($"Feature field count did not converge to {desiredCount}.");
    }

    private async Task SetKeywordsAsync(List<string> desiredKeywords)
    {
        var targetKeywords = desiredKeywords.Take(7).ToList();

        // 0. Ensure "其他信息" section is expanded
        await _client.EvaluateAsync<object>("""
        (() => {
          const searchTerms = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
          if (!searchTerms || searchTerms.getBoundingClientRect().height === 0) {
            const buttons = Array.from(document.querySelectorAll('button, he-button, [role="button"]'));
            for (const b of buttons) {
              const t = (b.innerText || '').trim();
              if (t.includes('其他信息') || t.includes('显示') || t.includes('展开') || t.includes('Show')) {
                b.click();
              }
            }
          }
          if (searchTerms) {
            searchTerms.scrollIntoView({ block: 'center', behavior: 'instant' });
          }
          return null;
        })()
        """);
        await Task.Delay(400);

        // 1. Read current live tags
        var existing = await _client.EvaluateAsync<List<string>>("""
        (() => {
          const root = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
          if (!root) return [];
          return Array.from(root.querySelectorAll('he-option'))
            .map(e => (e.innerText || e.getAttribute('value') || '').trim())
            .filter(Boolean);
        })()
        """) ?? [];

        Console.WriteLine($"[INFO] 关键词检测：当前页面已有 {existing.Count} 个关键词: [{string.Join(", ", existing)}]");
        Console.WriteLine($"[INFO] 目标配置关键词 ({targetKeywords.Count}个): [{string.Join(", ", targetKeywords)}]");

        // 2. If existing keywords already contain all desired keywords and <= 7, no need to touch
        if (targetKeywords.All(k => existing.Contains(k)) && existing.Count <= 7)
        {
            Console.WriteLine("[PASS] 当前所有目标关键词均已包含且未超过 7 个，保持当前关键词配置。");
            return;
        }

        // 3. Remove unwanted keywords that are not in targetKeywords, or if total count exceeds 7
        var toRemove = existing.Where(k => !targetKeywords.Contains(k)).ToList();

        foreach (var rem in toRemove.Distinct())
        {
            Console.WriteLine($"[INFO] 正在移除多余/旧关键词: {rem}");
            await _client.EvaluateAsync<bool>($$"""
            (() => {
              const root = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
              if (!root) return false;
              const opts = Array.from(root.querySelectorAll('he-option'));
              const target = opts.find(o => (o.innerText || o.getAttribute('value') || '').trim() === {{JsonSerializer.Serialize(rem)}});
              if (target) {
                const btn = target.querySelector('he-button, button, he-icon, .close, [aria-label*="删除"], [aria-label*="Remove"]') || target;
                if (btn.shadowRoot) {
                  const inner = btn.shadowRoot.querySelector('button');
                  if (inner) inner.click();
                }
                btn.click();
                btn.dispatchEvent(new MouseEvent('click', { bubbles: true, composed: true, cancelable: true }));
                return true;
              }
              return false;
            })()
            """);
            await Task.Delay(300);
        }

        // 4. Re-read current count after removal
        var currentAfterRemoval = await _client.EvaluateAsync<List<string>>("""
        (() => {
          const root = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
          if (!root) return [];
          return Array.from(root.querySelectorAll('he-option'))
            .map(e => (e.innerText || e.getAttribute('value') || '').trim())
            .filter(Boolean);
        })()
        """) ?? [];

        // 5. Add missing target keywords up to maximum of 7
        foreach (var kw in targetKeywords)
        {
            if (currentAfterRemoval.Contains(kw)) continue;
            if (currentAfterRemoval.Count >= 7)
            {
                Console.WriteLine($"[WARN] 关键词已达 7 个上限，停止添加关键词: {kw}");
                break;
            }

            Console.WriteLine($"[INFO] 添加关键词 ({currentAfterRemoval.Count + 1}/7): {kw}");
            
            bool focused = await _client.EvaluateAsync<bool>("""
            (() => {
              const root = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
              if (!root) return false;
              root.scrollIntoView({ block: 'center', behavior: 'instant' });
              const input = (root.shadowRoot ? root.shadowRoot.querySelector('input') : null) || root.querySelector('input') || root;
              input.focus();
              input.click();
              return true;
            })()
            """);

            if (focused)
            {
                await _input.InsertTextAsync(kw);
                await _input.PressKeyAsync("Enter", "Enter", 13);
                currentAfterRemoval.Add(kw);
                await Task.Delay(400);
            }
        }

        Console.WriteLine("[PASS] 关键词校验与填报全部完成！");
    }

    private async Task UploadVisualAssetsAsync(DesiredState desired)
    {
        var uploads = new[]
        {
            new { Key = "screenshot", Path = desired.Assets.Screenshot, Enabled = desired.Listing.Screenshot, Contexts = new[] { "屏幕截图", "screenshot", "desktop", "桌面" }, InputIndex = 0 },
            new { Key = "poster", Path = desired.Assets.Poster, Enabled = desired.Listing.Poster, Contexts = new[] { "9:16", "\u62db\u8d34\u753b" }, InputIndex = -1 },
            new { Key = "boxart", Path = desired.Assets.Boxart, Enabled = desired.Listing.Boxart, Contexts = new[] { "1:1", "\u9177\u56fe" }, InputIndex = -1 },
            new { Key = "logo300", Path = desired.Assets.Logo300, Enabled = desired.Listing.Logo300, Contexts = new[] { "300x300", "300 x 300" }, InputIndex = -1 },
            new { Key = "logo150", Path = desired.Assets.Logo150, Enabled = desired.Listing.Logo150, Contexts = new[] { "150x150", "150 x 150" }, InputIndex = -1 },
            new { Key = "logo71", Path = desired.Assets.Logo71, Enabled = desired.Listing.Logo71, Contexts = new[] { "71x71", "71 x 71" }, InputIndex = -1 },
            new { Key = "superhero", Path = desired.Assets.Superhero, Enabled = desired.Listing.Superhero, Contexts = new[] { "16:9", "\u8d85\u7ea7\u82f1\u96c4\u753b" }, InputIndex = -1 }
        };

        foreach (var up in uploads)
        {
            if (!up.Enabled || string.IsNullOrWhiteSpace(up.Path) || !File.Exists(up.Path)) continue;
            bool hasImage = await SectionHasImageAsync(up.Contexts, up.InputIndex);
            if (hasImage)
            {
                Console.WriteLine($"[INFO] 视觉资产 [{up.Key}] 已存在，跳过上传。");
                continue;
            }

            Console.WriteLine($"[INFO] 正在上传视觉资产 [{up.Key}]: {up.Path} ...");
            string? inputObjId = await GetFileInputObjectIdAsync(up.Contexts, up.InputIndex);
            if (string.IsNullOrWhiteSpace(inputObjId))
            {
                throw new InvalidOperationException($"无法定位视觉资产 [{up.Key}] 的上传控件 (input[type=file])！");
            }

            await _dom.SetFileInputFilesByObjectIdAsync(inputObjId, [up.Path]);
            
            // Wait for preview image / counter to update
            await _waiter.RequireAsync(() => SectionHasImageAsync(up.Contexts, up.InputIndex), TimeSpan.FromSeconds(90), $"Wait for {up.Key} upload preview");
            Console.WriteLine($"[PASS] 视觉资产 [{up.Key}] 上传并渲染成功！");
        }
    }

    private async Task<bool> SectionHasImageAsync(string[] contexts, int inputIndex)
    {
        return await _client.EvaluateAsync<bool>($$"""
        (() => {
          const AllFileInputs = () => {
            const roots=[document];
            for(let i=0;i<roots.length;i++){ try{ for(const e of roots[i].querySelectorAll('*')) if(e.shadowRoot) roots.push(e.shadowRoot); }catch(_){} }
            const out=[]; for(const r of roots){ out.push(...Array.from(r.querySelectorAll('input[type="file"]'))); } return out;
          };
          const texts = {{JsonSerializer.Serialize(contexts)}}.map(x => x.toLowerCase());
          const isScreenshot = texts.some(x => x.includes('屏幕截图') || x.includes('screenshot') || x.includes('desktop') || x.includes('桌面'));
          if (isScreenshot) {
            const m = (document.body.innerText || '').match(/桌面\s*\((\d+)\)/);
            if (m && parseInt(m[1], 10) >= 1) return true;
          }

          const files = AllFileInputs();
          const candidates = {{inputIndex}} >= 0 && {{inputIndex}} < files.length ? [files[{{inputIndex}}]] : files;
          for (const input of candidates) {
            let n = input;
            let t = '';
            for (let k = 0; k < 12 && n; k++, n = n.parentElement) {
              t += ' ' + (n.innerText || '');
              if ({{inputIndex}} >= 0 || texts.some(x => t.toLowerCase().includes(x))) {
                const img = n.querySelector('img[src]:not([src=""])');
                if (img && (img.naturalWidth > 0 || img.width > 0 || (img.src && img.src.length > 10))) return true;
              }
            }
          }
          return false;
        })()
        """);
    }

    private async Task<string?> GetFileInputObjectIdAsync(string[] contexts, int inputIndex)
    {
        return await _dom.GetObjectIdByExpressionAsync($$"""
        (() => {
          const AllFileInputs = () => {
            const roots=[document];
            for(let i=0;i<roots.length;i++){ try{ for(const e of roots[i].querySelectorAll('*')) if(e.shadowRoot) roots.push(e.shadowRoot); }catch(_){} }
            const out=[]; for(const r of roots){ out.push(...Array.from(r.querySelectorAll('input[type="file"]'))); } return out;
          };
          const texts = {{JsonSerializer.Serialize(contexts)}}.map(x => x.toLowerCase());
          const files = AllFileInputs();
          if ({{inputIndex}} >= 0 && {{inputIndex}} < files.length) return files[{{inputIndex}}];
          for (const e of files) {
            let n = e;
            let t = '';
            for (let k = 0; k < 12 && n; k++, n = n.parentElement) {
              t += ' ' + (n.innerText || '');
              if (texts.some(x => t.toLowerCase().includes(x))) return e;
            }
          }
          return files.length > 0 ? files[0] : null;
        })()
        """);
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        var plan = PlanDiff(desired, observed);
        if (plan.HasDifferences) throw new InvalidOperationException($"Listing cold-load verification failed:\n{plan}");
        await _native.AssertNoVisibleErrorsAsync();
    }
}
