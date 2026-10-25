using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

public interface ITrayRuntime : IAsyncDisposable
{
    event Action? Toggled;
    event Action? ExitRequested;
    event Action? RefreshRequested;
    event Action? StartupToggleRequested;
    void SetRefreshAvailable(bool available);
    void SetPresentation(BetaPresentationState state);
    void SetSettingsAvailable(bool available);
    void SetStartupEnabled(bool enabled);
    void Show();
    void Hide();
}

// Optional capability: existing ITrayRuntime implementations remain source-compatible.
public interface IClaudeQuotaTray
{
    event Action? ClaudeQuotaEnableRequested;
    event Action? ClaudeQuotaDisableRequested;
    void SetClaudeQuotaEnabled(bool enabled);
}

// Separate optional capability keeps existing Claude/Codex tray fakes source-compatible.
public interface IClaudeQuotaSourceTray
{
    event Action<ClaudeQuotaSource>? ClaudeSourceRequested;
    void SetClaudeSource(ClaudeQuotaSource source);
}

/// <summary>Binds only the optional Claude capability; never changes Codex consent.</summary>
public sealed class ClaudeQuotaTrayController : IDisposable
{
    private readonly IClaudeQuotaTray _tray;
    private readonly ClaudeQuotaRuntime _runtime;
    private readonly IPrivateIntegrationConsentPrompt _prompt;
    private int _granting;
    private bool _disposed;

    public ClaudeQuotaTrayController(IClaudeQuotaTray tray, ClaudeQuotaRuntime runtime, IPrivateIntegrationConsentPrompt prompt)
    {
        _tray = tray; _runtime = runtime; _prompt = prompt;
        _tray.ClaudeQuotaEnableRequested += OnEnable; _tray.ClaudeQuotaDisableRequested += OnDisable;
        _runtime.ConsentChanged += Update;
        _runtime.SourceChanged += Update;
        if (_tray is IClaudeQuotaSourceTray sourceTray) sourceTray.ClaudeSourceRequested += OnSource;
        Update();
    }
    public async ValueTask EnableAsync(CancellationToken cancellationToken)
    {
        if (_disposed || Interlocked.CompareExchange(ref _granting, 1, 0) != 0) return;
        try { if (!_runtime.IsConsentEnabled && _prompt.Confirm()) await _runtime.GrantConsentAsync(cancellationToken); }
        finally { Volatile.Write(ref _granting, 0); Update(); }
    }
    public async ValueTask SetSourceAsync(ClaudeQuotaSource source, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        try { await _runtime.SetSourceAsync(source, cancellationToken); }
        finally { Update(); }
    }
    private async void OnSource(ClaudeQuotaSource source)
    {
        try { await SetSourceAsync(source, CancellationToken.None); } catch (Exception) { }
    }

    public async ValueTask DisableAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;
        try { await _runtime.RevokeConsentAsync(cancellationToken); }
        finally { Update(); }
    }
    private async void OnEnable() { try { await EnableAsync(CancellationToken.None); } catch (Exception) { } }
    private async void OnDisable() { try { await DisableAsync(CancellationToken.None); } catch (Exception) { } }
    private void Update()
    {
        if (_disposed) return;
        _tray.SetClaudeQuotaEnabled(_runtime.IsConsentEnabled);
        if (_tray is IClaudeQuotaSourceTray sourceTray) sourceTray.SetClaudeSource(_runtime.Source);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tray.ClaudeQuotaEnableRequested -= OnEnable; _tray.ClaudeQuotaDisableRequested -= OnDisable;
        _runtime.ConsentChanged -= Update;
        _runtime.SourceChanged -= Update;
        if (_tray is IClaudeQuotaSourceTray sourceTray) sourceTray.ClaudeSourceRequested -= OnSource;
    }
}

public sealed class WindowsClaudeQuotaConsentPrompt : IPrivateIntegrationConsentPrompt
{
    public bool Confirm() => Forms.MessageBox.Show(ClaudeQuotaRuntime.ConsentDisclosure, "Enable Claude subscription quota",
        Forms.MessageBoxButtons.OKCancel, Forms.MessageBoxIcon.Warning, Forms.MessageBoxDefaultButton.Button2) == Forms.DialogResult.OK;
}

