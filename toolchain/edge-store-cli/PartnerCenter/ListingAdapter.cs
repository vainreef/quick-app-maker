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
    private List<string> _languageGridCodes = [];

    public ListingAdapter(CdpClient client, DomDriver dom, Waiter waiter, NativeFormAdapter native, InputDriver input)
    {
        _client = client;
        _dom = dom;
        _waiter = waiter;
        _native = native;
        _input = input;
    }

    public async Task EnterLanguageFormAsync(DesiredState desired, bool applyLanguageChanges)
    {
        bool form = await _client.EvaluateAsync<bool>("document.querySelector('#description-required') !== null");
        if (form) return;

        _languageGridCodes = await ReadLanguageGridCodesAsync();
        if (_languageGridCodes.Count > 0)
            Console.WriteLine($"[STATE] Listing language grid: {string.Join(",", _languageGridCodes)}");
        if (applyLanguageChanges) await ConvergeLanguageGridAsync(desired);

        string languageId = desired.Site.LanguageId;
        string languageCode = desired.Site.LanguageCode;
        string? href = await _client.EvaluateAsync<string?>($$"""
        (() => {
          const links=Array.from(document.querySelectorAll('a[href*="listings"]'));
          const a=links.find(x=>(x.href||'').includes('languageid={{languageId}}')) ||
                  links.find(x=>(x.href||'').toLowerCase().includes('languagecode={{languageCode.ToLowerInvariant()}}'));
          return a?.href || null;
        })()
        """);
        if (string.IsNullOrWhiteSpace(href))
            throw new InvalidOperationException($"Listing page is not the {languageCode} form and its language grid has no matching row.");
        await _waiter.NavigateAsync(href, $"listing {languageCode}");
    }

    private async Task ConvergeLanguageGridAsync(DesiredState desired)
    {
        var wanted = desired.Site.SupportedLanguageCodes.Count > 0
            ? desired.Site.SupportedLanguageCodes
            : [desired.Site.LanguageCode];
        for (int iteration = 0; iteration < 300; iteration++)
        {
            var target = await _client.EvaluateAsync<LanguageToggleTarget>($$"""
            (async () => {
              const wanted={{JsonSerializer.Serialize(wanted.Select(x => x.ToLowerInvariant()).ToArray())}};
              const buttons=Array.from(document.querySelectorAll('he-button[slot^="Action-"],button[slot^="Action-"]'));
              for(const b of buttons){
                const slot=b.getAttribute('slot')||'', id=slot.replace(/^Action-/, '');
                const name=document.querySelector('[slot="Name-'+CSS.escape(id)+'"] a[href*="languagecode="],a[slot="Name-'+CSS.escape(id)+'"][href*="languagecode="]');
                if(!name) continue;
                const m=(name.href||'').match(/[?&]languagecode=([^&#]+)/i), code=m?decodeURIComponent(m[1]).toLowerCase():'';
                const action=(b.innerText||b.textContent||'').trim().toLowerCase();
                const active=/remove|删除/.test(action), should=wanted.includes(code);
                if(active===should) continue;
                b.scrollIntoView({block:'center',behavior:'instant'}); await new Promise(r=>setTimeout(r,80));
                const r=b.getBoundingClientRect();
                return {code,action,x:r.left+r.width/2,y:r.top+r.height/2,width:r.width,height:r.height};
              }
              return null;
            })()
            """);
            if (target == null)
            {
                Console.WriteLine($"[INFO] Listing languages converged: {string.Join(",", wanted)}");
                _languageGridCodes = wanted.Select(x => x.ToLowerInvariant()).Distinct().ToList();
                return;
            }
            // Exactly one physical click, then discard the node and re-query after
            // Lit has replaced the grid. Replaying click/mousedown/.click would
            // toggle the language back.
            await _input.ClickCoordinatesAsync(target.X, target.Y, $"Language {target.Code}: {target.Action}");
            await Task.Delay(120);
        }
        throw new InvalidOperationException("Language grid did not converge within 300 live re-query iterations.");
    }

    private async Task<List<string>> ReadLanguageGridCodesAsync()
    {
        return await _client.EvaluateAsync<List<string>>("""
        (() => Array.from(document.querySelectorAll('a[href*="languagecode="]'))
          .map(a => { const m=(a.href||'').match(/[?&]languagecode=([^&#]+)/i); return m ? decodeURIComponent(m[1]).toLowerCase() : ''; })
          .filter(Boolean).filter((x,i,a)=>a.indexOf(x)===i))()
        """) ?? [];
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
        {
            plan.AddChange("listing.description", observed.Description.Length > 20 ? observed.Description[..20] + "..." : observed.Description, "...", "Update description");
        }

        if (!string.IsNullOrEmpty(desired.Values.ShortDescription) && observed.ShortDescription.Trim() != desired.Values.ShortDescription.Trim())
        {
            plan.AddChange("listing.shortDescription", observed.ShortDescription, desired.Values.ShortDescription, "Update short description");
        }

        if (desired.Values.Keywords.Count > 0)
        {
            var missing = desired.Values.Keywords.Except(observed.Keywords).ToList();
            if (missing.Count > 0)
            {
                plan.AddChange("listing.keywords", string.Join(",", observed.Keywords), string.Join(",", desired.Values.Keywords), $"Add keywords: {string.Join(", ", missing)}");
            }
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
        {
            await _native.SetFieldAsync(["#description-required"], desired.Values.Description, "description");
        }

        if (!string.IsNullOrEmpty(desired.Values.ShortDescription))
        {
            await _native.SetFieldAsync(["#shortDescription"], desired.Values.ShortDescription, "short description");
        }

        // Features
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

        // Keywords
        if (desired.Values.Keywords.Count > 0)
        {
            await SetKeywordsAsync(desired.Values.Keywords);
        }

        // Assets Uploads
        await UploadVisualAssetsAsync(desired);

        // Save
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

        await Task.Delay(2500);
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

    private async Task SetKeywordsAsync(List<string> keywords)
    {
        var existing = await _client.EvaluateAsync<List<string>>("""
        (() => {
          const root = document.querySelector('#search-terms') || document.querySelector('he-select[multiple]');
          if (!root) return [];
          return Array.from(root.querySelectorAll('he-option'))
            .filter(e => (e.getAttribute('slot') || '').startsWith('selected-') || e.getAttribute('role') === 'listitem')
            .map(e => (e.innerText || e.getAttribute('value') || '').trim())
            .filter(Boolean);
        })()
        """) ?? [];

        foreach (var kw in keywords)
        {
            if (existing.Contains(kw)) continue;

            await _native.ClickStrictAsync(["#search-terms he-select", "he-select[multiple]"], "keyword control");
            await _input.InsertTextAsync(kw);
            await _input.PressKeyAsync("Enter", "Enter", 13);
            await Task.Delay(300);
        }
    }

    private async Task UploadVisualAssetsAsync(DesiredState desired)
    {
        var uploads = new[]
        {
            new { Key = "screenshot", Path = desired.Assets.Screenshot, Enabled = desired.Listing.Screenshot, Contexts = new[] { "屏幕截图", "screenshot", "desktop" }, InputIndex = -1 },
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
            if (hasImage) continue;

            int? inputNodeId = await GetFileInputNodeIdAsync(up.Contexts, up.InputIndex);
            if (inputNodeId.HasValue)
            {
                await _dom.SetFileInputFilesAsync(inputNodeId.Value, [up.Path]);
                await _waiter.RequireAsync(() => SectionHasImageAsync(up.Contexts, up.InputIndex), TimeSpan.FromSeconds(90), $"Wait for {up.Key} preview");
            }
        }
    }

    private async Task<bool> SectionHasImageAsync(string[] contexts, int inputIndex)
    {
        return await _client.EvaluateAsync<bool>($$"""
        (() => {
          const texts = {{JsonSerializer.Serialize(contexts)}}.map(x => x.toLowerCase());
          const files = Array.from(document.querySelectorAll('input[type="file"]'));
          const candidates = {{inputIndex}} >= 0 ? [files[{{inputIndex}}]].filter(Boolean) : files;
          for (const input of candidates) {
            const card = input.closest('.listing-image-inner, .asset-card');
            let t = card ? (card.innerText || '') : '';
            if (!t) { let n = input; for (let k = 0; k < 12 && n; k++, n = n.parentElement) t += ' ' + (n.innerText || ''); }
            t = t.toLowerCase();
            if ({{inputIndex}} >= 0 || texts.some(x => t.includes(x))) {
              const root = card || input.closest('section') || input.parentElement;
              return !!root.querySelector('img[src]');
            }
          }
          return false;
        })()
        """);
    }

    private async Task<int?> GetFileInputNodeIdAsync(string[] contexts, int inputIndex)
    {
        return await _dom.RequestNodeByExpressionAsync($$"""
        (() => {
          const texts = {{JsonSerializer.Serialize(contexts)}}.map(x => x.toLowerCase());
          const files = Array.from(document.querySelectorAll('input[type="file"]'));
          for (const e of files) {
            const card = e.closest('.listing-image-inner, .asset-card');
            let t = card ? (card.innerText || '') : '';
            if (!t) { let n = e; for (let k = 0; k < 12 && n; k++, n = n.parentElement) t += ' ' + (n.innerText || ''); }
            t = t.toLowerCase();
            if (texts.some(x => t.includes(x))) return e;
          }
          return null;
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

public sealed class LanguageToggleTarget : JsElementRect
{
    public string Code { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
}
