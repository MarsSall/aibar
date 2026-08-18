using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaRefreshCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Publishes_cached_state_before_refreshing_and_skips_fresh_polling()
    {
        var store = new FakeStore(Snapshot(Now.AddMinutes(-2)));
        var provider = new FakeProvider(new QuotaProviderResult(Snapshot(Now), null));
        await using var coordinator = Create(store, provider);

        await coordinator.InitializeAsync(default);
        Assert.Equal(42, coordinator.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(FreshnessState.Current, coordinator.State.Freshness);
        await coordinator.RefreshAsync(RefreshTrigger.Poll, default);

        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Coalesces_concurrent_refreshes_and_manual_bypasses_freshness()
    {
        var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(gate.Task);
        var store = new FakeStore(Snapshot(Now));
        await using var coordinator = Create(store, provider);
        await coordinator.InitializeAsync(default);

        var first = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask();
        var second = coordinator.RefreshAsync(RefreshTrigger.PopoverOpened, default).AsTask();
        Assert.Same(first, second);
        Assert.True(coordinator.State.IsLoading);
        gate.SetResult(new(Snapshot(Now.AddMinutes(1)), null));
        await first;

        Assert.Equal(1, provider.Calls);
        Assert.Equal(1, store.Saves);
        Assert.Equal(42, coordinator.State.Snapshot!.Primary!.PercentageUsed);
    }

    [Fact]
    public async Task Preserves_snapshot_timestamp_and_overlays_failures_or_optional_degradation()
    {
        var cached = Snapshot(Now.AddMinutes(-11));
        var provider = new FakeProvider(new QuotaProviderResult(null, new(QuotaErrorKind.Network, "quota_network")));
        await using var coordinator = Create(new FakeStore(cached), provider);
        await coordinator.InitializeAsync(default);
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default);

        Assert.Equal(cached, coordinator.State.Snapshot);
        Assert.Equal(cached.RetrievedAt, coordinator.State.Snapshot!.RetrievedAt);
        Assert.Equal(FreshnessState.Stale, coordinator.State.Freshness);
        Assert.Equal(QuotaErrorKind.Network, coordinator.State.Failure!.Kind);

        provider.Result = new(Snapshot(Now), null, null, new(QuotaErrorKind.Service, "optional_unavailable"));
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(FreshnessState.Current, coordinator.State.Freshness);
        Assert.Null(coordinator.State.Failure);
        Assert.Equal("optional_unavailable", coordinator.State.OptionalFailure!.SafeCode);
    }

    [Fact]
    public async Task Cancellation_and_clear_suppress_late_publication_and_make_state_unavailable()
    {
        var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeStore(Snapshot(Now));
        var provider = new FakeProvider(gate.Task);
        await using var coordinator = Create(store, provider);
        await coordinator.InitializeAsync(default);
        var refresh = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask();

        var clear = coordinator.ClearAsync(default).AsTask();
        Assert.False(clear.IsCompleted); gate.SetResult(new(Snapshot(Now.AddHours(1)), null)); await clear;
        await refresh;

        Assert.Null(coordinator.State.Snapshot);
        Assert.Equal(FreshnessState.Unavailable, coordinator.State.Freshness);
        Assert.Equal(1, store.Clears);
    }

[Fact]
public async Task Caller_cancellation_only_cancels_its_wait()
{
        var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(gate.Task, observeCancellation: true);
        await using var coordinator = Create(new FakeStore(null), provider);
        using var cancelled = new CancellationTokenSource();
        var first = coordinator.RefreshAsync(RefreshTrigger.Poll, cancelled.Token).AsTask();
        var joined = coordinator.RefreshAsync(RefreshTrigger.Poll, default).AsTask();
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(coordinator.State.IsLoading); gate.SetResult(new(Snapshot(Now), null)); await joined;
        Assert.Equal(1, provider.Calls); Assert.False(coordinator.State.IsLoading);
}

[Fact]
public async Task Unavailable_attempt_throttles_until_clock_advances_but_manual_bypasses()
{
        var clock = new MutableClock(Now);
        var provider = new FakeProvider(new(null, new(QuotaErrorKind.Network, "network")));
        await using var coordinator = Create(new FakeStore(null), provider, clock);
        await coordinator.RefreshAsync(RefreshTrigger.Poll, default);
        await coordinator.RefreshAsync(RefreshTrigger.Resume, default); Assert.Equal(1, provider.Calls);
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        await coordinator.RefreshAsync(RefreshTrigger.PopoverOpened, default); Assert.Equal(2, provider.Calls);
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); Assert.Equal(3, provider.Calls);
}

