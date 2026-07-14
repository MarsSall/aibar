using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class SqliteDailyModelUsageStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-daily-usage-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Red_creates_a_token_only_migration_and_reopens_merged_aggregates()
    {
        var first = Usage("gpt-5", 2, 3, 5);
        await using (var store = new SqliteDailyModelUsageStore(_path))
        {
            await store.SaveAsync([first], default);
            await store.SaveAsync([Usage("gpt-5", 7, 11, 13), Usage("Unknown", 1, 0, 0)], default);
        }

        await using var reopened = new SqliteDailyModelUsageStore(_path);
        var usage = await reopened.LoadAsync(default);
        Assert.Contains(usage, item => item.Model == "gpt-5" && item.Tokens == new TokenTotals(9, 14, 18));
        Assert.Contains(usage, item => item.Model == "Unknown" && item.Tokens == new TokenTotals(1, 0, 0));
        await using var connection = Open();
        var columns = (string)(await Scalar(connection, "SELECT group_concat(name, ',') FROM pragma_table_info('daily_model_usage');"))!;
        Assert.DoesNotContain("price", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", columns, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Red_rolls_back_all_aggregate_mutation_when_the_precommit_seam_fails()
    {
        await using var stable = new SqliteDailyModelUsageStore(_path);
        await stable.SaveAsync([Usage("gpt-5", 2, 3, 5), Usage("Unknown", 1, 1, 1)], default);
        await using var failing = new SqliteDailyModelUsageStore(_path, () => throw new InvalidOperationException("synthetic crash"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.SaveAsync([Usage("gpt-5", 100, 100, 100), Usage("Unknown", 100, 100, 100)], default).AsTask());

        Assert.Equal(new TokenTotals(2, 3, 5), Assert.Single((await stable.LoadAsync(default)).Where(item => item.Model == "gpt-5")).Tokens);
        Assert.Equal(new TokenTotals(1, 1, 1), Assert.Single((await stable.LoadAsync(default)).Where(item => item.Model == "Unknown")).Tokens);
    }

    [Fact]
    public async Task Triangulate_migrates_alongside_existing_application_tables_without_destructive_ownership()
    {
        await using (var connection = Open())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE quota_snapshot (id INTEGER PRIMARY KEY, retained TEXT); INSERT INTO quota_snapshot VALUES (1, 'keep'); PRAGMA user_version = 2;";
            await command.ExecuteNonQueryAsync();
        }

        await using var store = new SqliteDailyModelUsageStore(_path);
        await store.SaveAsync([Usage("gpt-5", 1, 2, 3)], default);
        await using var verify = Open();
        Assert.Equal("keep", await Scalar(verify, "SELECT retained FROM quota_snapshot WHERE id = 1;"));
        Assert.Equal(1L, await Scalar(verify, "SELECT COUNT(*) FROM daily_model_usage;"));
    }

    [Fact]
    public async Task Red_retains_stored_token_facts_when_a_caller_reprices_a_loaded_aggregate()
    {
        await using (var store = new SqliteDailyModelUsageStore(_path))
            await store.SaveAsync([Usage("gpt-5", 2, 3, 5)], default);

        await using var reopened = new SqliteDailyModelUsageStore(_path);
        var stored = Assert.Single(await reopened.LoadAsync(default));
        var syntheticReprice = stored.Tokens.Total * 99m;
        Assert.Equal(990m, syntheticReprice);
        Assert.Equal(new TokenTotals(2, 3, 5), Assert.Single(await reopened.LoadAsync(default)).Tokens);
    }

    [Fact]
    public async Task Red_rejects_a_future_schema_before_mutating_the_aggregate_table()
    {
        await using (var connection = Open())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE daily_model_usage_schema (version INTEGER NOT NULL); INSERT INTO daily_model_usage_schema VALUES (2);";
            await command.ExecuteNonQueryAsync();
        }

        await using var store = new SqliteDailyModelUsageStore(_path);
        await Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync([Usage("gpt-5", 1, 2, 3)], default).AsTask());

        await using var verify = Open();
        Assert.Equal(0L, await Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'daily_model_usage';"));
        Assert.Equal(2L, await Scalar(verify, "SELECT version FROM daily_model_usage_schema;"));
    }

    [Fact]
    public async Task Red_rejects_integer_overflow_atomically_and_reopens_the_previous_aggregate()
    {
        await using (var store = new SqliteDailyModelUsageStore(_path))
        {
            await store.SaveAsync([Usage("gpt-5", long.MaxValue - 1, long.MaxValue - 1, long.MaxValue - 1)], default);
            await store.SaveAsync([Usage("gpt-5", 1, 1, 1)], default);
            await Assert.ThrowsAsync<OverflowException>(() => store.SaveAsync([Usage("gpt-5", 1, 1, 1)], default).AsTask());
        }

        await using var reopened = new SqliteDailyModelUsageStore(_path);
        Assert.Equal(new TokenTotals(long.MaxValue, long.MaxValue, long.MaxValue), Assert.Single(await reopened.LoadAsync(default)).Tokens);
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
    private SqliteConnection Open() { var connection = new SqliteConnection($"Data Source={_path};Pooling=False"); connection.Open(); return connection; }
    private static async Task<object?> Scalar(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private static DailyUsage Usage(string model, long input, long cached, long output) => new(new DateOnly(2026, 7, 14), "UTC", TimeSpan.Zero, model, new(input, cached, output));
}
