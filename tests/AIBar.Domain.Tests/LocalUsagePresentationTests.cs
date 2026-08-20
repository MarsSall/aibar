using System.Numerics;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class LocalUsagePresentationTests
{
    [Fact]
    public void Aggregates_each_scope_once_across_days_and_models_without_exposing_source_text()
    {
        var facts = new[]
        {
            Fact(UsageProjectionScope.OpenCode, 1, "should-not-display-model-a", 1, 2, 3, 4, 5),
            Fact(UsageProjectionScope.OpenCode, 2, "should-not-display-model-b", 10, 20, 30, 40, 50),
            Fact(UsageProjectionScope.Pi, 1, "should-not-display-model-c", 100, 200, 300, 400, 500),
            Fact(UsageProjectionScope.Combined, 1, "should-not-display-model-a", 1, 2, 3, 4, 5),
            Fact(UsageProjectionScope.Combined, 2, "should-not-display-model-b", 10, 20, 30, 40, 50),
            Fact(UsageProjectionScope.Combined, 1, "should-not-display-model-c", 100, 200, 300, 400, 500)
        };

        var state = new LocalUsagePresentationMapper().Map(Result(LocalUsageProjectionStatus.Completed, facts));

        var openCode = Row(state, UsageProjectionScope.OpenCode);
        var pi = Row(state, UsageProjectionScope.Pi);
        var combined = Row(state, UsageProjectionScope.Combined);
        Assert.Equal(new BigInteger(165), openCode.Total);
        Assert.Equal("Input 11 · Cache 55 (read 22, write 33) · Output 44 · Reasoning 55", openCode.TokenBreakdownLabel);
        Assert.Equal(new BigInteger(1500), pi.Total);
        Assert.Equal("Input 100 · Cache 500 (read 200, write 300) · Output 400 · Reasoning 500", pi.TokenBreakdownLabel);
        Assert.Equal(new BigInteger(1665), combined.Total);
        Assert.Equal("Input 111 · Cache 555 (read 222, write 333) · Output 444 · Reasoning 555", combined.TokenBreakdownLabel);
        Assert.NotEqual(openCode.Total + pi.Total + combined.Total, combined.Total);
        Assert.Equal("OpenCode, 165 retained tokens, Input 11 · Cache 55 (read 22, write 33) · Output 44 · Reasoning 55, Retained history", openCode.AutomationLabel);
        var visible = string.Join(" ", state.Rows.Select(row => row.AutomationLabel));
        Assert.DoesNotContain("should-not-display", visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Groups_scope_details_newest_day_then_ordinal_model_with_exact_safe_labels()
    {
        var longModel = new string('x', 90) + "\nnot-visible";
        var state = new LocalUsagePresentationMapper().Map(Result(LocalUsageProjectionStatus.Completed,
        [
            Fact(UsageProjectionScope.OpenCode, 1, "zeta", 1, 2, 3, 4, 5),
            Fact(UsageProjectionScope.OpenCode, 2, "beta", 10, 20, 30, 40, 50),
            Fact(UsageProjectionScope.OpenCode, 2, "alpha", 100, 200, 300, 400, 500),
            Fact(UsageProjectionScope.Pi, 1, "\r\n\t", 7, 0, 0, 0, 0),
            Fact(UsageProjectionScope.Combined, 1, longModel, 8, 0, 0, 0, 0)
        ]));

        var openCode = Row(state, UsageProjectionScope.OpenCode);
        Assert.Equal(new[] { "alpha", "beta", "zeta" }, openCode.Details.Select(detail => detail.ModelLabel));
        var alpha = openCode.Details[0];
        Assert.Equal(new DateOnly(2030, 1, 2), alpha.LocalDay);
        Assert.Equal("2030-01-02", alpha.LocalDayLabel);
        Assert.Equal(new BigInteger(1500), alpha.Total);
        Assert.Equal("1500 retained tokens", alpha.TotalLabel);
        Assert.Equal("Input 100 · Cache 500 (read 200, write 300) · Output 400 · Reasoning 500", alpha.TokenBreakdownLabel);
        Assert.Equal("2030-01-02, alpha, 1500 retained tokens, Input 100 · Cache 500 (read 200, write 300) · Output 400 · Reasoning 500", alpha.AutomationLabel);
        Assert.Equal("Unknown", Assert.Single(Row(state, UsageProjectionScope.Pi).Details).ModelLabel);
        var bounded = Assert.Single(Row(state, UsageProjectionScope.Combined).Details).ModelLabel;
        Assert.Equal(80, bounded.Length); Assert.All(bounded, character => Assert.False(char.IsControl(character)));
        Assert.DoesNotContain("alpha", openCode.AutomationLabel, StringComparison.Ordinal);
    }

    [Fact]
    public void Uses_overflow_safe_exact_totals_for_every_bucket()
    {
        var facts = new[]
        {
            Fact(UsageProjectionScope.OpenCode, 1, "a", long.MaxValue, 0, 0, 0, 0),
            Fact(UsageProjectionScope.OpenCode, 2, "b", long.MaxValue - 1, 0, 0, 0, 0),
            Fact(UsageProjectionScope.OpenCode, 3, "c", 0, long.MaxValue, 0, 0, 0),
            Fact(UsageProjectionScope.OpenCode, 4, "d", 0, long.MaxValue - 2, 0, 0, 0),
            Fact(UsageProjectionScope.OpenCode, 5, "e", 0, 0, long.MaxValue, 0, 0),
            Fact(UsageProjectionScope.OpenCode, 6, "f", 0, 0, long.MaxValue - 3, 0, 0),
            Fact(UsageProjectionScope.OpenCode, 7, "g", 0, 0, 0, long.MaxValue, 0),
            Fact(UsageProjectionScope.OpenCode, 8, "h", 0, 0, 0, long.MaxValue - 4, 0),
            Fact(UsageProjectionScope.OpenCode, 9, "i", 0, 0, 0, 0, long.MaxValue),
            Fact(UsageProjectionScope.OpenCode, 10, "j", 0, 0, 0, 0, long.MaxValue - 5)
        };

        var state = new LocalUsagePresentationMapper().Map(Result(LocalUsageProjectionStatus.Completed, facts));

        var max = (BigInteger)long.MaxValue;
        var row = Row(state, UsageProjectionScope.OpenCode);
        Assert.Equal(max * 10 - 15, row.Total);
        Assert.Equal($"Input {max * 2 - 1} · Cache {max * 4 - 5} (read {max * 2 - 2}, write {max * 2 - 3}) · Output {max * 2 - 4} · Reasoning {max * 2 - 5}", row.TokenBreakdownLabel);
    }

    [Fact]
    public void Maps_loaded_disabled_empty_partial_problem_and_unavailable_states_truthfully()
    {
        var mapper = new LocalUsagePresentationMapper();
        var retained = mapper.Map(Result(LocalUsageProjectionStatus.Completed,
            [Fact(UsageProjectionScope.OpenCode, 1, "model", 10, 0, 0, 0, 0), Fact(UsageProjectionScope.Combined, 1, "model", 10, 0, 0, 0, 0)],
            LocalUsageSourceStatus.Disabled, LocalUsageSourceStatus.Completed));
        Assert.Equal("Reading off", Row(retained, UsageProjectionScope.OpenCode).StatusLabel);
        Assert.Equal(new BigInteger(10), Row(retained, UsageProjectionScope.OpenCode).Total);
        Assert.Single(Row(retained, UsageProjectionScope.OpenCode).Details);

        var skipped = mapper.Map(Result(LocalUsageProjectionStatus.Skipped, [], LocalUsageSourceStatus.Disabled, LocalUsageSourceStatus.Disabled));
        Assert.Equal("Retained history was not loaded", skipped.StatusLabel);
        Assert.All(skipped.Rows, row => { Assert.Null(row.Total); Assert.Null(row.TokenBreakdownLabel); Assert.Empty(row.Details); Assert.DoesNotContain("Input ", row.AutomationLabel, StringComparison.Ordinal); });

        var empty = mapper.Map(Result(LocalUsageProjectionStatus.Completed, []));
        Assert.Equal("No retained usage facts", empty.StatusLabel);
        Assert.All(empty.Rows, row => { Assert.Null(row.TokenBreakdownLabel); Assert.Empty(row.Details); });

        var partial = mapper.Map(Result(LocalUsageProjectionStatus.Completed,
            [Fact(UsageProjectionScope.Pi, 1, "model", 5, 0, 0, 0, 0)], pi: LocalUsageSourceStatus.Partial));
        Assert.Equal("Partial refresh", Row(partial, UsageProjectionScope.Pi).StatusLabel);
        Assert.Equal(new BigInteger(5), Row(partial, UsageProjectionScope.Pi).Total);
        Assert.Single(Row(partial, UsageProjectionScope.Pi).Details);

        foreach (var problem in new[]
        {
            (LocalUsageSourceStatus.Unavailable, "Source unavailable"),
            (LocalUsageSourceStatus.Failed, "Refresh failed"),
            (LocalUsageSourceStatus.RebuildRequired, "Rebuild required")
        })
        {
            var state = mapper.Map(Result(LocalUsageProjectionStatus.Completed,
                [Fact(UsageProjectionScope.OpenCode, 1, "model", 7, 0, 0, 0, 0)], problem.Item1));
            Assert.Equal(new BigInteger(7), Row(state, UsageProjectionScope.OpenCode).Total);
            Assert.Equal(problem.Item2, Row(state, UsageProjectionScope.OpenCode).StatusLabel);
            Assert.Single(Row(state, UsageProjectionScope.OpenCode).Details);
        }

        var unavailable = mapper.Map(Result(LocalUsageProjectionStatus.Unavailable,
            [Fact(UsageProjectionScope.OpenCode, 1, "ignored", 99, 0, 0, 0, 0)]));
        Assert.Equal("Retained totals unavailable", unavailable.StatusLabel);
        Assert.All(unavailable.Rows, row => { Assert.Null(row.Total); Assert.Null(row.TokenBreakdownLabel); Assert.Empty(row.Details); });

        var notLoaded = mapper.NotLoaded();
        Assert.All(notLoaded.Rows, row => { Assert.Null(row.TokenBreakdownLabel); Assert.Empty(row.Details); });
    }

    [Fact]
    public async Task Host_publishes_startup_policy_and_refresh_while_preserving_factual_totals_during_refresh()
    {
        var startup = Result(LocalUsageProjectionStatus.Completed, [Fact(UsageProjectionScope.OpenCode, 1, "a", 1, 0, 0, 0, 0)]);
        var policy = Result(LocalUsageProjectionStatus.Completed, [Fact(UsageProjectionScope.OpenCode, 1, "b", 2, 0, 0, 0, 0)], LocalUsageSourceStatus.Partial);
        var refreshed = Result(LocalUsageProjectionStatus.Completed, [Fact(UsageProjectionScope.OpenCode, 1, "c", 3, 0, 0, 0, 0)]);
        var release = new TaskCompletionSource<LocalUsageCoordinatorResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new LocalUsagePresentationHost(new(), _ => ValueTask.FromResult(startup), (_, _) => ValueTask.FromResult(policy),
            async _ => await release.Task);
        var publications = 0; host.PropertyChanged += (_, _) => publications++;

        await host.StartAsync();
        Assert.Equal(BigInteger.One, Row(host.State, UsageProjectionScope.OpenCode).Total);
        await host.ApplyPolicyAsync(new(true, false));
        Assert.Equal(new BigInteger(2), Row(host.State, UsageProjectionScope.OpenCode).Total);
        var refresh = host.RefreshAsync().AsTask();
        Assert.True(host.State.IsRefreshing); Assert.Equal("Refreshing retained history", host.State.StatusLabel);
        Assert.Equal(new BigInteger(2), Row(host.State, UsageProjectionScope.OpenCode).Total);
        Assert.Single(Row(host.State, UsageProjectionScope.OpenCode).Details);
        release.SetResult(refreshed); await refresh;
        Assert.Equal(new BigInteger(3), Row(host.State, UsageProjectionScope.OpenCode).Total);
        Assert.Equal(6, publications);
    }

    private static LocalUsagePresentationRow Row(LocalUsagePresentationState state, UsageProjectionScope scope) => state.Rows.Single(row => row.Scope == scope);
    private static LocalUsageCoordinatorResult Result(LocalUsageProjectionStatus projection, IReadOnlyList<DailyToolModelUsageFact> facts,
        LocalUsageSourceStatus openCode = LocalUsageSourceStatus.Completed, LocalUsageSourceStatus pi = LocalUsageSourceStatus.Completed) =>
        new([new(UsageTool.OpenCode, openCode, 999), new(UsageTool.Pi, pi, 999)], projection, facts);
    private static DailyToolModelUsageFact Fact(UsageProjectionScope scope, int day, string model,
        long input, long read, long write, long visible, long reasoning) =>
        new(new DateOnly(2030, 1, day), "should-not-display-zone", TimeSpan.Zero, "should-not-display-policy", scope, model,
            new(input, read, write, visible, reasoning));
}
