using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace AIBar.Domain.Tests;

/// <summary>CodexBar panel identity: brand meters, inline states, compact resets and acrylic fallback.</summary>
[Collection(WpfTestCollection.Name)]
public sealed class PanelIdentityTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    // S10 replaces the S7 palette; state semantics remain unchanged.
    [InlineData(PanelTone.Near, true, "#F5B83D")]
    [InlineData(PanelTone.Muted, true, "#A9A9B2")]
    [InlineData(PanelTone.Near, false, "#8F5600")]
    [InlineData(PanelTone.Muted, false, "#575757")]
    public void Pace_warning_and_secondary_text_follow_the_mockup(PanelTone tone, bool dark, string expected) =>
        Assert.Equal(Parse(expected), PanelPalette.ToneColor(tone, dark));

    [Theory]
    [InlineData("Codex", "#10A37F")]
    [InlineData("Claude", "#C4703F")]
    public void Provider_brand_colors_are_theme_independent(string provider, string expected) =>
        Assert.Equal(Parse(expected), PanelPalette.ProviderColor(provider));

    [Theory]
    [InlineData("3d 20h 00m", "3d 20h")]
    [InlineData("1d 00h 05m", "1d 0h")]
    [InlineData("12d 07h 59m", "12d 7h")]
    [InlineData("03h 53m", "3h 53m")]
    [InlineData("00h 05m", "5m")]
    [InlineData("00h 00m", "<1m")]
    [InlineData(null, null)]
    [InlineData("unexpected", "unexpected")]
    public void Panel_reset_text_is_compact(string? countdown, string? expected) =>
        Assert.Equal(expected, PanelPalette.CompactReset(countdown));

    [Fact]
    public void Shared_reset_countdown_and_copy_summary_keep_their_existing_format()
    {
        var codex = new QuotaPresentationMapper(new Clock()).Map(new(
            new(null, new(1, Now.AddDays(3).AddHours(20)), Now), FreshnessState.Current, false, null, null));
        Assert.Equal("3d 20h 00m", codex.Weekly.ResetCountdown);
    }

    [Fact]
    public void Claude_source_label_names_where_the_quota_is_read()
    {
        var mapper = new ClaudeQuotaPresentationMapper(new Clock());
        var state = new QuotaRefreshState(new(null, new(14, Now.AddDays(1)), Now), FreshnessState.Current, false, null, null);
        Assert.Equal("This PC", mapper.Map(state, true, ClaudeQuotaSource.ThisPc).SourceLabel);
        Assert.Equal("Other machine", mapper.Map(state, true, ClaudeQuotaSource.OtherMachine).SourceLabel);
    }

    [Theory]
    [InlineData(WindowsTheme.Dark, new[] { "opaque", "dark", "corners", "backdrop", "translucent" })]
    [InlineData(WindowsTheme.Light, new[] { "opaque", "corners", "backdrop", "translucent" })]
    public void Acrylic_is_used_only_after_the_backdrop_succeeds(WindowsTheme theme, string[] expected)
    {
        var surface = new FakeSurface();
        Assert.True(PopupSurface.Apply(surface, theme, new FakeHints()));
        Assert.Equal(expected, surface.Calls);
    }

    [Fact]
    public void Panel_stays_opaque_when_transparency_is_off_unsupported_remote_high_contrast_or_failing()
    {
        var off = new FakeSurface(); var offHints = new FakeHints(transparency: false);
        Assert.False(PopupSurface.Apply(off, WindowsTheme.Dark, offHints));
        Assert.Equal(new[] { "opaque", "dark", "corners" }, off.Calls); Assert.Equal(0, offHints.BackdropCalls);
        var failing = new FakeSurface();
        Assert.False(PopupSurface.Apply(failing, WindowsTheme.Dark, new FakeHints(failBackdrop: true)));
        Assert.DoesNotContain("translucent", failing.Calls);
        foreach (var (theme, hints) in new[] { (WindowsTheme.Dark, new FakeHints(supports: false)), (WindowsTheme.Dark, new FakeHints(remote: true)), (WindowsTheme.HighContrast, new FakeHints()) })
        {
            var surface = new FakeSurface();
            Assert.False(PopupSurface.Apply(surface, theme, hints));
            Assert.Equal(new[] { "opaque" }, surface.Calls);
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData("0", true)]
    public void Windows_transparency_setting_defaults_on_and_reads_the_registry_dword(object? value, bool expected) =>
        Assert.Equal(expected, WindowsTransparencySetting.IsEnabled(value));

    [Theory]
    [InlineData(true, false, "#D90C0C10")]
    [InlineData(false, false, "#FF0C0C10")]
    [InlineData(true, true, "#CCF7F7F7")]
    [InlineData(false, true, "#FFF7F7F7")]
    public void Surface_tint_is_translucent_only_with_acrylic(bool translucent, bool light, string expected) =>
        Assert.Equal(Parse(expected), PanelPalette.Surface(dark: !light, translucent));

    [Theory]
    [InlineData(WindowsTheme.Dark, true, 0xD9)]
    [InlineData(WindowsTheme.Dark, false, 0xFF)]
    [InlineData(WindowsTheme.Light, true, 0xCC)]
    public void Rendered_surface_follows_the_window_translucency(WindowsTheme theme, bool translucent, byte alpha)
    {
        WithPanel(theme, 14, 7, ClaudeQuotaSource.OtherMachine, window =>
        {
            window.IsTranslucent = translucent; window.UpdateLayout();
            var surface = (System.Windows.Controls.Border)window.Template.FindName("PanelSurface", window);
            Assert.Equal(alpha, Assert.IsAssignableFrom<SolidColorBrush>(surface.Background).Color.A);
        });
    }

    [Fact]
    public void Rendered_panel_shows_provider_brands_no_chrome_flags_and_compact_resets()
    {
        WithPanel(WindowsTheme.Dark, 78, 92, ClaudeQuotaSource.OtherMachine, window =>
        {
            var text = VisibleText(window).ToArray();
            Assert.DoesNotContain("AIBAR", text);
            Assert.Contains("Codex", text); Assert.Contains("Claude", text);
            Assert.Contains(text, value => value.Contains("from other machine", StringComparison.Ordinal));
            Assert.DoesNotContain("High usage", text); Assert.DoesNotContain("Near limit", text);
            // S10 places the percent above the bar, with a non-color warning at >=90%.
            Assert.Contains("78%", text); Assert.Contains("⚠ 92%", text);
            Assert.Contains("Resets in 3d 20h", text); Assert.Contains("Resets in 3h 53m", text);
            var titles = Visuals<TextBlock>(window).Where(item => item.IsVisible && item.Text is "Codex" or "Claude").ToArray();
            Assert.Contains(titles, title => AutomationProperties.GetName(title) == "Claude");
            var claudeBars = Visuals<PanelMeter>(window).Where(meter => meter.IsVisible && meter.Provider == "Claude").SelectMany(Visuals<ProgressBar>).ToArray();
            Assert.Equal(new[] { Parse("#C4703F"), Parse("#C4703F") }, claudeBars.Select(bar => Assert.IsAssignableFrom<SolidColorBrush>(bar.Foreground).Color));
            window.SelectProvider("Codex"); window.UpdateLayout();
            Assert.DoesNotContain(Visuals<PanelMeter>(window), meter => meter.IsVisible && meter.Provider == "Claude");
            var codexBar = Visuals<ProgressBar>(window).First(bar => bar.IsVisible && AutomationProperties.GetName(bar) == "Weekly quota progress");
            Assert.Equal(Parse("#10A37F"), Assert.IsAssignableFrom<SolidColorBrush>(codexBar.Foreground).Color);
        });
    }

    [Fact]
    public void Rendered_panel_without_alerts_shows_no_flags_or_remote_suffix_for_local_Claude()
    {
        WithPanel(WindowsTheme.Light, 14, 7, ClaudeQuotaSource.ThisPc, window =>
        {
            var text = VisibleText(window).ToArray();
            Assert.DoesNotContain("High usage", text); Assert.DoesNotContain("Near limit", text);
            Assert.DoesNotContain(text, item => item.Contains("from other machine", StringComparison.Ordinal));
            Assert.DoesNotContain("Other machine", text);
            var bars = Visuals<ProgressBar>(window).Where(bar => bar.IsVisible).ToArray();
            Assert.All(bars, bar => Assert.Equal(Parse(AutomationProperties.GetName(bar).StartsWith("Claude", StringComparison.Ordinal) ? "#C4703F" : "#10A37F"), Assert.IsAssignableFrom<SolidColorBrush>(bar.Foreground).Color));
        });
    }

    private static void WithPanel(WindowsTheme theme, int weekly, int primary, ClaudeQuotaSource source, Action<MainWindow> assert)
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
                    new(null, new(1, Now.AddDays(3).AddHours(20)), Now), FreshnessState.Current, false, null, null));
                var claude = new ClaudeQuotaPresentationMapper(clock).Map(new(
                    new(new(primary, Now.AddHours(3).AddMinutes(53)), new(weekly, Now.AddDays(3).AddHours(20)), Now),
                    FreshnessState.Current, false, null, null), true, source);
                window = new MainWindow
                {
                    Width = 360, SizeToContent = SizeToContent.Height, WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
                    DataContext = new
                    {
                        codex.Primary, codex.Weekly, State = codex, codex.FreshnessLabel, codex.PrivateEndpointDisclosure,
                        Claude = new { IsEnabled = true, State = claude },
                        IsRefreshAvailable = true,
                        CopySummaryCommand = new QuotaSummaryCopyCommand(() => codex, _ => { }),
                        RefreshCommand = new ManualRefreshCommand(_ => ValueTask.CompletedTask, () => true),
                        Analytics = new { SourceDetail = "Synthetic", TotalTokensLabel = "0", ModelTotalsLabel = "", CoverageLabel = "", LastScanLabel = "", WarningLabel = "", IsLoading = false },
                        LocalUsage = new { Description = "Synthetic", StatusLabel = "Current", Rows = Array.Empty<object>() }
                    }
                };
                window.Show();
                window.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                assert(window);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries[0] = original;
            }
        });
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static IEnumerable<string> VisibleText(DependencyObject root) => Visuals<TextBlock>(root).Where(item => item.IsVisible)
        .Select(item => new TextRange(item.ContentStart, item.ContentEnd).Text);
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

    private sealed class FakeSurface : IOpaquePopupSurface
    {
        public List<string> Calls { get; } = [];
        public void UseOpaqueBase() => Calls.Add("opaque");
        public void UseTranslucentBase() => Calls.Add("translucent");
        public void SetDarkMode() => Calls.Add("dark");
        public void SetCorners() => Calls.Add("corners");
        public void SetBackdrop() => Calls.Add("backdrop");
    }

    private sealed class FakeHints(bool supports = true, bool remote = false, bool transparency = true, bool failBackdrop = false) : IDwmSurfaceHints
    {
        public int BackdropCalls { get; private set; }
        public bool SupportsHints => supports; public bool IsRemoteSession => remote; public bool IsTransparencyEnabled => transparency;
        public void ApplyDarkMode(IOpaquePopupSurface surface) => surface.SetDarkMode();
        public void ApplyCorners(IOpaquePopupSurface surface) => surface.SetCorners();
        public void ApplyBackdrop(IOpaquePopupSurface surface) { BackdropCalls++; if (failBackdrop) throw new InvalidOperationException(); surface.SetBackdrop(); }
    }
}
