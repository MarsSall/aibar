using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class NotificationPolicyTests
{
    [Theory]
    [InlineData(800, 360)]
    [InlineData(340, 300)]
    public void Cards_fit_work_area_and_keep_twelve_pixel_inset(double width, double expected)
    {
        var positions = NotificationLayout.Place(new System.Windows.Rect(100, 50, width, 700), new double[] { 86, 104, 86 });
        Assert.Equal(3, positions.Count);
        Assert.All(positions, p => Assert.Equal(expected, p.Width));
        Assert.Equal(100 + width - 12, positions[0].Right);
        Assert.Equal(738, positions[0].Bottom);
        Assert.Equal(positions[0].Top - 8, positions[1].Bottom);
        Assert.Equal(positions[1].Top - 8, positions[2].Bottom);
    }

    [Fact]
    public void Dwell_pauses_for_hover_or_focus_and_resumes_only_when_both_leave()
    {
        var dwell = new NotificationDwell();
        dwell.Advance(TimeSpan.FromSeconds(2), false, false);
        dwell.Advance(TimeSpan.FromSeconds(10), true, false);
        dwell.Advance(TimeSpan.FromSeconds(10), false, true);
        Assert.Equal(TimeSpan.FromSeconds(4), dwell.Remaining);
        Assert.False(dwell.Expired);
        dwell.Advance(TimeSpan.FromSeconds(4), false, false);
        Assert.True(dwell.Expired);
        Assert.Equal(0, dwell.Fraction);
    }

    [Theory]
    [InlineData(QuotaAlertKind.Reset, "RESET", "✓")]
    [InlineData(QuotaAlertKind.ThresholdCrossed, "THRESHOLD", "△")]
    [InlineData(QuotaAlertKind.RunsOutBeforeReset, "PACE", "◷")]
    public void Category_is_text_and_icon_not_color_alone(QuotaAlertKind kind, string label, string icon)
    {
        Assert.Equal(label, NotificationCategory.For(kind).Label);
        Assert.Equal(icon, NotificationCategory.For(kind).Icon);
    }

    [Fact]
    public void Stack_is_bounded_and_effect_geometry_is_scaled()
    {
        Assert.Equal(3, NotificationLayout.MaximumVisible);
        Assert.Equal(300, NotificationLayout.Place(new System.Windows.Rect(0, 0, 1920, 1080), new double[] { 86 }, narrow: true)[0].Width);
        Assert.Equal(64, EdgeGlowPolicy.Thickness(1920, 1080));
        Assert.Equal(108, EdgeGlowPolicy.Thickness(3840, 2160));
        Assert.Equal(128, EdgeGlowPolicy.Thickness(7680, 4320));
        Assert.Equal(120, ConfettiPolicy.Count(1920, 1080));
        Assert.Equal(480, ConfettiPolicy.Count(3840, 2160));
        Assert.Equal(-20, ConfettiPolicy.Y(0, 300));
        Assert.Equal(730, ConfettiPolicy.Y(1, 300));
    }
}
