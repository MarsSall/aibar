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

/// <summary>Approved panel identity: status-colored meters, word flags, compact resets,
/// source pills and a translucent surface that falls back to opaque.</summary>
public sealed class PanelIdentityTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, PanelTone.Ok)]
    [InlineData(0d, PanelTone.Ok)]
    [InlineData(74.99d, PanelTone.Ok)]
    [InlineData(75d, PanelTone.Near)]
    [InlineData(89.99d, PanelTone.Near)]
    [InlineData(90d, PanelTone.Full)]
    [InlineData(100d, PanelTone.Full)]
    public void Meter_tone_is_green_below_75_yellow_from_75_and_red_from_90(double? usage, PanelTone expected)
    {
        Assert.Equal(expected, PanelPalette.Tone((decimal?)usage, isCurrent: true));
        Assert.Equal(PanelTone.Muted, PanelPalette.Tone((decimal?)usage, isCurrent: false));
    }

    [Theory]
    [InlineData(PanelTone.Ok, true, "#3DDC84")]
    [InlineData(PanelTone.Near, true, "#FFC531")]
    [InlineData(PanelTone.Full, true, "#FF4D5E")]
    [InlineData(PanelTone.Muted, true, "#A1A2A8")]
    [InlineData(PanelTone.Ok, false, "#15924A")]
    [InlineData(PanelTone.Near, false, "#A86B00")]
    [InlineData(PanelTone.Full, false, "#C8203C")]
    [InlineData(PanelTone.Muted, false, "#66676D")]
    public void Meter_tones_use_the_approved_colors_for_each_theme(PanelTone tone, bool dark, string expected) =>
        Assert.Equal(Parse(expected), PanelPalette.ToneColor(tone, dark));

    [Theory]
    [InlineData(null, true, null)]
    [InlineData(74.99d, true, null)]
    [InlineData(75d, true, "High usage")]
    [InlineData(89.99d, true, "High usage")]
    [InlineData(90d, true, "Near limit")]
    [InlineData(100d, true, "Near limit")]
    [InlineData(92d, false, null)]
    public void Alert_flags_name_the_condition_in_words_only_for_current_values(double? usage, bool isCurrent, string? expected) =>
        Assert.Equal(expected, PanelPalette.FlagText((decimal?)usage, isCurrent));

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
    [InlineData(true, false, "#BD232428")]
    [InlineData(false, false, "#FF232428")]
    [InlineData(true, true, "#C2F6F6F8")]
    [InlineData(false, true, "#FFF6F6F8")]
    public void Surface_tint_is_translucent_only_with_acrylic(bool translucent, bool light, string expected) =>
        Assert.Equal(Parse(expected), PanelPalette.Surface(dark: !light, translucent));

    [Theory]
    [InlineData(WindowsTheme.Dark, true, 0xBD)]
    [InlineData(WindowsTheme.Dark, false, 0xFF)]
    [InlineData(WindowsTheme.Light, true, 0xC2)]
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
    public void Rendered_panel_shows_identity_flags_pills_and_compact_resets()
    {
        WithPanel(WindowsTheme.Dark, 78, 92, ClaudeQuotaSource.OtherMachine, window =>
        {
            var text = VisibleText(window).ToArray();
            Assert.Contains("AIBAR", text);
            Assert.Contains("CODEX", text); Assert.Contains("CLAUDE", text);
            Assert.Contains("This PC", text); Assert.Contains("Other machine", text);
            Assert.Contains("High usage", text); Assert.Contains("Near limit", text);
            Assert.Contains("78% used", text); Assert.Contains("92% used", text);
            Assert.Contains("Resets in 3d 20h", text); Assert.Contains("Resets in 3h 53m", text);
            var titles = Visuals<TextBlock>(window).Where(item => item.IsVisible && item.Text is "CODEX" or "CLAUDE").ToArray();
            Assert.Equal(new[] { "Codex", "Claude" }, titles.Select(AutomationProperties.GetName));
            var claudeBars = Visuals<ProgressBar>(window).Where(bar => bar.IsVisible && AutomationProperties.GetName(bar).StartsWith("Claude", StringComparison.Ordinal)).ToArray();
            Assert.Equal(new[] { Parse("#FFC531"), Parse("#FF4D5E") }, claudeBars.Select(bar => Assert.IsAssignableFrom<SolidColorBrush>(bar.Foreground).Color));
            var codexBar = Visuals<ProgressBar>(window).First(bar => bar.IsVisible && AutomationProperties.GetName(bar) == "Weekly quota progress");
            Assert.Equal(Parse("#3DDC84"), Assert.IsAssignableFrom<SolidColorBrush>(codexBar.Foreground).Color);
        });
    }

    [Fact]
    public void Rendered_panel_without_alerts_shows_no_flags_and_this_pc_for_local_Claude()
    {
        WithPanel(WindowsTheme.Light, 14, 7, ClaudeQuotaSource.ThisPc, window =>
        {
            var text = VisibleText(window).ToArray();
            Assert.DoesNotContain("High usage", text); Assert.DoesNotContain("Near limit", text);
            Assert.Equal(2, text.Count(item => item == "This PC"));
            Assert.DoesNotContain("Other machine", text);
            var bars = Visuals<ProgressBar>(window).Where(bar => bar.IsVisible).ToArray();
            Assert.All(bars, bar => Assert.Equal(Parse("#15924A"), Assert.IsAssignableFrom<SolidColorBrush>(bar.Foreground).Color));
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
