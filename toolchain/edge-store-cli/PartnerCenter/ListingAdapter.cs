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

    public ListingAdapter(CdpClient client, DomDriver dom, Waiter waiter, NativeFormAdapter native, InputDriver input)
    {
        _client = client;
        _dom = dom;
        _waiter = waiter;
        _native = native;
        _input = input;
    }

    public async Task<ObservedListing> ObserveAsync()
    {
        await _waiter.WaitUntilAsync(async () =>
        {
            return await _client.EvaluateAsync<bool>("document.querySelector('#description-required') !== null");
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for listing description field");

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
        for (int i = 0; i < desired.Values.Features.Count; i++)
        {
            string selector = $"#feature-{i}";
            bool exists = await _client.EvaluateAsync<bool>($"document.querySelector('{selector}') !== null");
            if (!exists)
            {
                await ClickAddFeatureAsync();
                await _waiter.WaitUntilAsync(async () =>
                {
                    return await _client.EvaluateAsync<bool>($"document.querySelector('{selector}') !== null");
                }, timeout: TimeSpan.FromSeconds(10), description: $"Wait for feature input #{i}");
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
        var rect = await _client.EvaluateAsync<JsElementRect>("""
        (() => {
          const els = Array.from(document.querySelectorAll('button, a, he-button, span')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return (e.innerText || '').trim() === '\u6dfb\u52a0\u5176\u4ed6\u9879\u76ee' && r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden';
          });
          if (els.length === 0) return null;
          const r = els[0].getBoundingClientRect();
          const cx = r.left + r.width / 2, cy = r.top + r.height / 2;
          if (cx <= 0 || cy <= 0 || cx > window.innerWidth || cy > window.innerHeight) return null;
          return { x: cx, y: cy, width: r.width, height: r.height };
        })()
        """);

        if (rect != null)
        {
            await _input.ClickCoordinatesAsync(rect.X, rect.Y, "Add product feature");
            await Task.Delay(300);
        }
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
            new { Key = "screenshot", Path = desired.Assets.Screenshot, Enabled = desired.Listing.Screenshot, Contexts = Array.Empty<string>(), InputIndex = 0 },
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
                await Task.Delay(2000);
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
        int rootId = await _dom.GetRootNodeIdAsync();
        var allInputs = await _dom.QuerySelectorAllAsync(rootId, "input[type=\"file\"]");
        if (allInputs.Count == 0) return null;

        if (inputIndex >= 0 && inputIndex < allInputs.Count)
        {
            return allInputs[inputIndex];
        }

        var matchedIndex = await _client.EvaluateAsync<int?>($$"""
        (() => {
          const texts = {{JsonSerializer.Serialize(contexts)}}.map(x => x.toLowerCase());
          const files = Array.from(document.querySelectorAll('input[type="file"]'));
          for (let i = 0; i < files.length; i++) {
            const e = files[i];
            const card = e.closest('.listing-image-inner, .asset-card');
            let t = card ? (card.innerText || '') : '';
            if (!t) { let n = e; for (let k = 0; k < 12 && n; k++, n = n.parentElement) t += ' ' + (n.innerText || ''); }
            t = t.toLowerCase();
            if (texts.some(x => t.includes(x))) return i;
          }
          return null;
        })()
        """);

        if (matchedIndex.HasValue && matchedIndex.Value >= 0 && matchedIndex.Value < allInputs.Count)
        {
            return allInputs[matchedIndex.Value];
        }

        return null;
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        await _native.AssertNoVisibleErrorsAsync();
    }
}
