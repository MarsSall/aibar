using System.Globalization;
using System.Windows;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

public static class SettingsThresholdEditor
{
    public static bool TryParse(string text, out int[] values, out string? error)
    {
        var parts = text.Split(',');
        var parsed = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                values = []; error = "Enter whole numbers from 1 to 100, separated by commas."; return false;
            }
            parsed.Add(value);
        }
        values = parsed.ToArray();
        if (!QuotaAlertPolicy.ValidThresholds(values))
        {
            error = "Use integers from 1 to 100 in ascending order, without duplicates."; return false;
        }
        error = null; return true;
    }
}

public partial class SettingsWindow : Window
{
    private readonly AppearanceSettings _appearance;
    private readonly AlertSettings _alerts;
    private readonly Func<AlertOptions, Task> _saveAlerts;
    private readonly WindowsThemeController _theme;
    private bool _saving;
    public SettingsWindow(AppearanceSettings appearance, AlertSettings alerts, Func<AlertOptions, Task>? saveAlerts = null, IWindowsThemeSource? themeSource = null)
    {
        _appearance = appearance; _alerts = alerts;
        _saveAlerts = saveAlerts ?? (options => alerts.SaveAsync(options, default).AsTask());
        InitializeComponent();
        _theme = new WindowsThemeController(Resources, themeSource ?? new WindowsThemeSource(), Dispatcher);
        Closed += (_, _) => _theme.Dispose();
        ValidateThresholds();
    }
    public async Task LoadAsync()
    {
        var appearance = await _appearance.LoadAsync(default);
        var alerts = await _alerts.LoadAsync(default);
        BarColors.SelectedIndex = appearance.BarColors == "usage" ? 1 : 0;
        YasbColors.SelectedIndex = appearance.YasbColors == "brand" ? 1 : 0;
        Resets.IsChecked = alerts.Resets; Thresholds.IsChecked = alerts.Thresholds; RunOut.IsChecked = alerts.RunOut;
        Notification.IsChecked = alerts.Notification; EdgeGlow.IsChecked = alerts.EdgeGlow; Confetti.IsChecked = alerts.Confetti;
        ThresholdInput.Text = string.Join(", ", alerts.ThresholdValues);
    }
    private void OnThresholdChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ValidateThresholds();
    private void ValidateThresholds()
    {
        // TextChanged can run while InitializeComponent is still connecting later named elements.
        if (ThresholdInput is null || ThresholdError is null || SaveButton is null) return;
        var valid = SettingsThresholdEditor.TryParse(ThresholdInput.Text, out _, out var error);
        ThresholdError.Text = error ?? "";
        System.Windows.Automation.AutomationProperties.SetHelpText(ThresholdInput, error ?? "Ascending integers from 1 to 100, separated by commas.");
        SaveButton.IsEnabled = valid && !_saving;
    }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_saving || !SettingsThresholdEditor.TryParse(ThresholdInput.Text, out var values, out _)) return;
        _saving = true; ValidateThresholds(); SaveStatus.Text = "Saving…";
        try
        {
            await _saveAlerts(new(Resets.IsChecked == true, Thresholds.IsChecked == true, RunOut.IsChecked == true,
                EdgeGlow.IsChecked == true, Notification.IsChecked == true, Confetti.IsChecked == true) { ThresholdValues = values });
            await _appearance.SaveAsync(new(BarColors.SelectedIndex == 1 ? "usage" : "brand", YasbColors.SelectedIndex == 1 ? "brand" : "usage"), default);
            SaveStatus.Text = "Changes saved.";
        }
        catch (Exception) { SaveStatus.Text = "Could not save all changes. Please try again."; }
        finally { _saving = false; ValidateThresholds(); }
    }
    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Single live settings window. Call Show(owner) from either the panel or the tray on the UI thread.</summary>
public sealed class SettingsWindowHost(AppearanceSettings appearance, QuotaAlertObserver alerts) : IDisposable
{
    private SettingsWindow? _window;
    private bool _disposed;
    public async void Show(Window? owner = null)
    {
        if (_disposed) return;
        if (_window is { } existing) { if (existing.IsVisible) existing.Activate(); return; }
        var window = new SettingsWindow(appearance, alerts.Settings, alerts.SaveSettingsAsync);
        _window = window;
        if (owner is { IsVisible: true }) window.Owner = owner;
        window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
        try
        {
            window.IsEnabled = false;
            window.Show();
            await window.LoadAsync();
            if (_disposed || !ReferenceEquals(_window, window)) { window.Close(); return; }
            window.IsEnabled = true; window.Activate();
        }
        catch (Exception) { window.Close(); }
    }
    public void Dispose() { _disposed = true; _window?.Close(); _window = null; }
}
