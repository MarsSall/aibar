using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class FoundationContractsTests
{
    [Fact]
    public void Freshness_is_current_at_the_threshold_and_stale_after_it()
    {
        var retrievedAt = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
        var snapshot = Snapshot(retrievedAt);
        var policy = new FreshnessPolicy(TimeSpan.FromMinutes(5));

        Assert.Equal(FreshnessState.Current, policy.Evaluate(snapshot, retrievedAt.AddMinutes(5)));
        Assert.Equal(FreshnessState.Stale, policy.Evaluate(snapshot, retrievedAt.AddMinutes(5).AddTicks(1)));
    }

    [Fact]
    public void Freshness_is_unavailable_without_a_snapshot()
    {
        var fixedNow = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
        var policy = new FreshnessPolicy(TimeSpan.FromMinutes(5));

        Assert.Equal(FreshnessState.Unavailable, policy.Evaluate(null, fixedNow));
    }

    [Fact]
    public void Countdown_uses_the_service_reported_reset_instant_and_never_goes_negative()
    {
        var now = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
        var window = new QuotaWindow(42, now.AddMinutes(90));

        Assert.Equal(TimeSpan.FromMinutes(90), window.CountdownAt(now));
        Assert.Equal(TimeSpan.Zero, window.CountdownAt(now.AddHours(2)));
    }

    [Fact]
    public void Token_deltas_clamp_each_cumulative_component_independently()
    {
        var previous = new TokenTotals(100, 50, 25);
        var current = new TokenTotals(90, 75, 20);

        Assert.Equal(new TokenTotals(0, 25, 0), TokenDelta.FromCumulative(previous, current));
    }

    [Fact]
    public void Fixed_clock_and_time_policy_make_time_basis_deterministic()
    {
        var instant = new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
        var clock = new FixedClock(instant);
        var policy = new TimeZoneLocalTimePolicy(TimeZoneInfo.Utc);

        Assert.Equal(instant, clock.UtcNow);
        Assert.Equal(instant, policy.ToLocal(instant));
    }

    [Fact]
    public void Service_quota_and_local_usage_are_immutable_distinct_data_types()
    {
        Assert.NotEqual(typeof(QuotaSnapshot), typeof(DailyUsage));
        Assert.All(typeof(QuotaSnapshot).GetProperties(), property => Assert.False(property.CanWrite));
        Assert.All(typeof(DailyUsage).GetProperties(), property => Assert.False(property.CanWrite));
    }

    private static QuotaSnapshot Snapshot(DateTimeOffset retrievedAt) => new(
        new QuotaWindow(42, retrievedAt.AddHours(1)),
        new QuotaWindow(12, retrievedAt.AddDays(1)),
        retrievedAt);
}
