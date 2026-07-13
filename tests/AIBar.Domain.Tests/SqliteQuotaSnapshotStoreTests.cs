using System.Globalization;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class SqliteQuotaSnapshotStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Creates_schema_with_wal_foreign_keys_and_an_empty_store()
    {
        await using var store = new SqliteQuotaSnapshotStore(_path);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
        await using var connection = Open(foreignKeys: true);
        Assert.Equal("wal", (string)(await Scalar(connection, "PRAGMA journal_mode;"))!);
        Assert.Equal(1L, (long)(await Scalar(connection, "PRAGMA foreign_keys;"))!);
        await Assert.ThrowsAsync<SqliteException>(() => Execute(connection, "INSERT INTO quota_snapshot (id, schema_version, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, retrieved_at, schema_adapter_version) VALUES (1, 999, '0', '2030-01-01T00:00:00+00:00', '0', '2030-01-01T00:00:00+00:00', '2030-01-01T00:00:00+00:00', 1);"));
        Assert.Contains("schema_adapter_version", await Text(connection, "SELECT sql FROM sqlite_master WHERE name = 'quota_snapshot';"));
    }

    [Fact]
    public async Task Saves_loads_and_clears_only_normalized_snapshot_data()
    {
        await using var store = new SqliteQuotaSnapshotStore(_path);
        var snapshot = Snapshot();
        await store.SaveAsync(snapshot, CancellationToken.None);
        Assert.Equal(snapshot, await store.LoadAsync(CancellationToken.None));
        await using var connection = Open();
        var columns = await Text(connection, "SELECT group_concat(name, ',') FROM pragma_table_info('quota_snapshot');");
        Assert.DoesNotContain("credential", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("body", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("header", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", columns, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer seeded-secret raw-body D:/synthetic", await Text(connection, "SELECT sql FROM sqlite_master WHERE name = 'quota_snapshot';"));
        await store.SaveAsync(Snapshot(15, 2.5m), CancellationToken.None);
        Assert.Equal(2.5m, (await store.LoadAsync(CancellationToken.None))!.ResetCredits);
        await store.SaveAsync(Snapshot(15), CancellationToken.None);
        Assert.Null((await store.LoadAsync(CancellationToken.None))!.ResetCredits);
        await store.ClearAsync(CancellationToken.None);
        Assert.Null(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Migrates_v1_and_preserves_its_snapshot()
    {
        await using (var connection = Open())
        {
            await Execute(connection, "CREATE TABLE quota_snapshot (id INTEGER PRIMARY KEY, primary_percentage TEXT NOT NULL, primary_reset_at TEXT NOT NULL, weekly_percentage TEXT NOT NULL, weekly_reset_at TEXT NOT NULL, reset_credits TEXT NULL, retrieved_at TEXT NOT NULL); PRAGMA user_version = 1;");
            await Execute(connection, "INSERT INTO quota_snapshot VALUES (1, '10', '2030-01-01T01:00:00+00:00', '20', '2030-01-02T01:00:00+00:00', '7.5', '2030-01-01T00:00:00+00:00');");
        }
        await using var store = new SqliteQuotaSnapshotStore(_path);
        Assert.Equal(7.5m, (await store.LoadAsync(CancellationToken.None))!.ResetCredits);
        await using var migrated = Open();
        Assert.Equal(2L, (long)(await Scalar(migrated, "PRAGMA user_version;"))!);
    }

    [Fact]
    public async Task Loads_fractional_values_invariantly_under_a_non_invariant_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); await using var store = new SqliteQuotaSnapshotStore(_path); await store.SaveAsync(Snapshot(12.5m, 3.25m), default); Assert.Equal(Snapshot(12.5m, 3.25m), await store.LoadAsync(default)); }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public async Task Dispose_waits_for_an_active_operation_rejects_queued_operations_and_completes_concurrent_callers()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var store = new SqliteQuotaSnapshotStore(_path, () => { entered.Set(); release.Wait(); });
        var active = Task.Run(async () => await store.SaveAsync(Snapshot(), default)); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        var queued = store.SaveAsync(Snapshot(90), default).AsTask(); Assert.False(queued.IsCompleted);
        var dispose = store.DisposeAsync().AsTask(); var concurrentDispose = store.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted); Assert.False(concurrentDispose.IsCompleted); release.Set();
        await active; await Task.WhenAll(dispose, concurrentDispose);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.LoadAsync(default).AsTask());
    }

        [Fact]
        public async Task Rejects_future_schema_without_a_destructive_downgrade()
    {
        await using (var connection = Open()) await Execute(connection, "CREATE TABLE preserved (value TEXT); INSERT INTO preserved VALUES ('keep'); PRAGMA user_version = 99;");
        await using var store = new SqliteQuotaSnapshotStore(_path);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.LoadAsync(CancellationToken.None).AsTask());
        await using var verify = Open();
        Assert.Equal("keep", await Text(verify, "SELECT value FROM preserved;"));
    }

    [Fact]
    public async Task Rolls_back_injected_failure_and_malformed_rows_load_empty()
    {
        await using var stable = new SqliteQuotaSnapshotStore(_path);
        await stable.SaveAsync(Snapshot(10), CancellationToken.None);
        await using (var failing = new SqliteQuotaSnapshotStore(_path, () => throw new InvalidOperationException("synthetic crash")))
            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.SaveAsync(Snapshot(90), CancellationToken.None).AsTask());
        Assert.Equal(10, (await stable.LoadAsync(CancellationToken.None))!.Primary.PercentageUsed);
        await using var connection = Open();
        await Execute(connection, "UPDATE quota_snapshot SET primary_percentage = 'not-a-number';");
        Assert.Null(await stable.LoadAsync(CancellationToken.None));
    }

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }
    private SqliteConnection Open(bool foreignKeys = false) { var connection = new SqliteConnection($"Data Source={_path};Foreign Keys={foreignKeys};Pooling=False"); connection.Open(); return connection; }
    private static async Task<object?> Scalar(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private static async Task<string> Text(SqliteConnection connection, string sql) => (string)(await Scalar(connection, sql))!;
    private static async Task Execute(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static QuotaSnapshot Snapshot(decimal percentage = 42, decimal? credits = null) => new(new(percentage, new DateTimeOffset(2030, 1, 1, 1, 0, 0, TimeSpan.Zero)), new(20, new DateTimeOffset(2030, 1, 2, 1, 0, 0, TimeSpan.Zero)), new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero), credits);
}
