using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class SqliteUsageEventLedgerTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-ledger-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Creates_v2_namespaced_schema_without_claiming_database_version()
    {
        await using (var existing = Open()) await Execute(existing, "CREATE TABLE unrelated(value TEXT); INSERT INTO unrelated VALUES('keep'); PRAGMA user_version=3;");
        await using (var ledger = await Ledger()) { Assert.Equal(0, await ledger.CountAsync()); await ledger.UpsertBatchAsync([Event()]); }
        await using var db = new SqliteConnection($"Data Source={_path};Pooling=False"); await db.OpenAsync();
        Assert.Equal(3L, await Scalar(db, "PRAGMA user_version;")); Assert.Equal("wal", await Scalar(db, "PRAGMA journal_mode;"));
        Assert.Equal(2L, await Scalar(db, "SELECT version FROM usage_schema_meta WHERE component='usage_ledger';"));
        Assert.Equal("keep", await Scalar(db, "SELECT value FROM unrelated;"));
        Assert.Equal(3L, await Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN('usage_source','usage_event','usage_checkpoint');"));
        Assert.Equal(1L, await Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='usage_event_source_idx';"));
        var columns = (string)(await Scalar(db, "SELECT group_concat(name, ',') FROM (SELECT name FROM pragma_table_info('usage_source') UNION ALL SELECT name FROM pragma_table_info('usage_event'));"))!;
        Assert.DoesNotContain(new[] { "path", "session", "project", "message", "response", "prompt", "content" }, term => columns.Contains(term, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("aibar_metadata_valid", (string)(await Scalar(db, "SELECT sql FROM sqlite_master WHERE name='usage_event';"))!);
        Assert.Equal(1L, await Scalar(db, "SELECT COUNT(*) FROM usage_event WHERE provider IS NULL;"));
        await Assert.ThrowsAsync<SqliteException>(() => Execute(db, "UPDATE usage_event SET provider='valid';"));
        await Assert.ThrowsAsync<SqliteException>(() => Execute(db, "INSERT INTO usage_source VALUES(zeroblob(31),1,1,1,0,0,1,0);"));
        await Assert.ThrowsAsync<SqliteException>(() => Execute(db, "INSERT INTO usage_source VALUES('12345678901234567890123456789012',1,1,1,0,0,1,0);"));
    }
    [Fact]
    public async Task Source_upsert_preserves_first_seen_and_never_moves_last_seen_backward()
    {
        await using var ledger = new SqliteUsageEventLedger(_path);
        await ledger.UpsertSourceAsync(Source(first: At.AddMinutes(2), last: At.AddMinutes(4), warnings: 1));
        await ledger.UpsertSourceAsync(Source(first: At, last: At.AddMinutes(3), warnings: 9));
        await using var db = Open();
        Assert.Equal(At.ToUnixTimeMilliseconds(), await Scalar(db, "SELECT first_seen_at_ms FROM usage_source;"));
        Assert.Equal(At.AddMinutes(4).ToUnixTimeMilliseconds(), await Scalar(db, "SELECT last_seen_at_ms FROM usage_source;"));
        Assert.Equal(9L, await Scalar(db, "SELECT warning_flags FROM usage_source;"));
    }

    [Fact]
    public async Task Existing_v1_ledger_migrates_checkpoint_storage_in_place()
    {
        await using (var ledger = await Ledger()) Assert.Equal(0, await ledger.CountAsync());
        await using (var db = Open()) await Execute(db, "DROP TABLE usage_checkpoint; UPDATE usage_schema_meta SET version=1 WHERE component='usage_ledger';");
        await using (var reopened = new SqliteUsageEventLedger(_path)) Assert.Equal(0, await reopened.CountAsync());
        await using var verify = Open(); Assert.Equal(2L, await Scalar(verify, "SELECT version FROM usage_schema_meta WHERE component='usage_ledger';"));
        Assert.Equal(1L, await Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='usage_checkpoint';"));
    }

    [Fact]
    public async Task Failed_fresh_schema_creation_leaves_no_version_marker_or_partial_owned_tables()
    {
        await using (var db = Open()) await Execute(db, "CREATE TABLE usage_source(preserved TEXT); INSERT INTO usage_source VALUES('keep');");
        await using var ledger = new SqliteUsageEventLedger(_path);
        await Assert.ThrowsAsync<SqliteException>(() => ledger.CountAsync().AsTask());
        await using var verify = Open(); Assert.Equal("keep", await Scalar(verify, "SELECT preserved FROM usage_source;"));
        Assert.Equal(0L, await Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE name IN('usage_schema_meta','usage_event','usage_checkpoint');"));
    }

    [Fact]
    public async Task Equal_time_source_updates_are_order_independent_and_metadata_is_monotonic()
    {
        var first = Source(state: UsageSourceState.Unavailable, warnings: 1, identityVersion: 1, capabilityVersion: 3);
        var second = Source(state: UsageSourceState.Disabled, warnings: 2, identityVersion: 2, capabilityVersion: 1);
        var forward = await PersistSourceRow(_path, first, second);
        var reversePath = Path.Combine(Path.GetTempPath(), $"aibar-ledger-{Guid.NewGuid():N}.db");
        try { Assert.Equal(forward, await PersistSourceRow(reversePath, second, first)); }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(reversePath)) File.Delete(reversePath); }
        Assert.Equal("2,3,3,3", forward);
    }

    [Fact]
    public async Task Exact_replay_is_idempotent_and_newer_correction_wins_but_stale_does_not()
    {
        await using var ledger = await Ledger(); var original = Event(updated: At.AddMinutes(1), tokens: new(1, 2, 3, 4, 5));
        Assert.Equal(UsageEventWriteState.Inserted, Assert.Single(await ledger.UpsertBatchAsync([original])).State);
        Assert.Equal(UsageEventWriteState.NoChange, Assert.Single(await ledger.UpsertBatchAsync([original])).State);
        var corrected = Event(updated: At.AddMinutes(2), tokens: new(9, 8, 7, 6, 5), outcome: UsageOutcome.Error);
        Assert.Equal(UsageEventWriteState.Updated, Assert.Single(await ledger.UpsertBatchAsync([corrected])).State);
        Assert.Equal(UsageEventWriteState.Stale, Assert.Single(await ledger.UpsertBatchAsync([Event(updated: At.AddMinutes(1), tokens: new(99, 0, 0, 0, 0))])).State);
        Assert.Equal(corrected, await ledger.GetAsync(Id<UsageEventIdentity>(1))); Assert.Equal(1, await ledger.CountAsync());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Equal_timestamp_divergence_collides_without_mutation_in_both_arrival_orders(bool reverse)
    {
        await using var ledger = await Ledger();
        var left = Event(updated: At.AddMinutes(1), tokens: new(1, 0, 0, 0, 0));
        var right = Event(updated: At.AddMinutes(1), tokens: new(2, 0, 0, 0, 0));
        var first = reverse ? right : left; var second = reverse ? left : right;
        Assert.Equal(UsageEventWriteState.Inserted, Assert.Single(await ledger.UpsertBatchAsync([first])).State);
        Assert.Equal(UsageEventWriteState.Collision, Assert.Single(await ledger.UpsertBatchAsync([second])).State);
        Assert.Equal(first, await ledger.GetAsync(first.EventIdentity));
    }
    [Fact]
    public async Task Null_update_order_is_provable_only_toward_a_non_null_timestamp()
    {
        await using var ledger = await Ledger(); var undated = Event(tokens: new(1, 0, 0, 0, 0));
        var dated = Event(updated: At.AddMinutes(1), tokens: new(2, 0, 0, 0, 0));
        Assert.Equal(UsageEventWriteState.Inserted, Assert.Single(await ledger.UpsertBatchAsync([undated])).State);
        Assert.Equal(UsageEventWriteState.Updated, Assert.Single(await ledger.UpsertBatchAsync([dated])).State);
        Assert.Equal(UsageEventWriteState.Stale, Assert.Single(await ledger.UpsertBatchAsync([undated])).State);

        var otherUndated = Event(eventId: 2, sourceEventId: 2, tokens: new(3, 0, 0, 0, 0));
        var divergentUndated = Event(eventId: 2, sourceEventId: 2, tokens: new(4, 0, 0, 0, 0));
        await ledger.UpsertBatchAsync([otherUndated]);
        Assert.Equal(UsageEventWriteState.Collision, Assert.Single(await ledger.UpsertBatchAsync([divergentUndated])).State);
    }

    [Fact]
    public async Task Collision_is_typed_and_rolls_back_the_entire_batch()
    {
        await using var ledger = await Ledger(); var original = Event(); await ledger.UpsertBatchAsync([original]);
        var acceptedFirst = Event(eventId: 2, sourceEventId: 2);
        var collision = Event(eventId: 3, sourceEventId: 1);
        var result = await ledger.UpsertBatchAsync([acceptedFirst, collision]);
        Assert.Equal([UsageEventWriteState.Inserted, UsageEventWriteState.Collision], result.Select(x => x.State));
        Assert.Equal(1, await ledger.CountAsync()); Assert.Null(await ledger.GetAsync(Id<UsageEventIdentity>(2)));
    }

    [Fact]
    public async Task Invalid_second_event_rolls_back_and_tool_namespaces_isolate_source_event_keys()
    {
        await using var ledger = await Ledger();
        var wrongSource = Event(eventId: 2, sourceId: 9, sourceEventId: 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.UpsertBatchAsync([Event(), wrongSource]).AsTask());
        Assert.Equal(0, await ledger.CountAsync());
        await ledger.UpsertSourceAsync(Source(tool: UsageTool.Pi, id: 2));
        await ledger.UpsertBatchAsync([Event(), Event(eventId: 2, sourceId: 2, tool: UsageTool.Pi)]);
        Assert.Equal(2, await ledger.CountAsync());
    }

    [Fact]
    public async Task Roundtrips_all_fields_enums_and_nullability()
    {
        await using var ledger = await Ledger(tool: UsageTool.Pi);
        var item = Event(tool: UsageTool.Pi, kind: UsageEventKind.OtherAggregate, purpose: UsagePurpose.Other,
            outcome: UsageOutcome.Deferred, finish: UsageFinishReason.ToolCall, fidelity: UsageFidelity.PersistedAggregateEvent, aggregate: true,
            metadata: new("Provider", "responses", "model-x"), tokens: new(1, 2, 3, 4, 5, 99), cost: new(123, "eur"), updated: At.AddMinutes(1));
        await ledger.UpsertBatchAsync([item]); Assert.Equal(item, await ledger.GetAsync(item.EventIdentity));
        var nullable = Event(eventId: 2, sourceEventId: 2, tool: UsageTool.Pi, metadata: new(null, null, null), cost: new(null, null));
        await ledger.UpsertBatchAsync([nullable]); Assert.Equal(nullable, await ledger.GetAsync(nullable.EventIdentity));
    }

    [Fact]
    public void Write_result_codes_are_stable()
        => Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Enum.GetValues<UsageEventWriteState>().Select(value => (int)value));

    [Theory]
    [InlineData("\u0000leading")] [InlineData("leading\u001f")] [InlineData("\u007fleading")] [InlineData("trailing\u007f")] [InlineData("c1\u0085control")]
    public async Task Direct_insert_rejects_dotnet_controls_in_metadata(string provider)
    {
        await using var ledger = await Ledger(); Assert.Equal(0, await ledger.CountAsync());
        await using var db = Open();
        await Assert.ThrowsAsync<SqliteException>(() => InsertRawEvent(db, 10, provider, 0, 0, 0, 0, 0));
        await Assert.ThrowsAsync<SqliteException>(() => InsertRawEvent(db, 15, new string('x', 129), 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Direct_inserts_reject_wrong_event_identity_storage_and_every_display_overflow_family()
    {
        await using var ledger = await Ledger(); Assert.Equal(0, await ledger.CountAsync());
        await using var db = Open();
        await Assert.ThrowsAsync<SqliteException>(() => InsertRawEvent(db, 11, null, 0, 0, 0, 0, 0, "printf('%032d',11)"));
        await Assert.ThrowsAsync<SqliteException>(() => InsertRawEvent(db, 12, null, long.MaxValue, 1, 0, 0, 0));
        await Assert.ThrowsAsync<SqliteException>(() => InsertRawEvent(db, 13, null, 0, 0, 0, long.MaxValue, 1));
        await Assert.ThrowsAsync<SqliteException>(() => InsertRawEvent(db, 14, null, long.MaxValue, 0, 0, 1, 0));
    }

    [Theory]
    [InlineData("occurred_at_ms", "253402300800000")] [InlineData("observed_at_ms", "253402300800000")]
    [InlineData("payload_version", "2147483648")] [InlineData("is_aggregate", "1.5")] [InlineData("source_total", "'not-an-integer'")]
    public async Task Direct_inserts_reject_unreconstructible_bounds_and_types(string column, string value)
    {
        await using var ledger = await Ledger(); await using var db = Open();
        await Execute(db, "INSERT INTO usage_event VALUES(randomblob(32),1,(SELECT source_key FROM usage_source),randomblob(32),0,0,NULL,NULL,NULL,NULL,1,1,1,2,1,0,0,0,0,0,0,NULL,NULL,NULL,1);");
        await Assert.ThrowsAsync<SqliteException>(() => Execute(db, $"UPDATE usage_event SET {column}={value};"));
    }
    [Theory]
    [InlineData("identity_version", "2147483648")] [InlineData("first_seen_at_ms", "253402300800000")] [InlineData("warning_flags", "1.5")]
    public async Task Direct_source_updates_reject_unreconstructible_bounds_and_types(string column, string value)
    { await using var ledger = await Ledger(); await using var db = Open(); await Assert.ThrowsAsync<SqliteException>(() => Execute(db, $"UPDATE usage_source SET {column}={value};")); }
    [Fact]
    public async Task Maximum_unix_millisecond_timestamp_roundtrips()
    {
        var maximum = DateTimeOffset.FromUnixTimeMilliseconds(253402300799999); await using var ledger = new SqliteUsageEventLedger(_path);
        await ledger.UpsertSourceAsync(Source(first: maximum, last: maximum)); var item = EventAt(maximum); await ledger.UpsertBatchAsync([item]); Assert.Equal(item, await ledger.GetAsync(item.EventIdentity));
    }
    [Fact]
    public async Task Rejects_future_schema_without_mutation()
    {
        await using (var db = Open()) await Execute(db, "PRAGMA journal_mode=DELETE; CREATE TABLE preserved(value TEXT); INSERT INTO preserved VALUES('keep'); CREATE TABLE usage_schema_meta(component TEXT PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO usage_schema_meta VALUES('usage_ledger',3); PRAGMA user_version=3;");
        await using var ledger = new SqliteUsageEventLedger(_path);
        await Assert.ThrowsAsync<NotSupportedException>(() => ledger.CountAsync().AsTask());
        await using var verify = Open(); Assert.Equal("keep", await Scalar(verify, "SELECT value FROM preserved;"));
        Assert.Equal("delete", await Scalar(verify, "PRAGMA journal_mode;")); Assert.Equal(3L, await Scalar(verify, "PRAGMA user_version;"));
        Assert.Equal(3L, await Scalar(verify, "SELECT version FROM usage_schema_meta WHERE component='usage_ledger';"));
        Assert.Equal(2L, await Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"));
        Assert.Equal(0L, await Scalar(verify, "SELECT COUNT(*) FROM sqlite_master WHERE name='usage_event';"));
    }
    [Fact]
    public async Task External_immediate_lock_blocks_a_separate_ledger_then_releases_one_idempotent_write()
    {
        await using var verifier = await Ledger(); await using var writer = new SqliteUsageEventLedger(_path);
        await using var blocker = new SqliteConnection($"Data Source={_path};Pooling=False"); await blocker.OpenAsync(); await Execute(blocker, "BEGIN IMMEDIATE;");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = Task.Run(async () => { entered.SetResult(); return await writer.UpsertBatchAsync([Event()]); });
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var observation = Task.Delay(TimeSpan.FromMilliseconds(250));
            Assert.Same(observation, await Task.WhenAny(write, observation));
        } finally { await Execute(blocker, "COMMIT;"); }
        var result = await write.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UsageEventWriteState.Inserted, Assert.Single(result).State);
        Assert.Equal(UsageEventWriteState.NoChange, Assert.Single(await verifier.UpsertBatchAsync([Event()])).State);
        Assert.Equal(Event(), await verifier.GetAsync(Id<UsageEventIdentity>(1))); Assert.Equal(1, await verifier.CountAsync());
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); }
    private async Task<SqliteUsageEventLedger> Ledger(UsageTool tool = UsageTool.OpenCode) { var ledger = new SqliteUsageEventLedger(_path); await ledger.UpsertSourceAsync(Source(tool)); return ledger; }
    private SqliteConnection Open() { var db = new SqliteConnection($"Data Source={_path};Pooling=False"); db.Open(); db.CreateFunction<string?, bool>("aibar_metadata_valid", value => { try { _ = new UsageDisplayMetadata(value, null, null); return value is not null; } catch (ArgumentException) { return false; } }, true); return db; }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private static async Task Execute(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task InsertRawEvent(SqliteConnection db, int id, string? provider, long input, long read, long write, long visible, long reasoning, string? eventIdSql = null) {
        await using var command = db.CreateCommand();
        command.CommandText = $"""
            INSERT INTO usage_event VALUES({eventIdSql ?? "$event"},1,$source,$sourceEvent,$at,$at,NULL,$provider,NULL,NULL,1,1,1,2,1,0,$input,$read,$write,$visible,$reasoning,NULL,NULL,NULL,1);
            """;
        command.Parameters.AddWithValue("$event", Id<UsageEventIdentity>(id).ToArray()); command.Parameters.AddWithValue("$source", Id<UsageSourceIdentity>(1).ToArray());
        command.Parameters.AddWithValue("$sourceEvent", Id<UsageSourceEventIdentity>(id).ToArray()); command.Parameters.AddWithValue("$at", At.ToUnixTimeMilliseconds()); command.Parameters.AddWithValue("$provider", provider is null ? DBNull.Value : provider);
        command.Parameters.AddWithValue("$input", input); command.Parameters.AddWithValue("$read", read); command.Parameters.AddWithValue("$write", write);
        command.Parameters.AddWithValue("$visible", visible); command.Parameters.AddWithValue("$reasoning", reasoning);
        await command.ExecuteNonQueryAsync();
    }
    private static readonly DateTimeOffset At = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero);
    private static UsageSource Source(UsageTool tool = UsageTool.OpenCode, int id = 1, DateTimeOffset? first = null, DateTimeOffset? last = null, int warnings = 0,
        UsageSourceState state = UsageSourceState.Active, int identityVersion = 1, int capabilityVersion = 1) => new(Id<UsageSourceIdentity>(id), tool, identityVersion, capabilityVersion, first ?? At, last ?? At, state, warnings);
    private static async Task<string> PersistSourceRow(string path, params UsageSource[] sources) {
        await using (var ledger = new SqliteUsageEventLedger(path)) foreach (var source in sources) await ledger.UpsertSourceAsync(source);
        await using var db = new SqliteConnection($"Data Source={path};Pooling=False"); await db.OpenAsync();
        return (string)(await Scalar(db, "SELECT identity_version||','||capability_version||','||state||','||warning_flags FROM usage_source;"))!;
    }
    private static NormalizedUsageEvent Event(int eventId = 1, int sourceId = 1, int sourceEventId = 1, UsageTool tool = UsageTool.OpenCode,
        UsageEventKind kind = UsageEventKind.AssistantStep, UsagePurpose purpose = UsagePurpose.Primary, UsageOutcome outcome = UsageOutcome.Success,
        UsageFinishReason finish = UsageFinishReason.Stop,
        UsageFidelity fidelity = UsageFidelity.SuccessfulSettledStep, bool aggregate = false, DateTimeOffset? updated = null,
        UsageDisplayMetadata? metadata = null, UsageTokens? tokens = null, UsageCost? cost = null) => new(Id<UsageEventIdentity>(eventId), Id<UsageSourceIdentity>(sourceId), Id<UsageSourceEventIdentity>(sourceEventId), tool, kind, purpose, outcome, fidelity, finish, At, At, updated, metadata ?? new(null, null, null), tokens ?? new(0, 0, 0, 0, 0), cost ?? new(null, null), 1, aggregate);
    private static NormalizedUsageEvent EventAt(DateTimeOffset at) => new(Id<UsageEventIdentity>(1), Id<UsageSourceIdentity>(1), Id<UsageSourceEventIdentity>(1), UsageTool.OpenCode, UsageEventKind.AssistantStep, UsagePurpose.Primary, UsageOutcome.Success, UsageFidelity.SuccessfulSettledStep, UsageFinishReason.Stop, at, at, at, new(null, null, null), new(0, 0, 0, 0, 0), new(null, null), int.MaxValue, false);
    private static T Id<T>(int value) where T : UsageIdentity {
        var bytes = new byte[32]; bytes[0] = checked((byte)value);
        return (T)(UsageIdentity)(typeof(T) == typeof(UsageEventIdentity) ? new UsageEventIdentity(bytes)
            : typeof(T) == typeof(UsageSourceIdentity) ? new UsageSourceIdentity(bytes)
            : new UsageSourceEventIdentity(bytes));
    }
}
