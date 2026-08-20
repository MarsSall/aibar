using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class PopupRenderedTests
{
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
                var state = new { CachedAgeLabel = "Cached 00h 05m", WarningLabel = "Network unavailable", IsLoading = false, IsMissingCredential = false, IsOffline = false, IsDegraded = true, IsUnavailable = false, IsPrivateIntegrationDisabled = false, IsSafeError = false, Disclosure = "Quota disclosure" };
                window = new MainWindow { Width = 420, Height = 720, SizeToContent = SizeToContent.Manual, DataContext = new { IsRefreshAvailable = true, FreshnessLabel = "Stale", Primary = new QuotaCardFixture("5-hour", 42m, "01h 00m"), Weekly = new QuotaCardFixture("Weekly", 73m, "06d 00h"), State = state, PrivateEndpointDisclosure = "Private endpoint disclosure", Analytics = new { SourceDetail = "Local", TotalTokensLabel = "100", ModelTotalsLabel = "model", CoverageLabel = "complete", LastScanLabel = "now", WarningLabel = "", IsLoading = false }, LocalUsage = new { Description = "Local usage", StatusLabel = "Current", Rows = rows } } };
                var root = Assert.IsType<Grid>(window.Content);
                void Layout() { root.Measure(new Size(420, 720)); root.Arrange(new Rect(0, 0, 420, 720)); root.UpdateLayout(); System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind); root.UpdateLayout(); }
                Layout();
                var children = root.Children.Cast<UIElement>().ToArray(); var cards = Assert.IsType<StackPanel>(children[1]); var secondary = Assert.IsType<ScrollViewer>(children[2]); var footer = Assert.IsType<StackPanel>(children[3]);
                var quotaCards = cards.Children.OfType<GroupBox>().ToArray(); var progress = Visuals<ProgressBar>(cards).ToArray(); var refresh = footer.Children.OfType<Button>().Single(); var expanders = Visuals<Expander>(secondary).ToArray();
                Assert.Equal(new[] { 0, 1, 2, 3 }, children.Select(Grid.GetRow)); Assert.Equal(10, expanders.Length); Assert.Equal(KeyboardNavigationMode.Continue, KeyboardNavigation.GetTabNavigation(root));
                Assert.Equal(rows.Select(row => row.AutomationLabel), expanders.Select(AutomationProperties.GetName)); Assert.All(expanders.Cast<Control>().Append(refresh), control => { Assert.True(control.Focusable && control.IsTabStop); Assert.NotNull(control.FocusVisualStyle); });
                Assert.Equal(ScrollBarVisibility.Auto, secondary.VerticalScrollBarVisibility); Assert.True(secondary.ExtentHeight > secondary.ViewportHeight && secondary.ScrollableHeight > 0); secondary.ScrollToEnd(); Layout(); Assert.True(secondary.VerticalOffset > 0); secondary.ScrollToTop();
                foreach (var theme in Enum.GetValues<WindowsTheme>())
                {
                    app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme); Layout();
                    Assert.Same(root, window.Content); Assert.Equal(children, root.Children.Cast<UIElement>()); Assert.Equal(rows.Select(row => row.AutomationLabel), expanders.Select(AutomationProperties.GetName)); Assert.All(expanders.Cast<Control>().Append(refresh), control => Assert.NotNull(control.FocusVisualStyle)); Assert.IsAssignableFrom<Brush>(window.TryFindResource("SurfaceBrush")); Assert.Contains($"Semantic.{theme}.xaml", app.Resources.MergedDictionaries[0].Source!.OriginalString, StringComparison.Ordinal);
                    Assert.InRange(root.ActualWidth, 1, 420); Assert.InRange(root.ActualHeight, 1, 720);
                    foreach (var item in children.Cast<FrameworkElement>()) { var bounds = item.TransformToAncestor(root).TransformBounds(new Rect(item.RenderSize)); Assert.True(item.ActualHeight > 0 && bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= root.ActualWidth + .1 && bounds.Bottom <= root.ActualHeight + .1); }
                    Assert.Equal(new[] { "5-hour quota card", "Weekly quota card" }, quotaCards.Select(card => new GroupBoxAutomationPeer(card).GetName())); Assert.All(quotaCards, card => Assert.Contains("percentage, reset, and state", new GroupBoxAutomationPeer(card).GetHelpText(), StringComparison.Ordinal));
                    Assert.Equal(new[] { 42d, 73d }, progress.Select(item => item.Value)); Assert.Equal(new[] { "5-hour quota progress", "Weekly quota progress" }, progress.Select(item => new ProgressBarAutomationPeer(item).GetName())); Assert.All(progress, item => { Assert.False(item.Focusable || item.IsHitTestVisible); Assert.Equal(item.Value, Assert.IsAssignableFrom<System.Windows.Automation.Provider.IRangeValueProvider>(new ProgressBarAutomationPeer(item).GetPattern(PatternInterface.RangeValue)).Value); });
                    var cardText = Visuals<TextBlock>(cards).ToArray(); Assert.Equal(new[] { "Reset 01h 00m", "Reset 06d 00h" }, cardText.Where(item => item.Text.StartsWith("Reset ", StringComparison.Ordinal)).Select(item => new TextBlockAutomationPeer(item).GetName())); Assert.Equal(2, cardText.Count(item => new TextBlockAutomationPeer(item).GetName() == "Stale"));
                    Assert.Equal("Refresh quota", new ButtonAutomationPeer(refresh).GetName()); var footerText = Visuals<TextBlock>(footer).Where(item => item.Visibility == Visibility.Visible && item.ActualHeight > 0).Select(item => new TextBlockAutomationPeer(item).GetName()).ToArray(); Assert.Contains("Cached 00h 05m", footerText); Assert.Contains("Network unavailable", footerText); Assert.True(secondary.ScrollableHeight > 0);
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

    private sealed class FakeSurface : IOpaquePopupSurface
    {
        public List<string> Calls { get; } = []; public bool Dark { get; private set; } public bool Corners { get; private set; } public bool Backdrop { get; private set; }
        public void UseOpaqueBase() => Calls.Add("opaque"); public void SetDarkMode() { Dark = true; Calls.Add("dark"); } public void SetCorners() { Corners = true; Calls.Add("corners"); } public void SetBackdrop() { Backdrop = true; Calls.Add("backdrop"); }
    }

    private sealed class FakeHints(bool supports = true, bool remote = false, bool failDark = false, bool failCorners = false, bool failBackdrop = false) : IDwmSurfaceHints
    {
        public int DarkCalls { get; private set; } public int CornerCalls { get; private set; } public int BackdropCalls { get; private set; }
        public bool SupportsHints => supports; public bool IsRemoteSession => remote;
        public void ApplyDarkMode(IOpaquePopupSurface surface) { DarkCalls++; if (failDark) throw new InvalidOperationException(); surface.SetDarkMode(); }
        public void ApplyCorners(IOpaquePopupSurface surface) { CornerCalls++; if (failCorners) throw new InvalidOperationException(); surface.SetCorners(); }
        public void ApplyBackdrop(IOpaquePopupSurface surface) { BackdropCalls++; if (failBackdrop) throw new InvalidOperationException(); surface.SetBackdrop(); }
    }
}
