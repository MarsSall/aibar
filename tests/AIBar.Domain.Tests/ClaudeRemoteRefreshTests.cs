using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

// Fresh other-machine data at startup: AIBar asks Linux for a push instead of waiting for its timer.
public sealed class ClaudeRemoteRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-claude-refresh-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private ConsentSettings Settings => new(Path.Combine(_root, "settings.json"));

    public ClaudeRemoteRefreshTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Startup_panel_open_and_manual_refresh_request_fresh_data_but_polls_do_not()
    {
        var channel = new Channel(); var provider = new Provider(); var time = new ManualTime();
        await using var runtime = await StartAsync(ClaudeQuotaSource.OtherMachine, channel, provider, time);
        await Until(() => channel.Requests.Count == 1 && provider.Calls >= 1);
        Assert.Equal(Now, channel.Requests[0]);

        // Past the one-minute minimum, so the poll really retrieves.
        var calls = provider.Calls;
        time.Advance(TimeSpan.FromMinutes(2));
        time.FirePolls();
        await Until(() => provider.Calls > calls);
        Assert.Single(channel.Requests);

        await runtime.RefreshAsync(RefreshTrigger.PopoverOpened, default);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(3, channel.Requests.Count);
    }

    [Fact]
    public async Task This_pc_source_never_requests_or_watches_remote_usage()
    {
        var channel = new Channel(); var provider = new Provider();
        await using var runtime = await StartAsync(ClaudeQuotaSource.ThisPc, channel, provider, new ManualTime());
        await Until(() => provider.Calls >= 1);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Empty(channel.Requests);
        Assert.Equal(0, channel.Watches);
    }

    [Fact]
    public async Task Pushed_usage_refreshes_immediately_without_requesting_again_until_revoked()
    {
        var channel = new Channel(); var provider = new Provider();
        await using var runtime = await StartAsync(ClaudeQuotaSource.OtherMachine, channel, provider, new ManualTime());
        await Until(() => provider.Calls >= 1 && !runtime.State.IsLoading);
        Assert.Equal(1, channel.Watches);

        var calls = provider.Calls;
        channel.Changed!();
        await Until(() => provider.Calls > calls);
        // A pushed file must never ask for another push, or Linux and Windows would loop.
        Assert.Single(channel.Requests);

        await runtime.RevokeConsentAsync(default);
        Assert.Equal(1, channel.WatchesDisposed);
        calls = provider.Calls;
        channel.Changed!();
        await Task.Delay(50);
        Assert.Equal(calls, provider.Calls);
    }

    [Fact]
    public async Task A_push_replacing_the_remote_file_never_switches_Automatic_to_this_pc()
    {
        // Observed 2026-10-22: File.Replace leaves the target briefly absent, and the change
        // watcher refreshes exactly then; Automatic flipped to This PC and showed "authentication failed".
        var replacing = 0; var local = new Provider(); var remote = new Provider();
        // While "replacing", exactly the next existence check sees the file missing.
        await using var runtime = await StartAutomaticAsync(local, remote, () => Interlocked.Exchange(ref replacing, 0) == 0);
        await Until(() => remote.Calls >= 1 && !runtime.State.IsLoading);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);

        Volatile.Write(ref replacing, 1);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);

        Assert.Equal(0, Volatile.Read(ref replacing));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
        Assert.Equal(0, local.Calls);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, await Settings.LoadClaudeCacheSourceAsync(default));
    }

    [Fact]
    public async Task A_removed_remote_file_still_switches_Automatic_to_this_pc_after_confirmation()
    {
        var present = true; var local = new Provider(); var remote = new Provider();
        await using var runtime = await StartAutomaticAsync(local, remote, () => Volatile.Read(ref present));
        await Until(() => remote.Calls >= 1 && !runtime.State.IsLoading);
        Volatile.Write(ref present, false);

        await runtime.RefreshAsync(RefreshTrigger.Manual, default);

        Assert.Equal(ClaudeQuotaSource.ThisPc, runtime.ResolvedSource);
        await Until(() => local.Calls >= 1);
    }

    [Fact]
    public async Task Request_file_is_exact_and_leaves_no_staging_file()
    {
        var channel = new ClaudeRemoteRefreshChannel(_root);
        await channel.RequestAsync(Now.AddTicks(1234567), default);
        Assert.Equal("{\"schemaVersion\":1,\"requestedAt\":\"2030-01-01T12:00:00.1234567Z\"}",
            await File.ReadAllTextAsync(Path.Combine(_root, "claude-refresh-request.json")));
        Assert.Equal(new[] { "claude-refresh-request.json" }, Directory.GetFiles(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Usage_watcher_reports_a_replaced_usage_file()
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watch = new ClaudeRemoteRefreshChannel(_root).WatchUsage(() => changed.TrySetResult());
        var staging = Path.Combine(_root, "claude-remote-usage.json.tmp");
        await File.WriteAllTextAsync(staging, "{}");
        File.Move(staging, Path.Combine(_root, "claude-remote-usage.json"));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Request_failure_is_silent()
    {
        var blocked = Path.Combine(_root, "missing", "deeper");
        await new ClaudeRemoteRefreshChannel(blocked).RequestAsync(Now, default);
        Assert.False(Directory.Exists(blocked));
    }

    private async Task<ClaudeQuotaRuntime> StartAsync(ClaudeQuotaSource source, Channel channel, Provider provider, ManualTime time)
    {
        await Settings.SaveClaudeAsync(true, default);
        await Settings.SaveClaudeSourceAsync(source, default);
        var runtime = new ClaudeQuotaRuntime(Settings, new Store(), _ => provider, new Clock(time), timeProvider: time,
            remoteProviderFactory: _ => provider, remoteFileAvailable: () => false, remoteRefresh: channel);
        await runtime.InitializeAsync(default);
        return runtime;
    }

    private async Task<ClaudeQuotaRuntime> StartAutomaticAsync(Provider local, Provider remote, Func<bool> available)
    {
        await Settings.SaveClaudeAsync(true, default);
        var runtime = new ClaudeQuotaRuntime(Settings, new Store(), _ => local, new Clock(new ManualTime()), timeProvider: TimeProvider.System,
            remoteProviderFactory: _ => remote, remoteFileAvailable: available, remoteRefresh: new Channel(),
            remoteAbsenceConfirmation: TimeSpan.FromMilliseconds(50));
        await runtime.InitializeAsync(default);
        return runtime;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Channel : IClaudeRemoteRefreshChannel
    {
        private readonly object _gate = new();
        private readonly List<DateTimeOffset> _requests = [];
        public IReadOnlyList<DateTimeOffset> Requests { get { lock (_gate) return _requests.ToArray(); } }
        public int Watches { get; private set; }
        public int WatchesDisposed { get; private set; }
        public Action? Changed { get; private set; }
        public ValueTask RequestAsync(DateTimeOffset now, CancellationToken cancellationToken)
        { lock (_gate) _requests.Add(now); return ValueTask.CompletedTask; }
        public IDisposable WatchUsage(Action changed)
        {
            Watches++; Changed = changed;
            return new Watch(this);
        }
        private sealed class Watch(Channel owner) : IDisposable { public void Dispose() => owner.WatchesDisposed++; }
    }
    private sealed class Provider : IQuotaProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(new QuotaProviderResult(new(new(10, Now.AddHours(5)), null, Now), null));
        }
    }
    private sealed class Clock(ManualTime time) : IClock { public DateTimeOffset UtcNow => time.GetUtcNow(); }
    private sealed class Store : IQuotaSnapshotStore
    {
        private QuotaSnapshot? _snapshot;
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_snapshot);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken) { _snapshot = snapshot; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { _snapshot = null; return ValueTask.CompletedTask; }
    }
    // Poll timers fire only when the test says so.
    private sealed class ManualTime : TimeProvider
    {
        private readonly List<(TimerCallback Callback, object? State, Timer Timer)> _timers = [];
        private DateTimeOffset _now = Now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(); lock (_timers) _timers.Add((callback, state, timer)); return timer;
        }
        public void FirePolls()
        {
            (TimerCallback Callback, object? State, Timer Timer)[] timers;
            lock (_timers) timers = _timers.Where(entry => !entry.Timer.Disposed).ToArray();
            foreach (var entry in timers) entry.Callback(entry.State);
        }
        private sealed class Timer : ITimer
        {
            public bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) => !Disposed;
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }
    }
}
