using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class AnalyticsLifecycleTests
{
    [Fact]
    public async Task Production_view_publishes_local_totals_partial_coverage_warning_and_scan_time_after_loading()
    {
        await using var harness = await ProductionHarness.CreateAsync(TimeSpan.FromSeconds(2), partial: true);
        var presentation = (BetaAnalyticsPresentation)harness.Composition.Presentation;

        await harness.Composition.Initialize();
        Assert.True(presentation.Analytics.IsLoading);
        Assert.True(harness.Entered.Wait(TimeSpan.FromSeconds(2)));
        harness.Release.Set();
        await harness.ScanFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var state = presentation.Analytics;
        Assert.Equal(AnalyticsScanStatus.Partial, state.Status);
        Assert.Equal(new TokenTotals(1, 0, 1), state.Totals);
        Assert.Equal(ScanCoverageState.Partial, state.Coverage);
        Assert.Contains("session_malformed_record", state.WarningCodes);
        Assert.NotNull(state.ScannedAt);
    }

    [Fact]
    public async Task BetaRuntime_revocation_stops_the_active_production_analytics_scan_without_promoting_a_result()
    {
        await using var harness = await ProductionHarness.CreateAsync(TimeSpan.FromSeconds(2), partial: true);
        var presentation = (BetaAnalyticsPresentation)harness.Composition.Presentation;

        await harness.Composition.Initialize();
        Assert.True(harness.Entered.Wait(TimeSpan.FromSeconds(2)));
        var revoke = harness.Composition.Settings!.DisablePrivateIntegrationAsync(CancellationToken.None).AsTask();
        Assert.True(harness.Cancelled.Wait(TimeSpan.FromSeconds(2)));
        harness.Release.Set();
        await revoke;

        Assert.Equal(AnalyticsShutdownKind.Completed, harness.Outcome.Kind);
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_cancelled"));
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_awaited"));
        Assert.NotEqual(AnalyticsScanStatus.Complete, presentation.Analytics.Status);
    }

    [Fact]
    public async Task Production_exit_cooperatively_cancels_awaits_and_disposes_analytics_dependencies_once()
    {
        await using var harness = await ProductionHarness.CreateAsync(TimeSpan.FromSeconds(2));
        await harness.Composition.Initialize();
        Assert.True(harness.Entered.Wait(TimeSpan.FromSeconds(2)));

        var exit = harness.ExitAsync();
        Assert.True(harness.Cancelled.Wait(TimeSpan.FromSeconds(2)));
        harness.Release.Set();
        await exit;

        Assert.Equal(AnalyticsShutdownKind.Completed, harness.Outcome.Kind);
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_cancelled"));
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_awaited"));
        Assert.Contains("analytics_view_disposed", harness.Events);
        Assert.Contains("analytics_store_disposed", harness.Events);
        Assert.Contains("quota_store_disposed", harness.Events);
    }

    [Fact]
    public async Task Production_exit_times_out_without_reawaiting_or_disposing_retained_analytics_dependencies()
    {
        await using var harness = await ProductionHarness.CreateAsync(TimeSpan.FromMilliseconds(50));
        await harness.Composition.Initialize();
        Assert.True(harness.Entered.Wait(TimeSpan.FromSeconds(2)));

        await harness.ExitAsync();

        Assert.Equal(AnalyticsShutdownKind.TimedOut, harness.Outcome.Kind);
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_cancelled"));
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_awaited"));
        Assert.Contains("analytics_timed_out", harness.Events);
        Assert.Contains("quota_store_disposed", harness.Events);
        Assert.DoesNotContain("analytics_view_disposed", harness.Events);
        Assert.DoesNotContain("analytics_store_disposed", harness.Events);

        harness.Release.Set();
        await harness.ScanFinished.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(AnalyticsShutdownKind.TimedOut, harness.Outcome.Kind);
        Assert.Equal(1, harness.Events.Count(item => item == "analytics_awaited"));
        Assert.DoesNotContain("analytics_view_disposed", harness.Events);
    }

    [Fact]
    public async Task Cooperative_stop_then_reenable_starts_exactly_one_fresh_scan_generation()
    {
        var scanner = new GenerationScanner();
        var events = new List<string>();
        await using var owner = new AnalyticsLifecycleOwner(scanner, new LocalCodexAnalyticsView(), new EmptyResource(), TimeSpan.FromSeconds(1), events.Add);

        await owner.StartAsync(CancellationToken.None);
        await scanner.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await owner.StopAsync(CancellationToken.None);

        await owner.StartAsync(CancellationToken.None);
        await owner.StartAsync(CancellationToken.None);
        await scanner.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(2, scanner.Calls);

        await owner.StopAsync(CancellationToken.None);
        Assert.Equal(2, events.Count(item => item == "analytics_cancelled"));
        Assert.Equal(2, events.Count(item => item == "analytics_awaited"));
    }

    private sealed class ProductionHarness : IAsyncDisposable
    {
        private readonly string _root;
        private readonly TrayHostRuntime _host;

        private ProductionHarness(string root, TrayHostRuntime host, App.StartupComposition composition, List<string> events, ManualResetEventSlim entered, ManualResetEventSlim cancelled, ManualResetEventSlim release, TaskCompletionSource scanFinished)
        {
            _root = root; _host = host; Composition = composition; Events = events; Entered = entered; Cancelled = cancelled; Release = release; ScanFinished = scanFinished;
        }

        public App.StartupComposition Composition { get; }
        public List<string> Events { get; }
        public ManualResetEventSlim Entered { get; }
        public ManualResetEventSlim Cancelled { get; }
        public ManualResetEventSlim Release { get; }
        public TaskCompletionSource ScanFinished { get; }
        public AnalyticsShutdownOutcome Outcome => ((App.QuotaRuntimeResource)Composition.Resource).AnalyticsOutcome;

        public static async Task<ProductionHarness> CreateAsync(TimeSpan shutdownBound, bool partial = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"aibar-lifecycle-{Guid.NewGuid():N}");
            var codex = Path.Combine(root, "codex");
            Directory.CreateDirectory(Path.Combine(codex, "sessions"));
            await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), "{\"schema\":1,\"privateCodexConsent\":true}");
            await File.WriteAllTextAsync(Path.Combine(codex, "sessions", "scan.jsonl"), "{\"timestamp\":\"2026-01-01T00:00:00Z\",\"model\":\"gpt-5\",\"usage\":{\"input_tokens\":1,\"cached_input_tokens\":0,\"output_tokens\":1}}\n" + (partial ? "{\n" : ""));
            var events = new List<string>(); var entered = new ManualResetEventSlim(); var cancelled = new ManualResetEventSlim(); var release = new ManualResetEventSlim();
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var composition = App.CreateComposition(new(root, codex, shutdownBound, _ => { entered.Set(); release.Wait(); }, item =>
            {
                lock (events) events.Add(item);
                if (item == "analytics_cancelled") cancelled.Set();
                if (item == "analytics_scan_finished") finished.TrySetResult();
            }));
            var instance = new SingleInstanceHost($"AIBar.lifecycle.{Guid.NewGuid():N}");
            var host = new TrayHostRuntime(instance, new FakeTray(), new FakePopover(), new FakeTaskbar(), _ => Task.CompletedTask, composition.Resource, () => { });
            instance.Dispose();
            return new(root, host, composition, events, entered, cancelled, release, finished);
        }

        public Task ExitAsync() => _host.ExitAsync();
        public async ValueTask DisposeAsync()
        {
            Release.Set();
            await _host.DisposeAsync();
            Entered.Dispose(); Cancelled.Dispose(); Release.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
    }

    private sealed class FakeTray : ITrayRuntime
    {
        public event Action? Toggled; public event Action? ExitRequested; public event Action? RefreshRequested; public event Action? StartupToggleRequested; public event Action? ClearAiBarDataRequested; public event Action? PrivateIntegrationEnableRequested; public event Action? PrivateIntegrationDisableRequested; public event Action? OpenCodeLocalUsageToggleRequested { add { } remove { } } public event Action? PiLocalUsageToggleRequested { add { } remove { } }
        public void SetRefreshAvailable(bool available) { } public void SetPresentation(BetaPresentationState state) { } public void SetSettingsAvailable(bool available) { } public void SetPrivateIntegrationEnabled(bool enabled) { } public void SetStartupEnabled(bool enabled) { } public void SetLocalUsageAvailable(bool available) { } public void SetLocalUsagePolicy(LocalUsagePolicy policy) { } public void Show() { } public void Hide() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakePopover : IPopoverRuntime { public event Action? Deactivated; public bool IsVisible => false; public bool IsOwnedDialogActive => false; public void Show() { } public void Hide() { } public void Activate() { } }
    private sealed class FakeTaskbar : ITaskbarRecreationEvents { public event Action? Recreated; }

    private sealed class GenerationScanner : ILocalCodexAnalyticsScanner
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<LocalAnalyticsState> ScanAsync(CancellationToken cancellationToken)
        {
            (Interlocked.Increment(ref _calls) == 1 ? FirstStarted : SecondStarted).TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancelled scan should not complete normally.");
        }
    }

    private sealed class EmptyResource : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
}
