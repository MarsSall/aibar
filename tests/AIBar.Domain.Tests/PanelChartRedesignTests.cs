using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class PanelChartRedesignTests
{
    private static readonly DateOnly Today = new(2030, 2, 1);
    private static readonly DateTimeOffset Now = new(2030, 2, 1, 12, 0, 0, TimeSpan.Zero);
    private static UsageCostSummary Usage(string model = "gpt-5") => UsageCostPolicy.Calculate([new(Today, model, 1_000_000, 0, 0, 0)], Today);

    [Theory] // S7: shared selector draws both charts with range-specific maximum buckets.
    [InlineData(QuotaHistoryChartRange.Day, 1, 7)]
    [InlineData(QuotaHistoryChartRange.Week, 7, 7)]
    [InlineData(QuotaHistoryChartRange.Month, 30, 30)]
    public void Style_selector_switches_both_charts_between_lines_and_columns(QuotaHistoryChartRange range, int days, int dailyCount)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var chart = new QuotaHistoryChart { CodexDaily = Usage() };
            var window = new Window { Content = chart, Width = 360, SizeToContent = SizeToContent.Height, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                ((ListBox)chart.FindName("HistoryRange")).SelectedIndex = (int)range;
                chart.Data = new(Now.AddDays(-days), Now, new[] { QuotaHistoryWindow.FiveHour, QuotaHistoryWindow.Weekly }.Select(kind =>
                    new QuotaHistoryChartSeries("Codex " + kind, QuotaHistoryProvider.Codex, kind,
                        [new(QuotaHistoryProvider.Codex, kind, 20, Now.AddDays(7), Now.AddHours(-3), QuotaHistorySource.CodexLocal),
                         new(QuotaHistoryProvider.Codex, kind, 60, Now.AddDays(7), Now.AddHours(-3).AddMinutes(10), QuotaHistorySource.CodexLocal)])).ToArray());
                var quotaPlot = Assert.IsType<Canvas>(chart.FindName("Plot"));
                var daily = Assert.IsType<DailyUsageChart>(chart.FindName("DailyChart"));
                Assert.Equal(2, quotaPlot.Children.OfType<Polyline>().Count());
                Assert.Single(Visuals<Polyline>(daily));
                var selector = Assert.IsType<ListBox>(chart.FindName("StyleSelector"));
                Assert.Equal("Chart style", AutomationProperties.GetName(selector));
                Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(selector));
                Assert.All(selector.Items.Cast<ListBoxItem>(), item => Assert.True(item.Focusable));
                selector.SelectedIndex = 1;
                Assert.Equal("columns", chart.ChartStyle);
                Assert.Empty(quotaPlot.Children.OfType<Polyline>());
                var columns = quotaPlot.Children.OfType<Rectangle>().ToArray();
                Assert.Equal(2, columns.Length); // maximum per series per hour, not one column per sample.
                Assert.All(columns, column => Assert.Equal(quotaPlot.ActualHeight * .6, column.Height, 3));
                Assert.NotEqual(Canvas.GetLeft(columns[0]), Canvas.GetLeft(columns[1]));
                Assert.Equal(dailyCount, Visuals<Rectangle>(daily).Count());
                Assert.Empty(Visuals<Polyline>(daily));
                selector.SelectedIndex = 0;
                Assert.Equal(2, quotaPlot.Children.OfType<Polyline>().Count());
                Assert.Single(Visuals<Polyline>(daily));
                Assert.Empty(Visuals<Rectangle>(daily));
            }
            finally { window.Close(); }
        });
    }

    [Fact] // S7: price only a fully priced visible range; otherwise keep all tokens.
    public void Daily_usage_selects_units_visible_range_and_empty_message()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var daily = new DailyUsageChart();
            var window = new Window { Content = daily, Width = 360, SizeToContent = SizeToContent.Height, ShowInTaskbar = false };
            try
            {
            window.Show(); window.UpdateLayout();
            Assert.Equal("No daily usage yet", daily.EmptyMessage);
            daily.Summary = Usage();
            Assert.Equal("", daily.EmptyMessage);
            Assert.Equal("$ per day", daily.UnitLabel);
            Assert.Equal(7, daily.VisibleDays.Count);
            daily.ChartStyle = "columns";
            Assert.Contains(Visuals<Rectangle>(daily), bar => AutomationProperties.GetName(bar) == "2030-02-01: $1.25 · 1000000 tokens");
            daily.Summary = Usage("unknown");
            Assert.Equal("tokens per day", daily.UnitLabel);
            daily.Summary = UsageCostPolicy.Calculate([new(Today.AddDays(-15), "unknown", 20, 0, 0, 0), new(Today, "gpt-5", 100, 0, 0, 0)], Today);
            Assert.Equal("$ per day", daily.UnitLabel); // unknown model lies outside 7 visible days.
            daily.Range = QuotaHistoryChartRange.Week;
            Assert.Equal(7, daily.VisibleDays.Count);
            daily.Range = QuotaHistoryChartRange.Month;
            Assert.Equal(30, daily.VisibleDays.Count);
            Assert.Equal("tokens per day", daily.UnitLabel);
            daily.Summary = UsageCostPolicy.Calculate([new(Today, "gpt-5", 100, 0, 0, 0)], Today, true);
            Assert.Equal("tokens per day", daily.UnitLabel);
            daily.Summary = UsageCostSummary.Unavailable;
            Assert.Equal("No daily usage yet", daily.EmptyMessage);
            }
            finally { window.Close(); }
        });
    }

    [Fact] // S7: actual MainWindow bindings survive rehosting and follow the provider tab live.
    public void Rehosted_charts_follow_selected_provider_and_appearance_options()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var codex = Usage(); var claude = Usage("unknown");
            var panel = new MainWindow { ClaudeEnabled = true, DataContext = new { Analytics = new { UsageCost = codex }, ClaudeCost = new { State = claude } } };
            try
            {
                panel.ApplyAppearanceOptions(new(ChartStyle: "columns"));
                panel.Show();
                ((Button)panel.FindName("CostButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                panel.DetailsWindow!.UpdateLayout();
                var chart = Assert.Single(Visuals<QuotaHistoryChart>(panel.DetailsWindow));
                var daily = Assert.IsType<DailyUsageChart>(chart.FindName("DailyChart"));
                Assert.Equal("columns", chart.ChartStyle);
                Assert.Same(codex, daily.Summary);
                panel.SelectProvider("Claude"); panel.DetailsWindow.UpdateLayout();
                Assert.Equal("Claude", chart.SelectedProvider);
                Assert.Same(claude, daily.Summary);
                Assert.Equal("tokens per day", daily.UnitLabel);
            }
            finally { panel.Close(); }
        });
    }

    [Fact] // S7: the panel writes through the shared store and startup options restore it.
    public void Chart_style_saves_without_overwriting_other_appearance_options()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aibar-chart-style-" + Guid.NewGuid().ToString("N"));
            var store = new AppearanceSettings(System.IO.Path.Combine(root, "appearance.json"));
            store.SaveAsync(new("usage", "brand", 70, 15, false), default).AsTask().GetAwaiter().GetResult();
            var panel = new MainWindow { AppearanceSettings = store };
            try
            {
                var save = panel.SaveChartStyleAsync("columns");
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                var deadline = DateTime.UtcNow.AddSeconds(60);
                timer.Tick += (_, _) => frame.Continue = !save.IsCompleted && DateTime.UtcNow < deadline;
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
                Assert.True(save.IsCompletedSuccessfully);
                var loaded = store.LoadAsync(default).AsTask().GetAwaiter().GetResult();
                Assert.Equal(new AppearanceOptions("usage", "brand", 70, 15, false, "columns"), loaded);
                panel.ApplyAppearanceOptions(loaded);
                Assert.Equal("columns", panel.ChartStyle);
            }
            finally { panel.Close(); if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
        });
    }

    [Theory] // S8: same surface, border, glow, and opaque paint as Settings, including live theme changes.
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Details_chrome_matches_settings_without_provider_tint_or_opacity(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aibar-chrome-no-io", "appearance.json");
            var source = new ThemeSource(theme);
            var panel = new MainWindow { BackgroundOpacity = .3, ProviderTint = true, ClaudeEnabled = true };
            var settings = new SettingsWindow(new AppearanceSettings(path), new AlertSettings(path + ".alerts"), themeSource: new ThemeSource(theme));
            PanelDetailsWindow? details = null;
            try
            {
                panel.Show(); settings.Show();
                details = new PanelDetailsWindow(panel, new System.Windows.Controls.TextBlock { Text = "Fixture" }, source);
                details.Show(); details.UpdateLayout(); settings.UpdateLayout();
                var expected = Assert.IsType<Border>(settings.FindName("SettingsSurface"));
                Assert.Equal(((SolidColorBrush)expected.Background).Color, ((SolidColorBrush)details.DetailsSurface.Background).Color);
                Assert.Equal(255, ((SolidColorBrush)details.DetailsSurface.Background).Color.A);
                Assert.Equal(((SolidColorBrush)expected.BorderBrush).Color, ((SolidColorBrush)details.DetailsSurface.BorderBrush).Color);
                if (theme == WindowsTheme.Dark)
                {
                    var glow = Assert.IsType<DropShadowEffect>(details.DetailsSurface.Effect);
                    var expectedGlow = Assert.IsType<DropShadowEffect>(expected.Effect);
                    Assert.Equal(expectedGlow.Color, glow.Color); Assert.Equal(29, glow.BlurRadius);
                    Assert.Equal(.27, glow.Opacity); Assert.Equal(0, glow.ShadowDepth);
                }
                else Assert.Null(details.DetailsSurface.Effect);
                Assert.False(details.IsTranslucent);
                source.Set(WindowsTheme.Light);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.Null(details.DetailsSurface.Effect);
                Assert.Equal(System.Windows.Media.Color.FromRgb(214, 214, 214), ((SolidColorBrush)details.DetailsSurface.BorderBrush).Color);
            }
            finally { details?.Close(); settings.Close(); panel.Close(); }
        });
    }

    private sealed class ThemeSource(WindowsTheme theme) : IWindowsThemeSource
    {
        public WindowsTheme Current { get; private set; } = theme;
        public event EventHandler? Changed;
        public void Set(WindowsTheme next) { Current = next; Changed?.Invoke(this, EventArgs.Empty); }
        public void Dispose() { }
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
