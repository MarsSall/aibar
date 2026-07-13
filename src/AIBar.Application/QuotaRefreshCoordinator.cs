using AIBar.Domain;

namespace AIBar.Application;

public sealed class QuotaRefreshCoordinator : IAsyncDisposable
{
    private readonly IQuotaSnapshotStore _store;
    private readonly IQuotaProvider _provider;
    private readonly IClock _clock;
    private readonly FreshnessPolicy _freshness;
    private readonly TimeSpan _minimumPollInterval;
    private readonly object _gate = new();
    private CancellationTokenSource _lifetime = new();
    private Task? _active;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private long _generation;
    private bool _disposed;

    public QuotaRefreshCoordinator(IQuotaSnapshotStore store, IQuotaProvider provider, IClock clock, FreshnessPolicy freshness, TimeSpan minimumPollInterval)
    {
        _store = store; _provider = provider; _clock = clock; _freshness = freshness;
        _minimumPollInterval = minimumPollInterval;
        State = new(null, FreshnessState.Unavailable, false, null, null);
    }

    public QuotaRefreshState State { get; private set; }
    public event Action<QuotaRefreshState>? StateChanged;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        try
        {
            var snapshot = await _store.LoadAsync(cancellationToken);
            Publish(new(snapshot, _freshness.Evaluate(snapshot, _clock.UtcNow), false, null, null));
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Publish(new(null, FreshnessState.Unavailable, false, RefreshFailure(), null));
        }
    }

    public ValueTask ReevaluateAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        QuotaRefreshState? changedState = null;
        Action<QuotaRefreshState>? handler = null;
        lock (_gate)
        {
            ThrowIfDisposed();
            var freshness = _freshness.Evaluate(State.Snapshot, _clock.UtcNow);
            if (freshness != State.Freshness)
            {
                changedState = State = State with { Freshness = freshness };
                handler = StateChanged;
            }
        }
        if (changedState is not null) handler?.Invoke(changedState);
        return trigger == RefreshTrigger.Sleep
            ? ValueTask.CompletedTask
            : RefreshAsync(trigger, cancellationToken);
    }

    public ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        Task active;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_active is not null && !_active.IsCompleted) active = _active;
            else
            {
                _active = null;
                if (!Eligible(trigger)) return ValueTask.CompletedTask;
                _lastAttempt = _clock.UtcNow;
                active = _active = RefreshCoreAsync(_generation, _lifetime.Token);
            }
        }
        return cancellationToken.CanBeCanceled ? new(active.WaitAsync(cancellationToken)) : new(active);
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource cancelled;
        lock (_gate)
        {
            ThrowIfDisposed();
            _generation++;
            cancelled = _lifetime;
            _lifetime = new();
            _active = null;
        }
        cancelled.Cancel(); cancelled.Dispose();
        try { await _store.ClearAsync(cancellationToken); }
        finally { Publish(new(null, FreshnessState.Unavailable, false, null, null)); }
    }

    private bool Eligible(RefreshTrigger trigger) => trigger == RefreshTrigger.Manual ||
        _freshness.Evaluate(State.Snapshot, _clock.UtcNow) != FreshnessState.Current &&
        _clock.UtcNow - _lastAttempt >= _minimumPollInterval;

    private async Task RefreshCoreAsync(long generation, CancellationToken lifetime)
    {
        Publish(State with { IsLoading = true, Failure = null });
        try
        {
            var result = await _provider.GetQuotaAsync(lifetime);
            if (!CanPublish(generation)) return;
            if (result.Snapshot is null)
            {
                Publish(State with { IsLoading = false, Freshness = _freshness.Evaluate(State.Snapshot, _clock.UtcNow), Failure = result.Failure });
                return;
            }
            var snapshot = new QuotaSnapshot(result.Snapshot.Primary, result.Snapshot.Weekly, result.Snapshot.RetrievedAt, result.ResetCredits);
            await _store.SaveAsync(snapshot, lifetime);
            if (CanPublish(generation)) Publish(new(snapshot, _freshness.Evaluate(snapshot, _clock.UtcNow), false, null, result.OptionalFailure));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            if (CanPublish(generation)) Publish(State with { IsLoading = false, Freshness = _freshness.Evaluate(State.Snapshot, _clock.UtcNow), Failure = RefreshFailure() });
        }
        finally { lock (_gate) if (generation == _generation) _active = null; }
    }

    private static QuotaFailure RefreshFailure() => new(QuotaErrorKind.Unavailable, "quota_refresh_failed");

    private bool CanPublish(long generation) { lock (_gate) return !_disposed && generation == _generation; }
    private void Publish(QuotaRefreshState state) { State = state; StateChanged?.Invoke(state); }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        Task? active;
        lock (_gate) { if (_disposed) return; _disposed = true; _lifetime.Cancel(); active = _active; }
        if (active is not null) try { await active; } catch (OperationCanceledException) { }
        _lifetime.Dispose();
    }
}
