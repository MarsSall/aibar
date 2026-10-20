using System.Collections.ObjectModel;

namespace AIBar.Domain;

public enum EstimatedCostState { Estimated, Incomplete, Unavailable }

public enum PricingWarning
{
    UnknownModel,
    UnsupportedModel,
    PricesMayChange,
    DiscountsMayApply,
    RoutingMayApply,
    ContractTermsMayApply,
    NonAuthoritative
}

public readonly record struct ComponentRates(decimal InputPerMillion, decimal CachedInputPerMillion, decimal OutputPerMillion, decimal CacheWritePerMillion = 0);

public sealed record PricingTokenFact(string Model, TokenTotals Tokens);

public sealed record EstimatedCostResult(
    EstimatedCostState State,
    decimal? EstimatedCost,
    string CatalogVersion,
    DateOnly CatalogDate,
    IReadOnlyList<PricingWarning> Warnings,
    IReadOnlyList<PricingTokenFact> TokenFacts);

public sealed class CheckedInPricingCatalog : IPricingCatalog
{
    private static readonly IReadOnlyDictionary<string, ComponentRates> CatalogRates = new ReadOnlyDictionary<string, ComponentRates>(
        new Dictionary<string, ComponentRates>(StringComparer.Ordinal)
        {
            ["claude-fable-5-1"] = new(10m, .25m, 50m, 12.5m),
            ["claude-fable-5"] = new(10m, 1m, 50m, 12.5m),
            ["claude-opus-5-5"] = new(4m, .2m, 20m, 5m),
            ["claude-opus-5"] = new(5m, .5m, 25m, 6.25m),
            ["claude-sonnet-5-5"] = new(2m, .1m, 10m, 2.5m),
            ["claude-sonnet-5"] = new(2m, .2m, 10m, 2.5m),
            ["claude-haiku-5-5"] = new(.1m, .01m, .5m, .125m),
            ["claude-haiku-4-5"] = new(1m, .1m, 5m, 1.25m),
            ["gpt-6-sol"] = new(2m, .2m, 10m),
            ["gpt-6.1-sol"] = new(2m, .1m, 10m),
            ["gpt-6-luna"] = new(.1m, .01m, .5m),
            ["gpt-6-astra"] = new(10m, 1m, 50m),
            ["gpt-5.6-sol"] = new(4m, .4m, 20m),
            ["gpt-5.6-terra"] = new(2m, .2m, 12m),
            ["gpt-5.6-luna"] = new(.2m, .02m, 1.2m),
            ["gpt-5.5"] = new(5m, .5m, 30m),
            ["gpt-5.4"] = new(2.5m, .25m, 15m),
            ["gpt-5.4-mini"] = new(.75m, .075m, 4.5m),
            ["gpt-5.2"] = new(1.75m, .175m, 14m),
            ["gpt-5.1"] = new(1.25m, .125m, 10m),
            ["gpt-5"] = new(1.25m, .125m, 10m),
            ["gpt-5-mini"] = new(.25m, .025m, 2m)
        });

    private CheckedInPricingCatalog() { }

    public static CheckedInPricingCatalog Current { get; } = new();
    public string Version => "2026-10-19";
    public DateOnly EffectiveDate => new(2026, 10, 19);
    public IReadOnlyDictionary<string, ComponentRates> Rates => CatalogRates;
    public bool TryGetRates(string model, out ComponentRates rates)
    {
        // Only Claude's literal eight-digit date suffix is an approved alias.
        if (model.StartsWith("claude-", StringComparison.Ordinal) && model.Length > 9 &&
            model[^9] == '-' && model[^8..].All(c => c is >= '0' and <= '9'))
            model = model[..^9];
        return CatalogRates.TryGetValue(model, out rates);
    }
}

public sealed class PricingPolicy(IPricingCatalog catalog)
{
    private static readonly IReadOnlyList<PricingWarning> StandardWarnings = Array.AsReadOnly<PricingWarning>([
        PricingWarning.PricesMayChange,
        PricingWarning.DiscountsMayApply,
        PricingWarning.RoutingMayApply,
        PricingWarning.ContractTermsMayApply,
        PricingWarning.NonAuthoritative
    ]);

    public EstimatedCostResult Calculate(IEnumerable<DailyUsage> usage)
    {
        var facts = usage.Select(item => new PricingTokenFact(item.Model, item.Tokens)).ToArray();
        if (facts.Length == 0)
            return Result(EstimatedCostState.Unavailable, null, [], facts);

        decimal total = 0;
        foreach (var fact in facts)
        {
            if (StringComparer.Ordinal.Equals(fact.Model, AnalyticsPolicy.UnknownModel))
                return Result(EstimatedCostState.Incomplete, null, Warnings(PricingWarning.UnknownModel), facts);

            if (!catalog.TryGetRates(fact.Model, out var rates))
                return Result(EstimatedCostState.Incomplete, null, Warnings(PricingWarning.UnsupportedModel), facts);

            total += fact.Tokens.Input * rates.InputPerMillion / 1_000_000m
                + fact.Tokens.CachedInput * rates.CachedInputPerMillion / 1_000_000m
                + fact.Tokens.Output * rates.OutputPerMillion / 1_000_000m;
        }

        return Result(EstimatedCostState.Estimated, total, StandardWarnings, facts);
    }

    private EstimatedCostResult Result(EstimatedCostState state, decimal? cost, IReadOnlyList<PricingWarning> warnings, IReadOnlyList<PricingTokenFact> facts) =>
        new(state, cost, catalog.Version, catalog.EffectiveDate, warnings, facts);

    private static IReadOnlyList<PricingWarning> Warnings(PricingWarning first) =>
        Array.AsReadOnly([first, .. StandardWarnings]);
}
