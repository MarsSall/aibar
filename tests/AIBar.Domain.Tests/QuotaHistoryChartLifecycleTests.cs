using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaHistoryChartLifecycleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Clear_invalidates_and_rejects_late_results_even_if_store_ignores_cancellation(bool chartRead)
    {
        var store = new DelayedStore(); PanelPaceHistory history = null!;
        Task read = null!; var invalidations = 0;
        WpfTestApplicationHost.Run(_ =>
        {
            history = new PanelPaceHistory(store, new Clock());
            history.Invalidated += () => invalidations++;
            read = chartRead ? history.ReadChartAsync(QuotaHistoryChartRange.Day, default) : history.ReadPaceAsync(QuotaHistoryProvider.Codex);
        });
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        WpfTestApplicationHost.Run(_ => history.CancelAndWaitAsync(default).GetAwaiter().GetResult());
        Assert.Equal(1, invalidations);
        store.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        WpfTestApplicationHost.Run(_ => history.Dispose());
    }

    [Fact]
    public async Task Pause_has_no_reads_and_resume_queries_fresh_history()
    {
        var store = new DelayedStore(); store.Release.TrySetResult();
        PanelPaceHistory history = null!; Task<IReadOnlyList<QuotaHistorySample>> read = null!;
        var requested = 0;
        WpfTestApplicationHost.Run(_ =>
        {
            history = new PanelPaceHistory(store, new Clock());
            history.PaceRefreshRequested += () => requested++;
            history.CancelAndWaitAsync(default).GetAwaiter().GetResult();
            history.RefreshPace(); read = history.ReadPaceAsync(QuotaHistoryProvider.Codex);
        });
        Assert.Empty(await read); Assert.Equal(0, store.QueryCount); Assert.Equal(0, requested);
        WpfTestApplicationHost.Run(_ =>
        {
            history.ResumeAfterClear(); history.RefreshPace(); read = history.ReadPaceAsync(QuotaHistoryProvider.Codex);
        });
        Assert.Empty(await read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, store.QueryCount); Assert.Equal(1, requested);
        WpfTestApplicationHost.Run(_ => history.Dispose());
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); }
    private sealed class DelayedStore : IQuotaHistoryStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int QueryCount;
        public ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public async ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref QueryCount); Entered.TrySetResult(); await Release.Task; return [];
        }
    }
}
