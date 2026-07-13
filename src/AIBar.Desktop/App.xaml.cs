using System.Windows;

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

        var window = new MainWindow { ShowInTaskbar = false, WindowStyle = WindowStyle.None };
        _taskbar = new TaskbarRecreationMonitor(window);
        _runtime = new TrayHostRuntime(_instance, new WindowsTrayRuntime(), new WpfPopoverRuntime(window), _taskbar,
            _ => Task.CompletedTask, new EmptyAsyncResource(), Shutdown);
        _runtime.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbar?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
