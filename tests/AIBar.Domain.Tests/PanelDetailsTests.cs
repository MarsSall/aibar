using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class PanelDetailsTests
{
    [Fact]
    public void Details_start_collapsed_and_expand_without_hiding_quota_or_footer()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = CreateWindow();
            try
            {
                Layout(window);
                var details = Assert.IsType<Expander>(window.FindName("AnalyticsDetailsExpander"));
                Assert.False(details.IsExpanded);
                Assert.True(details.Focusable && details.IsTabStop);
                Assert.NotNull(details.FocusVisualStyle);
                Assert.Equal("Local analytics details", AutomationProperties.GetName(details));
                var peer = new ExpanderAutomationPeer(details);
                var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
                var root = Assert.IsType<Grid>(window.Content);
                var cards = Assert.IsType<StackPanel>(root.Children[1]);
                var footer = Assert.IsType<StackPanel>(root.Children[3]);
                var values = Visuals<ProgressBar>(cards).Select(bar => bar.Value).ToArray();
                foreach (var expanded in new[] { true, false })
                {
                    if (expanded) provider.Expand(); else provider.Collapse();
                    Layout(window);
                    Assert.Equal(expanded, details.IsExpanded);
                    Assert.Equal(values, Visuals<ProgressBar>(cards).Select(bar => bar.Value));
                    foreach (var area in new FrameworkElement[] { cards, footer })
                    {
                        var bounds = area.TransformToAncestor(root).TransformBounds(new Rect(area.RenderSize));
                        Assert.True(area.ActualHeight > 0);
                        Assert.InRange(bounds.Top, 0, root.ActualHeight);
                        Assert.True(bounds.Bottom <= root.ActualHeight + .1);
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Provider_headers_and_expanded_details_resolve_semantic_text_color(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            var window = CreateWindow();
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                Layout(window);
                if (window.FindName("AnalyticsDetailsExpander") is Expander details) details.IsExpanded = true;
                Layout(window);
                var provider = Visuals<Expander>((Grid)window.Content).Single(item => AutomationProperties.GetName(item) == "Provider row");
                provider.IsExpanded = true;
                Layout(window);
                var expected = Assert.IsType<SolidColorBrush>(window.FindResource("TextBrush")).Color;
                foreach (var label in new[] { "Provider scope", "165 retained tokens", "2026-01-01", "sample-model" })
                {
                    var text = Assert.Single(Visuals<TextBlock>(provider).Where(item => item.Text == label));
                    Assert.Equal(expected, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
                }
                if (theme != WindowsTheme.HighContrast)
                {
                    var background = Assert.IsType<SolidColorBrush>(window.FindResource("SurfaceBrush")).Color;
                    var lights = new[] { Luminance(expected), Luminance(background) };
                    Assert.True((lights.Max() + .05) / (lights.Min() + .05) >= 4.5);
                }
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    private static MainWindow CreateWindow() => new()
    {
        Width = 420, Height = 720, SizeToContent = SizeToContent.Manual,
        DataContext = new
        {
            IsRefreshAvailable = true, FreshnessLabel = "Current",
            Primary = new QuotaWindowPresentation("5-hour quota", 25m, "01h 00m"),
            Weekly = new QuotaWindowPresentation("Weekly quota", 40m, "03d 00h"),
            State = new { IsWeeklyAvailable = true, IsPrimaryAvailable = true, Disclosure = "Quota disclosure" },
            PrivateEndpointDisclosure = "Private endpoint disclosure",
            Analytics = new { SourceDetail = "Local", TotalTokensLabel = "100", ModelTotalsLabel = "model" },
            LocalUsage = new
            {
                Description = "Local usage", StatusLabel = "Current",
                Rows = new[]
                {
                    new
                    {
                        AutomationLabel = "Provider row", ScopeLabel = "Provider scope",
                        TotalLabel = "165 retained tokens", TokenBreakdownLabel = "Input 11", StatusLabel = "Current",
                        Details = new[] { new { LocalDayLabel = "2026-01-01", ModelLabel = "sample-model", TotalLabel = "100 tokens", TokenBreakdownLabel = "Input 10", AutomationLabel = "Daily usage" } }
                    }
                }
            }
        }
    };

    private static void Layout(MainWindow window)
    {
        var root = Assert.IsType<Grid>(window.Content);
        root.Measure(new Size(420, 720)); root.Arrange(new Rect(0, 0, 420, 720)); root.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        root.UpdateLayout();
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value) { var normalized = value / 255d; return normalized <= .04045 ? normalized / 12.92 : Math.Pow((normalized + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
    }
}
