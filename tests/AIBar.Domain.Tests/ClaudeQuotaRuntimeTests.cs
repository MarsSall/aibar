using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class ClaudeQuotaRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-claude-runtime-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private ConsentSettings Settings => new(Path.Combine(_root, "settings.json"));

    [Fact]
    public async Task Plan_metadata_is_local_only_and_disappears_on_source_switch_or_revoke()
    {
        var provider = new Provider { PlanLabel = "Max" };
        await using var runtime = new ClaudeQuotaRuntime(Settings, new MemoryStore(), _ => provider, new Clock(),
            remoteProviderFactory: _ => new Provider { PlanLabel = "Pro" }, remoteFileAvailable: () => false);
        await runtime.InitializeAsync(default);
        Assert.Null(runtime.PlanLabel);
        await runtime.GrantConsentAsync(default);
        Assert.Equal("Max", runtime.PlanLabel);
        await runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        Assert.Null(runtime.PlanLabel); // Even a remote provider cannot borrow the local credential's plan.
        await runtime.SetSourceAsync(ClaudeQuotaSource.ThisPc, default);
        Assert.Equal("Max", runtime.PlanLabel);
        await runtime.RevokeConsentAsync(default);
        Assert.Null(runtime.PlanLabel);
    }

    // a: schema 1, optional decision, and the existing two-property Codex contract.
    [Fact]
    public async Task Missing_and_legacy_settings_leave_Claude_off_without_changing_Codex()
    {
        Assert.False(await Settings.LoadClaudeAsync(default));
        await Settings.SaveAsync(true, default);
        var before = await File.ReadAllTextAsync(Path.Combine(_root, "settings.json"));
        Assert.False(await Settings.LoadClaudeAsync(default));
        Assert.True(await Settings.LoadAsync(default));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(_root, "settings.json")));
        using var json = JsonDocument.Parse(before);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schema\":2,\"privateClaudeConsent\":true}")]
    [InlineData("{\"schema\":1,\"privateClaudeConsent\":\"true\"}")]
    [InlineData("{bad")]
    public async Task Invalid_or_unknown_schema_cannot_grant_Claude_consent(string content)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), content);
        Assert.False(await Settings.LoadClaudeAsync(default));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Each_save_preserves_the_other_providers_explicit_decision(bool codex, bool claude)
    {
        await Settings.SaveClaudeAsync(claude, default);
        await Settings.SaveAsync(codex, default);
        Assert.Equal(claude, await Settings.LoadClaudeAsync(default));
        await Settings.SaveClaudeAsync(!claude, default);
        Assert.Equal(codex, await Settings.LoadAsync(default));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "settings.json")));
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(codex, json.RootElement.GetProperty("privateCodexConsent").GetBoolean());
        Assert.Equal(!claude, json.RootElement.GetProperty("privateClaudeConsent").GetBoolean());
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
    }

    // b/c: disclosure is independent; cancel must not create provider resources.
    [Fact]
    public async Task Claude_disclosure_names_the_local_login_unofficial_endpoint_and_no_refresh_promise()
    {
        await using var runtime = Runtime(new MemoryStore(), new Provider());
        var copy = runtime.Disclosure;
        foreach (var phrase in new[] { "Claude Code", "on this PC", "read-only", "Anthropic", "unofficial", "subscription", "never refresh", "change", "any time" })
            Assert.Contains(phrase, copy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Codex", copy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Declining_Claude_prompt_keeps_both_consents_off_and_does_not_construct_provider()
    {
        var factories = 0;
        await using var runtime = new ClaudeQuotaRuntime(Settings, new MemoryStore(), _ => { factories++; return new Provider(); }, new Clock());
        await runtime.InitializeAsync(default);
        var tray = new ClaudeTray(); var prompt = new Prompt(false);
        using var controller = new ClaudeQuotaTrayController(tray, runtime, prompt);
        await controller.EnableAsync(default);
        Assert.Equal(1, prompt.Calls);
        Assert.False(runtime.IsConsentEnabled);
        Assert.False(await Settings.LoadClaudeAsync(default));
        Assert.False(await Settings.LoadAsync(default));
        Assert.Equal(0, factories);
        Assert.False(tray.Enabled);
    }

    [Fact]
    public async Task Claude_tray_commands_and_events_enable_and_disable_only_Claude()
    {
        await Settings.SaveAsync(true, default);
        await using var runtime = Runtime(new MemoryStore(), new Provider());
        await runtime.InitializeAsync(default);
        var tray = new ClaudeTray(); var prompt = new Prompt(true);
        using var controller = new ClaudeQuotaTrayController(tray, runtime, prompt);
        tray.Enable();
        await Until(() => runtime.IsConsentEnabled && tray.Enabled);
        Assert.Equal(1, prompt.Calls);
        Assert.True(await Settings.LoadAsync(default));
        tray.Disable();
        await Until(() => !runtime.IsConsentEnabled && !tray.Enabled);
        Assert.False(await Settings.LoadClaudeAsync(default));
        Assert.True(await Settings.LoadAsync(default));
        await controller.EnableAsync(default);
        await controller.DisableAsync(default);
        Assert.False(runtime.IsConsentEnabled);
    }

    [Fact]
    public void Windows_tray_exposes_Claude_as_an_optional_capability_not_a_new_Codex_requirement()
    {
        Assert.True(typeof(IClaudeQuotaTray).IsAssignableFrom(typeof(WindowsTrayRuntime)));
        Assert.DoesNotContain(typeof(ITrayRuntime).GetMembers(), member => member.Name.Contains("Claude", StringComparison.Ordinal));
    }

    // d/h: real stores, no provider column/migration and no secret-bearing fields.
    [Fact]
    public async Task Provider_databases_are_siblings_and_refreshes_cannot_overwrite_each_other()
    {
        Directory.CreateDirectory(_root);
        var codexPath = Path.Combine(_root, "quota.db");
        var claudePath = ClaudeQuotaRuntime.DatabasePath(_root);
        Assert.Equal(Path.Combine(_root, "claude-quota.db"), claudePath);
        await using var codex = new SqliteQuotaSnapshotStore(codexPath);
        await using var claude = new SqliteQuotaSnapshotStore(claudePath);
        await codex.SaveAsync(Snapshot(11), default);
        var codexBytes = await File.ReadAllBytesAsync(codexPath);
        await using var runtime = Runtime(claude, new Provider(Snapshot(73)));
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(73m, (await claude.LoadAsync(default))!.Primary!.PercentageUsed);
        Assert.Equal(codexBytes, await File.ReadAllBytesAsync(codexPath));
        Assert.Equal(11m, (await codex.LoadAsync(default))!.Primary!.PercentageUsed);
        await codex.SaveAsync(Snapshot(19), default);
        Assert.Equal(73m, (await claude.LoadAsync(default))!.Primary!.PercentageUsed);
        foreach (var path in new[] { codexPath, claudePath })
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(3L, (long)(await command.ExecuteScalarAsync())!);
            command.CommandText = "SELECT id, schema_version FROM quota_snapshot;";
            await using (var rows = await command.ExecuteReaderAsync())
            {
                Assert.True(await rows.ReadAsync()); Assert.Equal(1L, rows.GetInt64(0)); Assert.Equal(3L, rows.GetInt64(1));
                Assert.False(await rows.ReadAsync());
            }
            command.CommandText = "PRAGMA table_info(quota_snapshot);";
            await using var columns = await command.ExecuteReaderAsync();
            var names = new List<string>();
            while (await columns.ReadAsync()) names.Add(columns.GetString(1));
            Assert.Equal(new[] { "id", "schema_version", "primary_percentage", "primary_reset_at", "weekly_percentage", "weekly_reset_at", "reset_credits", "retrieved_at", "schema_adapter_version" }, names);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Desktop_composes_Claude_beside_Codex_with_lazy_provider_and_owned_store(bool enabled)
    {
        await Settings.SaveClaudeAsync(enabled, default);
        var factories = 0; var provider = new Provider(); var time = new PollTime();
        var composition = App.CreateComposition(new App.CompositionSeams(
            DataDirectory: _root, CodexHome: Path.Combine(_root, "synthetic-codex"),
            UserProfile: Path.Combine(_root, "synthetic-profile"),
            ClaudeProviderFactory: _ => { factories++; return provider; }, ClaudeTimeProvider: time));
        await using var resource = composition.Resource;
        Assert.Equal(0, factories);
        await composition.Initialize();
        Assert.Equal(enabled, composition.ClaudeQuota.IsConsentEnabled);
        Assert.Equal(enabled ? 1 : 0, factories);
        if (enabled) await Until(() => provider.Calls == 1);
        Assert.False(await Settings.LoadAsync(default));
        Assert.Equal(enabled, File.Exists(Path.Combine(_root, "claude-quota.db")));
        Assert.Equal(enabled ? 1 : 0, time.ActiveTimers);
    }

    // e: injected virtual timer avoids five-minute wall-clock tests or production-only hooks.
    [Fact]
    public async Task Disabled_Claude_creates_no_provider_or_timer_and_ignores_every_lifecycle_trigger()
    {
        var factories = 0; var time = new PollTime(); var events = new Lifecycle(); var store = new MemoryStore();
        await using var runtime = new ClaudeQuotaRuntime(Settings, store, _ => { factories++; return new Provider(); }, new Clock(), events, time);
        await runtime.InitializeAsync(default);
        foreach (var trigger in Enum.GetValues<RefreshTrigger>()) await runtime.RefreshAsync(trigger, default);
        events.Sleep(); events.Resume(); events.ClockChangedNow(); time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, factories); Assert.Equal(0, time.ActiveTimers); Assert.Equal(0, store.Loads); Assert.Equal(0, store.Saves);
        Assert.Null(runtime.State.Snapshot);
    }

    [Fact]
    public async Task Claude_off_does_not_stop_or_enable_Codex_runtime()
    {
        await Settings.SaveAsync(true, default);
        var provider = new Provider(); var clock = new Clock(); var store = new MemoryStore();
        await using var coordinator = new QuotaRefreshCoordinator(store, provider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        await using var codex = new BetaRuntime(Settings, new PrivateIntegrationPolicy(), coordinator, clock);
        await using var claude = Runtime(new MemoryStore(), new Provider());
        await claude.InitializeAsync(default); await codex.InitializeAsync(default);
        await Until(() => provider.Calls == 1);
        Assert.True(codex.IsConsentEnabled); Assert.False(claude.IsConsentEnabled);
        await codex.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task Cache_first_five_minute_poll_throttle_sleep_and_resume_match_Codex()
    {
        await Settings.SaveClaudeAsync(true, default);
        var clock = new Clock(); var time = new PollTime(); var events = new Lifecycle();
        var cached = Snapshot(21); var store = new MemoryStore(cached); var provider = new Provider(Snapshot(73));
        await using var runtime = Runtime(store, provider, clock, events, time);
        var published = new List<QuotaSnapshot?>();
        runtime.StateChanged += () => published.Add(runtime.State.Snapshot);
        await runtime.InitializeAsync(default);
        await Until(() => provider.Calls == 1 && !runtime.State.IsLoading);
        // S13 removes the freshness gate, including the initial poll. Cache still publishes first.
        Assert.Equal(cached, published.First(snapshot => snapshot is not null));
        var retrieved = runtime.State.Snapshot;
        Assert.Equal(73m, retrieved!.Primary!.PercentageUsed);
        Assert.Equal(1, time.ActiveTimers); Assert.Equal(TimeSpan.FromMinutes(5), time.Period);
        clock.UtcNow = Now.AddMinutes(5).AddTicks(-1);
        await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(1, provider.Calls);
        clock.UtcNow = Now.AddMinutes(5); time.Advance(TimeSpan.FromMinutes(5));
        await Until(() => provider.Calls == 2 && !runtime.State.IsLoading);
        clock.UtcNow = Now.AddMinutes(12); events.Sleep();
        Assert.Equal(2, provider.Calls); Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        provider.Result = new(null, new(QuotaErrorKind.Service, "quota_service"));
        events.Resume(); await Until(() => runtime.State.Failure is not null);
        Assert.Equal(3, provider.Calls); Assert.Equal(retrieved, runtime.State.Snapshot);
        events.ClockChangedNow(); await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(3, provider.Calls); Assert.Equal(TimeSpan.FromMinutes(12), runtime.CachedAge);
        clock.UtcNow = Now.AddMinutes(17); time.Advance(TimeSpan.FromMinutes(5));
        await Until(() => provider.Calls == 4);
    }

    [Theory]
    [InlineData(ClaudeQuotaSource.ThisPc, 5)]
    [InlineData(ClaudeQuotaSource.OtherMachine, 1)]
    public async Task Resolved_source_controls_timer_and_coordinator_cadence(ClaudeQuotaSource source, int minutes)
    {
        await Settings.SaveClaudeSourceAsync(source, default);
        var clock = new Clock(); var time = new PollTime(); var provider = new Provider();
        await using var runtime = new ClaudeQuotaRuntime(Settings, new MemoryStore(), _ => provider, clock,
            timeProvider: time, remoteProviderFactory: _ => provider, remoteFileAvailable: () => false);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(TimeSpan.FromMinutes(minutes), time.Period);
        clock.UtcNow = Now.AddMinutes(minutes).AddTicks(-1);
        await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(1, provider.Calls);
        clock.UtcNow = Now.AddMinutes(minutes); time.Advance(TimeSpan.FromMinutes(minutes));
        await Until(() => provider.Calls == 2 && !runtime.State.IsLoading);
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);

        var other = source == ClaudeQuotaSource.ThisPc ? ClaudeQuotaSource.OtherMachine : ClaudeQuotaSource.ThisPc;
        var nextMinutes = other == ClaudeQuotaSource.OtherMachine ? 1 : 5;
        await runtime.SetSourceAsync(other, default);
        Assert.Equal(3, provider.Calls);
        Assert.Equal(TimeSpan.FromMinutes(nextMinutes), time.Period);
        Assert.Equal(other, runtime.ResolvedSource);
        clock.UtcNow = clock.UtcNow.AddMinutes(nextMinutes).AddTicks(-1);
        await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(3, provider.Calls);
        clock.UtcNow = clock.UtcNow.AddTicks(1); time.Advance(TimeSpan.FromMinutes(nextMinutes));
        await Until(() => provider.Calls == 4 && !runtime.State.IsLoading);
    }

    [Fact]
    public async Task Automatic_source_handoff_replaces_the_poll_cadence()
    {
        var remoteAvailable = false; var clock = new Clock(); var time = new PollTime(); var provider = new Provider();
        await using var runtime = new ClaudeQuotaRuntime(Settings, new MemoryStore(), _ => provider, clock,
            timeProvider: time, remoteProviderFactory: _ => provider, remoteFileAvailable: () => remoteAvailable);
        await runtime.InitializeAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(TimeSpan.FromMinutes(5), time.Period);
        remoteAvailable = true;
        await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(ClaudeQuotaSource.Automatic, runtime.Source);
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.ResolvedSource);
        Assert.Equal(TimeSpan.FromMinutes(1), time.Period);
        Assert.Equal(2, provider.Calls);
        clock.UtcNow = Now.AddMinutes(1); time.Advance(TimeSpan.FromMinutes(1));
        await Until(() => provider.Calls == 3 && !runtime.State.IsLoading);
        remoteAvailable = false;
        await runtime.RefreshAsync(RefreshTrigger.Poll, default);
        Assert.Equal(ClaudeQuotaSource.ThisPc, runtime.ResolvedSource);
        Assert.Equal(TimeSpan.FromMinutes(5), time.Period);
        Assert.Equal(4, provider.Calls);
    }

    // f: deliberately non-cooperative provider exposes save-before-generation-check bugs.
    [Fact]
    public async Task Revoke_cancels_inflight_rejects_late_persistence_and_stops_all_refresh_paths()
    {
        await Settings.SaveAsync(true, default); await Settings.SaveClaudeAsync(true, default);
        var provider = new BlockingProvider(); var store = new MemoryStore(); var events = new Lifecycle(); var time = new PollTime();
        await using var runtime = Runtime(store, provider, events: events, time: time);
        await runtime.InitializeAsync(default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var revoke = runtime.RevokeConsentAsync(default).AsTask();
        try
        {
            await Until(() => provider.Token.IsCancellationRequested);
            Assert.False(runtime.IsConsentEnabled);
        }
        finally { provider.Complete(); }
        await revoke.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, store.Saves); Assert.Null(store.Snapshot); Assert.Null(runtime.State.Snapshot);
        Assert.Equal(FreshnessState.Unavailable, runtime.State.Freshness);
        Assert.False(await Settings.LoadClaudeAsync(default)); Assert.True(await Settings.LoadAsync(default));
        Assert.Equal(0, time.ActiveTimers);
        events.Resume(); events.ClockChangedNow(); time.Advance(TimeSpan.FromHours(1));
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(1, provider.Calls); Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task Revoke_waits_for_an_already_entered_save_and_removes_its_late_commit()
    {
        var store = new BlockingSaveStore();
        await using var runtime = Runtime(store, new Provider());
        await runtime.InitializeAsync(default);
        var grant = runtime.GrantConsentAsync(default).AsTask();
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var revoke = runtime.RevokeConsentAsync(default).AsTask();
        try { await Until(() => !runtime.IsConsentEnabled); }
        finally { store.Release.TrySetResult(); }
        await Task.WhenAll(grant, revoke).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Null(store.Snapshot); Assert.Null(runtime.State.Snapshot);
        Assert.False(await Settings.LoadClaudeAsync(default));
    }

    [Fact]
    public async Task Revoke_clears_cached_Claude_state_and_reenable_does_not_enable_Codex()
    {
        var store = new MemoryStore(); var provider = new Provider();
        await using var runtime = Runtime(store, provider);
        await runtime.InitializeAsync(default); await runtime.GrantConsentAsync(default);
        Assert.NotNull(runtime.State.Snapshot);
        await runtime.RevokeConsentAsync(default);
        Assert.Null(store.Snapshot); Assert.Null(runtime.State.Snapshot);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(2, provider.Calls); Assert.False(await Settings.LoadAsync(default));
        Assert.True(await Settings.LoadClaudeAsync(default));
    }

    // g: production default allowlist, not an override that could hide a missing path.
    [Fact]
    public async Task Clear_default_allowlist_deletes_Claude_database_sidecars_and_consent_only()
    {
        Directory.CreateDirectory(_root);
        foreach (var name in new[] { "claude-quota.db", "claude-quota.db-wal", "claude-quota.db-shm", "settings.json", "yasb-claude-quota.json", "unrelated.txt" })
            await File.WriteAllTextAsync(Path.Combine(_root, name), "synthetic");
        var work = new ClearProbe(() => Assert.True(File.Exists(Path.Combine(_root, "claude-quota.db"))));
        var clear = new ClearAiBarDataService(_root, [work], _ => ValueTask.CompletedTask);
        await clear.ClearAsync(default);
        Assert.True(work.Cancelled); Assert.True(work.Resumed);
        foreach (var name in new[] { "claude-quota.db", "claude-quota.db-wal", "claude-quota.db-shm", "settings.json", "yasb-claude-quota.json" })
            Assert.False(File.Exists(Path.Combine(_root, name)), name);
        Assert.True(File.Exists(Path.Combine(_root, "unrelated.txt")));
        Assert.False(await Settings.LoadClaudeAsync(default));
    }

    [Fact]
    public async Task Clear_runtime_work_resets_Claude_consent_and_does_not_restart_polling()
    {
        var provider = new Provider(); var time = new PollTime(); var events = new Lifecycle();
        await using var runtime = Runtime(new MemoryStore(), provider, events: events, time: time);
        await runtime.InitializeAsync(default); await runtime.GrantConsentAsync(default);
        var clear = new ClearAiBarDataService(_root, [runtime], _ => ValueTask.CompletedTask);
        await clear.ClearAsync(default);
        Assert.False(runtime.IsConsentEnabled); Assert.Null(runtime.State.Snapshot);
        Assert.False(await Settings.LoadClaudeAsync(default)); Assert.Equal(0, time.ActiveTimers);
        events.Resume(); time.Advance(TimeSpan.FromHours(1));
        await runtime.RefreshAsync(RefreshTrigger.Manual, default);
        Assert.Equal(1, provider.Calls);
    }

    // h: synthetic sentinels only; never read real credentials or a real user profile.
    [Fact]
    public async Task Unsafe_provider_exception_does_not_reach_state_trace_settings_or_snapshot_store()
    {
        const string secret = "synthetic-private-token-and-path";
        var output = new StringBuilder(); using var writer = new StringWriter(output);
        using var listener = new TextWriterTraceListener(writer); Trace.Listeners.Add(listener);
        try
        {
            var store = new MemoryStore();
            await using var runtime = Runtime(store, new ThrowingProvider(secret));
            await runtime.InitializeAsync(default); await runtime.GrantConsentAsync(default);
            await Until(() => runtime.State.Failure is not null);
            Assert.Equal("quota_refresh_failed", runtime.State.Failure!.SafeCode);
            Assert.Equal(0, store.Saves); Assert.Null(store.Snapshot);
            listener.Flush();
            Assert.DoesNotContain(secret, output.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(runtime.State), StringComparison.Ordinal);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "settings.json")));
            Assert.All(json.RootElement.EnumerateObject(), property => Assert.Contains(property.Name, new[] { "schema", "privateCodexConsent", "privateClaudeConsent" }));
        }
        finally { Trace.Listeners.Remove(listener); }
    }

    private ClaudeQuotaRuntime Runtime(IQuotaSnapshotStore store, IQuotaProvider provider, Clock? clock = null,
        Lifecycle? events = null, PollTime? time = null) => new(Settings, store, _ => provider, clock ?? new Clock(), events, time);
    private static QuotaSnapshot Snapshot(decimal used = 42) => new(new(used, Now.AddHours(5)), new(20, Now.AddDays(7)), Now);
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = Now; }
    private sealed class MemoryStore(QuotaSnapshot? initial = null) : IQuotaSnapshotStore
    {
        public QuotaSnapshot? Snapshot { get; private set; } = initial;
        public int Saves { get; private set; }
        public int Loads { get; private set; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) { Loads++; return ValueTask.FromResult(Snapshot); }
        // Intentionally ignores cancellation: the Claude boundary must suppress late save calls.
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken) { Saves++; Snapshot = snapshot; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { Snapshot = null; return ValueTask.CompletedTask; }
    }
    private sealed class BlockingSaveStore : IQuotaSnapshotStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public QuotaSnapshot? Snapshot { get; private set; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Snapshot);
        public async ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken)
        { Started.TrySetResult(); await Release.Task; Snapshot = snapshot; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { Snapshot = null; return ValueTask.CompletedTask; }
    }
    private sealed class Provider(QuotaSnapshot? snapshot = null) : IQuotaProvider, IQuotaPlanMetadata
    {
        public string? PlanLabel { get; init; }
        public int Calls { get; private set; }
        public QuotaProviderResult Result { get; set; } = new(snapshot ?? Snapshot(), null);
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) { Calls++; return ValueTask.FromResult(Result); }
    }
    private sealed class ThrowingProvider(string message) : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) => throw new IOException(message);
    }
    private sealed class BlockingProvider : IQuotaProvider
    {
        private readonly TaskCompletionSource<QuotaProviderResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public int Calls { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        { Calls++; Token = cancellationToken; Started.TrySetResult(); return new(_result.Task); }
        public void Complete() => _result.TrySetResult(new(Snapshot(), null));
    }
    private sealed class Lifecycle : IQuotaRefreshLifecycleEvents
    {
        public event Action? Suspended; public event Action? Resumed; public event Action? ClockChanged;
        public void Sleep() => Suspended?.Invoke(); public void Resume() => Resumed?.Invoke(); public void ClockChangedNow() => ClockChanged?.Invoke();
    }
    private sealed class Prompt(bool accepted) : IPrivateIntegrationConsentPrompt
    {
        public int Calls { get; private set; }
        public bool Confirm() { Calls++; return accepted; }
    }
    private sealed class ClaudeTray : IClaudeQuotaTray
    {
        public event Action? ClaudeQuotaEnableRequested; public event Action? ClaudeQuotaDisableRequested;
        public bool Enabled { get; private set; }
        public void SetClaudeQuotaEnabled(bool enabled) => Enabled = enabled;
        public void Enable() => ClaudeQuotaEnableRequested?.Invoke(); public void Disable() => ClaudeQuotaDisableRequested?.Invoke();
    }
    private sealed class ClearProbe(Action beforeCancel) : IAiBarClearWork
    {
        public bool Cancelled { get; private set; } public bool Resumed { get; private set; }
        public ValueTask CancelAndWaitAsync(CancellationToken cancellationToken) { beforeCancel(); Cancelled = true; return ValueTask.CompletedTask; }
        public void ResumeAfterClear() => Resumed = true;
    }
    private sealed class PollTime : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private TimeSpan _elapsed;
        public int ActiveTimers => _timers.Count(timer => !timer.Disposed && timer.Due != Timeout.InfiniteTimeSpan);
        public TimeSpan? Period => _timers.LastOrDefault(timer => !timer.Disposed && timer.Period != Timeout.InfiniteTimeSpan)?.Period;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state); timer.Change(dueTime, period); _timers.Add(timer); return timer;
        }
        public void Advance(TimeSpan elapsed)
        {
            _elapsed += elapsed;
            foreach (var timer in _timers.ToArray()) timer.Fire(_elapsed);
        }
        private sealed class Timer(PollTime owner, TimerCallback callback, object? state) : ITimer
        {
            public bool Disposed { get; private set; }
            public TimeSpan Due { get; private set; }
            public TimeSpan Period { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime == Timeout.InfiniteTimeSpan ? dueTime : owner._elapsed + dueTime; Period = period; return !Disposed; }
            public void Fire(TimeSpan now)
            {
                if (Disposed || Due == Timeout.InfiniteTimeSpan || now < Due) return;
                Due = Period == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : now + Period;
                callback(state);
            }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
