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

        _ = StartPrimary(CreateComposition, StartTray, ReportFault, Shutdown);
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
    private static StartupComposition CreateComposition()
    {
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar");
        Directory.CreateDirectory(dataDirectory);
        var store = new SqliteQuotaSnapshotStore(Path.Combine(dataDirectory, "quota.db"));
        var policy = new PrivateIntegrationPolicy();
        var coordinator = new QuotaRefreshCoordinator(store, new QuotaHttpProvider(policy, _ => ValueTask.FromResult<RequestCredential?>(null)), new SystemClock(), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
        var presentation = new QuotaPresentationHost(coordinator, new QuotaPresentationMapper(new SystemClock()), policy.IsEnabled, ReportFault);
        return new(presentation, policy.IsEnabled ? presentation.RefreshCommand : null, new QuotaRuntimeResource(presentation, coordinator, store), () => presentation.InitializeAsync(default).AsTask(), presentation.ReportUnavailable);
    }
    private void StartTray(StartupComposition composition)
    {
        var window = new MainWindow { DataContext = composition.Presentation, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        _taskbar = new TaskbarRecreationMonitor(window);
        _runtime = new TrayHostRuntime(_instance!, new WindowsTrayRuntime(), new WpfPopoverRuntime(window), _taskbar,
            _ => Task.CompletedTask, composition.Resource, Shutdown, composition.RefreshCommand);
        _runtime.Start();
    }
    private static void ReportFault(Exception exception) => Trace.TraceError("AIBar unavailable: {0}", exception.GetType().Name);
    internal sealed record StartupComposition(object Presentation, IManualRefreshCommand? RefreshCommand, IAsyncDisposable Resource, Func<Task> Initialize, Action ReportUnavailable)
    { internal static StartupComposition Unavailable => new(new UnavailableQuotaPresentation(), null, new EmptyAsyncResource(), () => Task.CompletedTask, () => { }); }

    private sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    private sealed class QuotaRuntimeResource(QuotaPresentationHost presentation, QuotaRefreshCoordinator coordinator, IAsyncDisposable store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { await presentation.DisposeAsync(); await coordinator.DisposeAsync(); await store.DisposeAsync(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbar?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
