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
    public void Summary_shows_inline_pace_with_accessible_rows_and_no_second_meter(WindowsTheme theme)
    {
        // Session projections remain available to data consumers even though S7 renders only Weekly pace.
        var state = new QuotaPresentationMapper(new Clock()).Map(new(
            new(new(70, Now.AddHours(2.5)), new(35, Now.AddDays(3.5)), Now), FreshnessState.Current, false, null, null));
        Assert.Equal("Pace: Ahead (+40%) · Runs out in 1h 4m", state.Primary.Pace!.Text);
        Assert.Equal(PanelTone.Near, state.Primary.Pace.Tone);
        WithPanel(theme, true, true, window =>
        {
            Assert.Equal(Visibility.Collapsed, ((System.Windows.Controls.StackPanel)window.FindName("DetailView")).Visibility);
            var rows = Visuals<PanelPaceRow>(window).Where(row => row.IsVisible).ToArray();
            Assert.Equal(new[] { "Codex weekly" }, rows.Select(row => row.Data.Label));
            Assert.Equal("Pace: Behind (−30%) · Lasts to reset", rows[0].Data.Text);
            Assert.Equal(PanelTone.Muted, rows[0].Data.Tone);
            window.SelectProvider("Claude"); window.UpdateLayout();
            Assert.Equal(new[] { "Claude weekly" }, Visuals<PanelPaceRow>(window).Where(row => row.IsVisible).Select(row => row.Data.Label));
            window.SelectProvider("Codex"); window.UpdateLayout();
            foreach (var row in rows)
            {
                Assert.Equal(row.Data.Label + " pace", AutomationProperties.GetName(row));
                Assert.Equal(row.Data.Label + ": " + row.Data.Text, AutomationProperties.GetHelpText(row));
                var text = Visuals<TextBlock>(row).Single(item => item.Text == row.Data.Text);
                var brush = Assert.IsAssignableFrom<SolidColorBrush>(text.Foreground);
                Assert.Equal(theme == WindowsTheme.HighContrast ? SystemColors.WindowTextBrush.Color :
                    PanelPalette.ToneColor(row.Data.Tone, theme == WindowsTheme.Dark), brush.Color);
                Assert.Equal(50m, row.Data.ExpectedPercentage);
                Assert.DoesNotContain(Visuals<Border>(row), item => AutomationProperties.GetName(item) == "Even-use guide");
            }
            Assert.All(rows, row => Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(row)));
        });
    }

    [Fact]
    public void Disabled_Claude_has_no_visible_rows()
    {
        WithPanel(WindowsTheme.Dark, false, true, window =>
        {
            window.UpdateLayout();
            Assert.Single(Visuals<PanelPaceRow>(window).Where(row => row.IsVisible));
            Assert.DoesNotContain(Visuals<PanelPaceRow>(window).Where(row => row.IsVisible), row => row.Data.Label.StartsWith("Claude"));
        });
    }

    [Fact]
    public void Cached_rows_are_muted_and_have_no_notice()
    {
        WithPanel(WindowsTheme.Dark, true, false, window =>
        {
            window.UpdateLayout();
            Assert.All(Visuals<PanelPaceRow>(window).Where(row => row.IsVisible), row =>
            {
                Assert.Equal("Pace: Not current", row.Data.Text);
                Assert.Equal(PanelTone.Muted, row.Data.Tone);
                Assert.Null(row.Data.Notice);
            });
            Assert.DoesNotContain(Visuals<TextBlock>(window).Where(item => item.IsVisible), item => item.Text.StartsWith("At this pace,"));
        });
    }

    [Fact]
    public void Limit_reached_is_explicit_muted_and_missing_windows_have_no_row()
    {
        WithPanel(WindowsTheme.Dark, true, true, window =>
        {
            window.UpdateLayout();
            var rows = Visuals<PanelPaceRow>(window).Where(row => row.IsVisible).ToArray();
            // S7 places pace only below Weekly; absent Weekly means no summary pace.
            Assert.Empty(rows);
            var session = Visuals<PanelMeter>(window).First(meter => meter.IsVisible);
            Assert.Equal(100m, session.Usage);
            Assert.Null(session.Pace);
            window.SelectProvider("Claude"); window.UpdateLayout();
            Assert.Empty(Visuals<PanelPaceRow>(window).Where(row => row.IsVisible));
        }, primary: 100, includeWeekly: false);
        var state = new QuotaPresentationMapper(new Clock()).Map(new(
            new(new(100, Now.AddHours(2.5)), null, Now), FreshnessState.Current, false, null, null));
        Assert.Equal("Pace: Limit reached", state.Primary.Pace!.Text);
        Assert.Equal(PanelTone.Muted, state.Primary.Pace.Tone);
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
