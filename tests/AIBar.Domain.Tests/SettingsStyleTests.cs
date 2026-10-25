using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AIBar.Application;
using AIBar.Desktop;
using CheckBox = System.Windows.Controls.CheckBox;
using Slider = System.Windows.Controls.Slider;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class SettingsStyleTests
{
    [Theory]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Settings_use_spec_controls_and_window_surface(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            // No Load/Save: this fixture never creates settings files.
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-style-{Guid.NewGuid():N}");
            var window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")),
                new AlertSettings(Path.Combine(root, "alerts.json")), themeSource: new ThemeSource(theme));
            try
            {
                window.Show(); window.UpdateLayout();
                var surface = Assert.IsType<Border>(window.FindName("SettingsSurface"));
                Assert.True(window.AllowsTransparency);
                Assert.Equal(new Thickness(40), Assert.IsType<Border>(window.FindName("SettingsShadow")).Margin);
                Assert.Equal(new Thickness(1), surface.BorderThickness);
                Assert.Equal(new CornerRadius(8), surface.CornerRadius);
                Assert.Equal(520, surface.ActualWidth); Assert.Equal(600, surface.ActualHeight);
                AssertColor(theme == WindowsTheme.Dark ? Color.FromArgb(107, 110, 155, 255) :
                    theme == WindowsTheme.Light ? Color.FromRgb(214, 214, 214) : SystemColors.WindowTextColor, surface.BorderBrush);
                if (theme == WindowsTheme.Dark)
                {
                    var glow = Assert.IsType<DropShadowEffect>(surface.Effect);
                    Assert.Equal(Color.FromRgb(110, 155, 255), glow.Color);
                    Assert.Equal(29, glow.BlurRadius); Assert.Equal(.27, glow.Opacity); Assert.Equal(0, glow.ShadowDepth);
                    var shadow = Assert.IsType<DropShadowEffect>(Assert.IsType<Border>(window.FindName("SettingsShadow")).Effect);
                    Assert.Equal(32, shadow.BlurRadius); Assert.Equal(12, shadow.ShadowDepth); Assert.Equal(.6, shadow.Opacity);
                }
                else
                {
                    Assert.Null(surface.Effect);
                    Assert.Null(Assert.IsType<Border>(window.FindName("SettingsShadow")).Effect);
                }

                var slider = Assert.IsType<Slider>(window.FindName("BackgroundOpacity"));
                Assert.NotNull(slider.FocusVisualStyle);
                var track = Assert.IsType<Track>(slider.Template.FindName("PART_Track", slider));
                track.Thumb.ApplyTemplate(); track.DecreaseRepeatButton.ApplyTemplate();
                var thumb = Assert.IsType<System.Windows.Shapes.Ellipse>(track.Thumb.Template.FindName("SettingsSliderThumb", track.Thumb));
                AssertColor(Token(window, "SettingsAccentBrush"), thumb.Fill);
                AssertColor(Token(window, "SettingsOnAccentBrush"), thumb.Stroke);
                var fill = Assert.IsType<Border>(track.DecreaseRepeatButton.Template.FindName("SettingsSliderFill", track.DecreaseRepeatButton));
                AssertColor(Token(window, "SettingsAccentBrush"), fill.Background);

                var tabs = Assert.IsType<TabControl>(window.FindName("Sections"));
                tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => AutomationProperties.GetName(tab) == "Alerts"); window.UpdateLayout();
                foreach (var name in new[] { "Notification", "EdgeGlow", "Confetti" })
                {
                    var box = Assert.IsType<CheckBox>(window.FindName(name));
                    box.ApplyTemplate();
                    var chrome = Assert.IsType<Border>(box.Template.FindName("SettingsCheckBox", box));
                    var glyph = Assert.IsType<System.Windows.Shapes.Path>(box.Template.FindName("SettingsCheckGlyph", box));
                    Assert.Equal(18, chrome.Width); Assert.Equal(18, chrome.Height);
                    Assert.Equal(new CornerRadius(4), chrome.CornerRadius);
                    Assert.NotNull(box.FocusVisualStyle);
                    box.IsChecked = false;
                    AssertColor(Token(window, "SettingsControlBrush"), chrome.Background);
                    AssertColor(Token(window, "SettingsControlBorderBrush"), chrome.BorderBrush);
                    Assert.Equal(Visibility.Collapsed, glyph.Visibility);
                    box.IsChecked = true;
                    AssertColor(Token(window, "SettingsAccentBrush"), chrome.Background);
                    AssertColor(Token(window, "SettingsOnAccentBrush"), glyph.Stroke);
                    Assert.Equal(Visibility.Visible, glyph.Visibility);
                    var label = Assert.IsType<TextBlock>(box.Content);
                    Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                    Assert.True(label.ActualWidth > 40);
                    Assert.Equal(VerticalAlignment.Center, chrome.VerticalAlignment);
                }
                // A resource replacement must reach the template, not just happen to
                // match its initial color. This catches hard-coded On/Off foregrounds.
                window.Resources["SettingsSecondaryBrush"] = new SolidColorBrush(Colors.Magenta);
                foreach (var name in new[] { "Resets", "Thresholds", "RunOut" })
                {
                    var toggle = Assert.IsType<CheckBox>(window.FindName(name));
                    toggle.ApplyTemplate();
                    var state = Assert.IsType<TextBlock>(toggle.Template.FindName("StateText", toggle));
                    foreach (var on in new[] { false, true })
                    {
                        toggle.IsChecked = on;
                        Assert.Equal(on ? "On" : "Off", state.Text);
                        AssertColor(Token(window, "SettingsSecondaryBrush"), state.Foreground);
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    private static Color Token(FrameworkElement window, string key) => ((SolidColorBrush)window.FindResource(key)).Color;
    private static void AssertColor(Color expected, Brush actual) => Assert.Equal(expected, Assert.IsType<SolidColorBrush>(actual).Color);
    private sealed class ThemeSource(WindowsTheme theme) : IWindowsThemeSource
    {
        public event EventHandler? Changed { add { } remove { } }
        public WindowsTheme Current => theme;
        public void Dispose() { }
    }
}
