using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class UsageEventProjectorTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"aibar-projection-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Boundary = new(2026, 8, 13, 0, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneLocalDayPolicy Policy = new(TimeZoneInfo.CreateCustomTimeZone("Synthetic/Minus02", TimeSpan.FromHours(-2), "Synthetic", "Synthetic"), "local-day-v1");

    [Fact]
    public async Task Rebuilds_deterministic_local_day_tool_model_and_combined_facts_from_the_ledger()
    {
        IReadOnlyList<DailyToolModelUsageFact> first;
        await using (var ledger = new SqliteUsageEventLedger(_path))
        {
            await ledger.UpsertSourceAsync(Source(1, UsageTool.OpenCode)); await ledger.UpsertSourceAsync(Source(2, UsageTool.Pi));
            var correctedOriginal = Event(2, 1, 2, UsageTool.OpenCode, Boundary.AddHours(2), null, new(3, 3, 3, 3, 3), UsageOutcome.Error, updated: Boundary.AddHours(3));
            var corrected = Event(2, 1, 2, UsageTool.OpenCode, Boundary.AddHours(2), null, new(4, 4, 4, 4, 4), UsageOutcome.Error, updated: Boundary.AddHours(4));
            var sameOpenCode = Event(3, 1, 3, UsageTool.OpenCode, Boundary.AddHours(2), "m", new(1, 1, 1, 1, 1));
            var events = new[] {
                Event(1, 1, 1, UsageTool.OpenCode, Boundary, "z", new(1, 2, 3, 4, 5)), correctedOriginal, sameOpenCode,
                Event(4, 2, 3, UsageTool.Pi, Boundary.AddHours(2), "m", new(1, 1, 1, 1, 1)),
                Event(5, 2, 5, UsageTool.Pi, Boundary.AddHours(2), "m", new(1, 1, 1, 1, 1), UsageOutcome.Aborted),
                Event(6, 2, 6, UsageTool.Pi, Boundary.AddHours(2), "m", new(2, 2, 2, 2, 2), UsageOutcome.Deferred, UsageEventKind.OtherAggregate, true) };
            Assert.All(await ledger.UpsertBatchAsync(events), result => Assert.Equal(UsageEventWriteState.Inserted, result.State));
            Assert.Equal(UsageEventWriteState.NoChange, Assert.Single(await ledger.UpsertBatchAsync([sameOpenCode])).State);
            Assert.Equal(UsageEventWriteState.Updated, Assert.Single(await ledger.UpsertBatchAsync([corrected])).State);
            Assert.Equal(UsageEventWriteState.Stale, Assert.Single(await ledger.UpsertBatchAsync([correctedOriginal])).State);
            first = await new UsageEventProjector(ledger, Policy).RebuildAsync();
        }

        Assert.Equal(new[] {
            "2026-08-12|OpenCode|z|1,2,3,4,5", "2026-08-12|Combined|z|1,2,3,4,5",
            "2026-08-13|OpenCode|Unknown|4,4,4,4,4", "2026-08-13|OpenCode|m|1,1,1,1,1",
            "2026-08-13|Pi|m|4,4,4,4,4", "2026-08-13|Combined|Unknown|4,4,4,4,4", "2026-08-13|Combined|m|5,5,5,5,5"
        }, first.Select(Fingerprint));
        Assert.All(first, fact => { Assert.Equal("Synthetic/Minus02", fact.TimeZoneId); Assert.Equal(TimeSpan.FromHours(-2), fact.ObservedOffset); Assert.Equal("local-day-v1", fact.LocalDayPolicyVersion); });
        Assert.Equal((6L, 9L, 15L), (first[0].Tokens.Input, first[0].Tokens.Output, first[0].Tokens.Total));

        await using var reopened = new SqliteUsageEventLedger(_path);
        Assert.Equal(first, await new UsageEventProjector(reopened, Policy).RebuildAsync());
        await using var db = Open(); Assert.Equal(0L, await Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE '%projection%';"));
    }

    [Fact]
    public async Task Combined_overflow_fails_without_wrapping_or_returning_partial_facts()
    {
        await using var ledger = new SqliteUsageEventLedger(_path);
        await ledger.UpsertSourceAsync(Source(1, UsageTool.OpenCode)); await ledger.UpsertSourceAsync(Source(2, UsageTool.Pi));
        await ledger.UpsertBatchAsync([Event(1, 1, 1, UsageTool.OpenCode, Boundary, "m", new(long.MaxValue, 0, 0, 0, 0), updated: Boundary.AddHours(1)), Event(2, 2, 1, UsageTool.Pi, Boundary, "m", new(1, 0, 0, 0, 0), updated: Boundary.AddHours(1))]);
        var projector = new UsageEventProjector(ledger, Policy);
        await Assert.ThrowsAsync<OverflowException>(() => projector.RebuildAsync().AsTask());
        await ledger.UpsertBatchAsync([Event(2, 2, 1, UsageTool.Pi, Boundary, "m", new(0, 0, 0, 0, 0), updated: Boundary.AddHours(2))]);
        Assert.Equal(long.MaxValue, Assert.Single((await projector.RebuildAsync()).Where(fact => fact.Scope == UsageProjectionScope.Combined)).Tokens.Total);
    }

    [Fact]
    public async Task Cancellation_during_final_materialization_throws_without_returning_partial_facts()
    {
        await using var ledger = new SqliteUsageEventLedger(_path); await ledger.UpsertSourceAsync(Source(1, UsageTool.OpenCode));
        await ledger.UpsertBatchAsync([Event(1, 1, 1, UsageTool.OpenCode, Boundary, "m", new(1, 0, 0, 0, 0))]);
        using var cancellation = new CancellationTokenSource(); var materialized = 0;
        var projector = new UsageEventProjector(ledger, Policy, () => { materialized++; cancellation.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => projector.RebuildAsync(cancellation.Token).AsTask());

        Assert.Equal(1, materialized);
    }

    [Fact]
    public async Task Cancellation_is_observed_and_public_facts_expose_no_ledger_identity_or_content_fields()
    {
        await using var ledger = new SqliteUsageEventLedger(_path); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UsageEventProjector(ledger, Policy).RebuildAsync(cancellation.Token).AsTask());
        Assert.Throws<ArgumentException>(() => new UsageEventProjector(ledger, new(TimeZoneInfo.Utc, " ")));
        var names = typeof(DailyToolModelUsageFact).GetProperties().Select(property => property.Name).Concat(typeof(UsageProjectionTokens).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(names, name => new[] { "source", "event", "path", "session", "project", "prompt", "response", "content", "credential", "argument", "raw" }.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); if (File.Exists(_path)) File.Delete(_path); }
    private SqliteConnection Open() { var db = new SqliteConnection($"Data Source={_path};Pooling=False"); db.Open(); return db; }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private static string Fingerprint(DailyToolModelUsageFact fact) => $"{fact.LocalDay:O}|{fact.Scope}|{fact.Model}|{fact.Tokens.InputUncached},{fact.Tokens.CacheRead},{fact.Tokens.CacheWrite},{fact.Tokens.OutputVisible},{fact.Tokens.OutputReasoning}";
    private static UsageSource Source(int id, UsageTool tool) => new(Id<UsageSourceIdentity>(id), tool, 1, 1, Boundary, Boundary.AddDays(1), UsageSourceState.Active, 0);
    private static NormalizedUsageEvent Event(int id, int sourceId, int sourceEventId, UsageTool tool, DateTimeOffset at, string? model, UsageTokens tokens,
        UsageOutcome outcome = UsageOutcome.Success, UsageEventKind kind = UsageEventKind.AssistantStep, bool aggregate = false, DateTimeOffset? updated = null) =>
        new(Id<UsageEventIdentity>(id), Id<UsageSourceIdentity>(sourceId), Id<UsageSourceEventIdentity>(sourceEventId), tool, kind, UsagePurpose.Primary, outcome,
            aggregate ? UsageFidelity.PersistedAggregateEvent : UsageFidelity.PersistedTerminalEvent, outcome == UsageOutcome.Error ? UsageFinishReason.Error : UsageFinishReason.Stop,
            at, at, updated, new(null, null, model), tokens, new(null, null), 1, aggregate);
    private static T Id<T>(int value) where T : UsageIdentity
    {
        var bytes = new byte[32]; bytes[0] = checked((byte)value);
        return (T)(UsageIdentity)(typeof(T) == typeof(UsageEventIdentity) ? new UsageEventIdentity(bytes) : typeof(T) == typeof(UsageSourceIdentity) ? new UsageSourceIdentity(bytes) : new UsageSourceEventIdentity(bytes));
    }
}
