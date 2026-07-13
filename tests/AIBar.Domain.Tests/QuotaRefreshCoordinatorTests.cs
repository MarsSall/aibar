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
        Assert.Equal(42, coordinator.State.Snapshot!.Primary.PercentageUsed);
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
        Assert.Equal(42, coordinator.State.Snapshot!.Primary.PercentageUsed);
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

        await coordinator.ClearAsync(default);
        gate.SetResult(new(Snapshot(Now.AddHours(1)), null));
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
        var old = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask(); await coordinator.ClearAsync(default);
        var current = coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask();
        oldGate.SetResult(new(Snapshot(Now), null)); await old;
        Assert.Same(current, coordinator.RefreshAsync(RefreshTrigger.Manual, default).AsTask()); Assert.Equal(2, provider.Calls);
        newGate.SetResult(new(Snapshot(Now), null)); await current;
}

private static QuotaRefreshCoordinator Create(FakeStore store, IQuotaProvider provider, IClock? clock = null) => new(store, provider, clock ?? new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
private static QuotaSnapshot Snapshot(DateTimeOffset retrieved) => new(new(42, Now.AddHours(1)), new(20, Now.AddDays(1)), retrieved);
private sealed class MutableClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow { get; set; } = utcNow; }

    private sealed class FakeStore(QuotaSnapshot? snapshot) : IQuotaSnapshotStore
    {
        public int Saves { get; private set; }
        public int Clears { get; private set; }
        public bool ThrowOnSave { get; init; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken _) => ValueTask.FromResult(snapshot);
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken _) { if (ThrowOnSave) throw new InvalidOperationException(); snapshot = value; Saves++; return ValueTask.CompletedTask; }
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
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) { Calls++; return _pending is null ? Result : await (_observeCancellation ? _pending.WaitAsync(cancellationToken) : _pending); }
    }

    private sealed class SequencedProvider(params Task<QuotaProviderResult>[] results) : IQuotaProvider
    {
public int Calls { get; private set; }
public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken _) => await results[Calls++];
    }
}
