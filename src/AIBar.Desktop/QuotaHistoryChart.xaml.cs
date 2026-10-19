using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Canvas = System.Windows.Controls.Canvas;
using ComboBox = System.Windows.Controls.ComboBox;
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
    private static readonly double[][] Dashes = [[], [5, 2], [1, 2], [5, 2, 1, 2]];

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
        if (Plot is not null) { Reset(); Refresh(); }
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
        var range = (QuotaHistoryChartRange)((ComboBox)HistoryRange).SelectedIndex;
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
    private void Draw()
    {
        if (Plot is null) return;
        Plot.Children.Clear(); Legend.Children.Clear();
        var data = Data;
        HistoryMessage.Text = data is null || data.IsEmpty ? "No usage history yet" : "";
        if (data is null) return;
        if (data.To <= data.From) { HistoryMessage.Text = "Usage history unavailable"; return; }
        for (var index = 0; index < data.Series.Count; index++)
        {
            var series = data.Series[index];
            var legend = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 12, 0) };
            var swatch = new Line { X1 = 0, X2 = 24, Y1 = 7, Y2 = 7, Width = 26, Height = 14, StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection(Dashes[index % Dashes.Length]) };
            swatch.SetResourceReference(Shape.StrokeProperty, "TextBrush"); legend.Children.Add(swatch);
            var label = new TextBlock { Text = series.Label, FontSize = 11, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); legend.Children.Add(label);
            var summary = series.Label + (series.Samples.Count == 0 ? ": no samples" :
                ": " + series.Samples.Count.ToString(CultureInfo.InvariantCulture) + " samples, latest " + series.Samples[^1].Percentage.ToString("0.#", CultureInfo.InvariantCulture) + "% used");
            AutomationProperties.SetName(label, summary); Legend.Children.Add(legend);
            Polyline? line = null;
            AIBar.Domain.QuotaHistorySample? previous = null;
            foreach (var sample in series.Samples)
            {
                // Never draw an artificial consumption segment across a reset cycle.
                var beginsSegment = previous is null || previous.ResetAt != sample.ResetAt || sample.Percentage < previous.Percentage;
                if (beginsSegment)
                {
                    line = new Polyline { StrokeThickness = 2, StrokeDashArray = new DoubleCollection(Dashes[index % Dashes.Length]) };
                    line.SetResourceReference(Shape.StrokeProperty, "TextBrush"); Plot.Children.Add(line);
                }
                var x = (double)((decimal)(sample.RetrievedAt - data.From).Ticks / (data.To - data.From).Ticks) * Plot.ActualWidth;
                var y = (1 - (double)sample.Percentage / 100) * Plot.ActualHeight;
                line!.Points.Add(new Point(x, y));
                if (beginsSegment)
                {
                    var dot = new Ellipse { Width = 3, Height = 3 };
                    dot.SetResourceReference(Shape.FillProperty, "TextBrush");
                    Canvas.SetLeft(dot, Math.Clamp(x - 1.5, 0, Math.Max(0, Plot.ActualWidth - 3)));
                    Canvas.SetTop(dot, Math.Clamp(y - 1.5, 0, Math.Max(0, Plot.ActualHeight - 3)));
                    Plot.Children.Add(dot);
                }
                previous = sample;
            }
        }
    }
}
