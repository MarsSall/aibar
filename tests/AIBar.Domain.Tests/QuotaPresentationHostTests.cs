using System.ComponentModel;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaPresentationHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-presentation-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Persisted_consent_makes_refresh_executable_and_revocation_disables_it()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var policy = new PrivateIntegrationPolicy();
        var provider = new CountingProvider();
        await using var coordinator = new QuotaRefreshCoordinator(new CountingStore(), provider, new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(Now)), () => policy.IsEnabled);
        await using var runtime = new BetaRuntime(settings, policy, coordinator, new FixedClock(Now));
        var availabilityChanges = 0;
        var commandChanges = 0;
        presentation.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(QuotaPresentationHost.IsRefreshAvailable)) availabilityChanges++; };
        presentation.RefreshCommand.CanExecuteChanged += (_, _) => commandChanges++;

        await runtime.InitializeAsync(default);
        presentation.RefreshAvailabilityChanged();

        Assert.True(presentation.IsRefreshAvailable);
        Assert.True(presentation.RefreshCommand.CanExecute);
        var callsBeforeManualRefresh = provider.Calls;
        await presentation.RefreshCommand.ExecuteAsync(default);
        Assert.True(provider.Calls > callsBeforeManualRefresh);

        await runtime.RevokeConsentAsync(default);
        presentation.RefreshAvailabilityChanged();

        Assert.False(presentation.IsRefreshAvailable);
        Assert.False(presentation.RefreshCommand.CanExecute);
        var callsBeforeDisabledRefresh = provider.Calls;
        await presentation.RefreshCommand.ExecuteAsync(default);
        Assert.Equal(callsBeforeDisabledRefresh, provider.Calls);
        Assert.Equal(2, availabilityChanges);
        Assert.Equal(4, commandChanges);
    }

    [Fact]
    public async Task Startup_initializes_the_coordinator_once_without_republishing_cached_state()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var cached = Snapshot(Now.AddMinutes(-12));
        var store = new CountingStore(cached);
        var provider = new BlockingProvider();
        var policy = new PrivateIntegrationPolicy();
        await using var coordinator = new QuotaRefreshCoordinator(store, provider, new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new FixedClock(Now)), () => policy.IsEnabled);
        await using var runtime = new BetaRuntime(settings, policy, coordinator, new FixedClock(Now));
        var cachedPublications = 0;
        coordinator.StateChanged += state =>
        {
            if (state.Snapshot == cached && !state.IsLoading) cachedPublications++;
        };

        await App.InitializeCompositionAsync(runtime, presentation, default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(1, cachedPublications);

        provider.Complete();
    }

    private static QuotaSnapshot Snapshot(DateTimeOffset retrievedAt) => new(new(42, Now.AddHours(1)), new(20, Now.AddDays(1)), retrievedAt);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class CountingStore(QuotaSnapshot? snapshot = null) : IQuotaSnapshotStore
    {
        public int LoadCalls { get; private set; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) { LoadCalls++; return ValueTask.FromResult(snapshot); }
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class CountingProvider : IQuotaProvider
    {
        public int Calls { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "test")));
        }
    }

    private sealed class BlockingProvider : IQuotaProvider
    {
        private readonly TaskCompletionSource<QuotaProviderResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return new(_result.Task.WaitAsync(cancellationToken));
        }
        public void Complete() => _result.TrySetResult(new(null, new(QuotaErrorKind.Unavailable, "test")));
    }
}
