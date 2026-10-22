using System.Text;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class CodexRemoteTokensTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aibar-codex-tokens-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(_root, "codex-remote-tokens.json");
    private const string Row = "{\"day\":\"2026-10-19\",\"model\":\"gpt-5\",\"input\":10,\"cachedInput\":20,\"cacheWrite\":0,\"output\":30}";
    private static byte[] Payload(string state = "current", string rows = Row) => Encoding.UTF8.GetBytes(
        "{\"schemaVersion\":1,\"generatedAt\":\"2026-10-19T12:00:00Z\",\"localDay\":\"2026-10-19\",\"state\":\"" + state + "\",\"days\":[" + rows + "]}");
    private static LocalAnalyticsState Local(IReadOnlyList<UsageCostDaily> rows, bool incomplete = false) => new(
        incomplete ? AnalyticsScanStatus.Partial : AnalyticsScanStatus.Complete,
        incomplete ? ScanCoverageState.Partial : ScanCoverageState.Complete,
        null, [], Now, [], UsageCostPolicy.Calculate(rows, Today, incomplete), rows, incomplete);
    private static void SameSummary(UsageCostSummary expected, UsageCostSummary actual)
    {
        Assert.Equal(expected.Today, actual.Today);
        Assert.Equal(expected.Last30Days, actual.Last30Days);
        Assert.Equal(expected.Days.ToArray(), actual.Days.ToArray());
    }

    [Theory] // S2: the Codex filename uses the same strict schema and states as Claude.
    [InlineData("current", true, false)]
    [InlineData("partial", true, true)]
    [InlineData("unavailable", false, false)]
    public async Task Codex_file_uses_shared_parser(string state, bool available, bool partial)
    {
        Directory.CreateDirectory(_root);
        var payload = Payload(state);
        await File.WriteAllBytesAsync(PathName, payload);
        var parsed = await RemoteTokens.ReadRowsAsync(PathName, Now);
        Assert.Equal(available, parsed.Available);
        Assert.Equal(partial, parsed.Partial);
        SameSummary(ClaudeRemoteTokens.Parse(payload, Now), parsed.Summary);
        if (available)
        {
            Assert.Equal(Today, parsed.Today);
            Assert.Equal(new UsageCostDaily(Today, "gpt-5", 10, 20, 0, 30), Assert.Single(parsed.Rows));
        }
        else Assert.Empty(parsed.Rows);
    }

    [Theory] // S2: malformed, duplicate, stale, unexpected, and overflowing files fail closed.
    [InlineData("malformed")]
    [InlineData("duplicate")]
    [InlineData("stale")]
    [InlineData("unexpected")]
    [InlineData("overflow")]
    public async Task Invalid_codex_file_is_unavailable(string kind)
    {
        var bytes = kind switch
        {
            "malformed" => Encoding.UTF8.GetBytes("not json"),
            "duplicate" => Payload(rows: Row + "," + Row),
            "stale" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Payload()).Replace("T12:00:00Z", "T09:59:59Z")),
            "unexpected" => Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Payload()).Replace("\"schemaVersion\"", "\"extra\":0,\"schemaVersion\"")),
            _ => Payload(rows: Row.Replace("\"input\":10", "\"input\":9223372036854775807"))
        };
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(PathName, bytes);
        Assert.False((await RemoteTokens.ReadRowsAsync(PathName, Now)).Available);
    }

    [Fact] // S2: duplicate day/model components sum; distinct days and models retain their own prices.
    public void Combines_overlapping_and_distinct_rows_without_losing_components()
    {
        UsageCostDaily[] localRows = [new(Today, "gpt-5", 100, 200, 0, 300), new(Today.AddDays(-1), "gpt-5", 5, 0, 0, 0)];
        UsageCostDaily[] remoteRows = [new(Today, "gpt-5", 10, 20, 0, 30), new(Today, "claude-fable-5-1", 1, 2, 3, 4), new(Today.AddDays(-2), "gpt-5", 7, 0, 0, 0)];
        var combined = RemoteTokens.Combine(Local(localRows), new(remoteRows, Today, false, true), Today);
        var expected = UsageCostPolicy.Calculate([
            new(Today, "gpt-5", 110, 220, 0, 330), remoteRows[1], localRows[1], remoteRows[2]], Today);
        SameSummary(expected, combined);
        Assert.Equal(670L, combined.Today.Tokens);
        Assert.Equal(682L, combined.Last30Days.Tokens);
    }

    [Fact] // S2: no remote must preserve the exact existing local summary, including unavailable/partial states.
    public async Task Missing_or_invalid_remote_returns_identical_local_summary()
    {
        var local = Local([new(Today, "gpt-5", 10, 0, 0, 0)], true);
        var absent = await RemoteTokens.ReadRowsAsync(PathName, Now);
        Assert.False(absent.Available);
        Assert.Same(local.UsageCost, RemoteTokens.Combine(local, absent, Today));
        Assert.Same(local.UsageCost, RemoteTokens.Combine(local, RemoteTokens.ParseRows(Encoding.UTF8.GetBytes("{}"), Now), Today));
        var unavailable = LocalAnalyticsState.Unavailable(Now, []);
        Assert.Same(unavailable.UsageCost, RemoteTokens.Combine(unavailable, absent, Today));
    }

    [Theory] // S2: usable remote data stands alone even if no local CLI sessions exist.
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_or_unavailable_local_with_remote_equals_remote(bool unavailable)
    {
        var local = unavailable ? LocalAnalyticsState.Unavailable(Now, []) : Local([]);
        var remote = RemoteTokens.ParseRows(Payload(), Now);
        SameSummary(remote.Summary, RemoteTokens.Combine(local, remote, Today));
    }

    [Theory] // S2: either incomplete source marks the entire combined window incomplete.
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Either_partial_source_marks_combination_incomplete(bool localPartial, bool remotePartial)
    {
        var remote = RemoteTokens.ParseRows(Payload(remotePartial ? "partial" : "current"), Now);
        var combined = RemoteTokens.Combine(Local([new(Today, "gpt-5", 10, 0, 0, 0)], localPartial), remote, Today);
        Assert.True(combined.Today.Partial);
        Assert.True(combined.Last30Days.Partial);
        Assert.All(combined.Days, day => Assert.True(day.Partial));
    }

    [Fact] // Unpriced is not incomplete: an old unknown local model must not taint priced days.
    public void Unknown_local_model_does_not_mark_other_days_incomplete()
    {
        var local = Local([new(Today.AddDays(-20), "unknown", 10, 0, 0, 0)]);
        var combined = RemoteTokens.Combine(local, RemoteTokens.ParseRows(Payload(), Now), Today);
        Assert.False(combined.Today.Partial);
        Assert.True(combined.Last30Days.Unpriced);
    }

    [Fact] // Different source time zones retain assigned dates; Today is the Windows presentation day.
    public void Combined_window_uses_windows_today_not_remote_today()
    {
        var remote = new RemoteTokenRows([new(Today.AddDays(-1), "gpt-5", 10, 0, 0, 0)], Today.AddDays(-1), false, true);
        var combined = RemoteTokens.Combine(Local([new(Today, "gpt-5", 20, 0, 0, 0)]), remote, Today);
        Assert.Equal(20L, combined.Today.Tokens);
        Assert.Equal(30L, combined.Last30Days.Tokens);
    }

    [Fact] // A sum overflowing Int64 fails back to the exact safe local result.
    public void Combined_overflow_does_not_replace_local_summary()
    {
        var local = Local([new(Today, "gpt-5", long.MaxValue, 0, 0, 0)]);
        Assert.Same(local.UsageCost, RemoteTokens.Combine(local, RemoteTokens.ParseRows(Payload(), Now), Today));
    }

    [Fact] // S2: disabled Codex never reads aggregates; refresh and clear cannot retain its old summary.
    public async Task Codex_view_respects_enabled_setting_and_clear()
    {
        var enabled = true;
        var reads = 0;
        var remote = RemoteTokens.ParseRows(Payload(), Now);
        await using var view = new UsageCostView(() => enabled, _ =>
        {
            reads++;
            return ValueTask.FromResult(RemoteTokens.Combine(Local([]), remote, Today));
        });
        await view.RefreshAsync(default);
        Assert.Equal(60L, view.State.Today.Tokens);
        enabled = false;
        await view.RefreshAsync(default);
        Assert.Same(UsageCostSummary.Unavailable, view.State);
        Assert.Equal(1, reads);
        enabled = true;
        await view.RefreshAsync(default);
        await view.CancelAndWaitAsync(default);
        Assert.Same(UsageCostSummary.Unavailable, view.State);
        await view.RefreshAsync(default);
        Assert.Equal(2, reads);
        view.ResumeAfterClear();
        await view.RefreshAsync(default);
        Assert.Equal(3, reads);
    }

    [Fact] // S2: the scanner exposes exactly the rows used for cost, even across loading transitions.
    public async Task Local_adapter_exposes_rows_and_incomplete_flag()
    {
        var day = DateOnly.FromDateTime(Now.LocalDateTime);
        var adapter = new LocalCodexAnalyticsAdapter(_ => new SessionDiscoveryResult([new(["synthetic"])], new(1, 0, ["session_file_unreadable"])),
            (_, _) => ValueTask.FromResult(new AnalyticsScanResult([], [], false)),
            _ => ValueTask.FromResult<IReadOnlyList<DailyUsage>>([new(day, "local", TimeSpan.Zero, "gpt-5", new(10, 20, 30))]), () => Now);
        var state = await adapter.ScanAsync(default);
        Assert.Equal(new UsageCostDaily(day, "gpt-5", 10, 20, 0, 30), Assert.Single(state.UsageCostRows));
        Assert.True(state.UsageCostIncomplete);
        var loading = state.WithStatus(AnalyticsScanStatus.Loading);
        Assert.Equal(state.UsageCostRows.ToArray(), loading.UsageCostRows.ToArray());
        Assert.True(loading.UsageCostIncomplete);
    }

    [Fact] // S2: production wiring reads on enable/open and hides on disable; clear owns the file.
    public async Task Composition_refreshes_codex_remote_cost_and_respects_provider_setting()
    {
        var data = Path.Combine(_root, "AIBar");
        Directory.CreateDirectory(data);
        var path = Path.Combine(data, "codex-remote-tokens.json");
        async Task Publish(long input)
        {
            var now = DateTimeOffset.UtcNow;
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                generatedAt = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                localDay = DateOnly.FromDateTime(now.LocalDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                state = "current",
                days = new[] { new { day = DateOnly.FromDateTime(now.LocalDateTime).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), model = "gpt-5", input, cachedInput = 0, cacheWrite = 0, output = 0 } }
            });
            await File.WriteAllBytesAsync(path, bytes);
        }
        await Publish(100);
        var composition = App.CreateComposition(new(data, Path.Combine(_root, "codex"), UserProfile: Path.Combine(_root, "home")));
        try
        {
            var presentation = Assert.IsType<BetaAnalyticsPresentation>(composition.Presentation);
            await composition.Initialize();
            Assert.Same(UsageCostSummary.Unavailable, presentation.CodexCost);
            await composition.Settings!.EnablePrivateIntegrationAsync(default);
            Assert.Equal(100L, presentation.CodexCost.Today.Tokens);
            await Publish(200);
            await composition.Reevaluate!(RefreshTrigger.PopoverOpened, default);
            Assert.Equal(200L, presentation.CodexCost.Today.Tokens);
            await composition.Settings.DisablePrivateIntegrationAsync(default);
            Assert.Same(UsageCostSummary.Unavailable, presentation.CodexCost);
            Assert.True(File.Exists(path));
            await composition.Settings.ClearAiBarDataAsync(default);
            Assert.False(File.Exists(path));
            Assert.Same(UsageCostSummary.Unavailable, presentation.CodexCost);
        }
        finally { await composition.Resource.DisposeAsync(); }
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
