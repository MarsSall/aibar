using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class OpenCodeSqliteUsageSourceAdapterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-opencode-source-{Guid.NewGuid():N}.db");
    private readonly string _ledgerPath = Path.Combine(Path.GetTempPath(), $"aibar-opencode-ledger-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Maps_settled_usage_into_non_overlapping_normalized_buckets()
    {
        await CreateSchema();
        await Insert("prt_settled", Part("tool-calls", 31, 1, 4, 5, 2, 3), Message("provider-x", "model-y", summary: true), At, At.AddSeconds(1));

        var result = await Adapter().ReadAsync(10);

        var item = Assert.Single(result.Events);
        Assert.Equal((1, 0, 0), (result.RecordsRead, result.RecordsIgnored, result.RecordsMalformed)); Assert.Empty(result.Warnings);
        Assert.Equal((UsageTool.OpenCode, UsageEventKind.AssistantStep, UsagePurpose.Compaction, UsageOutcome.Success), (item.Tool, item.Kind, item.Purpose, item.Outcome));
        Assert.Equal((UsageFidelity.SuccessfulSettledStep, UsageFinishReason.ToolCall, false), (item.Fidelity, item.FinishReason, item.IsAggregate));
        Assert.Equal((At, At.AddSeconds(1), At.AddSeconds(1)), (item.OccurredAt, item.ObservedAt, item.SourceUpdatedAt));
        Assert.Equal(("provider-x", null, "model-y"), (item.Metadata.Provider, item.Metadata.Api, item.Metadata.Model));
        Assert.Equal((1, 2, 3, 4, 5, 31), (item.Tokens.InputUncached, item.Tokens.CacheRead, item.Tokens.CacheWrite, item.Tokens.OutputVisible, item.Tokens.OutputReasoning, item.Tokens.SourceReportedTotal));
        Assert.Equal((6, 9, 15), (item.Tokens.DisplayInput, item.Tokens.DisplayOutput, item.Tokens.DisplayTotal));
        Assert.Null(item.Cost.Nanos); Assert.Null(item.Cost.Currency); Assert.Equal(1, item.PayloadVersion);
    }

    [Fact]
    public async Task Replays_are_stable_while_distinct_steps_and_source_instances_remain_independent()
    {
        await CreateSchema();
        await Insert("prt_first", Part(), Message(), At, At);
        await Insert("prt_second", Part(), Message(), At.AddMilliseconds(1), At.AddMilliseconds(1));
        var adapter = Adapter();

        var first = await adapter.ReadAsync(10); var replay = await adapter.ReadAsync(10);
        Assert.Equal(first.Events, replay.Events); Assert.NotEqual(first.Events[0].EventIdentity, first.Events[1].EventIdentity);
        var otherSource = await new OpenCodeSqliteUsageSourceAdapter(_path, Authorization(8)).ReadAsync(10);
        Assert.NotEqual(first.Events[0].EventIdentity, otherSource.Events[0].EventIdentity);

        await using var ledger = new SqliteUsageEventLedger(_ledgerPath);
        await ledger.UpsertSourceAsync(new(Source(7), UsageTool.OpenCode, 1, 1, At, At.AddMilliseconds(1), UsageSourceState.Active, 0));
        Assert.All(await ledger.UpsertBatchAsync(first.Events), result => Assert.Equal(UsageEventWriteState.Inserted, result.State));
        Assert.All(await ledger.UpsertBatchAsync(replay.Events), result => Assert.Equal(UsageEventWriteState.NoChange, result.State));
        Assert.Equal(2, await ledger.CountAsync());
    }

    [Fact]
    public async Task Irrelevant_and_malformed_records_are_skipped_without_losing_valid_rows()
    {
        await CreateSchema();
        await Insert("prt_irrelevant", "{\"type\":\"text\",\"text\":\"private\"}", "not-json", At, At);
        await Insert("prt_bad_json", "{bad", Message(), At.AddMilliseconds(1), At.AddMilliseconds(1));
        await Insert("prt_bad_tokens", Part(input: -1), Message(), At.AddMilliseconds(2), At.AddMilliseconds(2));
        await Insert("prt_valid", Part(reason: "stop"), Message(), At.AddMilliseconds(3), At.AddMilliseconds(3));
        await Insert("prt_oversized", JsonSerializer.Serialize(new { type = "step-finish", padding = new string('x', 300_000) }), Message(), At.AddMilliseconds(4), At.AddMilliseconds(4));

        var result = await Adapter().ReadAsync(10);

        Assert.Single(result.Events); Assert.Equal(5, result.RecordsRead); Assert.Equal(1, result.RecordsIgnored); Assert.Equal(3, result.RecordsMalformed);
        Assert.Equal([OpenCodeUsageReadWarning.MalformedRecord], result.Warnings);
        Assert.Equal(UsageFinishReason.Stop, result.Events[0].FinishReason);
    }

    [Fact]
    public async Task Unsupported_or_unavailable_sources_fail_closed_with_typed_results()
    {
        await using (var db = Open(_path)) await Execute(db, "CREATE TABLE part(id TEXT PRIMARY KEY, payload TEXT NOT NULL);");
        var unsupported = await Adapter().ReadAsync(10);
        var unavailable = await new OpenCodeSqliteUsageSourceAdapter(_path + ".missing", Authorization(7)).ReadAsync(10);

        Assert.Empty(unsupported.Events); Assert.Equal([OpenCodeUsageReadWarning.UnsupportedSchema], unsupported.Warnings);
        Assert.Empty(unavailable.Events); Assert.Equal([OpenCodeUsageReadWarning.Unavailable], unavailable.Warnings);
        await using var verify = Open(_path);
        Assert.Equal(1L, await Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='part';"));
    }

    [Fact]
    public async Task Explicit_row_bound_is_deterministic_and_reports_truncation()
    {
        await CreateSchema();
        await Insert("prt_b", Part(), Message(model: "second"), At.AddMilliseconds(1), At.AddMilliseconds(1));
        await Insert("prt_a", Part(), Message(model: "first"), At, At);
        await Insert("prt_c", Part(), Message(model: "third"), At.AddMilliseconds(2), At.AddMilliseconds(2));

        var result = await Adapter().ReadAsync(2);

        Assert.Equal(["second", "first"], result.Events.Select(item => item.Metadata.Model));
        Assert.Equal(2, result.RecordsRead); Assert.Equal([OpenCodeUsageReadWarning.Truncated], result.Warnings);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Adapter().ReadAsync(0).AsTask());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Adapter().ReadAsync(2, cancelled.Token).AsTask());
    }

    [Fact]
    public async Task Runtime_boundary_reads_only_allowlisted_fields_from_the_synthetic_fixture()
    {
        await CreateSchema();
        await Insert("prt_private_source_id", Part(), Message(), At, At);
        var before = await File.ReadAllBytesAsync(_path);

        var result = await Adapter().ReadAsync(10);

        Assert.Equal(before, await File.ReadAllBytesAsync(_path));
        var item = Assert.Single(result.Events);
        Assert.DoesNotContain("private_source_id", item.EventIdentity.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(new[] { "prompt", "response", "path", "project", "session", "message", "content" }, term =>
            result.GetType().GetProperties().Any(property => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Authorized_checkpoint_reopens_resumes_appends_once_and_replays_without_duplicates()
    {
        await CreateSchema(); await Insert("prt_private_first", Part(), Message(), At, At);
        var adapter = Adapter();
        await using (var ledger = new SqliteUsageEventLedger(_ledgerPath))
        {
            await ledger.UpsertSourceAsync(UsageSource());
            var prepared = await adapter.PrepareAsync(await ledger.LoadCheckpointAsync(Source(7), UsageTool.OpenCode), 10);
            Assert.Single(prepared.Events); Assert.NotNull(prepared.NextCheckpoint);
            await ledger.CommitBatchAsync(null, prepared.NextCheckpoint!, prepared.Events);
        }
        await Insert("prt_private_second", Part(), Message(model: "second"), At.AddMilliseconds(1), At.AddMilliseconds(1));

        await using var reopened = new SqliteUsageEventLedger(_ledgerPath);
        var checkpoint = await reopened.LoadCheckpointAsync(Source(7), UsageTool.OpenCode);
        var append = await adapter.PrepareAsync(checkpoint, 10);
        Assert.Equal("second", Assert.Single(append.Events).Metadata.Model);
        await reopened.CommitBatchAsync(checkpoint, append.NextCheckpoint!, append.Events);
        var replayCheckpoint = await reopened.LoadCheckpointAsync(Source(7), UsageTool.OpenCode);
        var replay = await adapter.PrepareAsync(replayCheckpoint, 10);
        Assert.Empty(replay.Events); await reopened.CommitBatchAsync(replayCheckpoint, replay.NextCheckpoint!, replay.Events);
        Assert.Equal(2, await reopened.CountAsync());

        await using var durable = Open(_ledgerPath);
        var columns = (string)(await Scalar(durable, "SELECT group_concat(name, ',') FROM pragma_table_info('usage_checkpoint');"))!;
        Assert.DoesNotContain(new[] { "path", "project", "session", "message", "prompt", "response", "content", "credential" },
            term => columns.Contains(term, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0L, await Scalar(durable, "SELECT COUNT(*) FROM usage_checkpoint WHERE state GLOB '*[^0-9A-F]*' OR length(state)<>64;"));
        Assert.Equal(0L, await Scalar(durable, "SELECT COUNT(*) FROM usage_event WHERE coalesce(provider,'')||coalesce(api,'')||coalesce(model,'')||coalesce(currency,'') LIKE '%must-not-escape%';"));
    }

    [Fact]
    public async Task Failure_cancellation_and_stale_expected_checkpoint_leave_work_retryable()
    {
        await CreateSchema(); await Insert("prt_retry", Part(), Message(), At, At);
        var prepared = await Adapter().PrepareAsync(null, 10);
        await using var ledger = new SqliteUsageEventLedger(_ledgerPath); await ledger.UpsertSourceAsync(UsageSource());
        await using (var db = Open(_ledgerPath)) await Execute(db, "CREATE TRIGGER reject_checkpoint BEFORE INSERT ON usage_checkpoint BEGIN SELECT RAISE(ABORT,'synthetic failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => ledger.CommitBatchAsync(null, prepared.NextCheckpoint!, prepared.Events).AsTask());
        Assert.Equal(0, await ledger.CountAsync()); Assert.Null(await ledger.LoadCheckpointAsync(Source(7), UsageTool.OpenCode));

        await using (var db = Open(_ledgerPath)) await Execute(db, "DROP TRIGGER reject_checkpoint;");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ledger.CommitBatchAsync(null, prepared.NextCheckpoint!, prepared.Events, cancelled.Token).AsTask());
        Assert.Equal(0, await ledger.CountAsync()); Assert.Null(await ledger.LoadCheckpointAsync(Source(7), UsageTool.OpenCode));
        await ledger.CommitBatchAsync(null, prepared.NextCheckpoint!, prepared.Events);
        await Assert.ThrowsAsync<UsageCheckpointConflictException>(() => ledger.CommitBatchAsync(null, prepared.NextCheckpoint!, prepared.Events).AsTask());
        Assert.Equal(1, await ledger.CountAsync());
    }

    [Fact]
    public async Task Wrong_authorization_schema_drift_and_source_replacement_require_rebuild()
    {
        await CreateSchema(); await Insert("prt_anchor", Part(), Message(), At, At);
        var checkpoint = (await Adapter().PrepareAsync(null, 10)).NextCheckpoint!;
        Assert.Equal([OpenCodeUsageReadWarning.RebuildRequired], (await Adapter().PrepareAsync(
            new(Source(8), UsageTool.OpenCode, 1, checkpoint.Cursor, checkpoint.State), 10)).Warnings);

        await using (var db = Open(_path)) await Execute(db, "ALTER TABLE part ADD COLUMN drift TEXT;");
        Assert.Equal([OpenCodeUsageReadWarning.RebuildRequired], (await Adapter().PrepareAsync(checkpoint, 10)).Warnings);
        SqliteConnection.ClearAllPools(); File.Delete(_path); await CreateSchema();
        await Insert("prt_replacement", Part(), Message(), At, At);
        Assert.Equal([OpenCodeUsageReadWarning.RebuildRequired], (await Adapter().PrepareAsync(checkpoint, 10)).Warnings);
        Assert.Equal([OpenCodeUsageReadWarning.UnsupportedSchema], (await new OpenCodeSqliteUsageSourceAdapter(_path,
            new(Source(7), 2)).PrepareAsync(null, 10)).Warnings);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _path, _ledgerPath }) if (File.Exists(path)) File.Delete(path);
    }

    private OpenCodeSqliteUsageSourceAdapter Adapter() => new(_path, Authorization(7));
    private static OpenCodeUsageSourceAuthorization Authorization(byte value) => new(Source(value), 1);
    private static UsageSource UsageSource() => new(Source(7), UsageTool.OpenCode, 1, 1, At, At, UsageSourceState.Active, 0);
    private static UsageSourceIdentity Source(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
    private static readonly DateTimeOffset At = new(2026, 8, 13, 10, 0, 0, TimeSpan.Zero);
    private static string Part(string reason = "stop", long? total = 15, long input = 1, long output = 4, long reasoning = 5, long read = 2, long write = 3) => JsonSerializer.Serialize(new
    {
        type = "step-finish", reason, cost = 99.99, tokens = new { total, input, output, reasoning, cache = new { read, write } },
        prompt = "must-not-escape", response = "must-not-escape", path = "must-not-escape"
    });
    private static string Message(string provider = "provider", string model = "model", bool summary = false) => JsonSerializer.Serialize(new
    {
        role = "assistant", providerID = provider, modelID = model, summary,
        sessionID = "must-not-escape", projectID = "must-not-escape", path = new { cwd = "must-not-escape", root = "must-not-escape" }
    });
    private async Task CreateSchema()
    {
        await using var db = Open(_path);
        await Execute(db, "CREATE TABLE message(id TEXT PRIMARY KEY,session_id TEXT NOT NULL,time_created INTEGER NOT NULL,time_updated INTEGER NOT NULL,data TEXT NOT NULL); CREATE TABLE part(id TEXT PRIMARY KEY,message_id TEXT NOT NULL,session_id TEXT NOT NULL,time_created INTEGER NOT NULL,time_updated INTEGER NOT NULL,data TEXT NOT NULL);");
    }
    private async Task Insert(string id, string part, string message, DateTimeOffset created, DateTimeOffset updated)
    {
        await using var db = Open(_path); await using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO message VALUES($message,$session,$created,$updated,$messageData); INSERT INTO part VALUES($part,$message,$session,$created,$updated,$partData);";
        command.Parameters.AddWithValue("$message", "msg_" + id); command.Parameters.AddWithValue("$part", id); command.Parameters.AddWithValue("$session", "sensitive-session");
        command.Parameters.AddWithValue("$created", created.ToUnixTimeMilliseconds()); command.Parameters.AddWithValue("$updated", updated.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$messageData", message); command.Parameters.AddWithValue("$partData", part); await command.ExecuteNonQueryAsync();
    }
    private static SqliteConnection Open(string path) { var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); return db; }
    private static async Task Execute(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
}
