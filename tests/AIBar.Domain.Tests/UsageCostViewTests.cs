using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class UsageCostViewTests
{
    [Fact]
    public async Task Refresh_publishes_cost_rows_and_disabling_hides_previous_data_without_a_read()
    {
        var enabled = true;
        var calls = 0;
        var state = new UsageCostSummary(new(15_000, .04m, false), new(218_000_000, 254.24m, false));
        await using var view = new UsageCostView(() => enabled, _ => { calls++; return ValueTask.FromResult(state); });
        var changes = new List<string?>();
        view.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        await view.RefreshAsync(default);
        Assert.Equal("Today: $0.04 · 15K tokens", view.State.TodayLabel);
        Assert.Equal("Last 30 days: $254.24 · 218M tokens", view.State.Last30DaysLabel);
        Assert.Contains("State", changes);
        enabled = false;
        await view.RefreshAsync(default);
        Assert.Equal(1, calls);
        Assert.Equal(UsageCostSummary.Unavailable, view.State);
    }

    [Fact]
    public async Task Disabled_provider_never_reads_and_clear_drains_then_resets_and_resumes()
    {
        var enabled = false;
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new UsageCostView(() => enabled, async token =>
        {
            calls++; entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return UsageCostSummary.Unavailable;
        });
        await view.RefreshAsync(default);
        Assert.Equal(0, calls);
        enabled = true;
        var read = view.RefreshAsync(default).AsTask();
        await entered.Task;
        await view.CancelAndWaitAsync(default);
        await read;
        Assert.Contains("Unavailable", view.State.TodayLabel);
        view.ResumeAfterClear();
        await view.DisposeAsync();
        await view.RefreshAsync(default);
        Assert.Equal(1, calls);
    }
}
