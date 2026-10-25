using System.Globalization;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class DataSourceCostTests
{
    [Theory]
    [InlineData(DataSource.Automatic, 3_000_000L, 2_000_000L)]
    [InlineData(DataSource.ThisPc, 1_000_000L, null)]
    [InlineData(DataSource.OtherMachine, 2_000_000L, 2_000_000L)]
    public async Task Public_cost_views_follow_source_and_refresh_on_switch_without_a_quota_poll(DataSource source, long codexTokens, long? claudeTokens)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-source-cost-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
        await settings.SaveAsync(true, default);
        await settings.SaveClaudeAsync(true, default);
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        await using (var store = new SqliteDailyModelUsageStore(Path.Combine(root, "analytics.db")))
            await store.SaveAsync([new(today, "synthetic", TimeSpan.Zero, "gpt-5", new(1_000_000, 0, 0))], default);
        await WriteRemoteAsync("codex-remote-tokens.json", "gpt-5");
        await WriteRemoteAsync("claude-remote-tokens.json", "claude-sonnet-4");
        var composition = App.CreateComposition(new(root, Path.Combine(root, "codex"), UserProfile: Path.Combine(root, "home"),
            ClaudeProviderFactory: _ => new UnavailableProvider(), ClaudeRemoteProviderFactory: _ => new UnavailableProvider()));
        try
        {
            var view = Assert.IsType<BetaAnalyticsPresentation>(composition.Presentation);
            await composition.Initialize();
            await WaitForAsync(() => view.CodexCost.Today.Tokens == 3_000_000);
            AssertTotals(view.CodexCost, "gpt-5", 3_000_000);
            AssertTotals(view.ClaudeCost!.State, "claude-sonnet-4", 2_000_000);
            await SettingsProviderCommands.ApplySourceAsync(source, composition.CodexQuota!, composition.ClaudeQuota);
            // No Reevaluate/Refresh call: changing the source itself must refresh both views.
            await WaitForAsync(() => view.CodexCost.Today.Tokens == codexTokens && view.ClaudeCost.State.Today.Tokens == claudeTokens);
            AssertTotals(view.CodexCost, "gpt-5", codexTokens);
            AssertTotals(view.ClaudeCost.State, "claude-sonnet-4", claudeTokens);
            await SettingsProviderCommands.ApplySourceAsync(DataSource.Automatic, composition.CodexQuota!, composition.ClaudeQuota);
            await WaitForAsync(() => view.CodexCost.Today.Tokens == 3_000_000 && view.ClaudeCost.State.Today.Tokens == 2_000_000);
        }
        finally
        {
            await composition.Resource.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
        async Task WriteRemoteAsync(string file, string model)
        {
            var json = JsonSerializer.Serialize(new
            {
                schemaVersion = 1, generatedAt = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                localDay = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), state = "current",
                days = new[] { new { day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), model, input = 2_000_000, cachedInput = 0, cacheWrite = 0, output = 0 } }
            });
            await File.WriteAllTextAsync(Path.Combine(root, file), json);
        }
        void AssertTotals(UsageCostSummary actual, string model, long? tokens)
        {
            if (tokens is null) { Assert.Equal(UsageCostSummary.Unavailable, actual); return; }
            var expected = UsageCostPolicy.Calculate([new(today, model, tokens.Value, 0, 0, 0)], today, false);
            Assert.Equal(expected.Today.Tokens, actual.Today.Tokens);
            Assert.Equal(expected.Today.Cost, actual.Today.Cost);
            Assert.Equal(expected.Last30Days.Tokens, actual.Last30Days.Tokens);
            Assert.Equal(expected.Last30Days.Cost, actual.Last30Days.Cost);
        }
    }

    private static async Task WaitForAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!ready()) await Task.Delay(10, timeout.Token);
        Assert.True(ready());
    }
    private sealed class UnavailableProvider : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) =>
            ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "synthetic")));
    }
}
