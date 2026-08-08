using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using AIBar.Application;
using AIBar.Domain;
[assembly: InternalsVisibleTo("AIBar.Domain.Tests")]
namespace AIBar.Desktop;
public partial class App : System.Windows.Application
{
    private SingleInstanceHost? _instance;
    private TaskbarRecreationMonitor? _taskbar;
    private TrayHostRuntime? _runtime;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instance = new SingleInstanceHost("AIBar");
        if (!_instance.IsPrimary)
        {
            _instance.RequestActivation();
            _instance.Dispose();
            Shutdown();
            return;
        }

        _ = StartPrimary(() => CreateComposition(), StartTray, ReportFault, Shutdown);
    }

    internal static async Task StartPrimary(Func<StartupComposition> compose, Action<StartupComposition> startTray, Action<Exception> report, Action shutdown)
    {
        StartupComposition composition;
        try { composition = compose(); }
        catch (Exception exception) { report(exception); composition = StartupComposition.Unavailable; }
        try { startTray(composition); }
        catch (Exception exception)
        {
            report(exception);
            try { await composition.Resource.DisposeAsync(); } catch (Exception disposalException) { report(disposalException); }
            shutdown();
            return;
        }
        try { await composition.Initialize(); }
        catch (Exception exception) { report(exception); composition.ReportUnavailable(); }
    }
    internal static StartupComposition CreateComposition(CompositionSeams? seams = null)
    {
        seams ??= new();
        var dataDirectory = seams.DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar");
        Directory.CreateDirectory(dataDirectory);
        var store = new SqliteQuotaSnapshotStore(Path.Combine(dataDirectory, "quota.db"));
        var policy = new PrivateIntegrationPolicy();
        var clock = new SystemClock();
        var codexHome = seams.CodexHome ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        var credentials = new ConsentCredentialSource(policy, new CodexCredentialReader(new ReadOnlyCredentialFileReader()), CodexRootResolver.Resolve(codexHome, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        var coordinator = new QuotaRefreshCoordinator(store, new QuotaHttpProvider(policy, credentials), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        var analyticsStore = new SqliteDailyModelUsageStore(Path.Combine(dataDirectory, "analytics.db"));
        var analytics = new LocalCodexAnalyticsView();
        var analyticsOwner = new AnalyticsLifecycleOwner(new LocalCodexAnalyticsAdapter(
            new SessionFileDiscovery(codexHome, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 50),
            new AnalyticsScanCoordinator(new SessionJsonlScanner(new SessionCheckpointStore(), "beta-v1", beforeStable: seams.BeforeAnalyticsStability), analyticsStore, new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Local, "beta-v1"))),
            analyticsStore, () => clock.UtcNow), analytics, analyticsStore, seams.AnalyticsShutdownBound ?? TimeSpan.FromSeconds(2), seams.LifecycleObservation);
        var betaRuntime = new BetaRuntime(new ConsentSettings(Path.Combine(dataDirectory, "settings.json")), policy, coordinator, clock, credentials, analyticsOwner);
        var lifecycleEvents = new WindowsLifecycleEvents();
        var lifecycleAdapter = new QuotaRefreshLifecycleAdapter(lifecycleEvents, coordinator);
        var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock), () => policy.IsEnabled, ReportFault, lifecycleEvents);
        var startup = new PerUserStartupRegistration(new WindowsPackagedStartupTaskRegistration("AIBar"), new WindowsCurrentUserRunStore(), "AIBar", Environment.ProcessPath ?? throw new InvalidOperationException());
        var clear = new ClearAiBarDataService(dataDirectory, [coordinator], CreateEmptyStateFactory(coordinator));
        return new(new BetaAnalyticsPresentation(presentation, analytics), presentation.RefreshCommand, new NativeSettingsCommands(startup, clear, policy, async token => { await betaRuntime.RevokeConsentAsync(token); presentation.RefreshAvailabilityChanged(); }), new QuotaRuntimeResource(presentation, lifecycleAdapter, lifecycleEvents, betaRuntime, analyticsOwner, store, seams.LifecycleObservation), () => InitializeCompositionAsync(betaRuntime, presentation, default), presentation.ReportUnavailable, coordinator.ReevaluateAsync);
    }
    internal static async Task InitializeCompositionAsync(BetaRuntime runtime, QuotaPresentationHost presentation, CancellationToken cancellationToken)
    {
        await runtime.InitializeAsync(cancellationToken);
        presentation.RefreshAvailabilityChanged();
    }
    internal static Func<CancellationToken, ValueTask> CreateEmptyStateFactory(QuotaRefreshCoordinator coordinator) => coordinator.ClearAsync;
    private void StartTray(StartupComposition composition)
    {
        var window = new MainWindow { DataContext = composition.Presentation, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        _taskbar = new TaskbarRecreationMonitor(window);
        var trayPresentation = composition.Presentation is BetaAnalyticsPresentation analyticsPresentation ? analyticsPresentation.QuotaPresentation : composition.Presentation as QuotaPresentationHost;
        _runtime = new TrayHostRuntime(_instance!, new WindowsTrayRuntime(), new WpfPopoverRuntime(window), _taskbar,
            _ => Task.CompletedTask, composition.Resource, Shutdown, composition.RefreshCommand, composition.Settings, composition.ReportUnavailable, composition.Reevaluate, trayPresentation);
        _runtime.Start();
    }
    private static void ReportFault(Exception exception) => Trace.TraceError("AIBar unavailable: {0}", exception.GetType().Name);
    internal sealed record StartupComposition(object Presentation, IManualRefreshCommand? RefreshCommand, NativeSettingsCommands? Settings, IAsyncDisposable Resource, Func<Task> Initialize, Action ReportUnavailable, Func<RefreshTrigger, CancellationToken, ValueTask>? Reevaluate = null)
    {
        internal StartupComposition(object presentation, IManualRefreshCommand? refreshCommand, IAsyncDisposable resource, Func<Task> initialize, Action reportUnavailable)
            : this(presentation, refreshCommand, null, resource, initialize, reportUnavailable) { }
        internal static StartupComposition Unavailable => new(new UnavailableQuotaPresentation(), null, null, new EmptyAsyncResource(), () => Task.CompletedTask, () => { });
    }

    internal sealed record CompositionSeams(string? DataDirectory = null, string? CodexHome = null, TimeSpan? AnalyticsShutdownBound = null, Action<string>? BeforeAnalyticsStability = null, Action<string>? LifecycleObservation = null);
    private sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    internal sealed class QuotaRuntimeResource(QuotaPresentationHost presentation, QuotaRefreshLifecycleAdapter lifecycleAdapter, WindowsLifecycleEvents lifecycleEvents, BetaRuntime runtime, AnalyticsLifecycleOwner analyticsOwner, IAsyncDisposable store, Action<string>? observe) : IAsyncDisposable
    {
        public AnalyticsShutdownOutcome AnalyticsOutcome => analyticsOwner.Outcome;
        public ValueTask DisposeAsync() => DisposeAllAsync([
            lifecycleAdapter.DisposeAsync, presentation.DisposeAsync,
            () => { lifecycleEvents.Dispose(); return ValueTask.CompletedTask; },
            runtime.DisposeAsync, () => DisposeStoreAsync(store, observe), analyticsOwner.DisposeAsync]);

        private static async ValueTask DisposeStoreAsync(IAsyncDisposable store, Action<string>? observe) { await store.DisposeAsync(); observe?.Invoke("quota_store_disposed"); }

        internal static async ValueTask DisposeAllAsync(IEnumerable<Func<ValueTask>> disposals)
        {
            Exception? primary = null;
            foreach (var dispose in disposals)
                try { await dispose(); } catch (Exception exception) { primary ??= exception; }
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbar?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
