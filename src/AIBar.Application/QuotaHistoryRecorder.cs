using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Nonblocking retrieval observer. Unlike presentation publishers, history never coalesces
/// successful retrievals. SQLite is always accessed on a background worker, never in authority callbacks.</summary>
public sealed class QuotaHistoryRecorder : IAiBarClearWork, IAsyncDisposable
{
    private readonly QuotaRefreshCoordinator _authority;
    private readonly IQuotaHistoryStore _store;
    private readonly IClock _clock;
    private readonly QuotaHistoryProvider _provider;
    private readonly QuotaHistorySource _source;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly Queue<(QuotaHistorySample[] Samples, DateTimeOffset Now, CancellationToken Token)> _pending = new();
    private CancellationTokenSource _cancellation = new();
    private Task _drain = Task.CompletedTask;
    private bool _draining;
    private bool _paused;
    private bool _disposed;
    private long _lastRetrieval;

    public QuotaHistoryRecorder(QuotaRefreshCoordinator authority, IQuotaHistoryStore store, IClock clock,
        QuotaHistoryProvider provider, QuotaHistorySource source)
    {
        _authority = authority; _store = store; _clock = clock; _provider = provider; _source = source;
        _lastRetrieval = authority.RetrievalGeneration;
        _authority.AuthorityUpdated += Accept;
    }

    private void Accept(QuotaAuthorityUpdate update)
    {
        lock (_gate)
        {
            if (_disposed || update.RetrievalGeneration <= _lastRetrieval) return;
            _lastRetrieval = update.RetrievalGeneration;
            if (_paused || update.State.Snapshot is not { } snapshot) return;
            // Codex keeps one coordinator across source changes. Use the origin
            // stamped at retrieval, even if this callback runs after a later handoff.
            var source = _provider == QuotaHistoryProvider.Codex
                ? _authority.WasRemote(update) ? QuotaHistorySource.CodexRemote : QuotaHistorySource.CodexLocal
                : _source;
            var samples = new List<QuotaHistorySample>(2);
            if (snapshot.Primary is { } primary) samples.Add(new(_provider, QuotaHistoryWindow.FiveHour, primary.PercentageUsed, primary.ResetAt, snapshot.RetrievedAt, source));
            if (snapshot.Weekly is { } weekly) samples.Add(new(_provider, QuotaHistoryWindow.Weekly, weekly.PercentageUsed, weekly.ResetAt, snapshot.RetrievedAt, source));
            _pending.Enqueue((samples.ToArray(), _clock.UtcNow, _cancellation.Token));
            if (!_draining) { _draining = true; _drain = Task.Run(DrainCoreAsync); }
        }
    }

    /// <summary>Waits for samples queued before this call. Does not initiate any provider retrieval.</summary>
    public async ValueTask DrainAsync(CancellationToken cancellationToken)
    {
        Task drain; lock (_gate) drain = _drain;
        await drain.WaitAsync(cancellationToken);
    }

    private async Task DrainCoreAsync()
    {
        while (true)
        {
            (QuotaHistorySample[] Samples, DateTimeOffset Now, CancellationToken Token) work;
            lock (_gate)
            {
                if (_pending.Count == 0) { _draining = false; return; }
                work = _pending.Dequeue();
            }
            try
            {
                work.Token.ThrowIfCancellationRequested();
                await _store.AppendAsync(work.Samples, work.Now, work.Token);
            }
            catch (Exception) { /* Optional history cannot break quota authority or expose private exception details. */ }
        }
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            CancellationTokenSource prior; Task drain;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _paused = true; _pending.Clear(); prior = _cancellation; _cancellation = new(); drain = _drain;
            }
            prior.Cancel();
            // Once privacy cancellation starts, finish draining even if the caller cancels.
            // A cancellation-ignoring store must not write after deletion.
            try { await drain; }
            finally { prior.Dispose(); }
        }
        finally { _lifecycle.Release(); }
    }

    public void ResumeAfterClear()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lastRetrieval = _authority.RetrievalGeneration;
            _paused = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            Task drain;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true; _paused = true; drain = _drain;
            }
            _authority.AuthorityUpdated -= Accept;
            // Normal shutdown/source handoff preserves every already accepted sample.
            // Clear explicitly uses CancelAndWaitAsync to discard pending samples instead.
            await drain;
            lock (_gate) _pending.Clear();
            _cancellation.Dispose();
        }
        finally { _lifecycle.Release(); }
    }
}
