using System.Globalization;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

public sealed class SqliteQuotaSnapshotStore : IQuotaSnapshotStore, IAsyncDisposable
{
    private const int SchemaVersion = 2;
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
            return decimal.TryParse(reader.GetString(0), NumberStyles.Number, CultureInfo.InvariantCulture, out var primary) && DateTimeOffset.TryParse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.None, out var primaryReset) && decimal.TryParse(reader.GetString(2), NumberStyles.Number, CultureInfo.InvariantCulture, out var weekly) && DateTimeOffset.TryParse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.None, out var weeklyReset) && DateTimeOffset.TryParse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.None, out var retrieved) && (reader.IsDBNull(5) || credits.HasValue) && primary is >= 0 and <= 100 && weekly is >= 0 and <= 100
                ? new(new(primary, primaryReset), new(weekly, weeklyReset), retrieved, credits) : null;
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
            command.CommandText = "INSERT OR REPLACE INTO quota_snapshot (id, schema_version, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, reset_credits, retrieved_at, schema_adapter_version) VALUES (1, 2, $primary, $primaryReset, $weekly, $weeklyReset, $credits, $retrieved, 1);";
            command.Parameters.AddWithValue("$primary", snapshot.Primary.PercentageUsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$primaryReset", snapshot.Primary.ResetAt.ToString("O"));
            command.Parameters.AddWithValue("$weekly", snapshot.Weekly.PercentageUsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$weeklyReset", snapshot.Weekly.ResetAt.ToString("O"));
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
        if (version == 0) await ExecuteAsync(connection, SqlBatch.CreateV2, cancellationToken);
    }

    private enum SqlBatch { Configure, CreateV2, MigrateV1 }

    private static async Task ExecuteAsync(SqliteConnection connection, SqlBatch batch, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = batch switch
        {
            SqlBatch.Configure => "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;",
            SqlBatch.CreateV2 => SchemaSql + " PRAGMA user_version = 2;",
            SqlBatch.MigrateV1 => "BEGIN; ALTER TABLE quota_snapshot RENAME TO quota_snapshot_v1;" + SchemaSql + " INSERT INTO quota_snapshot (id, schema_version, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, reset_credits, retrieved_at, schema_adapter_version) SELECT id, 2, primary_percentage, primary_reset_at, weekly_percentage, weekly_reset_at, reset_credits, retrieved_at, 1 FROM quota_snapshot_v1; DROP TABLE quota_snapshot_v1; PRAGMA user_version = 2; COMMIT;",
            _ => throw new ArgumentOutOfRangeException(nameof(batch)),
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string SchemaSql = "CREATE TABLE IF NOT EXISTS schema_meta (version INTEGER PRIMARY KEY); INSERT OR IGNORE INTO schema_meta VALUES (2); CREATE TABLE IF NOT EXISTS quota_snapshot (id INTEGER PRIMARY KEY CHECK (id = 1), schema_version INTEGER NOT NULL REFERENCES schema_meta(version), primary_percentage TEXT NOT NULL, primary_reset_at TEXT NOT NULL, weekly_percentage TEXT NOT NULL, weekly_reset_at TEXT NOT NULL, reset_credits TEXT NULL, retrieved_at TEXT NOT NULL, schema_adapter_version INTEGER NOT NULL);";
}
