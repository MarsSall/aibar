using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

public sealed record EdgeGlowPlan(bool Visible, bool Animate, TimeSpan Duration);
public static class EdgeGlowPolicy
{
    public static EdgeGlowPlan Plan(bool enabled, bool highContrast, bool animations) =>
        new(enabled && !highContrast, enabled && !highContrast && animations, TimeSpan.FromSeconds(3));
}

/// <summary>One non-interactive layered overlay per monitor. Native placement uses physical pixels.</summary>
public sealed class EdgeGlowWindow : Window
{
    private readonly DispatcherTimer _closeTimer;
    private EdgeGlowWindow(Forms.Screen screen, EdgeGlowPlan plan)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false; IsHitTestVisible = false;
        Content = new Border
        {
            BorderBrush = System.Windows.Media.Brushes.Red, BorderThickness = new Thickness(4), Margin = new Thickness(6),
            Effect = new DropShadowEffect { Color = System.Windows.Media.Colors.Red, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 }
        };
        _closeTimer = new DispatcherTimer { Interval = plan.Duration };
        _closeTimer.Tick += CloseOnTick;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var styles = GetWindowLong(handle, -20);
            SetWindowLong(handle, -20, styles | 0x20 | 0x80 | 0x08000000); // transparent, tool window, no activate
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
        Loaded += (_, _) =>
        {
            var bounds = screen.Bounds;
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y,
                bounds.Width, bounds.Height, 0x10 | 0x40); // no activate, show
            if (plan.Animate) BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, new Duration(plan.Duration)));
            _closeTimer.Start();
        };
        Closed += (_, _) => { _closeTimer.Stop(); _closeTimer.Tick -= CloseOnTick; };
    }
    private void CloseOnTick(object? sender, EventArgs e) => Close();
    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x84) { handled = true; return new IntPtr(-1); } // HTTRANSPARENT
        if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }
    public static IReadOnlyList<EdgeGlowWindow> ShowOnAllMonitors(bool enabled)
    {
        var plan = EdgeGlowPolicy.Plan(enabled, SystemParameters.HighContrast, SystemParameters.ClientAreaAnimation);
        if (!plan.Visible) return [];
        var windows = new List<EdgeGlowWindow>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new EdgeGlowWindow(screen, plan);
            windows.Add(window); window.Show();
        }
        return windows;
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
