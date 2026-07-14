using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class ScanRunProvenanceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"aibar-scan-run-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Records_complete_counts_and_non_negative_handoff_without_duplicate_commit()
    {
        var store = new InMemoryScanRunStore();
        var recorder = new ScanRunRecorder(store, () => DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        var discovery = new SessionDiscoveryResult(
            [new(["first.jsonl", "second.jsonl"])],
            new(2, 0, []));
        var scans = new[]
        {
            new SessionParseResult([new(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), "model-a", -1, 2, -3)], [], false),
            new SessionParseResult([], ["session_incomplete_tail", "session_malformed_record"], false)
        };

        var first = await recorder.RecordAsync(discovery, scans, default);
        var second = await recorder.RecordAsync(discovery, scans, default);

        Assert.Equal((2, 2, 0, 1, ScanCoverageState.Partial), (first.FilesDiscovered, first.FilesRead, first.FilesSkipped, first.FilesDeferred, first.Coverage));
        Assert.Equal(["session_incomplete_tail", "session_malformed_record"], first.WarningCodes);
        var record = Assert.Single(first.Handoff);
        Assert.Equal((0L, 2L, 0L), (record.InputTokens, record.CachedInputTokens, record.OutputTokens));
        Assert.Equal(first, second);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(first, store.LastCommit);
    }

    [Fact]
    public async Task Cancellation_before_commit_preserves_prior_scan_run_and_handoff()
    {
        var store = new InMemoryScanRunStore();
        var baseline = new ScanRunRecorder(store, () => DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        var discovery = new SessionDiscoveryResult([new(["first.jsonl"])], new(1, 0, []));
        var scan = new SessionParseResult([new(DateTimeOffset.Parse("2030-01-01T00:00:00Z"), "model-a", 1, 0, 0)], [], false);
        var committed = await baseline.RecordAsync(discovery, [scan], default);
        using var cancellation = new CancellationTokenSource();
        var cancelled = new ScanRunRecorder(store, () => DateTimeOffset.Parse("2030-01-02T00:00:00Z"), cancellation.Cancel);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.RecordAsync(discovery, [scan], cancellation.Token).AsTask());

        Assert.Equal(1, store.CommitCount);
        Assert.NotEqual(committed, store.LastCommit);
        Assert.True(store.LastCommit!.WasCancelled);
        Assert.Empty(store.LastCommit.Handoff);
    }

    [Fact]
    public async Task Safe_warnings_and_discovery_skips_produce_partial_coverage_without_paths()
    {
        var store = new InMemoryScanRunStore();
        var recorder = new ScanRunRecorder(store, () => DateTimeOffset.UnixEpoch);
        var discovery = new SessionDiscoveryResult([new(["synthetic.jsonl"])], new(1, 2, ["session_file_changed"]));

        var result = await recorder.RecordAsync(discovery, [new([], ["session_malformed_record"], false)], default);

        Assert.Equal((1, 1, 2, 0, ScanCoverageState.Partial), (result.FilesDiscovered, result.FilesRead, result.FilesSkipped, result.FilesDeferred, result.Coverage));
        Assert.Equal(["session_file_changed", "session_malformed_record"], result.WarningCodes);
        Assert.All(result.WarningCodes, warning => Assert.DoesNotContain("\\", warning));
        Assert.All(result.WarningCodes, warning => Assert.DoesNotContain("/", warning));
    }

    [Fact]
    public async Task Persists_last_scan_and_handoff_across_reopen_and_is_idempotent()
    {
        var discovery = new SessionDiscoveryResult([new(["synthetic.jsonl"])], new(1, 0, []));
        var handoff = new SessionTokenRecord(DateTimeOffset.UnixEpoch, "model-a", 1, 2, 3);
        var scans = new[] { new SessionParseResult([handoff], ["session_incomplete_tail"], false) };
        ScanRunProvenance first;
        await using (var store = new SqliteScanRunStore(_databasePath))
        {
            var recorder = new ScanRunRecorder(store, () => DateTimeOffset.UnixEpoch);
            first = await recorder.RecordAsync(discovery, scans, default);
            Assert.Equivalent(first, await recorder.RecordAsync(discovery, scans, default), strict: true);
        }

        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM scan_run WHERE was_cancelled = 0;";
        Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);

        await using var reopened = new SqliteScanRunStore(_databasePath);
        var loaded = await reopened.LoadLastAsync(default);
        Assert.NotNull(loaded);
        Assert.Equal((1, 1, 0, 1, false, ScanCoverageState.Partial), (loaded!.FilesDiscovered, loaded.FilesRead, loaded.FilesSkipped, loaded.FilesDeferred, loaded.WasCancelled, loaded.Coverage));
        Assert.Equal(["session_incomplete_tail"], loaded.WarningCodes);
        Assert.Equal(first.Handoff, loaded.Handoff);
        var replayed = await new ScanRunRecorder(reopened, () => DateTimeOffset.Parse("2030-01-02T00:00:00Z")).RecordAsync(discovery, scans, default);
        Assert.Equal(loaded.Handoff, replayed.Handoff);
    }

    [Fact]
    public async Task Persisted_scan_identity_does_not_contain_raw_warning_values()
    {
        const string secret = "Bearer scan-secret";
        const string path = @"C:\Users\someone\private.jsonl";
        var discovery = new SessionDiscoveryResult([new(["synthetic.jsonl"])], new(1, 0, [path, secret]));
        await using var store = new SqliteScanRunStore(_databasePath);

        await new ScanRunRecorder(store, () => DateTimeOffset.UnixEpoch).RecordAsync(discovery, [new([], [], false)], default);

        await using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT fingerprint || '|' || warnings FROM scan_run;";
        var persisted = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain(secret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(path, persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_before_commit_persists_cancelled_last_scan_without_committing_handoff()
    {
        var discovery = new SessionDiscoveryResult([new(["synthetic.jsonl"])], new(1, 0, []));
        var scan = new SessionParseResult([new(DateTimeOffset.UnixEpoch, "model-a", 1, 2, 3)], [], false);
        using var cancellation = new CancellationTokenSource();
        await using var store = new SqliteScanRunStore(_databasePath);
        var recorder = new ScanRunRecorder(store, () => DateTimeOffset.UnixEpoch, cancellation.Cancel);

        await Assert.ThrowsAsync<OperationCanceledException>(() => recorder.RecordAsync(discovery, [scan], cancellation.Token).AsTask());

        var loaded = await store.LoadLastAsync(default);
        Assert.NotNull(loaded);
        Assert.True(loaded!.WasCancelled);
        Assert.Empty(loaded.Handoff);
        Assert.Equal(ScanCoverageState.Partial, loaded.Coverage);
    }

        [Fact]
        public async Task Repeated_cancellation_persists_the_latest_status_across_reopen()
        {
            var discovery = new SessionDiscoveryResult([new(["synthetic.jsonl"])], new(1, 0, []));
            var scan = new SessionParseResult([], [], false);
            await using (var store = new SqliteScanRunStore(_databasePath))
            {
                foreach (var timestamp in new[] { DateTimeOffset.Parse("2030-01-01T00:00:00Z"), DateTimeOffset.Parse("2030-01-02T00:00:00Z") })
                {
                    using var cancellation = new CancellationTokenSource();
                    var recorder = new ScanRunRecorder(store, () => timestamp, cancellation.Cancel);
                    await Assert.ThrowsAsync<OperationCanceledException>(() => recorder.RecordAsync(discovery, [scan], cancellation.Token).AsTask());
                }
            }

            await using var reopened = new SqliteScanRunStore(_databasePath);
            var loaded = await reopened.LoadLastAsync(default);
            Assert.NotNull(loaded);
            Assert.True(loaded!.WasCancelled);
            Assert.Equal(DateTimeOffset.Parse("2030-01-02T00:00:00Z"), loaded.CompletedAt);
        }

        public void Dispose()
        {
            if (File.Exists(_databasePath)) File.Delete(_databasePath);
        }

    [Fact]
    public async Task Retains_only_safe_session_warning_codes()
    {
        var recorder = new ScanRunRecorder(new InMemoryScanRunStore(), () => DateTimeOffset.UnixEpoch);
        var discovery = new SessionDiscoveryResult([new(["fixture.jsonl"])], new(1, 0, ["unexpected_warning"]));

        var result = await recorder.RecordAsync(discovery, [new([], ["session_malformed_record"], false)], default);

        Assert.Equal(["session_malformed_record"], result.WarningCodes);
        Assert.Equal(ScanCoverageState.Partial, result.Coverage);
    }
}
