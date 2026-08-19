using System.Security.Cryptography;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ClearAiBarDataTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-clear-{Guid.NewGuid():N}");
    private readonly string _codex = Path.Combine(Path.GetTempPath(), $"codex-clear-{Guid.NewGuid():N}");

    [Fact]
    public async Task Clear_cancels_and_awaits_work_deletes_only_allowlisted_data_and_preserves_codex_bytes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "cache"));
        Directory.CreateDirectory(Path.Combine(_root, "logs", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_root, "cache", "quota.json"), "cache");
        await File.WriteAllTextAsync(Path.Combine(_root, "logs", "nested", "log.txt"), "log");
        await File.WriteAllTextAsync(Path.Combine(_root, "aibar.db"), "database");
        await File.WriteAllTextAsync(Path.Combine(_root, "analytics.db"), "analytics");
        await File.WriteAllTextAsync(Path.Combine(_root, "analytics.db-wal"), "analytics-wal");
        await File.WriteAllTextAsync(Path.Combine(_root, "analytics.db-shm"), "analytics-shm");
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), "settings");
        await File.WriteAllTextAsync(Path.Combine(_root, "yasb-quota.json"), "snapshot");
        await File.WriteAllTextAsync(Path.Combine(_root, "unrelated.txt"), "preserve");
        Directory.CreateDirectory(_codex);
        var source = Path.Combine(_codex, "session.jsonl");
        await File.WriteAllTextAsync(source, "synthetic codex source");
        var before = Hash(source); var work = new BlockingWork(); var recreated = 0;
        var service = new ClearAiBarDataService(_root, [work], async token =>
        {
            recreated++; Directory.CreateDirectory(Path.Combine(_root, "cache"));
            await File.WriteAllTextAsync(Path.Combine(_root, "aibar.db"), "empty", token);
        });

        await service.ClearAsync(default);

        Assert.Equal(["cancel", "await", "resume"], work.Events);
        Assert.Equal(1, recreated); Assert.Equal(before, Hash(source));
        Assert.True(Directory.Exists(Path.Combine(_root, "cache")));
        Assert.Equal("empty", await File.ReadAllTextAsync(Path.Combine(_root, "aibar.db")));
        Assert.False(File.Exists(Path.Combine(_root, "analytics.db")));
        Assert.False(File.Exists(Path.Combine(_root, "analytics.db-wal")));
        Assert.False(File.Exists(Path.Combine(_root, "analytics.db-shm")));
        Assert.False(Directory.Exists(Path.Combine(_root, "logs")));
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
        Assert.False(File.Exists(Path.Combine(_root, "yasb-quota.json")));
        Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(_root, "unrelated.txt")));
    }

    [Fact]
    public async Task Clear_is_idempotent_and_recreates_after_each_successful_retry()
    {
        var recreations = 0;
        var service = new ClearAiBarDataService(_root, [], token =>
        {
            recreations++; Directory.CreateDirectory(_root); return ValueTask.CompletedTask;
        });

        await service.ClearAsync(default);
        await service.ClearAsync(default);

        Assert.Equal(2, recreations);
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task Clear_failure_does_not_report_success_and_a_retry_recreates_the_empty_state()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), "old");
        var attempts = 0;
        var service = new ClearAiBarDataService(_root, [], _ =>
        {
            if (++attempts == 1) throw new IOException("synthetic recreation failure");
            return ValueTask.CompletedTask;
        });

        await Assert.ThrowsAsync<IOException>(() => service.ClearAsync(default).AsTask());
        await service.ClearAsync(default);

        Assert.Equal(2, attempts);
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    [Fact]
    public async Task Clear_locked_allowlisted_file_fails_closed_then_retries_after_the_windows_lock_is_released()
    {
        Directory.CreateDirectory(_root);
        var database = Path.Combine(_root, "aibar.db"); var outside = Path.Combine(_codex, "source.jsonl");
        Directory.CreateDirectory(_codex); await File.WriteAllTextAsync(database, "owned"); await File.WriteAllTextAsync(outside, "codex");
        await using (new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask);
            await Assert.ThrowsAsync<IOException>(() => service.ClearAsync(default).AsTask());
            Assert.True(File.Exists(database)); Assert.Equal("codex", await File.ReadAllTextAsync(outside));
        }
        await new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask).ClearAsync(default);
        Assert.False(File.Exists(database)); Assert.Equal("codex", await File.ReadAllTextAsync(outside));
    }

    [Fact]
    public async Task Clear_recreates_a_valid_empty_database_that_accepts_the_next_synthetic_rescan()
    {
        Directory.CreateDirectory(_root); Directory.CreateDirectory(_codex);
        var database = Path.Combine(_root, "aibar.db"); var source = Path.Combine(_codex, "session.jsonl");
        await File.WriteAllTextAsync(database, "old"); await File.WriteAllTextAsync(source, Line(2) + "\n"); var before = Hash(source);
        var service = new ClearAiBarDataService(_root, [], async token =>
        {
            await using var empty = new SqliteDailyModelUsageStore(database); Assert.Empty(await empty.LoadAsync(token));
        });

        await service.ClearAsync(default);
        await using var store = new SqliteDailyModelUsageStore(database);
        var result = await new AnalyticsScanCoordinator(new SessionJsonlScanner(new SessionCheckpointStore(), "v1"), store, new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, "v1"))).ScanAsync(source, default);

        Assert.Equal(new TokenTotals(2, 0, 0), Assert.Single(result.Usage).Tokens); Assert.Equal(before, Hash(source));
    }

    [Fact]
    public async Task Snapshot_staging_rolls_back_when_a_later_owned_file_is_locked()
    {
        Directory.CreateDirectory(_root); var snapshot = Path.Combine(_root, "yasb-quota.json"); var database = Path.Combine(_root, "aibar.db");
        await File.WriteAllTextAsync(snapshot, "snapshot"); await File.WriteAllTextAsync(database, "database");
        await using var locked = new FileStream(database, FileMode.Open, FileAccess.Read, FileShare.None);
        var service = new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask, ["yasb-quota.json", "aibar.db"]);

        await Assert.ThrowsAsync<IOException>(() => service.ClearAsync(default).AsTask());
        await locked.DisposeAsync();

        Assert.Equal("snapshot", await File.ReadAllTextAsync(snapshot)); Assert.Equal("database", await File.ReadAllTextAsync(database));
    }

    [Fact]
    public async Task Synthetic_production_composition_publishes_and_clear_recreates_only_a_disabled_snapshot()
    {
        var disposal = new List<string>(); var dataDirectory = Path.Combine(_root, "AIBar");
        var writer = new WindowsAtomicQuotaExportWriter(_root, new());
        var composition = App.CreateComposition(new(dataDirectory, Path.Combine(_root, "codex"), LifecycleObservation: disposal.Add, UserProfile: Path.Combine(_root, "home"), ExportWriter: writer));
        try
        {
            await composition.Initialize(); AssertDisabledSnapshot();
            await composition.Settings!.ClearAiBarDataAsync(default); AssertDisabledSnapshot();
        }
        finally { await composition.Resource.DisposeAsync(); }
        AssertDisabledSnapshot(); Assert.True(disposal.IndexOf("quota_export_publisher_disposed") < disposal.IndexOf("quota_store_disposed"));

        void AssertDisabledSnapshot()
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dataDirectory, "yasb-quota.json"))); var root = json.RootElement;
            Assert.Equal("disabled", root.GetProperty("state").GetString()); Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceRetrievedAt").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("fiveHour").ValueKind); Assert.Equal(JsonValueKind.Null, root.GetProperty("weekly").ValueKind);
        }
    }

    [Fact]
    public async Task Clear_cancels_inflight_and_queued_publication_before_recreating_a_disabled_snapshot()
    {
        var path = Path.Combine(_root, "yasb-quota.json"); Directory.CreateDirectory(_root); var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        await using var coordinator = new QuotaRefreshCoordinator(new EmptyStore(), new EmptyProvider(), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var publisher = new QuotaExportPublisher(coordinator, new BlockingWriter(path), clock); await publisher.EnableAsync(default);
        var state = new QuotaRefreshState(new(new QuotaWindow(42, DateTimeOffset.UnixEpoch), null, DateTimeOffset.UnixEpoch), FreshnessState.Current, false, null, null);
        publisher.Accept(new(state, 1, 1)); await BlockingWriter.Started.Task;
        publisher.Accept(new(state, 2, 1)); await new ClearAiBarDataService(_root, [publisher], publisher.DisableAsync, ["yasb-quota.json"]).ClearAsync(default);

        using var json = JsonDocument.Parse(await File.ReadAllBytesAsync(path)); Assert.Equal("disabled", json.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Routine_writer_failure_isolated_from_authoritative_state()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch); await using var coordinator = new QuotaRefreshCoordinator(new EmptyStore(), new EmptyProvider(), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var publisher = new QuotaExportPublisher(coordinator, new FailingWriter(), clock); await publisher.EnableAsync(default); var initial = coordinator.State;
        publisher.Accept(new(new(new(new QuotaWindow(42, DateTimeOffset.UnixEpoch), null, DateTimeOffset.UnixEpoch), FreshnessState.Current, false, null, null), 1, 1)); await publisher.PublicationCompletion;
        Assert.Same(initial, coordinator.State);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("cache/../settings.json")]
    public void Clear_rejects_ambiguous_or_traversing_owned_paths(string ownedPath)
    {
        Assert.Throws<ArgumentException>(() => new ClearAiBarDataService(_root, [], _ => ValueTask.CompletedTask, [ownedPath]));
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Line(long input) => $"{{\"timestamp\":\"2030-01-01T00:00:00Z\",\"model\":\"gpt-5\",\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":0,\"output_tokens\":0}}}}";
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); if (Directory.Exists(_codex)) Directory.Delete(_codex, true); }

    private sealed class BlockingWork : IAiBarClearWork
    {
        public List<string> Events { get; } = [];
        public ValueTask CancelAndWaitAsync(CancellationToken cancellationToken) { Events.Add("cancel"); Events.Add("await"); return ValueTask.CompletedTask; }
        public void ResumeAfterClear() => Events.Add("resume");
    }
    private sealed class EmptyStore : IQuotaSnapshotStore { public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken _) => ValueTask.FromResult<QuotaSnapshot?>(null); public ValueTask SaveAsync(QuotaSnapshot _, CancellationToken __) => ValueTask.CompletedTask; public ValueTask ClearAsync(CancellationToken _) => ValueTask.CompletedTask; }
    private sealed class EmptyProvider : IQuotaProvider { public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken _) => ValueTask.FromResult(new QuotaProviderResult(null, null)); }
    private sealed class BlockingWriter(string path) : IQuotaExportWriter
    {
        public static TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset now, CancellationToken token)
        {
            if (document.State != QuotaExportState.Disabled) { await File.WriteAllTextAsync(path, "valued", token); Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            await File.WriteAllTextAsync(path, QuotaExportWire.Serialize(document, now), token);
        }
    }
    private sealed class FailingWriter : IQuotaExportWriter { public ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset _, CancellationToken __) => document.State == QuotaExportState.Disabled ? ValueTask.CompletedTask : ValueTask.FromException(new IOException("synthetic writer failure")); }
}
