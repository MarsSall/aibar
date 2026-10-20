using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Media = System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace AIBar.Desktop;

public sealed record ConfettiPlan(bool Visible, TimeSpan Duration);
public static class ConfettiPolicy
{
    public static ConfettiPlan Plan(bool enabled, bool highContrast, bool animations) =>
        new(enabled && !highContrast && animations, TimeSpan.FromSeconds(2.5));
}

/// <summary>A bounded particle overlay per physical monitor; never owns keyboard or pointer input.</summary>
public sealed class ConfettiWindow : Window
{
    private readonly DispatcherTimer _timer;
    private ConfettiWindow(Forms.Screen screen, ConfettiPlan plan)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Media.Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false; IsHitTestVisible = false;
        var canvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
        Content = canvas;
        _timer = new DispatcherTimer { Interval = plan.Duration };
        _timer.Tick += CloseOnTick;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x20 | 0x80 | 0x08000000);
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
        Loaded += (_, _) =>
        {
            var bounds = screen.Bounds;
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x10 | 0x40);
            // Native positioning is pixels; particles live in WPF's per-monitor device-independent coordinates.
            UpdateLayout();
            var colors = new[] { "#2FBF98", "#E0876A", "#FFC531", "#60CDFF", "#DFA4F6" };
            for (var i = 0; i < 72; i++)
            {
                var particle = new Rectangle
                {
                    Width = 5 + Random.Shared.Next(5), Height = 4 + Random.Shared.Next(8),
                    Fill = new Media.SolidColorBrush((Media.Color)Media.ColorConverter.ConvertFromString(colors[i % colors.Length])),
                    RenderTransform = new Media.RotateTransform(Random.Shared.Next(360)), RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
                };
                var x = Random.Shared.NextDouble() * ActualWidth;
                Canvas.SetLeft(particle, x); Canvas.SetTop(particle, -20);
                canvas.Children.Add(particle);
                var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(350));
                var duration = new Duration(plan.Duration - delay);
                particle.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-20, ActualHeight + 20, duration) { BeginTime = delay });
                particle.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, x + Random.Shared.Next(-100, 101), duration) { BeginTime = delay });
                particle.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration) { BeginTime = delay });
            }
            _timer.Start();
        };
        Closed += (_, _) => { _timer.Stop(); _timer.Tick -= CloseOnTick; };
    }
    private void CloseOnTick(object? sender, EventArgs e) => Close();
    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x84) { handled = true; return new IntPtr(-1); }
        if (message == 0x21) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }
    private static readonly List<ConfettiWindow> Active = [];
    public static IReadOnlyList<ConfettiWindow> ShowOnAllMonitors(bool enabled)
    {
        var plan = ConfettiPolicy.Plan(enabled, SystemParameters.HighContrast, SystemParameters.ClientAreaAnimation);
        if (!plan.Visible || Active.Count != 0) return []; // simultaneous window resets share one bounded effect
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new ConfettiWindow(screen, plan);
            Active.Add(window);
            window.Closed += (_, _) => Active.Remove(window);
            window.Show();
        }
        return Active.ToArray();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
