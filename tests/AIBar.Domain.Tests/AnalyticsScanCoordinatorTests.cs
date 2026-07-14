using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class AnalyticsScanCoordinatorTests : IDisposable
{
    private readonly string _jsonl = Path.Combine(Path.GetTempPath(), $"aibar-coordinator-{Guid.NewGuid():N}.jsonl");
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"aibar-coordinator-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Scanner_handoff_retries_after_injected_failure_without_advancing_checkpoint_or_double_counting()
    {
        await File.WriteAllTextAsync(_jsonl, Line(2, 3, 4) + "\n");
        await using (var failing = new SqliteDailyModelUsageStore(_database, () => throw new InvalidOperationException("synthetic failure")))
        {
            var coordinator = Coordinator(failing);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ScanAsync(_jsonl, default).AsTask());
            Assert.Null(await failing.LoadCheckpointAsync(coordinator.SourceFingerprint(_jsonl), default));
        }

        await using var stable = new SqliteDailyModelUsageStore(_database);
        var retry = Coordinator(stable);
        await retry.ScanAsync(_jsonl, default);
        await retry.ScanAsync(_jsonl, default);

        Assert.Equal(new TokenTotals(2, 3, 4), Assert.Single(await stable.LoadAsync(default)).Tokens);
        Assert.NotNull(await stable.LoadCheckpointAsync(retry.SourceFingerprint(_jsonl), default));
    }

    [Fact]
    public async Task Appended_records_are_aggregated_once_and_incomplete_tail_is_retained_as_partial()
    {
        await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n");
        await using var store = new SqliteDailyModelUsageStore(_database);
        var coordinator = Coordinator(store);
        await coordinator.ScanAsync(_jsonl, default);
        await File.AppendAllTextAsync(_jsonl, Line(5, 1, 2) + "\n" + Line(9, 9, 9));

        var result = await coordinator.ScanAsync(_jsonl, default);

        Assert.Contains("session_incomplete_tail", result.WarningCodes);
        Assert.Equal(new TokenTotals(5, 1, 2), Assert.Single(await store.LoadAsync(default)).Tokens);
    }

    [Fact]
    public async Task Scanner_path_cancellation_before_commit_leaves_aggregate_and_checkpoint_retryable()
    {
        await File.WriteAllTextAsync(_jsonl, Line(2, 3, 4) + "\n");
        using var cancellation = new CancellationTokenSource();
        await using (var cancelled = new SqliteDailyModelUsageStore(_database, cancellation.Cancel))
        {
            var coordinator = Coordinator(cancelled);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.ScanAsync(_jsonl, cancellation.Token).AsTask());
            Assert.Empty(await cancelled.LoadAsync(default));
            Assert.Null(await cancelled.LoadCheckpointAsync(coordinator.SourceFingerprint(_jsonl), default));
        }

        await using var retryStore = new SqliteDailyModelUsageStore(_database);
        await Coordinator(retryStore).ScanAsync(_jsonl, default);
        Assert.Equal(new TokenTotals(2, 3, 4), Assert.Single(await retryStore.LoadAsync(default)).Tokens);
    }

    [Fact]
    public async Task Rebuild_required_retains_prior_aggregate_and_checkpoint()
    {
        await File.WriteAllTextAsync(_jsonl, Line(2, 3, 4) + "\n");
        await using var store = new SqliteDailyModelUsageStore(_database);
        var initial = Coordinator(store);
        await initial.ScanAsync(_jsonl, default);
        var fingerprint = initial.SourceFingerprint(_jsonl);
        var prior = await store.LoadCheckpointAsync(fingerprint, default);

        var result = await Coordinator(store, "v2").ScanAsync(_jsonl, default);

        Assert.True(result.RebuildRequired);
        Assert.Empty(result.Usage);
        Assert.Equal(new TokenTotals(2, 3, 4), Assert.Single(await store.LoadAsync(default)).Tokens);
        Assert.Equal(prior, await store.LoadCheckpointAsync(fingerprint, default));
    }

    private static AnalyticsScanCoordinator Coordinator(SqliteDailyModelUsageStore store, string parserVersion = "v1") => new(
        new SessionJsonlScanner(new SessionCheckpointStore(), parserVersion), store,
        new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, "v1")));
    private static string Line(long input, long cached, long output) => $"{{\"timestamp\":\"2030-01-01T00:00:00Z\",\"model\":\"gpt-5\",\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":{cached},\"output_tokens\":{output}}}}}";
    public void Dispose() { if (File.Exists(_jsonl)) File.Delete(_jsonl); if (File.Exists(_database)) File.Delete(_database); }
}
