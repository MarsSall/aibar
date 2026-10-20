using SystemColors = System.Windows.SystemColors;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace AIBar.Desktop;

public sealed record ConfettiPlan(bool Visible, TimeSpan Duration);
public static class ConfettiPolicy
{
    public static ConfettiPlan Plan(bool enabled, bool highContrast, bool animations) =>
        new(enabled && animations, TimeSpan.FromSeconds(2.5));
    public static int Count(double width, double height) => Math.Max(1, (int)Math.Round(120 * width * height / (1920 * 1080)));
    public static double Y(double elapsed, double initialVelocity) => -20 + initialVelocity * elapsed + 450 * elapsed * elapsed;
}

/// <summary>Analytic particle physics, one bounded 2.5 second overlay per physical monitor.</summary>
public sealed class ConfettiWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _elapsed = new();
    private ConfettiWindow(Forms.Screen screen, ConfettiPlan plan, string provider)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false; IsHitTestVisible = false;
        var particles = new ConfettiVisual(provider, ConfettiPolicy.Count(screen.Bounds.Width, screen.Bounds.Height));
        Content = particles;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            if (_elapsed.Elapsed >= plan.Duration) { Close(); return; }
            particles.Elapsed = _elapsed.Elapsed.TotalSeconds;
            particles.InvalidateVisual();
        };
        SourceInitialized += (_, _) => NotificationNative.Initialize(this, true);
        Loaded += (_, _) => { NotificationNative.PlaceOverlay(this, screen); _elapsed.Start(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _elapsed.Stop(); };
    }
    private static readonly List<ConfettiWindow> Active = [];
    public static IReadOnlyList<ConfettiWindow> ShowOnAllMonitors(bool enabled, string provider = "Codex")
    {
        var plan = ConfettiPolicy.Plan(enabled, SystemParameters.HighContrast, SystemParameters.ClientAreaAnimation);
        if (!plan.Visible || Active.Count != 0) return [];
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new ConfettiWindow(screen, plan, provider);
            Active.Add(window);
            window.Closed += (_, _) => Active.Remove(window);
            window.Show();
        }
        return Active.ToArray();
    }
}

internal sealed class ConfettiVisual : FrameworkElement
{
    private sealed record Particle(double X, double Spawn, double Vx, double Vy, double Spin, double Angle, double Width, double Height, bool Dot, int Color);
    private readonly Particle[] _particles;
    private readonly SolidColorBrush[] _colors;
    public double Elapsed { get; set; }
    public ConfettiVisual(string provider, int count)
    {
        var codex = string.Equals(provider, "Codex", StringComparison.OrdinalIgnoreCase);
        _colors = SystemParameters.HighContrast
            ? [new(SystemColors.HighlightColor), new(SystemColors.WindowTextColor)]
            : [new(codex ? Color.FromRgb(0x10, 0xA3, 0x7F) : Color.FromRgb(0xC4, 0x70, 0x3F)),
               new(codex ? Color.FromRgb(0x7B, 0xD8, 0xBE) : Color.FromRgb(0xE8, 0x95, 0x5F)),
               new(Colors.White), new(Color.FromRgb(0xF2, 0xC1, 0x4E))];
        _particles = Enumerable.Range(0, count).Select(i => new Particle(
            Random.Shared.NextDouble(), Random.Shared.NextDouble() * .3,
            Random.Shared.NextDouble() * 240 - 120, 200 + Random.Shared.NextDouble() * 220,
            (180 + Random.Shared.NextDouble() * 540) * (i % 2 == 0 ? 1 : -1), Random.Shared.NextDouble() * 360,
            6 + Random.Shared.NextDouble() * 3, 10 + Random.Shared.NextDouble() * 5, i % 5 == 0, i % _colors.Length)).ToArray();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        dc.PushOpacity(Math.Clamp((2.5 - Elapsed) / .6, 0, 1));
        foreach (var p in _particles)
        {
            var time = Elapsed - p.Spawn;
            if (time < 0) continue;
            var x = p.X * ActualWidth + p.Vx * time;
            var y = ConfettiPolicy.Y(time, p.Vy);
            dc.PushTransform(new RotateTransform(p.Angle + p.Spin * time, x, y));
            if (p.Dot) dc.DrawEllipse(_colors[p.Color], null, new Point(x, y), 3.5, 3.5);
            else dc.DrawRoundedRectangle(_colors[p.Color], null, new Rect(x - p.Width / 2, y - p.Height / 2, p.Width, p.Height), 1, 1);
            dc.Pop();
        }
        dc.Pop(); dc.Pop();
    }
}
