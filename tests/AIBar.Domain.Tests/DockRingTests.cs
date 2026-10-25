using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class DockRingTests
{
    [Fact]
    public void Hover_card_is_a_tooltip_wpf_can_open_without_crashing()
    {
        // WPF's tooltip service throws NotSupportedException (and AIBar exits) when it opens a ToolTip with StaysOpen = false.
        WpfTestApplicationHost.Run(_ =>
        {
            var tooltip = Assert.IsType<System.Windows.Controls.ToolTip>(new DockRing().ToolTip);
            Assert.True(tooltip.StaysOpen);
        });
    }

    [Fact]
    public void Zero_missing_partial_and_full_circle_geometry_are_observable()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            Assert.True(DockRing.CreateArc(0, 23).IsEmpty());
            Assert.True(DockRing.CreateArc(null, 23).IsEmpty());
            var partial = PathGeometry.CreateFromGeometry(DockRing.CreateArc(37, 23));
            partial.GetPointAtFractionLength(0, out var start, out var tangent);
            partial.GetPointAtFractionLength(1, out var end, out tangent);
            Assert.Equal(28, start.X, 3); Assert.Equal(5, start.Y, 3);
            Assert.Equal(28 + 23 * Math.Sin(.74 * Math.PI), end.X, 3);
            Assert.Equal(28 - 23 * Math.Cos(.74 * Math.PI), end.Y, 3);
            Assert.True(tangent.X < 0); // Clockwise, now past three o'clock.
            var full = DockRing.CreateArc(100, 23);
            Assert.False(full.IsEmpty());
            Assert.Equal(new Rect(5, 5, 46, 46), full.Bounds);
            Assert.Equal(full.Bounds, DockRing.CreateArc(150, 23).Bounds);
            Assert.True(DockRing.CreateArc(-1, 23).IsEmpty());
        });
    }

    [Fact]
    public void Rings_use_outer_primary_inner_weekly_and_safe_hover_copy()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var ring = new DockRing();
            var state = Reading();
            ring.Update("Codex", state);
            Assert.False(ring.OuterArc.IsEmpty()); Assert.False(ring.InnerArc.IsEmpty());
            Assert.True(ring.OuterArc.Bounds.Width > ring.InnerArc.Bounds.Width);
            Assert.Contains("Codex · Pro", ring.Details);
            Assert.Contains("5h  37% · resets in 2h 10m", ring.Details);
            Assert.Contains("Weekly  12% · resets in 3d 4h", ring.Details);
            Assert.Equal("Codex: 5 hour 37 percent used, weekly 12 percent used", AutomationProperties.GetName(ring));
            ring.Update("Codex", state with { Primary = state.Primary with { PercentageUsed = null } });
            Assert.True(ring.OuterArc.IsEmpty()); Assert.False(ring.InnerArc.IsEmpty());
            ring.Update("Claude", state with { IsUnavailable = true }, "Max");
            Assert.True(ring.OuterArc.IsEmpty()); Assert.True(ring.InnerArc.IsEmpty());
            Assert.Contains("Claude · Max", ring.Details);
            ring.Update("Codex", state with { IsPrivateIntegrationDisabled = true });
            Assert.True(ring.OuterArc.IsEmpty()); Assert.True(ring.InnerArc.IsEmpty());
            ring.Update("AIBar", null);
            Assert.Contains("No providers enabled", ring.Details);
        });
    }

    [Fact]
    public void Stale_readings_keep_arcs_but_reduce_opacity_and_show_state()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var ring = new DockRing();
            ring.Update("Claude", Reading() with { IsCurrent = false, FreshnessLabel = "Stale", WarningLabel = "Network unavailable" });
            ring.Measure(new Size(56, 56)); ring.Arrange(new Rect(0, 0, 56, 56)); ring.UpdateLayout();
            var paths = Descendants<System.Windows.Shapes.Path>(ring).ToArray();
            var outer = Assert.Single(paths.Where(p => ReferenceEquals(p.Data, ring.OuterArc)));
            Assert.InRange(outer.Opacity, .01, .99);
            Assert.False(ring.OuterArc.IsEmpty()); Assert.Contains("Stale · Network unavailable", ring.Details);
        });
    }

    internal static BetaPresentationState Reading() => new QuotaPresentationMapper(new Clock()).Map(
        new(null, FreshnessState.Unavailable, false, null, null)) with
    {
        Primary = new("5-hour quota", 37, "02h 10m"), Weekly = new("Weekly quota", 12, "3d 04h 00m"),
        IsPrimaryAvailable = true, IsWeeklyAvailable = true, IsCurrent = true, IsUnavailable = false,
        IsFresh = true, FreshnessLabel = "Current", PlanLabel = "Pro"
    };
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
}
