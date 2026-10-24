using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using Forms = System.Windows.Forms;

namespace AIBar.Domain.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class DockWindowTests
{
    [Fact]
    public void Strip_is_topmost_transparent_nonactivating_toolwindow_and_apply_controls_visibility()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var window = new DockWindow(new Presentation(), new(), () => { }, (_, _) => ValueTask.CompletedTask);
            try
            {
                Assert.True(window.Topmost); Assert.True(window.AllowsTransparency);
                Assert.False(window.ShowInTaskbar); Assert.False(window.ShowActivated);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal("AIBar floating quota widget", AutomationProperties.GetName(window));
                Assert.False(window.IsVisible);
                window.Apply(new(true));
                Assert.True(window.IsVisible);
                var handle = new WindowInteropHelper(window).Handle;
                var style = GetWindowLong(handle, -20);
                Assert.NotEqual(0, style & 0x08000000); // WS_EX_NOACTIVATE
                Assert.NotEqual(0, style & 0x80); // WS_EX_TOOLWINDOW
                Assert.Equal(new IntPtr(3), SendMessage(handle, 0x21, IntPtr.Zero, IntPtr.Zero));
                window.Apply(new()); Assert.False(window.IsVisible);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Providers_are_ordered_opt_in_only_and_nested_claude_changes_update_live()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var presentation = new Presentation();
            var window = new DockWindow(presentation, new(), () => { }, (_, _) => ValueTask.CompletedTask);
            try
            {
                window.Apply(new(true)); Pump();
                Assert.Equal("AIBar", Assert.Single(window.Rings).Provider);
                presentation.SetState(DockRingTests.Reading()); Pump();
                Assert.Equal("Codex", Assert.Single(window.Rings).Provider);
                presentation.Claude.SetEnabled(true); Pump();
                Assert.Equal(new[] { "Codex", "Claude" }, window.Rings.Select(r => r.Provider).ToArray());
                var claude = window.Rings[1];
                presentation.Claude.SetState(DockRingTests.Reading() with { Primary = new("5-hour quota", 100, "01h 00m") }); Pump();
                Assert.Same(claude, window.Rings[1]);
                Assert.Contains("100 percent used", AutomationProperties.GetName(claude));
                Assert.Contains("Claude · Max", claude.Details);
                window.Apply(new(true, "left"));
                Assert.True(window.Height > window.Width);
                window.Apply(new(true, "bottom"));
                Assert.True(window.Width > window.Height);
                presentation.SetState(presentation.State with { IsPrivateIntegrationDisabled = true }); Pump();
                Assert.Equal("Claude", Assert.Single(window.Rings).Provider);
                presentation.Claude.SetEnabled(false); Pump();
                Assert.Equal("AIBar", Assert.Single(window.Rings).Provider);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Click_opens_panel_but_drag_snaps_and_persists_without_opening()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var opened = 0;
            var saved = new List<DockOptions>();
            var window = new DockWindow(new Presentation(), new(), () => opened++, (options, _) =>
            { saved.Add(options); return ValueTask.CompletedTask; });
            try
            {
                window.Apply(new(true));
                var start = window.PointToScreen(new Point(20, 20));
                window.BeginPointer(start);
                window.EndPointerAsync(start).GetAwaiter().GetResult();
                Assert.Equal(1, opened); Assert.Empty(saved);
                var screen = Forms.Screen.PrimaryScreen!;
                var target = new Point(screen.WorkingArea.Left + 1, screen.WorkingArea.Top + screen.WorkingArea.Height / 2d);
                window.BeginPointer(start);
                window.MovePointer(target);
                window.EndPointerAsync(target).GetAwaiter().GetResult();
                Assert.Equal(1, opened);
                var options = Assert.Single(saved);
                Assert.True(options.Enabled); Assert.Equal("left", options.Edge);
                Assert.Equal(.5, options.Offset, 3); Assert.Equal(screen.DeviceName, options.Monitor);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Failed_persistence_is_observable_and_absent_monitor_falls_back_inside_primary_work_area()
    {
        WpfTestApplicationHost.Run(_ =>
        {
            var failure = new IOException("synthetic save failure");
            var window = new DockWindow(new Presentation(), new(), () => { }, (_, _) => ValueTask.FromException(failure));
            try
            {
                window.Apply(new(true, Monitor: "missing-synthetic-monitor"));
                var screen = Forms.Screen.PrimaryScreen!;
                var work = screen.WorkingArea;
                var scale = NotificationNative.MonitorScale(screen);
                var origin = window.PointToScreen(new Point());
                Assert.InRange(origin.X, work.Left - 1, work.Right);
                Assert.InRange(origin.Y, work.Top - 1, work.Bottom);
                Assert.True(origin.X + window.Width * scale.X <= work.Right + 1);
                Assert.True(origin.Y + window.Height * scale.Y <= work.Bottom + 1);
                window.BeginPointer(window.PointToScreen(new Point(20, 20)));
                window.EndPointerAsync(new Point(work.Left + work.Width / 2d, work.Top + 1)).GetAwaiter().GetResult();
                Assert.Same(failure, window.LastPersistenceError);
            }
            finally { window.Close(); }
        });
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    // Binding-compatible synthetic presentations; never start provider runtimes or polling.
    public sealed class Presentation : INotifyPropertyChanged
    {
        public BetaPresentationState State { get; private set; } = DockRingTests.Reading() with { IsPrivateIntegrationDisabled = true };
        public ClaudePresentation Claude { get; } = new();
        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetState(BetaPresentationState state) { State = state; PropertyChanged?.Invoke(this, new(nameof(State))); }
    }
    public sealed class ClaudePresentation : INotifyPropertyChanged
    {
        public BetaPresentationState State { get; private set; } = DockRingTests.Reading();
        public bool IsEnabled { get; private set; }
        public string PlanLabel => "Max";
        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetState(BetaPresentationState state) { State = state; PropertyChanged?.Invoke(this, new(nameof(State))); }
        public void SetEnabled(bool enabled) { IsEnabled = enabled; PropertyChanged?.Invoke(this, new(nameof(IsEnabled))); }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wp, IntPtr lp);
}
