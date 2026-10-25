using System.Windows;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class DockPlacementTests
{
    private static readonly Rect Area = new(-1000, 40, 1000, 800);
    [Theory]
    [InlineData("top", -600, 40, 200, 60)]
    [InlineData("bottom", -600, 780, 200, 60)]
    [InlineData("left", -1000, 340, 60, 200)]
    [InlineData("right", -60, 340, 60, 200)]
    public void Flush_edges_and_vertical_size_swap(string edge, double x, double y, double width, double height)
    {
        Assert.Equal(new Rect(x, y, width, height), DockPlacement.Place(Area, edge, .5, new Size(200, 60)));
    }

    [Theory]
    [InlineData("top")]
    [InlineData("bottom")]
    [InlineData("left")]
    [InlineData("right")]
    public void Clamps_endpoints_and_oversized_strips_inside_work_area(string edge)
    {
        foreach (var offset in new[] { -10d, 0, .4, 1, 10 })
        {
            Assert.True(Area.Contains(DockPlacement.Place(Area, edge, offset, new Size(200, 60))));
            Assert.Equal(Area, DockPlacement.Place(Area, edge, offset, new Size(2000, 2000)));
        }
        var start = DockPlacement.Place(Area, edge, 0, new Size(200, 60));
        var end = DockPlacement.Place(Area, edge, 1, new Size(200, 60));
        if (edge is "top" or "bottom") { Assert.Equal(Area.Left, start.Left); Assert.Equal(Area.Right, end.Right); }
        else { Assert.Equal(Area.Top, start.Top); Assert.Equal(Area.Bottom, end.Bottom); }
    }

    [Theory]
    [InlineData(-750, 41, "top", .25)]
    [InlineData(-750, 839, "bottom", .25)]
    [InlineData(-999, 240, "left", .25)]
    [InlineData(-1, 640, "right", .75)]
    [InlineData(-2000, 240, "left", .25)]
    [InlineData(1000, 640, "right", .75)]
    public void Snap_picks_nearest_edge_and_normalizes_offset(double x, double y, string edge, double offset)
    {
        Assert.Equal(new DockSnap(edge, offset), DockPlacement.Snap(Area, new Point(x, y)));
    }

    [Fact]
    public void Snap_outside_corner_clamps_offset_and_ties_are_deterministic()
    {
        Assert.Equal(new DockSnap("top", 0), DockPlacement.Snap(Area, new Point(-2000, -2000)));
        Assert.Equal(new DockSnap("bottom", 1), DockPlacement.Snap(Area, new Point(2000, 2000)));
        Assert.Throws<ArgumentException>(() => DockPlacement.Snap(Rect.Empty, new Point()));
        Assert.Throws<ArgumentException>(() => DockPlacement.Place(Area, "unknown", .5, new Size(20, 20)));
        Assert.Throws<ArgumentException>(() => DockPlacement.Place(Area, "top", double.NaN, new Size(20, 20)));
    }
}