[Fact]
public async Task Provider_and_store_exceptions_publish_terminal_failure()
{
        await using var first = Create(new FakeStore(null), new FakeProvider(Task.FromException<QuotaProviderResult>(new InvalidOperationException())));
        await first.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.False(first.State.IsLoading); Assert.Equal(QuotaErrorKind.Unavailable, first.State.Failure!.Kind);
        await using var second = Create(new FakeStore(null) { ThrowOnSave = true }, new FakeProvider(new(Snapshot(Now), null)));
        await second.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.False(second.State.IsLoading); Assert.Equal("quota_refresh_failed", second.State.Failure!.SafeCode);
}

[Fact]
public async Task Detached_refresh_cannot_clear_new_active_refresh()
{
        var oldGate = new TaskCompletionSource<QuotaProviderResult>(); var newGate = new TaskCompletionSource<QuotaProviderResult>();
        var provider = new SequencedProvider(oldGate.Task, newGate.Task);
        await using var coordinator = Create(new FakeStore(null), provider);
        var old = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask(); var clear = coordinator.ClearAsync(default).AsTask();
        Assert.False(clear.IsCompleted); oldGate.SetResult(new(Snapshot(Now), null)); await clear;
        var current = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask();
        await old;
        Assert.Same(current, coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask()); Assert.Equal(2, provider.Calls);
        newGate.SetResult(new(Snapshot(Now), null)); await current;
}

[Fact]
public async Task Lifecycle_resume_and_clock_changes_reevaluate_freshness_without_overlapping_refreshes()
{
var clock = new MutableClock(Now);
var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
var events = new FakeLifecycleEvents();
var provider = new FakeProvider(gate.Task);
await using var coordinator = Create(new FakeStore(Snapshot(Now)), provider, clock);
await using var adapter = new QuotaRefreshLifecycleAdapter(events, coordinator);
await coordinator.InitializeAsync(default);

clock.UtcNow = Now.AddMinutes(11);
events.RaiseSuspended();
Assert.Equal(FreshnessState.Stale, coordinator.State.Freshness);
Assert.Equal(0, provider.Calls);
events.RaiseResume();
events.RaiseClockChanged();
await WaitUntilAsync(() => provider.Calls == 1);
Assert.True(coordinator.State.IsLoading);
Assert.Equal(FreshnessState.Stale, coordinator.State.Freshness);

gate.SetResult(new(Snapshot(clock.UtcNow), null));
await WaitUntilAsync(() => coordinator.State.Freshness == FreshnessState.Current && !coordinator.State.IsLoading);
Assert.Equal(1, provider.Calls);

clock.UtcNow = Now.AddMinutes(-1);
events.RaiseClockChanged();
await WaitUntilAsync(() => coordinator.State.Freshness == FreshnessState.Current);
Assert.Equal(1, provider.Calls);
}

