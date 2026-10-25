using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

// T2 / S1-S2: synthetic inputs only; never open a real credential file.
public sealed class CodexRemoteUsageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Valid = "{\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\",\"state\":\"current\",\"fiveHour\":{\"percentageUsed\":81,\"resetAt\":\"2030-01-01T17:00:00Z\"},\"weekly\":null}";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-codex-remote-{Guid.NewGuid():N}");
    private string RemotePath => Path.Combine(_root, "codex-remote-usage.json");
    private ConsentSettings Settings => new(Path.Combine(_root, "settings.json"));

    // Exercise the existing shared contract API against the Codex filename, without
    // requiring a new production type in this tests-first revision.
    private ClaudeRemoteUsageProvider Reader(PrivateIntegrationPolicy policy, Files files,
        Func<string, FileAttributes>? attributes = null, IClock? clock = null) =>
        new(policy, RemotePath, files, attributes ?? (_ => FileAttributes.Normal), () => clock?.UtcNow ?? Now);

    [Theory]
    [InlineData(null, null)]
    [InlineData("pro", "Pro")]
    public async Task Shared_reader_maps_Codex_windows_time_and_optional_plan(string? plan, string? label)
    {
        var policy = new PrivateIntegrationPolicy(); policy.Enable();
        var files = new Files(plan is null ? Valid : Valid[..^1] + ",\"plan\":\"" + plan + "\"}");
        var reader = Reader(policy, files);
        var result = await reader.GetQuotaAsync(default);
        Assert.Null(result.Failure);
        Assert.Equal(81m, result.Snapshot!.Primary!.PercentageUsed);
        Assert.Null(result.Snapshot.Weekly);
        Assert.Equal(Now, result.Snapshot.RetrievedAt);
        Assert.Equal(Now.AddHours(5), result.Snapshot.Primary.ResetAt);
        Assert.Equal(label, reader.PlanLabel);
        Assert.Equal(new[] { RemotePath }, files.Paths);
        Assert.Equal(FileShare.ReadWrite | FileShare.Delete, files.Share);
    }

    [Theory]
    [InlineData("authentication-required", QuotaErrorKind.Authentication)]
    [InlineData("unavailable", QuotaErrorKind.Unavailable)]
    public async Task Noncurrent_documents_require_null_windows(string state, QuotaErrorKind kind)
    {
        var policy = new PrivateIntegrationPolicy(); policy.Enable();
        var json = "{\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\",\"state\":\"" + state + "\",\"fiveHour\":null,\"weekly\":null}";
        var result = await Reader(policy, new Files(json)).GetQuotaAsync(default);
        Assert.Null(result.Snapshot); Assert.Equal(kind, result.Failure!.Kind);
        result = await Reader(policy, new Files(Valid.Replace("current", state))).GetQuotaAsync(default);
        Assert.Null(result.Snapshot); Assert.Equal(QuotaErrorKind.Unavailable, result.Failure!.Kind);
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public async Task Fifteen_minute_contract_boundary(int age, bool current)
    {
        var policy = new PrivateIntegrationPolicy(); policy.Enable();
        var json = Valid.Replace("2030-01-01T12:00:00Z", Now.AddMinutes(-age).ToString("yyyy-MM-ddTHH:mm:ss'Z'"));
        var result = await Reader(policy, new Files(json)).GetQuotaAsync(default);
        Assert.Equal(current, result.Snapshot is not null);
        if (!current) Assert.Equal(QuotaErrorKind.Service, result.Failure!.Kind);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("oversize")]
    [InlineData("order")]
    [InlineData("range")]
    public async Task Contract_rejects_extras_duplicates_oversize_wrong_order_and_range(string invalid)
    {
        var policy = new PrivateIntegrationPolicy(); policy.Enable();
        var json = invalid switch
        {
            "extra" => Valid[..^1] + ",\"token\":\"synthetic\"}",
            "duplicate" => Valid[..^1] + ",\"weekly\":null}",
            "oversize" => Valid.PadRight(4097),
            "order" => Valid.Replace("\"schemaVersion\":1,\"generatedAt\":\"2030-01-01T12:00:00Z\"", "\"generatedAt\":\"2030-01-01T12:00:00Z\",\"schemaVersion\":1"),
            _ => Valid.Replace("\"percentageUsed\":81", "\"percentageUsed\":101")
        };
        var result = await Reader(policy, new Files(json)).GetQuotaAsync(default);
        Assert.Null(result.Snapshot); Assert.Equal(QuotaErrorKind.Unavailable, result.Failure!.Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reparse_points_before_or_after_open_are_rejected(bool afterOpen)
    {
        var policy = new PrivateIntegrationPolicy(); policy.Enable();
        var files = new Files(Valid);
        var reader = Reader(policy, files, _ => !afterOpen || files.Paths.Count > 0 ? FileAttributes.ReparsePoint : FileAttributes.Normal);
        Assert.Null((await reader.GetQuotaAsync(default)).Snapshot);
        Assert.Equal(afterOpen ? 1 : 0, files.Paths.Count);
        Assert.False(ClaudeRemoteUsageProvider.IsAvailableForAutomatic(RemotePath, _ => FileAttributes.ReparsePoint));
    }

    [Theory]
    [InlineData(DataSource.OtherMachine)]
    [InlineData(DataSource.Automatic)]
    public async Task Remote_reads_file_without_local_credentials_and_publishes_percentages(DataSource source)
    {
        var files = new Files(Valid); var local = new LocalProvider(); var clock = new Clock();
        await Settings.SaveAsync(true, default); await Settings.SaveDataSourceAsync(source, default);
        var policy = new PrivateIntegrationPolicy();
        var coordinator = Coordinator(local, clock);
        var writer = WindowsAtomicQuotaExportWriter.ForOwnedSyntheticDataDirectory(_root);
        await using var publisher = new QuotaExportPublisher(coordinator, writer, clock);
        await using var runtime = new BetaRuntime(Settings, policy, coordinator, clock, publisher: publisher,
            remoteProviderFactory: p => Reader(p, files, clock: clock), remoteFileAvailable: () => true);
        await runtime.InitializeAsync(default);
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        await publisher.PublicationCompletion;
        Assert.Equal(0, local.Calls); Assert.Equal(0, local.CredentialReads);
        Assert.NotEmpty(files.Paths);
        Assert.Equal(81m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(81m, new QuotaPresentationMapper(clock).Map(runtime.State).Primary.PercentageUsed);
        Assert.True(runtime.CanDeliverAlerts);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "yasb-quota.json")));
        Assert.Equal("current", json.RootElement.GetProperty("state").GetString());
        Assert.Equal(81m, json.RootElement.GetProperty("fiveHour").GetProperty("percentageUsed").GetDecimal());
        await runtime.RevokeConsentAsync(default);
        var reads = files.Paths.Count;
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(reads, files.Paths.Count);
        Assert.False(runtime.CanDeliverAlerts);
    }

    [Theory]
    [InlineData(DataSource.OtherMachine)]
    [InlineData(DataSource.Automatic)]
    public async Task Remote_mode_still_requires_Codex_consent_before_any_read(DataSource source)
    {
        await Settings.SaveDataSourceAsync(source, default);
        var clock = new Clock(); var local = new LocalProvider(); var files = new Files(Valid);
        var coordinator = Coordinator(local, clock);
        await using var runtime = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock,
            remoteProviderFactory: p => Reader(p, files), remoteFileAvailable: () => true);
        await runtime.InitializeAsync(default); await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.False(runtime.IsConsentEnabled); Assert.Empty(files.Paths); Assert.Equal(0, local.Calls);
        await runtime.GrantConsentAsync(default);
        Assert.NotEmpty(files.Paths); Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Remote_file_is_polled_every_minute_even_when_local_preference_is_fifteen_minutes()
    {
        var time = new VirtualTime(); var clock = new Clock(time); var local = new LocalProvider(); var files = new Files(Valid);
        await Settings.SaveAsync(true, default); await Settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
        var coordinator = Coordinator(local, clock);
        await using var runtime = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock, timeProvider: time,
            remoteProviderFactory: p => Reader(p, files, clock: clock), remoteFileAvailable: () => true);
        runtime.SetRefreshMinutes(15);
        await runtime.InitializeAsync(default); await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        var reads = files.Paths.Count;
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(reads, files.Paths.Count);
        time.Advance(TimeSpan.FromSeconds(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (files.Paths.Count == reads) await Task.Delay(10, timeout.Token);
        Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Automatic_rechecks_file_appearance_and_disappearance_and_clears_source_cache()
    {
        var clock = new Clock(); var files = new Files(Valid); var local = new LocalProvider();
        var store = new Store(); var available = false;
        await Settings.SaveAsync(true, default);
        var coordinator = Coordinator(local, clock, store);
        await using var runtime = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock,
            remoteProviderFactory: p => Reader(p, files, clock: clock), remoteFileAvailable: () => available);
        await runtime.InitializeAsync(default); await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(12m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        available = true;
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(81m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.True(store.Clears > 0);
        var calls = local.Calls;
        files.Content = "{}";
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(calls, local.Calls); // Invalid existing remote never falls back to credentials.
        available = false;
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(12m, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.True(local.Calls > calls); Assert.True(store.Clears >= 2);
    }

    [Fact]
    public async Task Remote_deadline_marks_stale_without_a_poll_or_another_file_read()
    {
        var time = new VirtualTime(); var clock = new Clock(time);
        var files = new Files(Valid.Replace("2030-01-01T12:00:00Z", "2030-01-01T11:45:30Z"));
        await Settings.SaveAsync(true, default); await Settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
        var local = new LocalProvider(); var coordinator = Coordinator(local, clock);
        await using var runtime = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock, timeProvider: time,
            remoteProviderFactory: p => Reader(p, files, clock: clock), remoteFileAvailable: () => true);
        await runtime.InitializeAsync(default); await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        var reads = files.Paths.Count;
        time.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        Assert.Equal(reads, files.Paths.Count); Assert.Equal(0, local.Calls);
    }

    [Fact]
    public async Task Remote_current_threshold_is_admitted_to_alert_delivery()
    {
        var clock = new Clock(); var files = new Files(Valid.Replace("\"percentageUsed\":81", "\"percentageUsed\":10"));
        await Settings.SaveAsync(true, default); await Settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
        var coordinator = Coordinator(new LocalProvider(), clock);
        await using var runtime = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock,
            remoteProviderFactory: p => Reader(p, files), remoteFileAvailable: () => true);
        await using var alerts = new QuotaAlertObserver(_root, clock);
        var delivered = 0;
        alerts.AlertReady += _ => Interlocked.Increment(ref delivered);
        coordinator.AuthorityUpdated += update => alerts.Observe("Codex", update, () => runtime.CanDeliverAlerts);
        await runtime.InitializeAsync(default); await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        await alerts.Completion;
        files.Content = Valid.Replace("\"percentageUsed\":81", "\"percentageUsed\":100");
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        await alerts.Completion;
        Assert.True(runtime.CanDeliverAlerts); Assert.True(delivered > 0);
    }

    private static QuotaRefreshCoordinator Coordinator(IQuotaProvider local, IClock clock, Store? store = null) =>
        new(store ?? new Store(), local, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class Files(string content) : ICredentialFileReader
    {
        public string Content = content;
        public List<string> Paths { get; } = [];
        public FileShare Share;
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        { Paths.Add(path); Share = share; return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Content))); }
    }
    private sealed class LocalProvider : IQuotaProvider
    {
        public int Calls, CredentialReads;
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Calls); Interlocked.Increment(ref CredentialReads);
            return ValueTask.FromResult(new QuotaProviderResult(new(new(12, Now.AddHours(5)), null, Now), null));
        }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public int Clears;
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult<QuotaSnapshot?>(null);
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken token) { Clears++; return ValueTask.CompletedTask; }
    }
    private sealed class Clock(TimeProvider? time = null) : IClock { public DateTimeOffset UtcNow => time?.GetUtcNow() ?? Now; }
    private sealed class VirtualTime : TimeProvider
    {
        private DateTimeOffset _now = Now;
        private readonly List<Timer> _timers = [];
        public override DateTimeOffset GetUtcNow() => _now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); _timers.Add(timer); timer.Change(dueTime, period); return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            var target = _now + elapsed;
            while (_timers.Where(t => t.Due <= target).OrderBy(t => t.Due).FirstOrDefault() is { } timer)
            { _now = timer.Due!.Value; timer.Fire(); }
            _now = target;
        }
        private sealed class Timer(VirtualTime owner, TimerCallback callback, object? state) : ITimer
        {
            private bool _disposed; private TimeSpan _period;
            public DateTimeOffset? Due { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            { if (_disposed) return false; _period = period; Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime; return true; }
            public void Fire() { Due = _period == Timeout.InfiniteTimeSpan ? null : owner._now + _period; callback(state); }
            public void Dispose() { _disposed = true; Due = null; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
