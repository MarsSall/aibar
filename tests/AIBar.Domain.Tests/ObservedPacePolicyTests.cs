using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ObservedPacePolicyTests
{
    private static readonly DateTimeOffset Start = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = Start.AddHours(5);
    private static QuotaHistorySample Sample(double minutes, decimal used, DateTimeOffset? reset = null,
        QuotaHistoryProvider provider = QuotaHistoryProvider.Codex, QuotaHistoryWindow window = QuotaHistoryWindow.FiveHour) =>
        new(provider, window, used, reset ?? Reset, Start.AddMinutes(minutes),
            provider == QuotaHistoryProvider.Codex ? QuotaHistorySource.CodexLocal : QuotaHistorySource.ClaudeRemote);

    [Fact]
    public void Exactly_two_samples_use_observed_rate_ratio_and_exhaustion()
    {
        var now = Start.AddHours(2);
        var result = ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, now,
            QuotaHistoryProvider.Codex, [Sample(60, 10), Sample(120, 40)]);
        Assert.True(result.IsObserved);
        Assert.Equal(QuotaPaceState.Available, result.State);
        Assert.Equal(0.4m, result.ElapsedFraction);
        Assert.Equal(40m, result.ExpectedPercentage);
        Assert.Equal(1.5m, result.PaceRatio);
        Assert.Equal(now.AddHours(2), result.RunsOutAt);
        Assert.True(result.RunsOutBeforeReset);
        Assert.False(result.IsLimitReached);
    }

    [Theory]
    [InlineData(14.99, false)]
    [InlineData(15, false)]
    [InlineData(15.01, true)]
    public void Observed_requires_more_than_five_percent_elapsed(double minutes, bool observed)
    {
        var now = Start.AddMinutes(minutes);
        var history = new[] { Sample(0, 0), Sample(minutes, 10) };
        var result = ObservedPacePolicy.Calculate(10, Reset, QuotaPaceWindow.FiveHour, now, QuotaHistoryProvider.Codex, history);
        Assert.Equal(observed, result.IsObserved);
        if (!observed) Assert.Equal(QuotaPacePolicy.Calculate(10, Reset, QuotaPaceWindow.FiveHour, now), result);
    }

    [Fact]
    public void Empty_single_duplicate_or_wrong_cycle_history_falls_back()
    {
        QuotaHistorySample[][] cases = [[], [Sample(60, 10)], [Sample(60, 10), Sample(60, 10)],
            [Sample(60, 10, Reset.AddHours(-5)), Sample(120, 40)],
            [Sample(60, 10, provider: QuotaHistoryProvider.Claude), Sample(120, 40)],
            [Sample(60, 10, window: QuotaHistoryWindow.Weekly), Sample(120, 40)],
            [Sample(-1, 10), Sample(120, 40)], [Sample(60, 10), Sample(121, 40)],
            [Sample(60, 40), Sample(120, 10)]];
        foreach (var history in cases)
            Assert.Equal(QuotaPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, Start.AddHours(2)),
                ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, Start.AddHours(2), QuotaHistoryProvider.Codex, history));
    }

    [Fact]
    public void Flat_rate_lasts_until_reset_and_limit_and_expiry_keep_snapshot_semantics()
    {
        var history = new[] { Sample(60, 40), Sample(120, 40) };
        var flat = ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, Start.AddHours(2), QuotaHistoryProvider.Codex, history);
        Assert.True(flat.IsObserved); Assert.Equal(0m, flat.PaceRatio);
        Assert.Null(flat.RunsOutAt); Assert.False(flat.RunsOutBeforeReset);
        foreach (var used in new decimal[] { 100, 105 })
            Assert.Equal(QuotaPacePolicy.Calculate(used, Reset, QuotaPaceWindow.FiveHour, Start.AddHours(2)),
                ObservedPacePolicy.Calculate(used, Reset, QuotaPaceWindow.FiveHour, Start.AddHours(2), QuotaHistoryProvider.Codex, history));
        Assert.Equal(QuotaPaceState.Unavailable, ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour,
            Reset.AddTicks(1), QuotaHistoryProvider.Codex, history).State);
    }

    [Fact]
    public void Even_rate_projects_exactly_at_reset_and_history_is_order_independent()
    {
        var now = Start.AddHours(2);
        var history = new[] { Sample(120, 40), Sample(60, 20), Sample(60, 20) };
        var result = ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, now, QuotaHistoryProvider.Codex, history);
        Assert.True(result.IsObserved); Assert.Equal(1m, result.PaceRatio);
        Assert.Equal(Reset, result.RunsOutAt); Assert.False(result.RunsOutBeforeReset);
        var atReset = ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, Reset, QuotaHistoryProvider.Codex, history);
        Assert.True(atReset.IsObserved); Assert.False(atReset.RunsOutBeforeReset);
        Assert.Equal(QuotaPaceState.Available, atReset.State);
        Assert.Equal(QuotaPacePolicy.Calculate(30, Reset, QuotaPaceWindow.FiveHour, now),
            ObservedPacePolicy.Calculate(30, Reset, QuotaPaceWindow.FiveHour, now, QuotaHistoryProvider.Codex, history));
    }

    [Fact]
    public void Missing_inputs_and_unrepresentable_projection_are_safe()
    {
        Assert.Equal(QuotaPaceState.Unavailable, ObservedPacePolicy.Calculate(null, Reset, QuotaPaceWindow.FiveHour,
            Start.AddHours(2), QuotaHistoryProvider.Codex, []).State);
        Assert.Equal(QuotaPaceState.Unavailable, ObservedPacePolicy.Calculate(40, null, QuotaPaceWindow.FiveHour,
            Start.AddHours(2), QuotaHistoryProvider.Codex, []).State);
        var history = new[] { Sample(60, 0), Sample(120, 0.000000000000000000000001m) };
        var slow = ObservedPacePolicy.Calculate(40, Reset, QuotaPaceWindow.FiveHour, Start.AddHours(2), QuotaHistoryProvider.Codex, history);
        Assert.True(slow.IsObserved); Assert.Null(slow.RunsOutAt); Assert.False(slow.RunsOutBeforeReset);
    }

    [Fact]
    public void Weekly_Claude_uses_its_own_cycle_and_even_rate_boundary()
    {
        var reset = Start.AddDays(7); var now = Start.AddDays(2);
        QuotaHistorySample[] history = [new(QuotaHistoryProvider.Claude, QuotaHistoryWindow.Weekly, 10, reset, Start.AddDays(1), QuotaHistorySource.ClaudeRemote),
            new(QuotaHistoryProvider.Claude, QuotaHistoryWindow.Weekly, 10 + 100m / 7, reset, now, QuotaHistorySource.ClaudeRemote)];
        var result = ObservedPacePolicy.Calculate(10 + 100m / 7, reset, QuotaPaceWindow.Weekly, now, QuotaHistoryProvider.Claude, history);
        Assert.True(result.IsObserved); Assert.Equal(1m, result.PaceRatio!.Value, 10);
        Assert.False(result.RunsOutBeforeReset);
    }
}