[Fact]
public async Task Lifecycle_resume_failure_preserves_timestamp_and_optional_degradation()
{
var clock = new MutableClock(Now.AddMinutes(11));
var cached = Snapshot(Now);
var events = new FakeLifecycleEvents();
var provider = new FakeProvider(new QuotaProviderResult(null, new(QuotaErrorKind.Network, "network")));
await using var coordinator = Create(new FakeStore(cached), provider, clock);
await using var adapter = new QuotaRefreshLifecycleAdapter(events, coordinator);
await coordinator.InitializeAsync(default);

events.RaiseResume();
await WaitUntilAsync(() => !coordinator.State.IsLoading && coordinator.State.Failure is not null);
Assert.Equal(cached.RetrievedAt, coordinator.State.Snapshot!.RetrievedAt);
Assert.Equal(FreshnessState.Stale, coordinator.State.Freshness);

provider.Result = new(Snapshot(clock.UtcNow), null, null, new(QuotaErrorKind.Service, "optional_unavailable"));
clock.UtcNow = clock.UtcNow.AddMinutes(5);
events.RaiseResume();
await WaitUntilAsync(() => coordinator.State.OptionalFailure is not null);
Assert.NotNull(coordinator.State.Snapshot);
Assert.Equal("optional_unavailable", coordinator.State.OptionalFailure!.SafeCode);
}

[Fact]
public async Task Lifecycle_store_failure_degrades_without_replacing_the_cached_snapshot()
{
var clock = new MutableClock(Now.AddMinutes(11));
var cached = Snapshot(Now);
var events = new FakeLifecycleEvents();
await using var coordinator = Create(new FakeStore(cached) { ThrowOnSave = true }, new FakeProvider(new(Snapshot(clock.UtcNow), null)), clock);
await using var adapter = new QuotaRefreshLifecycleAdapter(events, coordinator);
await coordinator.InitializeAsync(default);

events.RaiseResume();
await WaitUntilAsync(() => coordinator.State.Failure is not null);

Assert.Equal(cached.RetrievedAt, coordinator.State.Snapshot!.RetrievedAt);
Assert.Equal(FreshnessState.Stale, coordinator.State.Freshness);
Assert.Equal("quota_refresh_failed", coordinator.State.Failure!.SafeCode);
}

[Fact]
public async Task Lifecycle_boundary_contains_subscriber_failures_and_continues_serially()
{
var clock = new MutableClock(Now);
var events = new FakeLifecycleEvents();
var provider = new FakeProvider(new QuotaProviderResult(Snapshot(Now), null));
await using var coordinator = Create(new FakeStore(Snapshot(Now)), provider, clock);
await using var adapter = new QuotaRefreshLifecycleAdapter(events, coordinator);
await coordinator.InitializeAsync(default);
var subscriberCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
void ThrowingSubscriber(QuotaRefreshState _) { subscriberCalled.TrySetResult(); throw new InvalidOperationException("subscriber"); }
coordinator.StateChanged += ThrowingSubscriber;

clock.UtcNow = Now.AddMinutes(11);
events.RaiseSuspended();
await subscriberCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
coordinator.StateChanged -= ThrowingSubscriber;
events.RaiseResume();
await provider.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

Assert.Equal(1, provider.Calls);
}

[Fact]
public async Task Freshness_reevaluation_cannot_overwrite_a_concurrent_clear()
{
var clock = new BlockingClock(Now);
await using var coordinator = Create(new FakeStore(Snapshot(Now)), new FakeProvider(new QuotaProviderResult(Snapshot(Now), null)), clock);
await coordinator.InitializeAsync(default);
clock.UtcNow = Now.AddMinutes(11);
clock.BlockNextRead();
var unavailablePublished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
coordinator.StateChanged += state => { if (state.Freshness == FreshnessState.Unavailable) unavailablePublished.TrySetResult(); };

var reevaluation = Task.Run(async () => await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default));
await clock.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
var clear = Task.Run(async () => await coordinator.ClearAsync(default));
await Task.WhenAny(unavailablePublished.Task, Task.Delay(100));
clock.ReleaseRead();
await Task.WhenAll(reevaluation, clear);

Assert.Null(coordinator.State.Snapshot);
Assert.Equal(FreshnessState.Unavailable, coordinator.State.Freshness);
}

