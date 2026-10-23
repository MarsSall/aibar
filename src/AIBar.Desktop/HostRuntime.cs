using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

public interface ITrayRuntime : IAsyncDisposable
{
    event Action? Toggled;
    event Action? ExitRequested;
    event Action? RefreshRequested;
    event Action? StartupToggleRequested;
    event Action? ClearAiBarDataRequested;
    event Action? PrivateIntegrationEnableRequested;
    event Action? PrivateIntegrationDisableRequested;
    event Action? OpenCodeLocalUsageToggleRequested;
    event Action? PiLocalUsageToggleRequested;
    void SetRefreshAvailable(bool available);
    void SetPresentation(BetaPresentationState state);
    void SetSettingsAvailable(bool available);
    void SetPrivateIntegrationEnabled(bool enabled);
    void SetStartupEnabled(bool enabled);
    void SetLocalUsageAvailable(bool available);
    void SetLocalUsagePolicy(LocalUsagePolicy policy);
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

public interface IOpenCodeDataRootTray
{
    event Action? OpenCodeDataFolderChooseRequested;
    event Action? OpenCodeDataFolderResetRequested;
}

public interface IOpenCodeDataFolderPicker { string? Choose(); void ShowFailure(); }

internal static class OpenCodeDataFolderCopy
{
    internal const string Choose = "Choose OpenCode data folder...";
    internal const string Reset = "Use default OpenCode data folder";
    internal const string Guidance = "Select the folder that contains opencode.db.";
    internal const string Failure = "The selected OpenCode folder cannot be used.";
}

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
    private readonly Action? _reportSettingsFailure;
    private readonly Func<RefreshTrigger, CancellationToken, ValueTask>? _reevaluate;
    private readonly QuotaPresentationHost? _presentation;
    private readonly IPrivateIntegrationConsentPrompt? _consentPrompt;
    private readonly IOpenCodeDataFolderPicker? _openCodeFolderPicker;
    private readonly ClaudeQuotaTrayController? _claudeController;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DispatcherTimer _activationTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private Task? _cleanupTask;
    private Task? _exitTask;
    private int _grantingConsent;
    private bool _started;
    private bool _showPending;
    private bool _disposed;

    public TrayHostRuntime(SingleInstanceHost instance, ITrayRuntime tray, IPopoverRuntime popover, ITaskbarRecreationEvents taskbar, Func<CancellationToken, Task> awaitCancelledWork, IAsyncDisposable persistence, Action exitProcess, IManualRefreshCommand? refreshCommand = null, NativeSettingsCommands? settings = null, Action? reportSettingsFailure = null, Func<RefreshTrigger, CancellationToken, ValueTask>? reevaluate = null, QuotaPresentationHost? presentation = null, IPrivateIntegrationConsentPrompt? consentPrompt = null, IOpenCodeDataFolderPicker? openCodeFolderPicker = null, ClaudeQuotaRuntime? claudeQuota = null, IPrivateIntegrationConsentPrompt? claudeConsentPrompt = null)
    {
        _instance = instance; _tray = tray; _popover = popover; _taskbar = taskbar; _awaitCancelledWork = awaitCancelledWork; _persistence = persistence; _exitProcess = exitProcess; _refreshCommand = refreshCommand; _settings = settings; _reportSettingsFailure = reportSettingsFailure; _reevaluate = reevaluate; _presentation = presentation; _consentPrompt = consentPrompt; _openCodeFolderPicker = openCodeFolderPicker;
        if (_refreshCommand is not null) _refreshCommand.CanExecuteChanged += OnRefreshAvailabilityChanged;
        _tray.SetRefreshAvailable(_refreshCommand?.CanExecute == true);
        if (_presentation is not null) { _presentation.PropertyChanged += OnPresentationChanged; _tray.SetPresentation(_presentation.State); }
        _tray.SetSettingsAvailable(settings is not null);
        _tray.SetPrivateIntegrationEnabled(settings?.PrivateIntegrationEnabled == true);
        _tray.SetLocalUsageAvailable(settings?.LocalUsageAvailable == true); _tray.SetLocalUsagePolicy(settings?.LocalUsagePolicy ?? LocalUsagePolicy.Disabled);
        _tray.Toggled += Toggle; _tray.ExitRequested += OnExitRequested; _tray.RefreshRequested += OnRefreshRequested; _tray.StartupToggleRequested += OnStartupToggleRequested; _tray.ClearAiBarDataRequested += OnClearAiBarDataRequested; _tray.PrivateIntegrationEnableRequested += OnPrivateIntegrationEnableRequested; _tray.PrivateIntegrationDisableRequested += OnPrivateIntegrationDisableRequested; _tray.OpenCodeLocalUsageToggleRequested += OnOpenCodeLocalUsageToggleRequested; _tray.PiLocalUsageToggleRequested += OnPiLocalUsageToggleRequested; _popover.Deactivated += OnDeactivated; _taskbar.Recreated += RecreateTray; _instance.ActivationRequested += RequestShow;
        if (_tray is IOpenCodeDataRootTray rootTray) { rootTray.OpenCodeDataFolderChooseRequested += OnOpenCodeDataFolderChooseRequested; rootTray.OpenCodeDataFolderResetRequested += OnOpenCodeDataFolderResetRequested; }
        if (tray is IClaudeQuotaTray claudeTray && claudeQuota is not null)
            _claudeController = new(claudeTray, claudeQuota, claudeConsentPrompt ?? new WindowsClaudeQuotaConsentPrompt());
        _activationTimer.Tick += DispatchPendingActivation;
    }

