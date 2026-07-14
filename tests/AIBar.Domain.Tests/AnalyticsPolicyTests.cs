using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class AnalyticsPolicyTests
{
    private static readonly TimeZoneInfo ControlledEastern = TimeZoneInfo.CreateCustomTimeZone(
        "Controlled Eastern Standard Time",
        TimeSpan.FromHours(-5),
        "Controlled Eastern Standard Time",
        "Controlled Eastern Standard Time",
        "Controlled Eastern Daylight Time",
        new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2020, 1, 1),
                new DateTime(2030, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday))
        });

    [Fact]
    public void Aggregation_clamps_each_cumulative_component()
    {
        var policy = new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, "windows-local-day-v1"));
        var result = policy.Aggregate(new[]
        {
            new TokenObservation(new DateTimeOffset(2026, 11, 1, 4, 30, 0, TimeSpan.Zero), "gpt-5", true, new(100, 50, 25), new(90, 75, 20))
        });

        Assert.Equal(new TokenTotals(0, 25, 0), Assert.Single(result.Usage).Tokens);
    }

    [Theory]
    [InlineData("   ", true)]
    [InlineData("gpt-5", false)]
    public void Aggregation_attributes_trusted_blank_and_untrusted_named_models_to_unknown(string model, bool trusted)
    {
        var policy = new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, "windows-local-day-v1"));
        var result = policy.Aggregate(new[]
        {
            new TokenObservation(new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero), model, trusted, new(0, 0, 0), new(3, 4, 5))
        });

        Assert.Equal(AnalyticsPolicy.UnknownModel, Assert.Single(result.Usage).Model);
    }

    [Fact]
    public void Aggregation_keeps_dst_offsets_separate_under_controlled_timezone_rules()
    {
        var result = new AnalyticsPolicy(new TimeZoneLocalDayPolicy(ControlledEastern, "windows-local-day-v1")).Aggregate(new[]
        {
            new TokenObservation(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), "gpt-5", true, new(0, 0, 0), new(1, 0, 0)),
            new TokenObservation(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), "gpt-5", true, new(0, 0, 0), new(1, 0, 0))
        });

        Assert.Equal(2, result.Usage.Count);
        Assert.Contains(result.Usage, usage => usage.ObservedOffset == TimeSpan.FromHours(-4));
        Assert.Contains(result.Usage, usage => usage.ObservedOffset == TimeSpan.FromHours(-5));
    }

    [Fact]
    public void Most_used_model_uses_every_token_component_for_a_strict_winner()
    {
        var usage = new[]
        {
            new DailyUsage(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, "winner", new(4, 5, 6)),
            new DailyUsage(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, "less-input", new(3, 5, 6)),
            new DailyUsage(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, "less-cached-input", new(4, 4, 6)),
            new DailyUsage(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, "less-output", new(4, 5, 5))
        };

        Assert.Equal("winner", AnalyticsPolicy.MostUsedModel(usage));
    }

    [Fact]
    public void Most_used_model_breaks_equal_total_ties_ordinally()
    {
        var usage = new[]
        {
            new DailyUsage(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, "zeta", new(5, 1, 4)),
            new DailyUsage(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, "alpha", new(3, 2, 5))
        };

        Assert.Equal("alpha", AnalyticsPolicy.MostUsedModel(usage));
    }

    [Fact]
    public void Local_day_policy_retains_utc_timezone_offset_and_dst_provenance_under_controlled_rules()
    {
        var policy = new TimeZoneLocalDayPolicy(ControlledEastern, "windows-local-day-v1");
        var beforeMidnight = policy.Assign(new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero));
        var afterFallback = policy.Assign(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 31), beforeMidnight.LocalDay);
        Assert.Equal(new DateOnly(2026, 11, 1), afterFallback.LocalDay);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 6, 30, 0, TimeSpan.Zero), afterFallback.EventUtc);
        Assert.Equal("Controlled Eastern Standard Time", afterFallback.TimeZoneId);
        Assert.Equal(TimeSpan.FromHours(-5), afterFallback.ObservedOffset);
        Assert.Equal("windows-local-day-v1", afterFallback.PolicyVersion);
    }

    [Theory]
    [InlineData("windows-local-day-v1", "windows-local-day-v1", AnalyticsRebuildDecision.None)]
    [InlineData("windows-local-day-v1", "windows-local-day-v2", AnalyticsRebuildDecision.RebuildRequired)]
    public void Policy_version_mismatch_requires_explicit_rebuild(string persisted, string active, AnalyticsRebuildDecision expected)
    {
        Assert.Equal(expected, AnalyticsPolicy.RebuildDecision(persisted, active));
    }
}
