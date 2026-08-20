using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

public enum UsageEventWriteState { Inserted = 1, Updated = 2, NoChange = 3, Stale = 4, Collision = 5 }
public sealed record UsageEventWriteResult(UsageEventIdentity Identity, UsageEventWriteState State);
public sealed record UsageSourceCheckpoint
{
    public UsageSourceCheckpoint(UsageSourceIdentity sourceIdentity, UsageTool tool, int sourceSchemaVersion, long cursor, string state)
    {
        ArgumentNullException.ThrowIfNull(sourceIdentity);
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(tool) || sourceSchemaVersion < 1 || cursor < 0 || state.Length != 64 || state.Any(character => character is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new ArgumentException("Checkpoint values are invalid.");
        SourceIdentity = sourceIdentity; Tool = tool; SourceSchemaVersion = sourceSchemaVersion; Cursor = cursor; State = state;
    }
    public UsageSourceIdentity SourceIdentity { get; }
    public UsageTool Tool { get; }
    public int SourceSchemaVersion { get; }
    public long Cursor { get; }
    public string State { get; }
}
public sealed class UsageCheckpointConflictException() : InvalidOperationException("The expected usage checkpoint is stale or belongs to another source state.");

public sealed class SqliteUsageEventLedger : IAsyncDisposable
{
    private const int BusyTimeoutSeconds = 5;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TaskCompletionSource _disposeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposing;

    public SqliteUsageEventLedger(string databasePath) => _connectionString = new SqliteConnectionStringBuilder
    { DataSource = databasePath, ForeignKeys = true, Pooling = false, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = BusyTimeoutSeconds }.ToString();

