using AIBar.Domain;

namespace AIBar.Desktop;

public static class ViewModelDisplayLabels
{
    public const string ServiceQuota = "Private service-reported quota";
    public const string LocallyDerivedAnalytics = "Locally derived analytics";
    public const string EstimatedCost = "Estimated cost; not billed cost.";
    public const string EtaEstimate = "Simple linear estimate from service-reported quota observations.";
}

public sealed record EstimatedCostPresentation(string SourceLabel, string StateLabel, decimal? Value, string CatalogVersion, DateOnly CatalogDate, IReadOnlyList<string> Warnings, IReadOnlyList<PricingTokenFact> TokenFacts);
public sealed record TrendPresentation(string SourceLabel, string StateLabel, string WindowName, string TimeBasis, decimal? AverageDailyTokens, long? DayOverDayTokenChange, IReadOnlyList<DailyUsage> TokenFacts);
public sealed record EtaPresentation(string SourceLabel, string StateLabel, string WindowName, string RateBasis, decimal? ObservedRatePerHour, decimal? RemainingPercentage, DateTimeOffset? EstimatedExhaustionAt, QuotaSnapshot ServiceQuota, IReadOnlyList<QuotaUsageObservation> Observations);
public sealed record DerivedMetricsPresentationState(EstimatedCostPresentation Cost, TrendPresentation Trend, EtaPresentation Eta);

public sealed class DerivedMetricsPresentationMapper
{
    private static readonly IReadOnlyDictionary<PricingWarning, string> WarningCopy = new Dictionary<PricingWarning, string>
    {
        [PricingWarning.UnknownModel] = "Unknown model has no supported price.", [PricingWarning.UnsupportedModel] = "Unsupported model has no supported price.",
        [PricingWarning.PricesMayChange] = "Pricing may change.", [PricingWarning.DiscountsMayApply] = "Discounts may apply.",
        [PricingWarning.RoutingMayApply] = "Routing may apply.", [PricingWarning.ContractTermsMayApply] = "Contract terms may apply.", [PricingWarning.NonAuthoritative] = "Estimated cost is not billed cost."
    };

    public DerivedMetricsPresentationState Map(EstimatedCostResult cost, TrendMetricsResult trend, ExhaustionEtaResult eta) => new(MapCost(cost), MapTrend(trend), MapEta(eta));

    private static EstimatedCostPresentation MapCost(EstimatedCostResult result) => new(ViewModelDisplayLabels.EstimatedCost, State(result.State), result.EstimatedCost, result.CatalogVersion, result.CatalogDate, result.Warnings.Select(warning => WarningCopy[warning]).Append(WarningCopy[PricingWarning.NonAuthoritative]).Distinct().ToArray(), result.TokenFacts);
    private static TrendPresentation MapTrend(TrendMetricsResult result) => new(ViewModelDisplayLabels.LocallyDerivedAnalytics, State(result.State), result.WindowName, result.TimeBasis, result.AverageDailyTokens, result.DayOverDayTokenChange, result.TokenFacts);
    private static EtaPresentation MapEta(ExhaustionEtaResult result) => new(ViewModelDisplayLabels.EtaEstimate, State(result.State), result.WindowName, result.RateBasis, result.ObservedRatePerHour, result.RemainingPercentage, result.EstimatedExhaustionAt, result.ServiceQuota, result.Observations);
    private static string State<T>(T state) where T : struct, Enum => state.ToString() switch { "InsufficientData" => "Insufficient data", "Estimated" => "Estimated", "Incomplete" => "Incomplete", _ => state.ToString() };
}
