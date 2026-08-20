using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Application;

public enum OpenCodeUsageReadWarning
{
    Unavailable = 1,
    UnsupportedSchema = 2,
    MalformedRecord = 3,
    Truncated = 4,
    RebuildRequired = 5
}

public sealed record OpenCodeUsageSourceAuthorization(UsageSourceIdentity SourceIdentity, int SourceSchemaVersion);

public sealed record OpenCodeUsageReadResult(
    IReadOnlyList<NormalizedUsageEvent> Events,
    int RecordsRead,
    int RecordsIgnored,
    int RecordsMalformed,
    IReadOnlyList<OpenCodeUsageReadWarning> Warnings,
    UsageSourceCheckpoint? NextCheckpoint = null);

public sealed class OpenCodeSqliteUsageSourceAdapter
{
    public const int MaximumRecords = 10_000;
    public const int SupportedSourceSchemaVersion = 1;
    private const int MaximumJsonCharacters = 256 * 1024;
    private const int PayloadVersion = 1;
    private readonly string _connectionString;
    private readonly OpenCodeUsageSourceAuthorization _authorization;
    private readonly Action? _afterOpen;

    public OpenCodeSqliteUsageSourceAdapter(string databasePath, OpenCodeUsageSourceAuthorization authorization)
        : this(databasePath, authorization, null) { }
    internal OpenCodeSqliteUsageSourceAdapter(string databasePath, OpenCodeUsageSourceAuthorization authorization, Action? afterOpen)
    {
        if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("A database path is required.", nameof(databasePath));
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(authorization.SourceIdentity);
        if (authorization.SourceSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(authorization));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        _authorization = authorization;
        _afterOpen = afterOpen;
    }

    public async ValueTask<OpenCodeUsageReadResult> ReadAsync(int maximumRecords, CancellationToken cancellationToken = default)
        => await PrepareAsync(null, maximumRecords, cancellationToken);

    public async ValueTask<OpenCodeUsageReadResult> PrepareAsync(UsageSourceCheckpoint? checkpoint, int maximumRecords, CancellationToken cancellationToken = default)
    {
        if (maximumRecords is < 1 or > MaximumRecords) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        if (checkpoint is not null && (!checkpoint.SourceIdentity.Equals(_authorization.SourceIdentity)
            || checkpoint.Tool != UsageTool.OpenCode || checkpoint.SourceSchemaVersion != _authorization.SourceSchemaVersion))
            return Empty(OpenCodeUsageReadWarning.RebuildRequired);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            _afterOpen?.Invoke();
            var schemaState = _authorization.SourceSchemaVersion == SupportedSourceSchemaVersion
                ? await GetSchemaState(connection, cancellationToken) : null;
            if (schemaState is null) return Empty(OpenCodeUsageReadWarning.UnsupportedSchema);
            var cursor = checkpoint?.Cursor ?? 0;
            var anchors = await ReadAnchors(connection, cursor, cancellationToken);
            if (checkpoint is not null && (cursor > 0 && anchors.CursorId is null || checkpoint.State != CheckpointState(schemaState, anchors.FirstId, anchors.CursorId)))
                return Empty(OpenCodeUsageReadWarning.RebuildRequired);

            await using var command = connection.CreateCommand();
            command.CommandText = ProjectionSql;
            command.Parameters.AddWithValue("$limit", maximumRecords + 1L);
            command.Parameters.AddWithValue("$maximumJsonCharacters", MaximumJsonCharacters);
            command.Parameters.AddWithValue("$cursor", cursor);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var events = new List<NormalizedUsageEvent>(maximumRecords);
            var read = 0;
            var ignored = 0;
            var malformed = 0;
            var truncated = false;
            var firstId = anchors.FirstId;
            var lastId = anchors.CursorId;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (read == maximumRecords) { truncated = true; break; }
                read++;
                cursor = reader.GetInt64(0); lastId = reader.GetValue(1) as string;
                firstId ??= lastId;
                if (reader.GetInt64(6) == 0) { malformed++; continue; }
                if (!StringComparer.Ordinal.Equals(reader.GetValue(7) as string, "step-finish")) { ignored++; continue; }
                if (!TryMap(reader, out var item)) { malformed++; continue; }
                events.Add(item);
            }