public interface IPopoverRuntime
{
    event Action? Deactivated;
    bool IsVisible { get; }
    bool IsOwnedDialogActive { get; }
    void Show();
    void Hide();
    void Activate();
}

public interface ITaskbarRecreationEvents { event Action? Recreated; }

public interface IPrivateIntegrationConsentPrompt { bool Confirm(); }

public sealed class TrayHostRuntime : IAsyncDisposable
{
    private readonly SingleInstanceHost _instance;
    private readonly ITrayRuntime _tray;
    private readonly IPopoverRuntime _popover;
    private readonly ITaskbarRecreationEvents _taskbar;
    private readonly Func<CancellationToken, Task> _awaitCancelledWork;
    private readonly IAsyncDisposable _persistence;
    private readonly Action _exitProcess;
    private readonly IManualRefreshCommand? _refreshCommand;
    private readonly NativeSettingsCommands? _settings;
    private readonly Func<RefreshTrigger, CancellationToken, ValueTask>? _reevaluate;
    private readonly QuotaPresentationHost? _presentation;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _activationTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Task? _cleanupTask;
    private Task? _exitTask;
    private bool _started;
    private bool _showPending;
    private bool _disposed;

    public TrayHostRuntime(SingleInstanceHost instance, ITrayRuntime tray, IPopoverRuntime popover, ITaskbarRecreationEvents taskbar, Func<CancellationToken, Task> awaitCancelledWork, IAsyncDisposable persistence, Action exitProcess, IManualRefreshCommand? refreshCommand = null, NativeSettingsCommands? settings = null, Func<RefreshTrigger, CancellationToken, ValueTask>? reevaluate = null, QuotaPresentationHost? presentation = null)
    {
        _instance = instance; _tray = tray; _popover = popover; _taskbar = taskbar; _awaitCancelledWork = awaitCancelledWork; _persistence = persistence; _exitProcess = exitProcess; _refreshCommand = refreshCommand; _settings = settings; _reevaluate = reevaluate; _presentation = presentation;
        if (_refreshCommand is not null) _refreshCommand.CanExecuteChanged += OnRefreshAvailabilityChanged;
        _tray.SetRefreshAvailable(_refreshCommand?.CanExecute == true);
        if (_presentation is not null) { _presentation.PropertyChanged += OnPresentationChanged; _tray.SetPresentation(_presentation.State); }
        _tray.SetSettingsAvailable(settings is not null);
        _tray.Toggled += Toggle; _tray.ExitRequested += OnExitRequested; _tray.RefreshRequested += OnRefreshRequested; _tray.StartupToggleRequested += OnStartupToggleRequested; _popover.Deactivated += OnDeactivated; _taskbar.Recreated += RecreateTray; _instance.ActivationRequested += RequestShow;
        _activationTimer.Tick += DispatchPendingActivation;
    }

    public void Start() { if (_disposed || _started) return; _started = true; RunSafely(_tray.Show); _activationTimer.Start(); ShowPendingRequest(); }
    public void RequestShow()
    {
        if (_disposed) return;
        _showPending = true;
        ShowPendingRequest();
    }
    public bool? StartupEnabled { get; private set; }
    public Task ExitAsync()
    {
        lock (_shutdown)
        {
        _cleanupTask ??= CleanupAsync();
        return _exitTask ??= ExitAttemptAsync(_cleanupTask);
        }
    }

    private async Task CleanupAsync()
    {
        _disposed = true; _activationTimer.Stop(); _activationTimer.Tick -= DispatchPendingActivation; _shutdown.Cancel();
        try { await _awaitCancelledWork(_shutdown.Token); } catch (Exception) { }
        try { await _persistence.DisposeAsync(); } catch (Exception) { }
        Detach();
        try { await _tray.DisposeAsync(); } catch (Exception) { }
        _shutdown.Dispose();
    }
    private async Task ExitAttemptAsync(Task cleanup)
    {
        await cleanup; await Task.Yield();
        try { _exitProcess(); }
        catch { lock (_shutdown) _exitTask = null; throw; }
    }
    private void DispatchPendingActivation(object? sender, EventArgs args)
    {
        if (!_disposed) RunSafely(_instance.DispatchPendingActivation);
    }

