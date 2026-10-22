using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class NotificationWindowTests
{
    [Theory]
    [InlineData(WindowsTheme.Dark)]
    [InlineData(WindowsTheme.Light)]
    [InlineData(WindowsTheme.HighContrast)]
    public void Notification_is_polite_nonactivating_and_preserves_copy_and_named_dismiss(WindowsTheme theme)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var opened = 0;
            var window = new NotificationWindow(QuotaAlertKind.ThresholdCrossed, "Claude", "Claude weekly limit at 90%",
                "Resets in 3d 4h", () => opened++, theme, false);
            try
            {
                Assert.False(window.ShowActivated);
                Assert.False(window.ShowInTaskbar);
                Assert.True(window.Topmost);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(window));
                Assert.Equal("Claude weekly limit at 90%. Resets in 3d 4h", AutomationProperties.GetName(window));
                window.Show(); window.UpdateLayout();
                var text = Descendants<TextBlock>(window).Select(t => t.Text).ToArray();
                Assert.Contains("△  THRESHOLD · NOW", text);
                Assert.Contains("Claude weekly limit at 90%", text);
                Assert.Contains("Resets in 3d 4h", text);
                Assert.Single(Descendants<System.Windows.Shapes.Path>(window));
                var dismiss = Assert.Single(Descendants<Button>(window));
                Assert.Equal("Dismiss", AutomationProperties.GetName(dismiss));
                Assert.Equal(28d, dismiss.Width);
                dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, opened); // Dismiss never opens the quota panel.
            }
            finally { window.Close(); }
        });
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
