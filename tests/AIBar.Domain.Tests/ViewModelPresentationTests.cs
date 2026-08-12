using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ViewModelPresentationTests
{
    [Fact]
    public void Separates_service_quota_local_analytics_and_estimated_cost_labels()
    {
        var view = new DerivedMetricsPresentationMapper().Map(Cost(EstimatedCostState.Estimated, 1.25m), Trend(DerivedMetricState.Available), Eta(DerivedMetricState.Available));

        Assert.Equal("Private service-reported quota", ViewModelDisplayLabels.ServiceQuota);
        Assert.Equal("Locally derived analytics", view.Trend.SourceLabel);
        Assert.Equal("Estimated cost; not billed cost.", view.Cost.SourceLabel);
        Assert.Contains("estimate", view.Eta.SourceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Maps_estimate_and_unavailable_states_without_fabricating_a_cost()
    {
        var estimated = new DerivedMetricsPresentationMapper().Map(Cost(EstimatedCostState.Estimated, 1.25m), Trend(DerivedMetricState.Available), Eta(DerivedMetricState.Available));
        var unavailable = new DerivedMetricsPresentationMapper().Map(Cost(EstimatedCostState.Unavailable, null), Trend(DerivedMetricState.InsufficientData), Eta(DerivedMetricState.Unavailable));

        Assert.Equal("Estimated", estimated.Cost.StateLabel); Assert.Equal(1.25m, estimated.Cost.Value);
        Assert.Equal("Unavailable", unavailable.Cost.StateLabel); Assert.Null(unavailable.Cost.Value);
    }

    [Fact]
    public void Maps_unknown_and_unsupported_warnings_with_non_authoritative_language()
    {
        var view = new DerivedMetricsPresentationMapper().Map(Cost(EstimatedCostState.Incomplete, null, PricingWarning.UnknownModel), Trend(DerivedMetricState.Available), Eta(DerivedMetricState.Unsupported));

        Assert.Contains("Unknown model has no supported price.", view.Cost.Warnings);
        Assert.Contains(view.Cost.Warnings, warning => warning.Contains("not billed", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(view.Cost.Warnings, warning => warning.Contains("invoice", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Unsupported", view.Eta.StateLabel);
    }

    [Fact]
    public void Stale_unknown_unsupported_and_insufficient_snapshots_preserve_factual_aggregates()
    {
        var cost = Cost(EstimatedCostState.Incomplete, null, PricingWarning.UnsupportedModel);
        var trend = Trend(DerivedMetricState.InsufficientData);
        var eta = Eta(DerivedMetricState.Unavailable, stale: true);

        var view = new DerivedMetricsPresentationMapper().Map(cost, trend, eta);

        Assert.Equal(cost.TokenFacts, view.Cost.TokenFacts); Assert.Equal(trend.TokenFacts, view.Trend.TokenFacts);
        Assert.Equal(eta.ServiceQuota, view.Eta.ServiceQuota); Assert.Equal(eta.Observations, view.Eta.Observations);
        Assert.Equal("Insufficient data", view.Trend.StateLabel); Assert.Equal("Unavailable", view.Eta.StateLabel);
    }

    private static EstimatedCostResult Cost(EstimatedCostState state, decimal? value, params PricingWarning[] warnings) => new(state, value, "v1", new(2026, 7, 14), warnings, [new("gpt-5", new(7, 8, 9))]);
    private static TrendMetricsResult Trend(DerivedMetricState state) => new(state, "7 complete local days", "local calendar days", null, null, [new(new(2026, 7, 14), "UTC", TimeSpan.Zero, "Unknown", new(7, 8, 9))]);
    private static ExhaustionEtaResult Eta(DerivedMetricState state, bool stale = false) { var now = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero); var quota = new QuotaSnapshot(new(40, now.AddHours(1)), new(20, now.AddDays(7)), stale ? now.AddHours(-1) : now); return new(state, "current primary service window", "observed service quota percentage per hour", "unavailable", null, null, null, quota, [new(now.AddMinutes(-30), 40, quota.Primary!.ResetAt)]); }
}
