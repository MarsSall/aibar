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

public readonly record struct ComponentRates(decimal InputPerMillion, decimal CachedInputPerMillion, decimal OutputPerMillion);

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
            ["gpt-5"] = new(1.25m, 0.125m, 10m)
        });

    private CheckedInPricingCatalog() { }

    public static CheckedInPricingCatalog Current { get; } = new();
    public string Version => "2026-07-14";
    public DateOnly EffectiveDate => new(2026, 7, 14);
    public IReadOnlyDictionary<string, ComponentRates> Rates => CatalogRates;
    public bool TryGetRates(string model, out ComponentRates rates) => CatalogRates.TryGetValue(model, out rates!);
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
