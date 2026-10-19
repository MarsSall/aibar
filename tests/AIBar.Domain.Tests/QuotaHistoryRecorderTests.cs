using AIBar.Application;
using AIBar.Domain;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class QuotaHistoryRecorderTests
{
    private static readonly DateTimeOffset Now = new(2030, 2, 1, 12, 0, 0, TimeSpan.Zero);
    private static QuotaSnapshot Snapshot => new(new QuotaWindow(25, Now.AddHours(2)), new QuotaWindow(70, Now.AddDays(2)), Now);

    [Fact]
    public async Task Only_retrieval_generations_append_not_cache_loading_failure_or_freshness_transitions()
    {
        var provider = new Provider(new(Snapshot, null)); var history = new History();
        await using var coordinator = Coordinator(provider, new Cache { Snapshot = Snapshot });
        await using var recorder = new QuotaHistoryRecorder(coordinator, history, new FixedClock(Now), QuotaHistoryProvider.Codex, QuotaHistorySource.CodexLocal);
        await coordinator.InitializeAsync(default); await recorder.DrainAsync(default); Assert.Empty(history.Rows);
        await coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default); await recorder.DrainAsync(default); Assert.Empty(history.Rows);
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await recorder.DrainAsync(default);
        Assert.Equal(2, history.Rows.Count);
        Assert.Equal(new[] { QuotaHistoryWindow.FiveHour, QuotaHistoryWindow.Weekly }, history.Rows.Select(row => row.Window));
        Assert.All(history.Rows, row => { Assert.Equal(QuotaHistoryProvider.Codex, row.Provider); Assert.Equal(QuotaHistorySource.CodexLocal, row.Source); Assert.Equal(Now, row.RetrievedAt); });
        provider.Result = new(null, new(QuotaErrorKind.Network, "unavailable"));
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await recorder.DrainAsync(default); Assert.Equal(2, history.Rows.Count);
    }

    [Fact]
    public async Task Each_success_is_queued_without_coalescing_and_missing_windows_are_not_invented()
    {
        var history = new History(); var provider = new Provider(new(new(null, Snapshot.Weekly, Now), null));
        await using var coordinator = Coordinator(provider);
        await using var recorder = new QuotaHistoryRecorder(coordinator, history, new FixedClock(Now), QuotaHistoryProvider.Claude, QuotaHistorySource.ClaudeRemote);
        for (var i = 0; i < 20; i++)
        {
            provider.Result = new(new(null, Snapshot.Weekly, Now.AddTicks(i)), null);
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
        }
        await recorder.DrainAsync(default);
        Assert.Equal(20, history.Rows.Count);
        Assert.All(history.Rows, row => { Assert.Equal(QuotaHistoryWindow.Weekly, row.Window); Assert.Equal(QuotaHistorySource.ClaudeRemote, row.Source); });
    }

    [Fact]
    public async Task Writer_errors_do_not_change_authority_and_next_retrieval_retries()
    {
        var history = new History { Fail = true }; await using var coordinator = Coordinator(new(new(Snapshot, null)));
        await using var recorder = new QuotaHistoryRecorder(coordinator, history, new FixedClock(Now), QuotaHistoryProvider.Codex, QuotaHistorySource.CodexLocal);
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await recorder.DrainAsync(default);
        Assert.Equal(Snapshot, coordinator.State.Snapshot); Assert.Null(coordinator.State.Failure);
        history.Fail = false;
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await recorder.DrainAsync(default); Assert.Equal(2, history.Rows.Count);
    }

    [Fact]
    public async Task Clear_cancels_queued_work_drains_cancellation_ignoring_writer_and_resets_memory_before_resume()
    {
        var history = new BlockingHistory(); await using var coordinator = Coordinator(new(new(Snapshot, null)));
        await using var recorder = new QuotaHistoryRecorder(coordinator, history, new FixedClock(Now), QuotaHistoryProvider.Codex, QuotaHistorySource.CodexLocal);
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await history.Entered.Task;
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
        var clear = recorder.CancelAndWaitAsync(default).AsTask(); Assert.False(clear.IsCompleted);
        history.Release.TrySetResult(); await clear;
        Assert.Equal(1, history.Calls);
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await recorder.DrainAsync(default); Assert.Equal(1, history.Calls);
        recorder.ResumeAfterClear();
        await coordinator.RefreshAsync(RefreshTrigger.Manual, default); await recorder.DrainAsync(default); Assert.Equal(2, history.Calls);
    }

    [Fact]
    public async Task Claude_reattaches_for_consent_and_source_handoffs_without_gate_reentrancy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-history-runtime-{Guid.NewGuid():N}");
        try
        {
            var history = new History(); var clock = new FixedClock(Now);
            await using var runtime = new ClaudeQuotaRuntime(new ConsentSettings(Path.Combine(root, "settings.json")), new Cache(),
                _ => new Provider(new(Snapshot, null)), clock,
                remoteProviderFactory: _ => new Provider(new(Snapshot, null)), remoteFileAvailable: () => false, historyStore: history);
            await runtime.InitializeAsync(default); Assert.Empty(history.Rows);
            await runtime.GrantConsentAsync(default);
            await runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default);
            await runtime.RevokeConsentAsync(default);
            // Stop drains each recorder; source identity is captured per coordinator.
            Assert.Contains(history.Rows, row => row.Source == QuotaHistorySource.ClaudeLocal);
            Assert.Contains(history.Rows, row => row.Source == QuotaHistorySource.ClaudeRemote);
            var before = history.Rows.Count;
            await runtime.GrantConsentAsync(default); await runtime.RevokeConsentAsync(default);
            Assert.True(history.Rows.Count > before);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Desktop_owns_shared_history_clear_removes_it_and_Claude_can_record_after_a_new_grant()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-history-composition-{Guid.NewGuid():N}");
        try
        {
            var composition = App.CreateComposition(new(DataDirectory: root, CodexHome: Path.Combine(root, "codex"),
                UserProfile: Path.Combine(root, "profile"), ClaudeProviderFactory: _ => new Provider(new(Snapshot, null))));
            await using var resource = composition.Resource;
            await composition.Initialize();
            var history = Assert.IsAssignableFrom<IQuotaHistoryStore>(composition.QuotaHistory);
            Assert.False(File.Exists(SqliteQuotaHistoryStore.DatabasePath(root)));
            await composition.ClaudeQuota.GrantConsentAsync(default);
            await composition.ClaudeQuota.RevokeConsentAsync(default);
            Assert.Single(await history.QueryAsync(QuotaHistoryProvider.Claude, QuotaHistoryWindow.FiveHour, Now, Now, default));
            Assert.Empty(await history.QueryAsync(QuotaHistoryProvider.Codex, QuotaHistoryWindow.FiveHour, Now, Now, default));
            await composition.Settings!.ClearAiBarDataAsync(default);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) Assert.False(File.Exists(SqliteQuotaHistoryStore.DatabasePath(root) + suffix));
            Assert.Empty(await history.QueryAsync(QuotaHistoryProvider.Claude, QuotaHistoryWindow.FiveHour, Now, Now, default));
            await composition.ClaudeQuota.GrantConsentAsync(default);
            await composition.ClaudeQuota.RevokeConsentAsync(default);
            Assert.Single(await history.QueryAsync(QuotaHistoryProvider.Claude, QuotaHistoryWindow.FiveHour, Now, Now, default));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static QuotaRefreshCoordinator Coordinator(Provider provider, Cache? cache = null) => new(cache ?? new(), provider, new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
    private sealed class Provider(QuotaProviderResult result) : IQuotaProvider
    {
        public QuotaProviderResult Result { get; set; } = result;
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) => ValueTask.FromResult(Result);
    }
    private sealed class Cache : IQuotaSnapshotStore
    {
        public QuotaSnapshot? Snapshot { get; set; }
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult(Snapshot);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token) { Snapshot = snapshot; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken token) { Snapshot = null; return ValueTask.CompletedTask; }
    }
    private sealed class History : IQuotaHistoryStore
    {
        public List<QuotaHistorySample> Rows { get; } = [];
        public bool Fail { get; set; }
        public ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken token)
        {
            if (Fail) throw new IOException("synthetic"); Rows.AddRange(samples); return ValueTask.CompletedTask;
        }
        public ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window, DateTimeOffset from, DateTimeOffset to, CancellationToken token) => ValueTask.FromResult<IReadOnlyList<QuotaHistorySample>>(Rows.ToArray());
    }
    private sealed class BlockingHistory : IQuotaHistoryStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken token) { Calls++; Entered.TrySetResult(); await Release.Task; }
        public ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window, DateTimeOffset from, DateTimeOffset to, CancellationToken token) => ValueTask.FromResult<IReadOnlyList<QuotaHistorySample>>([]);
    }
}
