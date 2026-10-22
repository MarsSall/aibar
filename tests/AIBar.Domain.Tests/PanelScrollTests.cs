using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using Expander = System.Windows.Controls.Expander;
using ScrollViewer = System.Windows.Controls.ScrollViewer;

namespace AIBar.Domain.Tests;

/// <summary>The details scroll bar follows the panel: slim, rounded, no arrow buttons.</summary>
public sealed class PanelScrollTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Details_scroll_bar_is_slim_rounded_and_has_no_arrow_buttons(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            MainWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window = CreateWindow();
                window.Show();
                ((System.Windows.Controls.Button)window.FindName("CostButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var side = Assert.IsType<PanelDetailsWindow>(window.DetailsWindow);
                side.DetailsScrollViewer.MaxHeight = 180; // Both chart sections must scroll in a short viewport; no local-usage rows are needed.
                Settle(side);
                var scroller = side.DetailsScrollViewer;
                Assert.True(scroller.ScrollableHeight > 0);
                var bar = Assert.IsType<ScrollBar>(scroller.Template.FindName("PART_VerticalScrollBar", scroller));
                Assert.Equal(Visibility.Visible, bar.Visibility);
                Assert.InRange(bar.ActualWidth, 1, 8);
                Assert.DoesNotContain(Visuals<RepeatButton>(bar), button => button.IsVisible &&
                    (button.Command == ScrollBar.LineUpCommand || button.Command == ScrollBar.LineDownCommand));
                var thumb = Assert.Single(Visuals<Thumb>(bar));
                var chrome = Assert.Single(Visuals<System.Windows.Controls.Border>(thumb));
                Assert.True(chrome.CornerRadius.TopLeft >= 2);
                // Keyboard and wheel scrolling keep working with the slim bar. Opening Cost focuses Close,
                // and a late window activation can bring that focused button back into view; retry
                // a bounded number of times so only a viewer that cannot scroll fails.
                for (var attempt = 0; attempt < 10 && scroller.VerticalOffset <= 0; attempt++)
                {
                    scroller.ScrollToEnd(); Settle(side);
                }
                Assert.True(scroller.VerticalOffset > 0);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries[0] = original;
            }
        });
    }

    private static MainWindow CreateWindow()
    {
        var clock = new Clock();
        var codex = new QuotaPresentationMapper(clock).Map(new(
            new(new(7, Now.AddHours(3)), new(14, Now.AddDays(3)), Now), FreshnessState.Current, false, null, null));
        var rows = Enumerable.Range(0, 8).Select(index => new
        {
            AutomationLabel = $"Usage row {index}", ScopeLabel = $"Scope {index}", TotalLabel = "165 retained tokens",
            TokenBreakdownLabel = "Input 11 · Output 44", StatusLabel = "Retained history", Details = Array.Empty<object>()
        }).ToArray();
        return new MainWindow
        {
            Width = 360, Height = 420, SizeToContent = SizeToContent.Manual, WindowStyle = WindowStyle.None,
            ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
            DataContext = new
            {
                codex.Primary, codex.Weekly, State = codex, codex.FreshnessLabel, codex.PrivateEndpointDisclosure,
                Claude = new { IsEnabled = false, State = codex },
                IsRefreshAvailable = true,
                CopySummaryCommand = new QuotaSummaryCopyCommand(() => codex, _ => { }),
                RefreshCommand = new ManualRefreshCommand(_ => ValueTask.CompletedTask, () => true),
                Analytics = new { SourceDetail = "Synthetic", TotalTokensLabel = "0", ModelTotalsLabel = "", CoverageLabel = "", LastScanLabel = "", WarningLabel = "", IsLoading = false },
                LocalUsage = new { Description = "Synthetic", StatusLabel = "Current", Rows = rows }
            }
        };
    }

    private static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
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
