using System.Windows;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace AIBar.Desktop;

internal static class PanelDetailsPlacement
{
    internal const double Gap = 6;

    internal static Point Place(Rect panel, Rect workArea, Size details, double costTop)
    {
        var left = panel.Left - Gap - details.Width;
        if (left < workArea.Left) left = panel.Right + Gap;
        return new Point(Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - details.Width)),
            Math.Clamp(costTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - details.Height)));
    }
}
