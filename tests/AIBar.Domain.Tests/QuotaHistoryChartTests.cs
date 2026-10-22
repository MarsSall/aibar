using AIBar.Desktop;
using AIBar.Domain;
using System.Windows.Automation;
using ListBox = System.Windows.Controls.ListBox;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class QuotaHistoryChartTests
{
    private static readonly DateTimeOffset Now = new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(QuotaHistoryChartRange.Day, 1)]
    [InlineData(QuotaHistoryChartRange.Week, 7)]
    [InlineData(QuotaHistoryChartRange.Month, 30)]
    public async Task Queries_all_four_series_with_inclusive_range_and_fixed_scale(QuotaHistoryChartRange range, int days)
    {
        var store = new Store();
        var chart = await QuotaHistoryChartData.LoadAsync(store, range, Now, default);
        Assert.Equal(0m, chart.Minimum); Assert.Equal(100m, chart.Maximum);
        Assert.Equal(Now.AddDays(-days), chart.From); Assert.Equal(Now, chart.To);
        Assert.Equal(4, chart.Series.Count); Assert.Equal(4, store.Calls.Count);
        Assert.Equal(4, store.Calls.Select(call => (call.Provider, call.Window)).Distinct().Count());
        Assert.All(store.Calls, call => { Assert.Equal(chart.From, call.From); Assert.Equal(Now, call.To); });
        Assert.All(chart.Series, series =>
        {
            Assert.Equal(2, series.Samples.Count);
            Assert.Equal(chart.From, series.Samples[0].RetrievedAt); Assert.Equal(Now, series.Samples[1].RetrievedAt);
        });
    }

    [Fact]
    public async Task Empty_history_is_explicit_and_cancelled_queries_do_not_publish_data()
    {
        var chart = await QuotaHistoryChartData.LoadAsync(new Store { Empty = true }, QuotaHistoryChartRange.Day, Now, default);
        Assert.True(chart.IsEmpty); Assert.All(chart.Series, series => Assert.Empty(series.Samples));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => QuotaHistoryChartData.LoadAsync(new Store(), QuotaHistoryChartRange.Day, Now, cancellation.Token));
    }

    [Fact]
    public void Selector_is_keyboard_accessible_named_and_defaults_to_24_hours()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var chart = new QuotaHistoryChart();
            // S9 replaces dropdown chrome, not the range/data/accessibility contract.
            var selector = Assert.IsType<ListBox>(chart.FindName("HistoryRange"));
            // One Tab stop: the selected segment, not the ListBox shell (arrows/Home/End move within it).
            Assert.False(selector.IsTabStop); Assert.Equal(System.Windows.Input.KeyboardNavigationMode.Once, System.Windows.Input.KeyboardNavigation.GetTabNavigation(selector));
            Assert.All(selector.Items.Cast<System.Windows.Controls.ListBoxItem>(), item => Assert.True(item.Focusable));
            Assert.Equal("Usage history range", AutomationProperties.GetName(selector));
            Assert.Equal(0, selector.SelectedIndex); Assert.Equal(3, selector.Items.Count);
            Assert.Equal(new[] { "24h", "7d", "30d" }, selector.Items.Cast<System.Windows.Controls.ListBoxItem>().Select(item => (string)item.Content));
            Assert.All(selector.Items.Cast<System.Windows.Controls.ListBoxItem>(), item => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(item))));
            chart.Data = new(Now, Now, []);
            Assert.Equal("Usage history unavailable", ((System.Windows.Controls.TextBlock)chart.FindName("HistoryMessage")).Text);
            chart.Data = new(Now.AddDays(-1), Now, []);
            Assert.Equal("No usage history yet", ((System.Windows.Controls.TextBlock)chart.FindName("HistoryMessage")).Text);
        });
    }

    [Theory]
    [InlineData(WindowsTheme.Dark, false, false)]
    [InlineData(WindowsTheme.Light, false, false)]
    [InlineData(WindowsTheme.HighContrast, false, false)]
    [InlineData(WindowsTheme.Dark, true, false)]
    [InlineData(WindowsTheme.Dark, false, true)]
    public void Narrow_chart_renders_selected_provider_solid_series_with_dynamic_brushes_and_clear_removes_points(WindowsTheme theme, bool cycleChange, bool resetJitter)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            var store = new Store { ChangeCycle = cycleChange, ResetJitter = resetJitter };
            var history = new PanelPaceHistory(store, new Clock());
            var chart = new QuotaHistoryChart { History = history };
            var window = new System.Windows.Window { Content = chart, Width = 200, SizeToContent = System.Windows.SizeToContent.Height,
                ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000,
                WindowStartupLocation = System.Windows.WindowStartupLocation.Manual };
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window.Show(); window.UpdateLayout();
                var plot = (System.Windows.Controls.Canvas)chart.FindName("Plot");
                chart.Data = QuotaHistoryChartData.LoadAsync(store, QuotaHistoryChartRange.Day, Now, default).GetAwaiter().GetResult();
                var expectedSegments = cycleChange ? 4 : 2;
                Assert.Equal(expectedSegments, plot.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                var lines = plot.Children.OfType<System.Windows.Shapes.Polyline>().ToArray();
                Assert.All(lines, line => { Assert.True(line.StrokeDashArray is null || line.StrokeDashArray.Count == 0); Assert.Equal(2.5, line.StrokeThickness); });
                Assert.Contains(lines, line => line.Opacity == 1);
                Assert.Contains(lines, line => line.Opacity == .5);
                Assert.All(lines, line =>
                {
                    Assert.Equal(cycleChange ? 1 : 2, line.Points.Count);
                    if (!cycleChange)
                    {
                        Assert.Equal(0d, line.Points[0].X, 3); Assert.Equal(plot.ActualHeight, line.Points[0].Y, 3);
                        Assert.Equal(plot.ActualWidth, line.Points[1].X, 3); Assert.Equal(0d, line.Points[1].Y, 3);
                    }
                    Assert.Equal(theme == WindowsTheme.HighContrast ? System.Windows.SystemColors.HighlightColor :
                        PanelPalette.ProviderColor("Codex"),
                        Assert.IsType<System.Windows.Media.SolidColorBrush>(line.Stroke).Color);
                });
                chart.SelectedProvider = "Claude";
                Assert.All(plot.Children.OfType<System.Windows.Shapes.Polyline>(), line => Assert.StartsWith("Claude", AutomationProperties.GetName(line)));
                Assert.Equal(expectedSegments, plot.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                ((ListBox)chart.FindName("HistoryRange")).SelectedIndex = 2;
                Assert.Empty(plot.Children.Cast<object>());
                chart.Data = QuotaHistoryChartData.LoadAsync(store, QuotaHistoryChartRange.Month, Now, default).GetAwaiter().GetResult();
                Assert.Equal(expectedSegments, plot.Children.OfType<System.Windows.Shapes.Polyline>().Count());
                Assert.Equal("30d ago", ((System.Windows.Controls.TextBlock)chart.FindName("RangeStart")).Text);
                history.CancelAndWaitAsync(default).GetAwaiter().GetResult();
                Assert.Empty(plot.Children.Cast<object>());
                Assert.Equal("No usage history yet", ((System.Windows.Controls.TextBlock)chart.FindName("HistoryMessage")).Text);
            }
            finally { window.Close(); history.Dispose(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    private sealed class Clock : AIBar.Domain.IClock { public DateTimeOffset UtcNow => Now; }

    private sealed class Store : IQuotaHistoryStore
    {
        public bool Empty { get; init; }
        public bool ChangeCycle { get; init; }
        // Providers report the same reset with sub-second jitter; it must not split a line.
        public bool ResetJitter { get; init; }
        public System.Collections.Concurrent.ConcurrentQueue<(QuotaHistoryProvider Provider, QuotaHistoryWindow Window, DateTimeOffset From, DateTimeOffset To)> Calls { get; } = new();
        public ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls.Enqueue((provider, window, fromInclusive, toInclusive));
            var source = provider == QuotaHistoryProvider.Codex ? QuotaHistorySource.CodexLocal : QuotaHistorySource.ClaudeRemote;
            return ValueTask.FromResult<IReadOnlyList<QuotaHistorySample>>(Empty ? [] : [new(provider, window, 0, Now.AddDays(1), fromInclusive, source), new(provider, window, 100, ChangeCycle ? Now.AddDays(8) : Now.AddDays(1).AddSeconds(ResetJitter ? 0.9 : 0), toInclusive, source)]);
        }
    }
}
