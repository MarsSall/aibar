using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Shapes;
using AIBar.Domain;
using Canvas = System.Windows.Controls.Canvas;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AIBar.Desktop;

/// <summary>Display-only daily totals; never starts an analytics scan or a remote read.</summary>
public sealed class DailyUsageChart : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(nameof(Summary), typeof(UsageCostSummary), typeof(DailyUsageChart), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty ChartStyleProperty = DependencyProperty.Register(nameof(ChartStyle), typeof(string), typeof(DailyUsageChart), new PropertyMetadata("line", Changed));
    public static readonly DependencyProperty RangeProperty = DependencyProperty.Register(nameof(Range), typeof(QuotaHistoryChartRange), typeof(DailyUsageChart), new PropertyMetadata(QuotaHistoryChartRange.Day, Changed));
    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(nameof(Provider), typeof(string), typeof(DailyUsageChart), new PropertyMetadata("Codex", Changed));
    public UsageCostSummary? Summary { get => (UsageCostSummary?)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public string ChartStyle { get => (string)GetValue(ChartStyleProperty); set => SetValue(ChartStyleProperty, value); }
    public QuotaHistoryChartRange Range { get => (QuotaHistoryChartRange)GetValue(RangeProperty); set => SetValue(RangeProperty, value); }
    public string Provider { get => (string)GetValue(ProviderProperty); set => SetValue(ProviderProperty, value); }
    public string UnitLabel => _unit.Text;
    public string EmptyMessage => _message.Text;
    public IReadOnlyList<UsageCostDay> VisibleDays => Summary?.Days.TakeLast(Range == QuotaHistoryChartRange.Month ? 30 : 7).ToArray() ?? [];
    private readonly Canvas _plot = new() { ClipToBounds = true };
    // Axis labels mirror the quota chart: maximum, half and zero on the left; first day and today below.
    private readonly TextBlock _top = Axis(VerticalAlignment.Top), _middle = Axis(VerticalAlignment.Center), _zero = Axis(VerticalAlignment.Bottom);
    private readonly TextBlock _start = Axis(VerticalAlignment.Top), _end = Axis(VerticalAlignment.Top);
    public string AxisMaximumLabel => _top.Text;
    public string AxisStartLabel => _start.Text;
    private readonly TextBlock _unit = new() { FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _message = new() { FontSize = 12 };
    public DailyUsageChart()
    {
        var title = new TextBlock { Text = "Daily usage", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) };
        foreach (var text in new[] { title, _unit, _message }) text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(_plot, "Daily usage");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        var frame = new System.Windows.Controls.Grid { Height = 124, Margin = new Thickness(0, 4, 0, 0) };
        frame.ColumnDefinitions.Add(new() { Width = new GridLength(38) }); frame.ColumnDefinitions.Add(new());
        frame.Children.Add(_top); frame.Children.Add(_middle); frame.Children.Add(_zero);
        var area = new System.Windows.Controls.Grid { Margin = new Thickness(0, 6, 2, 6) };
        System.Windows.Controls.Grid.SetColumn(area, 1);
        var bounds = new System.Windows.Controls.Border { BorderThickness = new Thickness(0, 1, 0, 1) };
        var half = new System.Windows.Controls.Border { Height = 1, VerticalAlignment = VerticalAlignment.Center };
        bounds.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "SecondaryBorderBrush");
        half.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "SecondaryBorderBrush");
        area.Children.Add(bounds); area.Children.Add(half); area.Children.Add(_plot);
        frame.Children.Add(area);
        _end.Text = "Today"; _end.HorizontalAlignment = HorizontalAlignment.Right;
        var dates = new System.Windows.Controls.Grid { Margin = new Thickness(38, 0, 2, 8) };
        dates.Children.Add(_start); dates.Children.Add(_end);
        var body = new StackPanel();
        body.Children.Add(title); body.Children.Add(_unit); body.Children.Add(frame); body.Children.Add(dates); body.Children.Add(_message);
        Content = body;
        _plot.SizeChanged += (_, _) => Draw();
        Draw();
    }
    private static TextBlock Axis(VerticalAlignment alignment)
    {
        var text = new TextBlock { FontSize = 11, VerticalAlignment = alignment };
        text.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
        return text;
    }
    private static string Compact(double value, bool dollars) => dollars
        ? "$" + value.ToString(value < 10 ? "0.##" : "0", CultureInfo.InvariantCulture)
        : value >= 1e9 ? (value / 1e9).ToString("0.#", CultureInfo.InvariantCulture) + "B"
        : value >= 1e6 ? (value / 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "M"
        : value >= 1e3 ? (value / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "K"
        : value.ToString("0", CultureInfo.InvariantCulture);
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((DailyUsageChart)sender).Draw();
    private void Draw()
    {
        _plot.Children.Clear();
        var days = VisibleDays;
        // Zero-filled days alone are no usage: a flat line on the floor reads as a broken chart.
        var empty = days.Count == 0 || days.All(day => day.Tokens == 0);
        _message.Text = empty ? "No daily usage yet" : "";
        var priced = days.All(day => day.Cost.HasValue && !day.Partial && !day.Unpriced);
        _unit.Text = priced ? "$ per day" : "tokens per day";
        AutomationProperties.SetName(_unit, _unit.Text);
        _start.Text = days.Count == 0 ? "" : days[0].Day.ToString("MMM d", CultureInfo.InvariantCulture);
        if (empty) { _top.Text = _middle.Text = _zero.Text = ""; return; }
        double Value(UsageCostDay day) => priced ? (double)day.Cost!.Value : day.Tokens;
        var maximum = days.Max(Value);
        if (maximum <= 0) maximum = 1;
        _top.Text = Compact(maximum, priced); _middle.Text = Compact(maximum / 2, priced); _zero.Text = priced ? "$0" : "0";
        var width = _plot.ActualWidth / days.Count;
        Polyline? line = null;
        if (ChartStyle == "line")
        {
            line = new Polyline { StrokeThickness = 2.5 };
            QuotaHistoryChart.SetProviderBrush(line, Shape.StrokeProperty, Provider);
            AutomationProperties.SetName(line, "Daily usage, " + _unit.Text);
            _plot.Children.Add(line);
        }
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var height = Value(day) / maximum * _plot.ActualHeight;
            var x = (index + .5) * width;
            var y = _plot.ActualHeight - height;
            Shape point;
            if (line is null)
                point = new Rectangle { Width = Math.Max(0, width - 3), Height = height };
            else
            {
                line.Points.Add(new Point(x, y));
                point = new Ellipse { Width = 4, Height = 4 };
            }
            QuotaHistoryChart.SetProviderBrush(point, Shape.FillProperty, Provider);
            var cost = day.Cost is { } value ? "$" + value.ToString("F2", CultureInfo.InvariantCulture) + (day.Partial ? "+" : "") : "Cost unavailable";
            AutomationProperties.SetName(point, $"{day.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}: {cost} · {day.Tokens.ToString(CultureInfo.InvariantCulture)} tokens");
            point.ToolTip = AutomationProperties.GetName(point);
            Canvas.SetLeft(point, line is null ? index * width + 1.5 : x - 2);
            Canvas.SetTop(point, line is null ? y : y - 2);
            _plot.Children.Add(point);
        }
    }
}
