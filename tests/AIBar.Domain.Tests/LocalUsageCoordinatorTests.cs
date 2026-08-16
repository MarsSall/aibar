using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageCoordinatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-private-user-host-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private string SaltPath => Path.Combine(_root, "identity.salt");
    private string LedgerPath => Path.Combine(_root, "ledger.db");
    private string OpenRoot => Path.Combine(_root, "opencode");
    private string PiRoot => Path.Combine(_root, "pi");
    private static TimeZoneLocalDayPolicy DayPolicy => new(TimeZoneInfo.Utc, "coordinator-v1");

    [Fact]
    public async Task Both_disabled_and_existing_salt_probe_touch_no_storage_or_sources()
    {
        var protector = new SyntheticProtector(); var salt = new LocalUsageIdentitySaltStore(SaltPath, protector);
        Assert.Null(await salt.LoadExistingAsync(default)); Assert.False(Directory.Exists(_root));
        await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        await using var coordinator = new LocalUsageCoordinator(new(SettingsPath), salt, ledger, OpenRoot, PiRoot, DayPolicy);

        var result = await coordinator.StartAsync();

        Assert.All(result.Tools, item => Assert.Equal(LocalUsageSourceStatus.Disabled, item.Status));
        Assert.Equal(LocalUsageProjectionStatus.Skipped, result.ProjectionStatus); Assert.Empty(result.ProjectionFacts);
        Assert.Equal(0, protector.Calls); Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task Salt_is_created_once_reused_and_not_recreated_when_source_history_exists()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, false), default);
        var protector = new SyntheticProtector(); await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        await using var coordinator = new LocalUsageCoordinator(settings, new(SaltPath, protector), ledger, OpenRoot, PiRoot, DayPolicy);

        Assert.Equal(LocalUsageSourceStatus.Unavailable, (await coordinator.StartAsync()).Tools[0].Status);
        Assert.True(File.Exists(SaltPath)); Assert.Equal((1, 0), (protector.ProtectCalls, protector.UnprotectCalls));
        await coordinator.RefreshAsync(); Assert.Equal((1, 1), (protector.ProtectCalls, protector.UnprotectCalls));
        await ledger.UpsertSourceAsync(new(Id(9), UsageTool.OpenCode, 1, 1, At, At, UsageSourceState.Active, 0)); File.Delete(SaltPath);

        var refused = await coordinator.RefreshAsync();

        Assert.Equal(LocalUsageSourceStatus.RebuildRequired, refused.Tools[0].Status);
        Assert.Contains(LocalUsageWarning.RebuildRequired, refused.Tools[0].WarningCodes);
        Assert.Equal((1, 1), (protector.ProtectCalls, protector.UnprotectCalls)); Assert.False(File.Exists(SaltPath));
    }

    [Fact]
    public async Task Direct_and_fan_in_replay_rediscover_and_project_complete_durable_history_privately()
    {
        Directory.CreateDirectory(_root); await CreateOpenCode(); await AddOpenCode("private-open-id");
        await PiSession("one", "private-same-id"); await PiSession("two", "private-same-id");
        var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, true), default);
        await using var ledger = new SqliteUsageEventLedger(LedgerPath);
        var coordinator = new LocalUsageCoordinator(settings, new(SaltPath, new SyntheticProtector()), ledger, OpenRoot, PiRoot, DayPolicy);

        var first = await coordinator.StartAsync();
        Assert.Equal([LocalUsageSourceStatus.Completed, LocalUsageSourceStatus.Completed], first.Tools.Select(item => item.Status));
        Assert.Equal(3, first.Tools.Sum(item => item.EventsCommitted)); Assert.Equal(3, await ledger.CountAsync());
        Assert.Equal((4L, 6L, 10L), Totals(first.ProjectionFacts));
        Assert.Equal(0, (await coordinator.RefreshAsync()).Tools.Sum(item => item.EventsCommitted));

        await using (var db = Open(Path.Combine(OpenRoot, "opencode.db"))) await Execute(db, "DROP TABLE part;");
        await PiSession("three", "private-same-id");
        var rediscovered = await coordinator.RefreshAsync();
        Assert.Equal(LocalUsageSourceStatus.Unavailable, rediscovered.Tools[0].Status); Assert.Equal(1, rediscovered.Tools[1].EventsCommitted);
        Assert.Equal((4L, 9L, 13L), Totals(rediscovered.ProjectionFacts)); Assert.Equal(4, await ledger.CountAsync());
        await settings.SaveAsync(new(false, true), default); var disabled = await coordinator.RefreshAsync();
        Assert.Equal(LocalUsageSourceStatus.Disabled, disabled.Tools[0].Status); Assert.Equal((4L, 9L, 13L), Totals(disabled.ProjectionFacts));
        await using (var db = Open(LedgerPath)) Assert.Equal("4,4", await Scalar(db, "SELECT COUNT(*)||','||COUNT(DISTINCT hex(source_key)) FROM usage_checkpoint;"));

        var json = JsonSerializer.Serialize(rediscovered); Assert.DoesNotContain(_root, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(Environment.MachineName, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-same-id", json); Assert.DoesNotContain("private-open-id", json); Assert.DoesNotContain(_root, rediscovered.ToString());
        var names = typeof(LocalUsageCoordinatorResult).GetProperties().Concat(typeof(LocalUsageToolOutcome).GetProperties()).Select(item => item.Name);
        Assert.DoesNotContain(names, name => new[] { "path", "identity", "candidate", "admission", "exception", "salt", "prompt", "user", "host", "content", "argv" }.Any(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        await coordinator.DisposeAsync(); var closed = await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.StartAsync().AsTask()); Assert.DoesNotContain(_root, closed.ToString());
    }

    [Fact]
    public async Task Lifecycle_coalesces_detaches_cancellation_stops_restarts_and_disposes()
    {
        Directory.CreateDirectory(_root); var settings = new LocalUsageSettings(SettingsPath); await settings.SaveAsync(new(true, false), default);
        var entered = NewSignal(); var release = NewSignal(); var block = true; var calls = 0;
        async ValueTask Pause(CancellationToken token) { Interlocked.Increment(ref calls); entered.TrySetResult(); if (block) await release.Task.WaitAsync(token); }
        await using var ledger = new SqliteUsageEventLedger(LedgerPath); var coordinator = new LocalUsageCoordinator(settings,
            new(SaltPath, new SyntheticProtector(), Pause), ledger, OpenRoot, PiRoot, DayPolicy);

        using var caller = new CancellationTokenSource(); var first = coordinator.StartAsync(caller.Token).AsTask(); await entered.Task;
        var joined = coordinator.RefreshAsync().AsTask(); caller.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        block = false; release.SetResult(); await joined; Assert.Equal(1, calls);

        File.Delete(SaltPath); entered = NewSignal(); release = NewSignal(); block = true; var stopped = coordinator.StartAsync().AsTask(); await entered.Task;
        await coordinator.StopAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopped);
        block = false; Assert.Equal(LocalUsageSourceStatus.Unavailable, (await coordinator.StartAsync()).Tools[0].Status);

        File.Delete(SaltPath); entered = NewSignal(); release = NewSignal(); block = true; var disposing = coordinator.StartAsync().AsTask(); await entered.Task;
        await coordinator.DisposeAsync(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disposing);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.RefreshAsync().AsTask());
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static (long OpenCode, long Pi, long Combined) Totals(IReadOnlyList<DailyToolModelUsageFact> facts) =>
        (Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.OpenCode)).Tokens.Total,
         Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.Pi)).Tokens.Total,
         Assert.Single(facts.Where(item => item.Scope == UsageProjectionScope.Combined)).Tokens.Total);
    private async Task CreateOpenCode() { Directory.CreateDirectory(OpenRoot); await using var db = Open(Path.Combine(OpenRoot, "opencode.db")); await Execute(db, "CREATE TABLE message(id TEXT PRIMARY KEY,time_created INTEGER,time_updated INTEGER,data TEXT); CREATE TABLE part(id TEXT PRIMARY KEY,message_id TEXT,time_created INTEGER,time_updated INTEGER,data TEXT);"); }
    private async Task AddOpenCode(string id) { await using var db = Open(Path.Combine(OpenRoot, "opencode.db")); await using var command = db.CreateCommand(); command.CommandText = "INSERT INTO message VALUES($m,$at,$at,$md); INSERT INTO part VALUES($p,$m,$at,$at,$pd);"; command.Parameters.AddWithValue("$m", "message-" + id); command.Parameters.AddWithValue("$p", id); command.Parameters.AddWithValue("$at", At.ToUnixTimeMilliseconds()); command.Parameters.AddWithValue("$md", "{\"role\":\"assistant\",\"providerID\":\"provider\",\"modelID\":\"model\"}"); command.Parameters.AddWithValue("$pd", "{\"type\":\"step-finish\",\"reason\":\"stop\",\"tokens\":{\"total\":4,\"input\":1,\"output\":2,\"reasoning\":1,\"cache\":{\"read\":0,\"write\":0}}}"); await command.ExecuteNonQueryAsync(); }
    private async Task PiSession(string directory, string id) { var path = Path.Combine(PiRoot, $"--{directory}--"); Directory.CreateDirectory(path); await File.WriteAllTextAsync(Path.Combine(path, "session.jsonl"), Header() + "\n" + Assistant(id) + "\n", new UTF8Encoding(false)); }
    private static string Header() => JsonSerializer.Serialize(new { type = "session", version = 3, id = "fixture", timestamp = At });
    private static string Assistant(string id) => JsonSerializer.Serialize(new { type = "message", id, timestamp = At, message = new { role = "assistant", api = "api", provider = "provider", model = "model", timestamp = At.ToUnixTimeMilliseconds(), stopReason = "stop", usage = new { input = 1, output = 2, reasoning = 1, cacheRead = 0, cacheWrite = 0, totalTokens = 3, cost = new { total = 0 } } } });
    private static UsageSourceIdentity Id(byte value) => new(Enumerable.Repeat(value, 32).ToArray());
    private static SqliteConnection Open(string path) { var db = new SqliteConnection($"Data Source={path};Pooling=False"); db.Open(); return db; }
    private static async Task Execute(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    private static async Task<object?> Scalar(SqliteConnection db, string sql) { await using var command = db.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
    private static readonly DateTimeOffset At = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class SyntheticProtector : ILocalUsageIdentityProtector
    {
        public int ProtectCalls { get; private set; } public int UnprotectCalls { get; private set; } public int Calls => ProtectCalls + UnprotectCalls;
        public byte[] Protect(byte[] plaintext) { ProtectCalls++; return plaintext.Select(value => (byte)(value ^ 0xA5)).ToArray(); }
        public byte[] Unprotect(byte[] payload) { UnprotectCalls++; return payload.Select(value => (byte)(value ^ 0xA5)).ToArray(); }
    }
}
