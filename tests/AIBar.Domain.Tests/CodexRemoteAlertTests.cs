using System.Windows.Threading;
using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class CodexRemoteAlertTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Queued_alert_keeps_its_source_identity_when_the_new_source_is_current(bool startRemote)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-codex-alert-handoff-{Guid.NewGuid():N}");
        var clock = new Clock();
        var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
        settings.SaveAsync(true, default).AsTask().GetAwaiter().GetResult();
        settings.SaveDataSourceAsync(startRemote ? DataSource.OtherMachine : DataSource.ThisPc, default).AsTask().GetAwaiter().GetResult();
        var local = new Provider(clock, startRemote ? 90 : 75);
        var remote = new Provider(clock, startRemote ? 75 : 90);
        var coordinator = new QuotaRefreshCoordinator(new Store(), local, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock,
            remoteProviderFactory: _ => remote, remoteFileAvailable: () => true);
        var observer = new QuotaAlertObserver(root, clock);
        var tray = new Tray();
        var queued = new List<QuotaAlert>();
        using var controller = new QuotaAlertTrayController(tray, observer, Dispatcher.CurrentDispatcher, () => { }, _ => { }, _ => { });
        observer.AlertReady += alert => queued.Add(alert);
        coordinator.AuthorityUpdated += update =>
        {
            // Deliberately use existing APIs so this test compiles before production.
            // The predicate is immutable for this source; the observer must retain it per alert.
            var source = runtime.Source;
            observer.Observe("Codex", update, () => runtime.Source == source && runtime.CanDeliverAlerts);
        };
        try
        {
            observer.Settings.SaveAsync(new(Resets: false, RunOut: false, EdgeGlow: false), default).AsTask().GetAwaiter().GetResult();
            runtime.InitializeAsync(default).AsTask().GetAwaiter().GetResult();
            runtime.RefreshAsync(RefreshTrigger.Manual, default).AsTask().GetAwaiter().GetResult();
            observer.Completion.GetAwaiter().GetResult();
            var oldAlert = Assert.Single(queued.Where(a => a.Kind == QuotaAlertKind.ThresholdCrossed));
            Assert.Equal(75, oldAlert.Threshold);
            Assert.True(observer.CanDeliver(oldAlert));
            Assert.Empty(tray.Titles); // No dispatcher pumping yet.

            clock.UtcNow = clock.UtcNow.AddMinutes(1);
            runtime.SetSourceAsync(startRemote ? DataSource.ThisPc : DataSource.OtherMachine, default).AsTask().GetAwaiter().GetResult();
            observer.Completion.GetAwaiter().GetResult();
            Assert.True(runtime.CanDeliverAlerts); // The new source is valid, not an unavailable placeholder.
            var newAlert = Assert.Single(queued.Where(a => a.Threshold == 90));
            Assert.False(observer.CanDeliver(oldAlert));
            Assert.True(observer.CanDeliver(newAlert));

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            var deadline = DateTime.UtcNow.AddSeconds(10);
            timer.Tick += (_, _) => frame.Continue = tray.Titles.Count == 0 && DateTime.UtcNow < deadline;
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.Single(tray.Titles);
            Assert.Contains("90%", tray.Titles[0]);
            Assert.DoesNotContain(tray.Titles, title => title.Contains("75%"));
        }
        finally
        {
            observer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Delayed_local_authority_cannot_capture_the_remote_alert_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-delayed-authority-{Guid.NewGuid():N}");
        var clock = new Clock();
        var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
        settings.SaveAsync(true, default).AsTask().GetAwaiter().GetResult();
        settings.SaveDataSourceAsync(DataSource.ThisPc, default).AsTask().GetAwaiter().GetResult();
        var local = new MutableProvider(clock);
        var coordinator = new QuotaRefreshCoordinator(new Store(), local, clock,
            new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock,
            remoteProviderFactory: _ => new Provider(clock, 90), remoteFileAvailable: () => true);
        var observer = new QuotaAlertObserver(root, clock);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task? drain = null;
        var armed = 0;
        var queued = new List<QuotaAlert>();
        var tray = new Tray();
        using var controller = new QuotaAlertTrayController(tray, observer, Dispatcher.CurrentDispatcher, () => { }, _ => { }, _ => { });
        coordinator.AuthorityUpdated += _ =>
        {
            if (Interlocked.Exchange(ref armed, 0) == 0) return;
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "Authority drain was not released.");
        };
        coordinator.AuthorityUpdated += update =>
        {
            // Exercise production's update-bound capture API when present. Falling
            // back lets this tests-only revision expose the race on the current base.
            var method = typeof(BetaRuntime).GetMethod("CaptureAlertEligibility", [typeof(QuotaAuthorityUpdate)]);
            var eligible = method is null ? runtime.CaptureAlertEligibility()
                : (Func<bool>)method.Invoke(runtime, [update])!;
            observer.Observe("Codex", update, eligible);
        };
        observer.AlertReady += alert => queued.Add(alert);
        try
        {
            observer.Settings.SaveAsync(new(Resets: false, RunOut: false, EdgeGlow: false), default).AsTask().GetAwaiter().GetResult();
            runtime.InitializeAsync(default).AsTask().GetAwaiter().GetResult();
            runtime.RefreshAsync(RefreshTrigger.Manual, default).AsTask().GetAwaiter().GetResult();
            observer.Completion.GetAwaiter().GetResult();
            clock.UtcNow = clock.UtcNow.AddMinutes(11);
            Volatile.Write(ref armed, 1);
            // A concurrent freshness notification owns the event drain, not retrieval.
            drain = Task.Run(() => coordinator.ReevaluateAsync(RefreshTrigger.Sleep, default).AsTask());
            Assert.True(entered.Wait(TimeSpan.FromSeconds(30)));
            local.Percentage = 75;
            runtime.RefreshAsync(RefreshTrigger.Manual, default).AsTask().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            // The completed local retrieval is queued behind the blocked notification.
            runtime.SetSourceAsync(DataSource.OtherMachine, default).AsTask().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            Assert.Equal(90m, runtime.State.Snapshot!.Primary!.PercentageUsed);
            Assert.True(runtime.CanDeliverAlerts);
            release.Set();
            drain.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            observer.Completion.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            Assert.DoesNotContain(queued, alert => alert.Sample.PercentageUsed == 75);
            Assert.Contains(queued, alert => alert.Threshold == 90 && observer.CanDeliver(alert));

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            var deadline = DateTime.UtcNow.AddSeconds(10);
            timer.Tick += (_, _) => frame.Continue = tray.Titles.Count < queued.Count && DateTime.UtcNow < deadline;
            timer.Start();
            try { Dispatcher.PushFrame(frame); }
            finally { timer.Stop(); }
            Assert.Contains(tray.Titles, title => title.Contains("90%"));
            Assert.Equal(queued.Count, tray.Titles.Count);
        }
        finally
        {
            release.Set();
            try { drain?.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult(); }
            finally
            {
                observer.DisposeAsync().AsTask().GetAwaiter().GetResult();
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }

    private sealed class MutableProvider(Clock clock) : IQuotaProvider
    {
        public decimal Percentage = 10;
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) =>
            ValueTask.FromResult(new QuotaProviderResult(new(new(Percentage, clock.UtcNow.AddHours(5)), null, clock.UtcNow), null));
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow { get; set; } = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero); }
    private sealed class Provider(Clock clock, decimal percentage) : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken token) =>
            ValueTask.FromResult(new QuotaProviderResult(new(new(percentage, new DateTimeOffset(2030, 1, 1, 17, 0, 0, TimeSpan.Zero)), null, clock.UtcNow), null));
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken token) => ValueTask.FromResult<QuotaSnapshot?>(null);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class Tray : IQuotaAlertTray
    {
        public event Action? AlertClicked { add { } remove { } }
        public List<string> Titles { get; } = [];
        public void ShowAlert(string title, string body) => Titles.Add(title);
    }
}
