using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

/// <summary>S10 replaces S9 appearance, not quota/command/accessibility contracts.</summary>
public sealed class ClaudeDesignPanelTests
{
    [Fact]
    public void Sign_in_and_stale_flags_are_derived_without_discarding_cached_quota()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new AIBar.Domain.QuotaSnapshot(new(7, now.AddHours(3)), new(14, now.AddDays(3)), now.AddMinutes(-22));
        var mapper = new QuotaPresentationMapper(new Clock(now));
        var stale = mapper.Map(new(snapshot, AIBar.Domain.FreshnessState.Stale, false, null, null));
        Assert.True(stale.ShowStaleBanner); Assert.False(stale.NeedsLogin);
        Assert.Equal(7m, stale.Primary.PercentageUsed); Assert.Equal(14m, stale.Weekly.PercentageUsed);
        Assert.True(stale.IsPrimaryAvailable && stale.IsWeeklyAvailable);
        var login = mapper.Map(new(snapshot, AIBar.Domain.FreshnessState.Stale, false, new(AIBar.Domain.QuotaErrorKind.Authentication, "quota_authentication"), null));
        Assert.True(login.NeedsLogin); Assert.False(login.ShowStaleBanner);
        Assert.Equal(stale.Primary.PercentageUsed, login.Primary.PercentageUsed);
        Assert.Equal(stale.Weekly.PercentageUsed, login.Weekly.PercentageUsed);
    }

    [Fact]
    public void Background_opacity_is_bounded_and_does_not_dim_content()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new MainWindow();
            try
            {
                Assert.Equal(1d, window.BackgroundOpacity);
                window.BackgroundOpacity = .1;
                Assert.Equal(.3, window.BackgroundOpacity);
                window.BackgroundOpacity = 2;
                Assert.Equal(1d, window.BackgroundOpacity);
                window.BackgroundOpacity = .65;
                Assert.Equal(.65, window.BackgroundOpacity);
                Assert.Equal(1d, window.Opacity);
                Assert.Equal(1d, ((StackPanel)window.FindName("PanelActions")).Opacity);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("5-hour quota", "SESSION · 5 H")]
    [InlineData("Weekly quota", "WEEKLY · ALL MODELS")]
    public void Meter_uses_uppercase_section_and_large_right_aligned_value(string label, string expected)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var meter = new PanelMeter { Label = label, Usage = 92, IsCurrent = true };
            var window = new Window { Content = meter };
            try
            {
                window.Show(); window.UpdateLayout();
                var percent = (TextBlock)meter.FindName("MeterPercent");
                Assert.Equal("⚠ 92%", percent.Text);
                Assert.Equal(22d, percent.FontSize);
                Assert.Equal(HorizontalAlignment.Right, percent.HorizontalAlignment);
                Assert.Equal(expected, ((TextBlock)meter.FindName("MeterLabel")).Text);
                Assert.Equal(4d, ((ProgressBar)meter.FindName("MeterBar")).Height);
                Assert.False(((ProgressBar)meter.FindName("MeterBar")).Focusable);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Today: $0.04 · 15K tokens", "$ 0.04", "Today · 15K tokens")]
    [InlineData("Last 30 days: $12.80+ · 9.4M tokens (some models unpriced)", "$ 12.80+", "30 days · 9.4M tokens (some models unpriced)")]
    [InlineData("Today: unavailable", "unavailable", "Today")]
    public void Split_cost_preserves_values_tokens_and_partial_markers(string input, string amount, string meta)
    {
        var converter = new PanelCostLabelConverter();
        Assert.Equal(amount, converter.Convert(input, typeof(string), "Amount", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(meta, converter.Convert(input, typeof(string), "Meta", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Dark_selected_tab_uses_provider_gradient_and_high_usage_border_override()
    {
        var converter = new PanelTabDecorationConverter();
        object[] values = ["Claude", new SolidColorBrush(Color.FromRgb(12, 12, 16)), 14m, 92m, true, true];
        var fill = Assert.IsType<LinearGradientBrush>(converter.Convert(values, typeof(Brush), "Fill", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal((byte)33, fill.GradientStops[0].Color.A);
        Assert.Equal((byte)8, fill.GradientStops[1].Color.A);
        var border = Assert.IsType<SolidColorBrush>(converter.Convert(values, typeof(Brush), "Border", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(Color.FromArgb(143, 255, 107, 91), border.Color);
    }

    [Fact]
    public void Keyboard_routes_menu_tabs_back_and_existing_commands()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var refreshes = 0; var copied = 0; var settings = 0; var quit = 0;
            var window = new MainWindow
            {
                ClaudeEnabled = true,
                DataContext = new { RefreshCommand = new TestCommand(() => refreshes++), CopySummaryCommand = new TestCommand(() => copied++), IsRefreshAvailable = true }
            };
            window.SettingsRequested += () => settings++;
            window.QuitRequested += () => quit++;
            try
            {
                window.Show(); window.UpdateLayout();
                window.ExecuteShortcut(Key.R); window.ExecuteShortcut(Key.C);
                window.ExecuteShortcut(Key.OemComma); window.ExecuteShortcut(Key.Q);
                Assert.Equal((1, 1, 1, 1), (refreshes, copied, settings, quit));
                window.SelectProvider("Claude");
                Send(window, Key.Left); Assert.Equal("Codex", window.SelectedProvider);
                Send(window, Key.Right); Assert.Equal("Claude", window.SelectedProvider);
                ((Button)window.FindName("CostButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Send(window, Key.Back);
                Assert.Equal(Visibility.Visible, ((StackPanel)window.FindName("SummaryView")).Visibility);
                var menu = ((StackPanel)window.FindName("PanelActions")).Children.OfType<Button>().ToArray();
                menu[0].Focus(); Send(menu[0], Key.Down); Assert.True(menu[1].IsKeyboardFocused);
                Send(menu[1], Key.Up); Assert.True(menu[0].IsKeyboardFocused);
            }
            finally { window.Close(); }
        });
    }

    private static void Send(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private sealed class Clock(DateTimeOffset now) : AIBar.Domain.IClock { public DateTimeOffset UtcNow => now; }
    private sealed class TestCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
