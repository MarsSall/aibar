using AIBar.Domain;

namespace AIBar.Domain.Tests;

/// <summary>Providers report the same reset with sub-second jitter; only a real cycle change is a reset.</summary>
public sealed class QuotaResetJitterTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WeeklyReset = Now.AddDays(3);

    private static QuotaAlertSample Weekly(decimal used, DateTimeOffset reset, double minutesAgo = 0) =>
        new("Claude", QuotaPaceWindow.Weekly, used, reset, Now.AddMinutes(-minutesAgo));

    private static IReadOnlyList<QuotaAlert> Evaluate(QuotaAlertSample current, QuotaAlertSample? previous, IReadOnlyCollection<string>? fired = null) =>
        QuotaAlertPolicy.Evaluate(current, previous, fired ?? [], FreshnessState.Current, true, Now);

    [Theory]
    [InlineData(0.9)]
    [InlineData(-0.9)]
    [InlineData(59)]
    public void Reset_time_jitter_is_not_a_reset(double seconds)
    {
        var previous = Weekly(9, WeeklyReset, minutesAgo: 5);
        var current = Weekly(9, WeeklyReset.AddSeconds(seconds));
        Assert.Empty(Evaluate(current, previous));
    }

    [Fact]
    public void Jitter_keeps_the_threshold_key_so_a_crossed_threshold_never_fires_again()
    {
        var first = Assert.Single(Evaluate(Weekly(76, WeeklyReset), Weekly(70, WeeklyReset, minutesAgo: 5)));
        Assert.Equal(QuotaAlertKind.ThresholdCrossed, first.Kind);
        var anchored = QuotaAlertPolicy.Anchor(Weekly(77, WeeklyReset.AddSeconds(0.8)), Weekly(76, WeeklyReset, minutesAgo: 5));
        Assert.Equal(WeeklyReset, anchored.ResetAt);
        Assert.Empty(Evaluate(Weekly(77, WeeklyReset.AddSeconds(0.8)), Weekly(76, WeeklyReset, minutesAgo: 5), [first.Key]));
    }

    [Fact]
    public void A_real_weekly_reset_still_alerts_once()
    {
        var previous = Weekly(64, WeeklyReset.AddDays(-7), minutesAgo: 5);
        var alert = Assert.Single(Evaluate(Weekly(1, WeeklyReset), previous));
        Assert.Equal(QuotaAlertKind.Reset, alert.Kind);
        Assert.Equal(WeeklyReset, QuotaAlertPolicy.Anchor(Weekly(1, WeeklyReset), previous).ResetAt);
    }

    [Fact]
    public void A_five_hour_reset_needs_at_least_half_the_window()
    {
        var reset = Now.AddHours(2);
        QuotaAlertSample Session(decimal used, DateTimeOffset at, double minutesAgo = 0) =>
            new("Claude", QuotaPaceWindow.FiveHour, used, at, Now.AddMinutes(-minutesAgo));
        Assert.DoesNotContain(Evaluate(Session(5, reset.AddMinutes(30)), Session(5, reset, 5)), alert => alert.Kind == QuotaAlertKind.Reset);
        Assert.Contains(Evaluate(Session(5, reset.AddHours(3)), Session(5, reset, 5)), alert => alert.Kind == QuotaAlertKind.Reset);
    }

    [Fact]
    public void Observed_pace_treats_jittered_resets_as_one_cycle()
    {
        var start = Now.AddHours(-2);
        var reset = start.AddHours(5);
        QuotaHistorySample At(double minutes, decimal used, double jitterSeconds) =>
            new(QuotaHistoryProvider.Claude, QuotaHistoryWindow.FiveHour, used, reset.AddSeconds(jitterSeconds),
                start.AddMinutes(minutes), QuotaHistorySource.ClaudeRemote);
        var result = ObservedPacePolicy.Calculate(40, reset.AddSeconds(0.4), QuotaPaceWindow.FiveHour, Now,
            QuotaHistoryProvider.Claude, [At(60, 10, -0.9), At(120, 40, 0.7)]);
        Assert.True(result.IsObserved);
    }
}
