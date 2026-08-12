using System.Security.Cryptography;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Data.Sqlite;

namespace AIBar.Domain.Tests;

public sealed class Slice6AggregationIntegrationTests : IDisposable
{
    private const string SyntheticFixtureHash = "595C839F46D2512C5C7A0A50AA0FFC3DE8DFCBFE1ACF70ED32650EBB0366DFBB";
    private const string PromptSentinel = "prompt-sentinel-6d";
    private const string ResponseSentinel = "response-sentinel-6d";
    private const string SecretSentinel = "secret-sentinel-6d";
    private const string RawPathSentinel = "C:\\synthetic\\private\\session.jsonl";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-slice6d-{Guid.NewGuid():N}");
    private readonly string _source;
    private readonly string _database;

    public Slice6AggregationIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        _source = Path.Combine(_root, "synthetic-session.jsonl");
        _database = Path.Combine(_root, "aibar.db");
    }

    [Fact]
    public async Task Synthetic_scan_preserves_source_bytes_and_aggregates_unknown_resets_and_ranking()
    {
        await File.WriteAllTextAsync(_source, string.Join("\n",
            Line("2030-03-10T07:30:00Z", "gpt-5", 10, 2, 3),
            Line("2030-03-10T07:31:00Z", null, 15, 4, 6),
            Line("2030-03-10T07:32:00Z", "gpt-5", 1, 1, 1)) + "\n");
        var before = Hash(_source);

        await using var store = new SqliteDailyModelUsageStore(_database);
        var coordinator = Coordinator(store, TimeZoneInfo.Utc);
        await coordinator.ScanAsync(_source, default);
        await coordinator.ScanAsync(_source, default);

        var usage = await store.LoadAsync(default);
        Assert.Equal(SyntheticFixtureHash, before);
        Assert.Equal(SyntheticFixtureHash, Hash(_source));
        Assert.Equal("gpt-5", AnalyticsPolicy.MostUsedModel(usage));
        Assert.Contains(usage, item => item.Model == "gpt-5" && item.Tokens == new TokenTotals(10, 2, 3));
        Assert.Contains(usage, item => item.Model == AnalyticsPolicy.UnknownModel && item.Tokens == new TokenTotals(5, 2, 3));
    }

    [Fact]
    public async Task Crash_before_commit_retains_nothing_then_retry_and_partial_tail_retain_committed_usage()
    {
        await File.WriteAllTextAsync(_source, Line("2030-01-01T00:00:00Z", "gpt-5", 2, 0, 0) + "\n");
        await using (var failing = new SqliteDailyModelUsageStore(_database, () => throw new InvalidOperationException("synthetic crash")))
        {
            var coordinator = Coordinator(failing, TimeZoneInfo.Utc);
            await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ScanAsync(_source, default).AsTask());
            Assert.Empty(await failing.LoadAsync(default));
            Assert.Null(await failing.LoadCheckpointAsync(coordinator.SourceFingerprint(_source), default));
        }

        await using var stable = new SqliteDailyModelUsageStore(_database);
        var retry = Coordinator(stable, TimeZoneInfo.Utc);
        await retry.ScanAsync(_source, default);
        await File.AppendAllTextAsync(_source, Line("2030-01-01T00:01:00Z", "gpt-5", 5, 1, 2) + "\n" + "{\"incomplete\":");
        var partial = await retry.ScanAsync(_source, default);

        Assert.Contains("session_incomplete_tail", partial.WarningCodes);
        Assert.Equal(new TokenTotals(5, 1, 2), Assert.Single(await stable.LoadAsync(default)).Tokens);
    }

    [Fact]
    public void Time_policy_assigns_midnight_and_dst_events_with_explicit_provenance()
    {
        var policy = new TimeZoneLocalDayPolicy(TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"), "slice-6d");

        var beforeMidnight = policy.Assign(DateTimeOffset.Parse("2030-03-10T04:30:00Z"));
        var afterDst = policy.Assign(DateTimeOffset.Parse("2030-03-10T07:30:00Z"));

        Assert.Equal(new DateOnly(2030, 3, 9), beforeMidnight.LocalDay);
        Assert.Equal(new DateOnly(2030, 3, 10), afterDst.LocalDay);
        Assert.Equal("Eastern Standard Time", afterDst.TimeZoneId);
        Assert.Equal("slice-6d", afterDst.PolicyVersion);
    }

    [Fact]
    public async Task Scan_persists_only_hashed_fingerprint_and_token_facts_not_synthetic_sensitive_fields_or_paths()
    {
        var fixture = JsonSerializer.Serialize(new
        {
            timestamp = "2030-01-02T03:04:05Z",
            model = "gpt-5",
            usage = new { input_tokens = 7, cached_input_tokens = 8, output_tokens = 9 },
            prompt = PromptSentinel,
            response = ResponseSentinel,
            access_token = SecretSentinel,
            source_path = RawPathSentinel
        });
        await File.WriteAllTextAsync(_source, fixture + "\n");
        var before = Hash(_source);

        await using var store = new SqliteDailyModelUsageStore(_database);
        var coordinator = Coordinator(store, TimeZoneInfo.Utc);
        var fingerprint = coordinator.SourceFingerprint(_source);
        await coordinator.ScanAsync(_source, default);

        Assert.NotEqual(Path.GetFullPath(_source), fingerprint);
        Assert.NotNull(await store.LoadCheckpointAsync(fingerprint, default));
        Assert.Equal(new TokenTotals(7, 8, 9), Assert.Single(await store.LoadContributionAsync(fingerprint, default)).Tokens);
        var persisted = await PersistedValuesAsync(_database);
        Assert.Contains(fingerprint, persisted);
        foreach (var forbidden in new[] { PromptSentinel, ResponseSentinel, SecretSentinel, RawPathSentinel, Path.GetFullPath(_source) })
            Assert.False(persisted.Contains(forbidden, StringComparison.Ordinal));
        Assert.Equal(before, Hash(_source));
    }

    private static AnalyticsScanCoordinator Coordinator(SqliteDailyModelUsageStore store, TimeZoneInfo timeZone) => new(
        new SessionJsonlScanner(new SessionCheckpointStore(), "v1"),
        store,
        new AnalyticsPolicy(new TimeZoneLocalDayPolicy(timeZone, "v1")));

    private static string Line(string timestamp, string? model, long input, long cached, long output) =>
        $"{{\"timestamp\":\"{timestamp}\",{(model is null ? string.Empty : $"\"model\":\"{model}\",")}\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":{cached},\"output_tokens\":{output}}}}}";

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<string> PersistedValuesAsync(string database)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        await using var names = await tables.ExecuteReaderAsync();
        var tableNames = new List<string>();
        while (await names.ReadAsync()) tableNames.Add(names.GetString(0));
        var values = new List<string>();
        foreach (var table in tableNames)
        {
            await using var rows = connection.CreateCommand();
            rows.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\";";
            await using var reader = await rows.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                for (var index = 0; index < reader.FieldCount; index++) values.Add(Convert.ToString(reader.GetValue(index)) ?? string.Empty);
        }
        return string.Join("\n", values);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
