using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class PanelPolishTests
{
    [Theory]
    [InlineData(WindowsTheme.Light, 300)]
    [InlineData(WindowsTheme.Dark, 360)]
    [InlineData(WindowsTheme.HighContrast, 300)]
    public void Flat_rows_rules_equal_tabs_and_menu_groups_match_reference(WindowsTheme theme, int width)
    {
        WpfTestApplicationHost.Run(app =>
        {
            var previous = app.Resources.MergedDictionaries[0];
            app.Resources.MergedDictionaries[0] = ThemeDictionaries.Create(theme);
            var window = new MainWindow
            {
                Width = width, ClaudeEnabled = true,
                DataContext = new
                {
                    State = new { UpdatedLabel = "Updated just now", PlanLabel = "Plus" },
                    Claude = new { PlanLabel = "Max", State = new { UpdatedLabel = "Updated just now" } }
                }
            };
            try
            {
                window.Show(); window.UpdateLayout();
                var cost = (Button)window.FindName("CostButton");
                var back = (Button)window.FindName("BackButton");
                foreach (var row in new[] { cost, back })
                {
                    Assert.Equal(new Thickness(0), row.BorderThickness);
                    Assert.NotNull(row.FocusVisualStyle);
                    Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(row.Background).Color);
                    Assert.Contains(row.Style.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
                    Assert.Contains(row.Style.Triggers.OfType<Trigger>(), trigger => trigger.Property == ButtonBase.IsPressedProperty);
                    row.ApplyTemplate();
                    Assert.NotNull(row.Template.FindName("FlatChrome", row));
                }
                Assert.Equal("Cost", ((TextBlock)window.FindName("CostTitle")).Text);
                var chevron = (TextBlock)window.FindName("CostChevron");
                Assert.Equal("›", chevron.Text); Assert.Equal(HorizontalAlignment.Right, chevron.HorizontalAlignment);
                Assert.Equal(((RadioButton)window.FindName("CodexTab")).ActualWidth,
                    ((RadioButton)window.FindName("ClaudeTab")).ActualWidth, 2);
                var plan = Visuals<TextBlock>(window).Single(text => AutomationProperties.GetName(text) == "Codex plan");
                Assert.Equal("Plus", plan.Text); Assert.Equal(1, Grid.GetRow(plan));
                Assert.Equal(HorizontalAlignment.Right, plan.HorizontalAlignment);
                Assert.Equal(((SolidColorBrush)window.FindResource("MutedTextBrush")).Color, ((SolidColorBrush)plan.Foreground).Color);
                var summary = (StackPanel)window.FindName("SummaryView");
                var rules = summary.Children.OfType<Border>().ToArray();
                Assert.Equal(new[] { "TabsRule", "WindowsRule" }, rules.Select(rule => rule.Name));
                Assert.True(summary.Children.IndexOf(rules[0]) < summary.Children.IndexOf((StackPanel)window.FindName("QuotaCards")));
                Assert.True(summary.Children.IndexOf(rules[1]) < summary.Children.IndexOf(cost));
                foreach (var name in new[] { "CodexHeaderRule", "ClaudeHeaderRule", "PanelFooterRule" })
                    Assert.InRange(((Border)window.FindName(name)).BorderThickness.Top, .5, 1);
                var menu = (StackPanel)window.FindName("PanelActions");
                Assert.Equal(new[] { typeof(Button), typeof(Button), typeof(Border), typeof(Button), typeof(Button), typeof(Border), typeof(Button), typeof(Button), typeof(Button) }, menu.Children.Cast<UIElement>().Select(item => item.GetType()));
                var buttons = menu.Children.OfType<Button>().ToArray();
                Assert.All(buttons.Take(4), row => Assert.False(string.IsNullOrWhiteSpace(row.Tag as string)));
                Assert.All(buttons.Skip(4), row => Assert.Null(row.Tag));
                Assert.All(buttons, row =>
                {
                    Assert.Equal(28d, row.MinHeight); Assert.NotNull(row.FocusVisualStyle);
                    var hover = row.Style.Triggers.OfType<Trigger>().Single(trigger => trigger.Property == UIElement.IsMouseOverProperty);
                    var brush = hover.Setters.OfType<Setter>().Single(setter => setter.Property == Control.BackgroundProperty);
                    Assert.Equal("PanelMenuHighlightBrush", Assert.IsType<DynamicResourceExtension>(brush.Value).ResourceKey);
                    var highlight = (SolidColorBrush)window.FindResource("PanelMenuHighlightBrush");
                    var highlightText = (SolidColorBrush)window.FindResource("PanelMenuHighlightTextBrush");
                    Assert.Equal(theme == WindowsTheme.HighContrast ? SystemColors.HighlightColor : Color.FromRgb(52, 120, 246), highlight.Color);
                    Assert.Equal(theme == WindowsTheme.HighContrast ? SystemColors.HighlightTextColor : Colors.White, highlightText.Color);
                    Assert.Contains(row.Style.Triggers.OfType<Trigger>(), trigger => trigger.Property == ButtonBase.IsPressedProperty);
                });
            }
            finally { window.Close(); app.Resources.MergedDictionaries[0] = previous; }
        });
    }

    [Fact]
    public void Segments_support_arrow_selection_and_tab_exit_without_dropdown()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var chart = new QuotaHistoryChart();
            var window = new Window { Content = chart };
            try
            {
                window.Show(); window.UpdateLayout();
                var range = Assert.IsType<ListBox>(chart.FindName("HistoryRange"));
                var first = (ListBoxItem)range.Items[0]; first.Focus();
                first.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Right) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.Equal(1, range.SelectedIndex);
                Assert.Equal("Last 7 days", AutomationProperties.GetName((DependencyObject)range.SelectedItem));
                Assert.NotNull(((ListBoxItem)range.Items[1]).FocusVisualStyle);
                foreach (var (key, expected) in new[] { (Key.End, 2), (Key.Right, 2), (Key.Left, 1), (Key.Home, 0), (Key.Left, 0) })
                {
                    ((ListBoxItem)range.SelectedItem).RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Assert.Equal(expected, range.SelectedIndex);
                    Assert.True(((ListBoxItem)range.Items[expected]).IsKeyboardFocused);
                }
                Assert.Equal(KeyboardNavigationMode.Once, KeyboardNavigation.GetTabNavigation(range));
                Assert.Empty(Visuals<ComboBox>(chart));
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Today: $0.04 · 15K tokens", "Today: $ 0.04 · 15K tokens")]
    [InlineData("Last 30 days: $254.24+ · 218M tokens (partial)", "Last 30 days: $ 254.24+ · 218M tokens (partial)")]
    [InlineData("Today: unavailable", "Today: unavailable")]
    public void Cost_spacing_preserves_amount_tokens_and_partial_suffix(string input, string expected) =>
        Assert.Equal(expected, new PanelCostLabelConverter().Convert(input, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture));

    private static IEnumerable<T> Visuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Visuals<T>(child)) yield return nested;
        }
    }
}
