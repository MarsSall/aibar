using System.Globalization;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

public sealed class SqliteQuotaSnapshotStore : IQuotaSnapshotStore, IAsyncDisposable
{
    private const int SchemaVersion = 3;
    private readonly string _connectionString;
    private readonly Action? _beforeCommit;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TaskCompletionSource _disposeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposing;

    public SqliteQuotaSnapshotStore(string databasePath, Action? beforeCommit = null)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString();
        _beforeCommit = beforeCommit;
    }

    public async ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _lock.WaitAsync(cancellationToken); try
        {
            ThrowIfDisposing();
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, retrieved_at, reset_credits FROM quota_snapshot WHERE id = 1;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            var credits = reader.IsDBNull(5) ? (decimal?)null : decimal.TryParse(reader.GetString(5), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedCredits) ? parsedCredits : null;
            return TryReadWindow(reader, 0, 1, out var primary) && TryReadWindow(reader, 2, 3, out var weekly) &&
                (primary is not null || weekly is not null) && DateTimeOffset.TryParse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.None, out var retrieved) &&
                (reader.IsDBNull(5) || credits.HasValue) ? new(primary, weekly, retrieved, credits) : null;
        }
        finally { _lock.Release(); }
    }

    public async ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _lock.WaitAsync(cancellationToken); try
        {
            ThrowIfDisposing();
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT OR REPLACE INTO quota_snapshot (id, schema_version, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, reset_credits, retrieved_at, schema_adapter_version) VALUES (1, 3, $primary, $primaryReset, $weekly, $weeklyReset, $credits, $retrieved, 1);";
            command.Parameters.AddWithValue("$primary", snapshot.Primary?.PercentageUsed.ToString(CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$primaryReset", snapshot.Primary?.ResetAt.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$weekly", snapshot.Weekly?.PercentageUsed.ToString(CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$weeklyReset", snapshot.Weekly?.ResetAt.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$credits", snapshot.ResetCredits?.ToString(CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$retrieved", snapshot.RetrievedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken); _beforeCommit?.Invoke(); await transaction.CommitAsync(cancellationToken);
        }
        finally { _lock.Release(); }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _lock.WaitAsync(cancellationToken); try
        {
            ThrowIfDisposing();
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "DELETE FROM quota_snapshot;";
            await command.ExecuteNonQueryAsync(cancellationToken); _beforeCommit?.Invoke(); await transaction.CommitAsync(cancellationToken);
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0)
        {
            await _disposeCompleted.Task;
            return;
        }

        await _lock.WaitAsync();
        try { }
        finally
        {
            _lock.Release();
            _disposeCompleted.TrySetResult();
        }
    }

    private void ThrowIfDisposing() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposing(); var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        try { await InitializeAsync(connection, cancellationToken); return connection; } catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task InitializeAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, SqlBatch.Configure, cancellationToken);
        await using var versionCommand = connection.CreateCommand(); versionCommand.CommandText = "PRAGMA user_version;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(cancellationToken));
        if (version > SchemaVersion) throw new InvalidOperationException("Unsupported future SQLite schema.");
        if (version == 1) await ExecuteAsync(connection, SqlBatch.MigrateV1, cancellationToken);
        if (version == 2) await ExecuteAsync(connection, SqlBatch.MigrateV2, cancellationToken);
        if (version == 0) await ExecuteAsync(connection, SqlBatch.CreateV3, cancellationToken);
    }

    private enum SqlBatch { Configure, CreateV3, MigrateV1, MigrateV2 }

    private static async Task ExecuteAsync(SqliteConnection connection, SqlBatch batch, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = batch switch
        {
            SqlBatch.Configure => "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;",
            SqlBatch.CreateV3 => SchemaSql + " PRAGMA user_version = 3;",
            SqlBatch.MigrateV1 => MigrationSql("quota_snapshot_v1"),
            SqlBatch.MigrateV2 => MigrationSql("quota_snapshot_v2"),
            _ => throw new ArgumentOutOfRangeException(nameof(batch)),
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryReadWindow(SqliteDataReader reader, int percentageIndex, int resetIndex, out QuotaWindow? window)
    {
        window = null;
        var percentageNull = reader.IsDBNull(percentageIndex);
        var resetNull = reader.IsDBNull(resetIndex);
        if (percentageNull || resetNull) return percentageNull && resetNull;
        if (!decimal.TryParse(reader.GetString(percentageIndex), NumberStyles.Number, CultureInfo.InvariantCulture, out var percentage) || percentage is < 0 or > 100 ||
            !DateTimeOffset.TryParse(reader.GetString(resetIndex), CultureInfo.InvariantCulture, DateTimeStyles.None, out var resetAt)) return false;
        window = new(percentage, resetAt);
        return true;
    }

    private static string MigrationSql(string oldTable) => $"BEGIN; ALTER TABLE quota_snapshot RENAME TO {oldTable};" + SchemaSql + $" INSERT INTO quota_snapshot (id, schema_version, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, reset_credits, retrieved_at, schema_adapter_version) SELECT id, 3, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, reset_credits, retrieved_at, 1 FROM {oldTable}; DROP TABLE {oldTable}; PRAGMA user_version = 3; COMMIT;";

    private const string SchemaSql = "CREATE TABLE IF NOT EXISTS schema_meta (version INTEGER PRIMARY KEY); INSERT OR IGNORE INTO schema_meta VALUES (3); CREATE TABLE IF NOT EXISTS quota_snapshot (id INTEGER PRIMARY KEY CHECK (id = 1), schema_version INTEGER NOT NULL REFERENCES schema_meta(version), primary_percentage TEXT NULL, primary_reset_at TEXT NULL, weekly_percentage TEXT NULL, weekly_reset_at TEXT NULL, reset_credits TEXT NULL, retrieved_at TEXT NOT NULL, schema_adapter_version INTEGER NOT NULL, CHECK ((primary_percentage IS NULL AND primary_reset_at IS NULL) OR (primary_percentage IS NOT NULL AND primary_reset_at IS NOT NULL)), CHECK ((weekly_percentage IS NULL AND weekly_reset_at IS NULL) OR (weekly_percentage IS NOT NULL AND weekly_reset_at IS NOT NULL)), CHECK (primary_percentage IS NOT NULL OR weekly_percentage IS NOT NULL));";
}