    private void ShowPendingRequest()
    {
        if (!_started || !_showPending) return;
        _showPending = false;
        ShowPopover();
    }
    private void Toggle()
    {
        if (_disposed) return;
        if (_popover.IsVisible) RunSafely(_popover.Hide); else ShowPopover();
    }
    private void ShowPopover()
    {
        if (_disposed) return;
        _presentation?.RecalculateFromClock();
        RunSafely(() =>
        {
            _popover.Show();
            if (_popover.IsVisible) _ = ReevaluateSafelyAsync(RefreshTrigger.PopoverOpened);
            _popover.Activate();
        });
    }
    private void OnDeactivated()
    {
        if (!_disposed && !_popover.IsOwnedDialogActive) RunSafely(_popover.Hide);
    }
    private void RecreateTray()
    {
        if (!_disposed) RunSafely(() => { _tray.Hide(); _tray.Show(); });
    }
    private void OnExitRequested() => _ = ExitSafelyAsync();
    private void OnRefreshRequested() => _ = RefreshSafelyAsync();
    private void OnRefreshAvailabilityChanged(object? sender, EventArgs args)
    {
        if (!_disposed) _tray.SetRefreshAvailable(_refreshCommand?.CanExecute == true);
    }
    private void OnPresentationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (!_disposed && (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(QuotaPresentationHost.State)))
        {
            _tray.SetPresentation(_presentation!.State);
        }
    }
    private void OnStartupToggleRequested() => _ = ToggleStartupSafelyAsync();
    private async Task RefreshSafelyAsync()
    {
        if (_refreshCommand?.CanExecute == true) try { await _refreshCommand.ExecuteAsync(_shutdown.Token); } catch (Exception) { }
    }
    private async Task ReevaluateSafelyAsync(RefreshTrigger trigger)
    {
        if (_reevaluate is not null) try { await _reevaluate(trigger, _shutdown.Token); } catch (Exception) { }
    }
    private async Task ToggleStartupSafelyAsync()
    {
        if (_settings is not null) try { StartupEnabled = await _settings.ToggleStartupAsync(_shutdown.Token); _tray.SetStartupEnabled(StartupEnabled.Value); } catch (Exception) { }
    }
    private async Task ExitSafelyAsync() { try { await ExitAsync(); } catch (Exception) { } }
    private void Detach()
    {
        _tray.Toggled -= Toggle; _tray.ExitRequested -= OnExitRequested; _tray.RefreshRequested -= OnRefreshRequested; _tray.StartupToggleRequested -= OnStartupToggleRequested; _popover.Deactivated -= OnDeactivated; _taskbar.Recreated -= RecreateTray; _instance.ActivationRequested -= RequestShow;
        if (_refreshCommand is not null) _refreshCommand.CanExecuteChanged -= OnRefreshAvailabilityChanged;
        if (_presentation is not null) _presentation.PropertyChanged -= OnPresentationChanged;
    }
    private static void RunSafely(Action action) { try { action(); } catch (Exception) { } }
    public ValueTask DisposeAsync() => new(ExitAsync());
}

