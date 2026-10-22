using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class PanelRenderRegressionTests
{
    [Theory]
    [InlineData(WindowsTheme.Dark, 360)]
    [InlineData(WindowsTheme.Light, 360)]
    [InlineData(WindowsTheme.HighContrast, 300)]
    public void Current_summary_fits_height_cap_and_cost_columns_do_not_overlap(WindowsTheme theme, int width)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            var window = CreateWindow(width);
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window.Show(); window.SelectProvider("Claude"); Pump(window);
                var scroll = (ScrollViewer)window.FindName("SecondaryScrollViewer");
                Assert.InRange(window.ActualHeight, 260, 640);
                Assert.InRange(scroll.ScrollableHeight, 0, .5);
                var cost = (Button)window.FindName("CostButton");
                var bounds = cost.TransformToAncestor(scroll).TransformBounds(new Rect(cost.RenderSize));
                Assert.True(bounds.Bottom <= scroll.ViewportHeight + .5);
                var texts = Descendants<TextBlock>(cost).Where(text => text.IsVisible && !string.IsNullOrEmpty(text.Text)).ToArray();
                Assert.Contains(texts, text => text.Text.Contains("partial", StringComparison.Ordinal));
                foreach (var text in texts)
                {
                    var rect = text.TransformToAncestor(cost).TransformBounds(new Rect(text.RenderSize));
                    Assert.True(rect.Left >= 0 && rect.Right <= cost.ActualWidth + .5);
                    foreach (var other in texts.Where(other => !ReferenceEquals(text, other)))
                    {
                        var intersection = Rect.Intersect(rect, other.TransformToAncestor(cost).TransformBounds(new Rect(other.RenderSize)));
                        Assert.True(intersection.IsEmpty || intersection.Width < .5 || intersection.Height < .5,
                            $"Cost text overlaps: {text.Text} / {other.Text}");
                    }
                }
                AssertOpaque(window);
                // Genuinely oversized content must still scroll; do not fix clipping by hiding overflow.
                // The cap lives on the summary viewport; lower it to force genuine overflow.
                scroll.MaxHeight = 300; Pump(window);
                Assert.True(scroll.ScrollableHeight > 0);
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    [Fact]
    public void Light_selected_tab_keeps_readable_primary_text_on_its_pale_fill()
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            var window = CreateWindow(360);
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(WindowsTheme.Light);
                window.Show(); window.SelectProvider("Claude"); Pump(window);
                var tab = (RadioButton)window.FindName("ClaudeTab");
                Assert.Equal(((SolidColorBrush)window.FindResource("TextBrush")).Color,
                    ((SolidColorBrush)tab.Foreground).Color);
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    [Fact]
    public void Offscreen_show_navigation_and_reshow_never_animate_content_opacity()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = CreateWindow(360);
            try
            {
                window.Show(); Pump(window); AssertOpaque(window);
                ((Button)window.FindName("CostButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(window); AssertOpaque(window);
                ((Button)window.FindName("CloseDetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.SelectProvider("Codex"); Pump(window); AssertOpaque(window);
                window.Dismiss(); Assert.False(window.IsVisible);
                window.Show(); Pump(window); AssertOpaque(window);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Login_keeps_cached_Claude_cards_but_no_cache_exposes_no_meters(bool cached)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var now = DateTimeOffset.UtcNow;
            var state = new ClaudeQuotaPresentationMapper(new Clock(now)).Map(new(
                cached ? new QuotaSnapshot(new(67, now.AddHours(2)), new(14, now.AddDays(3)), now.AddMinutes(-20)) : null,
                cached ? FreshnessState.Stale : FreshnessState.Unavailable, false,
                new(QuotaErrorKind.Authentication, "quota_authentication"), null), true);
            var window = CreateWindow(360);
            window.DataContext = new { Claude = new { IsEnabled = true, State = state } };
            try
            {
                window.Show(); window.SelectProvider("Claude"); Pump(window);
                var section = (StackPanel)window.FindName("ClaudeQuotaSection");
                var cards = section.Children.OfType<GroupBox>().ToArray();
                Assert.Equal(new[] { "Claude 5-hour quota card", "Claude weekly quota card" }, cards.Select(AutomationProperties.GetName));
                Assert.All(cards, card => Assert.Equal(cached, card.IsVisible));
                var text = Descendants<TextBlock>(section).Where(text => text.IsVisible).Select(text => text.Text).ToArray();
                Assert.Contains("Open Claude Code on this PC to renew the session", text);
                Assert.Equal(cached, text.Contains("67%"));
            }
            finally { window.Close(); }
        });
    }

    private static MainWindow CreateWindow(int width)
    {
        var now = DateTimeOffset.UtcNow;
        var state = new ClaudeQuotaPresentationMapper(new Clock(now)).Map(new(
            new(new(7, now.AddHours(3)), new(14, now.AddDays(3)), now), FreshnessState.Current, false, null, null), true);
        return new MainWindow
        {
            Width = width, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000,
            DataContext = new
            {
                state.Primary, state.Weekly, State = state, state.FreshnessLabel,
                Claude = new { IsEnabled = true, State = state },
                ClaudeCost = new { State = new { TodayLabel = "Today: $38.17 · 264M tokens", Last30DaysLabel = "Last 30 days: $254.24 · 918M tokens (partial)" } },
                Analytics = new { UsageCost = new { TodayLabel = "Today: $0.04 · 15K tokens", Last30DaysLabel = "Last 30 days: $12.80 · 9.4M tokens" } }
            }
        };
    }

    private static void AssertOpaque(MainWindow window)
    {
        if (window.DetailsWindow is { } side)
        {
            Pump(side);
            Assert.Equal(1d, side.Opacity);
            Assert.Same(side, Window.GetWindow((FrameworkElement)window.FindName("DetailView")));
        }
        foreach (var body in new[] { (FrameworkElement)window.Content, (FrameworkElement)window.FindName("SummaryView"), (FrameworkElement)window.FindName("DetailView") })
        {
            Assert.Equal(1d, body.Opacity);
            Assert.False(DependencyPropertyHelper.GetValueSource(body, UIElement.OpacityProperty).IsAnimated);
        }
    }
    private static void Pump(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class Clock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
}
