using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class SettingsWindowTests
{
    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Sections_controls_and_inline_validation_are_accessible_in_every_theme(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-window-{Guid.NewGuid():N}");
            var old = app.Resources.MergedDictionaries[0];
            SettingsWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")), new AlertSettings(Path.Combine(root, "alerts.json")), themeSource: new ThemeSource(theme));
                window.Show(); window.UpdateLayout();
                Assert.Equal("AIBar Settings", window.Title);
                var tabs = Assert.IsType<TabControl>(window.FindName("Sections"));
                Assert.Equal(new[] { "Colors", "Alerts" }, tabs.Items.Cast<TabItem>().Select(t => t.Header.ToString()).ToArray());
                Assert.All(Visuals<ComboBox>(window), choice => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(choice))));
                tabs.SelectedIndex = 1; window.UpdateLayout();
                var input = Assert.IsType<TextBox>(window.FindName("ThresholdInput"));
                Assert.Equal("Alert thresholds (%)", AutomationProperties.GetName(input));
                input.Text = "90, 75, 100"; window.UpdateLayout();
                Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<TextBlock>(window.FindName("ThresholdError")).Text));
                input.Text = "1, 75, 100";
                Assert.True(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.True(input.Focusable);
                Assert.Equal(((SolidColorBrush)window.FindResource("SurfaceBrush")).Color, ((SolidColorBrush)window.Background).Color);
            }
            finally
            {
                window?.Close(); app.Resources.MergedDictionaries[0] = old;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }
    [Fact]
    public void Save_persists_both_sections_and_emits_appearance_change()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-save-{Guid.NewGuid():N}");
            var appearance = new AppearanceSettings(Path.Combine(root, "appearance.json"));
            var alerts = new AlertSettings(Path.Combine(root, "alerts.json"));
            var changed = 0; appearance.Changed += _ => Interlocked.Increment(ref changed);
            var window = new SettingsWindow(appearance, alerts);
            try
            {
                window.Show();
                Assert.IsType<ComboBox>(window.FindName("BarColors")).SelectedIndex = 1;
                Assert.IsType<ComboBox>(window.FindName("YasbColors")).SelectedIndex = 1;
                Assert.IsType<TextBox>(window.FindName("ThresholdInput")).Text = "20, 80, 100";
                Assert.IsType<CheckBox>(window.FindName("Confetti")).IsChecked = true;
                Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                var deadline = DateTime.UtcNow.AddSeconds(60);
                timer.Tick += (_, _) => frame.Continue = Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text == "Saving…" && DateTime.UtcNow < deadline;
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
                Assert.Equal("Changes saved.", Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text);
                Assert.Equal(1, changed);
                Assert.Equal(new AppearanceOptions("usage", "brand"), appearance.LoadAsync(default).AsTask().GetAwaiter().GetResult());
                var saved = alerts.LoadAsync(default).AsTask().GetAwaiter().GetResult();
                Assert.True(saved.Confetti); Assert.Equal(new[] { 20, 80, 100 }, saved.ThresholdValues.ToArray());
            }
            finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private sealed class ThemeSource(WindowsTheme theme) : IWindowsThemeSource
    {
        public event EventHandler? Changed { add { } remove { } }
        public WindowsTheme Current => theme;
        public void Dispose() { }
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
