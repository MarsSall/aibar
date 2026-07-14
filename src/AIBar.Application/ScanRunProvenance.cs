using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

public sealed record ScanRunHandoff(DateTimeOffset Timestamp, string Model, long InputTokens, long CachedInputTokens, long OutputTokens);
public sealed record ScanRunProvenance(DateTimeOffset StartedAt, DateTimeOffset CompletedAt, int FilesDiscovered, int FilesRead, int FilesSkipped, int FilesDeferred, IReadOnlyList<string> WarningCodes, bool WasCancelled, ScanCoverageState Coverage, IReadOnlyList<ScanRunHandoff> Handoff);

public interface IScanRunStore
{
    ValueTask<ScanRunProvenance?> LoadAsync(string fingerprint, CancellationToken cancellationToken);
    ValueTask CommitAsync(string fingerprint, ScanRunProvenance scanRun, CancellationToken cancellationToken);
    ValueTask RecordCancelledAsync(string fingerprint, ScanRunProvenance scanRun);
}

public sealed class InMemoryScanRunStore : IScanRunStore
{
    private readonly Dictionary<string, ScanRunProvenance> _runs = new(StringComparer.Ordinal);
    public int CommitCount { get; private set; }
    public ScanRunProvenance? LastCommit { get; private set; }
    public ValueTask<ScanRunProvenance?> LoadAsync(string fingerprint, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult(_runs.TryGetValue(fingerprint, out var run) ? run : null); }
    public ValueTask CommitAsync(string fingerprint, ScanRunProvenance scanRun, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (_runs.ContainsKey(fingerprint)) return ValueTask.CompletedTask; _runs[fingerprint] = scanRun; LastCommit = scanRun; CommitCount++; return ValueTask.CompletedTask; }
    public ValueTask RecordCancelledAsync(string fingerprint, ScanRunProvenance scanRun) { LastCommit = scanRun; return ValueTask.CompletedTask; }
}

public sealed class SqliteScanRunStore : IScanRunStore, IAsyncDisposable
{
    private readonly string _connectionString;
    public SqliteScanRunStore(string databasePath) => _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true, Pooling = false }.ToString();
    public async ValueTask<ScanRunProvenance?> LoadAsync(string fingerprint, CancellationToken cancellationToken) => await LoadAsync(fingerprint, false, cancellationToken);
    public async ValueTask<ScanRunProvenance?> LoadLastAsync(CancellationToken cancellationToken) => await LoadAsync(null, true, cancellationToken);
    public async ValueTask CommitAsync(string fingerprint, ScanRunProvenance scanRun, CancellationToken cancellationToken) => await InsertAsync(fingerprint, scanRun, cancellationToken);
    public ValueTask RecordCancelledAsync(string fingerprint, ScanRunProvenance scanRun) => InsertAsync(fingerprint, scanRun, CancellationToken.None);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async ValueTask InsertAsync(string fingerprint, ScanRunProvenance run, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        var values = "(fingerprint, started_at, completed_at, files_discovered, files_read, files_skipped, files_deferred, warnings, was_cancelled, coverage, handoff) VALUES ($fingerprint, $started, $completed, $discovered, $read, $skipped, $deferred, $warnings, $cancelled, $coverage, $handoff)";
        command.CommandText = run.WasCancelled
            ? $"INSERT INTO scan_run {values} ON CONFLICT(fingerprint) DO UPDATE SET id=(SELECT MAX(id) + 1 FROM scan_run), started_at=excluded.started_at, completed_at=excluded.completed_at, files_discovered=excluded.files_discovered, files_read=excluded.files_read, files_skipped=excluded.files_skipped, files_deferred=excluded.files_deferred, warnings=excluded.warnings, was_cancelled=excluded.was_cancelled, coverage=excluded.coverage, handoff=excluded.handoff;"
            : $"INSERT OR IGNORE INTO scan_run {values};";
        command.Parameters.AddWithValue("$fingerprint", fingerprint + (run.WasCancelled ? ":cancelled" : "")); command.Parameters.AddWithValue("$started", run.StartedAt.ToString("O")); command.Parameters.AddWithValue("$completed", run.CompletedAt.ToString("O")); command.Parameters.AddWithValue("$discovered", run.FilesDiscovered); command.Parameters.AddWithValue("$read", run.FilesRead); command.Parameters.AddWithValue("$skipped", run.FilesSkipped); command.Parameters.AddWithValue("$deferred", run.FilesDeferred); command.Parameters.AddWithValue("$warnings", string.Join(',', run.WarningCodes)); command.Parameters.AddWithValue("$cancelled", run.WasCancelled ? 1 : 0); command.Parameters.AddWithValue("$coverage", (int)run.Coverage); command.Parameters.AddWithValue("$handoff", JsonSerializer.Serialize(run.Handoff));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async ValueTask<ScanRunProvenance?> LoadAsync(string? fingerprint, bool last, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); await using var command = connection.CreateCommand();
        command.CommandText = last
            ? "SELECT started_at, completed_at, files_discovered, files_read, files_skipped, files_deferred, warnings, was_cancelled, coverage, handoff FROM scan_run ORDER BY id DESC LIMIT 1;"
            : "SELECT started_at, completed_at, files_discovered, files_read, files_skipped, files_deferred, warnings, was_cancelled, coverage, handoff FROM scan_run WHERE fingerprint = $fingerprint AND was_cancelled = 0;";

        if (!last) command.Parameters.AddWithValue("$fingerprint", fingerprint!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(DateTimeOffset.Parse(reader.GetString(0), null, System.Globalization.DateTimeStyles.RoundtripKind), DateTimeOffset.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries), reader.GetInt32(7) != 0, (ScanCoverageState)reader.GetInt32(8), JsonSerializer.Deserialize<ScanRunHandoff[]>(reader.GetString(9)) ?? []);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; CREATE TABLE IF NOT EXISTS scan_run (id INTEGER PRIMARY KEY, fingerprint TEXT NOT NULL UNIQUE, started_at TEXT NOT NULL, completed_at TEXT NOT NULL, files_discovered INTEGER NOT NULL, files_read INTEGER NOT NULL, files_skipped INTEGER NOT NULL, files_deferred INTEGER NOT NULL, warnings TEXT NOT NULL, was_cancelled INTEGER NOT NULL, coverage INTEGER NOT NULL, handoff TEXT NOT NULL);"; await command.ExecuteNonQueryAsync(cancellationToken); return connection;
    }
}

