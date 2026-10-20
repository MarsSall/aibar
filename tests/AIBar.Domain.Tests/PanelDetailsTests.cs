using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class PanelDetailsTests
{
    [Theory]
    [InlineData(WindowsTheme.Light, 300)]
    [InlineData(WindowsTheme.Light, 360)]
    [InlineData(WindowsTheme.Dark, 300)]
    [InlineData(WindowsTheme.Dark, 360)]
    [InlineData(WindowsTheme.HighContrast, 300)]
    [InlineData(WindowsTheme.HighContrast, 360)]
    public void Cost_detail_preserves_exact_local_usage_disclosure_focus_and_scroll(WindowsTheme theme, int width)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            var window = CreateWindow(); window.Width = width;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window.Show(); window.UpdateLayout();
                var detail = Assert.IsType<StackPanel>(window.FindName("DetailView"));
                Assert.False(detail.IsVisible);
                var cost = Assert.IsType<Button>(window.FindName("CostButton"));
                cost.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Assert.True(detail.IsVisible);
                var back = Assert.IsType<Button>(window.FindName("BackButton"));
                Assert.True(back.IsKeyboardFocused);
                var provider = Visuals<Expander>(detail).Single(item => AutomationProperties.GetName(item) == "Provider row");
                Assert.True(provider.Focusable && provider.IsTabStop); Assert.NotNull(provider.FocusVisualStyle);
                Assert.Contains(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "1234567890 retained tokens");
                Assert.DoesNotContain(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "Input 11");
                var toggle = Assert.IsAssignableFrom<IExpandCollapseProvider>(new ExpanderAutomationPeer(provider).GetPattern(PatternInterface.ExpandCollapse));
                toggle.Expand(); window.UpdateLayout();
                var expected = Assert.IsType<SolidColorBrush>(window.FindResource("TextBrush")).Color;
                if (theme != WindowsTheme.HighContrast)
                {
                    var background = Assert.IsType<SolidColorBrush>(window.FindResource("SurfaceBrush")).Color;
                    var lights = new[] { Luminance(expected), Luminance(background) };
                    Assert.True((lights.Max() + .05) / (lights.Min() + .05) >= 4.5);
                }
                foreach (var label in new[] { "Provider scope", "1234567890 retained tokens", "Input 11", "2026-01-01", "sample-model", "100 tokens", "Input 10" })
                {
                    var text = Assert.Single(Visuals<TextBlock>(provider).Where(text => text.Text == label));
                    Assert.True(text.IsVisible); text.BringIntoView(); window.UpdateLayout(); AssertInViewports(text);
                    if (label is "Provider scope" or "1234567890 retained tokens" or "2026-01-01" or "sample-model")
                        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
                }
                back.Focus(); Assert.True(back.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                // The segmented ListBox owns one Tab stop; focus lives on its selected segment.
                var selector = Assert.IsType<ListBox>(Assert.Single(Visuals<QuotaHistoryChart>(detail)).FindName("HistoryRange"));
                Assert.True(selector.IsKeyboardFocusWithin); Assert.Equal("Usage history range", AutomationProperties.GetName(selector));
                Assert.True(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
                Assert.True(provider.IsKeyboardFocused); window.UpdateLayout(); AssertInViewports(provider);
                toggle.Collapse(); window.UpdateLayout();
                Assert.DoesNotContain(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "Input 11");
                foreach (var label in new[] { "Quota disclosure", "Private endpoint disclosure" })
                {
                    var text = Assert.Single(Visuals<TextBlock>(detail).Where(text => text.Text == label));
                    text.BringIntoView(); window.UpdateLayout(); AssertInViewports(text);
                }
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout();
                Assert.False(detail.IsVisible); Assert.True(cost.IsKeyboardFocused);
                var cards = Visuals<GroupBox>(window).Take(2).ToArray();
                Assert.Equal(new[] { "5-hour quota card", "Weekly quota card" }, cards.Select(AutomationProperties.GetName));
                Assert.Equal(new[] { 25d, 40d }, cards.SelectMany(Visuals<ProgressBar>).Select(bar => bar.Value));
                Assert.All(cards, card =>
                {
                    var border = Assert.IsType<Border>(card.Content);
                    Assert.Same(window.Resources["PanelQuotaCardStyle"], border.Style);
                    Assert.Equal(new Thickness(0), border.BorderThickness);
                    Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(border.Background).Color);
                });
                Assert.False(app.Resources.Contains("PanelQuotaCardStyle"));
                var percentage = Assert.Single(Visuals<TextBlock>(cards[1]).Where(text => text.Text == "40% used"));
                // Reference percentage text is approximately 22 physical pixels at 2x.
                Assert.Equal(11d, percentage.FontSize); Assert.Equal(expected, Assert.IsType<SolidColorBrush>(percentage.Foreground).Color);
                var menu = Assert.IsType<StackPanel>(window.FindName("PanelActions"));
                Assert.All(menu.Children.OfType<Button>(), button =>
                {
                    Assert.True(button.Focusable && button.IsTabStop); Assert.NotNull(button.FocusVisualStyle);
                    button.ApplyTemplate();
                    Assert.Equal(new CornerRadius(6), Assert.IsType<Border>(button.Template.FindName("ActionChrome", button)).CornerRadius);
                });
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    private static void AssertInViewports(FrameworkElement element)
    {
        Assert.True(element.IsVisible && element.ActualHeight > 0);
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not ScrollContentPresenter viewport) continue;
            var bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
            Assert.True(bounds.Left >= -.1 && bounds.Top >= -.1 && bounds.Right <= viewport.ActualWidth + .1 && bounds.Bottom <= viewport.ActualHeight + .1,
                $"Content is clipped: {bounds}, viewport {viewport.RenderSize}");
        }
    }

    private static MainWindow CreateWindow() => new()
    {
        Width = 360, Height = 720, SizeToContent = SizeToContent.Manual,
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
                Rows = new[] { new {
                    AutomationLabel = "Provider row", ScopeLabel = "Provider scope", TotalLabel = "1234567890 retained tokens",
                    TokenBreakdownLabel = "Input 11", StatusLabel = "Current",
                    Details = new[] { new { LocalDayLabel = "2026-01-01", ModelLabel = "sample-model", TotalLabel = "100 tokens", TokenBreakdownLabel = "Input 10", AutomationLabel = "Daily usage" } }
                } }
            }
        }
    };

    private static double Luminance(Color color)
    {
        static double Channel(byte value) { var normalized = value / 255d; return normalized <= .04045 ? normalized / 12.92 : Math.Pow((normalized + .055) / 1.055, 2.4); }
        return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
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
}
