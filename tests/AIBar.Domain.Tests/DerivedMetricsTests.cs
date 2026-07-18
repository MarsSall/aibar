using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class DerivedMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Trends_use_seven_complete_local_days_with_explicit_name_and_time_basis()
    {
        var policy = new NamedTrendPolicy(new FixedClock(Now), new TimeZoneLocalTimePolicy(TimeZoneInfo.Utc));
        var usage = Enumerable.Range(8, 7).Select(day => Usage(new DateOnly(2026, 7, day), day * 10)).Append(Usage(new DateOnly(2026, 7, 15), 999));

        var result = policy.Calculate(usage);

        Assert.Equal(DerivedMetricState.Available, result.State);
        Assert.Equal("7 complete local days", result.WindowName);
        Assert.Equal("local calendar days", result.TimeBasis);
        Assert.Equal(110, result.AverageDailyTokens);
        Assert.Equal(10, result.DayOverDayTokenChange);
        Assert.DoesNotContain(result.TokenFacts, fact => fact.LocalDay == new DateOnly(2026, 7, 15));
    }

    [Fact]
    public void Trends_are_insufficient_without_all_seven_complete_local_days()
    {
        var result = new NamedTrendPolicy(new FixedClock(Now), new TimeZoneLocalTimePolicy(TimeZoneInfo.Utc))
            .Calculate(Enumerable.Range(8, 6).Select(day => Usage(new DateOnly(2026, 7, day), 10)));

        Assert.Equal(DerivedMetricState.InsufficientData, result.State);
        Assert.Null(result.AverageDailyTokens);
        Assert.Null(result.DayOverDayTokenChange);
        Assert.Equal(6, result.TokenFacts.Count);
    }

    [Fact]
    public void Eta_rejects_observations_from_another_service_window_without_forecast_language()
    {
        var result = Eta().Calculate(Snapshot(), new QuotaUsageObservation(Now.AddHours(-2), 20, Now.AddHours(3)), new QuotaUsageObservation(Now.AddHours(-1), 40, Now.AddHours(3)));

        Assert.Equal(DerivedMetricState.Unavailable, result.State);
        Assert.Null(result.EstimatedExhaustionAt);
        Assert.DoesNotContain("prediction", result.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hourly", result.Label, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Eta_is_unavailable_for_zero_or_negative_observed_rate(decimal change)
    {
        var result = Eta().Calculate(Snapshot(), Observation(new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero), 40), Observation(new(2026, 7, 15, 11, 0, 0, TimeSpan.Zero), 40 + change));

        Assert.Equal(DerivedMetricState.Unavailable, result.State);
        Assert.Null(result.EstimatedExhaustionAt);
    }

    [Theory]
    [InlineData(-1, 40, DerivedMetricState.Unavailable)]
    [InlineData(20, 101, DerivedMetricState.Unavailable)]
    [InlineData(0, 40, DerivedMetricState.Available)]
    [InlineData(20, 100, DerivedMetricState.Available)]
    public void Eta_accepts_only_observation_percentages_from_zero_through_one_hundred(
        decimal firstPercentage, decimal lastPercentage, DerivedMetricState expectedState)
    {
        var result = Eta().Calculate(Snapshot(), Observation(Now.AddHours(-2), firstPercentage), Observation(Now.AddHours(-1), lastPercentage));

        Assert.Equal(expectedState, result.State);
    }

    [Fact]
    public void Eta_is_unavailable_for_stale_quota_or_invalid_service_reset()
    {
        var stale = Eta().Calculate(Snapshot(retrievedAt: Now.AddMinutes(-6)), Observation(Now.AddHours(-2), 20), Observation(Now.AddHours(-1), 40));
        var invalidReset = Eta().Calculate(Snapshot(resetAt: Now), Observation(Now.AddHours(-2), 20), Observation(Now.AddHours(-1), 40));

        Assert.Equal(DerivedMetricState.Unavailable, stale.State);
        Assert.Equal(DerivedMetricState.Unavailable, invalidReset.State);
    }

    [Fact]
    public void Eta_is_a_simple_linear_estimate_only_for_current_service_window_observations()
    {
        var result = Eta().Calculate(Snapshot(), Observation(Now.AddHours(-2), 20), Observation(Now.AddHours(-1), 40));

        Assert.Equal(DerivedMetricState.Available, result.State);
        Assert.Equal("current primary service window", result.WindowName);
        Assert.Equal("observed service quota percentage per hour", result.RateBasis);
        Assert.Equal(60, result.RemainingPercentage);
        Assert.Equal(Now.AddHours(3), result.EstimatedExhaustionAt);
        Assert.Equal("simple linear estimate", result.Label);
    }

    [Fact]
    public void Unsupported_forecasting_and_unavailable_estimates_preserve_source_facts()
    {
        var snapshot = Snapshot();
        var observations = new[] { Observation(Now.AddHours(-1), 40) };
        var result = Eta().Calculate(snapshot, observations);

        Assert.Equal(DerivedMetricState.Unsupported, result.State);
        Assert.Equal(snapshot, result.ServiceQuota);
        Assert.Equal(observations, result.Observations);
        Assert.DoesNotContain("prediction", result.Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hourly history", result.Label, StringComparison.OrdinalIgnoreCase);
    }

    private static LinearExhaustionEtaPolicy Eta() => new(new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(5)));
    private static DailyUsage Usage(DateOnly day, long tokens) => new(day, "UTC", TimeSpan.Zero, "gpt-5", new(tokens, 0, 0));
    private static QuotaUsageObservation Observation(DateTimeOffset at, decimal percentage) => new(at, percentage, Now.AddHours(4));
    private static QuotaSnapshot Snapshot(DateTimeOffset? retrievedAt = null, DateTimeOffset? resetAt = null) => new(new(40, resetAt ?? Now.AddHours(4)), new(20, Now.AddDays(6)), retrievedAt ?? Now);
}
