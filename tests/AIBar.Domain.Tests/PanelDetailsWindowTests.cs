using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class PanelDetailsWindowTests
{
    // S5/S6: click pins an owned side window without replacing the summary.
    [Fact]
    public void Click_opens_owned_details_and_focuses_close()
    {
        WithPanel(panel =>
        {
            Cost(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var side = Assert.IsType<PanelDetailsWindow>(panel.DetailsWindow);
            Assert.Same(panel, side.Owner);
            Assert.True(side.IsVisible);
            Assert.True(side.ShowActivated);
            Assert.True(Close(panel).IsKeyboardFocused);
            Assert.Equal("Close usage details", AutomationProperties.GetName(Close(panel)));
            Assert.Equal(Visibility.Visible, ((StackPanel)panel.FindName("SummaryView")).Visibility);
            Assert.Same(side, Window.GetWindow((StackPanel)panel.FindName("DetailView")));
            Assert.Equal(WindowStyle.None, side.WindowStyle);
            Assert.Equal(ResizeMode.NoResize, side.ResizeMode);
            Assert.False(side.ShowInTaskbar);
            Assert.Equal(panel.ActualWidth, side.Width);
            Assert.InRange(side.MaxHeight, 1, 640);
            Assert.Equal(SizeToContent.Height, side.SizeToContent);
        });
    }

    // S6: both dismissal keys return keyboard focus to Cost.
    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.Left)]
    public void Details_keys_close_and_return_focus(Key key)
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            var side = panel.DetailsWindow!;
            Send(side, side, key);
            Assert.Null(panel.DetailsWindow);
            Assert.False(side.IsVisible);
            Assert.True(Cost(panel).IsKeyboardFocused);
        });
    }

    // S6: hover then click is the natural CodexBar gesture; the click pins the window.
    [Fact]
    public void Click_after_hover_open_pins_details_instead_of_closing()
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(false);
            var side = panel.DetailsWindow!;
            Cost(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(side, panel.DetailsWindow);
            Assert.True(side.IsVisible);
            Assert.True(Close(panel).IsKeyboardFocused);
            panel.ScheduleDetailsClose();
            Assert.Same(side, panel.DetailsWindow);
        });
    }

    // S6/S7: Left belongs to either chart selector while it has focus.
    [Theory]
    [InlineData("HistoryRange")]
    [InlineData("StyleSelector")]
    public void Left_in_chart_selector_moves_segment_without_closing(string selectorName)
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            var side = panel.DetailsWindow!;
            side.UpdateLayout();
            var detail = (StackPanel)panel.FindName("DetailView");
            var range = (ListBox)detail.Children.OfType<QuotaHistoryChart>().Single().FindName(selectorName);
            range.SelectedIndex = 1;
            var item = (ListBoxItem)range.ItemContainerGenerator.ContainerFromIndex(1);
            Assert.True(item.Focus());
            Send(item, side, Key.Left);
            Assert.Equal(0, range.SelectedIndex);
            Assert.Same(side, panel.DetailsWindow);
            Assert.True(side.IsVisible);
        });
    }

    // S5/S8: the layered host is transparent; the Settings-colored rounded surface is opaque.
    [Fact]
    public void Details_window_background_is_transparent_behind_the_rounded_surface()
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            Assert.Equal(System.Windows.Media.Colors.Transparent, Assert.IsType<System.Windows.Media.SolidColorBrush>(panel.DetailsWindow!.Background).Color);
        });
    }

    // S6: a second click closes, rather than opening another window.
    [Fact]
    public void Second_cost_click_closes_details()
    {
        WithPanel(panel =>
        {
            Cost(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Cost(panel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(panel.DetailsWindow);
            Assert.True(Cost(panel).IsKeyboardFocused);
        });
    }

    // S6: intercept Right before the panel's provider-switching shortcut.
    [Fact]
    public void Right_on_cost_opens_details_but_elsewhere_switches_provider()
    {
        WithPanel(panel =>
        {
            panel.ClaudeEnabled = true;
            panel.SelectProvider("Codex");
            Cost(panel).Focus();
            Send(Cost(panel), panel, Key.Right);
            Assert.NotNull(panel.DetailsWindow);
            Assert.Equal("Codex", panel.SelectedProvider);
            panel.CloseDetails(true);
            var action = ((StackPanel)panel.FindName("PanelActions")).Children.OfType<Button>().First();
            action.Focus();
            Send(action, panel, Key.Right);
            Assert.Equal("Claude", panel.SelectedProvider);
        });
    }

    // S6: the hover path must never steal activation or keyboard focus.
    [Fact]
    public void Hover_open_preserves_panel_activation_and_focus()
    {
        WithPanel(panel =>
        {
            Cost(panel).Focus();
            var focus = Keyboard.FocusedElement;
            var active = panel.IsActive;
            panel.OpenDetails(false);
            Assert.False(panel.DetailsWindow!.ShowActivated);
            Assert.False(panel.DetailsWindow.IsActive);
            Assert.Equal(active, panel.IsActive);
            Assert.Same(focus, Keyboard.FocusedElement);
            Assert.Equal(250, MainWindow.DetailsHoverDelayMilliseconds);
            Assert.Equal(400, MainWindow.DetailsCloseDelayMilliseconds);
        });
    }

    // S5: left is preferred when the panel's monitor has sufficient room.
    [Fact]
    public void Placement_prefers_left_with_six_DIP_gap()
    {
        var position = PanelDetailsPlacement.Place(new Rect(700, 100, 360, 400), new Rect(0, 0, 1920, 1080), new Size(360, 500), 300);
        Assert.Equal(334, position.X);
        Assert.Equal(300, position.Y);
    }

    // S5: fall back to the right when there is no room on the left.
    [Fact]
    public void Placement_uses_right_when_left_does_not_fit()
    {
        var position = PanelDetailsPlacement.Place(new Rect(100, 100, 360, 400), new Rect(0, 0, 1920, 1080), new Size(360, 500), 300);
        Assert.Equal(466, position.X);
        Assert.Equal(300, position.Y);
    }

    // S5: top follows Cost, clamped to the monitor work area, including negative origins.
    [Theory]
    [InlineData(-1000, -900)]
    [InlineData(-700, -700)]
    [InlineData(100, -320)]
    public void Placement_clamps_cost_top_to_work_area(double costTop, double expected)
    {
        var position = PanelDetailsPlacement.Place(new Rect(-500, -800, 360, 400), new Rect(-1920, -900, 1920, 1080), new Size(360, 500), costTop);
        Assert.Equal(expected, position.Y);
    }

    // S5: closing the owner closes the side window and releases its content.
    [Fact]
    public void Closing_panel_closes_details()
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            var side = panel.DetailsWindow!;
            panel.Close();
            Assert.False(side.IsVisible);
            Assert.Null(panel.DetailsWindow);
            Assert.Null(side.DetailsScrollViewer.Content);
        });
    }

    // S5: neither hide nor animated dismissal may strand a side window.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hiding_or_dismissing_panel_closes_details(bool dismiss)
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            panel.ShowActivated = false; // Disable the dismissal animation for this lifecycle assertion.
            if (dismiss) panel.Dismiss(); else panel.Hide();
            Assert.Null(panel.DetailsWindow);
            Assert.False(panel.IsVisible);
        });
    }

    // S6: activation may move within the pair without hiding the panel.
    [Fact]
    public void Activation_within_pair_keeps_both_windows_visible()
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            panel.Activate();
            panel.CheckDetailsActivation();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(panel.IsVisible);
            Assert.True(panel.DetailsWindow!.IsVisible);
        });
    }

    // S6: leaving both windows closes details; a settings menu suppresses dismissal.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outside_activation_dismisses_pair_unless_settings_menu_is_open(bool settingsOpen)
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            panel.ActivationLeftApp = () => true; // The test's "outside" window lives in this process.
            panel.IsSettingsMenuOpen = settingsOpen;
            panel.ShowActivated = false; // Keep the outside-dismissal assertion independent of animation clocks.
            var outside = new Window { Width = 100, Height = 100 };
            try
            {
                outside.Show(); outside.Activate();
                panel.CheckDetailsActivation();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                if (settingsOpen)
                {
                    Assert.True(panel.IsVisible);
                    Assert.NotNull(panel.DetailsWindow);
                }
                else
                {
                    Assert.Null(panel.DetailsWindow);
                    Assert.False(panel.IsVisible);
                }
            }
            finally { panel.IsSettingsMenuOpen = false; outside.Close(); }
        });
    }

    // S6: activation still inside AIBar (or not granted yet) never closes the pair.
    [Fact]
    public void Activation_without_another_app_in_front_keeps_the_pair_open()
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            panel.ActivationLeftApp = () => false;
            var outside = new Window { Width = 100, Height = 100 };
            try
            {
                outside.Show(); outside.Activate();
                panel.CheckDetailsActivation();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(panel.IsVisible);
                Assert.NotNull(panel.DetailsWindow);
            }
            finally { outside.Close(); }
        });
    }

        // S5: re-hosting retains explicit history and live DataContext bindings.
    [Fact]
    public void Reopening_reuses_content_and_tracks_data_context()
    {
        WithPanel(panel =>
        {
            panel.OpenDetails(true);
            var detail = (StackPanel)panel.FindName("DetailView");
            var chart = detail.Children.OfType<QuotaHistoryChart>().Single();
            var historyBinding = System.Windows.Data.BindingOperations.GetBinding(chart, QuotaHistoryChart.HistoryProperty);
            Assert.Same(panel, historyBinding!.Source);
            var context = new object();
            panel.DataContext = context;
            Assert.Same(context, panel.DetailsWindow!.DataContext);
            panel.CloseDetails(true);
            panel.OpenDetails(true);
            Assert.Same(detail, panel.DetailsWindow!.DetailsScrollViewer.Content);
            Assert.Same(context, panel.DetailsWindow.DataContext);
        });
    }

    private static Button Cost(MainWindow panel) => (Button)panel.FindName("CostButton");
    private static Button Close(MainWindow panel) => (Button)panel.FindName("CloseDetailsButton");
    private static void Send(UIElement target, Window window, Key key) => target.RaiseEvent(
        new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void WithPanel(Action<MainWindow> test)
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var panel = new MainWindow { Left = 800, Top = 100 };
            var closed = false;
            panel.Closed += (_, _) => closed = true;
            try
            {
                panel.Show(); panel.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                test(panel);
            }
            finally { if (!closed) panel.Close(); }
        });
    }
}