public sealed class ScanRunRecorder(IScanRunStore store, Func<DateTimeOffset> utcNow, Action? beforeCommit = null)
{
    public async ValueTask<ScanRunProvenance> RecordAsync(SessionDiscoveryResult discovery, IReadOnlyList<SessionParseResult> scans, CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(discovery, scans);
        var warnings = discovery.Coverage.WarningCodes.Concat(scans.SelectMany(scan => scan.WarningCodes)).Where(IsSafeWarning).Distinct(StringComparer.Ordinal).OrderBy(code => code, StringComparer.Ordinal).ToArray(); var deferred = scans.Count(scan => scan.WarningCodes.Contains("session_incomplete_tail", StringComparer.Ordinal)); var coverage = warnings.Length > 0 || discovery.Coverage.FilesSkipped > 0 || deferred > 0 ? ScanCoverageState.Partial : ScanCoverageState.Complete; var started = utcNow();
        var run = new ScanRunProvenance(started, utcNow(), discovery.Coverage.FilesDiscovered, scans.Count, discovery.Coverage.FilesSkipped, deferred, warnings, false, coverage, scans.SelectMany(scan => scan.Records).Select(record => new ScanRunHandoff(record.Timestamp, record.Model, Math.Max(0, record.InputTokens), Math.Max(0, record.CachedInputTokens), Math.Max(0, record.OutputTokens))).ToArray());
        try { cancellationToken.ThrowIfCancellationRequested(); beforeCommit?.Invoke(); cancellationToken.ThrowIfCancellationRequested(); var existing = await store.LoadAsync(fingerprint, cancellationToken); if (existing is not null) return existing; await store.CommitAsync(fingerprint, run, cancellationToken); return run; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { var cancelled = run with { WasCancelled = true, Coverage = ScanCoverageState.Partial, WarningCodes = warnings.Append("session_cancelled").ToArray(), Handoff = [] }; await store.RecordCancelledAsync(fingerprint, cancelled); throw; }
    }
    private static bool IsSafeWarning(string warning) => warning.StartsWith("session_", StringComparison.Ordinal) && warning.All(character => char.IsLower(character) || character is '_' or >= '0' and <= '9');
        private static string Fingerprint(SessionDiscoveryResult discovery, IReadOnlyList<SessionParseResult> scans)
        {
            var canonical = string.Join("|", [discovery.Coverage.FilesDiscovered.ToString(System.Globalization.CultureInfo.InvariantCulture), discovery.Coverage.FilesSkipped.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Join(",", discovery.Coverage.WarningCodes.OrderBy(code => code, StringComparer.Ordinal)), string.Join(";", scans.Select(scan => $"{scan.RebuildRequired}:{string.Join(",", scan.WarningCodes.OrderBy(code => code, StringComparer.Ordinal))}:{string.Join(",", scan.Records.Select(record => $"{record.Timestamp.UtcTicks}:{record.Model}:{record.InputTokens}:{record.CachedInputTokens}:{record.OutputTokens}"))}"))]);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
}