[Fact]
public async Task Lifecycle_disposal_and_clear_prevent_late_republication()
{
var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
var events = new FakeLifecycleEvents();
var provider = new FakeProvider(gate.Task);
await using var coordinator = Create(new FakeStore(Snapshot(Now.AddMinutes(-11))), provider);
await coordinator.InitializeAsync(default);
var adapter = new QuotaRefreshLifecycleAdapter(events, coordinator);

events.RaiseResume();
await WaitUntilAsync(() => provider.Calls == 1);
 var clear = coordinator.ClearAsync(default).AsTask();
 Assert.False(clear.IsCompleted); gate.SetResult(new(Snapshot(Now), null)); await clear;
 await adapter.DisposeAsync();
 events.RaiseResume();
 await Task.Delay(20);

Assert.Null(coordinator.State.Snapshot);
Assert.Equal(FreshnessState.Unavailable, coordinator.State.Freshness);
Assert.Equal(1, provider.Calls);
}

[Fact]
public async Task Authority_stream_pairs_transitions_and_advances_only_persisted_retrievals()
{
    var clock = new MutableClock(Now); var store = new FakeStore(Snapshot(Now));
    var provider = new FakeProvider(new QuotaProviderResult(null, new(QuotaErrorKind.Network, "network")));
    await using var coordinator = Create(store, provider, clock); var updates = Capture(coordinator);
    await coordinator.InitializeAsync(default); await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
    var real = Snapshot(Now.AddMinutes(1));
    provider.Result = new(real, null, null, new(QuotaErrorKind.Service, "optional"));
    await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
    provider.Result = new(real, null); await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
    store.ThrowOnSave = true; await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
    Assert.Equal(real.RetrievedAt, coordinator.State.Snapshot!.RetrievedAt);
    clock.UtcNow = Now.AddMinutes(20); await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default); await coordinator.ClearAsync(default);
    Assert.Equal(Enumerable.Range(1, 11).Select(value => (long)value), updates.Select(update => update.EventSequence));
    Assert.Equal(new long[] { 0, 0, 0, 0, 1, 1, 2, 2, 2, 2, 2 }, updates.Select(update => update.RetrievalGeneration));
    Assert.Same(updates[^1].State, coordinator.State); Assert.Equal(FreshnessState.Unavailable, coordinator.State.Freshness);

    await using var loadFailure = Create(new FakeStore(null) { ThrowOnLoad = true }, new FakeProvider(new(null, null)));
    var loadUpdates = Capture(loadFailure); await loadFailure.InitializeAsync(default); Assert.Equal(0L, Assert.Single(loadUpdates).RetrievalGeneration);
    await using var providerFailure = Create(new FakeStore(null), new FakeProvider(Task.FromException<QuotaProviderResult>(new InvalidOperationException())));
    var failureUpdates = Capture(providerFailure); await providerFailure.RefreshAsync(RefreshTrigger.Manual, default);
    Assert.Equal(new long[] { 0, 0 }, failureUpdates.Select(update => update.RetrievalGeneration));
}

