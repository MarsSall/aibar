using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-runtime-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private string LedgerPath => Path.Combine(_root, "ledger.db");
    private string OpenCodePath => Path.Combine(_root, "opencode.db");
    private string PiPath => Path.Combine(_root, "pi.jsonl");

    [Fact]
    public async Task Runs_replays_reconfigures_and_projects_all_durable_source_history()
    {
        Directory.CreateDirectory(_root); await CreateOpenCode(); await AddOpenCode("open-1");
        await File.WriteAllTextAsync(PiPath, Header() + "\n" + Assistant("pi-1") + "\n", new UTF8Encoding(false));
        var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        await using var runtime = new LocalUsageRuntime(settings, ledger, [OpenCode(), Pi()]);

        Assert.All((await runtime.StartAsync()).Sources, item => Assert.Equal(LocalUsageSourceStatus.Completed, item.Status));
        Assert.Equal(2, await ledger.CountAsync()); Assert.All((await runtime.RefreshAsync()).Sources, item => Assert.Equal(0, item.EventsCommitted)); Assert.Equal(2, await ledger.CountAsync());
        await File.AppendAllTextAsync(PiPath, Assistant("pi-2") + "\n", new UTF8Encoding(false));
        await settings.SaveAsync(new(false, true), default); var disabled = await runtime.RefreshAsync();
        Assert.Equal(LocalUsageSourceStatus.Disabled, disabled.Sources[0].Status); Assert.Equal(3, await ledger.CountAsync());
        await AddOpenCode("open-2"); await settings.SaveAsync(new(true, true), default); await runtime.RefreshAsync();
        Assert.Equal(4, await ledger.CountAsync());

        var facts = await runtime.ProjectAsync(new(TimeZoneInfo.Utc, "runtime-v1"));
        Assert.Equal(8, Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.OpenCode)).Tokens.Total);
        Assert.Equal(6, Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.Pi)).Tokens.Total);
        Assert.Equal(14, Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.Combined)).Tokens.Total);
    }

    [Fact]
    public async Task Disabled_access_is_skipped_failures_are_isolated_and_results_are_private()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); var calls = new List<UsageTool>(); var perTool = new Dictionary<UsageTool, int>();
        var registrations = new List<LocalUsageSourceRegistration> { OpenCode(), Pi("private-path.jsonl") };
        ValueTask<LocalUsagePreparedBatch> Prepare(LocalUsageSourceRegistration item, UsageSourceCheckpoint? checkpoint, int _, CancellationToken __)
        {
            calls.Add(item.Tool); perTool[item.Tool] = perTool.GetValueOrDefault(item.Tool) + 1;
            if (item.Tool == UsageTool.Pi) return perTool[item.Tool] == 1 ? ValueTask.FromResult(new LocalUsagePreparedBatch([], 0, 0, 0, [], null)) : throw new InvalidOperationException("private-exception-message");
            return ValueTask.FromResult(perTool[item.Tool] == 1 ? new LocalUsagePreparedBatch([], 2, 1, 1, [], new(item.Identity, item.Tool, item.SourceSchemaVersion, 2, new string('A', 64))) : new([Event(item, 3), Event(item, 4)], 0, int.MaxValue, int.MaxValue, [], new(item.Identity, item.Tool, item.SourceSchemaVersion, 3, new string('A', 64))));
        }
        await using var runtime = new LocalUsageRuntime(settings, ledger, registrations, 10, Prepare); registrations.Clear();

        Assert.All((await runtime.StartAsync()).Sources, item => Assert.Equal(LocalUsageSourceStatus.Disabled, item.Status)); Assert.Empty(calls);
        await settings.SaveAsync(new(true, false), default); var disabled = await runtime.RefreshAsync();
        Assert.Equal([UsageTool.OpenCode], calls); Assert.Equal((2, 1, 1), (disabled.Sources[0].RecordsRead, disabled.Sources[0].RecordsIgnored, disabled.Sources[0].RecordsMalformed)); Assert.Equal(LocalUsageSourceStatus.Disabled, disabled.Sources[1].Status);
        await settings.SaveAsync(new(true, true), default); var partial = await runtime.RefreshAsync();
        Assert.All(partial.Sources, item => Assert.Equal(LocalUsageSourceStatus.Failed, item.Status)); Assert.All(partial.Sources, item => Assert.Contains(LocalUsageWarning.InvalidAdapterResult, item.WarningCodes)); Assert.Equal((0, 0, 0), (partial.Sources[0].RecordsRead, partial.Sources[0].RecordsIgnored, partial.Sources[0].RecordsMalformed));
        await using (var db = Open(LedgerPath)) Assert.Equal(2L, await Scalar(db, "SELECT COUNT(*) FROM usage_source WHERE state=2 AND warning_flags>0;"));
        var failed = await runtime.RefreshAsync(); Assert.Contains(LocalUsageWarning.SourceFailed, failed.Sources[1].WarningCodes);
        var names = typeof(LocalUsageSourceRunResult).GetProperties().Select(item => item.Name);
        Assert.DoesNotContain(names, name => new[] { "path", "identity", "raw", "content", "exception", "project", "session" }.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        var json = JsonSerializer.Serialize(partial); Assert.DoesNotContain("private-path", json); Assert.DoesNotContain("private-exception", json);
    }

    [Fact]
    public async Task Cancellation_after_first_commit_throws_and_preserves_its_checkpoint()
    {
        Directory.CreateDirectory(_root); await CreateOpenCode(); await AddOpenCode("committed"); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); var calls = 0;
        async ValueTask<LocalUsagePreparedBatch> Prepare(LocalUsageSourceRegistration item, UsageSourceCheckpoint? checkpoint, int maximum, CancellationToken token) { calls++; var read = await new OpenCodeSqliteUsageSourceAdapter(item.Path, new(item.Identity, 1)).PrepareAsync(checkpoint, maximum, token); return new(read.Events, read.RecordsRead, read.RecordsIgnored, read.RecordsMalformed, [], read.NextCheckpoint); }
        LocalUsageRuntime? owner = null; Task? stop = null; await using var runtime = owner = new(settings, ledger, [OpenCode(), Pi()], 10, Prepare, afterSource: tool => { if (tool == UsageTool.OpenCode) stop = owner!.StopAsync().AsTask(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StartAsync().AsTask()); await stop!; Assert.Equal(1, calls);
        Assert.Equal(1, await ledger.CountAsync()); Assert.NotNull(await ledger.LoadCheckpointAsync(Id(1), UsageTool.OpenCode)); Assert.Null(await ledger.LoadCheckpointAsync(Id(2), UsageTool.Pi));
    }

    [Fact]
    public async Task Checkpoint_conflict_and_metadata_failure_return_safe_unavailable_results()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, false), default); await using var ledger = new SqliteUsageEventLedger(LedgerPath); var next = new UsageSourceCheckpoint(Id(1), UsageTool.OpenCode, 1, 0, new string('A', 64));
        async ValueTask<LocalUsagePreparedBatch> Prepare(LocalUsageSourceRegistration item, UsageSourceCheckpoint? _, int __, CancellationToken token) { await ledger.UpsertSourceAsync(new(item.Identity, item.Tool, 1, 1, At, At, UsageSourceState.Active, 0), token); await ledger.CommitBatchAsync(null, next, [], token); return new([], 2, 1, 1, [], next); }
        await using var runtime = new LocalUsageRuntime(settings, ledger, [OpenCode()], 10, Prepare); var conflict = Assert.Single((await runtime.StartAsync()).Sources); Assert.Equal([LocalUsageWarning.CheckpointConflict], conflict.WarningCodes); Assert.Equal((2, 1, 1, 0), (conflict.RecordsRead, conflict.RecordsIgnored, conflict.RecordsMalformed, conflict.EventsPrepared));
        await using (var db = Open(LedgerPath)) Assert.Equal(1L, await Scalar(db, "SELECT COUNT(*) FROM usage_source WHERE state=2 AND warning_flags>0;"));
        await ledger.DisposeAsync(); await using var broken = new LocalUsageRuntime(settings, ledger, [OpenCode()]); Assert.Equal([LocalUsageWarning.SourceFailed], Assert.Single((await broken.StartAsync()).Sources).WarningCodes);
    }

    [Fact]
    public async Task Stop_and_dispose_cancel_and_await_serialized_projections_and_close_entry_points()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await using var ledger = new SqliteUsageEventLedger(LedgerPath); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<IReadOnlyList<DailyToolModelUsageFact>> Project(TimeZoneLocalDayPolicy _, CancellationToken token) { entered.SetResult(); await release.Task; return []; }
        var runtime = new LocalUsageRuntime(settings, ledger, [OpenCode()], 10, (item, checkpoint, _, _) => ValueTask.FromResult(Empty(item, checkpoint)), Project);
        var projection = runtime.ProjectAsync(new(TimeZoneInfo.Utc, "test")).AsTask(); var queued = runtime.ProjectAsync(new(TimeZoneInfo.Utc, "test")).AsTask(); await entered.Task; var stop = runtime.StopAsync().AsTask(); Assert.False(stop.IsCompleted); release.SetResult(); await stop;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => projection); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously); projection = runtime.ProjectAsync(new(TimeZoneInfo.Utc, "test")).AsTask(); await entered.Task; var dispose = runtime.DisposeAsync().AsTask(); Assert.False(dispose.IsCompleted); await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.ProjectAsync(new(TimeZoneInfo.Utc, "test")).AsTask()); release.SetResult(); await dispose; await runtime.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => projection); await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.StartAsync().AsTask()); await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.ProjectAsync(new(TimeZoneInfo.Utc, "test")).AsTask());
    }

    [Fact]
    public async Task Concurrent_runs_coalesce_and_stop_allows_a_clean_restart_before_dispose()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, false), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        async ValueTask<LocalUsagePreparedBatch> Prepare(LocalUsageSourceRegistration item, UsageSourceCheckpoint? checkpoint, int _, CancellationToken token)
        { Interlocked.Increment(ref calls); entered.TrySetResult(); await release.Task.WaitAsync(token); return Empty(item, checkpoint); }
        var runtime = new LocalUsageRuntime(settings, ledger, [OpenCode()], 10, Prepare);

        using var caller = new CancellationTokenSource(); var first = runtime.StartAsync(caller.Token).AsTask(); await entered.Task; var second = runtime.RefreshAsync().AsTask(); caller.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first); release.SetResult(); await second; Assert.Equal(1, calls);
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = runtime.StartAsync().AsTask(); await entered.Task; await runtime.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled); release.TrySetResult();
        release = new(TaskCreationOptions.RunContinuationsAsynchronously); release.SetResult();
        Assert.Equal(LocalUsageSourceStatus.Completed, Assert.Single((await runtime.StartAsync()).Sources).Status);
        entered = new(TaskCreationOptions.RunContinuationsAsynchronously); release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing = runtime.StartAsync().AsTask(); await entered.Task; await runtime.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposing); release.TrySetResult();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => runtime.StartAsync().AsTask());
    }

    [Fact]
    public async Task Rejects_ambiguous_or_mismatched_explicit_registrations()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.PiJsonlV3, PiPath, Id(1), 3));
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, OpenCodePath, Id(1), 2));
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, " ", Id(1), 1));
        Assert.Throws<ArgumentException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, Path.GetPathRoot(_root)!, Id(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalUsageSourceRegistration((UsageTool)99, LocalUsageAdapterKind.OpenCodeSqliteV1, OpenCodePath, Id(1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalUsageSourceRegistration(UsageTool.OpenCode, (LocalUsageAdapterKind)99, OpenCodePath, Id(1), 1));
        Assert.Throws<ArgumentException>(() => new LocalUsageRuntime(settings, ledger, [OpenCode(1), OpenCode(2)]));
        Assert.Throws<ArgumentException>(() => new LocalUsageRuntime(settings, ledger, [OpenCode(1), Pi(identity: 1)]));
        Assert.Throws<ArgumentException>(() => new LocalUsageRuntime(settings, ledger, []));
        Assert.Throws<ArgumentException>(() => new LocalUsageRuntime(settings, ledger, [OpenCode(), new(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, OpenCodePath.ToUpperInvariant(), Id(2), 3)]));
    }

    private LocalUsageSourceRegistration OpenCode(byte identity = 1) => new(UsageTool.OpenCode, LocalUsageAdapterKind.OpenCodeSqliteV1, OpenCodePath, Id(identity), 1);
    private LocalUsageSourceRegistration Pi(string? file = null, byte identity = 2) => new(UsageTool.Pi, LocalUsageAdapterKind.PiJsonlV3, file is null ? PiPath : Path.Combine(_root, file), Id(identity), 3);
    private static UsageSourceIdentity Id(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
    private static LocalUsagePreparedBatch Empty(LocalUsageSourceRegistration item, UsageSourceCheckpoint? checkpoint) =>
        new([], 0, 0, 0, [], checkpoint ?? new(item.Identity, item.Tool, item.SourceSchemaVersion, 0, new string('A', 64)));
    private static NormalizedUsageEvent Event(LocalUsageSourceRegistration item, byte id) => new(new(Enumerable.Repeat(id, 32).ToArray()), item.Identity, new(Enumerable.Repeat(id, 32).ToArray()), item.Tool, UsageEventKind.AssistantStep, UsagePurpose.Primary, UsageOutcome.Success, UsageFidelity.SuccessfulSettledStep, UsageFinishReason.Stop, At, At, At, new(null, null, "model"), new(0, 0, 0, 0, 0), new(null, null), 1, false);
    private static readonly DateTimeOffset At = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private static string Header() => JsonSerializer.Serialize(new { type = "session", version = 3, id = "synthetic", timestamp = At });
    private static string Assistant(string id) => JsonSerializer.Serialize(new { type = "message", id, timestamp = At, message = new { role = "assistant", api = "api", provider = "provider", model = "model", timestamp = At.ToUnixTimeMilliseconds(), stopReason = "stop", usage = new { input = 1, output = 2, reasoning = 1, cacheRead = 0, cacheWrite = 0, totalTokens = 3, cost = new { total = 0 } } } });
    private async Task CreateOpenCode() { await using var db = Open(OpenCodePath); await Execute(db, "CREATE TABLE message(id TEXT PRIMARY KEY,time_created INTEGER,time_updated INTEGER,data TEXT); CREATE TABLE part(id TEXT PRIMARY KEY,message_id TEXT,time_created INTEGER,time_updated INTEGER,data TEXT);"); }
    private async Task AddOpenCode(string id)
    {
        await using var db = Open(OpenCodePath); await using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO message VALUES($m,$at,$at,$md); INSERT INTO part VALUES($p,$m,$at,$at,$pd);";
        command.Parameters.AddWithValue("$m", "message-" + id); command.Parameters.AddWithValue("$p", id); command.Parameters.AddWithValue("$at", At.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$md", "{\"role\":\"assistant\",\"providerID\":\"provider\",\"modelID\":\"model\"}");
        command.Parameters.AddWithValue("$pd", "{\"type\":\"step-finish\",\"reason\":\"stop\",\"tokens\":{\"total\":4,\"input\":1,\"output\":2,\"reasoning\":1,\"cache\":{\"read\":0,\"write\":0}}}"); await command.ExecuteNonQueryAsync();
    }
    private static SqliteConnection Open(string path) { var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); return db; }
    private static async Task Execute(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