    public void Start() { if (_disposed || _started) return; _started = true; RunSafely(_tray.Show); _activationTimer.Start(); ShowPendingRequest(); _ = LoadLocalUsagePolicySafelyAsync(); }
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
            _tray.SetPrivateIntegrationEnabled(_settings?.PrivateIntegrationEnabled == true);
        }
    }
    private void OnStartupToggleRequested() => _ = ToggleStartupSafelyAsync();
    private void OnClearAiBarDataRequested() => _ = ClearAiBarDataSafelyAsync();
    private void OnPrivateIntegrationEnableRequested() => _ = EnablePrivateIntegrationSafelyAsync();
    private void OnPrivateIntegrationDisableRequested() => _ = SetPrivateIntegrationSafelyAsync(settings => settings.DisablePrivateIntegrationAsync(_shutdown.Token));
    private void OnOpenCodeLocalUsageToggleRequested() => _ = ChangeLocalUsageSafelyAsync(settings => settings.ToggleOpenCodeLocalUsageAsync(_shutdown.Token));
    private void OnPiLocalUsageToggleRequested() => _ = ChangeLocalUsageSafelyAsync(settings => settings.TogglePiLocalUsageAsync(_shutdown.Token));
    private void OnOpenCodeDataFolderChooseRequested() => _ = ChooseOpenCodeDataFolderSafelyAsync();
    private void OnOpenCodeDataFolderResetRequested() => _ = ChangeOpenCodeDataFolderSafelyAsync(settings => settings.ResetOpenCodeDataRootAsync(_shutdown.Token));
    private async Task ChooseOpenCodeDataFolderSafelyAsync()
    {
        if (_settings?.LocalUsageAvailable != true || _openCodeFolderPicker is null) return;
        string? selected;
        try { selected = _openCodeFolderPicker.Choose(); }
        catch { ShowOpenCodeFolderFailure(); return; }
        if (selected is null) { ShowOpenCodeFolderFailure(); return; }
        await ChangeOpenCodeDataFolderSafelyAsync(settings => settings.ChooseOpenCodeDataRootAsync(selected, _shutdown.Token));
    }
    private async Task ChangeOpenCodeDataFolderSafelyAsync(Func<NativeSettingsCommands, ValueTask<LocalUsagePolicy>> operation)
    {
        if (_settings?.LocalUsageAvailable != true) return;
        try { await operation(_settings); SetLocalUsagePolicy(); }
        catch { ShowOpenCodeFolderFailure(); }
    }
    private void ShowOpenCodeFolderFailure()
    {
        if (!_shutdown.IsCancellationRequested) try { _openCodeFolderPicker?.ShowFailure(); } catch { }
    }
    private async Task EnablePrivateIntegrationSafelyAsync()
    {
        if (_settings is null || _consentPrompt is null || Interlocked.CompareExchange(ref _grantingConsent, 1, 0) != 0) return;
        try
        {
            if (!_consentPrompt.Confirm()) return;
            await SetPrivateIntegrationSafelyAsync(settings => settings.EnablePrivateIntegrationAsync(_shutdown.Token));
        }
        catch (Exception) { }
        finally { Volatile.Write(ref _grantingConsent, 0); }
    }
    private async Task SetPrivateIntegrationSafelyAsync(Func<NativeSettingsCommands, ValueTask> operation)
    {
        await SettingsSafelyAsync(operation);
        _presentation?.RefreshAvailabilityChanged();
        _tray.SetPrivateIntegrationEnabled(_settings?.PrivateIntegrationEnabled == true);
    }
    private async Task RefreshSafelyAsync()
    {
        if (_refreshCommand?.CanExecute == true) try { await _refreshCommand.ExecuteAsync(_shutdown.Token); } catch (Exception) { }
    }
    private async Task ReevaluateSafelyAsync(RefreshTrigger trigger)
    {
        if (_reevaluate is not null) try { await _reevaluate(trigger, _shutdown.Token); } catch (Exception) { }
    }
    private async Task SettingsSafelyAsync(Func<NativeSettingsCommands, ValueTask> operation)
    {
        if (_settings is not null) try { await operation(_settings); } catch (Exception) { }
    }
    private async Task ClearAiBarDataSafelyAsync()
    {
        if (_settings is not null) try { await _settings.ClearAiBarDataAsync(_shutdown.Token); SetLocalUsagePolicy(); } catch (Exception) { _reportSettingsFailure?.Invoke(); }
    }
    private async Task LoadLocalUsagePolicySafelyAsync()
    {
        if (_settings?.LocalUsageAvailable != true) return;
        try { await _settings.LoadLocalUsagePolicyAsync(_shutdown.Token); SetLocalUsagePolicy(); } catch (Exception) { }
    }
    private async Task ChangeLocalUsageSafelyAsync(Func<NativeSettingsCommands, ValueTask<LocalUsagePolicy>> operation)
    {
        if (_settings?.LocalUsageAvailable != true) return;
        try { await operation(_settings); SetLocalUsagePolicy(); } catch (Exception) { }
    }
    private void SetLocalUsagePolicy() { if (!_disposed) _tray.SetLocalUsagePolicy(_settings?.LocalUsagePolicy ?? LocalUsagePolicy.Disabled); }
    private async Task ToggleStartupSafelyAsync()
    {
        if (_settings is not null) try { StartupEnabled = await _settings.ToggleStartupAsync(_shutdown.Token); _tray.SetStartupEnabled(StartupEnabled.Value); } catch (Exception) { }
    }
    private async Task ExitSafelyAsync() { try { await ExitAsync(); } catch (Exception) { } }
    private void Detach()
    {
        _claudeController?.Dispose();
        _tray.Toggled -= Toggle; _tray.ExitRequested -= OnExitRequested; _tray.RefreshRequested -= OnRefreshRequested; _tray.StartupToggleRequested -= OnStartupToggleRequested; _tray.ClearAiBarDataRequested -= OnClearAiBarDataRequested; _tray.PrivateIntegrationEnableRequested -= OnPrivateIntegrationEnableRequested; _tray.PrivateIntegrationDisableRequested -= OnPrivateIntegrationDisableRequested; _tray.OpenCodeLocalUsageToggleRequested -= OnOpenCodeLocalUsageToggleRequested; _tray.PiLocalUsageToggleRequested -= OnPiLocalUsageToggleRequested; _popover.Deactivated -= OnDeactivated; _taskbar.Recreated -= RecreateTray; _instance.ActivationRequested -= RequestShow;
        if (_tray is IOpenCodeDataRootTray rootTray) { rootTray.OpenCodeDataFolderChooseRequested -= OnOpenCodeDataFolderChooseRequested; rootTray.OpenCodeDataFolderResetRequested -= OnOpenCodeDataFolderResetRequested; }
        if (_refreshCommand is not null) _refreshCommand.CanExecuteChanged -= OnRefreshAvailabilityChanged;
        if (_presentation is not null) _presentation.PropertyChanged -= OnPresentationChanged;
    }
    private static void RunSafely(Action action) { try { action(); } catch (Exception) { } }
    public ValueTask DisposeAsync() => new(ExitAsync());
}