[Fact]
public async Task Concurrent_reentrant_and_replayed_transitions_remain_ordered()
{
    var clock = new MutableClock(Now); var providerGate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var provider = new FakeProvider(providerGate.Task);
    await using var coordinator = Create(new FakeStore(Snapshot(Now)), provider, clock);
    var updates = Capture(coordinator); var entered = new ManualResetEventSlim(); var release = new ManualResetEventSlim();
    coordinator.AuthorityUpdated += update =>
    {
        if (update.EventSequence == 1) { clock.UtcNow = Now.AddMinutes(20); Assert.True(coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default).IsCompletedSuccessfully); }
        if (update.EventSequence == 3) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(2))); }
    };
    await coordinator.InitializeAsync(default); Assert.Equal(new long[] { 1, 2 }, updates.Select(update => update.EventSequence));
    var refresh = Task.Run(async () => await coordinator.RefreshAsync(RefreshTrigger.Manual, default));
    Assert.True(entered.Wait(TimeSpan.FromSeconds(2))); clock.UtcNow = Now;
    var reevaluations = Task.WhenAll(Task.Run(async () => await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default)), Task.Run(async () => await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default)));
    var replay = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask(); Assert.Same(replay, coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask());
    await reevaluations; release.Set(); await provider.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2)); clock.UtcNow = Now.AddMinutes(20);
    var completionRace = Task.Run(() => providerGate.SetResult(new(Snapshot(clock.UtcNow), null)));
    var freshnessRace = Task.Run(async () => await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default));
    await Task.WhenAll(refresh, replay, completionRace, freshnessRace);
    Assert.Equal(Enumerable.Range(1, updates.Count).Select(value => (long)value), updates.Select(update => update.EventSequence));
    Assert.Same(updates[^1].State, coordinator.State);
}

[Fact]
public async Task Cancellation_and_clear_win_blocked_provider_and_store_completions()
{
    var providerGate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    await using var cancelled = Create(new FakeStore(null), new FakeProvider(providerGate.Task)); var cancelledUpdates = Capture(cancelled);
    var refresh = cancelled.RefreshAsync(RefreshTrigger.Manual, default).AsTask(); var cancel = cancelled.CancelAndWaitAsync(default).AsTask();
    Assert.False(cancel.IsCompleted); providerGate.SetResult(new(Snapshot(Now), null)); await Task.WhenAll(refresh, cancel);
    Assert.All(cancelledUpdates, update => Assert.Equal(0L, update.RetrievalGeneration)); Assert.Null(cancelled.State.Snapshot); Assert.False(cancelled.State.IsLoading);

    var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var saveRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var store = new FakeStore(null) { SaveStarted = saveStarted, SaveRelease = saveRelease };
    await using var cleared = Create(store, new FakeProvider(new(Snapshot(Now), null))); var clearedUpdates = Capture(cleared);
    var saving = cleared.RefreshAsync(RefreshTrigger.Manual, default).AsTask(); await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
    var clear = cleared.ClearAsync(default).AsTask(); Assert.False(clear.IsCompleted); saveRelease.SetResult(); await Task.WhenAll(saving, clear);
    Assert.All(clearedUpdates, update => Assert.Equal(0L, update.RetrievalGeneration)); Assert.Same(clearedUpdates[^1].State, cleared.State);
    Assert.Null(cleared.State.Snapshot); Assert.Equal(FreshnessState.Unavailable, cleared.State.Freshness);
}

[Fact]
public async Task Throwing_subscribers_recover_and_disposal_rejects_late_work()
{
    var clock = new MutableClock(Now); await using var coordinator = Create(new FakeStore(Snapshot(Now)), new FakeProvider(new(Snapshot(Now), null)), clock);
    var updates = Capture(coordinator); await coordinator.InitializeAsync(default);
    void Throwing(QuotaRefreshState _) => throw new InvalidOperationException("subscriber");
    coordinator.StateChanged += Throwing; clock.UtcNow = Now.AddMinutes(20);
    await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default).AsTask());
    coordinator.StateChanged -= Throwing; clock.UtcNow = Now; await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default);
    Assert.Equal(new long[] { 1, 2, 3 }, updates.Select(update => update.EventSequence));

    var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
    var disposed = Create(new FakeStore(null), new FakeProvider(gate.Task)); var disposedUpdates = Capture(disposed);
    var refresh = disposed.RefreshAsync(RefreshTrigger.Manual, default).AsTask(); var disposal = disposed.DisposeAsync().AsTask();
    Assert.False(disposal.IsCompleted); gate.SetResult(new(Snapshot(Now), null)); await Task.WhenAll(refresh, disposal);
    Assert.Single(disposedUpdates); await Assert.ThrowsAsync<ObjectDisposedException>(() => disposed.RefreshAsync(RefreshTrigger.Manual, default).AsTask());
    await Assert.ThrowsAsync<ObjectDisposedException>(() => disposed.ReevaluateAsync(RefreshTrigger.Sleep, default).AsTask());
}

