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
    private DockWindow? _dock;
    private DockSettings? _dockSettings;
    private Action<DockOptions>? _dockChanged;

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

        _ = StartPrimary(() => CreateComposition(), StartTray, ReportFault, Shutdown, showRequested ? RequestShowAfterStartup : null, RunStartupOffersAsync);
    }

    internal static async Task StartPrimary(Func<StartupComposition> compose, Action<StartupComposition> startTray, Action<Exception> report, Action shutdown, Action? showWhenReady = null, Func<StartupComposition, Task>? afterInitialize = null)
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
        try { await composition.Initialize(); showWhenReady?.Invoke(); if (afterInitialize is not null) await afterInitialize(composition); }
        catch (Exception exception) { report(exception); composition.ReportUnavailable(); }
    }
    private void RequestShowAfterStartup() => _runtime?.RequestShow();
    internal static StartupComposition CreateComposition(CompositionSeams? seams = null)
    {
        seams ??= new();
        var dataDirectory = seams.DataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar");
        Directory.CreateDirectory(dataDirectory);
        var userProfile = seams.UserProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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
        var analyticsStore = new SqliteDailyModelUsageStore(Path.Combine(dataDirectory, "analytics.db"));
        var analytics = new LocalCodexAnalyticsView();
        var analyticsOwner = new AnalyticsLifecycleOwner(new LocalCodexAnalyticsAdapter(
            new SessionFileDiscovery(codexHome, userProfile, 50),
            new AnalyticsScanCoordinator(new SessionJsonlScanner(new SessionCheckpointStore(), "beta-v1", beforeStable: seams.BeforeAnalyticsStability), analyticsStore, new AnalyticsPolicy(new TimeZoneLocalDayPolicy(TimeZoneInfo.Local, "beta-v1"))),
            analyticsStore, () => clock.UtcNow), analytics, analyticsStore, seams.AnalyticsShutdownBound ?? TimeSpan.FromSeconds(2), seams.LifecycleObservation);
        var codexRemotePath = Path.Combine(dataDirectory, "codex-remote-usage.json");
        var betaRuntime = new BetaRuntime(new ConsentSettings(Path.Combine(dataDirectory, "settings.json")), policy, coordinator, clock,
            credentials, analyticsOwner, publisher,
            remoteProviderFactory: codexPolicy => new CodexRemoteUsageProvider(codexPolicy, codexRemotePath, utcNow: () => clock.UtcNow),
            remoteFileAvailable: () => CodexRemoteUsageProvider.IsAvailableForAutomatic(codexRemotePath),
            remoteAbsenceConfirmation: TimeSpan.FromSeconds(2));
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
        var claudeCost = new UsageCostView(() => claudeRuntime.IsConsentEnabled && claudeRuntime.Source != ClaudeQuotaSource.ThisPc,
            token => ClaudeRemoteTokens.ReadAsync(Path.Combine(dataDirectory, "claude-remote-tokens.json"), clock.UtcNow, token));
        claudeRuntime.ConsentChanged += () => { _ = claudeCost.RefreshAsync(default); };
        claudeRuntime.StateChanged += () => { _ = claudeCost.RefreshAsync(default); };
        claudeRuntime.SourceChanged += () => { _ = claudeCost.RefreshForSourceAsync(default); };
        var codexCost = new UsageCostView(() => policy.IsEnabled, async token =>
        {
            var source = betaRuntime.Source;
            var remote = source == DataSource.ThisPc ? RemoteTokenRows.Unavailable :
                await RemoteTokens.ReadRowsAsync(Path.Combine(dataDirectory, "codex-remote-tokens.json"), clock.UtcNow, token);
            return RemoteTokens.Combine(analytics.State, remote, DateOnly.FromDateTime(clock.UtcNow.LocalDateTime), source);
        });
        betaRuntime.SourceChanged += () => { _ = codexCost.RefreshForSourceAsync(default); };
        analytics.StateChanged += state => { _ = codexCost.RefreshAsync(default); };
        coordinator.StateChanged += state => { _ = codexCost.RefreshAsync(default); };
        var appearance = new AppearanceSettings(Path.Combine(dataDirectory, "appearance.json"));
        var dock = new DockSettings(Path.Combine(dataDirectory, DockSettings.FileName));
        var onboarding = new OnboardingSettings(Path.Combine(dataDirectory, OnboardingSettings.FileName));
        var alerts = new QuotaAlertObserver(dataDirectory, clock);
        coordinator.AuthorityUpdated += update => alerts.Observe("Codex", update,
            betaRuntime.CaptureAlertEligibility(update));
        claudeRuntime.AuthorityUpdated += update => alerts.Observe("Claude", update,
            () => claudeRuntime.IsConsentEnabled && claudeRuntime.State.Freshness == FreshnessState.Current && claudeRuntime.State.Failure is null);
        var lifecycleAdapter = new QuotaRefreshLifecycleAdapter(lifecycleEvents, coordinator);
        var claudePresentation = new ClaudeQuotaPresentationHost(claudeRuntime, new ClaudeQuotaPresentationMapper(clock), paceHistory);
        var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(clock), () => policy.IsEnabled, ReportFault, lifecycleEvents, claudePresentation, paceHistory, betaRuntime, betaRuntime);
        var providerStatus = new ProviderStatusPresentation(presentation, claudePresentation, () => policy.IsEnabled);
        var startup = new PerUserStartupRegistration(new WindowsPackagedStartupTaskRegistration("AIBar"), new WindowsCurrentUserRunStore(), "AIBar", Environment.ProcessPath ?? throw new InvalidOperationException());
        var clear = new ClearAiBarDataService(dataDirectory, [paceHistory, codexCost, claudeCost, claudeRuntime, publisher, coordinator, historyRecorder, historyStore, alerts, appearance, dock, onboarding, analyticsOwner], CreateEmptyStateFactory(coordinator, analytics, publisher));
        return new(new BetaAnalyticsPresentation(presentation, analytics, claude: claudePresentation, claudeCost: claudeCost, codexCost: codexCost), presentation.RefreshCommand, new NativeSettingsCommands(startup, clear, policy,
                async token => { await betaRuntime.RevokeConsentAsync(token); await codexCost.RefreshAsync(token); },
                async token => { await betaRuntime.GrantConsentAsync(token); await codexCost.RefreshAsync(token); }),
            new QuotaRuntimeResource(presentation, lifecycleAdapter, lifecycleEvents, betaRuntime, publisher, analyticsOwner, store, seams.LifecycleObservation, claudeRuntime, historyRecorder, historyStore, claudeCost, providerStatus, alerts, paceHistory, codexCost),
            async () => { await Task.WhenAll(InitializeCompositionAsync(betaRuntime, presentation, default), claudeRuntime.InitializeAsync(default).AsTask()); await claudeCost.RefreshAsync(default); await codexCost.RefreshAsync(default); }, presentation.ReportUnavailable,
            async (trigger, token) =>
            {
                // Runtime entry points enforce each provider's consent independently.
                // Start both before awaiting so a slow network request cannot delay the local remote file.
                await Task.WhenAll(betaRuntime.RefreshAsync(trigger, token).AsTask(), claudeRuntime.RefreshAsync(trigger, token).AsTask());
                await claudeCost.RefreshAsync(token);
                await codexCost.RefreshAsync(token);
            })
            { CodexQuota = betaRuntime, ClaudeQuota = claudeRuntime, QuotaHistory = historyStore, ProviderStatus = providerStatus, Alerts = alerts, Appearance = appearance, PaceHistory = paceHistory, Dock = dock, Onboarding = onboarding, UserProfile = userProfile };
    }
    internal static Task InitializeCompositionAsync(BetaRuntime runtime, QuotaPresentationHost presentation, CancellationToken cancellationToken) =>
        InitializePrivateAsync(runtime, presentation, cancellationToken);
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
            var providerSettings = composition.Settings is { } native && composition.ClaudeQuota is { } claude && composition.CodexQuota is { } codex
                ? SettingsProviderCommands.Create(native, claude, codex) : null;
            _settingsWindowHost = new(appearance, settingsAlerts, providerSettings,
                () => tray.ShowAlert("AIBar · Window reset", "A session or weekly window starts over."), composition.Dock, SettingsGlyphFont.Create());
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
            _ => Task.CompletedTask, composition.Resource, Shutdown, composition.RefreshCommand, composition.Settings, composition.Reevaluate, trayPresentation);
        if (composition.Alerts is { } alerts)
            _alertController = new(tray, alerts, Dispatcher, () => _runtime.RequestShow(),
                cards: new NotificationStack(() => _runtime.RequestShow(), () => window.Width <= 300)) { BackgroundOpacity = window.BackgroundOpacity };
        if (composition.Dock is { } dockSettings)
        {
            var dock = new DockWindow(composition.Presentation, new DockOptions(), () => _runtime.RequestShow(), dockSettings.SaveAsync);
            _dock = dock; _dockSettings = dockSettings;
            _dockChanged = options =>
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(_dock, dock)) dock.Apply(options);
                }));
            };
            dockSettings.Changed += _dockChanged;
            _ = LoadDockAsync(dockSettings, dock);
        }
        _runtime.Start();
        composition.ProviderStatus?.Start();
    }
    private async Task LoadDockAsync(DockSettings settings, DockWindow dock)
    {
        try
        {
            var options = await settings.LoadAsync(default);
            if (ReferenceEquals(_dock, dock)) dock.Apply(options);
        }
        catch (Exception error) { ReportFault(error); }
    }

    private async Task RunStartupOffersAsync(StartupComposition composition)
    {
        if (composition.Dock is not { } dock || composition.Onboarding is not { } onboarding) return;
        var font = SettingsGlyphFont.Create();
        await new StartupOffers(() => YasbPresence.IsDetected(composition.UserProfile!), font.IsInstalled, font.IsBundledFontAvailable,
            async token => (await dock.LoadAsync(token)).Enabled, onboarding, new WindowsStartupOfferPrompt(), font.Install,
            async token => await dock.SaveAsync((await dock.LoadAsync(token)) with { Enabled = true }, token), ReportFault).RunAsync(default);
    }

    private static void ReportFault(Exception exception) => Trace.TraceError("AIBar unavailable: {0}", exception.GetType().Name);
    internal sealed record StartupComposition(object Presentation, IManualRefreshCommand? RefreshCommand, NativeSettingsCommands? Settings, IAsyncDisposable Resource, Func<Task> Initialize, Action ReportUnavailable, Func<RefreshTrigger, CancellationToken, ValueTask>? Reevaluate = null)
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
        internal DockSettings? Dock { get; init; }
        internal OnboardingSettings? Onboarding { get; init; }
        internal string? UserProfile { get; init; }
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
    internal sealed class QuotaRuntimeResource(QuotaPresentationHost presentation, QuotaRefreshLifecycleAdapter lifecycleAdapter, WindowsLifecycleEvents lifecycleEvents, BetaRuntime runtime, QuotaExportPublisher publisher, AnalyticsLifecycleOwner analyticsOwner, IAsyncDisposable store, Action<string>? observe, ClaudeQuotaRuntime? claudeRuntime = null, QuotaHistoryRecorder? historyRecorder = null, SqliteQuotaHistoryStore? historyStore = null, UsageCostView? claudeCost = null, ProviderStatusPresentation? providerStatus = null, QuotaAlertObserver? alerts = null, PanelPaceHistory? paceHistory = null, UsageCostView? codexCost = null) : IAsyncDisposable
    {
        public AnalyticsShutdownOutcome AnalyticsOutcome => analyticsOwner.Outcome;
        public ValueTask DisposeAsync() => DisposeAllAsync([
            () => { paceHistory?.Dispose(); return ValueTask.CompletedTask; },
            () => providerStatus?.DisposeAsync() ?? ValueTask.CompletedTask,
            lifecycleAdapter.DisposeAsync, presentation.DisposeAsync,
            () => codexCost?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => claudeCost?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => claudeRuntime?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => alerts?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => { lifecycleEvents.Dispose(); return ValueTask.CompletedTask; },
            () => DisposeResourceAsync(publisher, observe, "quota_export_publisher_disposed"), runtime.DisposeAsync,
            () => historyRecorder?.DisposeAsync() ?? ValueTask.CompletedTask,
            () => historyStore?.DisposeAsync() ?? ValueTask.CompletedTask,
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
        if (_dockSettings is { } dockSettings && _dockChanged is { } changed) dockSettings.Changed -= changed;
        var dock = _dock; _dock = null; dock?.Close();
        _settingsWindowHost?.Dispose();
        _alertController?.Dispose();
        _theme?.Dispose();
        _taskbar?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
