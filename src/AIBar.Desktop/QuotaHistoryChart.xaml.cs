using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Canvas = System.Windows.Controls.Canvas;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using StackPanel = System.Windows.Controls.StackPanel;
using TextBlock = System.Windows.Controls.TextBlock;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using Orientation = System.Windows.Controls.Orientation;

namespace AIBar.Desktop;

public partial class QuotaHistoryChart : UserControl
{
    public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(nameof(History), typeof(PanelPaceHistory),
        typeof(QuotaHistoryChart), new PropertyMetadata(null, OnHistoryChanged));
    public PanelPaceHistory? History { get => (PanelPaceHistory?)GetValue(HistoryProperty); set => SetValue(HistoryProperty, value); }
    /// <summary>The immutable plot input; also supports standalone, data-bound chart consumers.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(QuotaHistoryChartData),
        typeof(QuotaHistoryChart), new PropertyMetadata(null, (sender, _) => ((QuotaHistoryChart)sender).Draw()));
    public QuotaHistoryChartData? Data { get => (QuotaHistoryChartData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    private readonly DispatcherTimer _timer;
    private CancellationTokenSource? _read;
    private PanelPaceHistory? _subscribedHistory;
    private long _revision;
    public static readonly DependencyProperty SelectedProviderProperty = DependencyProperty.Register(nameof(SelectedProvider), typeof(string), typeof(QuotaHistoryChart), new PropertyMetadata("Codex", OnDisplayChanged));
    public static readonly DependencyProperty ChartStyleProperty = DependencyProperty.Register(nameof(ChartStyle), typeof(string), typeof(QuotaHistoryChart), new PropertyMetadata("line", OnDisplayChanged));
    public static readonly DependencyProperty CodexDailyProperty = DependencyProperty.Register(nameof(CodexDaily), typeof(AIBar.Domain.UsageCostSummary), typeof(QuotaHistoryChart), new PropertyMetadata(null, OnDisplayChanged));
    public static readonly DependencyProperty ClaudeDailyProperty = DependencyProperty.Register(nameof(ClaudeDaily), typeof(AIBar.Domain.UsageCostSummary), typeof(QuotaHistoryChart), new PropertyMetadata(null, OnDisplayChanged));
    public string SelectedProvider { get => (string)GetValue(SelectedProviderProperty); set => SetValue(SelectedProviderProperty, value); }
    public string ChartStyle { get => (string)GetValue(ChartStyleProperty); set => SetValue(ChartStyleProperty, value); }
    public AIBar.Domain.UsageCostSummary? CodexDaily { get => (AIBar.Domain.UsageCostSummary?)GetValue(CodexDailyProperty); set => SetValue(CodexDailyProperty, value); }
    public AIBar.Domain.UsageCostSummary? ClaudeDaily { get => (AIBar.Domain.UsageCostSummary?)GetValue(ClaudeDailyProperty); set => SetValue(ClaudeDailyProperty, value); }
    public event Action<string>? StyleSelected;
    private static void OnDisplayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((QuotaHistoryChart)sender).Draw();
    private void OnStyleChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Plot is null || _updatingStyle) return;
        var style = StyleSelector.SelectedIndex == 1 ? "columns" : "line";
        SetCurrentValue(ChartStyleProperty, style);
        StyleSelected?.Invoke(style);
    }
    private bool _updatingStyle;

    public QuotaHistoryChart()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(30), DispatcherPriority.Background, (_, _) => Refresh(), Dispatcher);
        _timer.Stop();
        IsVisibleChanged += (_, _) => { if (IsVisible) { _timer.Start(); Refresh(); } else { _timer.Stop(); Reset(); } };
        Loaded += (_, _) => { SetHistorySubscription(History); if (IsVisible) { _timer.Start(); Refresh(); } };
        Unloaded += (_, _) => { _timer.Stop(); SetHistorySubscription(null); Reset(); };
    }

    private static void OnHistoryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var chart = (QuotaHistoryChart)sender;
        chart.SetHistorySubscription(chart.IsLoaded ? args.NewValue as PanelPaceHistory : null);
        chart.Reset(); chart.Refresh();
    }
    private void SetHistorySubscription(PanelPaceHistory? history)
    {
        if (ReferenceEquals(_subscribedHistory, history)) return;
        if (_subscribedHistory is not null) _subscribedHistory.Invalidated -= Reset;
        _subscribedHistory = history;
        if (_subscribedHistory is not null) _subscribedHistory.Invalidated += Reset;
    }
    private void OnRangeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Plot is not null) { Reset(); Refresh(); Draw(); }
    }
    private void OnRangeKeyDown(object sender, KeyEventArgs e)
    {
        var selector = (System.Windows.Controls.ListBox)sender;
        var index = selector.SelectedIndex;
        var next = e.Key switch
        {
            Key.Left or Key.Up => Math.Max(0, index - 1),
            Key.Right or Key.Down => Math.Min(selector.Items.Count - 1, index + 1),
            Key.Home => 0,
            Key.End => selector.Items.Count - 1,
            _ => -1
        };
        if (next < 0) return; // Tab is deliberately left to normal focus traversal.
        selector.SelectedIndex = next;
        ((ListBoxItem)selector.Items[next]).Focus();
        e.Handled = true;
    }
    private void OnPlotSizeChanged(object sender, SizeChangedEventArgs e) => Draw();
    private void Reset()
    {
        ++_revision;
        var retired = _read; _read = null;
        if (retired is not null) _ = PanelPaceHistory.CancelAndDisposeAsync(retired);
        Data = null;
        if (Plot is null) return;
        Plot.Children.Clear(); Legend.Children.Clear(); HistoryMessage.Text = "No usage history yet";
    }
    private async void Refresh()
    {
        var history = History;
        if (!IsVisible || history is null || _read is not null) return;
        var revision = ++_revision;
        var historyRevision = history.Revision;
        var cancellation = new CancellationTokenSource(); _read = cancellation;
        var range = (QuotaHistoryChartRange)HistoryRange.SelectedIndex;
        RangeStart.Text = range switch { QuotaHistoryChartRange.Day => "24h ago", QuotaHistoryChartRange.Week => "7d ago", _ => "30d ago" };
        try
        {
            var data = await history.ReadChartAsync(range, cancellation.Token);
            if (revision != _revision || !IsVisible || !ReferenceEquals(History, history) || !history.CanPublish(historyRevision)) return;
            Data = data;
            history.RefreshPace();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (revision == _revision) { Data = null; HistoryMessage.Text = "Usage history unavailable"; }
        }
        finally
        {
            // Reset transfers ownership of an interrupted source to the cancellation helper.
            if (ReferenceEquals(_read, cancellation))
            {
                _read = null;
                _ = PanelPaceHistory.CancelAndDisposeAsync(cancellation);
            }
        }
    }
    internal static void SetProviderBrush(Shape target, DependencyProperty property, string provider)
    {
        target.SetResourceReference(TagProperty, "SurfaceBrush");
        var brush = new System.Windows.Data.MultiBinding { Converter = new ProviderVisuals(), ConverterParameter = provider };
        brush.Bindings.Add(new System.Windows.Data.Binding { Source = 0 });
        brush.Bindings.Add(new System.Windows.Data.Binding("Tag") { RelativeSource = System.Windows.Data.RelativeSource.Self });
        brush.Bindings.Add(new System.Windows.Data.Binding("Tag.Color") { RelativeSource = System.Windows.Data.RelativeSource.Self });
        System.Windows.Data.BindingOperations.SetBinding(target, property, brush);
    }

    private void DrawColumns(QuotaHistoryChartData data, QuotaHistoryChartSeries series, double opacity)
    {
        var bucket = HistoryRange.SelectedIndex switch { 0 => TimeSpan.FromHours(1), 1 => TimeSpan.FromHours(6), _ => TimeSpan.FromDays(1) };
        var count = Math.Ceiling((data.To - data.From).TotalSeconds / bucket.TotalSeconds);
        var width = Plot.ActualWidth / Math.Max(1, count);
        var slot = series.Window == AIBar.Domain.QuotaHistoryWindow.FiveHour ? 0 : 1;
        // The inclusive right edge belongs to the final bucket, not a duplicate column at the same position.
        var bucketCount = Math.Max(1L, (long)count);
        foreach (var group in series.Samples.GroupBy(sample => Math.Clamp((sample.RetrievedAt - data.From).Ticks / bucket.Ticks, 0L, bucketCount - 1)))
        {
            var percentage = group.Max(sample => sample.Percentage);
            var column = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, width / 2 - 1), Height = (double)percentage / 100 * Plot.ActualHeight, Opacity = opacity };
            SetProviderBrush(column, Shape.FillProperty, series.Provider.ToString());
            AutomationProperties.SetName(column, series.Label + ": " + percentage.ToString("0.#", CultureInfo.InvariantCulture) + "% used");
            Canvas.SetLeft(column, group.Key * width + slot * width / 2);
            Canvas.SetTop(column, Plot.ActualHeight - column.Height);
            Plot.Children.Add(column);
        }
    }

    private void Draw()
    {
        if (Plot is null) return;
        Plot.Children.Clear(); Legend.Children.Clear();
        _updatingStyle = true;
        try { StyleSelector.SelectedIndex = ChartStyle == "columns" ? 1 : 0; }
        finally { _updatingStyle = false; }
        DailyChart.ChartStyle = ChartStyle;
        DailyChart.Range = (QuotaHistoryChartRange)HistoryRange.SelectedIndex;
        DailyChart.Provider = SelectedProvider;
        DailyChart.Summary = SelectedProvider == "Claude" ? ClaudeDaily : CodexDaily;
        var data = Data;
        HistoryMessage.Text = data is null || !data.Series.Any(series => series.Provider.ToString() == SelectedProvider && series.Samples.Count > 0) ? "No usage history yet" : "";
        if (data is null) return;
        if (data.To <= data.From) { HistoryMessage.Text = "Usage history unavailable"; return; }
        for (var index = 0; index < data.Series.Count; index++)
        {
            var series = data.Series[index];
            if (series.Provider.ToString() != SelectedProvider) continue;
            var opacity = series.Window == AIBar.Domain.QuotaHistoryWindow.Weekly ? .5 : 1;
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 12, 0) };
            var swatch = new Line { X1 = 0, X2 = 24, Y1 = 7, Y2 = 7, Width = 26, Height = 14, StrokeThickness = 2.5, Opacity = opacity };
            SetProviderBrush(swatch, Shape.StrokeProperty, series.Provider.ToString()); legend.Children.Add(swatch);
            var label = new TextBlock { Text = series.Label, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); legend.Children.Add(label);
            var summary = series.Label + (series.Samples.Count == 0 ? ": no samples" :
                ": " + series.Samples.Count.ToString(CultureInfo.InvariantCulture) + " samples, latest " + series.Samples[^1].Percentage.ToString("0.#", CultureInfo.InvariantCulture) + "% used");
            AutomationProperties.SetName(label, summary); Legend.Children.Add(legend);
            if (ChartStyle == "columns")
            {
                DrawColumns(data, series, opacity);
                continue;
            }
            Polyline? line = null;
            AIBar.Domain.QuotaHistorySample? previous = null;
            foreach (var sample in series.Samples)
            {
                // Never draw an artificial consumption segment across a reset cycle.
                var paceWindow = sample.Window == AIBar.Domain.QuotaHistoryWindow.FiveHour ? AIBar.Domain.QuotaPaceWindow.FiveHour : AIBar.Domain.QuotaPaceWindow.Weekly;
                // Reset jitter (sub-second differences) is the same cycle; only a real reset starts a new line.
                var beginsSegment = previous is null || !AIBar.Domain.QuotaAlertPolicy.IsSameCycle(previous.ResetAt, sample.ResetAt, paceWindow) ||
                    sample.Percentage < previous.Percentage;
                if (beginsSegment)
                {
                    line = new Polyline { StrokeThickness = 2.5, Opacity = opacity };
                    SetProviderBrush(line, Shape.StrokeProperty, series.Provider.ToString());
                    AutomationProperties.SetName(line, series.Label);
                    Plot.Children.Add(line);
                }
                var x = (double)((decimal)(sample.RetrievedAt - data.From).Ticks / (data.To - data.From).Ticks) * Plot.ActualWidth;
                var y = (1 - (double)sample.Percentage / 100) * Plot.ActualHeight;
                line!.Points.Add(new Point(x, y));
                if (beginsSegment)
                {
                    var dot = new Ellipse { Width = 3, Height = 3, Opacity = opacity };
                    SetProviderBrush(dot, Shape.FillProperty, series.Provider.ToString());
                    Canvas.SetLeft(dot, Math.Clamp(x - 1.5, 0, Math.Max(0, Plot.ActualWidth - 3)));
                    Canvas.SetTop(dot, Math.Clamp(y - 1.5, 0, Math.Max(0, Plot.ActualHeight - 3)));
                    Plot.Children.Add(dot);
                }
                previous = sample;
            }
        }
    }
}
