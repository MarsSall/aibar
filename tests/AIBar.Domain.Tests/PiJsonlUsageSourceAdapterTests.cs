using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class PiJsonlUsageSourceAdapterTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-pi-source-{Guid.NewGuid():N}.jsonl");
    private readonly string _ledgerPath = Path.Combine(Path.GetTempPath(), $"aibar-pi-ledger-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Maps_terminal_retries_and_aggregate_usage_without_reasoning_double_counting()
    {
        await Write(Header("private-session"), Assistant("retry-error", "error", 10, 8, 3, 2, 1),
            Assistant("retry-success", "toolUse", 4, 9, 5), Assistant("aborted", "aborted", 1, 2, 0),
            Aggregate("compaction", "compact", 3, 7, 2), Aggregate("branch_summary", "branch", 2, 4, 1));

        var result = await Adapter().ReadAsync(10);

        Assert.Equal(5, result.Events.Count); Assert.Empty(result.Warnings);
        Assert.Equal([UsageOutcome.Error, UsageOutcome.Success, UsageOutcome.Aborted], result.Events.Take(3).Select(item => item.Outcome));
        Assert.Equal([UsageFinishReason.Error, UsageFinishReason.ToolCall, UsageFinishReason.Other], result.Events.Take(3).Select(item => item.FinishReason));
        Assert.Equal((10, 2, 1, 5, 3, 21), Tokens(result.Events[0]));
        Assert.Equal((UsageEventKind.Compaction, UsagePurpose.Compaction, true), (result.Events[3].Kind, result.Events[3].Purpose, result.Events[3].IsAggregate));
        Assert.Equal((UsageEventKind.BranchSummary, UsagePurpose.BranchSummary, true), (result.Events[4].Kind, result.Events[4].Purpose, result.Events[4].IsAggregate));
        Assert.All(result.Events.Skip(3), item => Assert.Equal((UsageOutcome.Deferred, UsageFinishReason.Other, UsageFidelity.PersistedAggregateEvent), (item.Outcome, item.FinishReason, item.Fidelity)));
        Assert.Equal(5, result.Events.Select(item => item.SourceEventIdentity).Distinct().Count());
        Assert.Null(result.Events[0].Cost.Nanos); Assert.Null(result.Events[0].Cost.Currency);
        Assert.Equal(result.Events, (await Adapter().ReadAsync(10)).Events);
        var otherSource = await new PiJsonlUsageSourceAdapter(_path, Authorization(24)).ReadAsync(10);
        Assert.NotEqual(result.Events[0].EventIdentity, otherSource.Events[0].EventIdentity);
    }

    [Fact]
    public async Task Invalid_utf8_fails_closed_in_headers_and_malformed_records()
    {
        var marker = Encoding.UTF8.GetBytes("must-not-escape"); var header = Encoding.UTF8.GetBytes(Header());
        header[header.AsSpan().IndexOf(marker)] = 0xff; await File.WriteAllBytesAsync(_path, [.. header, (byte)'\n']);
        Assert.Equal([PiUsageReadWarning.UnsupportedSchema], (await Adapter().ReadAsync(10)).Warnings);

        var record = Encoding.UTF8.GetBytes(Assistant("invalid")); record[record.AsSpan().IndexOf(marker)] = 0xff;
        await File.WriteAllBytesAsync(_path, [.. Encoding.UTF8.GetBytes(Header() + "\n"), .. record, (byte)'\n']);
        var result = await Adapter().ReadAsync(10);
        Assert.Empty(result.Events); Assert.Equal(1, result.RecordsMalformed); Assert.Equal([PiUsageReadWarning.MalformedRecord], result.Warnings);
    }

    [Fact]
    public async Task Complete_malformed_and_irrelevant_lines_advance_but_incomplete_tail_retries()
    {
        var pending = Assistant("tail", "stop", 1, 2, 1); var split = pending.Length / 2;
        await File.WriteAllTextAsync(_path, $"{Header()}\n{{bad\n{Assistant("bad-tokens", output: 1, reasoning: 2)}\n{User()}\n{pending[..split]}", new UTF8Encoding(false));

        var first = await Adapter().PrepareAsync(null, 10);

        Assert.Empty(first.Events); Assert.Equal((3, 1, 2), (first.RecordsRead, first.RecordsIgnored, first.RecordsMalformed));
        Assert.Equal([PiUsageReadWarning.MalformedRecord, PiUsageReadWarning.IncompleteTail], first.Warnings);
        await File.AppendAllTextAsync(_path, pending[split..] + "\n", new UTF8Encoding(false));
        var resumed = await Adapter().PrepareAsync(first.NextCheckpoint, 10);
        Assert.Single(resumed.Events); Assert.Empty(resumed.Warnings);
    }

    [Fact]
    public async Task Oversized_and_record_limits_fail_closed_and_cancellation_propagates()
    {
        await File.WriteAllTextAsync(_path, $"{Header()}\n{{bad\n{new string('x', PiJsonlUsageSourceAdapter.MaximumRecordBytes + 1)}\n{Assistant()}\n", new UTF8Encoding(false));
        var oversized = await Adapter().ReadAsync(10);
        Assert.Empty(oversized.Events); Assert.Equal((1, 1), (oversized.RecordsRead, oversized.RecordsMalformed));
        Assert.Equal([PiUsageReadWarning.MalformedRecord, PiUsageReadWarning.OversizedRecord], oversized.Warnings);

        await Write(Header(), Assistant("one"), Assistant("two"));
        var bounded = await Adapter().ReadAsync(1);
        Assert.Single(bounded.Events); Assert.Equal([PiUsageReadWarning.Truncated], bounded.Warnings);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Adapter().ReadAsync(0).AsTask());
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Adapter().ReadAsync(1, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Independent_writer_can_append_while_the_adapter_read_handle_is_open()
    {
        await Write(Header(), Assistant("first")); var appended = false;
        var adapter = new PiJsonlUsageSourceAdapter(_path, Authorization(), () =>
        {
            File.AppendAllText(_path, Assistant("appended") + "\n", new UTF8Encoding(false)); appended = true;
        });
        var result = await adapter.ReadAsync(10);
        Assert.True(appended); Assert.Equal(2, result.Events.Count); Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Durable_checkpoint_resumes_append_once_and_commits_atomically_without_private_data()
    {
        await Write(Header("must-not-escape"), Assistant("private-id")); var before = await File.ReadAllBytesAsync(_path);
        var adapter = Adapter(); await using (var ledger = new SqliteUsageEventLedger(_ledgerPath))
        {
            await ledger.UpsertSourceAsync(Source()); var prepared = await adapter.PrepareAsync(null, 10);
            Assert.Equal(before, await File.ReadAllBytesAsync(_path));
            await ledger.CommitBatchAsync(null, prepared.NextCheckpoint!, prepared.Events);
        }
        await File.AppendAllTextAsync(_path, Assistant("append", model: "second") + "\n", new UTF8Encoding(false));

        await using var reopened = new SqliteUsageEventLedger(_ledgerPath);
        var checkpoint = await reopened.LoadCheckpointAsync(Identity(), UsageTool.Pi);
        var append = await Adapter().PrepareAsync(checkpoint, 10); Assert.Equal("second", Assert.Single(append.Events).Metadata.Model);
        await reopened.CommitBatchAsync(checkpoint, append.NextCheckpoint!, append.Events);
        var replayCheckpoint = await reopened.LoadCheckpointAsync(Identity(), UsageTool.Pi);
        var replay = await adapter.PrepareAsync(replayCheckpoint, 10); Assert.Empty(replay.Events);
        await reopened.CommitBatchAsync(replayCheckpoint, replay.NextCheckpoint!, replay.Events);
        Assert.Equal(2, await reopened.CountAsync()); Assert.Equal(before, (await File.ReadAllBytesAsync(_path))[..before.Length]);

        await using var db = Open(_ledgerPath);
        var columns = (string)(await Scalar(db, "SELECT group_concat(name, ',') FROM pragma_table_info('usage_checkpoint');"))!;
        Assert.DoesNotContain(new[] { "path", "project", "session", "message", "prompt", "response", "content", "credential" }, term => columns.Contains(term, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0L, await Scalar(db, "SELECT COUNT(*) FROM usage_checkpoint WHERE state GLOB '*[^0-9A-F]*' OR length(state)<>64;"));
        Assert.Equal(0L, await Scalar(db, "SELECT COUNT(*) FROM usage_event WHERE coalesce(provider,'')||coalesce(api,'')||coalesce(model,'') LIKE '%must-not-escape%';"));
    }

    [Fact]
    public async Task Replacement_truncation_schema_drift_and_unavailable_sources_are_typed()
    {
        await Write(Header("first"), Assistant("anchor"), Assistant("middle-a"), Assistant("cursor"));
        var checkpoint = (await Adapter().PrepareAsync(null, 10)).NextCheckpoint!;
        await Write(Header("first"), Assistant("anchor"), Assistant("middle-b"), Assistant("cursor"));
        Assert.Equal([PiUsageReadWarning.RebuildRequired], (await Adapter().PrepareAsync(checkpoint, 10)).Warnings);
        await Write(Header("first"));
        Assert.Equal([PiUsageReadWarning.RebuildRequired], (await Adapter().PrepareAsync(checkpoint, 10)).Warnings);
        await Write(JsonSerializer.Serialize(new { type = "session", version = 4, id = "drift", timestamp = At, cwd = "private" }));
        Assert.Equal([PiUsageReadWarning.UnsupportedSchema], (await Adapter().ReadAsync(10)).Warnings);
        Assert.Equal([PiUsageReadWarning.UnsupportedSchema], (await new PiJsonlUsageSourceAdapter(_path, new(Identity(), 4)).ReadAsync(10)).Warnings);
        Assert.Equal([PiUsageReadWarning.Unavailable], (await new PiJsonlUsageSourceAdapter(_path + ".missing", Authorization()).ReadAsync(10)).Warnings);
    }

    public void Dispose() { foreach (var path in new[] { _path, _ledgerPath }) if (File.Exists(path)) File.Delete(path); }
    private PiJsonlUsageSourceAdapter Adapter() => new(_path, Authorization());
    private static PiUsageSourceAuthorization Authorization(byte value = 23) => new(Identity(value), 3);
    private static UsageSource Source() => new(Identity(), UsageTool.Pi, 1, 1, At, At, UsageSourceState.Active, 0);
    private static UsageSourceIdentity Identity(byte value = 23) => new(Enumerable.Repeat(value, 32).ToArray());
    private static readonly DateTimeOffset At = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    private static (long, long, long, long, long, long?) Tokens(NormalizedUsageEvent item) => (item.Tokens.InputUncached, item.Tokens.CacheRead, item.Tokens.CacheWrite, item.Tokens.OutputVisible, item.Tokens.OutputReasoning, item.Tokens.SourceReportedTotal);
    private static string Header(string id = "header") => JsonSerializer.Serialize(new { type = "session", version = 3, id, timestamp = At, cwd = "must-not-escape" });
    private static string User() => JsonSerializer.Serialize(new { type = "message", id = "user", parentId = (string?)null, timestamp = At, message = new { role = "user", content = "must-not-escape" } });
    private static string Assistant(string id = "assistant", string stop = "stop", long input = 1, long output = 2, long reasoning = 1, long read = 0, long write = 0, string model = "model") => JsonSerializer.Serialize(new { type = "message", id, parentId = "parent", timestamp = At.AddSeconds(1), message = new { role = "assistant", api = "api", provider = "provider", model, content = "must-not-escape", timestamp = At.ToUnixTimeMilliseconds(), stopReason = stop, usage = Usage(input, output, reasoning, read, write), errorMessage = "must-not-escape" } });
    private static string Aggregate(string type, string id, long input, long output, long reasoning) => JsonSerializer.Serialize(new { type, id, parentId = "parent", timestamp = At, summary = "must-not-escape", usage = Usage(input, output, reasoning, 0, 0) });
    private static object Usage(long input, long output, long reasoning, long read, long write) => new { input, output, reasoning, cacheRead = read, cacheWrite = write, totalTokens = input + output + read + write, cost = new { total = 99.0 } };
    private async Task Write(params string[] lines) => await File.WriteAllTextAsync(_path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    private static SqliteConnection Open(string path) { var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); return db; }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
}
