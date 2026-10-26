using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class CodexRemoteHistoryTests
{
    [Fact]
    public async Task Remote_retrieval_is_persisted_as_codex_remote_and_excluded_from_local_pace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-codex-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = SqliteQuotaHistoryStore.DatabasePath(root);
        try
        {
            var clock = new Clock();
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(true, default);
            await settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
            await using var store = new SqliteQuotaHistoryStore(path);
            var coordinator = new QuotaRefreshCoordinator(new Snapshots(), new Provider(clock, 12), clock,
                new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            await using var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock,
                remoteProviderFactory: _ => new Provider(clock, 81), remoteFileAvailable: () => true);
            // Same constructor as production composition: provenance must come from the
            // retrieval, not the legacy default passed when the recorder is constructed.
            await using var recorder = new QuotaHistoryRecorder(coordinator, store, clock,
                QuotaHistoryProvider.Codex, QuotaHistorySource.CodexLocal);
            await runtime.InitializeAsync(default);
            await runtime.RefreshAsync(RefreshTrigger.Manual, default);
            await recorder.DrainAsync(default);
            var rows = await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour,
                clock.UtcNow.AddHours(-1), clock.UtcNow, default);
            Assert.NotEmpty(rows);
            Assert.All(rows, row => Assert.Equal("CodexRemote", row.Source.ToString()));
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT DISTINCT source FROM quota_history;";
                Assert.Equal("codex-remote", (string?)await command.ExecuteScalarAsync());
            }
            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            await runtime.SetSourceAsync(DataSource.ThisPc, default);
            await recorder.DrainAsync(default);
            using var history = new PanelPaceHistory(store, clock);
            var localPace = await history.ReadPaceAsync(QuotaHistoryProvider.Codex);
            Assert.NotEmpty(localPace);
            Assert.All(localPace, row => Assert.Equal(QuotaHistorySource.CodexLocal, row.Source));
            Assert.DoesNotContain(localPace, row => row.Percentage == 81);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Production_composition_records_remote_history_and_does_not_reuse_it_as_local_pace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-history-composition-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
        await settings.SaveAsync(true, default);
        await settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
        var now = DateTimeOffset.UtcNow;
        await File.WriteAllTextAsync(Path.Combine(root, "codex-remote-usage.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1, generatedAt = now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'"), state = "current",
            fiveHour = new { percentageUsed = 81, resetAt = now.AddHours(5).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss'Z'") },
            weekly = (object?)null
        }));
        var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: Path.Combine(root, "home")));
        try
        {
            await composition.Initialize();
            await composition.CodexQuota!.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(81m, composition.CodexQuota.State.Snapshot!.Primary!.PercentageUsed);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            IReadOnlyList<QuotaHistorySample> rows;
            do
            {
                rows = await composition.QuotaHistory!.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour,
                    now.AddHours(-1), DateTimeOffset.UtcNow, timeout.Token);
                if (rows.Count == 0) await Task.Delay(10, timeout.Token);
            } while (rows.Count == 0);
            Assert.All(rows, row => Assert.Equal("CodexRemote", row.Source.ToString()));
            await composition.CodexQuota.SetSourceAsync(DataSource.ThisPc, default);
            Assert.Empty(await composition.PaceHistory!.ReadPaceAsync(QuotaHistoryProvider.Codex));
        }
        finally
        {
            await composition.Resource.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Existing_v1_history_rows_survive_remote_source_schema_upgrade()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-history-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = SqliteQuotaHistoryStore.DatabasePath(root);
        var clock = new Clock();
        try
        {
            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE quota_history (provider TEXT NOT NULL CHECK(provider IN ('codex','claude')), window TEXT NOT NULL CHECK(window IN ('five-hour','weekly')), percentage TEXT NOT NULL, reset_at INTEGER NOT NULL, retrieved_at INTEGER NOT NULL, source TEXT NOT NULL CHECK(source IN ('codex-local','claude-local','claude-remote')), PRIMARY KEY(provider, window, retrieved_at)); CREATE INDEX quota_history_retention ON quota_history(retrieved_at); PRAGMA user_version=1; INSERT INTO quota_history VALUES ('codex','five-hour','12',$reset,$at,'codex-local');";
                command.Parameters.AddWithValue("$reset", clock.UtcNow.AddHours(5).UtcTicks);
                command.Parameters.AddWithValue("$at", clock.UtcNow.AddMinutes(-1).UtcTicks);
                await command.ExecuteNonQueryAsync();
            }
            await using var store = new SqliteQuotaHistoryStore(path);
            // Parse by name so the tests-only commit compiles on the pre-fix enum.
            Assert.True(Enum.TryParse<QuotaHistorySource>("CodexRemote", out var remote), "CodexRemote source must exist.");
            await store.AppendAsync([new(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, 81,
                clock.UtcNow.AddHours(5), clock.UtcNow, remote)], clock.UtcNow, default);
            var rows = await store.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour,
                clock.UtcNow.AddHours(-1), clock.UtcNow, default);
            Assert.Equal(2, rows.Count);
            Assert.Equal(QuotaHistorySource.CodexLocal, rows[0].Source);
            Assert.Equal(remote, rows[1].Source);
            await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(
                [new(QuotaHistoryProvider.Claude, QuotaHistoryWindow.FiveHour, 30,
                    clock.UtcNow.AddHours(5), clock.UtcNow, remote)], clock.UtcNow, default).AsTask());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Provider(Clock clock, decimal percentage) : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) =>
            ValueTask.FromResult(new QuotaProviderResult(new(new(percentage, clock.UtcNow.AddHours(5)), null, clock.UtcNow), null));
    }
    private sealed class Snapshots : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult<QuotaSnapshot?>(null);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
