using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

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
            Assert.Empty(await failing.LoadContributionAsync(coordinator.SourceFingerprint(_jsonl), default));
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

    [Fact]
    public async Task Source_attribution_is_independent_and_legacy_aggregates_require_rebuild()
    {
        await using var store = new SqliteDailyModelUsageStore(_database);
        await store.SaveAsync([new(new DateOnly(2030, 1, 1), "UTC", TimeSpan.Zero, "gpt-5", new(1, 0, 0)), new(new DateOnly(2030, 1, 1), "UTC", TimeSpan.Zero, "gpt-4", new(0, 1, 0))], default);
        Assert.True((await store.GetContributionStatusAsync(default)).RebuildRequired);

        var first = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n");
            await File.WriteAllTextAsync(first, Line(0, 3, 0) + "\n");
            var coordinator = Coordinator(store);
            await coordinator.ScanAsync(_jsonl, default);
            await coordinator.ScanAsync(first, default);
            Assert.True((await store.GetContributionStatusAsync(default)).RebuildRequired);
            Assert.Equal(new TokenTotals(2, 0, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(_jsonl), default)).Tokens);
            Assert.Equal(new TokenTotals(0, 3, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(first), default)).Tokens);
            Assert.Equal("v1", await store.LoadContributionPolicyAsync(coordinator.SourceFingerprint(_jsonl), default));
        }
        finally { if (File.Exists(first)) File.Delete(first); }
    }

    [Fact]
    public async Task MultiSourceRebuild_replaces_selected_source_preserves_unrelated_source_and_requires_all_sources_for_policy_transition()
    {
        var other = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n");
            await File.WriteAllTextAsync(other, Line(0, 3, 0) + "\n");
            await using var store = new SqliteDailyModelUsageStore(_database);
            var coordinator = Coordinator(store);
            await coordinator.ScanAsync(_jsonl, default); await coordinator.ScanAsync(other, default);
            await File.WriteAllTextAsync(_jsonl, Line(5, 0, 0) + "\n");

            await coordinator.RebuildAsync([_jsonl], false, default);

            Assert.Equal(new TokenTotals(5, 0, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(_jsonl), default)).Tokens);
            Assert.Equal(new TokenTotals(0, 3, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(other), default)).Tokens);
            Assert.Equal(new TokenTotals(5, 3, 0), Assert.Single(await store.LoadAsync(default)).Tokens);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(store, "v2").RebuildAsync([_jsonl], false, default).AsTask());
            await Coordinator(store, "v2").RebuildAsync([_jsonl, other], false, default);
            Assert.Equal("v2", await store.LoadContributionPolicyAsync(coordinator.SourceFingerprint(other), default));
        }
        finally { if (File.Exists(other)) File.Delete(other); }
    }

    [Fact]
    public async Task MultiSourceRebuild_full_rescan_rejects_equal_count_substitution_without_mutation()
    {
        var other = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}.jsonl"); var substitute = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n"); await File.WriteAllTextAsync(other, Line(0, 3, 0) + "\n"); await File.WriteAllTextAsync(substitute, Line(0, 0, 5) + "\n");
            await using var store = new SqliteDailyModelUsageStore(_database); var coordinator = Coordinator(store);
            await coordinator.ScanAsync(_jsonl, default); await coordinator.ScanAsync(other, default);
            await store.SaveAsync([new(new DateOnly(2030, 1, 1), "UTC", TimeSpan.Zero, "gpt-5", new(7, 0, 0))], default); var prior = await store.LoadAsync(default); var priorFirst = await store.LoadCheckpointAsync(coordinator.SourceFingerprint(_jsonl), default); var priorOther = await store.LoadCheckpointAsync(coordinator.SourceFingerprint(other), default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RebuildAsync([_jsonl, substitute], true, default).AsTask());
            Assert.Equal(prior, await store.LoadAsync(default)); Assert.True((await store.GetContributionStatusAsync(default)).RebuildRequired);
            Assert.Equal(new TokenTotals(2, 0, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(_jsonl), default)).Tokens); Assert.Equal(new TokenTotals(0, 3, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(other), default)).Tokens); Assert.Empty(await store.LoadContributionAsync(coordinator.SourceFingerprint(substitute), default));
            Assert.Equal(priorFirst, await store.LoadCheckpointAsync(coordinator.SourceFingerprint(_jsonl), default)); Assert.Equal(priorOther, await store.LoadCheckpointAsync(coordinator.SourceFingerprint(other), default)); Assert.Null(await store.LoadCheckpointAsync(coordinator.SourceFingerprint(substitute), default)); Assert.Equal("v1", await store.LoadContributionPolicyAsync(coordinator.SourceFingerprint(_jsonl), default)); Assert.Equal("v1", await store.LoadContributionPolicyAsync(coordinator.SourceFingerprint(other), default));
        }
        finally { if (File.Exists(other)) File.Delete(other); if (File.Exists(substitute)) File.Delete(substitute); }
    }

    [Fact]
    public async Task MultiSourceRebuild_full_rescan_accepts_partially_attributed_checkpoint_universe()
    {
        var other = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n"); await File.WriteAllTextAsync(other, Line(0, 3, 0) + "\n");
            await using var store = new SqliteDailyModelUsageStore(_database); var coordinator = Coordinator(store); await coordinator.ScanAsync(_jsonl, default); await coordinator.ScanAsync(other, default);
            await ExecuteAsync("DELETE FROM source_daily_model_usage WHERE source_fingerprint=$fingerprint; DELETE FROM source_contribution_state WHERE source_fingerprint=$fingerprint; UPDATE daily_model_usage_migration SET rebuild_required=1;", coordinator.SourceFingerprint(other));
            await Coordinator(store, "v2").RebuildAsync([_jsonl, other], true, default);
            Assert.False((await store.GetContributionStatusAsync(default)).RebuildRequired); Assert.Equal(new TokenTotals(2, 3, 0), Assert.Single(await store.LoadAsync(default)).Tokens);
            Assert.Equal("v2", await store.LoadContributionPolicyAsync(coordinator.SourceFingerprint(_jsonl), default)); Assert.Equal("v2", await store.LoadContributionPolicyAsync(coordinator.SourceFingerprint(other), default));
            Assert.Equal(new TokenTotals(0, 3, 0), Assert.Single(await store.LoadContributionAsync(coordinator.SourceFingerprint(other), default)).Tokens);
        }
        finally { if (File.Exists(other)) File.Delete(other); }
    }

    [Fact]
    public async Task MultiSourceRebuild_requires_explicit_full_rescan_for_policyless_legacy_data_and_retries_atomically()
    {
        await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n");
        await using (var legacy = new SqliteDailyModelUsageStore(_database))
        {
            await legacy.SaveAsync([new(new DateOnly(2030, 1, 1), "UTC", TimeSpan.Zero, "gpt-5", new(9, 0, 0))], default);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(legacy).RebuildAsync([_jsonl], false, default).AsTask());
        }
        await using (var failing = new SqliteDailyModelUsageStore(_database, () => throw new InvalidOperationException("synthetic failure")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(failing).RebuildAsync([_jsonl], true, default).AsTask());
        await using var stable = new SqliteDailyModelUsageStore(_database);
        await Coordinator(stable).RebuildAsync([_jsonl], true, default);
        await Coordinator(stable).RebuildAsync([_jsonl], false, default);

        Assert.False((await stable.GetContributionStatusAsync(default)).RebuildRequired);
        Assert.Equal(new TokenTotals(2, 0, 0), Assert.Single(await stable.LoadAsync(default)).Tokens);
    }

    [Fact]
    public async Task MultiSourceRebuild_cancellation_leaves_prior_dataset_and_retries_once()
    {
        await File.WriteAllTextAsync(_jsonl, Line(2, 0, 0) + "\n");
        await using (var initial = new SqliteDailyModelUsageStore(_database)) await Coordinator(initial).ScanAsync(_jsonl, default);
        await File.WriteAllTextAsync(_jsonl, Line(5, 0, 0) + "\n");
        using var cancellation = new CancellationTokenSource();
        await using (var cancelled = new SqliteDailyModelUsageStore(_database, cancellation.Cancel))
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Coordinator(cancelled).RebuildAsync([_jsonl], false, cancellation.Token).AsTask());
        await using var stable = new SqliteDailyModelUsageStore(_database);
        Assert.Equal(new TokenTotals(2, 0, 0), Assert.Single(await stable.LoadAsync(default)).Tokens);
        await Coordinator(stable).RebuildAsync([_jsonl], false, default);
        Assert.Equal(new TokenTotals(5, 0, 0), Assert.Single(await stable.LoadAsync(default)).Tokens);
    }

    private static AnalyticsScanCoordinator Coordinator(SqliteDailyModelUsageStore store, string parserVersion = "v1") => new(
        new SessionJsonlScanner(new SessionCheckpointStore(), parserVersion), store,
        new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, parserVersion)));
    private async Task ExecuteAsync(string sql, string fingerprint)
    {
        await using var connection = new SqliteConnection($"Data Source={_database};Pooling=False"); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$fingerprint", fingerprint); await command.ExecuteNonQueryAsync();
    }
    private static string Line(long input, long cached, long output) => $"{{\"timestamp\":\"2030-01-01T00:00:00Z\",\"model\":\"gpt-5\",\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":{cached},\"output_tokens\":{output}}}}}";
    public void Dispose() { if (File.Exists(_jsonl)) File.Delete(_jsonl); if (File.Exists(_database)) File.Delete(_database); }
}
