using System.Windows;
using System.Windows.Threading;
using AIBar.Desktop;
namespace AIBar.Domain.Tests;
internal static class WpfTestApplicationHost
{
    private static readonly Lazy<Host> Shared = new(() => new Host(), LazyThreadSafetyMode.ExecutionAndPublication);
    public static void Run(Action<App> callback) => Shared.Value.Invoke(callback);

    private sealed class Host
    {
        private readonly TaskCompletionSource<(App Application, Dispatcher Dispatcher)> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Host() { var thread = new Thread(Start) { IsBackground = true, Name = "AIBar WPF test application host" }; thread.SetApartmentState(ApartmentState.STA); thread.Start(); }
        public void Invoke(Action<App> callback)
        {
            ArgumentNullException.ThrowIfNull(callback); var (application, dispatcher) = _ready.Task.GetAwaiter().GetResult();
            if (dispatcher.CheckAccess()) callback(application); else dispatcher.Invoke(() => callback(application));
        }
        private void Start()
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher; var application = new App(suppressHostStartup: true);
                application.InitializeComponent(); application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _ready.SetResult((application, dispatcher)); Dispatcher.Run();
            }
            catch (Exception exception) { _ready.TrySetException(exception); }
        }
    }
}
