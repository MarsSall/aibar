using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class PopupRenderedTests
{
    [Fact]
    public void Popover_show_succeeds_after_a_native_handle_was_created_while_hidden()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            Window? window = null;
            try
            {
                window = new Window();
                new WindowInteropHelper(window).EnsureHandle();
                var popover = new WpfPopoverRuntime(window, surfaceHints: new FakeHints(supports: false));

                popover.Show();

                Assert.True(popover.IsVisible);
            }
            finally
            {
                window?.Close();
            }
        });
    }

    [Fact]
    public void Unit_4b_rendered_popup_keeps_layout_scroll_focus_automation_and_themes_intact()
    {
        WpfTestApplicationHost.Run(app =>
        {
            MainWindow? window = null;
            var originalTheme = app.Resources.MergedDictionaries[0];
            try
            {
                var rows = Enumerable.Range(0, 10).Select(index => new { AutomationLabel = $"Usage row {index}", ScopeLabel = $"Scope {index}", TotalLabel = "165 retained tokens", TokenBreakdownLabel = "Input 11 · Output 44", StatusLabel = "Retained history", Details = Array.Empty<object>() }).ToArray();
                var state = new { CachedAgeLabel = "Cached 00h 05m", WarningLabel = "Network unavailable", IsLoading = false, IsMissingCredential = false, IsOffline = false, IsDegraded = true, IsUnavailable = false, IsPrimaryAvailable = true, IsWeeklyAvailable = true, IsPrivateIntegrationDisabled = false, IsSafeError = false, Disclosure = "Quota disclosure" };
                window = new MainWindow { Width = 420, Height = 720, SizeToContent = SizeToContent.Manual, DataContext = new { IsRefreshAvailable = true, FreshnessLabel = "Stale", Primary = new QuotaCardFixture("5-hour", 42m, "01h 00m"), Weekly = new QuotaCardFixture("Weekly", 73m, "06d 00h"), State = state, PrivateEndpointDisclosure = "Private endpoint disclosure", Analytics = new { SourceDetail = "Local", TotalTokensLabel = "100", ModelTotalsLabel = "model", CoverageLabel = "complete", LastScanLabel = "now", WarningLabel = "", IsLoading = false }, LocalUsage = new { Description = "Local usage", StatusLabel = "Current", Rows = rows } } };
                var root = Assert.IsType<Grid>(window.Content);
                void Layout() { root.Measure(new Size(420, 720)); root.Arrange(new Rect(0, 0, 420, 720)); root.UpdateLayout(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind); root.UpdateLayout(); }
                Layout();
                var details = Assert.IsType<Expander>(window.FindName("AnalyticsDetailsExpander"));
                Assert.False(details.IsExpanded);
                details.IsExpanded = true;
                Layout();
                var children = root.Children.Cast<UIElement>().ToArray(); var cards = Assert.IsType<StackPanel>(children[1]); var secondary = Assert.IsType<ScrollViewer>(children[2]); var footer = Assert.IsType<StackPanel>(children[3]);
                var quotaCards = cards.Children.OfType<GroupBox>().ToArray();
                var claudeSection = Assert.IsType<StackPanel>(window.FindName("ClaudeQuotaSection"));
                Assert.Equal(Visibility.Collapsed, claudeSection.Visibility);
                // A collapsed section is never measured, so count its meters in the logical tree.
                static IEnumerable<object> Logical(object node) => node is DependencyObject parent
                    ? LogicalTreeHelper.GetChildren(parent).Cast<object>().SelectMany(child => Logical(child).Prepend(child))
                    : Enumerable.Empty<object>();
                Assert.Equal(2, Logical(claudeSection).OfType<ProgressBar>().Count());
                var progress = quotaCards.SelectMany(Visuals<ProgressBar>).ToArray(); var refresh = Visuals<Button>(footer).Single(button => AutomationProperties.GetName(button) == "Refresh quota"); var expanders = Visuals<Expander>(secondary).Where(item => item != details).ToArray();
                Assert.Equal(new[] { 0, 1, 2, 3 }, children.Select(Grid.GetRow)); Assert.Equal(10, expanders.Length); Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(root));
                Assert.Equal(rows.Select(row => row.AutomationLabel), expanders.Select(AutomationProperties.GetName)); Assert.All(expanders.Cast<Control>().Append(refresh), control => { Assert.True(control.Focusable && control.IsTabStop); Assert.NotNull(control.FocusVisualStyle); });
                Assert.Equal(ScrollBarVisibility.Auto, secondary.VerticalScrollBarVisibility); Assert.True(secondary.ExtentHeight > secondary.ViewportHeight && secondary.ScrollableHeight > 0); secondary.ScrollToEnd(); Layout(); Assert.True(secondary.VerticalOffset > 0); secondary.ScrollToTop();
                foreach (var theme in Enum.GetValues<WindowsTheme>())
                {
                    app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme); Layout();
                    Assert.Same(root, window.Content); Assert.Equal(children, root.Children.Cast<UIElement>()); Assert.Equal(rows.Select(row => row.AutomationLabel), expanders.Select(AutomationProperties.GetName)); Assert.All(expanders.Cast<Control>().Append(refresh), control => Assert.NotNull(control.FocusVisualStyle)); Assert.IsAssignableFrom<Brush>(window.TryFindResource("SurfaceBrush")); Assert.Contains($"Semantic.{theme}.xaml", app.Resources.MergedDictionaries[0].Source!.OriginalString, StringComparison.Ordinal);
                    Assert.InRange(root.ActualWidth, 1, 420); Assert.InRange(root.ActualHeight, 1, 720);
                    foreach (var item in children.Cast<FrameworkElement>()) { var bounds = item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize)); Assert.True(item.ActualHeight > 0 && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= root.ActualWidth + .1 && bounds.Bottom <= root.ActualHeight + .1); }
                    Assert.Equal(new[] { "Weekly quota card", "5-hour quota card" }, quotaCards.Select(card => new GroupBoxAutomationPeer(card).GetName())); Assert.All(quotaCards, card => Assert.Contains("percentage, reset, and state", new GroupBoxAutomationPeer(card).GetHelpText(), StringComparison.Ordinal));
                    Assert.Equal(new[] { 73d, 42d }, progress.Select(item => item.Value)); Assert.Equal(new[] { "Weekly quota progress", "5-hour quota progress" }, progress.Select(item => new ProgressBarAutomationPeer(item).GetName())); Assert.All(progress, item => { Assert.False(item.Focusable || item.IsHitTestVisible); Assert.Equal(item.Value, Assert.IsAssignableFrom<System.Windows.Automation.Provider.IRangeValueProvider>(new ProgressBarAutomationPeer(item).GetPattern(PatternInterface.RangeValue)).Value); });
                    var cardText = quotaCards.SelectMany(Visuals<TextBlock>).ToArray(); static string Rendered(TextBlock item) => new System.Windows.Documents.TextRange(item.ContentStart, item.ContentEnd).Text; Assert.Equal(new[] { "Resets in 06d 00h", "Resets in 1h 0m" }, cardText.Select(Rendered).Where(text => text.StartsWith("Resets in ", StringComparison.Ordinal))); Assert.DoesNotContain(cardText, item => Rendered(item) == "Stale");
                    var freshness = Assert.Single(Visuals<TextBlock>(children[0]).Where(item => new TextBlockAutomationPeer(item).GetName() == "Stale"));
                    Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(freshness));
                    Assert.All(quotaCards, card => Assert.Equal("Stale", new GroupBoxAutomationPeer(card).GetItemStatus()));
                    Assert.Equal("Refresh quota", new ButtonAutomationPeer(refresh).GetName()); var statusText = Visuals<TextBlock>(cards).Where(item => item.Visibility == Visibility.Visible && item.ActualHeight > 0).Select(item => new TextBlockAutomationPeer(item).GetName()).ToArray(); Assert.Contains("Cached 00h 05m", statusText); Assert.Contains("Network unavailable", statusText); Assert.True(secondary.ScrollableHeight > 0);
                    var image = new RenderTargetBitmap(420, 720, 96, 96, PixelFormats.Pbgra32); image.Render(root); var pixels = new byte[420 * 720 * 4]; image.CopyPixels(pixels, 420 * 4, 0); Assert.Contains(pixels.Where((_, index) => index % 4 == 3), alpha => alpha > 0);
                }
            }
            finally
            {
                try { window?.Close(); }
                finally { app.Resources.MergedDictionaries[0] = originalTheme; }
            }
        });
    }

    [Fact]
    public void Unit_1a_rendered_popup_omits_absent_quota_cards_from_layout_and_automation()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            foreach (var scenario in new[] { (Primary: false, Weekly: true), (Primary: true, Weekly: false), (Primary: true, Weekly: true), (Primary: false, Weekly: false) })
            {
                MainWindow? window = null;
                try
                {
                    var state = new QuotaAvailabilityFixture(scenario.Primary, scenario.Weekly);
                    window = new MainWindow
                    {
                        Width = 420,
                        Height = 720,
                        SizeToContent = SizeToContent.Manual,
                        DataContext = new
                        {
                            IsRefreshAvailable = true,
                            FreshnessLabel = scenario.Primary || scenario.Weekly ? "Current" : "Unavailable",
                            Primary = new QuotaCardFixture("5-hour", 12m, "01h 00m"),
                            Weekly = new QuotaCardFixture("Weekly", 25m, "06d 00h"),
                            State = state,
                            PrivateEndpointDisclosure = "Private endpoint disclosure",
                            Analytics = new { SourceDetail = "Local", TotalTokensLabel = "0", ModelTotalsLabel = "", CoverageLabel = "", LastScanLabel = "", WarningLabel = "", IsLoading = false },
                            LocalUsage = new { Description = "Local usage", StatusLabel = "Unavailable", Rows = Array.Empty<object>() }
                        }
                    };
                    window.Show();
                    var root = Assert.IsType<Grid>(window.Content);
                    root.Measure(new Size(420, 720)); root.Arrange(new Rect(0, 0, 420, 720)); root.UpdateLayout();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); root.UpdateLayout();
                    var cards = Assert.IsType<StackPanel>(root.Children[1]);
                    var quotaCards = cards.Children.OfType<GroupBox>().ToArray();
                    Assert.Equal(new[] { "Weekly quota card", "5-hour quota card" }, quotaCards.Select(card => AutomationProperties.GetName(card)));
                    Assert.Equal(new[] { scenario.Weekly ? Visibility.Visible : Visibility.Collapsed, scenario.Primary ? Visibility.Visible : Visibility.Collapsed }, quotaCards.Select(card => card.Visibility));
                    Assert.All(quotaCards.Where(card => card.Visibility == Visibility.Collapsed), card => { Assert.False(card.IsVisible); Assert.Equal(0, card.ActualHeight); });
                    var automationRoot = AutomationElement.FromHandle(new WindowInteropHelper(window).EnsureHandle());
                    var automationCards = new[] { "Weekly quota card", "5-hour quota card" }.Where(name => automationRoot.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) is not null).ToArray();
                    var expectedAutomationCards = new[] { (scenario.Weekly, "Weekly quota card"), (scenario.Primary, "5-hour quota card") }.Where(item => item.Item1).Select(item => item.Item2).ToArray();
                    Assert.Equal(expectedAutomationCards, automationCards);
                    var visibleText = Visuals<TextBlock>(cards).Where(text => text.IsVisible).Select(text => new TextBlockAutomationPeer(text).GetName()).ToArray();
                    if (!scenario.Primary) Assert.DoesNotContain(visibleText, name => name.Contains("5-hour", StringComparison.Ordinal));
                    if (!scenario.Weekly) Assert.DoesNotContain(visibleText, name => name.Contains("Weekly", StringComparison.Ordinal));
                }
                finally
                {
                    window?.Close();
                }
            }
        });
    }

    [Fact]
    public void Unit_4b_surface_hints_are_isolated_and_always_restore_an_opaque_base()
    {
        var light = new FakeSurface(); var lightHints = new FakeHints(); PopupSurface.Apply(light, WindowsTheme.Light, lightHints);
        Assert.Equal("opaque", light.Calls[0]); Assert.Equal(0, lightHints.DarkCalls); Assert.True(light.Corners && light.Backdrop);
        var darkFailure = new FakeSurface(); var darkHints = new FakeHints(failDark: true); PopupSurface.Apply(darkFailure, WindowsTheme.Dark, darkHints);
        Assert.Equal("opaque", darkFailure.Calls[0]); Assert.Equal(1, darkHints.DarkCalls); Assert.True(darkFailure.Corners && darkFailure.Backdrop);
        var cornerFailure = new FakeSurface(); PopupSurface.Apply(cornerFailure, WindowsTheme.Dark, new FakeHints(failCorners: true)); Assert.True(cornerFailure.Dark && cornerFailure.Backdrop);
        var backdropFailure = new FakeSurface(); PopupSurface.Apply(backdropFailure, WindowsTheme.Dark, new FakeHints(failBackdrop: true)); Assert.True(backdropFailure.Dark && backdropFailure.Corners);
        foreach (var (theme, hints) in new[] { (WindowsTheme.Dark, new FakeHints(supports: false)), (WindowsTheme.Dark, new FakeHints(remote: true)), (WindowsTheme.HighContrast, new FakeHints()) })
        {
            var surface = new FakeSurface(); PopupSurface.Apply(surface, theme, hints);
            Assert.Equal(new[] { "opaque" }, surface.Calls); Assert.Equal(0, hints.DarkCalls + hints.CornerCalls + hints.BackdropCalls);
        }
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); if (child is T typed) yield return typed;
            foreach (var descendant in Visuals<T>(child)) yield return descendant;
        }
    }

    private sealed class QuotaCardFixture(string label, decimal? percentageUsed, string resetCountdown) { public string Label { get; set; } = label; public decimal? PercentageUsed { get; set; } = percentageUsed; public string ResetCountdown { get; set; } = resetCountdown; }
    private sealed class QuotaAvailabilityFixture(bool primary, bool weekly)
    {
        public string CachedAgeLabel { get; } = ""; public string WarningLabel { get; } = ""; public bool IsLoading { get; } = false;
        public bool IsMissingCredential { get; } = false; public bool IsOffline { get; } = false; public bool IsDegraded { get; } = false;
        public bool IsUnavailable { get; } = !primary && !weekly; public bool IsPrimaryAvailable { get; } = primary; public bool IsWeeklyAvailable { get; } = weekly;
        public bool IsPrivateIntegrationDisabled { get; } = false; public bool IsSafeError { get; } = false; public string Disclosure { get; } = "Quota disclosure";
    }

    private sealed class FakeSurface : IOpaquePopupSurface
    {
        public List<string> Calls { get; } = []; public bool Dark { get; private set; } public bool Corners { get; private set; } public bool Backdrop { get; private set; }
        public void UseOpaqueBase() => Calls.Add("opaque"); public void UseTranslucentBase() => Calls.Add("translucent"); public void SetDarkMode() { Dark = true; Calls.Add("dark"); } public void SetCorners() { Corners = true; Calls.Add("corners"); } public void SetBackdrop() { Backdrop = true; Calls.Add("backdrop"); }
    }

    private sealed class FakeHints(bool supports = true, bool remote = false, bool failDark = false, bool failCorners = false, bool failBackdrop = false) : IDwmSurfaceHints
    {
        public int DarkCalls { get; private set; } public int CornerCalls { get; private set; } public int BackdropCalls { get; private set; }
        public bool SupportsHints => supports; public bool IsRemoteSession => remote; public bool IsTransparencyEnabled => true;
        public void ApplyDarkMode(IOpaquePopupSurface surface) { DarkCalls++; if (failDark) throw new InvalidOperationException(); surface.SetDarkMode(); }
        public void ApplyCorners(IOpaquePopupSurface surface) { CornerCalls++; if (failCorners) throw new InvalidOperationException(); surface.SetCorners(); }
        public void ApplyBackdrop(IOpaquePopupSurface surface) { BackdropCalls++; if (failBackdrop) throw new InvalidOperationException(); surface.SetBackdrop(); }
    }
}