public sealed partial class WindowsTrayRuntime : ITrayRuntime, IQuotaAlertTray
{
    private readonly Forms.NotifyIcon _icon = new() { Icon = LoadTrayIcon(), Text = "AIBar", Visible = false };
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _refresh = new("Refresh");
    private readonly Forms.ToolStripMenuItem _startup = new("Start with Windows") { CheckOnClick = false };
    public WindowsTrayRuntime()
    {
        var settings = new Forms.ToolStripMenuItem("Settings…");
        var exit = new Forms.ToolStripMenuItem("Exit AIBar");
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        _refresh.Click += (_, _) => RefreshRequested?.Invoke();
        _startup.Click += (_, _) => StartupToggleRequested?.Invoke();
        exit.Click += (_, _) => ExitRequested?.Invoke();
        _menu.Items.AddRange(new Forms.ToolStripItem[] { settings, _startup, exit });
        _icon.ContextMenuStrip = _menu;
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggled?.Invoke(); };
        _icon.BalloonTipClicked += (_, _) => AlertClicked?.Invoke();
    }
    // The .ico carries 16/20/24 px tray frames; ask for the DPI-scaled small size so Windows never downsamples the tile.
    private static Icon LoadTrayIcon()
    {
        using var stream = typeof(WindowsTrayRuntime).Assembly.GetManifestResourceStream("AIBar.Desktop.Assets.AIBar.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
    public event Action? Toggled;
    public event Action? ExitRequested;
    public event Action? RefreshRequested;
    public event Action? StartupToggleRequested;
    public event Action? SettingsRequested;
    public void SetRefreshAvailable(bool available)
    {
        if (available && !_menu.Items.Contains(_refresh)) _menu.Items.Insert(0, _refresh);
        else if (!available) _menu.Items.Remove(_refresh);
    }
    public void SetPresentation(BetaPresentationState state) => _icon.Text = TrayPresentationFormatter.Format(state);
    public void SetSettingsAvailable(bool available) => _startup.Enabled = available;
    public void SetStartupEnabled(bool enabled) => _startup.Checked = enabled;
    // The fallback panel action reuses the same action-only menu and shutdown event.
    public void ShowSettingsMenu(System.Windows.Window owner)
    {
        if (owner is MainWindow panel) panel.IsSettingsMenuOpen = true;
        void Closed(object? sender, Forms.ToolStripDropDownClosedEventArgs args)
        {
            _menu.Closed -= Closed;
            if (owner is MainWindow current) current.IsSettingsMenuOpen = false;
            if (!owner.IsActive) owner.Hide();
        }
        _menu.Closed += Closed;
        _menu.Show(Forms.Cursor.Position);
        _menu.Focus();
    }
    public void RequestExit() => ExitRequested?.Invoke();
    public void Show() => _icon.Visible = true;
    public void Hide() => _icon.Visible = false;
    public ValueTask DisposeAsync() { _icon.Dispose(); return ValueTask.CompletedTask; }
}

internal static class TrayPresentationFormatter
{
    internal static string Format(BetaPresentationState state)
    {
        var slots = $"5h {Percentage(state.Primary)} | 7d {Percentage(state.Weekly)}";
        var status = state.IsPrivateIntegrationDisabled ? "Disabled" : state.IsLoading ? "Loading" : state.IsDegraded || state.IsCached ? "Stale" : state.IsUnavailable ? "Unavailable" : state.FreshnessLabel;
        var age = state.CachedAgeLabel is { Length: > 0 } ? $" | {state.CachedAgeLabel}" : string.Empty;
        var text = $"AIBar: {slots} | {status}{age}";
        return text.Length <= 63 ? text : $"AIBar: {slots} | {status}";
    }

    private static string Percentage(QuotaWindowPresentation window) => window.PercentageUsed is { } value ? $"{value:0}%" : "--";
}

public sealed class WindowsPrivateIntegrationConsentPrompt : IPrivateIntegrationConsentPrompt
{
    private const string Disclosure = "Enable AIBar's private quota integration?\n\nAIBar will read your existing local Codex credential to access a private, undocumented, unsupported quota endpoint. Only your consent is saved. Credential values are never stored, displayed, or logged. You can disable this integration at any time.";
    public bool Confirm() => Forms.MessageBox.Show(Disclosure, "Enable Private Quota Integration", Forms.MessageBoxButtons.OKCancel, Forms.MessageBoxIcon.Warning, Forms.MessageBoxDefaultButton.Button2) == Forms.DialogResult.OK;
}

public static class WindowsPopoverPlacementContextProvider
{
    public static PopoverPlacementContext Get(Window window)
    {
        var width = Math.Max(window.MinWidth, Positive(window.ActualWidth, window.Width, 360));
        var height = Math.Max(window.MinHeight, Positive(window.ActualHeight, window.Height, window.MinHeight));
        try
        {
            var point = Forms.Cursor.Position;
            var screen = Forms.Screen.FromPoint(point) ?? Forms.Screen.PrimaryScreen;
            var bounds = screen is null ? new ScreenRect(0, 0, 1, 1) : Rect(screen.Bounds);
            var work = screen is null ? bounds : Rect(screen.WorkingArea);
            var primary = Forms.Screen.PrimaryScreen is { } value ? Rect(value.WorkingArea) : work;
            return new(work, bounds, new(point.X, point.Y, 1, 1), primary, width, height, Positive(System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX, 1), DetectTaskbarEdge(bounds, work));
        }
        catch
        {
            var fallback = new ScreenRect(0, 0, Positive(SystemParameters.PrimaryScreenWidth, 1), Positive(SystemParameters.PrimaryScreenHeight, 1));
            var anchor = new ScreenRect(double.IsFinite(window.Left) ? window.Left : 0, double.IsFinite(window.Top) ? window.Top : 0, 1, 1);
            return new(fallback, fallback, anchor, fallback, width, height, 1, TaskbarEdge.Bottom);
        }
    }

    private static ScreenRect Rect(Rectangle value) => new(value.Left, value.Top, value.Width, value.Height);
    private static double Positive(params double[] values) => values.FirstOrDefault(value => double.IsFinite(value) && value > 0, 1);
    internal static TaskbarEdge DetectTaskbarEdge(ScreenRect bounds, ScreenRect work)
    {
        if (!bounds.IsUsable || !work.IsUsable || work.Left < bounds.Left || work.Top < bounds.Top || work.Right > bounds.Right || work.Bottom > bounds.Bottom) return TaskbarEdge.Bottom;
        var gaps = new[] { bounds.Bottom - work.Bottom, work.Top - bounds.Top, work.Left - bounds.Left, bounds.Right - work.Right };
        var largest = gaps.Max();
        return largest <= 0 ? TaskbarEdge.Bottom : Array.IndexOf(gaps, largest) switch { 1 => TaskbarEdge.Top, 2 => TaskbarEdge.Left, 3 => TaskbarEdge.Right, _ => TaskbarEdge.Bottom };
    }
}

public sealed class WpfPopoverRuntime : IPopoverRuntime
{
    private readonly Window _window;
    private readonly IThemeController? _theme;
    private readonly Func<PopoverPlacementContext>? _placement;
    private readonly IDwmSurfaceHints _surfaceHints;
    private readonly Func<WindowsTheme> _currentTheme;
    public WpfPopoverRuntime(Window window, IThemeController? theme = null, Func<PopoverPlacementContext>? placement = null, IDwmSurfaceHints? surfaceHints = null, Func<WindowsTheme>? currentTheme = null)
    {
        _window = window; _theme = theme; _placement = placement; _surfaceHints = surfaceHints ?? new WindowsDwmSurfaceHints(); _currentTheme = currentTheme ?? (() => WindowsTheme.Light);
        _window.Deactivated += (_, _) => Deactivated?.Invoke();
    }
    public event Action? Deactivated;
    public bool IsVisible => _window.IsVisible;
    public bool IsOwnedDialogActive => _window is MainWindow { IsSettingsMenuOpen: true } || _window.OwnedWindows.OfType<Window>().Any(candidate => candidate.IsVisible);
    public void Show()
    {
        _theme?.Reevaluate();
        if (_placement is not null)
        {
            var bounds = PopoverPlacement.PlaceInContext(_placement());
            _window.Left = bounds.Left; _window.Top = bounds.Top; _window.Width = bounds.Width; _window.Height = bounds.Height;
        }
        PopupSurface.Apply(new WpfOpaquePopupSurface(_window), _currentTheme(), _surfaceHints);
        if (!_window.IsVisible) _window.Show();
    }
    public void Hide() => _window.Hide();
    public void Activate()
    {
        var activated = _window.Activate();
        System.Diagnostics.Trace.TraceInformation("AIBar popup activated: {0}; foreground PID: {1}",
            activated, ForegroundActivation.ForegroundProcessId());
    }
}

public sealed class TaskbarRecreationMonitor : ITaskbarRecreationEvents, IDisposable
{
    private readonly HwndSource _source;
    private readonly int _message = RegisterWindowMessage("TaskbarCreated");
    public TaskbarRecreationMonitor(Window window)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(window).EnsureHandle())!;
        _source.AddHook(WndProc);
    }
    public event Action? Recreated;
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == _message) Recreated?.Invoke();
        return IntPtr.Zero;
    }
    public void Dispose() => _source.RemoveHook(WndProc);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string value);
}

public sealed class EmptyAsyncResource : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
