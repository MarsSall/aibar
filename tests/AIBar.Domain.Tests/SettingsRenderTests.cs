using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

/// <summary>Writes native Settings renders for visual review when AIBAR_PANEL_RENDER_DIRECTORY is set.</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SettingsRenderTests
{
    [Theory]
    [InlineData(WindowsTheme.Dark, 0, "appearance")]
    [InlineData(WindowsTheme.Dark, 1, "alerts")]
    [InlineData(WindowsTheme.Dark, 2, "providers")]
    [InlineData(WindowsTheme.Light, 0, "appearance")]
    [InlineData(WindowsTheme.Light, 1, "alerts")]
    [InlineData(WindowsTheme.Light, 2, "providers")]
    public void Render_settings_section(WindowsTheme theme, int section, string name)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-render-{Guid.NewGuid():N}");
            var old = app.Resources.MergedDictionaries[0];
            SettingsWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")),
                    new AlertSettings(Path.Combine(root, "alerts.json")), themeSource: new ThemeSource(theme))
                { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                window.Show();
                ((TabControl)window.FindName("Sections")).SelectedIndex = section;
                window.UpdateLayout();
                Assert.True(window.ActualWidth > 0 && window.ActualHeight > 0);
                var directory = Environment.GetEnvironmentVariable("AIBAR_PANEL_RENDER_DIRECTORY");
                if (string.IsNullOrWhiteSpace(directory)) return;
                var visual = (Visual)VisualTreeHelper.GetChild(window, 0);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                var background = new DrawingVisual();
                using (var drawing = background.RenderOpen())
                    drawing.DrawRectangle(window.Background ?? System.Windows.Media.Brushes.Black, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
                bitmap.Render(background);
                bitmap.Render(visual);
                Directory.CreateDirectory(directory);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, $"settings-{name}-{theme.ToString().ToLowerInvariant()}.png"));
                png.Save(stream);
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries[0] = old;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    private sealed class ThemeSource(WindowsTheme theme) : IWindowsThemeSource
    {
        public event EventHandler? Changed { add { } remove { } }
        public WindowsTheme Current => theme;
        public void Dispose() { }
    }
}
