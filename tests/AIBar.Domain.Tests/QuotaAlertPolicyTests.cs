using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaAlertPolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
    private static QuotaAlertSample Sample(decimal used, DateTimeOffset? reset = null, DateTimeOffset? at = null) =>
        new("Codex", QuotaPaceWindow.FiveHour, used, reset ?? Now.AddHours(2), at ?? Now);

    [Theory]
    [InlineData(74, 75, 75)]
    [InlineData(89, 90, 90)]
    [InlineData(99, 100, 100)]
    public void Exact_crossing_fires_once(int before, int after, int threshold)
    {
        var previous = Sample(before, at: Now.AddMinutes(-1));
        var current = Sample(after);
        var result = QuotaAlertPolicy.Evaluate(current, previous, [], FreshnessState.Current, true, Now);
        var alert = Assert.Single(result.Where(a => a.Kind == QuotaAlertKind.ThresholdCrossed));
        Assert.Equal(threshold, alert.Threshold);
        Assert.DoesNotContain(QuotaAlertPolicy.Evaluate(current, previous, [alert.Key], FreshnessState.Current, true, Now), a => a.Key == alert.Key);
    }

    [Fact]
    public void First_current_sample_can_report_existing_limits_but_not_a_reset()
    {
        var events = QuotaAlertPolicy.Evaluate(Sample(100), null, [], FreshnessState.Current, true, Now);
        Assert.Equal(new int?[] { 75, 90, 100 }, events.Where(a => a.Kind == QuotaAlertKind.ThresholdCrossed).Select(a => a.Threshold));
        Assert.DoesNotContain(events, a => a.Kind == QuotaAlertKind.Reset);
    }

    [Fact]
    public void Later_cycle_resets_and_rearms_thresholds_but_backward_reset_does_not()
    {
        var previous = Sample(100, Now.AddHours(-1), Now.AddHours(-2));
        var current = Sample(2);
        Assert.Contains(QuotaAlertPolicy.Evaluate(current, previous, [], FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.Reset);
        Assert.DoesNotContain(QuotaAlertPolicy.Evaluate(Sample(2, Now.AddHours(1)), Sample(90), [], FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.Reset);
        var old = QuotaAlertPolicy.Evaluate(previous, null, [], FreshnessState.Current, true, Now.AddHours(-2));
        Assert.Equal(3, QuotaAlertPolicy.Evaluate(Sample(100), previous, old.Select(a => a.Key).ToArray(), FreshnessState.Current, true, Now).Count(a => a.Kind == QuotaAlertKind.ThresholdCrossed));
    }

    [Theory]
    [InlineData(FreshnessState.Stale, true, 90)]
    [InlineData(FreshnessState.Unavailable, true, 90)]
    [InlineData(FreshnessState.Current, false, 90)]
    [InlineData(FreshnessState.Current, true, -1)]
    [InlineData(FreshnessState.Current, true, 101)]
    public void Invalid_or_unconsented_or_noncurrent_never_alerts(FreshnessState freshness, bool consent, int used) =>
        Assert.Empty(QuotaAlertPolicy.Evaluate(Sample(used), null, [], freshness, consent, Now));

    [Fact]
    public void Expired_future_retrieved_and_nonincreasing_samples_do_not_alert()
    {
        Assert.Empty(QuotaAlertPolicy.Evaluate(Sample(100, Now), null, [], FreshnessState.Current, true, Now));
        Assert.Empty(QuotaAlertPolicy.Evaluate(Sample(100, at: Now.AddSeconds(1)), null, [], FreshnessState.Current, true, Now));
        Assert.Empty(QuotaAlertPolicy.Evaluate(Sample(100), Sample(50), [], FreshnessState.Current, true, Now));
    }

    [Fact]
    public void Window_keys_are_independent_and_same_instant_offsets_are_the_same_cycle()
    {
        var sample = Sample(100);
        var fired = QuotaAlertPolicy.Evaluate(sample, null, [], FreshnessState.Current, true, Now).Select(a => a.Key).ToArray();
        Assert.Equal(3, QuotaAlertPolicy.Evaluate(sample with { Window = QuotaPaceWindow.Weekly }, null, fired, FreshnessState.Current, true, Now)
            .Count(a => a.Kind == QuotaAlertKind.ThresholdCrossed));
        Assert.Empty(QuotaAlertPolicy.Evaluate(sample with { ResetAt = sample.ResetAt.ToOffset(TimeSpan.FromHours(3)) }, null, fired, FreshnessState.Current, true, Now));
        Assert.Empty(QuotaAlertPolicy.Evaluate(sample with { Window = (QuotaPaceWindow)999 }, null, [], FreshnessState.Current, true, Now));
        Assert.Empty(QuotaAlertPolicy.Evaluate(sample with { Provider = "unknown" }, null, [], FreshnessState.Current, true, Now));
    }

    [Fact]
    public void Equal_pace_is_not_exhaustion_and_reset_detection_requires_a_later_timestamp()
    {
        Assert.DoesNotContain(QuotaAlertPolicy.Evaluate(Sample(60), null, [], FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.RunsOutBeforeReset);
        Assert.DoesNotContain(QuotaAlertPolicy.Evaluate(Sample(2), Sample(80, at: Now.AddSeconds(-1)), [], FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.Reset);
    }

    [Fact]
    public void Pace_uses_existing_policy_and_keys_isolate_provider_window_and_cycle()
    {
        var sample = Sample(70);
        var events = QuotaAlertPolicy.Evaluate(sample, null, [], FreshnessState.Current, true, Now);
        Assert.Equal(QuotaPacePolicy.Calculate(70, sample.ResetAt, sample.Window, Now).RunsOutBeforeReset,
            events.Any(a => a.Kind == QuotaAlertKind.RunsOutBeforeReset));
        var keys = events.Select(a => a.Key).ToArray();
        Assert.Contains(QuotaAlertPolicy.Evaluate(sample with { Provider = "Claude" }, null, keys, FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.RunsOutBeforeReset);
        Assert.DoesNotContain(QuotaAlertPolicy.Evaluate(Sample(0), null, [], FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.RunsOutBeforeReset);
        Assert.DoesNotContain(QuotaAlertPolicy.Evaluate(Sample(1, Now.AddHours(4.9)), null, [], FreshnessState.Current, true, Now), a => a.Kind == QuotaAlertKind.RunsOutBeforeReset);
    }
}
