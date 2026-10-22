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
    private IThemeController? _theme;
    private QuotaAlertTrayController? _alertController;
    private SettingsWindowHost? _settingsWindowHost;

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
            ForegroundActivation.Request(ForegroundActivation.TryGrantToPrimary, _instance.RequestActivation);
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
        var codexProvider = new QuotaHttpProvider(policy, credentials);
        var coordinator = new QuotaRefreshCoordinator(store, codexProvider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        var historyStore = new SqliteQuotaHistoryStore(SqliteQuotaHistoryStore.DatabasePath(dataDirectory));
        var paceHistory = new PanelPaceHistory(historyStore, clock);
        var historyRecorder = new QuotaHistoryRecorder(coordinator, historyStore, clock, QuotaHistoryProvider.Codex, QuotaHistorySource.CodexLocal);
        var exportWriter = seams.ExportWriter ?? (seams.DataDirectory is null
            ? new WindowsAtomicQuotaExportWriter()
            : WindowsAtomicQuotaExportWriter.ForOwnedSyntheticDataDirectory(dataDirectory));
        var publisher = new QuotaExportPublisher(coordinator, exportWriter, clock);
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
        var claudeRuntime = new ClaudeQuotaRuntime(new ConsentSettings(Path.Combine(dataDirectory, "settings.json")),
            new SqliteQuotaSnapshotStore(ClaudeQuotaRuntime.DatabasePath(dataDirectory)),
            seams.ClaudeProviderFactory ?? (claudePolicy => new ClaudeQuotaHttpProvider(claudePolicy,
                new ClaudeConsentCredentialSource(claudePolicy, new ClaudeCredentialReader(new ReadOnlyCredentialFileReader()),
                    ClaudeRootResolver.Resolve(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"), userProfile)))),
            clock, lifecycleEvents, seams.ClaudeTimeProvider,
            seams.ClaudeExportWriter ?? (seams.DataDirectory is null
                ? WindowsAtomicQuotaExportWriter.ForClaudeCurrentUser()
                : WindowsAtomicQuotaExportWriter.ForClaudeOwnedSyntheticDataDirectory(dataDirectory)),
            seams.ClaudeRemoteProviderFactory ?? (claudePolicy => new ClaudeRemoteUsageProvider(claudePolicy,
                Path.Combine(dataDirectory, "claude-remote-usage.json"))),
            () => ClaudeRemoteUsageProvider.IsAvailableForAutomatic(Path.Combine(dataDirectory, "claude-remote-usage.json")), historyStore: historyStore,
            remoteRefresh: new ClaudeRemoteRefreshChannel(dataDirectory), remoteAbsenceConfirmation: TimeSpan.FromSeconds(2));
        var claudeCost = new UsageCostView(() => claudeRuntime.IsConsentEnabled,
            token => ClaudeRemoteTokens.ReadAsync(Path.Combine(dataDirectory, "claude-remote-tokens.json"), clock.UtcNow, token));
        claudeRuntime.ConsentChanged += () => { _ = claudeCost.RefreshAsync(default); };
        claudeRuntime.StateChanged += () => { _ = claudeCost.RefreshAsync(default); };
        var appearance = new AppearanceSettings(Path.Combine(dataDirectory, "appearance.json"));
        var alerts = new QuotaAlertObserver(dataDirectory, clock);
        coordinator.AuthorityUpdated += update => alerts.Observe("Codex", update,
            () => policy.IsEnabled && coordinator.State.Freshness == FreshnessState.Current && coordinator.State.Failure is null);
        claudeRuntime.AuthorityUpdated += update => alerts.Observe("Claude", update,
            () => claudeRuntime.IsConsentEnabled && claudeRuntime.State.Freshness == FreshnessState.Current && claudeRuntime.State.Failure is null);
        var lifecycleAdapter = new QuotaRefreshLifecycleAdapter(lifecycleEvents, coordinator);
        var claudePresentation = new ClaudeQuotaPresentationHost(claudeRuntime, new ClaudeQuotaPresentationMapper(clock), paceHistory);
        var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock), () => policy.IsEnabled, ReportFault, lifecycleEvents, claudePresentation, paceHistory, codexProvider);
        var localUsagePresentation = new LocalUsagePresentationHost(localUsage, new LocalUsagePresentationMapper());
        var providerStatus = new ProviderStatusPresentation(presentation, claudePresentation, () => policy.IsEnabled);
        var startup = new PerUserStartupRegistration(new WindowsPackagedStartupTaskRegistration("AIBar"), new WindowsCurrentUserRunStore(), "AIBar", Environment.ProcessPath ?? throw new InvalidOperationException());
        var clear = new ClearAiBarDataService(dataDirectory, [paceHistory, claudeCost, claudeRuntime, publisher, coordinator, historyRecorder, historyStore, alerts, appearance, localUsage, analyticsOwner], CreateEmptyStateFactory(coordinator, analytics, publisher));
        return new(new BetaAnalyticsPresentation(presentation, analytics, localUsagePresentation, claudePresentation, claudeCost), presentation.RefreshCommand, new NativeSettingsCommands(startup, clear, policy, betaRuntime.RevokeConsentAsync, betaRuntime.GrantConsentAsync,
                localUsageSettings.LoadAsync, localUsageSettings.SaveAsync, async (value, token) => { await localUsagePresentation.ApplyPolicyAsync(value, token); }),
            new QuotaRuntimeResource(presentation, lifecycleAdapter, lifecycleEvents, betaRuntime, publisher, analyticsOwner, store, localUsage, localUsageLedger, seams.LifecycleObservation, claudeRuntime, historyRecorder, historyStore, claudeCost, providerStatus, alerts, paceHistory),
            async () => { await Task.WhenAll(InitializeCompositionAsync(betaRuntime, presentation, localUsagePresentation, default), claudeRuntime.InitializeAsync(default).AsTask()); await claudeCost.RefreshAsync(default); }, presentation.ReportUnavailable,
            async (trigger, token) =>
            {
                // Runtime entry points enforce each provider's consent independently.
                // Start both before awaiting so a slow network request cannot delay the local remote file.
                await Task.WhenAll(betaRuntime.RefreshAsync(trigger, token).AsTask(), claudeRuntime.RefreshAsync(trigger, token).AsTask());
                if (trigger == RefreshTrigger.PopoverOpened) await localUsagePresentation.RefreshAsync(token);
                await claudeCost.RefreshAsync(token);
            }, localUsagePresentation.RefreshAsync)
            { CodexQuota = betaRuntime, ClaudeQuota = claudeRuntime, QuotaHistory = historyStore, ProviderStatus = providerStatus, Alerts = alerts, Appearance = appearance, PaceHistory = paceHistory };
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
        var window = new MainWindow { Title = ForegroundActivation.WindowTitle, DataContext = composition.Presentation, ProviderStatus = composition.ProviderStatus, PaceHistory = composition.PaceHistory, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        var themeSource = new WindowsThemeSource();
        _theme = new WindowsThemeController(Resources, themeSource, Dispatcher);
        _taskbar = new TaskbarRecreationMonitor(window);
        var trayPresentation = composition.Presentation is BetaAnalyticsPresentation analyticsPresentation ? analyticsPresentation.QuotaPresentation : composition.Presentation as QuotaPresentationHost;
        var tray = new WindowsTrayRuntime();
        if (composition.Appearance is { } appearance && composition.Alerts is { } settingsAlerts)
        {
            var providerSettings = composition.Settings is { } native && composition.ClaudeQuota is { } claude
                ? SettingsProviderCommands.Create(native, claude) : null;
            _settingsWindowHost = new(appearance, settingsAlerts, providerSettings,
                () => tray.ShowAlert("AIBar · Window reset", "A session or weekly window starts over."));
            // The colour choice applies live; Changed can run during clear, so never wait on the UI thread.
            window.AppearanceSettings = appearance;
            appearance.Changed += options => Dispatcher.BeginInvoke(new Action(() => { _ = ApplyAppearance(options, window, composition); }));
            _ = ApplyInitialAppearanceAsync(appearance, window, composition);
            window.SettingsRequested += () => _settingsWindowHost.Show(window);
            tray.SettingsRequested += () => _settingsWindowHost.Show(window);
        }
        else window.SettingsRequested += () => tray.ShowSettingsMenu(window);
        window.QuitRequested += tray.RequestExit;
        window.UseOtherMachineRequested += async () =>
        {
            if (composition.ClaudeQuota is not { } claude) return;
            try { await claude.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default); }
            catch (Exception error) { ReportFault(error); }
        };
        _runtime = new TrayHostRuntime(_instance!, tray, new WpfPopoverRuntime(window, _theme, () => WindowsPopoverPlacementContextProvider.Get(window), null, () => themeSource.Current), _taskbar,
            _ => Task.CompletedTask, composition.Resource, Shutdown, composition.RefreshCommand, composition.Settings, composition.ReportUnavailable, composition.Reevaluate, trayPresentation, new WindowsPrivateIntegrationConsentPrompt(), new WindowsOpenCodeDataFolderPicker(), composition.ClaudeQuota);
        if (composition.Alerts is { } alerts)
            _alertController = new(tray, alerts, Dispatcher, () => _runtime.RequestShow(),
                cards: new NotificationStack(() => _runtime.RequestShow(), () => window.Width <= 300)) { BackgroundOpacity = window.BackgroundOpacity };
        _runtime.Start();
        composition.ProviderStatus?.Start();
    }
    private static void ReportFault(Exception exception) => Trace.TraceError("AIBar unavailable: {0}", exception.GetType().Name);
    internal sealed record StartupComposition(object Presentation, IManualRefreshCommand? RefreshCommand, NativeSettingsCommands? Settings, IAsyncDisposable Resource, Func<Task> Initialize, Action ReportUnavailable, Func<RefreshTrigger, CancellationToken, ValueTask>? Reevaluate = null, Func<CancellationToken, ValueTask<LocalUsageCoordinatorResult>>? RefreshLocalUsage = null)
    {
        internal StartupComposition(object presentation, IManualRefreshCommand? refreshCommand, IAsyncDisposable resource, Func<Task> initialize, Action reportUnavailable)
            : this(presentation, refreshCommand, null, resource, initialize, reportUnavailable) { }
        // Unavailable/legacy compositions have no Claude capability; production always supplies it.
        internal BetaRuntime? CodexQuota { get; init; }
        internal ClaudeQuotaRuntime ClaudeQuota { get; init; } = null!;
        internal IQuotaHistoryStore? QuotaHistory { get; init; }
        internal PanelPaceHistory? PaceHistory { get; init; }
        internal ProviderStatusPresentation? ProviderStatus { get; init; }
        internal QuotaAlertObserver? Alerts { get; init; }
        internal AppearanceSettings? Appearance { get; init; }
        internal static StartupComposition Unavailable => new(new UnavailableQuotaPresentation(), null, null, new EmptyAsyncResource(), () => Task.CompletedTask, () => { });
    }

    internal static double ToBackgroundOpacity(AppearanceOptions options) => Math.Clamp(options.BackgroundOpacity, 30, 100) / 100d;

    // Only the window backgrounds take the opacity; text, bars and buttons stay opaque.
    private async Task ApplyAppearance(AppearanceOptions options, MainWindow window, StartupComposition composition)
    {
        window.BarColorMode = ToBarColorMode(options);
        window.ApplyAppearanceOptions(options);
        window.BackgroundOpacity = ToBackgroundOpacity(options);
        if (_alertController is { } controller) controller.BackgroundOpacity = window.BackgroundOpacity;
        try { await ApplyRefreshIntervalAsync(options, composition.CodexQuota, composition.ClaudeQuota); }
        catch (ObjectDisposedException) { /* A queued appearance change may outlive shutdown. */ }
        catch (Exception error) { ReportFault(error); }
    }

    internal static async Task ApplyRefreshIntervalAsync(AppearanceOptions options, BetaRuntime? codex, ClaudeQuotaRuntime? claude)
    {
        codex?.SetRefreshMinutes(options.RefreshMinutes);
        if (claude is not null) await claude.SetRefreshMinutesAsync(options.RefreshMinutes).ConfigureAwait(false);
    }

    internal static PanelBarColorMode ToBarColorMode(AppearanceOptions options) =>
        options.BarColors == "usage" ? PanelBarColorMode.Usage : PanelBarColorMode.Brand;

    private async Task ApplyInitialAppearanceAsync(AppearanceSettings appearance, MainWindow window, StartupComposition composition)
    {
        try { await ApplyAppearance(await appearance.LoadAsync(default), window, composition); }
        catch (Exception) { /* Unreadable appearance keeps the default brand colours. */ }
    }

    internal sealed record CompositionSeams(string? DataDirectory = null, string? CodexHome = null, TimeSpan? AnalyticsShutdownBound = null, Action<string>? BeforeAnalyticsStability = null, Action<string>? LifecycleObservation = null, string? UserProfile = null, IQuotaExportWriter? ExportWriter = null, Func<PrivateIntegrationPolicy, IQuotaProvider>? ClaudeProviderFactory = null, TimeProvider? ClaudeTimeProvider = null, IQuotaExportWriter? ClaudeExportWriter = null, Func<PrivateIntegrationPolicy, IQuotaProvider>? ClaudeRemoteProviderFactory = null);
    private sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    internal sealed class QuotaRuntimeResource(QuotaPresentationHost presentation, QuotaRefreshLifecycleAdapter lifecycleAdapter, WindowsLifecycleEvents lifecycleEvents, BetaRuntime runtime, QuotaExportPublisher publisher, AnalyticsLifecycleOwner analyticsOwner, IAsyncDisposable store, LocalUsageCoordinator localUsage, IAsyncDisposable localUsageLedger, Action<string>? observe, ClaudeQuotaRuntime? claudeRuntime = null, QuotaHistoryRecorder? historyRecorder = null, SqliteQuotaHistoryStore? historyStore = null, UsageCostView? claudeCost = null, ProviderStatusPresentation? providerStatus = null, QuotaAlertObserver? alerts = null, PanelPaceHistory? paceHistory = null) : IAsyncDisposable
    {
        public AnalyticsShutdownOutcome AnalyticsOutcome => analyticsOwner.Outcome;
        public ValueTask DisposeAsync() => DisposeAllAsync([
            () => { paceHistory?.Dispose(); return ValueTask.CompletedTask; },
            () => providerStatus?.DisposeAsync() ?? ValueTask.CompletedTask,
            lifecycleAdapter.DisposeAsync, presentation.DisposeAsync,
            () => claudeCost?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => claudeRuntime?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => alerts?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => { lifecycleEvents.Dispose(); return ValueTask.CompletedTask; },
            () => DisposeResourceAsync(publisher, observe, "quota_export_publisher_disposed"), runtime.DisposeAsync,
            () => historyRecorder?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => historyStore?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => DisposeResourceAsync(localUsage, observe, "local_usage_coordinator_disposed"),
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
        _settingsWindowHost?.Dispose();
        _alertController?.Dispose();
        _theme?.Dispose();
        _taskbar?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