            var warnings = new List<OpenCodeUsageReadWarning>(2);
            if (malformed > 0) warnings.Add(OpenCodeUsageReadWarning.MalformedRecord);
            if (truncated) warnings.Add(OpenCodeUsageReadWarning.Truncated);
            var nextState = CheckpointState(schemaState, firstId, lastId);
            var next = new UsageSourceCheckpoint(_authorization.SourceIdentity, UsageTool.OpenCode, _authorization.SourceSchemaVersion, cursor, nextState);
            return new(events.AsReadOnly(), read, ignored, malformed, warnings.AsReadOnly(), next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (SqliteException) { return Empty(OpenCodeUsageReadWarning.Unavailable); }
        catch (IOException) { return Empty(OpenCodeUsageReadWarning.Unavailable); }
        catch (UnauthorizedAccessException) { return Empty(OpenCodeUsageReadWarning.Unavailable); }
    }

    private bool TryMap(SqliteDataReader reader, out NormalizedUsageEvent item)
    {
        item = null!;
        if (reader.GetValue(1) is not string partId || string.IsNullOrEmpty(partId) || partId.Length > 256
            || !TryMilliseconds(reader, 2, out var occurredAt)
            || !TryMilliseconds(reader, 3, out var updatedAt)
            || updatedAt < occurredAt
            || reader.GetInt64(4) == 0
            || !StringComparer.Ordinal.Equals(reader.GetValue(5) as string, "assistant")
            || reader.GetValue(8) is not string reason || string.IsNullOrEmpty(reason)
            || !TryNonNegativeInteger(reader, 9, nullable: true, out var total)
            || !TryNonNegativeInteger(reader, 10, nullable: false, out var input)
            || !TryNonNegativeInteger(reader, 11, nullable: false, out var output)
            || !TryNonNegativeInteger(reader, 12, nullable: false, out var reasoning)
            || !TryNonNegativeInteger(reader, 13, nullable: false, out var cacheRead)
            || !TryNonNegativeInteger(reader, 14, nullable: false, out var cacheWrite)
            || !TryBoolean(reader, 17, out var summary)) return false;

        UsageDisplayMetadata metadata;
        UsageTokens tokens;
        try
        {
            if (reader.GetValue(15) is not string provider || reader.GetValue(16) is not string model) return false;
            metadata = new(provider, null, model);
            if (metadata.Provider is null || metadata.Model is null) return false;
            tokens = UsageTokenNormalization.OpenCode(input!.Value, cacheRead!.Value, cacheWrite!.Value, output!.Value, reasoning!.Value, total).Tokens;
        }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }

