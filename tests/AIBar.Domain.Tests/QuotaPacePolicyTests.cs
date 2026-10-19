using AIBar.Domain;
using AIBar.Desktop;
using System.Globalization;

namespace AIBar.Domain.Tests;

public sealed class QuotaPacePolicyTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(QuotaPaceWindow.FiveHour, 5)]
    [InlineData(QuotaPaceWindow.Weekly, 168)]
    public void Even_use_and_exhaustion_use_the_full_window(QuotaPaceWindow window, int hours)
    {
        var reset = Now.AddHours(hours / 2d);
        var even = QuotaPacePolicy.Calculate(50, reset, window, Now);
        Assert.Equal(QuotaPaceState.Available, even.State);
        Assert.Equal(0.5m, even.ElapsedFraction);
        Assert.Equal(50m, even.ExpectedPercentage);
        Assert.Equal(1m, even.PaceRatio);
        Assert.Null(even.RunsOutAt);
        Assert.False(even.RunsOutBeforeReset);
        var fast = QuotaPacePolicy.Calculate(80, reset, window, Now);
        Assert.Equal(1.6m, fast.PaceRatio);
        Assert.Equal(Now.AddHours(hours / 8d), fast.RunsOutAt);
        Assert.True(fast.RunsOutBeforeReset);
    }

    [Theory]
    [InlineData(QuotaPaceWindow.FiveHour, 5)]
    [InlineData(QuotaPaceWindow.Weekly, 168)]
    public void Five_percent_is_inclusive_and_earlier_is_insufficient(QuotaPaceWindow window, int hours)
    {
        var reset = Now.AddTicks(TimeSpan.FromHours(hours).Ticks * 95 / 100);
        Assert.Equal(QuotaPaceState.Available, QuotaPacePolicy.Calculate(1, reset, window, Now).State);
        Assert.Equal(QuotaPaceState.NotEnoughData, QuotaPacePolicy.Calculate(1, reset.AddTicks(1), window, Now).State);
        var future = QuotaPacePolicy.Calculate(1, Now.AddHours(hours + 1), window, Now);
        Assert.Equal(0m, future.ElapsedFraction);
        Assert.Equal(QuotaPaceState.NotEnoughData, future.State);
    }

    [Fact]
    public void Missing_inputs_and_past_reset_are_unavailable()
    {
        Assert.Equal(QuotaPaceState.Unavailable, Calc(null, Now.AddHours(1)).State);
        Assert.Equal(QuotaPaceState.Unavailable, Calc(1, null).State);
        Assert.Equal(QuotaPaceState.Unavailable, Calc(100, Now.AddTicks(-1)).State);
        Assert.Equal(1m, Calc(1, Now).ElapsedFraction);
        Assert.Equal(0m, Calc(0, Now.AddHours(2)).PaceRatio);
        Assert.Null(Calc(0, Now.AddHours(2)).RunsOutAt);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(110)]
    public void Limit_reached_overrides_early_window(int used)
    {
        var result = Calc(used, Now.AddHours(5));
        Assert.Equal(QuotaPaceState.Available, result.State);
        Assert.True(result.IsLimitReached);
        Assert.Equal(Now, result.RunsOutAt);
        Assert.True(result.RunsOutBeforeReset);
    }

    [Fact]
    public void Exact_labels_are_invariant_and_durations_are_compact()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Pace: Behind (−30%) · Lasts to reset", PanelPacePresentation.Create("Codex weekly", 35, Calc(35, Now.AddHours(2.5)), true, Now).Text);
            Assert.Equal("Pace: Ahead (+40%) · Runs out in 1h 10m", PanelPacePresentation.Create("Codex 5-hour", 70, Calc(70, Now.AddHours(2.5)) with { RunsOutAt = Now.AddMinutes(70) }, true, Now).Text);
            Assert.Equal("Pace: Limit reached", PanelPacePresentation.Create("Codex weekly", 100, Calc(100, Now.AddHours(2)), true, Now).Text);
            Assert.Equal("Pace: Not enough data yet", PanelPacePresentation.Create("Codex weekly", 1, Calc(1, Now.AddHours(5)), true, Now).Text);
            Assert.Equal("", PanelPacePresentation.Create("Codex weekly", null, Calc(null, null), true, Now).Text);
            Assert.Equal("Pace: Not current", PanelPacePresentation.Create("Codex weekly", 70, Calc(70, Now.AddHours(2.5)), false, Now).Text);
            Assert.Equal("1d 8h", PanelPacePresentation.Duration(TimeSpan.FromHours(32)));
            Assert.Equal("3h 53m", PanelPacePresentation.Duration(TimeSpan.FromMinutes(233)));
            Assert.Equal("12m", PanelPacePresentation.Duration(TimeSpan.FromMinutes(12)));
            Assert.Equal("<1m", PanelPacePresentation.Duration(TimeSpan.FromSeconds(59)));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void Display_rounding_does_not_change_exhaustion_decisions()
    {
        var result = Calc(52, Now.AddHours(2.5));
        Assert.Equal(1.04m, result.PaceRatio);
        Assert.True(result.RunsOutBeforeReset);
        var display = PanelPacePresentation.Create("Codex 5-hour", 52, result, true, Now);
        Assert.StartsWith("Pace: On track · Runs out in ", display.Text);
        Assert.Equal("At this pace, Codex 5-hour runs out before it resets.", display.Notice);
        Assert.Equal(PanelTone.Near, display.Tone);
        Assert.Null(PanelPacePresentation.Create("Codex 5-hour", 52, result, false, Now).Notice);
        Assert.False(PanelPacePresentation.Create("Codex 5-hour", null, Calc(null, null), true, Now).IsVisible);
    }

    private static QuotaPaceResult Calc(decimal? used, DateTimeOffset? reset) =>
        QuotaPacePolicy.Calculate(used, reset, QuotaPaceWindow.FiveHour, Now);
}
