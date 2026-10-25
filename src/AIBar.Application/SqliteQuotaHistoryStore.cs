using System.Globalization;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

/// <summary>Independent schema-v2 history database (migrates v1 without losing rows). Each operation owns and closes its connection;
/// the semaphore serializes reads, writes, privacy drains and disposal.</summary>
public sealed class SqliteQuotaHistoryStore : IQuotaHistoryStore, IAiBarClearWork, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TaskCompletionSource _disposeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposing;
    private bool _paused;

    public SqliteQuotaHistoryStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
    }

    public static string DatabasePath(string dataDirectory) => Path.Combine(dataDirectory, "quota-history.db");

    public async ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(samples);
        // Snapshot the caller's batch before awaiting; validate everything before opening a transaction.
        var batch = samples.ToArray();
        foreach (var sample in batch) Validate(sample);
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            await using var connection = await OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            foreach (var sample in batch)
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "INSERT OR IGNORE INTO quota_history (provider, window, percentage, reset_at, retrieved_at, source) VALUES ($provider, $window, $percentage, $reset, $retrieved, $source);";
                command.Parameters.AddWithValue("$provider", ProviderName(sample.Provider));
                command.Parameters.AddWithValue("$window", WindowName(sample.Window));
                command.Parameters.AddWithValue("$percentage", sample.Percentage.ToString(CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$reset", sample.ResetAt.UtcTicks);
                command.Parameters.AddWithValue("$retrieved", sample.RetrievedAt.UtcTicks);
                command.Parameters.AddWithValue("$source", SourceName(sample.Source));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var prune = connection.CreateCommand(); prune.Transaction = transaction;
            prune.CommandText = "DELETE FROM quota_history WHERE retrieved_at < $cutoff;";
            prune.Parameters.AddWithValue("$cutoff", now.AddDays(-30).UtcTicks);
            await prune.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window,
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken)
    {
        var providerName = ProviderName(provider); var windowName = WindowName(window);
        if (fromInclusive > toInclusive) throw new ArgumentOutOfRangeException(nameof(toInclusive));
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfUnavailable();
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT percentage, reset_at, retrieved_at, source FROM quota_history WHERE provider = $provider AND window = $window AND retrieved_at >= $from AND retrieved_at <= $to ORDER BY retrieved_at;";
            command.Parameters.AddWithValue("$provider", providerName); command.Parameters.AddWithValue("$window", windowName);
            command.Parameters.AddWithValue("$from", fromInclusive.UtcTicks); command.Parameters.AddWithValue("$to", toInclusive.UtcTicks);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<QuotaHistorySample>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var sample = new QuotaHistorySample(provider, window, decimal.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                    new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero), new DateTimeOffset(reader.GetInt64(2), TimeSpan.Zero), ReadSource(reader.GetString(3)));
                Validate(sample); rows.Add(sample);
            }
            return rows;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposing();
        await _gate.WaitAsync(cancellationToken);
        try { ThrowIfDisposing(); _paused = true; }
        finally { _gate.Release(); }
    }

    public void ResumeAfterClear()
    {
        _gate.Wait();
        try { if (Volatile.Read(ref _disposing) == 0) _paused = false; }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) { await _disposeCompleted.Task; return; }
        await _gate.WaitAsync();
        try { _paused = true; }
        finally { _gate.Release(); _disposeCompleted.TrySetResult(); }
    }

    private void ThrowIfDisposing() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this);
    private void ThrowIfUnavailable()
    {
        ThrowIfDisposing();
        if (_paused) throw new InvalidOperationException("Quota history is paused for data clearing.");
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (version > 2) throw new InvalidOperationException("Unsupported future quota history schema.");
            command.CommandText = "PRAGMA journal_mode = WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
            const string createTable = "CREATE TABLE quota_history (provider TEXT NOT NULL CHECK(provider IN ('codex','claude')), window TEXT NOT NULL CHECK(window IN ('five-hour','weekly')), percentage TEXT NOT NULL, reset_at INTEGER NOT NULL, retrieved_at INTEGER NOT NULL, source TEXT NOT NULL CHECK(source IN ('codex-local','codex-remote','claude-local','claude-remote')), PRIMARY KEY(provider, window, retrieved_at));";
            const string finishSchema = "CREATE INDEX quota_history_retention ON quota_history(retrieved_at); PRAGMA user_version = 2; COMMIT;";
            if (version == 0)
            {
                command.CommandText = "BEGIN IMMEDIATE; " + createTable + finishSchema;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (version == 1)
            {
                // SQLite cannot alter a CHECK constraint. Replace the table atomically;
                // old source values, row identities and retention index are preserved.
                command.CommandText = "BEGIN IMMEDIATE; ALTER TABLE quota_history RENAME TO quota_history_v1; " + createTable +
                    "INSERT INTO quota_history SELECT provider, window, percentage, reset_at, retrieved_at, source FROM quota_history_v1; " +
                    "DROP TABLE quota_history_v1; " + finishSchema;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static void Validate(QuotaHistorySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        _ = ProviderName(sample.Provider); _ = WindowName(sample.Window); _ = SourceName(sample.Source);
        if (sample.Percentage is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(sample.Percentage));
        if ((sample.Provider == QuotaHistoryProvider.Codex) != (sample.Source is QuotaHistorySource.CodexLocal or QuotaHistorySource.CodexRemote))
            throw new ArgumentException("Quota history source must match its provider.", nameof(sample));
    }

    private static string ProviderName(QuotaHistoryProvider provider) => provider switch
    {
        QuotaHistoryProvider.Codex => "codex", QuotaHistoryProvider.Claude => "claude", _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    private static string WindowName(QuotaHistoryWindow window) => window switch
    {
        QuotaHistoryWindow.FiveHour => "five-hour", QuotaHistoryWindow.Weekly => "weekly", _ => throw new ArgumentOutOfRangeException(nameof(window))
    };
    private static string SourceName(QuotaHistorySource source) => source switch
    {
        QuotaHistorySource.CodexLocal => "codex-local", QuotaHistorySource.CodexRemote => "codex-remote", QuotaHistorySource.ClaudeLocal => "claude-local", QuotaHistorySource.ClaudeRemote => "claude-remote", _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
    private static QuotaHistorySource ReadSource(string source) => source switch
    {
        "codex-local" => QuotaHistorySource.CodexLocal, "codex-remote" => QuotaHistorySource.CodexRemote, "claude-local" => QuotaHistorySource.ClaudeLocal, "claude-remote" => QuotaHistorySource.ClaudeRemote, _ => throw new InvalidDataException("Invalid quota history source.")
    };
}
