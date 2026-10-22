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
    private readonly Canvas _plot = new() { Height = 124, ClipToBounds = true };
    private readonly TextBlock _unit = new() { FontSize = 11, Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _message = new() { FontSize = 12 };
    public DailyUsageChart()
    {
        var title = new TextBlock { Text = "Daily usage", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 8) };
        foreach (var text in new[] { title, _unit, _message }) text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        AutomationProperties.SetName(_plot, "Daily usage");
        AutomationProperties.SetLiveSetting(_message, AutomationLiveSetting.Polite);
        var body = new StackPanel();
        body.Children.Add(title); body.Children.Add(_unit); body.Children.Add(_plot); body.Children.Add(_message);
        Content = body;
        _plot.SizeChanged += (_, _) => Draw();
        Draw();
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((DailyUsageChart)sender).Draw();
    private void Draw()
    {
        _plot.Children.Clear();
        var days = VisibleDays;
        _message.Text = days.Count == 0 ? "No daily usage yet" : "";
        var priced = days.All(day => day.Cost.HasValue && !day.Partial && !day.Unpriced);
        _unit.Text = priced ? "$ per day" : "tokens per day";
        AutomationProperties.SetName(_unit, _unit.Text);
        if (days.Count == 0) return;
        double Value(UsageCostDay day) => priced ? (double)day.Cost!.Value : day.Tokens;
        var maximum = Math.Max(1, days.Max(Value));
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
            var height = Value(day) / maximum * _plot.Height;
            var x = (index + .5) * width;
            var y = _plot.Height - height;
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
