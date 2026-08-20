using System.Runtime.ExceptionServices;
using AIBar.Domain;

namespace AIBar.Application;

public sealed class QuotaRefreshCoordinator : IAsyncDisposable, IAiBarClearWork
{
    private readonly IQuotaSnapshotStore _store;
    private readonly IQuotaProvider _provider;
    private readonly IClock _clock;
    private readonly FreshnessPolicy _freshness;
    private readonly TimeSpan _minimumPollInterval;
    private readonly object _gate = new();
    private readonly Queue<QuotaAuthorityUpdate> _updates = new();
    private CancellationTokenSource _lifetime = new();
    private Task? _active;
    private QuotaRefreshState _state;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private long _generation;
    private long _eventSequence;
    private long _retrievalGeneration;
    private bool _draining;
    private bool _disposed;
    private bool _paused;

    public QuotaRefreshCoordinator(IQuotaSnapshotStore store, IQuotaProvider provider, IClock clock, FreshnessPolicy freshness, TimeSpan minimumPollInterval)
    {
        _store = store; _provider = provider; _clock = clock; _freshness = freshness;
        _minimumPollInterval = minimumPollInterval;
        _state = new(null, FreshnessState.Unavailable, false, null, null);
    }

    public QuotaRefreshState State { get { lock (_gate) return _state; } }
    public event Action<QuotaAuthorityUpdate>? AuthorityUpdated;
    public event Action<QuotaRefreshState>? StateChanged;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate) { ThrowIfDisposed(); generation = _generation; }
        QuotaSnapshot? snapshot;
        try { snapshot = await _store.LoadAsync(cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            Transition(_ => new(null, FreshnessState.Unavailable, false, RefreshFailure(), null), generation);
            return;
        }
        Transition(_ => new(snapshot, _freshness.Evaluate(snapshot, _clock.UtcNow), false, null, null), generation);
    }

    public ValueTask ReevaluateAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        lock (_gate) ThrowIfDisposed();
        Transition(state =>
        {
            var freshness = _freshness.Evaluate(state.Snapshot, _clock.UtcNow);
            return freshness == state.Freshness ? null : state with { Freshness = freshness };
        });
        return trigger == RefreshTrigger.Sleep
            ? ValueTask.CompletedTask
            : RefreshAsync(trigger, cancellationToken);
    }

    public ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        Task active; TaskCompletionSource? start = null; long generation = 0; CancellationToken lifetime = default;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_paused) return ValueTask.CompletedTask;
            if (_active is not null && !_active.IsCompleted) active = _active;
            else
            {
                _active = null;
                if (!Eligible(trigger)) return ValueTask.CompletedTask;
                _lastAttempt = _clock.UtcNow;
                generation = _generation; lifetime = _lifetime.Token;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                active = _active = start.Task;
            }
        }
        if (start is not null) _ = CompleteRefreshAsync(start, generation, lifetime);
        return cancellationToken.CanBeCanceled ? new(active.WaitAsync(cancellationToken)) : new(active);
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await CancelAndWaitAsync(cancellationToken);
        try
        {
            await _store.ClearAsync(cancellationToken);
            Transition(_ => new(null, FreshnessState.Unavailable, false, null, null));
        }
        finally { ResumeAfterClear(); }
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource cancelled; Task? active; bool drain;
        lock (_gate)
        {
            ThrowIfDisposed(); _generation++; _paused = true; cancelled = _lifetime; _lifetime = new(); active = _active; _active = null;
            drain = QueueLocked(_state.IsLoading ? _state with { IsLoading = false } : null, false);
        }
        cancelled.Cancel();
        try
        {
            if (drain) Drain();
            if (active is not null) await active.WaitAsync(cancellationToken);
        }
        finally { cancelled.Dispose(); }
    }

    public void ResumeAfterClear() { lock (_gate) if (!_disposed) _paused = false; }

    private bool Eligible(RefreshTrigger trigger) => trigger == RefreshTrigger.Manual ||
        _freshness.Evaluate(_state.Snapshot, _clock.UtcNow) != FreshnessState.Current &&
        _clock.UtcNow - _lastAttempt >= _minimumPollInterval;

    private async Task CompleteRefreshAsync(TaskCompletionSource completion, long generation, CancellationToken lifetime)
    {
        try { await RefreshCoreAsync(generation, lifetime); completion.TrySetResult(); }
        catch (Exception exception) { completion.TrySetException(exception); }
        finally { lock (_gate) if (generation == _generation && ReferenceEquals(_active, completion.Task)) _active = null; }
    }

    private async Task RefreshCoreAsync(long generation, CancellationToken lifetime)
    {
        if (!Transition(state => state with { IsLoading = true, Failure = null }, generation)) return;
        QuotaProviderResult result;
        try { result = await _provider.GetQuotaAsync(lifetime); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception) { Transition(FailureState, generation); return; }
        if (result.Snapshot is null)
        {
            Transition(state => state with { IsLoading = false, Freshness = _freshness.Evaluate(state.Snapshot, _clock.UtcNow), Failure = result.Failure }, generation);
            return;
        }
        var snapshot = new QuotaSnapshot(result.Snapshot.Primary, result.Snapshot.Weekly, result.Snapshot.RetrievedAt, result.ResetCredits);
        try { await _store.SaveAsync(snapshot, lifetime); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch (Exception) { Transition(FailureState, generation); return; }
        Transition(_ => new(snapshot, _freshness.Evaluate(snapshot, _clock.UtcNow), false, null, result.OptionalFailure), generation, retrieval: true);
    }

    private QuotaRefreshState FailureState(QuotaRefreshState state) => state with
    {
        IsLoading = false,
        Freshness = _freshness.Evaluate(state.Snapshot, _clock.UtcNow),
        Failure = RefreshFailure()
    };

    private static QuotaFailure RefreshFailure() => new(QuotaErrorKind.Unavailable, "quota_refresh_failed");

    private bool Transition(Func<QuotaRefreshState, QuotaRefreshState?> transition, long? generation = null, bool retrieval = false)
    {
        bool drain;
        lock (_gate)
        {
            if (_disposed || generation is not null && generation != _generation) return false;
            drain = QueueLocked(transition(_state), retrieval);
        }
        if (drain) Drain();
        return true;
    }

    private bool QueueLocked(QuotaRefreshState? state, bool retrieval)
    {
        if (state is null) return false;
        _state = state;
        if (retrieval) _retrievalGeneration++;
        _updates.Enqueue(new(state, ++_eventSequence, _retrievalGeneration));
        if (_draining) return false;
        return _draining = true;
    }

    private void Drain()
    {
        Exception? failure = null;
        while (true)
        {
            QuotaAuthorityUpdate update;
            lock (_gate)
            {
                if (_updates.Count == 0) { _draining = false; break; }
                update = _updates.Dequeue();
            }
            try { AuthorityUpdated?.Invoke(update); } catch (Exception exception) { failure ??= exception; }
            try { StateChanged?.Invoke(update.State); } catch (Exception exception) { failure ??= exception; }
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        Task? active; CancellationTokenSource lifetime;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _paused = true; _generation++; lifetime = _lifetime; active = _active; _active = null;
        }
        lifetime.Cancel();
        if (active is not null) try { await active; } catch (OperationCanceledException) { }
        lifetime.Dispose();
    }
}
