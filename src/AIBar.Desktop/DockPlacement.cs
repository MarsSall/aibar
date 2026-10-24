using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace AIBar.Desktop;

public readonly record struct DockSnap(string Edge, double Offset);

/// <summary>Pure DIP placement. Offset locates the strip centre along an edge; endpoints are clamped inside the work area.</summary>
public static class DockPlacement
{
    public static Rect Place(Rect workArea, string edge, double offset, Size horizontalSize)
    {
        Validate(workArea);
        if (edge is not ("top" or "bottom" or "left" or "right")) throw new ArgumentException("Unknown edge.", nameof(edge));
        if (!double.IsFinite(offset) || !double.IsFinite(horizontalSize.Width) || !double.IsFinite(horizontalSize.Height))
            throw new ArgumentException("Placement requires finite dimensions and offset.");
        var vertical = edge is "left" or "right";
        var width = Math.Min(workArea.Width, vertical ? horizontalSize.Height : horizontalSize.Width);
        var height = Math.Min(workArea.Height, vertical ? horizontalSize.Width : horizontalSize.Height);
        var x = Math.Clamp(workArea.Left + Math.Clamp(offset, 0, 1) * workArea.Width - width / 2, workArea.Left, workArea.Right - width);
        var y = Math.Clamp(workArea.Top + Math.Clamp(offset, 0, 1) * workArea.Height - height / 2, workArea.Top, workArea.Bottom - height);
        return edge switch
        {
            "top" => new(x, workArea.Top, width, height),
            "bottom" => new(x, workArea.Bottom - height, width, height),
            "left" => new(workArea.Left, y, width, height),
            _ => new(workArea.Right - width, y, width, height)
        };
    }

    public static DockSnap Snap(Rect workArea, Point point)
    {
        Validate(workArea);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Point must be finite.", nameof(point));
        var x = Math.Clamp(point.X, workArea.Left, workArea.Right);
        var y = Math.Clamp(point.Y, workArea.Top, workArea.Bottom);
        var nearest = new[] { ("top", y - workArea.Top), ("bottom", workArea.Bottom - y),
            ("left", x - workArea.Left), ("right", workArea.Right - x) }.MinBy(item => item.Item2);
        return new(nearest.Item1, nearest.Item1 is "top" or "bottom" ? (x - workArea.Left) / workArea.Width : (y - workArea.Top) / workArea.Height);
    }

    private static void Validate(Rect area)
    {
        if (area.IsEmpty || !double.IsFinite(area.X) || !double.IsFinite(area.Y) ||
            !double.IsFinite(area.Width) || !double.IsFinite(area.Height) || area.Width <= 0 || area.Height <= 0)
            throw new ArgumentException("A finite, positive work area is required.", nameof(area));
    }
}
