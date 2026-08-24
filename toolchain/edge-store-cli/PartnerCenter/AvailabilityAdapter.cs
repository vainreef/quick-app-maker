using Vainreef.EdgeStore.Cdp;
using Vainreef.EdgeStore.ComponentAdapters;
using Vainreef.EdgeStore.State;

namespace Vainreef.EdgeStore.PartnerCenter;

public class AvailabilityAdapter
{
    private readonly CdpClient _client;
    private readonly Waiter _waiter;
    private readonly HeSelectAdapter _heSelect;
    private readonly NativeFormAdapter _native;

    public AvailabilityAdapter(CdpClient client, Waiter waiter, HeSelectAdapter heSelect, NativeFormAdapter native)
    {
        _client = client;
        _waiter = waiter;
        _heSelect = heSelect;
        _native = native;
    }

    public async Task<ObservedAvailability> ObserveAsync()
    {
        await _waiter.WaitUntilAsync(async () =>
        {
            var hasMarket = await _client.EvaluateAsync<bool>("document.querySelector('input[name=\"marketSelection\"]') !== null");
            return hasMarket;
        }, timeout: TimeSpan.FromSeconds(30), description: "Wait for availability form market controls");

        var obs = new ObservedAvailability();

        obs.AllMarkets = await _client.EvaluateAsync<bool>("""
        (() => {
          const e = document.querySelector('input[name="marketSelection"][value="true"]');
          return e ? e.checked : false;
        })()
        """);

        obs.Audience = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('#radioDistribution_PublicAudience');
          return e && e.checked ? 'Public' : 'Unknown';
        })()
        """) ?? "Unknown";

        obs.ReleaseSchedule = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('select[uitestid="AvailableSelector-0"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.StopSelling = await _client.EvaluateAsync<string>("""
        (() => {
          const e = document.querySelector('select[uitestid="StopSellingSelector-0"]');
          return e ? e.value : '';
        })()
        """) ?? "";

        obs.Currency = await _heSelect.ObserveValueAsync("market-group .price-config > he-select");
        obs.PriceTier = await _heSelect.ObserveValueAsync("market-group price-tier-selection he-select");

        obs.SaveButtonEnabled = await _client.EvaluateAsync<bool>("""
        (() => {
          const btn = document.querySelector('button[data-l10n-key="AppSubmission_SaveButton"], input#saveButtonPricing, button#saveButtonPricing');
          return btn ? !btn.disabled : false;
        })()
        """);

        return obs;
    }

    public ReconcilePlan PlanDiff(DesiredState desired, ObservedAvailability observed)
    {
        var plan = new ReconcilePlan { Phase = "availability" };

        if (!observed.AllMarkets)
        {
            plan.AddChange("availability.allMarkets", observed.AllMarkets, true, "Select global markets");
        }

        if (observed.Audience != "Public")
        {
            plan.AddChange("availability.audience", observed.Audience, "Public", "Select public audience");
        }

        if (observed.ReleaseSchedule != "string:asap")
        {
            plan.AddChange("availability.releaseSchedule", observed.ReleaseSchedule, "string:asap", "Set release date to ASAP");
        }

        if (observed.StopSelling != "string:auto-fill")
        {
            plan.AddChange("availability.stopSelling", observed.StopSelling, "string:auto-fill", "Set stop selling to never");
        }

        string expectedCurrency = desired.Pricing.Currency == "CN" ? "CNY - 中国" : desired.Pricing.Currency;
        if (!observed.Currency.Contains("CNY", StringComparison.OrdinalIgnoreCase) &&
            !observed.Currency.Contains("CN", StringComparison.OrdinalIgnoreCase))
        {
            plan.AddChange("availability.currency", observed.Currency, expectedCurrency, "Set base currency to CNY");
        }

        if (!string.IsNullOrEmpty(desired.Pricing.PriceTier) && observed.PriceTier != desired.Pricing.PriceTier)
        {
            plan.AddChange("availability.priceTier", observed.PriceTier, desired.Pricing.PriceTier, $"Set price tier to {desired.Pricing.PriceTier}");
        }

        return plan;
    }

    public async Task ApplyChangesAsync(ReconcilePlan plan, DesiredState desired)
    {
        if (plan.Actions.Any(a => a.Field == "availability.allMarkets"))
        {
            await _native.SetRadioAsync("input[name=\"marketSelection\"][value=\"true\"]", "all markets");
        }

        if (plan.Actions.Any(a => a.Field == "availability.audience"))
        {
            bool hasPublicAudience = await _client.EvaluateAsync<bool>("document.querySelector('#radioDistribution_PublicAudience') !== null");
            if (hasPublicAudience)
            {
                await _native.SetRadioAsync("#radioDistribution_PublicAudience", "public audience");
            }
        }

        if (plan.Actions.Any(a => a.Field == "availability.releaseSchedule"))
        {
            await _native.SetFieldAsync(["select[uitestid=\"AvailableSelector-0\"]"], "string:asap", "publish ASAP");
        }

        if (plan.Actions.Any(a => a.Field == "availability.stopSelling"))
        {
            await _native.SetFieldAsync(["select[uitestid=\"StopSellingSelector-0\"]"], "string:auto-fill", "never stop selling");
        }

        if (plan.Actions.Any(a => a.Field == "availability.currency"))
        {
            await _heSelect.SetValueAsync("market-group .price-config > he-select", "CNY - 中国", "base currency");
        }

        if (plan.Actions.Any(a => a.Field == "availability.priceTier"))
        {
            await _heSelect.SetValueAsync("market-group price-tier-selection he-select", desired.Pricing.PriceTier, "price tier");
        }

        // Save
        await _native.ClickStrictAsync([
            "input#saveButtonPricing",
            "button#saveButtonPricing",
            "input[uitestid=\"saveButtonPricing\"]",
            "button[data-l10n-key=\"AppSubmission_SaveButton\"]"
        ], "Save availability");

        await Task.Delay(2000);
        await _native.AssertNoVisibleErrorsAsync();
    }

    public async Task VerifyAsync(DesiredState desired)
    {
        await _waiter.ReloadAsync(ignoreCache: true);
        var observed = await ObserveAsync();
        var plan = PlanDiff(desired, observed);
        if (plan.HasDifferences)
        {
            throw new InvalidOperationException($"Availability reload verification failed to converge:\n{plan}");
        }
        await _native.AssertNoVisibleErrorsAsync();
    }
}
