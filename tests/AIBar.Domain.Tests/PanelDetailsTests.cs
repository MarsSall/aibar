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

    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Panel_visual_treatment_is_local_and_preserves_semantic_colors(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            var window = CreateWindow();
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                Layout(window);
                Assert.True(window.Resources.Contains("PanelQuotaCardStyle"));
                Assert.False(app.Resources.Contains("PanelQuotaCardStyle"));
                var cards = Visuals<GroupBox>((Grid)window.Content).Take(2).ToArray();
                Assert.Equal(new[] { "Weekly quota card", "5-hour quota card" }, cards.Select(AutomationProperties.GetName));
                foreach (var card in cards)
                {
                    var border = Assert.IsType<Border>(card.Content);
                    Assert.Same(window.Resources["PanelQuotaCardStyle"], border.Style);
                    Assert.Equal(new CornerRadius(0), border.CornerRadius);
                    Assert.Equal(new Thickness(0), border.BorderThickness);
                    Assert.Equal(Colors.Transparent, ColorOf(border.Background));
                    Assert.Equal(ColorOf(window.FindResource("SecondaryBorderBrush")), ColorOf(border.BorderBrush));
                }
                var percentage = Assert.Single(Visuals<TextBlock>(cards[0]).Where(text => text.Text == "40%"));
                Assert.Equal(20d, percentage.FontSize);
                Assert.Equal(ColorOf(window.FindResource("TextBrush")), ColorOf(percentage.Foreground));
                Assert.Equal(new[] { 40d, 25d }, cards.SelectMany(Visuals<ProgressBar>).Select(bar => bar.Value));
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    [Fact]
    public void Copy_and_refresh_share_a_wrapping_row_and_keep_keyboard_affordances()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = CreateWindow();
            try
            {
                Layout(window);
                var actions = Assert.IsType<WrapPanel>(window.FindName("PanelActions"));
                var buttons = actions.Children.OfType<Button>().ToArray();
                Assert.Equal(new[] { "Copy quota summary", "Refresh quota" }, buttons.Select(AutomationProperties.GetName));
                Assert.All(buttons, button =>
                {
                    Assert.True(button.Focusable && button.IsTabStop);
                    Assert.NotNull(button.FocusVisualStyle);
                    Assert.Same(window.Resources["PanelActionButtonStyle"], button.Style);
                    button.ApplyTemplate();
                    var chrome = Assert.IsType<Border>(button.Template.FindName("ActionChrome", button));
                    Assert.Equal(new CornerRadius(8), chrome.CornerRadius);
                });
                var copyBounds = buttons[0].TransformToAncestor(actions).TransformBounds(new Rect(buttons[0].RenderSize));
                var refreshBounds = buttons[1].TransformToAncestor(actions).TransformBounds(new Rect(buttons[1].RenderSize));
                Assert.True(copyBounds.Width > 0 && refreshBounds.Width > 0);
                Assert.Equal(copyBounds.Top, refreshBounds.Top);
                Assert.True(refreshBounds.Left >= copyBounds.Right);
                var footer = Assert.IsType<StackPanel>(((Grid)window.Content).Children[3]);
                Assert.Same(footer, actions.Parent);
                Assert.False(((Expander)window.FindName("AnalyticsDetailsExpander")).IsExpanded);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(WindowsTheme.Light, 300)]
    [InlineData(WindowsTheme.Light, 420)]
    [InlineData(WindowsTheme.Dark, 300)]
    [InlineData(WindowsTheme.Dark, 420)]
    [InlineData(WindowsTheme.HighContrast, 300)]
    [InlineData(WindowsTheme.HighContrast, 420)]
    public void Integrated_composition_keeps_exact_details_accessible_at_supported_widths(WindowsTheme theme, int width)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            const string total = "1234567890 retained tokens";
            var window = CreateWindow(total);
            window.Width = width;
            window.SizeToContent = SizeToContent.Height;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window.Show();
                window.UpdateLayout();
                var root = Assert.IsType<Grid>(window.Content);
                var details = Assert.IsType<Expander>(window.FindName("AnalyticsDetailsExpander"));
                Assert.False(details.IsExpanded);
                details.ApplyTemplate();
                Assert.NotNull(details.Template.FindName("DetailsChevron", details));
                Assert.NotNull(window.FindName("PanelHeaderBand"));
                Assert.NotNull(window.FindName("PanelFooterRule"));
                SaveRender(root, theme, width, "collapsed");
                details.Focus();
                Assert.True(details.IsKeyboardFocused);
                var key = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Space)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                details.RaiseEvent(key);
                Assert.True(key.Handled && details.IsExpanded);
                window.UpdateLayout();
                var provider = Visuals<Expander>(details).Single(row => AutomationProperties.GetName(row) == "Provider row");
                Assert.Contains(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == total);
                Assert.DoesNotContain(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "Input 11");
                var peer = new ExpanderAutomationPeer(provider);
                var toggle = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer.GetPattern(PatternInterface.ExpandCollapse));
                toggle.Expand(); window.UpdateLayout();
                Assert.Contains(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "Input 11");
                Assert.Contains(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "sample-model");
                System.Windows.Input.InputManager.Current.ProcessInput(new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Tab)
                    { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
                Assert.True(provider.IsKeyboardFocused);
                provider.BringIntoView(); window.UpdateLayout();
                foreach (var label in new[] { total, "Input 11", "2026-01-01", "sample-model", "100 tokens", "Input 10" })
                    AssertInViewports(Assert.Single(Visuals<TextBlock>(provider).Where(text => text.Text == label)));
                Assert.NotEmpty(System.Windows.Documents.AdornerLayer.GetAdornerLayer(provider).GetAdorners(provider));
                SaveRender(root, theme, width, "expanded");
                toggle.Collapse(); window.UpdateLayout();
                Assert.DoesNotContain(Visuals<TextBlock>(provider), text => text.IsVisible && text.Text == "Input 11");
                foreach (var label in new[] { "Quota disclosure", "Private endpoint disclosure" })
                {
                    var notice = Assert.Single(Visuals<TextBlock>(details).Where(text => text.Text == label));
                    notice.BringIntoView(); window.UpdateLayout();
                    AssertInViewports(notice);
                    SaveRender(root, theme, width, label.StartsWith("Private", StringComparison.Ordinal) ? "private-access" : "quota-access");
                }
                details.Focus();
                var enter = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Enter)
                    { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                details.RaiseEvent(enter);
                Assert.True(enter.Handled && !details.IsExpanded);
                window.UpdateLayout();
                foreach (var child in root.Children.OfType<FrameworkElement>())
                {
                    var bounds = child.TransformToAncestor(root).TransformBounds(new Rect(child.RenderSize));
                    Assert.InRange(bounds.Left, 0, root.ActualWidth);
                    Assert.True(bounds.Right <= root.ActualWidth + .1 && bounds.Bottom <= root.ActualHeight + .1);
                }
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
            Assert.True(bounds.Left >= -.1 && bounds.Top >= -.1 && bounds.Right <= viewport.ActualWidth + .1 &&
                bounds.Bottom <= viewport.ActualHeight + .1, $"Content is clipped: {bounds}, viewport {viewport.RenderSize}");
        }
    }

    private static void SaveRender(FrameworkElement root, WindowsTheme theme, int width, string state)
    {
        var directory = Environment.GetEnvironmentVariable("AIBAR_PANEL_RENDER_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        // The root Grid is transparent; include its real Window background in the export.
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(Window.GetWindow(root).Background, null, new Rect(root.RenderSize));
        bitmap.Render(background);
        bitmap.Render(root);
        if (System.Windows.Documents.AdornerLayer.GetAdornerLayer(root) is { } layer) bitmap.Render(layer);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"panel-{theme}-{width}-{state}.png"));
        png.Save(output);
    }

    private static Color ColorOf(object brush) => Assert.IsType<SolidColorBrush>(brush).Color;

    private static MainWindow CreateWindow(string total = "165 retained tokens") => new()
    {
        Width = 420, Height = 720, SizeToContent = SizeToContent.Manual,
        DataContext = new
        {
            IsRefreshAvailable = true, FreshnessLabel = "Current",
            Primary = new QuotaWindowPresentation("5-hour quota", 25m, "01h 00m"),
            Weekly = new QuotaWindowPresentation("Weekly quota", 40m, "03d 00h"),
            State = new
            {
                IsWeeklyAvailable = true, IsPrimaryAvailable = true, Disclosure = "Quota disclosure",
                IsLoading = false, IsMissingCredential = false, IsOffline = false, IsDegraded = false,
                IsUnavailable = false, IsPrivateIntegrationDisabled = false, IsSafeError = false
            },
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
                        TotalLabel = total, TokenBreakdownLabel = "Input 11", StatusLabel = "Current",
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
