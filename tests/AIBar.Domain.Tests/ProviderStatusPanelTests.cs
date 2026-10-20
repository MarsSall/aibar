using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ProviderStatusPanelTests
{
    [Fact]
    public void HeadersExposeUsageAndIncidentDescriptionAndRevokeHidesBadge()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var codex = new EnabledProvider();
            var claude = new EnabledProvider();
            var calls = new List<StatusProvider>();
            var presentation = new ProviderStatusPresentation(codex, claude, () => codex.IsEnabled, () => claude.IsEnabled,
                (provider, _) =>
                {
                    calls.Add(provider);
                    return Task.FromResult(ProviderStatus.Parse(Encoding.UTF8.GetBytes(
                        provider == StatusProvider.Codex
                            ? "{\"status\":{\"indicator\":\"major\",\"description\":\"API incident\"}}"
                            : "{\"status\":{\"indicator\":\"none\",\"description\":\"Operational\"}}")));
                });
            var window = new MainWindow { ProviderStatus = presentation, DataContext = new { Claude = claude } };
            try
            {
                window.Show();
                presentation.Start();
                Pump();
                Assert.Empty(calls);
                var usage = Descendants<Button>(window).Single(button => Equals(button.Content, "Usage Dashboard"));
                Assert.True(usage.Focusable && usage.IsVisible);
                var codexBadge = Descendants<Button>(window).Single(button => AutomationProperties.GetName(button) == "Open OpenAI service status");
                var claudeBadge = Descendants<Button>(window).Single(button => AutomationProperties.GetName(button) == "Open Anthropic service status");
                Assert.Equal(Visibility.Collapsed, codexBadge.Visibility);
                codex.Set(true);
                claude.Set(true);
                Pump();
                Assert.Equal(new[] { StatusProvider.Codex, StatusProvider.Claude }, calls);
                Assert.Equal(Visibility.Visible, codexBadge.Visibility);
                Assert.Equal("API incident", codexBadge.Content);
                Assert.Contains(Descendants<System.Windows.Shapes.Ellipse>(codexBadge), dot => dot.IsVisible && dot.Width == 7);
                Assert.Contains(Descendants<TextBlock>(codexBadge), text => text.IsVisible && text.Text == "API incident");
                Assert.Equal(Visibility.Collapsed, claudeBadge.Visibility);
                window.SelectProvider("Claude"); window.UpdateLayout();
                Assert.True(usage.IsVisible);
                Assert.False(codexBadge.IsVisible);
                window.SelectProvider("Codex"); window.UpdateLayout();
                codex.Set(false);
                Pump();
                Assert.Equal(Visibility.Collapsed, codexBadge.Visibility);
                Assert.Null(presentation.CodexDescription);
                presentation.DisposeAsync().AsTask().GetAwaiter().GetResult();
                claude.Set(false);
                claude.Set(true);
                Pump();
                Assert.Equal(2, calls.Count);
            }
            finally
            {
                window.Close();
                presentation.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    public sealed class EnabledProvider : INotifyPropertyChanged
    {
        public bool IsEnabled { get; private set; }
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Set(bool enabled) { IsEnabled = enabled; PropertyChanged?.Invoke(this, new(nameof(IsEnabled))); }
    }
}
