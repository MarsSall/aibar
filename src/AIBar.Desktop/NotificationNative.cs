using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

internal static class NotificationNative
{
    internal static void Initialize(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x80000 | 0x80 | 0x08000000 | (clickThrough ? 0x20 : 0));
        HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            if (message == 0x21) { handled = true; return new IntPtr(3); }
            if (clickThrough && message == 0x84) { handled = true; return new IntPtr(-1); }
            return IntPtr.Zero;
        });
    }
    internal static Vector MonitorScale(Forms.Screen screen)
    {
        try
        {
            var monitor = MonitorFromPoint(new NativePoint { X = screen.Bounds.X + 1, Y = screen.Bounds.Y + 1 }, 2);
            if (GetDpiForMonitor(monitor, 0, out var x, out var y) == 0) return new Vector(x / 96d, y / 96d);
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return new Vector(1, 1);
    }
    internal static void PlaceCard(Window window, double x, double y)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x1 | 0x10);
    }
    internal static void PlaceOverlay(Window window, Forms.Screen screen)
    {
        var b = screen.Bounds;
        SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), b.X, b.Y, b.Width, b.Height, 0x10 | 0x40);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
