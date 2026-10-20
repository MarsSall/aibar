using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ObservedPacePresentationTests
{
    [Fact]
    public void Observed_pace_changes_only_pace_and_preserves_labels_and_noncurrent_guard()
    {
        var now = new DateTimeOffset(2030, 1, 1, 2, 0, 0, TimeSpan.Zero); var reset = now.AddHours(3);
        QuotaHistorySample[] history = [new(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, 10, reset, now.AddHours(-1), QuotaHistorySource.CodexLocal),
            new(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, 40, reset, now, QuotaHistorySource.CodexLocal)];
        var mapper = new QuotaPresentationMapper(new Clock(now));
        var state = new QuotaRefreshState(new(new(40, reset), null, now), FreshnessState.Current, false, null, null);
        var snapshot = mapper.Map(state); var observed = mapper.Map(state, history);
        Assert.Equal(snapshot with { Primary = observed.Primary }, observed);
        Assert.Equal(snapshot.Primary with { Pace = observed.Primary.Pace }, observed.Primary);
        Assert.Equal("Pace: Ahead (+50%) · Runs out in 2h 0m", observed.Primary.Pace!.Text);
        Assert.Equal("Codex 5-hour", observed.Primary.Pace.Label);
        Assert.Contains("observed use", observed.Primary.Pace.HelpText);
        Assert.Equal(PanelTone.Near, observed.Primary.Pace.Tone);
        Assert.Equal("At this pace, Codex 5-hour runs out before it resets.", observed.Primary.Pace.Notice);
        var stale = mapper.Map(state with { Freshness = FreshnessState.Stale }, history);
        Assert.Equal("Pace: Not current", stale.Primary.Pace!.Text); Assert.Null(stale.Primary.Pace.Notice);
        Assert.DoesNotContain("observed use", stale.Primary.Pace.HelpText);
    }
    private sealed record Clock(DateTimeOffset UtcNow) : IClock;
}
