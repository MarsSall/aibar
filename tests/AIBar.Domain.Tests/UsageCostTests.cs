using AIBar.Domain;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class UsageCostTests
{
    private static readonly DateOnly Today = new(2026, 10, 19);
    private static UsageCostDaily Row(DateOnly day, string model = "gpt-5", long input = 1_000_000) => new(day, model, input, 0, 0, 0);

    [Fact]
    public void Ranges_are_today_and_30_calendar_days_inclusive_excluding_future()
    {
        var result = UsageCostPolicy.Calculate([Row(Today), Row(Today.AddDays(-29)), Row(Today.AddDays(-30)), Row(Today.AddDays(1))], Today);
        Assert.Equal(1_000_000L, result.Today.Tokens);
        Assert.Equal(2_000_000L, result.Last30Days.Tokens);
        Assert.Equal(1.25m, result.Today.Cost);
        Assert.Equal(2.5m, result.Last30Days.Cost);
    }

    [Fact]
    public void Mixed_pricing_keeps_known_subtotal_and_all_tokens_and_marks_partial()
    {
        var result = UsageCostPolicy.Calculate([Row(Today), Row(Today, "new-model")], Today);
        Assert.Equal(1.25m, result.Today.Cost);
        Assert.True(result.Today.Partial);
        Assert.Equal(2_000_000L, result.Today.Tokens);
        Assert.Contains("$1.25+", result.TodayLabel);
        Assert.Contains("some models unpriced", result.TodayLabel);
        var unknown = UsageCostPolicy.Calculate([Row(Today, "new-model")], Today);
        Assert.Null(unknown.Today.Cost);
        Assert.DoesNotContain("$0.00", unknown.TodayLabel);
    }

    [Fact]
    public void Claude_components_are_disjoint_and_cache_creation_has_its_own_rate()
    {
        var result = UsageCostPolicy.Calculate([new(Today, "claude-fable-5-1", 1_000_000, 1_000_000, 1_000_000, 1_000_000)], Today);
        Assert.Equal(72.75m, result.Today.Cost);
        Assert.Equal(4_000_000L, result.Today.Tokens);
    }

    [Fact]
    public void Presentation_is_invariant_and_compact()
    {
        Assert.Equal("Today: $0.04 · 15K tokens", new UsageCostSummary(new(15_000, .04m, false), new(218_000_000, 254.24m, false)).TodayLabel);
        Assert.Equal("Last 30 days: $254.24 · 218M tokens", new UsageCostSummary(new(15_000, .04m, false), new(218_000_000, 254.24m, false)).Last30DaysLabel);
        Assert.Contains("Unavailable", UsageCostSummary.Unavailable.TodayLabel);
    }

    [Fact]
    public void Empty_complete_data_is_zero_but_incomplete_empty_data_is_not()
    {
        Assert.Equal(0m, UsageCostPolicy.Calculate([], Today).Today.Cost);
        Assert.Null(UsageCostPolicy.Calculate([], Today, true).Today.Cost);
        Assert.True(UsageCostPolicy.Calculate([Row(Today)], Today, true).Today.Partial);
        Assert.Throws<ArgumentOutOfRangeException>(() => UsageCostPolicy.Calculate([Row(Today, input: -1)], Today));
    }

    [Fact]
    public void Daily_breakdown_zero_fills_exact_30_day_window_without_changing_totals_or_labels()
    {
        var result = UsageCostPolicy.Calculate([Row(Today), Row(Today.AddDays(-29)), Row(Today.AddDays(-30)), Row(Today.AddDays(1))], Today);
        Assert.Equal(30, result.Days.Count);
        Assert.Equal(Today.AddDays(-29), result.Days[0].Day);
        Assert.Equal(Today, result.Days[^1].Day);
        Assert.All(result.Days.Skip(1).SkipLast(1), day => Assert.Equal(new UsageCostDay(day.Day, 0, 0, false), day));
        Assert.Equal(result.Last30Days.Tokens, result.Days.Sum(day => day.Tokens));
        Assert.Equal(result.Last30Days.Cost, result.Days.Sum(day => day.Cost));
        Assert.Equal("Today: $1.25 · 1M tokens", result.TodayLabel);
        Assert.Equal("Last 30 days: $2.50 · 2M tokens", result.Last30DaysLabel);
        Assert.Empty(UsageCostSummary.Unavailable.Days);
    }

    [Fact]
    public void Daily_breakdown_preserves_unpriced_subtotals_and_incomplete_flags()
    {
        var result = UsageCostPolicy.Calculate([Row(Today), Row(Today, "unknown"), Row(Today.AddDays(-1), "unknown")], Today);
        Assert.Equal(new UsageCostDay(Today, 2_000_000, 1.25m, true, true), result.Days[^1]);
        Assert.Equal(new UsageCostDay(Today.AddDays(-1), 1_000_000, null, true, true), result.Days[^2]);
        var incomplete = UsageCostPolicy.Calculate([Row(Today)], Today, true);
        Assert.All(incomplete.Days, day => Assert.True(day.Partial));
        Assert.Equal(1.25m, incomplete.Days[^1].Cost);
        Assert.Null(incomplete.Days[0].Cost);
        Assert.Equal(incomplete.Today.Tokens, incomplete.Days[^1].Tokens);
    }

    [Fact]
    public async Task Codex_adapter_keeps_daily_rows_before_collapsing_model_totals()
    {
        var now = new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        var day = DateOnly.FromDateTime(now.LocalDateTime);
        var discovery = new SessionDiscoveryResult([new(["synthetic"])], new(1, 0, []));
        var adapter = new LocalCodexAnalyticsAdapter(_ => discovery,
            (_, _) => ValueTask.FromResult(new AnalyticsScanResult([], [], false)),
            _ => ValueTask.FromResult<IReadOnlyList<DailyUsage>>([
                new(day, "local", TimeSpan.Zero, "gpt-5", new(1_000_000, 0, 0)),
                new(day.AddDays(-30), "local", TimeSpan.Zero, "gpt-5", new(9_000_000, 0, 0))]), () => now);
        var result = await adapter.ScanAsync(default);
        Assert.Equal(10_000_000L, result.Totals!.Total);
        Assert.Equal(1_000_000L, result.UsageCost.Today.Tokens);
        Assert.Equal(1_000_000L, result.UsageCost.Last30Days.Tokens);
        Assert.Equal(result.UsageCost, result.WithStatus(AnalyticsScanStatus.Loading).UsageCost);
    }
}
