using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class SqliteQuotaHistoryStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-history-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 2, 1, 12, 0, 0, TimeSpan.Zero);
    private string Database => Path.Combine(_root, "quota-history.db");
    public SqliteQuotaHistoryStoreTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Round_trip_is_durable_ordered_and_isolated_by_provider_window_and_inclusive_range()
    {
        var first = Sample(Now.AddHours(-2));
        var last = Sample(Now, percentage: 100);
        await using (var store = new SqliteQuotaHistoryStore(Database))
        {
            await store.AppendAsync([last, first, Sample(Now.AddHours(-1)), first with { Provider = QuotaHistoryProvider.Claude, Source = QuotaHistorySource.ClaudeRemote }, first with { Window = QuotaHistoryWindow.Weekly }], Now, default);
        }
        await using var reopened = new SqliteQuotaHistoryStore(Database);
        var rows = await reopened.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, first.RetrievedAt, last.RetrievedAt, default);
        Assert.Equal(new[] { first.RetrievedAt, Now.AddHours(-1), last.RetrievedAt }, rows.Select(row => row.RetrievedAt));
        Assert.Equal(first, rows[0]); Assert.Equal(last, rows[2]);
        Assert.Single(await reopened.QueryAsync(QuotaHistoryProvider.Claude, QuotaHistoryWindow.FiveHour, first.RetrievedAt, Now, default));
        Assert.Single(await reopened.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.Weekly, first.RetrievedAt, Now, default));
        Assert.Empty(await reopened.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now.AddTicks(1), Now.AddDays(1), default));
    }

    [Fact]
    public async Task Dedupe_uses_provider_window_and_instant_not_percentage_reset_source_or_offset()
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        var original = Sample(Now);
        await store.AppendAsync([original], Now, default);
        await store.AppendAsync([original with { Percentage = 99, ResetAt = Now.AddDays(2), RetrievedAt = Now.ToOffset(TimeSpan.FromHours(2)) }], Now, default);
        Assert.Equal(original, Assert.Single(await store.QueryAsync(original.Provider, original.Window, Now, Now, default)));
    }

    [Fact]
    public async Task Every_append_prunes_all_providers_strictly_before_thirty_days_including_old_incoming_rows()
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        var boundary = Sample(Now.AddDays(-30));
        var expired = Sample(boundary.RetrievedAt.AddTicks(-1)) with { Provider = QuotaHistoryProvider.Claude, Source = QuotaHistorySource.ClaudeLocal };
        await store.AppendAsync([expired, boundary], Now.AddDays(-1), default);
        await store.AppendAsync([Sample(Now), expired], Now, default);
        Assert.Equal(2, (await store.QueryAsync(boundary.Provider, boundary.Window, Now.AddDays(-31), Now, default)).Count);
        Assert.Empty(await store.QueryAsync(expired.Provider, expired.Window, Now.AddDays(-31), Now, default));
    }

    [Fact]
    public async Task Schema_is_v1_WAL_and_contains_only_normalized_sample_columns()
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        await store.AppendAsync([Sample(Now)], Now, default);
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;"; Assert.Equal(1L, (long)(await command.ExecuteScalarAsync())!);
        command.CommandText = "PRAGMA journal_mode;"; Assert.Equal("wal", (string)(await command.ExecuteScalarAsync())!);
        command.CommandText = "PRAGMA table_info(quota_history);";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>(); while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
        Assert.Equal(new[] { "provider", "window", "percentage", "reset_at", "retrieved_at", "source" }, columns);
    }

    [Fact]
    public async Task Future_schema_is_rejected_without_overwriting_it()
    {
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version = 2;"; await command.ExecuteNonQueryAsync();
        }
        await using var store = new SqliteQuotaHistoryStore(Database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync([Sample(Now)], Now, default).AsTask());
    }

    [Fact]
    public async Task Concurrent_appends_are_serialized_and_disposal_rejects_further_access()
    {
        var store = new SqliteQuotaHistoryStore(Database);
        await Task.WhenAll(Enumerable.Range(0, 50).Select(i => store.AppendAsync([Sample(Now.AddTicks(i))], Now, default).AsTask()));
        Assert.Equal(50, (await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now.AddSeconds(1), default)).Count);
        await Task.WhenAll(store.DisposeAsync().AsTask(), store.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.AppendAsync([Sample(Now)], Now, default).AsTask());
    }

    [Fact]
    public async Task Invalid_batch_is_atomic_cancellation_and_reversed_ranges_are_reported()
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AppendAsync([Sample(Now), Sample(Now.AddTicks(1), -1)], Now, default).AsTask());
        Assert.Empty(await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now.AddDays(1), default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AppendAsync([Sample(Now)], Now, cancelled.Token).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now.AddTicks(-1), default).AsTask());
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public async Task Percentages_outside_zero_to_one_hundred_are_rejected(double percentage)
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AppendAsync([Sample(Now, (decimal)percentage)], Now, default).AsTask());
    }

    [Fact]
    public async Task Invalid_identifiers_and_source_mismatches_are_rejected_and_paused_store_rejects_access()
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AppendAsync([Sample(Now) with { Provider = (QuotaHistoryProvider)99 }], Now, default).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AppendAsync([Sample(Now) with { Window = (QuotaHistoryWindow)99 }], Now, default).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AppendAsync([Sample(Now) with { Source = (QuotaHistorySource)99 }], Now, default).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync([Sample(Now) with { Source = QuotaHistorySource.ClaudeLocal }], Now, default).AsTask());
        await store.CancelAndWaitAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync([Sample(Now)], Now, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now, default).AsTask());
        store.ResumeAfterClear(); await store.AppendAsync([Sample(Now, 100)], Now, default);
        Assert.Single(await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now, default));
    }

    [Fact]
    public async Task Clear_drains_store_deletes_database_and_sidecars_and_resume_allows_fresh_samples()
    {
        await using var store = new SqliteQuotaHistoryStore(Database);
        await store.AppendAsync([Sample(Now)], Now, default);
        await new ClearAiBarDataService(_root, [store], _ => ValueTask.CompletedTask).ClearAsync(default);
        foreach (var suffix in new[] { "", "-wal", "-shm" }) Assert.False(File.Exists(Database + suffix));
        Assert.Empty(await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now, default));
        await store.AppendAsync([Sample(Now)], Now, default);
        Assert.Single(await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now, default));
    }

    private static QuotaHistorySample Sample(DateTimeOffset at, decimal percentage = 0) => new(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, percentage, Now.AddHours(5), at, QuotaHistorySource.CodexLocal);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
