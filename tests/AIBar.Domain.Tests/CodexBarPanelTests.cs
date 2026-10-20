using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class CodexBarPanelTests
{
    [Theory]
    [InlineData(WindowsTheme.Light, 300)]
    [InlineData(WindowsTheme.Light, 360)]
    [InlineData(WindowsTheme.Dark, 300)]
    [InlineData(WindowsTheme.Dark, 360)]
    [InlineData(WindowsTheme.HighContrast, 300)]
    [InlineData(WindowsTheme.HighContrast, 360)]
    public void Cost_navigation_is_keyboard_accessible_and_menu_stays_available(WindowsTheme theme, int width)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var previous = app.Resources.MergedDictionaries[0];
            MainWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                var primary = new QuotaWindowPresentation("5-hour quota", 37m, "02h 13m");
                var weekly = new QuotaWindowPresentation("Weekly quota", 31m, "00h 56m");
                window = new MainWindow
                {
                    Width = width, Height = 720, SizeToContent = SizeToContent.Manual,
                    DataContext = new
                    {
                        Primary = primary, Weekly = weekly, FreshnessLabel = "Current", IsRefreshAvailable = true,
                        State = new { IsCurrent = true, IsPrimaryAvailable = true, IsWeeklyAvailable = true, UpdatedLabel = "Updated just now" },
                        Claude = new { IsEnabled = true, State = new { Primary = primary, Weekly = weekly, IsCurrent = true, IsPrimaryAvailable = true, IsWeeklyAvailable = true, UpdatedLabel = "Updated 2m ago", SourceLabel = "Other machine" } },
                        Analytics = new { UsageCost = new { TodayLabel = "Today: $0.04 · 15K tokens", Last30DaysLabel = "Last 30 days: $12.80+ · 9.4M tokens (some models unpriced)" } },
                        ClaudeCost = new { State = new { TodayLabel = "Today: unavailable", Last30DaysLabel = "Last 30 days: $254.24 · 918M tokens (partial)" } }
                    }
                };
                window.Show(); window.UpdateLayout();
                window.SelectProvider("Claude"); window.UpdateLayout();
                Assert.True(((StackPanel)window.FindName("ClaudeQuotaSection")).IsVisible);
                Assert.Contains(Visuals<TextBlock>(window), text => text.IsVisible && text.Text == "unavailable");
                Assert.Contains(Visuals<TextBlock>(window), text => text.IsVisible && text.Text == "$ 254.24");
                Assert.Contains(Visuals<TextBlock>(window), text => text.IsVisible && text.Text == "30 days · 918M tokens (partial)");
                window.SelectProvider("Codex"); window.UpdateLayout();
                var titles = Visuals<TextBlock>(window).Where(text => text.Text is "Codex" or "Claude").ToArray();
                Assert.Contains(titles, text => text.Text == "Codex");
                Assert.Contains(titles, text => text.Text == "Claude");
                Assert.Equal("Codex", window.SelectedProvider);
                Assert.False(((StackPanel)window.FindName("ClaudeQuotaSection")).IsVisible);
                var heading = Visuals<TextBlock>(window).Single(text => text.IsVisible && AutomationProperties.GetName(text) == "Codex");
                // Reference is 2x: the 28px heading is 14 DIPs, not the former 21-DIP mockup title.
                // S10 specifies a 16/22 semibold provider title.
                Assert.Equal(16d, heading.FontSize); Assert.Equal(FontWeights.SemiBold, heading.FontWeight);
                Assert.Equal(new[] { "5-hour quota progress", "Weekly quota progress", "Claude 5-hour quota progress", "Claude weekly quota progress" }, Visuals<PanelMeter>(window).Select(meter => meter.MeterName));
                Assert.All(Visuals<PanelMeter>(window).SelectMany(Visuals<ProgressBar>), bar =>
                {
                    Assert.Equal(4d, bar.Height); // S10 usage bars are four DIPs.
                    Assert.Equal(theme == WindowsTheme.HighContrast ? System.Windows.SystemColors.HighlightColor :
                        PanelPalette.ProviderColor(AutomationProperties.GetName(bar).StartsWith("Claude", StringComparison.Ordinal) ? "Claude" : "Codex"),
                        Assert.IsType<SolidColorBrush>(bar.Foreground).Color);
                });
                var texts = Visuals<TextBlock>(window).Select(text => text.Text).ToArray();
                Assert.Contains("Updated 2 min ago · from other machine", texts);
                Assert.Contains("SESSION · 5 H", texts); Assert.Contains("WEEKLY · ALL MODELS", texts);
                // S10 splits amount/meta into two columns; every exact value and partial marker stays asserted.
                Assert.Contains("$ 0.04", texts); Assert.Contains("Today · 15K tokens", texts);
                Assert.Contains("$ 12.80+", texts); Assert.Contains("30 days · 9.4M tokens (some models unpriced)", texts);
                Assert.Contains("unavailable", texts);
                Assert.Contains("$ 254.24", texts); Assert.Contains("30 days · 918M tokens (partial)", texts);
                var cost = Assert.IsType<Button>(window.FindName("CostButton"));
                var back = Assert.IsType<Button>(window.FindName("BackButton"));
                var summary = Assert.IsType<StackPanel>(window.FindName("SummaryView"));
                var detail = Assert.IsType<StackPanel>(window.FindName("DetailView"));
                Assert.Equal(Visibility.Collapsed, detail.Visibility);
                Assert.Equal("Cost details", AutomationProperties.GetName(cost));
                foreach (var key in new[] { Key.Enter, Key.Space })
                {
                    cost.Focus();
                    cost.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
                        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, summary.Visibility);
                    Assert.Equal(Visibility.Visible, detail.Visibility);
                    Assert.True(back.IsKeyboardFocused);
                    Assert.Contains(Visuals<TextBlock>(detail), text => text.IsVisible && text.Text == "Usage history");
                    Assert.Single(Visuals<QuotaHistoryChart>(detail));
                    Invoke(back); window.UpdateLayout();
                    Assert.Equal(Visibility.Visible, summary.Visibility);
                    Assert.True(cost.IsKeyboardFocused);
                }
                Invoke(cost); window.UpdateLayout();
                Assert.Equal(Visibility.Visible, detail.Visibility);
                back.Focus();
                back.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Space)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                window.UpdateLayout(); Assert.Equal(Visibility.Collapsed, detail.Visibility);
                var menu = Assert.IsType<StackPanel>(window.FindName("PanelActions"));
                var buttons = menu.Children.OfType<Button>().ToArray();
                Assert.Equal(new[] { "Usage Dashboard", "Status Page", "Refresh quota", "Copy quota summary", "Settings", "About AIBar", "Quit AIBar" }, buttons.Select(AutomationProperties.GetName));
                Assert.All(buttons, button =>
                {
                    Assert.True(button.Focusable && button.IsTabStop); Assert.NotNull(button.FocusVisualStyle);
                    var bounds = button.TransformToAncestor(window).TransformBounds(new Rect(button.RenderSize));
                    Assert.InRange(bounds.Left, 0, window.ActualWidth);
                    Assert.True(bounds.Right <= window.ActualWidth && bounds.Bottom <= window.ActualHeight);
                });
                var settings = 0; var quit = 0;
                window.SettingsRequested += () => settings++;
                window.QuitRequested += () => quit++;
                Invoke(buttons[4]); Invoke(buttons[6]);
                Assert.Equal(1, settings); Assert.Equal(1, quit);
                Assert.DoesNotContain(Visuals<TextBlock>(window), text => text.Text is "AIBAR" or "Pace & details");
            }
            finally { window?.Close(); app.Resources.MergedDictionaries[0] = previous; }
        });
    }

    [Fact]
    public void Menu_and_dashboard_actions_keep_existing_refresh_copy_and_allowlisted_links()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var links = new List<string>(); var refreshes = 0; string? copied = null;
            var now = DateTimeOffset.UtcNow;
            var state = new QuotaPresentationMapper(new Clock()).Map(new(
                new(null, new(14, now.AddDays(3)), now), AIBar.Domain.FreshnessState.Current, false, null, null));
            var copy = new QuotaSummaryCopyCommand(() => state, text => copied = text);
            var refresh = new ManualRefreshCommand(_ => { refreshes++; return ValueTask.CompletedTask; }, () => true);
            var window = new MainWindow(start => { Assert.True(start.UseShellExecute); links.Add(start.FileName); })
            {
                DataContext = new { State = state, Claude = new { IsEnabled = true }, IsRefreshAvailable = true, CopySummaryCommand = copy, RefreshCommand = refresh }
            };
            try
            {
                window.Show(); window.UpdateLayout();
                var buttons = ((StackPanel)window.FindName("PanelActions")).Children.OfType<Button>().ToArray();
                Invoke(buttons[2]); Assert.Equal(1, refreshes);
                Invoke(buttons[3]); Assert.NotNull(copied); Assert.Contains("14%", copied, StringComparison.Ordinal);
                Assert.DoesNotContain("Claude", copied, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(copy.Feedback));
                window.SelectProvider("Codex"); Invoke(buttons[1]); Invoke(buttons[0]);
                window.SelectProvider("Claude"); Invoke(buttons[1]); Invoke(buttons[0]);
                Assert.Equal(new[] { "https://status.openai.com", "https://chatgpt.com/codex/settings/usage", "https://status.claude.com", "https://claude.ai/settings/usage" }, links);
            }
            finally { window.Close(); }
        });
    }

    private sealed class Clock : AIBar.Domain.IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

    [Theory]
    [InlineData("Codex", "#FF10A37F")]
    [InlineData("Claude", "#FFC4703F")]
    public void Brand_bars_do_not_change_at_thresholds_and_stale_bars_are_grey(string provider, string expected)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var converter = new ProviderVisuals();
            var surface = new SolidColorBrush(Colors.White);
            foreach (var usage in new[] { 0m, 75m, 90m, 100m })
            {
                var current = (SolidColorBrush)converter.Convert(new object[] { usage, surface, surface.Color, true, provider }, typeof(Brush), "Meter", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(expected, current.Color.ToString());
                var stale = (SolidColorBrush)converter.Convert(new object[] { usage, surface, surface.Color, false, provider }, typeof(Brush), "Meter", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(stale.Color.R, stale.Color.G); Assert.Equal(stale.Color.G, stale.Color.B);
            }
            var unavailable = (SolidColorBrush)converter.Convert(new object[] { DependencyProperty.UnsetValue, surface, surface.Color, true, provider }, typeof(Brush), "Meter", System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(unavailable.Color.R, unavailable.Color.G); Assert.Equal(unavailable.Color.G, unavailable.Color.B);
        });
    }

    private static void Invoke(Button button)
    {
        Assert.IsAssignableFrom<IInvokeProvider>(new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
