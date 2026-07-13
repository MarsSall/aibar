using AIBar.Desktop;
using System.Windows.Threading;

namespace AIBar.Domain.Tests;

public sealed class HostRuntimeTests
{
    [Fact]
    public async Task Tray_toggle_recreation_activation_and_deactivation_are_deterministic()
    {
        var name = $"AIBar.Tests.{Guid.NewGuid():N}";
        using var instance = new SingleInstanceHost(name);
        var tray = new FakeTray();
        var popover = new FakePopover();
        var taskbar = new FakeRecreationEvents();
        await using var host = new TrayHostRuntime(instance, tray, popover, taskbar, _ => Task.CompletedTask, new ProbeResource(), () => { });
        host.Start();

        tray.Click(); tray.Click();
        Assert.Equal(1, popover.Shows); Assert.Equal(1, popover.Hides);
        popover.IsOwnedDialogActive = true; popover.Deactivate();
        Assert.Equal(1, popover.Hides);
        popover.IsOwnedDialogActive = false; popover.Deactivate();
        Assert.Equal(2, popover.Hides);
        taskbar.Recreate();
        Assert.Equal(2, tray.Shows);
        using var secondary = new SingleInstanceHost(name);
        Assert.True(secondary.RequestActivation());
        PumpUntil(() => popover.Shows == 2);
        Assert.Equal(2, popover.Shows);
        instance.Dispose();
        await host.ExitAsync();
    }

    [Fact]
    public async Task Exit_cancels_waits_then_disposes_every_resource_once_and_blocks_late_activation()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var popover = new FakePopover(); var resource = new ProbeResource();
        var cancellationObserved = false; var exited = 0;
        var host = new TrayHostRuntime(instance, tray, popover, new FakeRecreationEvents(), token =>
        {
            cancellationObserved = token.IsCancellationRequested; resource.Events.Add("cancel"); return Task.CompletedTask;
        }, resource, () => exited++);

        tray.Exit(); tray.Exit();
        instance.Dispose();
        await host.ExitAsync();

        Assert.True(cancellationObserved);
        Assert.Equal(new[] { "cancel", "dispose" }, resource.Events);
        Assert.Equal(1, resource.Disposals); Assert.Equal(1, tray.Disposals); Assert.Equal(1, exited);
        tray.Click(); Assert.Equal(0, popover.Shows);
    }

    [Fact]
    public async Task Exit_failure_is_reported_and_retry_only_repeats_process_exit()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new FakeTray(); var resource = new ProbeResource(); var attempts = 0;
        await using var host = new TrayHostRuntime(instance, tray, new FakePopover(), new FakeRecreationEvents(), _ => Task.CompletedTask, resource,
            () => { if (++attempts == 1) throw new InvalidOperationException("exit failed"); });

        instance.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(host.ExitAsync);
        await host.ExitAsync();

        Assert.Equal(2, attempts); Assert.Equal(1, resource.Disposals); Assert.Equal(1, tray.Disposals);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; var timeout = DateTime.UtcNow.AddSeconds(2);
        timer.Tick += (_, _) => frame.Continue = !condition() && DateTime.UtcNow < timeout;
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Assert.True(condition());
    }

    private sealed class FakeTray : ITrayRuntime
    {
        public event Action? Toggled; public event Action? ExitRequested; public event Action? Recreated;
        public int Shows { get; private set; } public int Disposals { get; private set; }
        public void Show() => Shows++; public void Hide() { } public void Click() => Toggled?.Invoke(); public void Exit() => ExitRequested?.Invoke(); public void Recreate() => Recreated?.Invoke();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class FakePopover : IPopoverRuntime
    {
        public event Action? Deactivated; public bool IsVisible { get; private set; } public bool IsOwnedDialogActive { get; set; }
        public int Shows { get; private set; } public int Hides { get; private set; }
        public void Show() { IsVisible = true; Shows++; } public void Hide() { IsVisible = false; Hides++; } public void Activate() { }
        public void Deactivate() => Deactivated?.Invoke();
    }

    private sealed class FakeRecreationEvents : ITaskbarRecreationEvents
    {
        public event Action? Recreated;
        public void Recreate() => Recreated?.Invoke();
    }
    private sealed class ProbeResource : IAsyncDisposable
    {
        public List<string> Events { get; } = []; public int Disposals { get; private set; }
        public ValueTask DisposeAsync() { Events.Add("dispose"); Disposals++; return ValueTask.CompletedTask; }
    }
}