public sealed partial class WindowsTrayRuntime : ITrayRuntime, IOpenCodeDataRootTray, IClaudeQuotaTray, IClaudeQuotaSourceTray, IQuotaAlertTray
{
    private readonly Forms.NotifyIcon _icon = new() { Icon = LoadTrayIcon(), Text = "AIBar", Visible = false };
    private readonly Forms.ContextMenuStrip _menu = new();
    private readonly Forms.ToolStripMenuItem _refresh = new("Refresh");
    private readonly Forms.ToolStripMenuItem _startup = new("Start with Windows");
    private readonly Forms.ToolStripMenuItem _clear = new("Clear AIBar Data");
    private readonly Forms.ToolStripMenuItem _enablePrivate = new("Enable Private Quota Integration");
    private readonly Forms.ToolStripMenuItem _disablePrivate = new("Disable Private Quota Integration");
    private readonly Forms.ToolStripMenuItem _openCodeUsage = new("Read OpenCode usage data locally");
    private readonly Forms.ToolStripMenuItem _piUsage = new("Read Pi usage data locally");
    private readonly Forms.ToolStripMenuItem _chooseOpenCodeFolder = new(OpenCodeDataFolderCopy.Choose) { AccessibleName = OpenCodeDataFolderCopy.Choose };
    private readonly Forms.ToolStripMenuItem _resetOpenCodeFolder = new(OpenCodeDataFolderCopy.Reset) { AccessibleName = OpenCodeDataFolderCopy.Reset };
    private readonly Forms.ToolStripMenuItem _enableClaude = new("Enable Claude quota");
    private readonly Forms.ToolStripMenuItem _disableClaude = new("Disable Claude quota");
    private readonly Forms.ToolStripMenuItem _claudeSource = new("Claude source");
    private readonly Forms.ToolStripMenuItem _claudeAutomatic = new("Automatic") { Checked = true };
    private readonly Forms.ToolStripMenuItem _claudeThisPc = new("This PC");
    private readonly Forms.ToolStripMenuItem _claudeOtherMachine = new("Other machine");
    private bool _claudeAvailable;
    private bool _claudeEnabled;
    private bool _settingsAvailable;
    private bool _privateIntegrationEnabled;
    public WindowsTrayRuntime()
    {
        InitializeAlerts();
        var exit = new Forms.ToolStripMenuItem("Exit AIBar");
        _claudeSource.DropDownItems.AddRange(new Forms.ToolStripItem[] { _claudeAutomatic, _claudeThisPc, _claudeOtherMachine });
        _claudeAutomatic.Click += (_, _) => ClaudeSourceRequested?.Invoke(ClaudeQuotaSource.Automatic);
        _claudeThisPc.Click += (_, _) => ClaudeSourceRequested?.Invoke(ClaudeQuotaSource.ThisPc);
        _claudeOtherMachine.Click += (_, _) => ClaudeSourceRequested?.Invoke(ClaudeQuotaSource.OtherMachine);
        _enableClaude.Click += (_, _) => ClaudeQuotaEnableRequested?.Invoke();
        _disableClaude.Click += (_, _) => ClaudeQuotaDisableRequested?.Invoke();
        _refresh.Click += (_, _) => RefreshRequested?.Invoke(); _startup.Click += (_, _) => StartupToggleRequested?.Invoke(); _clear.Click += (_, _) => ClearAiBarDataRequested?.Invoke(); _enablePrivate.Click += (_, _) => PrivateIntegrationEnableRequested?.Invoke(); _disablePrivate.Click += (_, _) => PrivateIntegrationDisableRequested?.Invoke(); _openCodeUsage.Click += (_, _) => OpenCodeLocalUsageToggleRequested?.Invoke(); _piUsage.Click += (_, _) => PiLocalUsageToggleRequested?.Invoke(); _chooseOpenCodeFolder.Click += (_, _) => OpenCodeDataFolderChooseRequested?.Invoke(); _resetOpenCodeFolder.Click += (_, _) => OpenCodeDataFolderResetRequested?.Invoke(); exit.Click += (_, _) => ExitRequested?.Invoke(); _menu.Items.Add(exit); _icon.ContextMenuStrip = _menu;
        _icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggled?.Invoke(); };
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
    public event Action? ClearAiBarDataRequested;
    public event Action? PrivateIntegrationEnableRequested;
    public event Action? PrivateIntegrationDisableRequested;
    public event Action? OpenCodeLocalUsageToggleRequested;
    public event Action? PiLocalUsageToggleRequested;
    public event Action? OpenCodeDataFolderChooseRequested;
    public event Action? OpenCodeDataFolderResetRequested;
    public event Action<ClaudeQuotaSource>? ClaudeSourceRequested;
    public void SetClaudeSource(ClaudeQuotaSource source)
    {
        if (_menu.InvokeRequired) { _menu.BeginInvoke(new Action(() => SetClaudeSource(source))); return; }
        _claudeAutomatic.Checked = source == ClaudeQuotaSource.Automatic;
        _claudeThisPc.Checked = source == ClaudeQuotaSource.ThisPc;
        _claudeOtherMachine.Checked = source == ClaudeQuotaSource.OtherMachine;
        UpdateClaudeMenu();
    }
    public event Action? ClaudeQuotaEnableRequested;
    public event Action? ClaudeQuotaDisableRequested;
    public void SetClaudeQuotaEnabled(bool enabled)
    {
        if (_menu.InvokeRequired) { _menu.BeginInvoke(new Action(() => SetClaudeQuotaEnabled(enabled))); return; }
        _claudeAvailable = true; _claudeEnabled = enabled; UpdateClaudeMenu();
    }
    private void UpdateClaudeMenu()
    {
        _menu.Items.Remove(_enableClaude); _menu.Items.Remove(_disableClaude); _menu.Items.Remove(_claudeSource);
        if (_claudeAvailable && _settingsAvailable)
        {
            _menu.Items.Insert(Math.Max(0, _menu.Items.Count - 1), _claudeEnabled ? _disableClaude : _enableClaude);
            _menu.Items.Insert(Math.Max(0, _menu.Items.Count - 1), _claudeSource);
        }
    }
    public void SetRefreshAvailable(bool available)
    {
        if (available && !_menu.Items.Contains(_refresh)) _menu.Items.Insert(0, _refresh);
        else if (!available) _menu.Items.Remove(_refresh);
    }
    public void SetPresentation(BetaPresentationState state) => _icon.Text = TrayPresentationFormatter.Format(state);
    public void SetSettingsAvailable(bool available)
    {
        _settingsAvailable = available;
        if (available && !_menu.Items.Contains(_startup)) { _menu.Items.Insert(0, _clear); _menu.Items.Insert(0, _startup); }
        else if (!available) { _menu.Items.Remove(_startup); _menu.Items.Remove(_clear); }
        UpdatePrivateIntegrationMenu(); UpdateClaudeMenu();
    }
    public void SetPrivateIntegrationEnabled(bool enabled) { _privateIntegrationEnabled = enabled; UpdatePrivateIntegrationMenu(); }
    private void UpdatePrivateIntegrationMenu()
    {
        _menu.Items.Remove(_enablePrivate); _menu.Items.Remove(_disablePrivate);
        if (_settingsAvailable) _menu.Items.Insert(Math.Min(2, _menu.Items.Count), _privateIntegrationEnabled ? _disablePrivate : _enablePrivate);
    }
    public void SetStartupEnabled(bool enabled) => _startup.Checked = enabled;
    public void SetLocalUsageAvailable(bool available)
    {
        if (available) { if (!_menu.Items.Contains(_resetOpenCodeFolder)) _menu.Items.Insert(0, _resetOpenCodeFolder); if (!_menu.Items.Contains(_chooseOpenCodeFolder)) _menu.Items.Insert(0, _chooseOpenCodeFolder); if (!_menu.Items.Contains(_piUsage)) _menu.Items.Insert(0, _piUsage); if (!_menu.Items.Contains(_openCodeUsage)) _menu.Items.Insert(0, _openCodeUsage); }
        else { _menu.Items.Remove(_openCodeUsage); _menu.Items.Remove(_piUsage); _menu.Items.Remove(_chooseOpenCodeFolder); _menu.Items.Remove(_resetOpenCodeFolder); }
    }
    public void SetLocalUsagePolicy(LocalUsagePolicy policy) { _openCodeUsage.Checked = policy.OpenCodeEnabled; _piUsage.Checked = policy.PiEnabled; _resetOpenCodeFolder.Enabled = policy.OpenCodeDataRoot is not null; }
    // Panel actions reuse the exact live tray menu and shutdown event, including consent and alerts.
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

public sealed class WindowsOpenCodeDataFolderPicker : IOpenCodeDataFolderPicker
{
    public string? Choose()
    {
        var dialog = new OpenFolderDialog { Multiselect = false, Title = OpenCodeDataFolderCopy.Guidance };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    public void ShowFailure() => Forms.MessageBox.Show(OpenCodeDataFolderCopy.Failure, "OpenCode data folder", Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
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
