using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Read-only history access. Clear invalidates in-flight readers without waiting for UI delivery.</summary>
public sealed class PanelPaceHistory(IQuotaHistoryStore store, IClock clock) : IAiBarClearWork, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object _gate = new();
    private CancellationTokenSource? _epoch = new();
    private bool _paused;
    private bool _disposed;
    private long _revision;
    public event Action? Invalidated;
    public event Action? PaceRefreshRequested;
    public long Revision { get { lock (_gate) return _revision; } }
    public bool CanPublish(long revision)
    {
        lock (_gate) return !_disposed && !_paused && revision == _revision;
    }
    public void RefreshPace()
    {
        if (!_dispatcher.CheckAccess())
        {
            if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(RefreshPace);
            return;
        }
        lock (_gate) { if (_paused || _disposed) return; }
        PaceRefreshRequested?.Invoke();
    }
    public DateTimeOffset Now => clock.UtcNow;

    public async Task<IReadOnlyList<QuotaHistorySample>> ReadPaceAsync(QuotaHistoryProvider provider)
    {
        CancellationToken token; long revision;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paused) return [];
            token = _epoch!.Token; revision = _revision;
        }
        var now = Now;
        var samples = await Task.Run(async () =>
        {
            var primary = await store.QueryAsync(provider, QuotaHistoryWindow.FiveHour, now.AddHours(-5), now, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var weekly = await store.QueryAsync(provider, QuotaHistoryWindow.Weekly, now.AddDays(-7), now, token).ConfigureAwait(false);
            return primary.Concat(weekly).ToArray();
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!CanPublish(revision)) throw new OperationCanceledException();
        return samples;
    }

    public async Task<QuotaHistoryChartData> ReadChartAsync(QuotaHistoryChartRange range, CancellationToken cancellationToken)
    {
        CancellationTokenSource linked; long revision;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_paused) throw new OperationCanceledException();
            linked = CancellationTokenSource.CreateLinkedTokenSource(_epoch!.Token, cancellationToken);
            revision = _revision;
        }
        using (linked)
        {
            var result = await Task.Run(() => QuotaHistoryChartData.LoadAsync(store, range, Now, linked.Token), linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            if (!CanPublish(revision)) throw new OperationCanceledException();
            return result;
        }
    }

    public ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancellationTokenSource? retired;
        lock (_gate)
        {
            if (_disposed || _paused) return ValueTask.CompletedTask;
            _paused = true; ++_revision;
            retired = _epoch; _epoch = null;
        }
        if (retired is not null) _ = CancelAndDisposeAsync(retired);
        // The store owns draining database access. Waiting for either its query or the UI here
        // deadlocks clear when composition was created on a non-pumping dispatcher thread.
        if (_dispatcher.CheckAccess()) Invalidated?.Invoke();
        else if (!_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(() => Invalidated?.Invoke());
        return ValueTask.CompletedTask;
    }
    public void ResumeAfterClear()
    {
        lock (_gate)
        {
            if (_disposed || !_paused) return;
            _epoch = new(); _paused = false;
        }
    }
    public void Dispose()
    {
        CancellationTokenSource? retired;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _paused = true; ++_revision;
            retired = _epoch; _epoch = null;
            Invalidated = null; PaceRefreshRequested = null;
        }
        if (retired is not null) _ = CancelAndDisposeAsync(retired);
    }

    // A detached source has exactly one owner. Cancellation callbacks run off-thread;
    // neither clear nor disposal waits for a cancellation-ignoring store or UI continuation.
    internal static async Task CancelAndDisposeAsync(CancellationTokenSource source)
    {
        try { await source.CancelAsync().ConfigureAwait(false); }
        catch (Exception) { /* Optional-history cancellation callbacks must not fail clear/shutdown. */ }
        finally { source.Dispose(); }
    }
}
