using Microsoft.Win32;
using AIBar.Application;

namespace AIBar.Desktop;

public sealed class WindowsLifecycleEvents : IQuotaRefreshLifecycleEvents, IDisposable
{
    private bool _disposed;

    public WindowsLifecycleEvents()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    public event Action? Suspended;
    public event Action? Resumed;
    public event Action? ClockChanged;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.Mode == PowerModes.Suspend) Suspended?.Invoke();
        if (args.Mode == PowerModes.Resume) Resumed?.Invoke();
    }

    private void OnTimeChanged(object? sender, EventArgs args)
    {
        if (!_disposed) ClockChanged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
    }
}
