using AIBar.Application;
using AIBar.Domain;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class RefreshIntervalRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-refresh-runtime-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private ConsentSettings Settings => new(Path.Combine(_root, "settings.json"));

    [Fact]
    public async Task Codex_live_change_updates_timer_and_throttle_without_resetting_attempt_or_panel_rules()
    {
        var clock = new Clock(); var time = new PollTime(); var provider = new Provider(clock);
        var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        await using var runtime = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock, timeProvider: time);
        runtime.SetRefreshMinutes(2);
        await runtime.InitializeAsync(default); await runtime.GrantConsentAsync(default);
        Assert.Equal(TimeSpan.FromMinutes(2), time.Period);
        clock.UtcNow = Now.AddMinutes(1); time.Tick();
        await runtime.RefreshAsync(RefreshTrigger.Poll, default); Assert.Equal(1, provider.Calls);
        clock.UtcNow = Now.AddMinutes(2); time.Tick(); await Until(() => provider.Calls == 2 && !runtime.State.IsLoading);
        runtime.SetRefreshMinutes(30); Assert.Equal(TimeSpan.FromMinutes(30), time.Period);
        clock.UtcNow = Now.AddMinutes(4); time.Tick();
        await runtime.RefreshAsync(RefreshTrigger.Poll, default); Assert.Equal(2, provider.Calls);
        await runtime.RefreshAsync(RefreshTrigger.PopoverOpened, default); Assert.Equal(3, provider.Calls);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default); Assert.Equal(4, provider.Calls);
        runtime.SetRefreshMinutes(1); Assert.Equal(TimeSpan.FromMinutes(1), time.Period);
        clock.UtcNow = Now.AddMinutes(5); time.Tick(); await Until(() => provider.Calls == 5 && !runtime.State.IsLoading);
    }

    [Theory]
    [InlineData(ClaudeQuotaSource.ThisPc)]
    [InlineData(ClaudeQuotaSource.OtherMachine)]
    [InlineData(ClaudeQuotaSource.Automatic)]
    public async Task Claude_changes_live_but_remote_stays_one_minute_and_source_handoffs_keep_local_choice(ClaudeQuotaSource source)
    {
        await Settings.SaveClaudeSourceAsync(source, default);
        var clock = new Clock(); var time = new PollTime(); var provider = new Provider(clock);
        await using var runtime = new ClaudeQuotaRuntime(Settings, new Store(), _ => provider, clock,
            timeProvider: time, remoteProviderFactory: _ => provider, remoteFileAvailable: () => false);
        await runtime.SetRefreshMinutesAsync(2);
        await runtime.InitializeAsync(default); await runtime.GrantConsentAsync(default);
        var minutes = source == ClaudeQuotaSource.OtherMachine ? 1 : 2;
        Assert.Equal(TimeSpan.FromMinutes(minutes), time.Period);
        clock.UtcNow = Now.AddMinutes(minutes).AddTicks(-1);
        await runtime.RefreshAsync(RefreshTrigger.Poll, default); Assert.Equal(1, provider.Calls);
        clock.UtcNow = Now.AddMinutes(minutes); time.Tick(); await Until(() => provider.Calls == 2 && !runtime.State.IsLoading);
        await runtime.SetRefreshMinutesAsync(15);
        Assert.Equal(TimeSpan.FromMinutes(source == ClaudeQuotaSource.OtherMachine ? 1 : 15), time.Period);
        clock.UtcNow = clock.UtcNow.AddMinutes(1); time.Tick();
        await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(source == ClaudeQuotaSource.OtherMachine ? 3 : 2, provider.Calls);
        await runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        Assert.Equal(TimeSpan.FromMinutes(1), time.Period);
        await runtime.SetRefreshMinutesAsync(30);
        Assert.Equal(TimeSpan.FromMinutes(1), time.Period);
        await runtime.SetSourceAsync(ClaudeQuotaSource.ThisPc, default);
        Assert.Equal(TimeSpan.FromMinutes(30), time.Period);
        var calls = provider.Calls;
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        await runtime.RefreshAsync(RefreshTrigger.Poll, default); Assert.Equal(calls, provider.Calls);
        await runtime.RefreshAsync(RefreshTrigger.PopoverOpened, default); Assert.Equal(calls + 1, provider.Calls);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default); Assert.Equal(calls + 2, provider.Calls);
        await runtime.RevokeConsentAsync(default);
        await runtime.SetRefreshMinutesAsync(1);
        Assert.Null(time.Period); // Saving while disabled must not start polling or a provider.
        Assert.Equal(calls + 2, provider.Calls);
        await runtime.GrantConsentAsync(default); Assert.Equal(TimeSpan.FromMinutes(1), time.Period);
    }

    [Fact]
    public async Task App_appearance_binding_applies_loaded_and_saved_intervals_to_both_runtimes()
    {
        var clock = new Clock(); var codexTime = new PollTime(); var claudeTime = new PollTime();
        var provider = new Provider(clock);
        var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        await using var codex = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock, timeProvider: codexTime);
        await using var claude = new ClaudeQuotaRuntime(Settings, new Store(), _ => provider, clock,
            timeProvider: claudeTime, remoteFileAvailable: () => false);
        var appearance = new AppearanceSettings(Path.Combine(_root, "appearance.json"));
        await appearance.SaveAsync(new(RefreshMinutes: 15), default);
        await App.ApplyRefreshIntervalAsync(await appearance.LoadAsync(default), codex, claude);
        await claude.InitializeAsync(default); await claude.GrantConsentAsync(default);
        Assert.Equal(TimeSpan.FromMinutes(15), codexTime.Period);
        Assert.Equal(TimeSpan.FromMinutes(15), claudeTime.Period);
        Task applied = Task.CompletedTask;
        appearance.Changed += options => { applied = App.ApplyRefreshIntervalAsync(options, codex, claude); };
        await appearance.SaveAsync(new(RefreshMinutes: 2), default);
        await applied.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.FromMinutes(2), codexTime.Period);
        Assert.Equal(TimeSpan.FromMinutes(2), claudeTime.Period);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => claude.SetRefreshMinutesAsync(3).AsTask());
        Assert.Throws<ArgumentOutOfRangeException>(() => codex.SetRefreshMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(2), codexTime.Period);
        Assert.Equal(TimeSpan.FromMinutes(2), claudeTime.Period);
    }

    [Fact]
    public async Task Coordinator_interval_change_preserves_last_attempt_and_validates_positive_duration()
    {
        var clock = new Clock(); var provider = new Provider(clock);
        await using var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        await coordinator.RefreshAsync(RefreshTrigger.Poll, default);
        coordinator.SetMinimumPollInterval(TimeSpan.FromMinutes(15));
        clock.UtcNow = Now.AddMinutes(5);
        await coordinator.RefreshAsync(RefreshTrigger.Poll, default); Assert.Equal(1, provider.Calls);
        coordinator.SetMinimumPollInterval(TimeSpan.FromMinutes(2));
        await coordinator.RefreshAsync(RefreshTrigger.Poll, default); Assert.Equal(2, provider.Calls);
        Assert.Throws<ArgumentOutOfRangeException>(() => coordinator.SetMinimumPollInterval(TimeSpan.Zero));
    }

    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!done() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.True(done(), "Refresh did not finish within 60 seconds.");
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = Now; }
    private sealed class Provider(Clock clock) : IQuotaProvider
    {
        public int Calls;
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(new QuotaProviderResult(new(new(20, Now.AddHours(5)), new(40, Now.AddDays(7)), clock.UtcNow), null));
        }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult<QuotaSnapshot?>(null);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
    // Explicit ticks exercise real timer callbacks without wall-clock minute waits.
    private sealed class PollTime : TimeProvider
    {
        private readonly List<TestTimer> _timers = [];
        public TimeSpan? Period => _timers.LastOrDefault(timer => !timer.Disposed && timer.Period != Timeout.InfiniteTimeSpan)?.Period;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(callback, state); timer.Change(dueTime, period); _timers.Add(timer); return timer;
        }
        public void Tick()
        {
            foreach (var timer in _timers.ToArray()) if (!timer.Disposed && timer.Period != Timeout.InfiniteTimeSpan) timer.Fire();
        }
        private sealed class TestTimer(TimerCallback callback, object? state) : ITimer
        {
            public bool Disposed { get; private set; }
            public TimeSpan Period { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) { Period = period; return !Disposed; }
            public void Fire() => callback(state);
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
