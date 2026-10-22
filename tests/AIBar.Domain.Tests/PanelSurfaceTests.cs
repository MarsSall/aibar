using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class PanelSurfaceTests
{
    // Frozen: xunit may build this static on another thread than the test's dispatcher.
    private static readonly SolidColorBrush DarkSurface = Frozen(Color.FromRgb(12, 12, 16));
    private static SolidColorBrush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    // Converter inputs: surface, provider, Codex usage, Claude usage, Codex/Claude current, Codex/Claude login, tint.
    private static object[] Window(bool tint, bool claudeLogin = false) =>
        [DarkSurface, "Claude", 10m, 40m, true, true, false, claudeLogin, tint];

    [Fact]
    public void Panel_window_is_not_resizable_so_windows_draws_no_second_frame()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new MainWindow();
            try { Assert.Equal(ResizeMode.NoResize, window.ResizeMode); }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    public void Cost_card_is_a_light_veil_over_the_panel_surface(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var previous = app.Resources.MergedDictionaries[0];
            app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
            var window = new MainWindow { Width = 360, DataContext = new { } };
            try
            {
                window.Show(); window.UpdateLayout();
                var card = Assert.IsType<Border>(window.FindName("CostCard"));
                Assert.True(Assert.IsType<SolidColorBrush>(card.Background).Color.A <= 0x10, "The Cost card must not paint an opaque block.");
                Assert.True(Assert.IsType<SolidColorBrush>(card.BorderBrush).Color.A <= 0x20);
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = previous; }
        });
    }

    [Fact]
    public void Provider_glow_is_stronger_than_the_original_halo()
    {
        var glow = Assert.IsType<DropShadowEffect>(new PanelWindowDecorationConverter().Convert(Window(tint: true), typeof(Effect), "Glow", CultureInfo.InvariantCulture));
        Assert.Equal(Color.FromRgb(0xE8, 0x95, 0x5F), glow.Color);
        Assert.Equal(40, glow.BlurRadius); Assert.Equal(.42, glow.Opacity); Assert.Equal(0, glow.ShadowDepth);
    }

    [Fact]
    public void Provider_tint_off_keeps_border_glow_divider_and_tab_neutral()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new PanelWindowDecorationConverter();
            Assert.Equal(Color.FromArgb(107, 0xE8, 0x95, 0x5F), Assert.IsType<SolidColorBrush>(window.Convert(Window(tint: true), typeof(Brush), null!, CultureInfo.InvariantCulture)).Color);
            Assert.Equal(Color.FromArgb(31, 255, 255, 255), Assert.IsType<SolidColorBrush>(window.Convert(Window(tint: false), typeof(Brush), null!, CultureInfo.InvariantCulture)).Color);
            Assert.Equal(0, Assert.IsType<DropShadowEffect>(window.Convert(Window(tint: false), typeof(Effect), "Glow", CultureInfo.InvariantCulture)).Opacity);
            Assert.IsType<LinearGradientBrush>(window.Convert(Window(tint: true), typeof(Brush), "Divider", CultureInfo.InvariantCulture));
            Assert.IsNotType<LinearGradientBrush>(window.Convert(Window(tint: false), typeof(Brush), "Divider", CultureInfo.InvariantCulture));
            // Sign-in is a state warning, not branding: it stays visible with the tint off.
            Assert.Equal(Color.FromRgb(0xF5, 0xC2, 0x5A), Assert.IsType<DropShadowEffect>(window.Convert(Window(tint: false, claudeLogin: true), typeof(Effect), "Glow", CultureInfo.InvariantCulture)).Color);

            var tab = new PanelTabDecorationConverter();
            object[] tinted = ["Claude", DarkSurface, 10m, 40m, true, true, true], neutral = ["Claude", DarkSurface, 10m, 40m, true, true, false];
            Assert.Equal(Color.FromArgb(143, 0xE8, 0x95, 0x5F), Assert.IsType<SolidColorBrush>(tab.Convert(tinted, typeof(Brush), "Border", CultureInfo.InvariantCulture)).Color);
            Assert.Equal(Color.FromArgb(143, 0x8E, 0x8E, 0x98), Assert.IsType<SolidColorBrush>(tab.Convert(neutral, typeof(Brush), "Border", CultureInfo.InvariantCulture)).Color);
            Assert.Equal(Color.FromArgb(33, 0x8E, 0x8E, 0x98), Assert.IsType<LinearGradientBrush>(tab.Convert(neutral, typeof(Brush), "Fill", CultureInfo.InvariantCulture)).GradientStops[0].Color);
        });
    }

    [Fact]
    public void Provider_tint_defaults_on_and_reaches_the_rendered_panel_border()
    {
        WpfTestApplicationHost.Run(app =>
        {
            var previous = app.Resources.MergedDictionaries[0];
            app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(WindowsTheme.Dark);
            var window = new MainWindow { Width = 360, DataContext = new { State = new { IsCurrent = true } } };
            try
            {
                Assert.True(window.ProviderTint);
                window.Show(); window.UpdateLayout();
                var surface = Assert.IsType<Border>(window.Template.FindName("PanelSurface", window));
                Assert.Equal((byte)107, Assert.IsType<SolidColorBrush>(surface.BorderBrush).Color.A);
                window.ProviderTint = false; window.UpdateLayout();
                Assert.Equal(Color.FromArgb(31, 255, 255, 255), Assert.IsType<SolidColorBrush>(surface.BorderBrush).Color);
                Assert.Equal(0, Assert.IsType<DropShadowEffect>(surface.Effect).Opacity);
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = previous; }
        });
    }
}