private static List<QuotaAuthorityUpdate> Capture(QuotaRefreshCoordinator coordinator)
{
    var updates = new List<QuotaAuthorityUpdate>(); QuotaAuthorityUpdate? pending = null;
    coordinator.AuthorityUpdated += update => { Assert.Null(pending); pending = update; updates.Add(update); };
    coordinator.StateChanged += state => { Assert.NotNull(pending); Assert.Same(pending!.State, state); pending = null; };
    return updates;
}

private static async Task WaitUntilAsync(Func<bool> condition)
{
for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
Assert.True(condition());
}

private static QuotaRefreshCoordinator Create(FakeStore store, IQuotaProvider provider, IClock? clock = null) => new(store, provider, clock ?? new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
private static QuotaSnapshot Snapshot(DateTimeOffset retrieved) => new(new(42, Now.AddHours(1)), new(20, Now.AddDays(1)), retrieved);
private sealed class MutableClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow { get; set; } = utcNow; }
private sealed class BlockingClock(DateTimeOffset utcNow) : IClock
{
private bool _blockNextRead;
private readonly ManualResetEventSlim _release = new(false);
public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
public DateTimeOffset UtcNow
{
get { if (_blockNextRead) { _blockNextRead = false; ReadStarted.TrySetResult(); _release.Wait(); } return utcNow; }
set => utcNow = value;
}
public void BlockNextRead() => _blockNextRead = true;
public void ReleaseRead() => _release.Set();
}
private sealed class FakeLifecycleEvents : IQuotaRefreshLifecycleEvents
{
public event Action? Suspended;
public event Action? Resumed;
public event Action? ClockChanged;
public void RaiseSuspended() => Suspended?.Invoke();
public void RaiseResume() => Resumed?.Invoke();
public void RaiseClockChanged() => ClockChanged?.Invoke();
}

    private sealed class FakeStore(QuotaSnapshot? snapshot) : IQuotaSnapshotStore
    {
        public int Saves { get; private set; }
        public int Clears { get; private set; }
        public bool ThrowOnLoad { get; init; }
        public bool ThrowOnSave { get; set; }
        public TaskCompletionSource? SaveStarted { get; init; }
        public TaskCompletionSource? SaveRelease { get; init; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken _) => ThrowOnLoad ? throw new InvalidOperationException() : ValueTask.FromResult(snapshot);
        public async ValueTask SaveAsync(QuotaSnapshot value, CancellationToken _) { SaveStarted?.TrySetResult(); if (SaveRelease is not null) await SaveRelease.Task; if (ThrowOnSave) throw new InvalidOperationException(); snapshot = value; Saves++; }
        public ValueTask ClearAsync(CancellationToken _) { snapshot = null; Clears++; return ValueTask.CompletedTask; }
    }

    private sealed class FakeProvider : IQuotaProvider
    {
        private readonly Task<QuotaProviderResult>? _pending;
        private readonly bool _observeCancellation;
        public FakeProvider(QuotaProviderResult result) => Result = result;
        public FakeProvider(Task<QuotaProviderResult> pending, bool observeCancellation = false) { _pending = pending; _observeCancellation = observeCancellation; }
        public QuotaProviderResult Result { get; set; } = new(null, null);
        public int Calls { get; private set; }
        public TaskCompletionSource CallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) { Calls++; CallStarted.TrySetResult(); return _pending is null ? Result : await (_observeCancellation ? _pending.WaitAsync(cancellationToken) : _pending); }
    }

    private sealed class SequencedProvider(params Task<QuotaProviderResult>[] results) : IQuotaProvider
    {
public int Calls { get; private set; }
public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken _) => await results[Calls++];
    }
}
