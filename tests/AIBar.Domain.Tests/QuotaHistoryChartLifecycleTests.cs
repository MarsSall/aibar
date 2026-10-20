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

    [Fact]
    public void Dispose_and_clear_resume_are_idempotent_and_cannot_resurrect_a_disposed_reader()
    {
        using var history = new PanelPaceHistory(new DelayedStore(), new Clock());
        history.CancelAndWaitAsync(default).GetAwaiter().GetResult();
        history.ResumeAfterClear();
        history.ResumeAfterClear();
        var revision = history.Revision;
        history.Dispose(); history.Dispose();
        history.CancelAndWaitAsync(default).GetAwaiter().GetResult();
        history.ResumeAfterClear(); history.Dispose();
        Assert.False(history.CanPublish(revision));
        Assert.ThrowsAsync<ObjectDisposedException>(() => history.ReadPaceAsync(QuotaHistoryProvider.Codex)).GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Background_clear_finishes_while_owner_dispatcher_is_not_pumping_and_query_is_blocked(bool dispose)
    {
        var store = new DelayedStore();
        PanelPaceHistory history = null!; Task read = null!;
        // This thread owns a Dispatcher but never runs it, matching background composition tests.
        var owner = new Thread(() =>
        {
            history = new PanelPaceHistory(store, new Clock());
            read = history.ReadPaceAsync(QuotaHistoryProvider.Codex);
        });
        owner.Start(); Assert.True(owner.Join(TimeSpan.FromSeconds(5)));
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var revision = history.Revision;
        try
        {
            await Task.Run(async () =>
            {
                if (dispose) { history.Dispose(); history.Dispose(); }
                else await history.CancelAndWaitAsync(default);
            }).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(history.CanPublish(revision));
        }
        finally { store.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        history.Dispose();
    }

    [Fact]
    public async Task Retired_epoch_cannot_publish_after_resume_and_new_epoch_can_read()
    {
        var store = new DelayedStore(); var history = new PanelPaceHistory(store, new Clock());
        var revision = history.Revision;
        var read = history.ReadPaceAsync(QuotaHistoryProvider.Codex);
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await history.CancelAndWaitAsync(default);
            history.ResumeAfterClear();
            Assert.False(history.CanPublish(revision));
            Assert.True(history.CanPublish(history.Revision));
        }
        finally { store.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(await history.ReadPaceAsync(QuotaHistoryProvider.Codex).WaitAsync(TimeSpan.FromSeconds(5)));
        history.Dispose();
    }

    [Fact]
    public async Task Clear_does_not_wait_for_a_blocked_cancellation_callback()
    {
        var store = new BlockingCancellationStore();
        using var history = new PanelPaceHistory(store, new Clock());
        var read = history.ReadPaceAsync(QuotaHistoryProvider.Codex);
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(() => history.CancelAndWaitAsync(default).AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
            await store.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(history.CanPublish(history.Revision));
        }
        finally { store.Release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class BlockingCancellationStore : IQuotaHistoryStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public async ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                CallbackEntered.TrySetResult();
                Release.Task.Wait(TimeSpan.FromSeconds(10));
            });
            Entered.TrySetResult(); await Release.Task;
            return [];
        }
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
