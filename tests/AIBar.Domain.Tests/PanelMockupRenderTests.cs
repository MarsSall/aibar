using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

/// <summary>
/// Image fixtures for comparing the native panel with the approved design, not pixel assertions. Without the opt-in directory
/// each case still constructs and lays out the real panel to catch XAML failures.
/// All data and commands are synthetic; no runtime, credential or usage files are opened.
/// </summary>
public sealed class PanelMockupRenderTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("current-dark", WindowsTheme.Dark, 14, 7, true, false, false)]
    [InlineData("current-light", WindowsTheme.Light, 14, 7, true, false, false)]
    [InlineData("alerts-dark", WindowsTheme.Dark, 78, 92, true, false, false)]
    [InlineData("needs-login-dark", WindowsTheme.Dark, 14, 7, true, true, false)]
    [InlineData("stale-dark", WindowsTheme.Dark, 14, 7, true, false, true)]
    [InlineData("codex-only-dark", WindowsTheme.Dark, 14, 7, false, false, false)]
    [InlineData("details-dark", WindowsTheme.Dark, 14, 7, true, false, false)]
    [InlineData("details-light", WindowsTheme.Light, 14, 7, true, false, false)]
    [InlineData("narrow-high-contrast", WindowsTheme.HighContrast, 14, 7, true, false, false)]
    public void Render_panel_identity_mockup(string scenario, WindowsTheme theme, int weekly, int primary,
        bool claudeEnabled, bool needsLogin, bool stale)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var original = app.Resources.MergedDictionaries[0];
            MainWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                var clock = new Clock();
                var codex = new QuotaPresentationMapper(clock).Map(new(
                    new(null, new(1, Now.AddDays(3).AddHours(20)), Now),
                    FreshnessState.Current, false, null, null));
                var claudeSnapshot = new QuotaSnapshot(new(primary, Now.AddHours(3).AddMinutes(53)),
                    new(weekly, Now.AddDays(3).AddHours(20)), stale ? Now.AddMinutes(-18) : Now);
                var claude = new ClaudeQuotaPresentationMapper(clock).Map(needsLogin
                    ? new(null, FreshnessState.Unavailable, false,
                        new(QuotaErrorKind.Authentication, "quota_authentication"), null)
                    : stale
                        ? new(claudeSnapshot, FreshnessState.Stale, false, new(QuotaErrorKind.Unavailable, "remote_stale"), null)
                        : new(claudeSnapshot, FreshnessState.Current, false, null, null),
                    claudeEnabled, ClaudeQuotaSource.OtherMachine);
                window = new MainWindow
                {
                    Width = scenario.StartsWith("narrow", StringComparison.Ordinal) ? 300 : 360, SizeToContent = SizeToContent.Height,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
                    DataContext = new
                    {
                        codex.Primary, codex.Weekly, State = codex, codex.FreshnessLabel,
                        codex.PrivateEndpointDisclosure,
                        Claude = new { IsEnabled = claudeEnabled, State = claude },
                        IsRefreshAvailable = true,
                        CopySummaryCommand = new QuotaSummaryCopyCommand(() => codex, _ => { }),
                        RefreshCommand = new ManualRefreshCommand(_ => ValueTask.CompletedTask, () => true),
                        Analytics = new
                        {
                            SourceDetail = "Synthetic local data", TotalTokensLabel = "0 tokens",
                            ModelTotalsLabel = "", CoverageLabel = "", LastScanLabel = "",
                            WarningLabel = "", IsLoading = false,
                            UsageCost = new { TodayLabel = "Today: $0.04 · 15K tokens", Last30DaysLabel = "Last 30 days: $12.80 · 9.4M tokens" }
                        },
                        ClaudeCost = new { State = new { TodayLabel = "Today: $38.17 · 264M tokens", Last30DaysLabel = "Last 30 days: $254.24 · 918M tokens (partial)" } },
                        LocalUsage = new { Description = "Synthetic local usage", StatusLabel = "Current", Rows = Array.Empty<object>() }
                    }
                };
                window.Show();
                // The reference frames intentionally show Claude, including when it
                // has no cache. Do not let highest-usage default selection render Codex
                // instead of the sign-in scenario being compared.
                if (claudeEnabled) window.SelectProvider("Claude");
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                if (scenario.StartsWith("details", StringComparison.Ordinal))
                {
                    ((Button)window.FindName("CostButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.UpdateLayout();
                }
                SaveRender(window, scenario);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries[0] = original;
            }
        });
    }

    private static void SaveRender(MainWindow window, string scenario)
    {
        var directory = Environment.GetEnvironmentVariable("AIBAR_PANEL_RENDER_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        // Render the template surface, not just Window.Content: this includes the
        // rounded border, real background and AdornerDecorator subtree. DWM acrylic
        // cannot be captured offscreen, so renders use the opaque fallback surface.
        var surface = (Border)window.Template.FindName("PanelSurface", window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),
            (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle(window.Background, null, new Rect(surface.RenderSize));
        bitmap.Render(background);
        bitmap.Render(surface);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, $"panel-mockup-{scenario}-{window.Width:0}.png"));
        png.Save(output);
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Now; }
}
