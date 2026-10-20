using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class RefreshIntervalWindowTests
{
    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Providers_refresh_choices_load_navigate_warn_and_save(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-refresh-window-{Guid.NewGuid():N}");
            var appearance = new AppearanceSettings(Path.Combine(root, "appearance.json"));
            var previous = app.Resources.MergedDictionaries[0];
            SettingsWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                appearance.SaveAsync(new(RefreshMinutes: 15), default).AsTask().GetAwaiter().GetResult();
                window = new SettingsWindow(appearance, new AlertSettings(Path.Combine(root, "alerts.json")), themeSource: new ThemeSource(theme));
                window.Show();
                var load = window.LoadAsync(); PumpUntil(() => load.IsCompleted); load.GetAwaiter().GetResult();
                Assert.IsType<TabControl>(window.FindName("Sections")).SelectedIndex = 2;
                window.UpdateLayout();
                var choices = new[] { 1, 2, 5, 15, 30 }.Select(value => Assert.IsType<RadioButton>(window.FindName($"Refresh{value}"))).ToArray();
                var note = Assert.IsType<TextBlock>(window.FindName("RefreshRateLimitNote"));
                Assert.Equal("May hit provider rate limits.", note.Text);
                Assert.Equal(Visibility.Collapsed, note.Visibility);
                Assert.True(choices[3].IsChecked);
                foreach (var choice in choices)
                {
                    Assert.Equal($"Refresh every {choice.Content}", AutomationProperties.GetName(choice));
                    Assert.NotNull(choice.FocusVisualStyle);
                }
                Assert.Single(choices.Where(choice => choice.IsTabStop));
                Assert.True(choices[3].Focus());
                Press(choices[3], Key.Home);
                Assert.True(choices[0].IsChecked); Assert.True(choices[0].IsKeyboardFocused);
                Assert.Equal(Visibility.Visible, note.Visibility);
                Assert.Equal(15, appearance.LoadAsync(default).AsTask().GetAwaiter().GetResult().RefreshMinutes); // Selection alone never saves.
                Press(choices[0], Key.Right);
                Assert.True(choices[1].IsChecked); Assert.Equal(Visibility.Collapsed, note.Visibility);
                Press(choices[1], Key.End); Assert.True(choices[4].IsChecked);
                Press(choices[4], Key.Left); Assert.True(choices[3].IsChecked);
                Press(choices[3], Key.Home);
                Assert.IsType<Button>(window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text != "Saving…");
                Assert.Equal("Changes saved.", Assert.IsType<TextBlock>(window.FindName("SaveStatus")).Text);
                Assert.Equal(1, appearance.LoadAsync(default).AsTask().GetAwaiter().GetResult().RefreshMinutes);
                Assert.Single(choices.Where(choice => choice.IsTabStop));
            }
            finally
            {
                window?.Close(); app.Resources.MergedDictionaries[0] = previous;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    private static void Press(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target)!, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void PumpUntil(Func<bool> completed)
    {
        if (completed()) return;
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(60);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => frame.Continue = !completed() && DateTime.UtcNow < deadline;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Assert.True(completed(), "Settings operation did not finish within 60 seconds.");
    }
    private sealed class ThemeSource(WindowsTheme theme) : IWindowsThemeSource
    {
        public WindowsTheme Current => theme;
        public event EventHandler? Changed { add { } remove { } }
        public void Dispose() { }
    }
}
