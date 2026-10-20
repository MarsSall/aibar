using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class PanelMotionRegressionTests
{
    [Fact]
    public void Transition_completion_restores_opaque_content_and_removes_animation_clocks()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var body = new Grid();
            var original = new ScaleTransform(1, 1);
            body.RenderTransform = original;
            var motion = new PanelMotion();
            motion.Begin(body, true, 8, 167, vertical: true);
            Assert.True(DependencyPropertyHelper.GetValueSource(body, UIElement.OpacityProperty).IsAnimated);
            motion.Complete();
            Assert.Equal(1d, body.Opacity);
            Assert.False(DependencyPropertyHelper.GetValueSource(body, UIElement.OpacityProperty).IsAnimated);
            Assert.Same(original, body.RenderTransform);
            motion.Complete(); // Completion, hide and close may race; cleanup is idempotent.
        });
    }

    [Fact]
    public void Dispatcher_deadline_finishes_a_transition_without_a_rendering_frame()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var motion = new PanelMotion();
            var body = new Grid();
            motion.Begin(body, true, 8, 10, vertical: true);
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
            timeout.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }
            Assert.Equal(1d, body.Opacity);
            Assert.False(DependencyPropertyHelper.GetValueSource(body, UIElement.OpacityProperty).IsAnimated);
        });
    }

    [Fact]
    public void Real_show_hide_and_reshow_finish_with_opaque_unanimated_content()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new MainWindow { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = SystemParameters.VirtualScreenLeft + 20, Top = SystemParameters.VirtualScreenTop + 20 };
            try
            {
                window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                // Interrupt the first opening, then open again: no old close/open
                // callback may change the new lifetime's opacity or visibility.
                window.Hide(); window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var frame = new DispatcherFrame();
                var timeout = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
                timeout.Tick += (_, _) => { timeout.Stop(); frame.Continue = false; };
                timeout.Start();
                try { Dispatcher.PushFrame(frame); }
                finally { timeout.Stop(); }
                var body = (FrameworkElement)window.Content;
                Assert.True(window.IsVisible);
                Assert.Equal(1d, body.Opacity);
                Assert.False(DependencyPropertyHelper.GetValueSource(body, UIElement.OpacityProperty).IsAnimated);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Skipped_transition_cancels_previous_fade_and_preserves_opacity()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var motion = new PanelMotion();
            var body = new Grid();
            motion.Begin(body, true, 24, 167);
            motion.Begin(body, false, -24, 167);
            Assert.Equal(1d, body.Opacity);
            Assert.False(DependencyPropertyHelper.GetValueSource(body, UIElement.OpacityProperty).IsAnimated);
            Assert.True(body.RenderTransform.Value.IsIdentity);
        });
    }

    [Fact]
    public void Hidden_offscreen_nonactivated_and_reduced_motion_windows_skip_open_effects()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new MainWindow { ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
            try
            {
                Assert.False(PanelMotion.CanAnimate(window, true, false));
                window.Show(); window.UpdateLayout();
                Assert.False(PanelMotion.CanAnimate(window, true, false));
                window.Left = SystemParameters.VirtualScreenLeft + 20;
                window.Top = SystemParameters.VirtualScreenTop + 20;
                window.ShowActivated = true;
                Assert.True(PanelMotion.CanAnimate(window, true, false));
                Assert.False(PanelMotion.CanAnimate(window, false, false));
                Assert.False(PanelMotion.CanAnimate(window, true, true));
                window.Hide();
                Assert.False(PanelMotion.CanAnimate(window, true, false));
            }
            finally { window.Close(); }
        });
    }
}
