using System.Text.Json;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class CodexDataSourceTests
{
    [Theory]
    [InlineData(DataSource.ThisPc)]
    [InlineData(DataSource.Automatic)]
    public async Task Remote_only_blocks_credentials_endpoint_and_cached_values_then_local_refresh_resumes(DataSource local)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-codex-source-{Guid.NewGuid():N}");
        try
        {
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(true, default);
            await settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
            await settings.SaveClaudeSourceAsync(ClaudeQuotaSource.ThisPc, default);
            var policy = new PrivateIntegrationPolicy();
            var files = new Files();
            var credentials = new ConsentCredentialSource(policy, new CodexCredentialReader(files), "synthetic");
            var provider = new Provider(credentials);
            var clock = new FixedClock();
            var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            await using var runtime = new BetaRuntime(settings, policy, coordinator, clock);
            await runtime.InitializeAsync(default);
            await runtime.RefreshAsync(RefreshTrigger.Manual, default);
            // Also protect callers that use the coordinator directly (panel and lifecycle).
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(0, files.Reads); Assert.Equal(0, provider.Calls);
            Assert.Null(runtime.State.Snapshot);
            var shown = new QuotaPresentationMapper(clock).Map(runtime.State);
            Assert.True(shown.IsUnavailable);
            Assert.Equal("No data from the other machine yet", shown.ErrorLabel);
            await using var claude = new ClaudeQuotaRuntime(settings, new Store(), _ => provider, clock,
                remoteProviderFactory: _ => provider, remoteFileAvailable: () => false);
            await claude.InitializeAsync(default);
            await runtime.SetSourceAsync(local, default);
            Assert.Equal(local, runtime.Source);
            Assert.Equal(ClaudeQuotaSource.ThisPc, claude.Source);
            Assert.Equal(local, await settings.LoadDataSourceAsync(default));
            Assert.False(claude.IsConsentEnabled); // Changing source never grants consent.
            Assert.True(provider.Calls > 0); Assert.True(files.Reads > 0);
            await runtime.SetSourceAsync(DataSource.OtherMachine, default);
            var calls = provider.Calls;
            await runtime.RevokeConsentAsync(default);
            await runtime.GrantConsentAsync(default);
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(calls, provider.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Switching_remote_drains_inflight_request_and_hides_its_late_local_result()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-source-drain-{Guid.NewGuid():N}");
        try
        {
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(true, default);
            var provider = new BlockingProvider(); var clock = new FixedClock();
            var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            await using var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock);
            await runtime.InitializeAsync(default);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var change = runtime.SetSourceAsync(DataSource.OtherMachine, default).AsTask();
            await provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(change.IsCompleted);
            provider.Result.SetResult(new(new(new(80, clock.UtcNow.AddHours(1)), null, clock.UtcNow), null));
            await change;
            Assert.Null(runtime.State.Snapshot);
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(1, provider.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Other_machine_replaces_export_and_drains_even_a_cancellation_ignoring_local_write(bool pendingWrite)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-source-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? change = null;
        try
        {
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(true, default);
            var clock = new FixedClock();
            var coordinator = new QuotaRefreshCoordinator(new Store(), new CurrentProvider(clock), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            QuotaAuthorityUpdate latest = null!;
            coordinator.AuthorityUpdated += update => latest = update;
            var writer = new DelayedWriter(WindowsAtomicQuotaExportWriter.ForOwnedSyntheticDataDirectory(root), release);
            await using var publisher = new QuotaExportPublisher(coordinator, writer, clock);
            await using var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock, publisher: publisher);
            await runtime.InitializeAsync(default);
            await runtime.RefreshAsync(RefreshTrigger.Manual, default);
            await publisher.PublicationCompletion;
            AssertExport("current", true);
            if (pendingWrite)
            {
                writer.Block = true;
                await runtime.RefreshAsync(RefreshTrigger.Manual, default);
                await writer.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            change = runtime.SetSourceAsync(DataSource.OtherMachine, default).AsTask();
            if (pendingWrite)
            {
                // Cancellation alone is insufficient: this writer deliberately commits its late value.
                await writer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.False(change.IsCompleted);
                release.TrySetResult();
            }
            await change;
            AssertExport("unavailable", false);
            publisher.Accept(latest with { EventSequence = latest.EventSequence + 1 });
            await publisher.PublicationCompletion;
            AssertExport("unavailable", false);
            writer.Block = false;
            await runtime.SetSourceAsync(DataSource.ThisPc, default);
            // Real authority retrievals must resume publishing, not just the panel.
            await runtime.RefreshAsync(RefreshTrigger.Manual, default);
            await publisher.PublicationCompletion;
            AssertExport("current", true);

            void AssertExport(string state, bool values)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "yasb-quota.json")));
                var document = json.RootElement;
                Assert.Equal(1, document.GetProperty("schemaVersion").GetInt32());
                Assert.Equal(state, document.GetProperty("state").GetString());
                Assert.Equal(values ? JsonValueKind.Object : JsonValueKind.Null, document.GetProperty("fiveHour").ValueKind);
                Assert.Equal(JsonValueKind.Null, document.GetProperty("weekly").ValueKind);
                if (!values) Assert.Equal(JsonValueKind.Null, document.GetProperty("sourceRetrievedAt").ValueKind);
            }
        }
        finally
        {
            release.TrySetResult();
            if (change is not null) await change;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Queued_local_alert_is_dropped_at_tray_delivery_after_source_switch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-source-alert-{Guid.NewGuid():N}");
        var clock = new FixedClock();
        var coordinator = new QuotaRefreshCoordinator(new Store(), new CurrentProvider(clock), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        var runtime = new BetaRuntime(new ConsentSettings(Path.Combine(root, "settings.json")), new PrivateIntegrationPolicy(), coordinator, clock);
        var observer = new QuotaAlertObserver(root, clock);
        try
        {
            runtime.GrantConsentAsync(default).AsTask().GetAwaiter().GetResult();
            var tray = new AlertTray(); var glows = 0;
            using var controller = new QuotaAlertTrayController(tray, observer, Dispatcher.CurrentDispatcher, () => { }, _ => glows++, _ => { });
            // Discover the new public predicate so the tests-only revision still compiles on the base.
            var predicate = typeof(BetaRuntime).GetProperty("CanDeliverAlerts");
            Assert.NotNull(predicate);
            var checks = 0;
            bool Eligible() { Interlocked.Increment(ref checks); return (bool)predicate.GetValue(runtime)!; }
            var queued = 0;
            observer.AlertReady += _ => queued++;
            observer.Observe("Codex", new(new(new(new(100, clock.UtcNow.AddHours(2)), null, clock.UtcNow), FreshnessState.Current, false, null, null), 1, 1), Eligible);
            observer.Completion.GetAwaiter().GetResult(); // Delivery is queued on the dispatcher, not pumped yet.
            Assert.True(queued > 0);
            Assert.True(Eligible());
            runtime.SetSourceAsync(DataSource.OtherMachine, default).AsTask().GetAwaiter().GetResult();
            Assert.False(Eligible());
            checks = 0;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            var deadline = DateTime.UtcNow.AddSeconds(30);
            timer.Tick += (_, _) => frame.Continue = Volatile.Read(ref checks) < queued && DateTime.UtcNow < deadline;
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.True(checks >= queued); // Every async delivery has reached its final eligibility recheck.
            Assert.Equal(0, tray.Delivered);
            Assert.Equal(0, glows);
        }
        finally
        {
            observer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Remote_only_never_starts_local_analytics_and_switching_stops_then_resumes_it(bool enabledAtStartup)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-source-analytics-{Guid.NewGuid():N}");
        try
        {
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
            await settings.SaveClaudeSourceAsync(ClaudeQuotaSource.ThisPc, default);
            await settings.SaveAsync(enabledAtStartup, default);
            var clock = new FixedClock(); var analytics = new Analytics();
            var coordinator = new QuotaRefreshCoordinator(new Store(), new CurrentProvider(clock), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            await using var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock, analytics: analytics);
            await runtime.InitializeAsync(default);
            if (!enabledAtStartup) await runtime.GrantConsentAsync(default);
            Assert.Equal(0, analytics.Starts);
            await runtime.SetSourceAsync(DataSource.ThisPc, default);
            Assert.Equal(1, analytics.Starts);
            await runtime.SetSourceAsync(DataSource.OtherMachine, default);
            Assert.Equal(1, analytics.Stops);
            await runtime.SetSourceAsync(DataSource.Automatic, default);
            Assert.Equal(2, analytics.Starts);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Analytics : ILocalAnalyticsLifecycle
    {
        public int Starts, Stops;
        public ValueTask StartAsync(CancellationToken token) { Starts++; return ValueTask.CompletedTask; }
        public ValueTask StopAsync(CancellationToken token) { Stops++; return ValueTask.CompletedTask; }
    }
    private sealed class AlertTray : IQuotaAlertTray
    {
        public event Action? AlertClicked { add { } remove { } }
        public int Delivered;
        public void ShowAlert(string title, string body) => Delivered++;
    }
    private sealed class CurrentProvider(IClock clock) : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) =>
            ValueTask.FromResult(new QuotaProviderResult(new(new(42, clock.UtcNow.AddHours(1)), null, clock.UtcNow), null));
    }
    private sealed class DelayedWriter(IQuotaExportWriter inner, TaskCompletionSource release) : IQuotaExportWriter
    {
        public bool Block;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset now, CancellationToken token)
        {
            if (Block && document.FiveHour is not null)
            {
                using var registration = token.Register(() => Cancelled.TrySetResult());
                Started.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
                await inner.WriteAsync(document, now, default);
            }
            else await inner.WriteAsync(document, now, token);
        }
    }
    private sealed class BlockingProvider : IQuotaProvider
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<QuotaProviderResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++; Started.TrySetResult();
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            return await Result.Task;
        }
    }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2030-01-01T12:00:00Z"); }
    private sealed class Files : ICredentialFileReader
    {
        public int Reads;
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        { Reads++; return ValueTask.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"))); }
    }
    private sealed class Provider(ConsentCredentialSource credentials) : IQuotaProvider
    {
        public int Calls;
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        { Calls++; await credentials.GetAvailabilityAsync(cancellationToken); return new(null, new(QuotaErrorKind.Unavailable, "quota_credential_missing")); }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<QuotaSnapshot?>(new(new(42, DateTimeOffset.UtcNow), null, DateTimeOffset.UtcNow));
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
