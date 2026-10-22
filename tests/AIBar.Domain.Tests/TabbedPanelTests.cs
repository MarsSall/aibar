using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class TabbedPanelTests
{
    [Fact]
    public void Highest_usage_is_default_and_explicit_selection_survives_refresh_and_reopen()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new MainWindow { ClaudeEnabled = true, CodexUsage = 20, ClaudeUsage = 80 };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.Equal("Claude", window.SelectedProvider);
                Assert.False(((StackPanel)window.FindName("CodexQuotaSection")).IsVisible);
                Assert.True(((StackPanel)window.FindName("ClaudeQuotaSection")).IsVisible);
                window.SelectProvider("Codex");
                window.ClaudeUsage = 99;
                window.Hide(); window.Show(); window.UpdateLayout();
                Assert.Equal("Codex", window.SelectedProvider);
                Assert.True(((StackPanel)window.FindName("CodexQuotaSection")).IsVisible);
                Assert.False(((StackPanel)window.FindName("ClaudeQuotaSection")).IsVisible);
                window.SelectProvider("Claude"); window.ClaudeEnabled = false;
                Assert.Equal("Codex", window.SelectedProvider);
                window.ClaudeEnabled = true;
                Assert.Equal("Claude", window.SelectedProvider);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Tabs_support_arrow_keys_and_bar_mode_changes_live_without_changing_data()
    {
        WpfTestApplicationHost.Run(app =>
        {
            var previous = app.Resources.MergedDictionaries[0];
            app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(WindowsTheme.Light);
            var primary = new QuotaWindowPresentation("5-hour quota", 90m, "01h 00m");
            var window = new MainWindow
            {
                DataContext = new
                {
                    Primary = primary, Weekly = primary,
                    State = new { IsCurrent = true, IsPrimaryAvailable = true, IsWeeklyAvailable = true },
                    Claude = new { IsEnabled = true, State = new { Primary = primary, Weekly = primary, IsCurrent = true, IsPrimaryAvailable = true, IsWeeklyAvailable = true } }
                }
            };
            try
            {
                window.Show(); window.UpdateLayout();
                var codexTab = (RadioButton)window.FindName("CodexTab");
                var claudeTab = (RadioButton)window.FindName("ClaudeTab");
                codexTab.Focus();
                codexTab.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Right) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                window.UpdateLayout();
                Assert.Equal("Claude", window.SelectedProvider);
                Assert.True(claudeTab.IsKeyboardFocused);
                Assert.True(claudeTab.IsChecked);
                var section = (StackPanel)window.FindName("ClaudeQuotaSection");
                var meter = Descendants<PanelMeter>(section).First();
                var bar = Descendants<ProgressBar>(meter).Single();
                Assert.Equal(PanelPalette.ProviderColor("Claude"), ((SolidColorBrush)bar.Foreground).Color);
                window.BarColorMode = PanelBarColorMode.Usage;
                window.UpdateLayout();
                Assert.Equal(PanelPalette.UsageColor(90, false), ((SolidColorBrush)bar.Foreground).Color);
                Assert.Equal(90d, bar.Value);
                Assert.Equal(90m, meter.Usage);
                Assert.Equal("01h 00m", meter.ResetCountdown);
                Assert.Equal(((SolidColorBrush)bar.Foreground).Color, ((SolidColorBrush)((ProgressBar)window.FindName("ClaudeTabUsage")).Foreground).Color);
                var about = Descendants<Button>(window).Single(button => Equals(button.Content, "About AIBar"));
                about.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var note = (TextBlock)window.FindName("AboutText");
                Assert.Equal(Visibility.Visible, note.Visibility);
                Assert.Contains("AIBar ", note.Text); Assert.Contains("Simple Icons (CC0)", note.Text);
                about.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, note.Visibility);
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = previous; }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    [Theory]
    // S10 light usage tokens; exact 75/90 boundaries are unchanged.
    [InlineData(74, "#FF1E9E5A")]
    [InlineData(75, "#FFB88400")]
    [InlineData(89, "#FFB88400")]
    [InlineData(90, "#FFD13B2A")]
    public void Usage_mode_restores_semantic_thresholds_and_stale_is_gray(int usage, string expected)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var converter = new ProviderVisuals();
            var surface = new SolidColorBrush(Colors.White);
            object[] Values(bool current) => new object[] { usage, surface, surface.Color, current, "Claude", PanelBarColorMode.Usage };
            Assert.Equal(expected, ((SolidColorBrush)converter.Convert(Values(true), typeof(Brush), "Meter", CultureInfo.InvariantCulture)).Color.ToString());
            var stale = ((SolidColorBrush)converter.Convert(Values(false), typeof(Brush), "Meter", CultureInfo.InvariantCulture)).Color;
            Assert.Equal(stale.R, stale.G); Assert.Equal(stale.G, stale.B);
            var window = new MainWindow();
            Assert.Equal(PanelBarColorMode.Brand, window.BarColorMode);
            window.Close();
        });
    }

    [Theory]
    [InlineData("max", "Max")]
    [InlineData("pro", "Pro")]
    [InlineData("plus", "Plus")]
    [InlineData("unknown-value", null)]
    [InlineData(null, null)]
    public void Plans_are_allowlisted_display_labels_not_arbitrary_credential_text(string? source, string? expected) =>
        Assert.Equal(expected, QuotaPlanLabels.FromKnownValue(source));
}
