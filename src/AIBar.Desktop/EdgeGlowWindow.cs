using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace AIBar.Desktop;

public sealed record EdgeGlowPlan(bool Visible, bool Animate, TimeSpan Duration);
public static class EdgeGlowPolicy
{
    public static EdgeGlowPlan Plan(bool enabled, bool highContrast, bool animations) =>
        new(enabled, enabled && animations, TimeSpan.FromMilliseconds(animations ? 3000 : 1750));
    public static double Thickness(double width, double height) => Math.Clamp(Math.Min(width, height) * .05, 64, 128);
}

/// <summary>One layered, click-through, no-activate overlay per monitor, including system-color HC.</summary>
public sealed class EdgeGlowWindow : Window
{
    private readonly DispatcherTimer _closeTimer;
    private EdgeGlowWindow(Forms.Screen screen, EdgeGlowPlan plan)
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; Focusable = false; IsHitTestVisible = false;
        Content = new EdgeGlowVisual();
        _closeTimer = new DispatcherTimer { Interval = plan.Duration };
        _closeTimer.Tick += CloseOnTick;
        SourceInitialized += (_, _) => NotificationNative.Initialize(this, true);
        Loaded += (_, _) =>
        {
            NotificationNative.PlaceOverlay(this, screen);
            var animation = new DoubleAnimationUsingKeyFrames();
            if (plan.Animate)
            {
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1000))));
                animation.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(plan.Duration), new KeySpline(.4, 0, 1, 1)));
            }
            else
            {
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(plan.Duration)));
            }
            BeginAnimation(OpacityProperty, animation);
            _closeTimer.Start();
        };
        Closed += (_, _) => { _closeTimer.Stop(); _closeTimer.Tick -= CloseOnTick; };
    }
    private void CloseOnTick(object? sender, EventArgs e) => Close();
    private static readonly List<EdgeGlowWindow> Active = [];
    public static IReadOnlyList<EdgeGlowWindow> ShowOnAllMonitors(bool enabled)
    {
        var plan = EdgeGlowPolicy.Plan(enabled, SystemParameters.HighContrast, SystemParameters.ClientAreaAnimation);
        if (!plan.Visible || Active.Count != 0) return [];
        foreach (var screen in Forms.Screen.AllScreens)
        {
            var window = new EdgeGlowWindow(screen, plan);
            Active.Add(window);
            window.Closed += (_, _) => Active.Remove(window);
            window.Show();
        }
        return Active.ToArray();
    }
}

internal sealed class EdgeGlowVisual : FrameworkElement
{
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = ActualWidth; var height = ActualHeight;
        if (width <= 0 || height <= 0) return;
        var thickness = EdgeGlowPolicy.Thickness(width, height);
        var color = SystemParameters.HighContrast ? SystemColors.HighlightColor : Color.FromRgb(0xE8, 0x95, 0x5F);
        Brush Gradient(Point start, Point end) => new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(153, color.R, color.G, color.B), 0),
            new(Color.FromArgb(46, color.R, color.G, color.B), .4),
            new(Color.FromArgb(0, color.R, color.G, color.B), 1)
        }, start, end);
        dc.DrawRectangle(Gradient(new Point(0, 0), new Point(0, 1)), null, new Rect(0, 0, width, thickness));
        dc.DrawRectangle(Gradient(new Point(0, 1), new Point(0, 0)), null, new Rect(0, height - thickness, width, thickness));
        dc.DrawRectangle(Gradient(new Point(0, 0), new Point(1, 0)), null, new Rect(0, 0, thickness, height));
        dc.DrawRectangle(Gradient(new Point(1, 0), new Point(0, 0)), null, new Rect(width - thickness, 0, thickness, height));
        // The warm source has HSL lightness above .5; raise L by .20 while preserving hue/saturation.
        var lightness = (Math.Max(color.R, Math.Max(color.G, color.B)) + Math.Min(color.R, Math.Min(color.G, color.B))) / 510d;
        var tintScale = (1 - Math.Min(1, lightness + .2)) / Math.Max(.001, 1 - lightness);
        byte Tint(byte value) => (byte)Math.Round(255 - (255 - value) * tintScale);
        var line = SystemParameters.HighContrast ? color : Color.FromArgb(217, Tint(color.R), Tint(color.G), Tint(color.B));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(line), 2), new Rect(1, 1, Math.Max(0, width - 2), Math.Max(0, height - 2)));
    }
}
