using AIBar.Domain;

namespace AIBar.Application;

public interface IQuotaRefreshLifecycleEvents
{
    event Action? Suspended;
    event Action? Resumed;
    event Action? ClockChanged;
}

public sealed class QuotaRefreshLifecycleAdapter : IAsyncDisposable
{
    private readonly IQuotaRefreshLifecycleEvents _events;
    private readonly QuotaRefreshCoordinator _coordinator;
    private bool _disposed;

    public QuotaRefreshLifecycleAdapter(IQuotaRefreshLifecycleEvents events, QuotaRefreshCoordinator coordinator)
    {
        _events = events;
        _coordinator = coordinator;
        _events.Suspended += OnSuspended;
        _events.Resumed += OnResumed;
        _events.ClockChanged += OnClockChanged;
    }

    private void OnSuspended() => Forward(RefreshTrigger.Sleep);
    private void OnResumed() => Forward(RefreshTrigger.Resume);
    private void OnClockChanged() => Forward(RefreshTrigger.ClockChanged);

    private async void Forward(RefreshTrigger trigger)
    {
        if (_disposed) return;
        try { await _coordinator.ReevaluateAsync(trigger, CancellationToken.None); }
        catch (Exception) { }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _events.Suspended -= OnSuspended;
        _events.Resumed -= OnResumed;
        _events.ClockChanged -= OnClockChanged;
        return ValueTask.CompletedTask;
    }
}
