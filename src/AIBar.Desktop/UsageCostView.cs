using System.ComponentModel;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Refresh-on-open/poll aggregate view. Clear closes publication, cancels, drains, then resets.</summary>
public sealed class UsageCostView(Func<bool> enabled, Func<CancellationToken, ValueTask<UsageCostSummary>> read) : INotifyPropertyChanged, IAiBarClearWork, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private CancellationTokenSource _epoch = new();
    private bool _paused;
    private bool _disposed;
    public UsageCostSummary State { get; private set; } = UsageCostSummary.Unavailable;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async ValueTask RefreshAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            CancellationToken epoch;
            lock (_sync)
            {
                if (_paused || _disposed) return;
                epoch = _epoch.Token;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, epoch);
            var value = enabled() ? await Task.Run(async () => await read(linked.Token), linked.Token) : UsageCostSummary.Unavailable;
            lock (_sync)
            {
                if (_paused || _disposed || linked.IsCancellationRequested) return;
                State = enabled() ? value : UsageCostSummary.Unavailable;
            }
            Notify();
        }
        catch (OperationCanceledException) { }
        finally { _gate.Release(); }
    }

    /// <summary>Invalidate reads admitted under the previous source before queuing the new projection.</summary>
    public async ValueTask RefreshForSourceAsync(CancellationToken token)
    {
        CancellationTokenSource previous;
        lock (_sync)
        {
            if (_disposed || _paused) return;
            previous = _epoch;
            _epoch = new();
            previous.Cancel();
        }
        try { await RefreshAsync(token); }
        finally { previous.Dispose(); }
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken token)
    {
        lock (_sync) { _paused = true; _epoch.Cancel(); }
        await _gate.WaitAsync(token);
        try { State = UsageCostSummary.Unavailable; Notify(); }
        finally { _gate.Release(); }
    }
    private void Notify()
    {
        if (_context is not null && SynchronizationContext.Current != _context)
            _context.Post(_ => PropertyChanged?.Invoke(this, new(nameof(State))), null);
        else PropertyChanged?.Invoke(this, new(nameof(State)));
    }
    public void ResumeAfterClear()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _epoch.Dispose(); _epoch = new(); _paused = false;
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_sync) { if (_disposed) return; _disposed = true; }
        await CancelAndWaitAsync(default);
        lock (_sync) _epoch.Dispose();
    }
}
