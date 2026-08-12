using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class BetaAnalyticsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-beta-analytics-{Guid.NewGuid():N}");

    [Fact]
    public async Task Composed_discovery_preserves_readable_aggregates_when_sources_become_unreadable_or_mutate()
    {
        var readable = Session("readable.jsonl", Line("gpt-5", 2, 3, 4)); var unreadable = Session("unreadable.jsonl", Line("gpt-4", 1, 0, 2));
        await using var firstStore = Store("unreadable");
        var first = await Adapter(firstStore, new(new(), "beta-v1", open: path => path == unreadable ? throw new UnauthorizedAccessException() : File.OpenRead(path))).ScanAsync(default);
        Assert.Equal((AnalyticsScanStatus.Partial, ScanCoverageState.Partial, new TokenTotals(2, 3, 4)), (first.Status, first.Coverage, first.Totals));
        Assert.Contains("session_file_unreadable", first.WarningCodes);

        File.Delete(unreadable); var mutating = Session("mutating.jsonl", Line("gpt-4", 1, 0, 2));
        await using var secondStore = Store("mutating");
        var second = await Adapter(secondStore, new(new(), "beta-v1", beforeStable: path => { if (path == mutating) File.AppendAllText(path, "\n"); })).ScanAsync(default);
        Assert.Equal((AnalyticsScanStatus.Partial, ScanCoverageState.Partial, new TokenTotals(2, 3, 4)), (second.Status, second.Coverage, second.Totals));
        Assert.Contains("session_file_changed", second.WarningCodes);
    }

    [Fact]
    public async Task Complete_and_empty_scans_report_factual_local_totals_models_and_time()
    {
        Session("complete.jsonl", Line("gpt-5", 2, 3, 4) + Line("gpt-4", 3, 3, 6));
        Directory.CreateDirectory(Path.Combine(_root, "archived_sessions"));
        await using var completeStore = Store("complete");
        var complete = await Adapter(completeStore, new(new(), "beta-v1")).ScanAsync(default);

        Assert.True(complete.WarningCodes.Count == 0, string.Join(",", complete.WarningCodes));
        Assert.Equal((AnalyticsScanStatus.Complete, ScanCoverageState.Complete, new TokenTotals(3, 3, 6), Now), (complete.Status, complete.Coverage, complete.Totals, complete.ScannedAt));
        Assert.Equal([new("gpt-4", new TokenTotals(1, 0, 2)), new("gpt-5", new TokenTotals(2, 3, 4))], complete.Models);
        Assert.Equal("Local Codex data", complete.SourceLabel);
        Assert.Equal("Source: local Codex CLI session data", complete.SourceDetail);

        var emptyRoot = Path.Combine(_root, "empty-root");
        var emptyPath = Path.Combine(emptyRoot, "sessions", "empty.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(emptyPath)!);
        Directory.CreateDirectory(Path.Combine(emptyRoot, "archived_sessions"));
        File.WriteAllText(emptyPath, string.Empty);
        await using var store = Store("empty");
        var empty = await Adapter(store, new(new(), "beta-v1"), emptyRoot).ScanAsync(default);
        Assert.Equal((AnalyticsScanStatus.Empty, ScanCoverageState.Unavailable, Now), (empty.Status, empty.Coverage, empty.ScannedAt));
        Assert.Null(empty.Totals);
        Assert.Empty(empty.Models);
        Assert.NotEmpty(empty.WarningCodes);
    }

    [Fact]
    public async Task Partial_and_unavailable_results_always_snapshot_safe_warnings()
    {
        var warnings = new List<string>();
        var models = new List<ModelTokenTotal> { new("gpt-5", new(2, 0, 1)) };
        var partial = LocalAnalyticsState.Partial(new(2, 0, 1), models, Now, warnings);
        warnings.Add("changed-after-result");
        models.Clear();
        Assert.Equal(ScanCoverageState.Partial, partial.Coverage);
        Assert.NotEmpty(partial.WarningCodes);
        Assert.Single(partial.Models);

        await using var store = Store("unavailable");
        var unavailable = await Adapter(store, new(new(), "beta-v1")).ScanAsync(default);
        Assert.Equal((AnalyticsScanStatus.Unavailable, ScanCoverageState.Unavailable), (unavailable.Status, unavailable.Coverage));
        Assert.NotEmpty(unavailable.WarningCodes);
    }

    [Fact]
    public async Task Scan_honors_cancellation_without_promoting_a_result()
    {
        await using var store = Store("cancelled");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Adapter(store, new(new(), "beta-v1")).ScanAsync(cancelled.Token).AsTask());
    }

    private LocalCodexAnalyticsAdapter Adapter(SqliteDailyModelUsageStore store, SessionJsonlScanner scanner, string? root = null) => new(new SessionFileDiscovery(root ?? _root, _root, 50), new AnalyticsScanCoordinator(scanner, store, new(new TimeZoneLocalDayPolicy(TimeZoneInfo.Utc, "beta-v1"))), store, () => Now);
    private SqliteDailyModelUsageStore Store(string name) { Directory.CreateDirectory(_root); return new(Path.Combine(_root, $"{name}.db")); }
    private string Session(string name, string content) { var path = Path.Combine(_root, "sessions", name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path; }
    private static string Line(string model, long input, long cached, long output) => $"{{\"timestamp\":\"2030-01-02T03:04:05Z\",\"model\":\"{model}\",\"usage\":{{\"input_tokens\":{input},\"cached_input_tokens\":{cached},\"output_tokens\":{output}}}}}\n";
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
