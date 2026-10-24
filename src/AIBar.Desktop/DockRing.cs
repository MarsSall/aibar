using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Path = System.Windows.Shapes.Path;
using ToolTip = System.Windows.Controls.ToolTip;
using UserControl = System.Windows.Controls.UserControl;
using Control = System.Windows.Controls.Control;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace AIBar.Desktop;

/// <summary>Outer 5h / inner weekly usage. Missing values leave tracks, stale readings dim, unavailable readings have no arcs.</summary>
public sealed class DockRing : UserControl
{
    private readonly Path _outer = new() { StrokeThickness = 3 };
    private readonly Path _inner = new() { StrokeThickness = 3 };
    private readonly Path _mark = new() { Width = 19, Height = 19, Stretch = Stretch.Uniform };
    private readonly TextBlock _placeholder = new() { Text = "AI", FontSize = 12, FontWeight = FontWeights.SemiBold,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _details = new() { Margin = new Thickness(6), FontSize = 12 };
    public string Provider { get; private set; } = "AIBar";
    public Geometry OuterArc => _outer.Data;
    public Geometry InnerArc => _inner.Data;
    public string Details => _details.Text;

    public DockRing()
    {
        Width = Height = 56;
        Focusable = false;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/AIBar.Desktop;component/DockProviderMarks.xaml") });
        var grid = new Grid { Background = Brushes.Transparent };
        foreach (var radius in new[] { 23d, 17d })
        {
            var track = new Path { Data = CreateArc(100, radius), StrokeThickness = 3, Opacity = .18 };
            track.SetResourceReference(Path.StrokeProperty, "SettingsTextBrush");
            grid.Children.Add(track);
        }
        grid.Children.Add(_outer); grid.Children.Add(_inner); grid.Children.Add(_mark); grid.Children.Add(_placeholder);
        Content = grid;
        var tooltip = new ToolTip { Content = _details, Focusable = false, StaysOpen = false };
        tooltip.SetResourceReference(Control.BackgroundProperty, "SettingsSurfaceBrush");
        tooltip.SetResourceReference(Control.ForegroundProperty, "SettingsTextBrush");
        ToolTip = tooltip;
        ToolTipService.SetInitialShowDelay(this, 250);
        ToolTipService.SetShowDuration(this, 30000);
        Update("AIBar", null);
    }

    public void Update(string provider, BetaPresentationState? state, string? plan = null)
    {
        Provider = provider;
        var unavailable = state is null || state.IsPrivateIntegrationDisabled || state.IsUnavailable;
        Brush color = unavailable ? Brushes.Gray : SystemParameters.HighContrast
            ? System.Windows.SystemColors.HighlightBrush : new SolidColorBrush(PanelPalette.ProviderColor(provider));
        _outer.Stroke = _inner.Stroke = _mark.Fill = _placeholder.Foreground = color;
        _outer.Opacity = _inner.Opacity = state?.IsCurrent == true ? 1 : .45;
        _outer.Data = CreateArc(unavailable || state?.IsPrimaryAvailable != true ? null : state.Primary.PercentageUsed, 23);
        _inner.Data = CreateArc(unavailable || state?.IsWeeklyAvailable != true ? null : state.Weekly.PercentageUsed, 17);
        _placeholder.Visibility = provider == "AIBar" ? Visibility.Visible : Visibility.Collapsed;
        _mark.Visibility = provider == "AIBar" ? Visibility.Collapsed : Visibility.Visible;
        if (provider != "AIBar") _mark.Data = (Geometry)FindResource(provider == "Claude" ? "DockClaudeMark" : "DockOpenAiMark");
        var primary = unavailable || state?.IsPrimaryAvailable != true ? null : state.Primary.PercentageUsed;
        var weekly = unavailable || state?.IsWeeklyAvailable != true ? null : state.Weekly.PercentageUsed;
        AutomationProperties.SetName(this, $"{provider}: 5 hour {Spoken(primary)}, weekly {Spoken(weekly)}");
        var header = provider + (string.IsNullOrWhiteSpace(plan ?? state?.PlanLabel) ? "" : " · " + (plan ?? state?.PlanLabel));
        _details.Text = state is null ? "AIBar · No providers enabled" :
            $"{header}\n5h  {Percent(primary)}{Reset(state.Primary.ResetCountdown)}\nWeekly  {Percent(weekly)}{Reset(state.Weekly.ResetCountdown)}\n{state.FreshnessLabel}" +
            (string.IsNullOrWhiteSpace(state.WarningLabel ?? state.ErrorLabel) ? "" : " · " + (state.WarningLabel ?? state.ErrorLabel));
        InvalidateVisual();
    }

    /// <summary>Clockwise from noon. A complete circle uses two arcs because coincident arc endpoints draw nothing.</summary>
    public static Geometry CreateArc(decimal? percentage, double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0 || radius > 28) throw new ArgumentOutOfRangeException(nameof(radius));
        var geometry = new StreamGeometry();
        var used = Math.Clamp(percentage ?? 0, 0, 100);
        using (var context = geometry.Open())
        {
            if (used > 0)
            {
                var start = new Point(28, 28 - radius);
                context.BeginFigure(start, false, false);
                if (used == 100)
                {
                    context.ArcTo(new Point(28, 28 + radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                    context.ArcTo(start, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                }
                else
                {
                    var angle = (double)used * Math.PI / 50;
                    context.ArcTo(new Point(28 + radius * Math.Sin(angle), 28 - radius * Math.Cos(angle)),
                        new Size(radius, radius), 0, used > 50, SweepDirection.Clockwise, true, false);
                }
            }
        }
        geometry.Freeze(); return geometry;
    }

    private static string Percent(decimal? value) => value is { } number ? number.ToString("0.#", CultureInfo.CurrentCulture) + "%" : "Unavailable";
    private static string Spoken(decimal? value) => value is { } number ? number.ToString("0.#", CultureInfo.CurrentCulture) + " percent used" : "unavailable";
    private static string Reset(string? countdown) => countdown is null ? "" : " · resets in " + PanelPalette.CompactReset(countdown);
    protected override AutomationPeer OnCreateAutomationPeer() => new UserControlAutomationPeer(this);
}
