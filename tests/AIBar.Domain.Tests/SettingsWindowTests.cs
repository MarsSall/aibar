using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
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
                Assert.Equal(4, tabs.Items.Count);
                Assert.Equal("General", AutomationProperties.GetName((TabItem)tabs.Items[0]));
                Assert.Equal("Appearance", AutomationProperties.GetName((TabItem)tabs.Items[1]));
                Assert.Equal("Alerts", AutomationProperties.GetName((TabItem)tabs.Items[2]));
                Assert.Equal("Providers", AutomationProperties.GetName((TabItem)tabs.Items[3]));
                Assert.Equal("Uses data sent from another machine when it exists, otherwise this PC's logins.", Assert.IsType<TextBlock>(window.FindName("AutomaticDescription")).Text);
                Assert.DoesNotContain(Visuals<RadioButton>((TabItem)tabs.Items[3]), radio => radio.GroupName == "DataSource");
                // The 520 × 600 Settings surface is unchanged; the transparent host reserves
                // 40 DIPs on each side so the exterior glow is not clipped.
                Assert.Equal(600, window.Width);
                Assert.Equal(680, window.Height);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                var opacity = Assert.IsType<Slider>(window.FindName("BackgroundOpacity"));
                Assert.Equal(30, opacity.Minimum); Assert.Equal(100, opacity.Maximum);
                Assert.Equal(5, opacity.TickFrequency); Assert.True(opacity.IsSnapToTickEnabled);
                Assert.All(Visuals<RadioButton>(window), choice => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(choice))));
                tabs.SelectedIndex = 2; window.UpdateLayout();
                var input = Assert.IsType<TextBox>(window.FindName("ThresholdInput"));
                Assert.Equal("Alert thresholds (%)", AutomationProperties.GetName(input));
                input.Text = "90, 75, 100"; window.UpdateLayout();
                Assert.False(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<TextBlock>(window.FindName("ThresholdError")).Text));
                Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(window.FindName("AlertsErrorDot")).Visibility);
                Assert.Contains("thresholds", Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text);
                input.Text = "1, 75, 100";
                Assert.True(Assert.IsType<Button>(window.FindName("SaveButton")).IsEnabled);
                Assert.True(input.Focusable);
                Assert.All(Visuals<CheckBox>(window), toggle => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle))));
                tabs.SelectedIndex = 3; window.UpdateLayout();
                Assert.False(Assert.IsType<CheckBox>(window.FindName("CodexEnabled")).IsEnabled);
                Assert.False(Assert.IsType<CheckBox>(window.FindName("ClaudeEnabled")).IsEnabled);
                Assert.NotNull(Assert.IsType<System.Windows.Shapes.Path>(window.FindName("CodexMark")).Data);
                Assert.NotNull(Assert.IsType<System.Windows.Shapes.Path>(window.FindName("ClaudeMark")).Data);
                // The host must be transparent for exterior shadows; the surface stays opaque.
                var surface = Assert.IsType<Border>(window.FindName("SettingsSurface"));
                Assert.Equal(((SolidColorBrush)window.FindResource("SettingsSurfaceBrush")).Color, ((SolidColorBrush)surface.Background).Color);
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
            appearance.SaveAsync(new(ChartStyle: "columns"), default).AsTask().GetAwaiter().GetResult();
            var changed = 0; appearance.Changed += _ => Interlocked.Increment(ref changed);
            var dock = new DockSettings(Path.Combine(root, DockSettings.FileName));
            var initialDock = new DockOptions(true, "left", .25, "screen-2");
            dock.SaveAsync(initialDock, default).AsTask().GetAwaiter().GetResult();
            var window = new SettingsWindow(appearance, alerts, dock: dock);
            try
            {
                window.Show();
                Pump(window.LoadAsync());
                Assert.True(Assert.IsType<CheckBox>(window.FindName("FloatingWidget")).IsChecked == true);
                Assert.IsType<CheckBox>(window.FindName("FloatingWidget")).IsChecked = false;
                Assert.IsType<RadioButton>(window.FindName("BarUsage")).IsChecked = true;
                Assert.IsType<RadioButton>(window.FindName("YasbBrand")).IsChecked = true;
                Assert.IsType<Slider>(window.FindName("BackgroundOpacity")).Value = 70;
                Assert.IsType<TextBox>(window.FindName("ThresholdInput")).Text = "20, 80, 100";
                Assert.IsType<CheckBox>(window.FindName("Confetti")).IsChecked = true;
                Assert.IsType<CheckBox>(window.FindName("ProviderTint")).IsChecked = false;
                Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                var deadline = DateTime.UtcNow.AddSeconds(60);
                timer.Tick += (_, _) => frame.Continue = Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text == "Saving…" && DateTime.UtcNow < deadline;
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
                Assert.Equal("Changes saved.", Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text);
                Assert.Equal(1, changed);
                Assert.Equal(initialDock with { Enabled = false }, dock.LoadAsync(default).AsTask().GetAwaiter().GetResult());
                Assert.Equal(new AppearanceOptions("usage", "brand", 70, ProviderTint: false, ChartStyle: "columns"), appearance.LoadAsync(default).AsTask().GetAwaiter().GetResult());
                var saved = alerts.LoadAsync(default).AsTask().GetAwaiter().GetResult();
                Assert.True(saved.Confetti); Assert.Equal(new[] { 20, 80, 100 }, saved.ThresholdValues.ToArray());
            }
            finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Fact]
    public void Closing_without_save_discards_edits_in_all_sections_without_writing()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-cancel-{Guid.NewGuid():N}");
            var window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")), new AlertSettings(Path.Combine(root, "alerts.json")));
            try
            {
                window.Show();
                Assert.IsType<RadioButton>(window.FindName("BarUsage")).IsChecked = true;
                Assert.IsType<Slider>(window.FindName("BackgroundOpacity")).Value = 30;
                Assert.IsType<TextBox>(window.FindName("ThresholdInput")).Text = "25, 80";
                window.Close();
                Assert.False(Directory.Exists(root));
            }
            finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Theory]
    [InlineData(true, true, false, "IosevkaTerm Nerd Font is installed.")]
    [InlineData(false, false, false, "Bundled font not found.")]
    [InlineData(false, true, true, "Install the bundled font for YASB quota glyphs.")]
    public void Font_status_and_install_button_use_the_replaceable_facade(bool installed, bool available, bool enabled, string status)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-font-{Guid.NewGuid():N}");
            var calls = 0;
            var font = new SettingsGlyphFont(() => installed, () => available, () => { calls++; installed = true; return null; });
            var window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")), new AlertSettings(Path.Combine(root, "alerts.json")), font: font);
            try
            {
                var button = Assert.IsType<Button>(window.FindName("InstallYasbFont"));
                var line = Assert.IsType<TextBlock>(window.FindName("FontStatus"));
                Assert.Equal(enabled, button.IsEnabled);
                Assert.Equal(status, line.Text);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(enabled ? 1 : 0, calls);
                if (enabled)
                {
                    Assert.Equal("Installed. Restart YASB to use it.", line.Text);
                    Assert.False(button.IsEnabled);
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Font_install_failure_is_shown_without_disabling_retry()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-font-failure-{Guid.NewGuid():N}");
            var window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")), new AlertSettings(Path.Combine(root, "alerts.json")),
                font: new(() => false, () => true, () => "Windows refused the installation (IOException)."));
            try
            {
                var button = Assert.IsType<Button>(window.FindName("InstallYasbFont"));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Windows refused the installation (IOException).", Assert.IsType<TextBlock>(window.FindName("FontStatus")).Text);
                Assert.True(button.IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void General_controls_load_toggle_confirm_and_save_source()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-general-{Guid.NewGuid():N}");
            var approve = false; var clears = 0; var toggles = 0; var source = ClaudeQuotaSource.OtherMachine;
            var applied = new List<ClaudeQuotaSource>();
            var commands = new SettingsProviderCommands(() => false, () => false, () => source, () => true, () => true,
                _ => Task.CompletedTask, _ => Task.CompletedTask,
                selected => { applied.Add(selected); source = selected; return Task.CompletedTask; },
                general: new(() => Task.FromResult(true), () => { toggles++; return Task.FromResult(false); },
                    () => approve, () => { clears++; return Task.CompletedTask; }));
            var window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")), new AlertSettings(Path.Combine(root, "alerts.json")), providers: commands);
            try
            {
                window.Show(); Pump(window.LoadAsync());
                Assert.True(Assert.IsType<CheckBox>(window.FindName("StartWithWindows")).IsChecked);
                Assert.True(Assert.IsType<RadioButton>(window.FindName("SourceOtherMachine")).IsChecked);
                Assert.IsType<CheckBox>(window.FindName("StartWithWindows")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(Assert.IsType<CheckBox>(window.FindName("StartWithWindows")).IsChecked); Assert.Equal(1, toggles);
                Assert.IsType<Button>(window.FindName("ClearData")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, clears);
                approve = true;
                Assert.IsType<Button>(window.FindName("ClearData")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // LoadAsync after clear is asynchronous. Pump it before issuing Save.
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                var deadline = DateTime.UtcNow.AddSeconds(60);
                timer.Tick += (_, _) => frame.Continue = !Assert.IsType<Button>(window.FindName("ClearData")).IsEnabled && DateTime.UtcNow < deadline;
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
                Assert.Equal(1, clears);
                Assert.IsType<RadioButton>(window.FindName("SourceThisPc")).IsChecked = true;
                Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                frame = new System.Windows.Threading.DispatcherFrame();
                timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
                deadline = DateTime.UtcNow.AddSeconds(60);
                timer.Tick += (_, _) => frame.Continue = Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text == "Saving…" && DateTime.UtcNow < deadline;
                timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
                Assert.Equal(new[] { ClaudeQuotaSource.ThisPc }, applied);
                Assert.Equal("Changes saved.", Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text);
            }
            finally { window.Close(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Fact]
    public async Task General_commands_reflect_startup_readback_and_confirm_before_clear()
    {
        var toggles = 0; var clears = 0; var confirmations = 0; var approve = false;
        var general = new SettingsGeneralCommands(() => Task.FromResult(true),
            () => { toggles++; return Task.FromResult(false); },
            () => { confirmations++; return approve; },
            () => { clears++; return Task.CompletedTask; });
        Assert.True(await general.LoadStartupAsync());
        Assert.False(await general.ToggleStartupAsync()); Assert.Equal(1, toggles);
        Assert.False(await general.ClearAsync()); Assert.Equal(0, clears);
        approve = true;
        Assert.True(await general.ClearAsync()); Assert.Equal(1, clears); Assert.Equal(2, confirmations);
    }

    private static void Pump(Task task)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        timer.Tick += (_, _) => frame.Continue = !task.IsCompleted && DateTime.UtcNow < deadline;
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        Assert.True(task.IsCompleted, "Settings load did not complete.");
        task.GetAwaiter().GetResult();
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