        var sourceEventIdentity = new UsageSourceEventIdentity(Hash("opencode-source-event-v1", _authorization.SourceIdentity, partId));
        item = new(
            new UsageEventIdentity(Hash("opencode-event-v1", _authorization.SourceIdentity, partId)), _authorization.SourceIdentity, sourceEventIdentity,
            UsageTool.OpenCode, UsageEventKind.AssistantStep, summary ? UsagePurpose.Compaction : UsagePurpose.Primary,
            UsageOutcome.Success, UsageFidelity.SuccessfulSettledStep, FinishReason(reason), occurredAt, updatedAt, updatedAt,
            metadata, tokens, new UsageCost(null, null), PayloadVersion, false);
        return true;
    }

    private static async Task<string?> GetSchemaState(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT type,name,sql FROM sqlite_master WHERE name IN('message','part') ORDER BY name;";
        await using var objects = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new StringBuilder();
        while (await objects.ReadAsync(cancellationToken)) if (objects.GetString(0) == "table") { tables.Add(objects.GetString(1)); definitions.Append(objects.GetString(2)); }
        if (!tables.SetEquals(["message", "part"])) return null;

        return await HasColumns(connection, "message", ["id", "time_created", "time_updated", "data"], cancellationToken)
            && await HasColumns(connection, "part", ["id", "message_id", "time_created", "time_updated", "data"], cancellationToken)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definitions.ToString()))) : null;
    }

    private static async Task<(string? FirstId, string? CursorId)> ReadAnchors(SqliteConnection connection, long cursor, CancellationToken token)
    {
        if (cursor == 0) return (null, null);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT id FROM part ORDER BY rowid LIMIT 1),(SELECT id FROM part WHERE rowid=$cursor);";
        command.Parameters.AddWithValue("$cursor", cursor);
        await using var reader = await command.ExecuteReaderAsync(token); await reader.ReadAsync(token);
        return (reader.GetValue(0) as string, reader.GetValue(1) as string);
    }

    private string CheckpointState(string schemaState, string? firstId, string? cursorId)
        => Convert.ToHexString(Hash("opencode-checkpoint-v1", _authorization.SourceIdentity,
            $"{schemaState.Length}:{schemaState}{firstId?.Length ?? -1}:{firstId}{cursorId?.Length ?? -1}:{cursorId}"));

    private static async Task<bool> HasColumns(SqliteConnection connection, string table, string[] required, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand(); command.CommandText = $"SELECT name FROM pragma_table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(0));
        return required.All(columns.Contains);
    }

    private static bool TryMilliseconds(SqliteDataReader reader, int index, out DateTimeOffset value)
    {
        value = default;
        if (reader.GetValue(index) is not long milliseconds || milliseconds is < 0 or > 253402300799999) return false;
        value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return true;
    }

    private static bool TryNonNegativeInteger(SqliteDataReader reader, int index, bool nullable, out long? value)
    {
        value = null;
        if (reader.IsDBNull(index)) return nullable;
        if (reader.GetValue(index) is not long parsed || parsed < 0) return false;
        value = parsed;
        return true;
    }

    private static bool TryBoolean(SqliteDataReader reader, int index, out bool value)
    {
        value = false;
        if (reader.IsDBNull(index)) return true;
        if (reader.GetValue(index) is not long parsed || parsed is not (0 or 1)) return false;
        value = parsed == 1;
        return true;
    }

    private static UsageFinishReason FinishReason(string value) => value switch
    {
        "stop" => UsageFinishReason.Stop,
        "length" => UsageFinishReason.Length,
        "tool-calls" or "tool-call" => UsageFinishReason.ToolCall,
        "error" => UsageFinishReason.Error,
        _ => UsageFinishReason.Other
    };

    private static byte[] Hash(string domain, UsageSourceIdentity sourceIdentity, string sourceEventId)
    {
        var domainBytes = Encoding.UTF8.GetBytes(domain);
        var sourceBytes = sourceIdentity.ToArray();
        var eventBytes = Encoding.UTF8.GetBytes(sourceEventId);
        var input = new byte[12 + domainBytes.Length + sourceBytes.Length + eventBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(input, domainBytes.Length); domainBytes.CopyTo(input, 4);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4 + domainBytes.Length), sourceBytes.Length); sourceBytes.CopyTo(input, 8 + domainBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(8 + domainBytes.Length + sourceBytes.Length), eventBytes.Length); eventBytes.CopyTo(input, 12 + domainBytes.Length + sourceBytes.Length);
        return SHA256.HashData(input);
    }

    private static OpenCodeUsageReadResult Empty(OpenCodeUsageReadWarning warning) => new([], 0, 0, 0, [warning]);

    private const string ProjectionSql = """
        WITH bounded AS (
            SELECT p.rowid,p.id,p.time_created,p.time_updated,
            CASE WHEN typeof(m.data)='text' AND length(m.data)<=$maximumJsonCharacters THEN CASE WHEN json_valid(m.data) THEN m.data END END message_data,
            CASE WHEN typeof(p.data)='text' AND length(p.data)<=$maximumJsonCharacters THEN CASE WHEN json_valid(p.data) THEN p.data END END part_data
            FROM part p LEFT JOIN message m ON m.id=p.message_id WHERE p.rowid>$cursor ORDER BY p.rowid LIMIT $limit
        )
        SELECT rowid,id,time_created,time_updated,message_data IS NOT NULL,json_extract(message_data,'$.role'),
        part_data IS NOT NULL,json_extract(part_data,'$.type'),json_extract(part_data,'$.reason'),json_extract(part_data,'$.tokens.total'),
        json_extract(part_data,'$.tokens.input'),json_extract(part_data,'$.tokens.output'),json_extract(part_data,'$.tokens.reasoning'),
        json_extract(part_data,'$.tokens.cache.read'),json_extract(part_data,'$.tokens.cache.write'),json_extract(message_data,'$.providerID'),
        json_extract(message_data,'$.modelID'),json_extract(message_data,'$.summary') FROM bounded ORDER BY rowid;
        """;
}
