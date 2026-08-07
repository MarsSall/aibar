using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class BetaPresentationTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Maps_one_immutable_snapshot_with_explicit_freshness_failure_and_disclosure_bindings()
    {
        var clock = new MutableClock(Now);
        var mapper = new QuotaPresentationMapper(clock);
        var snapshot = Snapshot(Now.AddMinutes(-11));

        var fresh = mapper.Map(new(Snapshot(Now), FreshnessState.Current, false, null, null));
        var loading = mapper.Map(new(snapshot, FreshnessState.Current, true, null, null));
        var missing = mapper.Map(new(null, FreshnessState.Unavailable, false, new(QuotaErrorKind.Unavailable, "quota_credential_missing"), null));
        var offlineCached = mapper.Map(new(snapshot, FreshnessState.Stale, false, new(QuotaErrorKind.Network, "quota_network"), null));
        var safeError = mapper.Map(new(null, FreshnessState.Unavailable, false, new(QuotaErrorKind.Service, "private response: secret"), null));

        Assert.IsType<BetaPresentationState>(fresh);
        Assert.True(fresh.IsFresh); Assert.False(fresh.IsLoading); Assert.Null(fresh.CachedAge);
        Assert.True(loading.IsLoading); Assert.False(loading.IsFresh); Assert.Equal("Loading", loading.FreshnessLabel);
        Assert.True(missing.IsMissingCredential); Assert.True(missing.IsUnavailable); Assert.Equal("Credential unavailable", missing.WarningLabel);
        Assert.True(offlineCached.IsOffline); Assert.True(offlineCached.IsDegraded); Assert.Equal(TimeSpan.FromMinutes(11), offlineCached.CachedAge);
        Assert.Equal("Cached 00:11:00", offlineCached.CachedAgeLabel); Assert.Equal("Network unavailable", offlineCached.WarningLabel);
        Assert.True(safeError.IsSafeError); Assert.Equal("Service unavailable", safeError.WarningLabel); Assert.DoesNotContain("secret", safeError.WarningLabel, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(fresh.Disclosure); Assert.NotEmpty(fresh.QuotaDisclosure); Assert.NotEmpty(fresh.PrivateEndpointDisclosure);
    }

    [Fact]
    public async Task Shared_host_state_recalculates_countdowns_on_clock_events_and_stops_after_disposal()
    {
        var clock = new MutableClock(Now);
        var lifecycle = new LifecycleEvents();
        var snapshot = Snapshot(Now);
        await using var coordinator = new QuotaRefreshCoordinator(new SnapshotStore(snapshot), new NeverProvider(), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromDays(1));
        var host = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock), lifecycleEvents: lifecycle);
        await coordinator.InitializeAsync(default);

        var trayState = host.State;
        Assert.Equal("01:00:00", trayState.Primary.ResetCountdown);
        clock.Advance(TimeSpan.FromMinutes(15)); lifecycle.RaiseClockChanged();
        Assert.Equal("00:45:00", host.State.Primary.ResetCountdown);
        Assert.NotSame(trayState, host.State);

        await host.DisposeAsync();
        clock.Advance(TimeSpan.FromMinutes(15)); lifecycle.RaiseClockChanged();
        Assert.Equal("00:45:00", host.State.Primary.ResetCountdown);
    }

    [Fact]
    public async Task Popup_open_uses_the_same_coalesced_refresh_path_as_manual_refresh()
    {
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var calls = new List<RefreshTrigger>();
        var tray = new Tray();
        await using var runtime = new TrayHostRuntime(instance, tray, new Popover(), new Taskbar(), _ => Task.CompletedTask, new Resource(), () => { },
            reevaluate: (trigger, _) => { calls.Add(trigger); return ValueTask.CompletedTask; });

        runtime.Start(); tray.Toggle();

        Assert.Equal(new[] { RefreshTrigger.PopoverOpened }, calls);
        instance.Dispose();
    }

    [Fact]
    public async Task Tray_and_popup_receive_the_same_latest_immutable_state()
    {
        var clock = new MutableClock(Now);
        await using var coordinator = new QuotaRefreshCoordinator(new SnapshotStore(Snapshot(Now)), new NeverProvider(), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromDays(1));
        await using var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock));
        using var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new Tray();
        await using var runtime = new TrayHostRuntime(instance, tray, new Popover(), new Taskbar(), _ => Task.CompletedTask, new Resource(), () => { }, presentation: presentation);

        await coordinator.InitializeAsync(default);

        Assert.Same(presentation.State, tray.State);
        instance.Dispose();
    }

    [Fact]
    public void Popup_display_remaps_countdowns_below_freshness_threshold_and_publishes_one_state_to_both_observers()
    {
        var clock = new MutableClock(Now);
        var provider = new CountingProvider();
        var coordinator = new QuotaRefreshCoordinator(new SnapshotStore(Snapshot(Now)), provider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromDays(1));
        var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock));
        coordinator.InitializeAsync(default).AsTask().GetAwaiter().GetResult();
        clock.Advance(TimeSpan.FromMinutes(5));

        var instance = new SingleInstanceHost($"AIBar.Tests.{Guid.NewGuid():N}");
        var tray = new Tray();
        BetaPresentationState? popupState = null;
        presentation.PropertyChanged += (_, _) => popupState = presentation.State;
        var runtime = new TrayHostRuntime(instance, tray, new Popover(), new Taskbar(), _ => Task.CompletedTask, new Resource(), () => { }, reevaluate: coordinator.ReevaluateAsync, presentation: presentation);
        try
        {
            tray.Toggle();

            Assert.Equal("00:55:00", presentation.State.Primary.ResetCountdown);
            Assert.Same(presentation.State, popupState);
            Assert.Same(presentation.State, tray.State);
            Assert.Equal(0, provider.Calls);
        }
        finally
        {
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            instance.Dispose();
            presentation.DisposeAsync().AsTask().GetAwaiter().GetResult();
            coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static QuotaSnapshot Snapshot(DateTimeOffset retrievedAt) => new(new(42, Now.AddHours(1)), new(20, Now.AddDays(7)), retrievedAt);
    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = now;
        public void Advance(TimeSpan value) => UtcNow += value;
    }
    private sealed class LifecycleEvents : IQuotaRefreshLifecycleEvents
    {
        public event Action? Suspended; public event Action? Resumed; public event Action? ClockChanged;
        public void RaiseClockChanged() => ClockChanged?.Invoke();
    }
    private sealed class SnapshotStore(QuotaSnapshot snapshot) : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<QuotaSnapshot?>(snapshot);
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
    private sealed class NeverProvider : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "unused")));
    }
    private sealed class CountingProvider : IQuotaProvider
    {
        public int Calls { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "unused")));
        }
    }
    private sealed class Tray : ITrayRuntime
    {
        public event Action? Toggled; public event Action? ExitRequested; public event Action? RefreshRequested; public event Action? StartupToggleRequested; public event Action? ClearAiBarDataRequested; public event Action? PrivateIntegrationDisableRequested;
        public void SetRefreshAvailable(bool available) { } public void SetSettingsAvailable(bool available) { } public void SetStartupEnabled(bool enabled) { } public void Show() { } public void Hide() { }
        public BetaPresentationState? State { get; private set; }
        public void SetPresentation(BetaPresentationState state) => State = state;
        public void Toggle() => Toggled?.Invoke(); public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Popover : IPopoverRuntime
    {
        public event Action? Deactivated; public bool IsVisible { get; private set; } public bool IsOwnedDialogActive => false;
        public void Show() => IsVisible = true; public void Hide() => IsVisible = false; public void Activate() { }
    }
    private sealed class Taskbar : ITaskbarRecreationEvents { public event Action? Recreated; }
    private sealed class Resource : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
