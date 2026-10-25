using System.Globalization;
using System.Windows;
using AIBar.Application;
using AIBar.Domain;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBlock = System.Windows.Controls.TextBlock;
using RadioButton = System.Windows.Controls.RadioButton;

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
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] < values[i - 1])
            {
                error = $"Keep the values ascending: {values[i]} comes after {values[i - 1]}. Use whole numbers from 1 to 100.";
                return false;
            }
        }
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
    private readonly SettingsProviderCommands? _providers;
    private readonly Action? _previewNotification;
    private readonly IWindowsThemeSource _themeSource;
    private readonly DockSettings? _dock;
    private readonly SettingsGlyphFont? _font;
    private bool _saving;
    public SettingsWindow(AppearanceSettings appearance, AlertSettings alerts, Func<AlertOptions, Task>? saveAlerts = null, IWindowsThemeSource? themeSource = null,
        SettingsProviderCommands? providers = null, Action? previewNotification = null, DockSettings? dock = null, SettingsGlyphFont? font = null)
    {
        _dock = dock; _font = font;
        _appearance = appearance; _alerts = alerts; _providers = providers; _previewNotification = previewNotification;
        _themeSource = themeSource ?? new WindowsThemeSource();
        _saveAlerts = saveAlerts ?? (options => alerts.SaveAsync(options, default).AsTask());
        InitializeComponent();
        _theme = new WindowsThemeController(Resources, _themeSource, Dispatcher);
        _themeSource.Changed += OnThemeChanged;
        Closed += (_, _) => { _themeSource.Changed -= OnThemeChanged; _theme.Dispose(); };
        // The layered host stays transparent: applying the opaque popup policy here
        // would remove AllowsTransparency and clip the exterior Settings glow.
        UpdateWindowDecoration();
        CodexEnabled.IsEnabled = ClaudeEnabled.IsEnabled = SourceChoices.IsEnabled = providers is not null;
        StartWithWindows.IsEnabled = ClearData.IsEnabled = providers?.General is not null;
        FloatingWidget.IsEnabled = dock is not null;
        UpdateFontStatus();
        UpdateThemeDescription();
        ValidateThresholds();
    }
    public async Task LoadAsync()
    {
        if (_providers?.General is { } general) StartWithWindows.IsChecked = await general.LoadStartupAsync();
        var appearance = await _appearance.LoadAsync(default);
        var alerts = await _alerts.LoadAsync(default);
        BarUsage.IsChecked = appearance.BarColors == "usage"; BarBrand.IsChecked = !BarUsage.IsChecked;
        YasbBrand.IsChecked = appearance.YasbColors == "brand"; YasbUsage.IsChecked = !YasbBrand.IsChecked;
        BackgroundOpacity.Value = appearance.BackgroundOpacity;
        ProviderTint.IsChecked = appearance.ProviderTint;
        if (_dock is { } dock) FloatingWidget.IsChecked = (await dock.LoadAsync(default)).Enabled;
        UpdateFontStatus();
        foreach (var choice in RefreshChoices.Children.OfType<RadioButton>())
            choice.IsChecked = RefreshMinutes(choice) == appearance.RefreshMinutes;
        if (_providers is { } providers)
        {
            CodexEnabled.IsChecked = providers.CodexEnabled; ClaudeEnabled.IsChecked = providers.ClaudeEnabled;
            SourceThisPc.IsChecked = providers.Source == ClaudeQuotaSource.ThisPc;
            SourceOtherMachine.IsChecked = providers.Source == ClaudeQuotaSource.OtherMachine;
            SourceAutomatic.IsChecked = providers.Source == ClaudeQuotaSource.Automatic;
            SourceStatus.Text = "ⓘ " + providers.SourceDescription;
        }
        else SourceStatus.Text = "Provider settings are unavailable.";
        Resets.IsChecked = alerts.Resets; Thresholds.IsChecked = alerts.Thresholds; RunOut.IsChecked = alerts.RunOut;
        Notification.IsChecked = alerts.Notification; EdgeGlow.IsChecked = alerts.EdgeGlow; Confetti.IsChecked = alerts.Confetti;
        ThresholdInput.Text = string.Join(", ", alerts.ThresholdValues);
    }
    private async void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        if (_saving || _providers?.General is not { } general) return;
        _saving = true; StartWithWindows.IsEnabled = ClearData.IsEnabled = false; ValidateThresholds();
        try { StartWithWindows.IsChecked = await general.ToggleStartupAsync(); GeneralStatus.Text = "Startup setting updated."; }
        catch (Exception)
        {
            try { StartWithWindows.IsChecked = await general.LoadStartupAsync(); } catch (Exception) { StartWithWindows.IsChecked = false; }
            GeneralStatus.Text = "Could not update startup. Please try again.";
        }
        finally { _saving = false; StartWithWindows.IsEnabled = ClearData.IsEnabled = true; ValidateThresholds(false); }
    }
    private async void OnClearData(object sender, RoutedEventArgs e)
    {
        if (_saving || _providers?.General is not { } general) return;
        _saving = true; StartWithWindows.IsEnabled = ClearData.IsEnabled = false; ValidateThresholds();
        try
        {
            if (!await general.ClearAsync()) { GeneralStatus.Text = "Clear cancelled."; return; }
            await LoadAsync();
            GeneralStatus.Text = "AIBar data cleared. Codex quota access is off.";
        }
        catch (Exception) { GeneralStatus.Text = "Could not clear all AIBar data. Please try again."; }
        finally { _saving = false; StartWithWindows.IsEnabled = ClearData.IsEnabled = true; ValidateThresholds(false); }
    }
    private void OnThresholdChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ValidateThresholds();
    private void ValidateThresholds(bool updateStatus = true)
    {
        // TextChanged can run while InitializeComponent is still connecting later named elements.
        if (ThresholdInput is null || ThresholdError is null || SaveButton is null) return;
        var valid = SettingsThresholdEditor.TryParse(ThresholdInput.Text, out _, out var error);
        ThresholdError.Text = valid ? "1–100, ascending" : "ⓘ " + error;
        ThresholdError.SetResourceReference(TextBlock.ForegroundProperty, valid ? "SettingsSecondaryBrush" : "SettingsDangerBrush");
        ThresholdInput.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, valid ? "SettingsBorderBrush" : "SettingsDangerBrush");
        ThresholdInput.BorderThickness = valid ? new Thickness(1) : new Thickness(1, 1, 1, 2);
        AlertsErrorDot.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        ThresholdTicks.Visibility = valid ? Visibility.Visible : Visibility.Collapsed;
        if (updateStatus && !_saving)
        {
            SaveStatus.Text = valid ? "Changes apply when you save" : "ⓘ Fix the thresholds to save";
            SaveStatus.SetResourceReference(TextBlock.ForegroundProperty, valid ? "SettingsSecondaryBrush" : "SettingsDangerBrush");
        }
        System.Windows.Automation.AutomationProperties.SetHelpText(ThresholdInput, error ?? "Ascending integers from 1 to 100, separated by commas.");
        SaveButton.IsEnabled = valid && !_saving;
        if (StartWithWindows is not null && ClearData is not null)
            StartWithWindows.IsEnabled = ClearData.IsEnabled = !_saving && _providers?.General is not null;
        DrawThresholdTicks();
    }
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_saving || !SettingsThresholdEditor.TryParse(ThresholdInput.Text, out var values, out _)) return;
        _saving = true; ValidateThresholds(); SaveStatus.Text = "Saving…";
        try
        {
            if (_providers is { } providers && !await providers.SaveAsync(CodexEnabled.IsChecked == true, ClaudeEnabled.IsChecked == true,
                SourceThisPc.IsChecked == true ? ClaudeQuotaSource.ThisPc : SourceOtherMachine.IsChecked == true ? ClaudeQuotaSource.OtherMachine : ClaudeQuotaSource.Automatic))
            {
                SaveStatus.Text = "Consent declined. Changes were not saved."; return;
            }
            await _saveAlerts(new(Resets.IsChecked == true, Thresholds.IsChecked == true, RunOut.IsChecked == true,
                EdgeGlow.IsChecked == true, Notification.IsChecked == true, Confetti.IsChecked == true) { ThresholdValues = values });
            var currentAppearance = await _appearance.LoadAsync(default);
            await _appearance.SaveAsync(new(BarUsage.IsChecked == true ? "usage" : "brand", YasbBrand.IsChecked == true ? "brand" : "usage", (int)BackgroundOpacity.Value,
                RefreshChoices.Children.OfType<RadioButton>().Where(choice => choice.IsChecked == true).Select(RefreshMinutes).Single(), ProviderTint.IsChecked == true, currentAppearance.ChartStyle), default);
            if (_dock is { } dock)
            {
                var current = await dock.LoadAsync(default);
                await dock.SaveAsync(current with { Enabled = FloatingWidget.IsChecked == true }, default);
            }
            SaveStatus.Text = "Changes saved.";
            if (_providers is { } currentProviders) SourceStatus.Text = "ⓘ " + currentProviders.SourceDescription;
        }
        catch (Exception) { SaveStatus.Text = "Could not save all changes. Please try again."; }
        finally { _saving = false; ValidateThresholds(false); }
    }
    private static int RefreshMinutes(RadioButton choice) => int.Parse((string)choice.Tag, CultureInfo.InvariantCulture);
    private void OnRefreshSelected(object sender, RoutedEventArgs e)
    {
        // Checked also fires while InitializeComponent connects the default segment.
        if (RefreshChoices is null || RefreshRateLimitNote is null) return;
        foreach (var choice in RefreshChoices.Children.OfType<RadioButton>())
            choice.IsTabStop = choice.IsChecked == true;
        RefreshRateLimitNote.Visibility = Refresh1.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnRefreshKeyDown(object sender, KeyEventArgs e)
    {
        var choices = RefreshChoices.Children.OfType<RadioButton>().ToArray();
        var index = Array.FindIndex(choices, choice => choice.IsChecked == true);
        var next = e.Key switch
        {
            Key.Left or Key.Up => (index + choices.Length - 1) % choices.Length,
            Key.Right or Key.Down => (index + 1) % choices.Length,
            Key.Home => 0,
            Key.End => choices.Length - 1,
            _ => -1
        };
        if (next < 0) return;
        choices[next].IsChecked = true;
        choices[next].Focus();
        e.Handled = true;
    }
    private void OnClose(object sender, RoutedEventArgs e) { if (!_saving) Close(); }
    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnTitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) OnMaximize(sender, e); else if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
    }
    private void OnSettingsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
            Sections.SelectedIndex = (Sections.SelectedIndex + direction + Sections.Items.Count) % Sections.Items.Count;
            ((TabItem)Sections.SelectedItem).Focus(); e.Handled = true;
        }
        else if (e.Key == Key.Escape) { if (!_saving) Close(); e.Handled = true; }
        else if (e.Key == Key.Enter) { if (SaveButton.IsEnabled) OnSave(sender, e); e.Handled = true; }
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateThemeDescription();
            UpdateWindowDecoration();
        }));
    }
    private void UpdateWindowDecoration()
    {
        var dark = _themeSource.Current == WindowsTheme.Dark;
        SettingsDecoration.Apply(SettingsSurface, _themeSource.Current);
        SettingsShadow.Effect = dark ? new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = System.Windows.Media.Colors.Black,
            Direction = 270, ShadowDepth = 12, BlurRadius = 32, Opacity = .6
        } : null;
    }
    private void UpdateThemeDescription() => ThemeDescription.Text = $"Currently {_themeSource.Current}. Change it in Windows Settings › Personalization › Colors.";
    private void OnTickSizeChanged(object sender, SizeChangedEventArgs e) => DrawThresholdTicks();
    private void DrawThresholdTicks()
    {
        if (TickCanvas is null || ThresholdInput is null) return;
        TickCanvas.Children.Clear();
        if (!SettingsThresholdEditor.TryParse(ThresholdInput.Text, out var values, out _)) return;
        foreach (var value in values)
        {
            var x = Math.Max(0, (TickCanvas.ActualWidth - 14) * value / 100.0);
            var tick = new System.Windows.Shapes.Rectangle { Width = 1, Height = 12 };
            tick.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "SettingsTextBrush");
            Canvas.SetLeft(tick, x + 7); Canvas.SetTop(tick, 8); TickCanvas.Children.Add(tick);
            var label = new TextBlock { Text = value.ToString(CultureInfo.InvariantCulture), FontSize = 11 };
            label.SetResourceReference(TextBlock.ForegroundProperty, "SettingsSecondaryBrush");
            Canvas.SetLeft(label, x); Canvas.SetTop(label, 20); TickCanvas.Children.Add(label);
        }
    }
    private void OnToggleMotion(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.CheckBox toggle || !toggle.IsLoaded || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
        if (toggle.Template.FindName("Knob", toggle) is not System.Windows.Shapes.Ellipse knob ||
            knob.RenderTransform is not System.Windows.Media.TranslateTransform translate) return;
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new System.Windows.Media.Animation.DoubleAnimation(toggle.IsChecked == true ? -16 : 16, 0, TimeSpan.FromMilliseconds(167))
            { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }
    private void OnPreviewNotification(object sender, RoutedEventArgs e)
    {
        if (_previewNotification is { } preview) preview();
        else SaveStatus.Text = "Notification preview is unavailable.";
    }
    private void UpdateFontStatus()
    {
        var installed = _font?.IsInstalled() == true;
        var available = _font?.IsBundledFontAvailable() == true;
        InstallYasbFont.IsEnabled = !installed && available;
        FontStatus.Text = installed ? "IosevkaTerm Nerd Font is installed." : available ? "Install the bundled font for YASB quota glyphs." : "Bundled font not found.";
    }
    private void OnInstallYasbFont(object sender, RoutedEventArgs e)
    {
        if (_font is null || !InstallYasbFont.IsEnabled) return;
        try
        {
            var failure = _font.Install();
            UpdateFontStatus();
            FontStatus.Text = failure ?? "Installed. Restart YASB to use it.";
        }
        catch (Exception) { FontStatus.Text = "Could not install the font. Please try again."; }
    }
    private void OnPreviewGlow(object sender, RoutedEventArgs e) => EdgeGlowWindow.ShowOnAllMonitors(true);
    private void OnPreviewConfetti(object sender, RoutedEventArgs e) => ConfettiWindow.ShowOnAllMonitors(true);
}

/// <summary>Single live settings window. Call Show(owner) from either the panel or the tray on the UI thread.</summary>
public sealed class SettingsWindowHost(AppearanceSettings appearance, QuotaAlertObserver alerts,
    SettingsProviderCommands? providers = null, Action? previewNotification = null, DockSettings? dock = null, SettingsGlyphFont? font = null) : IDisposable
{
    private SettingsWindow? _window;
    private bool _disposed;
    public async void Show(Window? owner = null)
    {
        if (_disposed) return;
        if (_window is { } existing) { if (existing.IsVisible) existing.Activate(); return; }
        var window = new SettingsWindow(appearance, alerts.Settings, alerts.SaveSettingsAsync, providers: providers, previewNotification: previewNotification, dock: dock, font: font);
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
