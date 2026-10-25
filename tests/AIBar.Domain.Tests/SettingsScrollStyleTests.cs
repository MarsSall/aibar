using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

/// <summary>Settings sections scroll with the panel's slim bar and use uppercase section headings.</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SettingsScrollStyleTests
{
    [Theory]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.Dark)]
    public void Providers_section_uses_a_slim_scroll_bar_and_uppercase_headings(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-settings-scroll-{Guid.NewGuid():N}");
            var previous = app.Resources.MergedDictionaries[0];
            SettingsWindow? window = null;
            try
            {
                app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
                window = new SettingsWindow(new AppearanceSettings(Path.Combine(root, "appearance.json")),
                    new AlertSettings(Path.Combine(root, "alerts.json")), themeSource: new ThemeSource(theme))
                { Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                window.Show();
                var tabs = (TabControl)window.FindName("Sections"); tabs.SelectedItem = tabs.Items.Cast<TabItem>().Single(tab => AutomationProperties.GetName(tab) == "Providers");
                window.UpdateLayout();
                var headings = Visuals<TextBlock>(window).Where(text => text.IsVisible && text.Style == window.FindResource("SettingsHeading")).Select(text => text.Text).ToArray();
                Assert.Contains("REFRESH EVERY", headings);
                Assert.All(headings, heading => Assert.Equal(heading.ToUpperInvariant(), heading));
                foreach (var bar in Visuals<ScrollBar>(window).Where(bar => bar.Orientation == Orientation.Vertical && bar.IsVisible))
                {
                    Assert.InRange(bar.ActualWidth, 1, 8);
                    Assert.DoesNotContain(Visuals<RepeatButton>(bar), button => button.IsVisible &&
                        (button.Command == ScrollBar.LineUpCommand || button.Command == ScrollBar.LineDownCommand));
                }
            }
            finally
            {
                window?.Close();
                app.Resources.MergedDictionaries[0] = previous;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }

    private sealed class ThemeSource(WindowsTheme theme) : IWindowsThemeSource
    {
        public event EventHandler? Changed { add { } remove { } }
        public WindowsTheme Current => theme;
        public void Dispose() { }
    }
}
