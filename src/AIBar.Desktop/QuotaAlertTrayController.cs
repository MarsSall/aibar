using System.Globalization;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

public interface IQuotaAlertTray
{
    event Action<string>? AlertToggleRequested;
    event Action? AlertClicked;
    void SetAlertOptions(AlertOptions options);
    void ShowAlert(string title, string body);
}

public sealed partial class WindowsTrayRuntime
{
    private readonly Forms.ToolStripMenuItem _alerts = new("Alerts");
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _alertItems = [];
    public event Action<string>? AlertToggleRequested;
    public event Action? AlertClicked;
    public event Action? SettingsRequested;
    private void InitializeAlerts()
    {
        foreach (var (key, text) in new[] { ("resets", "Window resets"), ("thresholds", "Threshold alerts"),
                     ("runOut", "May run out before reset"), ("edgeGlow", "Edge glow at 100%") })
        {
            // The persisted decision drives the checkmark; failed saves never lie about the choice.
            var item = new Forms.ToolStripMenuItem(text) { Checked = true, CheckOnClick = false };
            item.Click += (_, _) => AlertToggleRequested?.Invoke(key);
            _alertItems.Add(key, item); _alerts.DropDownItems.Add(item);
        }
        var settings = new Forms.ToolStripMenuItem("Settings…");
        settings.Click += (_, _) => SettingsRequested?.Invoke();
        _menu.Items.Add(settings);
        var configure = new Forms.ToolStripMenuItem("Configure alerts…");
        configure.Click += (_, _) => SettingsRequested?.Invoke();
        _alerts.DropDownItems.Add(configure);
        _menu.Items.Add(_alerts);
        _icon.BalloonTipClicked += (_, _) => AlertClicked?.Invoke();
    }
    public void SetAlertOptions(AlertOptions options)
    {
        _alertItems["resets"].Checked = options.Resets;
        _alertItems["thresholds"].Checked = options.Thresholds;
        _alertItems["thresholds"].Text = string.Join(" / ", options.ThresholdValues) + "% thresholds";
        _alertItems["runOut"].Checked = options.RunOut;
        _alertItems["edgeGlow"].Checked = options.EdgeGlow;
    }
    public void ShowAlert(string title, string body) => _icon.ShowBalloonTip(5000, title, body, Forms.ToolTipIcon.Warning);
}

/// <summary>Dispatches only delivery; persistence and policy run on the observer's worker queue.</summary>
public sealed class QuotaAlertTrayController : IDisposable
{
    private readonly IQuotaAlertTray _tray;
    private readonly QuotaAlertObserver _observer;
    private readonly Dispatcher _dispatcher;
    private readonly Action _showPanel;
    private readonly Action<bool> _glow;
    private readonly Action<bool>? _confetti;
    private readonly INotificationDelivery? _cards;
    private bool _disposed;
    // The shared Appearance unit can bind its percentage setting without changing delivery policy.
    public double BackgroundOpacity
    {
        get => _cards is NotificationStack stack ? stack.BackgroundOpacity : 1;
        set { if (_cards is NotificationStack stack) stack.BackgroundOpacity = Math.Clamp(value, .3, 1); }
    }
    public QuotaAlertTrayController(IQuotaAlertTray tray, QuotaAlertObserver observer, Dispatcher dispatcher,
        Action showPanel, Action<bool>? glow = null, Action<bool>? confetti = null, INotificationDelivery? cards = null)
    {
        _tray = tray; _observer = observer; _dispatcher = dispatcher; _showPanel = showPanel;
        _cards = cards ?? (tray is WindowsTrayRuntime ? new NotificationStack(showPanel) : null);
        _glow = glow ?? (enabled => { EdgeGlowWindow.ShowOnAllMonitors(enabled); });
        _confetti = confetti;
        tray.AlertToggleRequested += Toggle; tray.AlertClicked += Clicked;
        observer.AlertReady += Deliver; observer.SettingsChanged += SettingsChanged;
        _ = LoadAsync();
    }
    private async Task LoadAsync()
    {
        try { SettingsChanged(await _observer.Settings.LoadAsync(default)); } catch (Exception) { }
    }
    private void Post(Action action)
    {
        if (_dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
    }
    private void SettingsChanged(AlertOptions options) => Post(() => _tray.SetAlertOptions(options));
    private void Toggle(string key) => _ = _observer.ChangeSettingsAsync(options => key switch
    {
        "resets" => options with { Resets = !options.Resets },
        "thresholds" => options with { Thresholds = !options.Thresholds },
        "runOut" => options with { RunOut = !options.RunOut },
        "edgeGlow" => options with { EdgeGlow = !options.EdgeGlow },
        _ => options
    });
    private void Clicked() { if (!_disposed) _showPanel(); }
    private void Deliver(QuotaAlert alert) => Post(async () =>
    {
        try
        {
            var options = await _observer.Settings.LoadAsync(default);
            if (_disposed || !_observer.CanDeliver(alert)) return;
            var sample = alert.Sample;
            var window = sample.Window == QuotaPaceWindow.FiveHour ? "5-hour" : "weekly";
            var label = $"{sample.Provider} {window}";
            var remaining = Duration(sample.ResetAt - DateTimeOffset.UtcNow);
            var balloon = alert.Kind switch
            {
                QuotaAlertKind.Reset => options.Resets,
                QuotaAlertKind.ThresholdCrossed => options.Thresholds && options.ThresholdValues.Contains(alert.Threshold ?? -1),
                QuotaAlertKind.RunsOutBeforeReset => options.RunOut,
                _ => false
            };
            if (balloon && options.Notification)
            {
                var (title, body) = alert.Kind switch
                {
                    QuotaAlertKind.Reset => ($"{label} limit reset", $"Now at {sample.PercentageUsed.ToString("0", CultureInfo.InvariantCulture)}% used"),
                    QuotaAlertKind.ThresholdCrossed => ($"{label} limit at {alert.Threshold}%", $"Resets in {remaining}"),
                    _ => ($"{label} may run out in {Duration((alert.RunsOutAt ?? DateTimeOffset.UtcNow) - DateTimeOffset.UtcNow)}",
                        $"At this pace it runs out before the reset in {remaining}")
                };
                // Keep the balloon strictly as a fallback when a custom card cannot be shown.
                if (_cards?.TryShow(alert.Kind, sample.Provider, title, body) != true)
                    _tray.ShowAlert(title, body);
            }
            if (alert.Kind == QuotaAlertKind.ThresholdCrossed && alert.Threshold == 100 && options.EdgeGlow) _glow(true);
            if (alert.Kind == QuotaAlertKind.Reset && options.Resets && options.Confetti)
            {
                if (_confetti is not null) _confetti(true);
                else ConfettiWindow.ShowOnAllMonitors(true, sample.Provider);
            }
        }
        catch (Exception) { /* Windows notification failures must not affect the quota authority. */ }
    });
    private static string Duration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) return "0m";
        return value.TotalDays >= 1 ? $"{(int)value.TotalDays}d {value.Hours}h" :
            value.TotalHours >= 1 ? $"{(int)value.TotalHours}h {value.Minutes}m" : $"{Math.Max(1, (int)value.TotalMinutes)}m";
    }
    public void Dispose()
    {
        _disposed = true;
        _cards?.Dispose();
        _tray.AlertToggleRequested -= Toggle; _tray.AlertClicked -= Clicked;
        _observer.AlertReady -= Deliver; _observer.SettingsChanged -= SettingsChanged;
    }
}
