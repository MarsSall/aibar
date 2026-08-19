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
    private readonly bool _suppressHostStartup;
    private SingleInstanceHost? _instance;
    private TaskbarRecreationMonitor? _taskbar;
    private TrayHostRuntime? _runtime;

    public App() : this(false) { }
    internal App(bool suppressHostStartup) => _suppressHostStartup = suppressHostStartup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (_suppressHostStartup) return;
        var showRequested = StartupIntentParser.Parse(e.Args) == StartupIntent.Show;
        _instance = new SingleInstanceHost("AIBar");
        if (!_instance.IsPrimary)
        {
            _instance.RequestActivation();
            _instance.Dispose();
            Shutdown();
            return;
        }

        _ = StartPrimary(() => CreateComposition(), StartTray, ReportFault, Shutdown, showRequested ? RequestShowAfterStartup : null);
    }

    internal static async Task StartPrimary(Func<StartupComposition> compose, Action<StartupComposition> startTray, Action<Exception> report, Action shutdown, Action? showWhenReady = null)
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
        try { await composition.Initialize(); showWhenReady?.Invoke(); }
        catch (Exception exception) { report(exception); composition.ReportUnavailable(); }
    }
    private void RequestShowAfterStartup() => _runtime?.RequestShow();
    internal static StartupComposition CreateComposition(CompositionSeams? seams = null)
    {
        seams ??= new();
        var dataDirectory = seams.DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar");
        Directory.CreateDirectory(dataDirectory);
        var userProfile = seams.UserProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localUsageRoots = LocalUsageSourceRootResolver.Resolve(new(userProfile));
        var store = new SqliteQuotaSnapshotStore(Path.Combine(dataDirectory, "quota.db"));
        var policy = new PrivateIntegrationPolicy();
        var clock = new SystemClock();
        var codexHome = seams.CodexHome ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        var credentials = new ConsentCredentialSource(policy, new CodexCredentialReader(new ReadOnlyCredentialFileReader()), CodexRootResolver.Resolve(codexHome, userProfile));
        var coordinator = new QuotaRefreshCoordinator(store, new QuotaHttpProvider(policy, credentials), clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        var exportPath = seams.DataDirectory is null ? WindowsQuotaExportPath.ForCurrentUser() : Path.Combine(dataDirectory, "yasb-quota.json");
        var publisher = new QuotaExportPublisher(coordinator, seams.ExportWriter ?? new WindowsAtomicQuotaExportWriter(exportPath), clock);
        var localUsageLedger = new SqliteUsageEventLedger(Path.Combine(dataDirectory, "local-usage.db"));
        var localUsageSettings = new LocalUsageSettings(Path.Combine(dataDirectory, "local-usage-settings.json"));
        var localUsage = new LocalUsageCoordinator(localUsageSettings,
            new(Path.Combine(dataDirectory, "local-usage-identity-salt.bin")), localUsageLedger,
            localUsageRoots.OpenCodeDataRoot, localUsageRoots.PiSessionsRoot, new(TimeZoneInfo.Local, "local-usage-v1"));
        var analyticsStore = new SqliteDailyModelUsageStore(Path.Combine(dataDirectory, "analytics.db"));
        var analytics = new LocalCodexAnalyticsView();
        var analyticsOwner = new AnalyticsLifecycleOwner(new LocalCodexAnalyticsAdapter(
            new SessionFileDiscovery(codexHome, userProfile, 50),
            new AnalyticsScanCoordinator(new SessionJsonlScanner(new SessionCheckpointStore(), "beta-v1", beforeStable: seams.BeforeAnalyticsStability), analyticsStore, new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Local, "beta-v1"))),
            analyticsStore, () => clock.UtcNow), analytics, analyticsStore, seams.AnalyticsShutdownBound ?? TimeSpan.FromSeconds(2), seams.LifecycleObservation);
        var betaRuntime = new BetaRuntime(new ConsentSettings(Path.Combine(dataDirectory, "settings.json")), policy, coordinator, clock, credentials, analyticsOwner, publisher);
        var lifecycleEvents = new WindowsLifecycleEvents();
        var lifecycleAdapter = new QuotaRefreshLifecycleAdapter(lifecycleEvents, coordinator);
        var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock), () => policy.IsEnabled, ReportFault, lifecycleEvents);
        var localUsagePresentation = new LocalUsagePresentationHost(localUsage, new LocalUsagePresentationMapper());
        var startup = new PerUserStartupRegistration(new WindowsPackagedStartupTaskRegistration("AIBar"), new WindowsCurrentUserRunStore(), "AIBar", Environment.ProcessPath ?? throw new InvalidOperationException());
        var clear = new ClearAiBarDataService(dataDirectory, [publisher, coordinator, localUsage, analyticsOwner], CreateEmptyStateFactory(coordinator, analytics, publisher));
        return new(new BetaAnalyticsPresentation(presentation, analytics, localUsagePresentation), presentation.RefreshCommand, new NativeSettingsCommands(startup, clear, policy, betaRuntime.RevokeConsentAsync, betaRuntime.GrantConsentAsync,
                localUsageSettings.LoadAsync, localUsageSettings.SaveAsync, async (value, token) => { await localUsagePresentation.ApplyPolicyAsync(value, token); }),
            new QuotaRuntimeResource(presentation, lifecycleAdapter, lifecycleEvents, betaRuntime, publisher, analyticsOwner, store, localUsage, localUsageLedger, seams.LifecycleObservation),
            () => InitializeCompositionAsync(betaRuntime, presentation, localUsagePresentation, default), presentation.ReportUnavailable,
            (trigger, token) => ReevaluateCompositionAsync(coordinator, localUsagePresentation, trigger, token), localUsagePresentation.RefreshAsync);
    }
    internal static async Task InitializeCompositionAsync(BetaRuntime runtime, QuotaPresentationHost presentation, LocalUsagePresentationHost localUsage, CancellationToken cancellationToken)
    {
        var privateInitialization = InitializePrivateAsync(runtime, presentation, cancellationToken);
        await Task.WhenAll(privateInitialization, localUsage.StartAsync(cancellationToken).AsTask());
    }
    internal static Task InitializeCompositionAsync(BetaRuntime runtime, QuotaPresentationHost presentation, CancellationToken cancellationToken) =>
        InitializePrivateAsync(runtime, presentation, cancellationToken);
    internal static async ValueTask ReevaluateCompositionAsync(QuotaRefreshCoordinator quota, LocalUsagePresentationHost localUsage, RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        await quota.ReevaluateAsync(trigger, cancellationToken);
        if (trigger == RefreshTrigger.PopoverOpened) await localUsage.RefreshAsync(cancellationToken);
    }
    private static async Task InitializePrivateAsync(BetaRuntime runtime, QuotaPresentationHost presentation, CancellationToken cancellationToken)
    { await runtime.InitializeAsync(cancellationToken); presentation.RefreshAvailabilityChanged(); }
    internal static Func<CancellationToken, ValueTask> CreateEmptyStateFactory(QuotaRefreshCoordinator coordinator, LocalCodexAnalyticsView? analytics = null, QuotaExportPublisher? publisher = null) => async token =>
    {
        analytics?.ResetAfterClear(); await coordinator.ClearAsync(token);
        if (publisher is not null) await publisher.DisableAsync(token);
    };
    private void StartTray(StartupComposition composition)
    {
        var window = new MainWindow { DataContext = composition.Presentation, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        _taskbar = new TaskbarRecreationMonitor(window);
        var trayPresentation = composition.Presentation is BetaAnalyticsPresentation analyticsPresentation ? analyticsPresentation.QuotaPresentation : composition.Presentation as QuotaPresentationHost;
        _runtime = new TrayHostRuntime(_instance!, new WindowsTrayRuntime(), new WpfPopoverRuntime(window), _taskbar,
            _ => Task.CompletedTask, composition.Resource, Shutdown, composition.RefreshCommand, composition.Settings, composition.ReportUnavailable, composition.Reevaluate, trayPresentation, new WindowsPrivateIntegrationConsentPrompt(), new WindowsOpenCodeDataFolderPicker());
        _runtime.Start();
    }
    private static void ReportFault(Exception exception) => Trace.TraceError("AIBar unavailable: {0}", exception.GetType().Name);
    internal sealed record StartupComposition(object Presentation, IManualRefreshCommand? RefreshCommand, NativeSettingsCommands? Settings, IAsyncDisposable Resource, Func<Task> Initialize, Action ReportUnavailable, Func<RefreshTrigger, CancellationToken, ValueTask>? Reevaluate = null, Func<CancellationToken, ValueTask<LocalUsageCoordinatorResult>>? RefreshLocalUsage = null)
    {
        internal StartupComposition(object presentation, IManualRefreshCommand? refreshCommand, IAsyncDisposable resource, Func<Task> initialize, Action reportUnavailable)
            : this(presentation, refreshCommand, null, resource, initialize, reportUnavailable) { }
        internal static StartupComposition Unavailable => new(new UnavailableQuotaPresentation(), null, null, new EmptyAsyncResource(), () => Task.CompletedTask, () => { });
    }

    internal sealed record CompositionSeams(string? DataDirectory = null, string? CodexHome = null, TimeSpan? AnalyticsShutdownBound = null, Action<string>? BeforeAnalyticsStability = null, Action<string>? LifecycleObservation = null, string? UserProfile = null, IQuotaExportWriter? ExportWriter = null);
    private sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    internal sealed class QuotaRuntimeResource(QuotaPresentationHost presentation, QuotaRefreshLifecycleAdapter lifecycleAdapter, WindowsLifecycleEvents lifecycleEvents, BetaRuntime runtime, QuotaExportPublisher publisher, AnalyticsLifecycleOwner analyticsOwner, IAsyncDisposable store, LocalUsageCoordinator localUsage, IAsyncDisposable localUsageLedger, Action<string>? observe) : IAsyncDisposable
    {
        public AnalyticsShutdownOutcome AnalyticsOutcome => analyticsOwner.Outcome;
        public ValueTask DisposeAsync() => DisposeAllAsync([
            lifecycleAdapter.DisposeAsync, presentation.DisposeAsync,
            () => { lifecycleEvents.Dispose(); return ValueTask.CompletedTask; },
            () => DisposeResourceAsync(publisher, observe, "quota_export_publisher_disposed"), runtime.DisposeAsync, () => DisposeResourceAsync(localUsage, observe, "local_usage_coordinator_disposed"),
            () => DisposeResourceAsync(localUsageLedger, observe, "local_usage_ledger_disposed"),
            () => DisposeResourceAsync(store, observe, "quota_store_disposed"), analyticsOwner.DisposeAsync]);

        private static async ValueTask DisposeResourceAsync(IAsyncDisposable resource, Action<string>? observe, string label) { await resource.DisposeAsync(); observe?.Invoke(label); }

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