    public async ValueTask UpsertSourceAsync(UsageSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await Locked(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO usage_source VALUES($key,$tool,$identityVersion,$capabilityVersion,$first,$last,$state,$warnings)
                ON CONFLICT(source_key) DO UPDATE SET identity_version=max(identity_version,excluded.identity_version), capability_version=max(capability_version,excluded.capability_version),
                first_seen_at_ms=min(first_seen_at_ms,excluded.first_seen_at_ms), last_seen_at_ms=max(last_seen_at_ms,excluded.last_seen_at_ms),
                state=CASE WHEN excluded.last_seen_at_ms>last_seen_at_ms THEN excluded.state WHEN excluded.last_seen_at_ms=last_seen_at_ms THEN max(state,excluded.state) ELSE state END,
                warning_flags=warning_flags|excluded.warning_flags
                WHERE tool=excluded.tool;
                """;
            BindSource(command, source);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0) throw new InvalidOperationException("A source identity cannot change tools.");
        }, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<UsageEventWriteResult>> UpsertBatchAsync(IReadOnlyList<NormalizedUsageEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        return await Locked(async connection =>
        {
            await using var transaction = connection.BeginTransaction(deferred: false);
            var results = new List<UsageEventWriteResult>(events.Count);
            foreach (var item in events)
            {
                var result = await UpsertEvent(connection, transaction, item, cancellationToken);
                results.Add(result);
                if (result.State == UsageEventWriteState.Collision) { await transaction.RollbackAsync(cancellationToken); return results; }
            }
            await transaction.CommitAsync(cancellationToken); return results;
        }, cancellationToken);
    }

    public ValueTask<UsageSourceCheckpoint?> LoadCheckpointAsync(UsageSourceIdentity sourceIdentity, UsageTool tool, CancellationToken cancellationToken = default)
        => Locked(connection => ReadCheckpoint(connection, null, sourceIdentity, tool, cancellationToken), cancellationToken);

    public ValueTask<IReadOnlyList<UsageEventWriteResult>> CommitBatchAsync(UsageSourceCheckpoint? expected, UsageSourceCheckpoint next,
        IReadOnlyList<NormalizedUsageEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next); ArgumentNullException.ThrowIfNull(events);
        if (expected is not null && (!expected.SourceIdentity.Equals(next.SourceIdentity) || expected.Tool != next.Tool)
            || events.Any(item => !item.SourceIdentity.Equals(next.SourceIdentity) || item.Tool != next.Tool)) throw new ArgumentException("Checkpoint and events must have one source.");
        return Locked(async connection =>
        {
            await using var transaction = connection.BeginTransaction(deferred: false);
            var current = await ReadCheckpoint(connection, transaction, next.SourceIdentity, next.Tool, cancellationToken);
            if (current != expected) throw new UsageCheckpointConflictException();
            var results = new List<UsageEventWriteResult>(events.Count);
            foreach (var item in events)
            {
                var result = await UpsertEvent(connection, transaction, item, cancellationToken); results.Add(result);
                if (result.State == UsageEventWriteState.Collision) { await transaction.RollbackAsync(cancellationToken); return results; }
            }
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO usage_checkpoint VALUES($source,$tool,$version,$cursor,$state) ON CONFLICT(source_key,tool) DO UPDATE SET source_schema_version=$version,cursor=$cursor,state=$state;";
            command.Parameters.AddWithValue("$source", next.SourceIdentity.ToArray()); command.Parameters.AddWithValue("$tool", (int)next.Tool);
            command.Parameters.AddWithValue("$version", next.SourceSchemaVersion); command.Parameters.AddWithValue("$cursor", next.Cursor); command.Parameters.AddWithValue("$state", next.State);
            await command.ExecuteNonQueryAsync(cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            await transaction.CommitAsync(CancellationToken.None); return (IReadOnlyList<UsageEventWriteResult>)results;
        }, cancellationToken);
    }

    public ValueTask<long> CountAsync(CancellationToken cancellationToken = default) => Locked(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM usage_event;";
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }, cancellationToken);

    public ValueTask<bool> HasSourceHistoryAsync(CancellationToken cancellationToken = default) => Locked(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT EXISTS(SELECT 1 FROM usage_source LIMIT 1);";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0;
    }, cancellationToken);

    public ValueTask<NormalizedUsageEvent?> GetAsync(UsageEventIdentity identity, CancellationToken cancellationToken = default) => Locked(async connection =>
    {
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM usage_event WHERE event_id=$id;"; command.Parameters.AddWithValue("$id", identity.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); return await reader.ReadAsync(cancellationToken) ? ReadEvent(reader) : null;
    }, cancellationToken);

    internal ValueTask<IReadOnlyList<UsageProjectionInput>> ReadProjectionInputsAsync(CancellationToken cancellationToken) => Locked(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT occurred_at_ms,tool,model,input_uncached,cache_read,cache_write,output_visible,output_reasoning FROM usage_event ORDER BY occurred_at_ms,tool,model;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var inputs = new List<UsageProjectionInput>();
        while (await reader.ReadAsync(cancellationToken)) inputs.Add(new(FromMilliseconds(reader.GetInt64(0)), (UsageTool)reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7)));
        return (IReadOnlyList<UsageProjectionInput>)inputs;
    }, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposing, 1) != 0) { await _disposeCompleted.Task; return; }
        await _lock.WaitAsync();
        try { }
        finally { _lock.Release(); _disposeCompleted.TrySetResult(); }
    }

    private async ValueTask<T> Locked<T>(Func<SqliteConnection, Task<T>> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this); await _lock.WaitAsync(token);
        try { ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposing) != 0, this); await using var connection = await Open(token); return await action(connection); }
        finally { _lock.Release(); }
    }
    private async ValueTask Locked(Func<SqliteConnection, Task> action, CancellationToken token)
        => await Locked(async connection => { await action(connection); return 0; }, token);

    private async Task<SqliteConnection> Open(CancellationToken token)
    {
        var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(token);
        try
        {
            connection.CreateFunction<string?, bool>("aibar_metadata_valid", MetadataValid, isDeterministic: true);
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA busy_timeout={BusyTimeoutSeconds * 1000};"; await command.ExecuteNonQueryAsync(token);
            command.CommandText = "SELECT version FROM usage_schema_meta WHERE component='usage_ledger' AND EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='usage_schema_meta');";
            int version;
            try { version = Convert.ToInt32(await command.ExecuteScalarAsync(token) ?? 0); }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 1) { version = 0; }
            if (version > 2) throw new NotSupportedException("Unsupported future usage ledger schema.");
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;"; await command.ExecuteNonQueryAsync(token);
            if (version == 0) { command.CommandText = Schema; await command.ExecuteNonQueryAsync(token); }
            else if (version == 1) { command.CommandText = Migration2; await command.ExecuteNonQueryAsync(token); }
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<UsageSourceCheckpoint?> ReadCheckpoint(SqliteConnection connection, SqliteTransaction? transaction,
        UsageSourceIdentity sourceIdentity, UsageTool tool, CancellationToken token)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT source_schema_version,cursor,state FROM usage_checkpoint WHERE source_key=$source AND tool=$tool;";
        command.Parameters.AddWithValue("$source", sourceIdentity.ToArray()); command.Parameters.AddWithValue("$tool", (int)tool);
        await using var reader = await command.ExecuteReaderAsync(token);
        return await reader.ReadAsync(token) ? new(sourceIdentity, tool, reader.GetInt32(0), reader.GetInt64(1), reader.GetString(2)) : null;
    }

    private static async Task<UsageEventWriteResult> UpsertEvent(SqliteConnection connection, SqliteTransaction transaction, NormalizedUsageEvent item, CancellationToken token)
    {
        await using var existing = connection.CreateCommand(); existing.Transaction = transaction;
        existing.CommandText = "SELECT * FROM usage_event WHERE event_id=$id OR (tool=$tool AND source_event_key=$sourceEvent);";
        existing.Parameters.AddWithValue("$id", item.EventIdentity.ToArray()); existing.Parameters.AddWithValue("$tool", (int)item.Tool); existing.Parameters.AddWithValue("$sourceEvent", item.SourceEventIdentity.ToArray());
        NormalizedUsageEvent? prior; await using (var reader = await existing.ExecuteReaderAsync(token)) prior = await reader.ReadAsync(token) ? ReadEvent(reader) : null;
        if (prior is not null)
        {
            if (!prior.EventIdentity.Equals(item.EventIdentity) || prior.Tool != item.Tool || !prior.SourceIdentity.Equals(item.SourceIdentity) || !prior.SourceEventIdentity.Equals(item.SourceEventIdentity)) return new(item.EventIdentity, UsageEventWriteState.Collision);
            if (prior == item) return new(item.EventIdentity, UsageEventWriteState.NoChange);
            if (item.SourceUpdatedAt == prior.SourceUpdatedAt) return new(item.EventIdentity, UsageEventWriteState.Collision);
            if (item.SourceUpdatedAt is null || prior.SourceUpdatedAt is not null && item.SourceUpdatedAt < prior.SourceUpdatedAt) return new(item.EventIdentity, UsageEventWriteState.Stale);
        }
        await using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = prior is null ? InsertSql : UpdateSql; BindEvent(command, item);
        try { await command.ExecuteNonQueryAsync(token); }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 19) { throw new InvalidOperationException("Event source is missing or belongs to another tool.", exception); }
        return new(item.EventIdentity, prior is null ? UsageEventWriteState.Inserted : UsageEventWriteState.Updated);
    }

    private static void BindSource(SqliteCommand c, UsageSource s)
    {
        c.Parameters.AddWithValue("$key", s.Identity.ToArray()); c.Parameters.AddWithValue("$tool", (int)s.Tool); c.Parameters.AddWithValue("$identityVersion", s.IdentityVersion); c.Parameters.AddWithValue("$capabilityVersion", s.CapabilityVersion);
        c.Parameters.AddWithValue("$first", Milliseconds(s.FirstSeenAt)); c.Parameters.AddWithValue("$last", Milliseconds(s.LastSeenAt)); c.Parameters.AddWithValue("$state", (int)s.State); c.Parameters.AddWithValue("$warnings", s.WarningFlags);
    }
    private static void BindEvent(SqliteCommand c, NormalizedUsageEvent e)
    {
        object Db(object? value) => value ?? DBNull.Value;
        c.Parameters.AddWithValue("$id", e.EventIdentity.ToArray()); c.Parameters.AddWithValue("$tool", (int)e.Tool); c.Parameters.AddWithValue("$source", e.SourceIdentity.ToArray()); c.Parameters.AddWithValue("$sourceEvent", e.SourceEventIdentity.ToArray());
        c.Parameters.AddWithValue("$occurred", Milliseconds(e.OccurredAt)); c.Parameters.AddWithValue("$observed", Milliseconds(e.ObservedAt)); c.Parameters.AddWithValue("$updated", Db(e.SourceUpdatedAt is { } u ? Milliseconds(u) : null));
        c.Parameters.AddWithValue("$provider", Db(e.Metadata.Provider)); c.Parameters.AddWithValue("$api", Db(e.Metadata.Api)); c.Parameters.AddWithValue("$model", Db(e.Metadata.Model)); c.Parameters.AddWithValue("$kind", (int)e.Kind); c.Parameters.AddWithValue("$purpose", (int)e.Purpose); c.Parameters.AddWithValue("$outcome", (int)e.Outcome); c.Parameters.AddWithValue("$finish", (int)e.FinishReason); c.Parameters.AddWithValue("$fidelity", (int)e.Fidelity); c.Parameters.AddWithValue("$aggregate", e.IsAggregate);
        c.Parameters.AddWithValue("$input", e.Tokens.InputUncached); c.Parameters.AddWithValue("$read", e.Tokens.CacheRead); c.Parameters.AddWithValue("$write", e.Tokens.CacheWrite); c.Parameters.AddWithValue("$visible", e.Tokens.OutputVisible); c.Parameters.AddWithValue("$reasoning", e.Tokens.OutputReasoning); c.Parameters.AddWithValue("$total", Db(e.Tokens.SourceReportedTotal)); c.Parameters.AddWithValue("$cost", Db(e.Cost.Nanos)); c.Parameters.AddWithValue("$currency", Db(e.Cost.Currency)); c.Parameters.AddWithValue("$version", e.PayloadVersion);
    }

    private static NormalizedUsageEvent ReadEvent(SqliteDataReader r) => new(new((byte[])r[0]), new((byte[])r[2]), new((byte[])r[3]), (UsageTool)r.GetInt32(1), (UsageEventKind)r.GetInt32(10), (UsagePurpose)r.GetInt32(11), (UsageOutcome)r.GetInt32(12), (UsageFidelity)r.GetInt32(14), (UsageFinishReason)r.GetInt32(13),
        FromMilliseconds(r.GetInt64(4)), FromMilliseconds(r.GetInt64(5)), r.IsDBNull(6) ? null : FromMilliseconds(r.GetInt64(6)), new(r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9)),
        new(r.GetInt64(16), r.GetInt64(17), r.GetInt64(18), r.GetInt64(19), r.GetInt64(20), r.IsDBNull(21) ? null : r.GetInt64(21)), new(r.IsDBNull(22) ? null : r.GetInt64(22), r.IsDBNull(23) ? null : r.GetString(23)), r.GetInt32(24), r.GetBoolean(15));
    private static long Milliseconds(DateTimeOffset value)
    {
        var milliseconds = checked(value.ToUnixTimeMilliseconds());
        if (FromMilliseconds(milliseconds) != value) throw new ArgumentException("Usage timestamps must have millisecond precision.", nameof(value));
        return milliseconds;
    }
    private static DateTimeOffset FromMilliseconds(long value) => DateTimeOffset.FromUnixTimeMilliseconds(value);

    private const string InsertSql = "INSERT INTO usage_event VALUES($id,$tool,$source,$sourceEvent,$occurred,$observed,$updated,$provider,$api,$model,$kind,$purpose,$outcome,$finish,$fidelity,$aggregate,$input,$read,$write,$visible,$reasoning,$total,$cost,$currency,$version);";
    private const string UpdateSql = "UPDATE usage_event SET occurred_at_ms=$occurred,observed_at_ms=$observed,source_updated_at_ms=$updated,provider=$provider,api=$api,model=$model,kind=$kind,purpose=$purpose,outcome=$outcome,finish_reason=$finish,fidelity=$fidelity,is_aggregate=$aggregate,input_uncached=$input,cache_read=$read,cache_write=$write,output_visible=$visible,output_reasoning=$reasoning,source_total=$total,cost_nanos=$cost,currency=$currency,payload_version=$version WHERE event_id=$id;";
    private static bool MetadataValid(string? value)
    {
        try { return value is not null && new UsageDisplayMetadata(value, null, null).Provider is not null; }
        catch (ArgumentException) { return false; }
    }
    private static string MetadataCheck(string column) => $"{column} IS NULL OR (typeof({column})='text' AND aibar_metadata_valid({column}) AND "
        + string.Join(" AND ", Enumerable.Range(0, 32).Concat(Enumerable.Range(127, 33)).Select(code => $"instr({column},char({code}))=0")) + ")";

    private static readonly string Schema = $"""
        BEGIN IMMEDIATE;
        CREATE TABLE IF NOT EXISTS usage_schema_meta(component TEXT PRIMARY KEY,version INTEGER NOT NULL CHECK(typeof(version)='integer' AND version BETWEEN 1 AND 2147483647)) STRICT;
        INSERT INTO usage_schema_meta VALUES('usage_ledger',2) ON CONFLICT(component) DO NOTHING;
        CREATE TABLE usage_source(source_key BLOB PRIMARY KEY CHECK(typeof(source_key)='blob' AND length(source_key)=32),tool INTEGER NOT NULL CHECK(typeof(tool)='integer' AND tool BETWEEN 1 AND 2),identity_version INTEGER NOT NULL CHECK(typeof(identity_version)='integer' AND identity_version BETWEEN 1 AND 2147483647),capability_version INTEGER NOT NULL CHECK(typeof(capability_version)='integer' AND capability_version BETWEEN 1 AND 2147483647),first_seen_at_ms INTEGER NOT NULL CHECK(typeof(first_seen_at_ms)='integer' AND first_seen_at_ms BETWEEN 0 AND 253402300799999),last_seen_at_ms INTEGER NOT NULL CHECK(typeof(last_seen_at_ms)='integer' AND last_seen_at_ms BETWEEN first_seen_at_ms AND 253402300799999),state INTEGER NOT NULL CHECK(typeof(state)='integer' AND state BETWEEN 1 AND 3),warning_flags INTEGER NOT NULL CHECK(typeof(warning_flags)='integer' AND warning_flags BETWEEN 0 AND 2147483647),UNIQUE(source_key,tool)) STRICT;
        CREATE TABLE usage_event(event_id BLOB PRIMARY KEY CHECK(typeof(event_id)='blob' AND length(event_id)=32),tool INTEGER NOT NULL CHECK(typeof(tool)='integer' AND tool BETWEEN 1 AND 2),source_key BLOB NOT NULL CHECK(typeof(source_key)='blob' AND length(source_key)=32),source_event_key BLOB NOT NULL CHECK(typeof(source_event_key)='blob' AND length(source_event_key)=32),occurred_at_ms INTEGER NOT NULL CHECK(typeof(occurred_at_ms)='integer' AND occurred_at_ms BETWEEN 0 AND 253402300799999),observed_at_ms INTEGER NOT NULL CHECK(typeof(observed_at_ms)='integer' AND observed_at_ms BETWEEN occurred_at_ms AND 253402300799999),source_updated_at_ms INTEGER CHECK(source_updated_at_ms IS NULL OR (typeof(source_updated_at_ms)='integer' AND source_updated_at_ms BETWEEN occurred_at_ms AND 253402300799999)),provider TEXT CHECK({MetadataCheck("provider")}),api TEXT CHECK({MetadataCheck("api")}),model TEXT CHECK({MetadataCheck("model")}),kind INTEGER NOT NULL CHECK(typeof(kind)='integer' AND kind BETWEEN 1 AND 6),purpose INTEGER NOT NULL CHECK(typeof(purpose)='integer' AND purpose BETWEEN 1 AND 6),outcome INTEGER NOT NULL CHECK(typeof(outcome)='integer' AND outcome BETWEEN 1 AND 5),finish_reason INTEGER NOT NULL CHECK(typeof(finish_reason)='integer' AND finish_reason BETWEEN 1 AND 6),fidelity INTEGER NOT NULL CHECK(typeof(fidelity)='integer' AND fidelity BETWEEN 1 AND 3),is_aggregate INTEGER NOT NULL CHECK(typeof(is_aggregate)='integer' AND is_aggregate IN(0,1)),input_uncached INTEGER NOT NULL CHECK(typeof(input_uncached)='integer' AND input_uncached>=0),cache_read INTEGER NOT NULL CHECK(typeof(cache_read)='integer' AND cache_read>=0),cache_write INTEGER NOT NULL CHECK(typeof(cache_write)='integer' AND cache_write>=0),output_visible INTEGER NOT NULL CHECK(typeof(output_visible)='integer' AND output_visible>=0),output_reasoning INTEGER NOT NULL CHECK(typeof(output_reasoning)='integer' AND output_reasoning>=0),source_total INTEGER CHECK(source_total IS NULL OR (typeof(source_total)='integer' AND source_total>=0)),cost_nanos INTEGER CHECK(cost_nanos IS NULL OR (typeof(cost_nanos)='integer' AND cost_nanos>=0)),currency TEXT CHECK(currency IS NULL OR (typeof(currency)='text' AND currency GLOB '[A-Z][A-Z][A-Z]')),payload_version INTEGER NOT NULL CHECK(typeof(payload_version)='integer' AND payload_version BETWEEN 1 AND 2147483647),CHECK((cost_nanos IS NULL)=(currency IS NULL)),CHECK(input_uncached<=9223372036854775807-cache_read AND input_uncached+cache_read<=9223372036854775807-cache_write),CHECK(output_visible<=9223372036854775807-output_reasoning),CHECK(input_uncached+cache_read+cache_write<=9223372036854775807-output_visible-output_reasoning),FOREIGN KEY(source_key,tool) REFERENCES usage_source(source_key,tool),UNIQUE(tool,source_event_key)) STRICT;
        CREATE INDEX usage_event_source_idx ON usage_event(source_key);
        CREATE TABLE usage_checkpoint(source_key BLOB NOT NULL CHECK(typeof(source_key)='blob' AND length(source_key)=32),tool INTEGER NOT NULL CHECK(typeof(tool)='integer' AND tool BETWEEN 1 AND 2),source_schema_version INTEGER NOT NULL CHECK(typeof(source_schema_version)='integer' AND source_schema_version BETWEEN 1 AND 2147483647),cursor INTEGER NOT NULL CHECK(typeof(cursor)='integer' AND cursor>=0),state TEXT NOT NULL CHECK(typeof(state)='text' AND length(state)=64 AND state NOT GLOB '*[^0-9A-F]*'),PRIMARY KEY(source_key,tool),FOREIGN KEY(source_key,tool) REFERENCES usage_source(source_key,tool)) STRICT;
        COMMIT;
        """;
    private const string Migration2 = "BEGIN IMMEDIATE; CREATE TABLE IF NOT EXISTS usage_checkpoint(source_key BLOB NOT NULL CHECK(typeof(source_key)='blob' AND length(source_key)=32),tool INTEGER NOT NULL CHECK(typeof(tool)='integer' AND tool BETWEEN 1 AND 2),source_schema_version INTEGER NOT NULL CHECK(typeof(source_schema_version)='integer' AND source_schema_version BETWEEN 1 AND 2147483647),cursor INTEGER NOT NULL CHECK(typeof(cursor)='integer' AND cursor>=0),state TEXT NOT NULL CHECK(typeof(state)='text' AND length(state)=64 AND state NOT GLOB '*[^0-9A-F]*'),PRIMARY KEY(source_key,tool),FOREIGN KEY(source_key,tool) REFERENCES usage_source(source_key,tool)) STRICT; UPDATE usage_schema_meta SET version=2 WHERE component='usage_ledger'; COMMIT;";
}
