using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class PricingPolicyTests
{
    [Theory]
    [InlineData(1_000_000, 0, 0, 1.25)]
    [InlineData(0, 1_000_000, 0, 0.125)]
    [InlineData(0, 0, 1_000_000, 10.0)]
    public void Pricing_maps_each_component_to_its_checked_in_per_million_rate(long input, long cached, long output, decimal expected)
    {
        var usage = Usage("gpt-5", input, cached, output);

        var result = new PricingPolicy(CheckedInPricingCatalog.Current).Calculate([usage]);

        Assert.Equal(EstimatedCostState.Estimated, result.State);
        Assert.Equal(expected, result.EstimatedCost);
        Assert.Equal(usage.Tokens, result.TokenFacts.Single().Tokens);
    }

    [Fact]
    public void Pricing_exposes_immutable_catalog_version_and_date_provenance()
    {
        var catalog = CheckedInPricingCatalog.Current;
        var result = new PricingPolicy(catalog).Calculate([Usage("gpt-5", 1, 2, 3)]);

        Assert.Equal("2026-07-14", catalog.Version);
        Assert.Equal(new DateOnly(2026, 7, 14), catalog.EffectiveDate);
        Assert.Equal(catalog.Version, result.CatalogVersion);
        Assert.Equal(catalog.EffectiveDate, result.CatalogDate);
        Assert.Equal(new TokenTotals(1, 2, 3), result.TokenFacts.Single().Tokens);
    }

    [Fact]
    public void Pricing_catalog_rates_and_keys_cannot_be_mutated_by_callers()
    {
        IPricingCatalog catalog = CheckedInPricingCatalog.Current;
        var alteredCopy = catalog.Rates["gpt-5"] with { InputPerMillion = 99m };

        Assert.Equal(99m, alteredCopy.InputPerMillion);
        Assert.Equal(new ComponentRates(1.25m, 0.125m, 10m), catalog.Rates["gpt-5"]);
        var rates = Assert.IsAssignableFrom<IDictionary<string, ComponentRates>>(catalog.Rates);
        Assert.Throws<NotSupportedException>(() => rates.Add("mutated", alteredCopy));
        Assert.False(catalog.Rates.ContainsKey("mutated"));
        Assert.Equal("2026-07-14", catalog.Version);
        Assert.Equal(new DateOnly(2026, 7, 14), catalog.EffectiveDate);
    }

    [Fact]
    public void Pricing_returns_incomplete_without_a_number_for_unknown_models()
    {
        var result = new PricingPolicy(CheckedInPricingCatalog.Current).Calculate([Usage(AnalyticsPolicy.UnknownModel, 2, 3, 5)]);

        Assert.Equal(EstimatedCostState.Incomplete, result.State);
        Assert.Null(result.EstimatedCost);
        Assert.Contains(PricingWarning.UnknownModel, result.Warnings);
        Assert.DoesNotContain(PricingWarning.UnsupportedModel, result.Warnings);
    }

    [Fact]
    public void Pricing_returns_incomplete_without_a_fallback_for_unsupported_models()
    {
        var result = new PricingPolicy(CheckedInPricingCatalog.Current).Calculate([Usage("unlisted-model", 2, 3, 5)]);

        Assert.Equal(EstimatedCostState.Incomplete, result.State);
        Assert.Null(result.EstimatedCost);
        Assert.Contains(PricingWarning.UnsupportedModel, result.Warnings);
    }

    [Fact]
    public void Pricing_returns_unavailable_when_no_token_facts_are_available()
    {
        var result = new PricingPolicy(CheckedInPricingCatalog.Current).Calculate([]);

        Assert.Equal(EstimatedCostState.Unavailable, result.State);
        Assert.Null(result.EstimatedCost);
        Assert.Empty(result.TokenFacts);
    }

    [Fact]
    public void Pricing_marks_estimates_as_non_authoritative_and_subject_to_billing_factors()
    {
        var result = new PricingPolicy(CheckedInPricingCatalog.Current).Calculate([Usage("gpt-5", 1, 1, 1)]);

        Assert.Equal(EstimatedCostState.Estimated, result.State);
        Assert.Equal(
            [PricingWarning.PricesMayChange, PricingWarning.DiscountsMayApply, PricingWarning.RoutingMayApply, PricingWarning.ContractTermsMayApply, PricingWarning.NonAuthoritative],
            result.Warnings);
        var warnings = Assert.IsAssignableFrom<IList<PricingWarning>>(result.Warnings);
        Assert.Throws<NotSupportedException>(() => warnings[0] = PricingWarning.UnknownModel);
    }

    private static DailyUsage Usage(string model, long input, long cached, long output) =>
        new(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, model, new TokenTotals(input, cached, output));
}
