using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using TextBlock = System.Windows.Controls.TextBlock;
using Expander = System.Windows.Controls.Expander;
using Border = System.Windows.Controls.Border;
using SystemColors = System.Windows.SystemColors;

namespace AIBar.Domain.Tests;

public sealed class PanelPaceTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Details_show_pace_guide_notices_and_accessible_rows(WindowsTheme theme)
    {
        WithPanel(theme, true, true, window =>
        {
            var details = (Expander)window.FindName("AnalyticsDetailsExpander");
            Assert.False(details.IsExpanded);
            Assert.Equal("Pace & details", Assert.IsType<TextBlock>(details.Header).Text);
            Assert.Equal("Local analytics details", AutomationProperties.GetName(details));
            Assert.True(details.Focusable);
            details.IsExpanded = true; window.UpdateLayout();
            var rows = Visuals<PanelPaceRow>(window).Where(row => row.IsVisible).ToArray();
            Assert.Equal(new[] { "Codex weekly", "Codex 5-hour", "Claude weekly", "Claude 5-hour" }, rows.Select(row => row.Data.Label));
            Assert.Equal("Pace 0.7x · lasts until reset", rows[0].Data.Text);
            Assert.Equal("Pace 1.4x · runs out in 1h 4m", rows[1].Data.Text);
            Assert.Equal(PanelTone.Muted, rows[0].Data.Tone);
            Assert.Equal(PanelTone.Near, rows[1].Data.Tone);
            foreach (var row in rows)
            {
                Assert.Equal(row.Data.Label + " pace", AutomationProperties.GetName(row));
                Assert.Equal(row.Data.Label + ": " + row.Data.Text, AutomationProperties.GetHelpText(row));
                var text = Visuals<TextBlock>(row).Single(item => item.Text == row.Data.Text);
                var brush = Assert.IsAssignableFrom<SolidColorBrush>(text.Foreground);
                Assert.Equal(theme == WindowsTheme.HighContrast ? SystemColors.WindowTextBrush.Color :
                    PanelPalette.ToneColor(row.Data.Tone, theme == WindowsTheme.Dark), brush.Color);
                Assert.Equal(50m, row.Data.ExpectedPercentage);
                var marker = Visuals<Border>(row).Single(item => AutomationProperties.GetName(item) == "Even-use guide");
                Assert.Equal(2d, marker.Width);
                var track = Assert.IsType<System.Windows.Controls.Grid>(row.FindName("PaceTrack"));
                Assert.Equal(track.ActualWidth / 2, marker.Margin.Left, 2);
            }
            var notice = Visuals<TextBlock>(window).Single(item => item.IsVisible && item.Text == "At this pace, Claude 5-hour runs out before it resets.");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(notice));
        });
    }

    [Fact]
    public void Disabled_Claude_has_no_visible_rows()
    {
        WithPanel(WindowsTheme.Dark, false, true, window =>
        {
            ((Expander)window.FindName("AnalyticsDetailsExpander")).IsExpanded = true; window.UpdateLayout();
            Assert.Equal(2, Visuals<PanelPaceRow>(window).Count(row => row.IsVisible));
            Assert.DoesNotContain(Visuals<PanelPaceRow>(window).Where(row => row.IsVisible), row => row.Data.Label.StartsWith("Claude"));
        });
    }

    [Fact]
    public void Cached_rows_are_muted_and_have_no_notice()
    {
        WithPanel(WindowsTheme.Dark, true, false, window =>
        {
            ((Expander)window.FindName("AnalyticsDetailsExpander")).IsExpanded = true; window.UpdateLayout();
            Assert.All(Visuals<PanelPaceRow>(window).Where(row => row.IsVisible), row =>
            {
                Assert.Equal("Not current", row.Data.Text);
                Assert.Equal(PanelTone.Muted, row.Data.Tone);
                Assert.Null(row.Data.Notice);
            });
            Assert.DoesNotContain(Visuals<TextBlock>(window).Where(item => item.IsVisible), item => item.Text.StartsWith("At this pace,"));
        });
    }

    [Fact]
    public void Limit_reached_is_red_and_missing_windows_have_no_row()
    {
        WithPanel(WindowsTheme.Dark, true, true, window =>
        {
            ((Expander)window.FindName("AnalyticsDetailsExpander")).IsExpanded = true; window.UpdateLayout();
            var rows = Visuals<PanelPaceRow>(window).Where(row => row.IsVisible).ToArray();
            Assert.Equal(new[] { "Codex 5-hour", "Claude 5-hour" }, rows.Select(row => row.Data.Label));
            Assert.All(rows, row =>
            {
                Assert.Equal("Limit reached", row.Data.Text);
                Assert.Equal(PanelTone.Full, row.Data.Tone);
                var text = Visuals<TextBlock>(row).Single(item => item.Text == "Limit reached");
                Assert.Equal(PanelPalette.ToneColor(PanelTone.Full, true), Assert.IsAssignableFrom<SolidColorBrush>(text.Foreground).Color);
            });
        }, primary: 100, includeWeekly: false);
    }

    private static void WithPanel(WindowsTheme theme, bool enabled, bool current, Action<MainWindow> assert, decimal primary = 70, bool includeWeekly = true)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            MainWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                var state = new QuotaRefreshState(new(new(primary, Now.AddHours(2.5)), includeWeekly ? new(35, Now.AddDays(3.5)) : null, Now),
                    current ? FreshnessState.Current : FreshnessState.Stale, false, null, null);
                var codex = new QuotaPresentationMapper(new Clock()).Map(state);
                var claude = new ClaudeQuotaPresentationMapper(new Clock()).Map(state, enabled);
                window = new MainWindow
                {
                    Width = 420, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
                    DataContext = new
                    {
                        codex.Primary, codex.Weekly, State = codex, codex.FreshnessLabel, codex.PrivateEndpointDisclosure,
                        Claude = new { IsEnabled = enabled, State = claude }, IsRefreshAvailable = true,
                        CopySummaryCommand = new QuotaSummaryCopyCommand(() => codex, _ => { }),
                        RefreshCommand = new ManualRefreshCommand(_ => ValueTask.CompletedTask, () => true),
                        Analytics = new { SourceDetail = "Synthetic", TotalTokensLabel = "0", ModelTotalsLabel = "", CoverageLabel = "", LastScanLabel = "", WarningLabel = "", IsLoading = false },
                        LocalUsage = new { Description = "Synthetic", StatusLabel = "Current", Rows = Array.Empty<object>() }
                    }
                };
                window.Show(); window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout(); assert(window);
            }
            finally { window?.Close(); app.Resources.MergedDictionaries[0] = original; }
        });
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
}
