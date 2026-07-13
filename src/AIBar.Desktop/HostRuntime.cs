using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

public interface ITrayRuntime : IAsyncDisposable
{
    event Action? Toggled;
    event Action? ExitRequested;
    void Show();
    void Hide();
}

public interface IPopoverRuntime
{
    event Action? Deactivated;
    bool IsVisible { get; }
    bool IsOwnedDialogActive { get; }
    void Show();
    void Hide();
    void Activate();
}

public interface ITaskbarRecreationEvents { event Action? Recreated; }

public sealed class TrayHostRuntime : IAsyncDisposable
{
    private readonly SingleInstanceHost _instance;
    private readonly ITrayRuntime _tray;
    private readonly IPopoverRuntime _popover;
    private readonly ITaskbarRecreationEvents _taskbar;
    private readonly Func<CancellationToken, Task> _awaitCancelledWork;
    private readonly IAsyncDisposable _persistence;
    private readonly Action _exitProcess;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _activationTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Task? _cleanupTask;
    private Task? _exitTask;
    private bool _disposed;

    public TrayHostRuntime(SingleInstanceHost instance, ITrayRuntime tray, IPopoverRuntime popover, ITaskbarRecreationEvents taskbar, Func<CancellationToken, Task> awaitCancelledWork, IAsyncDisposable persistence, Action exitProcess)
    {
        _instance = instance; _tray = tray; _popover = popover; _taskbar = taskbar; _awaitCancelledWork = awaitCancelledWork; _persistence = persistence; _exitProcess = exitProcess;
        _tray.Toggled += Toggle; _tray.ExitRequested += OnExitRequested; _popover.Deactivated += OnDeactivated; _taskbar.Recreated += RecreateTray; _instance.ActivationRequested += ShowPopover;
        _activationTimer.Tick += DispatchPendingActivation;
    }

    public void Start() { if (_disposed) return; RunSafely(_tray.Show); _activationTimer.Start(); }
    public Task ExitAsync()
    {
        lock (_shutdown)
        {
        _cleanupTask ??= CleanupAsync();
        return _exitTask ??= ExitAttemptAsync(_cleanupTask);
        }
    }

    private async Task CleanupAsync()
    {
        _disposed = true; _activationTimer.Stop(); _activationTimer.Tick -= DispatchPendingActivation; _shutdown.Cancel();
        try { await _awaitCancelledWork(_shutdown.Token); } catch (Exception) { }
        try { await _persistence.DisposeAsync(); } catch (Exception) { }
        Detach();
        try { await _tray.DisposeAsync(); } catch (Exception) { }
        _shutdown.Dispose();
    }
    private async Task ExitAttemptAsync(Task cleanup)
    {
        await cleanup; await Task.Yield();
        try { _exitProcess(); }
        catch { lock (_shutdown) _exitTask = null; throw; }
    }
    private void DispatchPendingActivation(object? sender, EventArgs args)
    {
        if (!_disposed) RunSafely(_instance.DispatchPendingActivation);
    }

    private void Toggle()
    {
        if (_disposed) return;
        if (_popover.IsVisible) RunSafely(_popover.Hide); else ShowPopover();
    }
    private void ShowPopover()
    {
        if (_disposed) return;
        RunSafely(() => { _popover.Show(); _popover.Activate(); });
    }
    private void OnDeactivated()
    {
        if (!_disposed && !_popover.IsOwnedDialogActive) RunSafely(_popover.Hide);
    }
    private void RecreateTray()
    {
        if (!_disposed) RunSafely(() => { _tray.Hide(); _tray.Show(); });
    }
    private void OnExitRequested() => _ = ExitSafelyAsync();
    private async Task ExitSafelyAsync() { try { await ExitAsync(); } catch (Exception) { } }
    private void Detach()
    {
        _tray.Toggled -= Toggle; _tray.ExitRequested -= OnExitRequested; _popover.Deactivated -= OnDeactivated; _taskbar.Recreated -= RecreateTray; _instance.ActivationRequested -= ShowPopover;
    }
    private static void RunSafely(Action action) { try { action(); } catch (Exception) { } }
    public ValueTask DisposeAsync() => new(ExitAsync());
}

public sealed class WindowsTrayRuntime : ITrayRuntime
{
    private readonly Forms.NotifyIcon _icon = new() { Icon = SystemIcons.Application, Text = "AIBar", Visible = false };
    public WindowsTrayRuntime()
    {
        var menu = new Forms.ContextMenuStrip(); var exit = new Forms.ToolStripMenuItem("Exit AIBar");
        exit.Click += (_, _) => ExitRequested?.Invoke(); menu.Items.Add(exit); _icon.ContextMenuStrip = menu;
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggled?.Invoke(); };
    }
    public event Action? Toggled;
    public event Action? ExitRequested;
    public void Show() => _icon.Visible = true;
    public void Hide() => _icon.Visible = false;
    public ValueTask DisposeAsync() { _icon.Dispose(); return ValueTask.CompletedTask; }
}

public sealed class WpfPopoverRuntime : IPopoverRuntime
{
    private readonly Window _window;
    public WpfPopoverRuntime(Window window)
    {
        _window = window;
        _window.Deactivated += (_, _) => Deactivated?.Invoke();
    }
    public event Action? Deactivated;
    public bool IsVisible => _window.IsVisible;
    public bool IsOwnedDialogActive => _window.OwnedWindows.OfType<Window>().Any(candidate => candidate.IsVisible);
    public void Show() { if (!_window.IsVisible) _window.Show(); }
    public void Hide() => _window.Hide();
    public void Activate() => _window.Activate();
}

public sealed class TaskbarRecreationMonitor : ITaskbarRecreationEvents, IDisposable
{
    private readonly HwndSource _source;
    private readonly int _message = RegisterWindowMessage("TaskbarCreated");
    public TaskbarRecreationMonitor(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle())!;
        _source.AddHook(WndProc);
    }
    public event Action? Recreated;
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _message) Recreated?.Invoke();
        return IntPtr.Zero;
    }
    public void Dispose() => _source.RemoveHook(WndProc);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string value);
}

public sealed class EmptyAsyncResource : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
