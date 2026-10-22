using System.Windows;
using System.Windows.Input;

namespace AIBar.Desktop;

public enum PanelBarColorMode { Brand, Usage }

public partial class MainWindow : Window
{
    public static readonly DependencyProperty BackgroundOpacityProperty = DependencyProperty.Register(
        nameof(BackgroundOpacity), typeof(double), typeof(MainWindow), new FrameworkPropertyMetadata(1d, null,
            (_, value) => value is double number && double.IsFinite(number) ? Math.Clamp(number, .30, 1) : 1d));
    /// <summary>Alpha of the background brush only; never UIElement.Opacity.</summary>
    public double BackgroundOpacity { get => (double)GetValue(BackgroundOpacityProperty); set => SetValue(BackgroundOpacityProperty, value); }

    public static readonly DependencyProperty BarColorModeProperty = DependencyProperty.Register(
        nameof(BarColorMode), typeof(PanelBarColorMode), typeof(MainWindow), new PropertyMetadata(PanelBarColorMode.Brand));
    public PanelBarColorMode BarColorMode { get => (PanelBarColorMode)GetValue(BarColorModeProperty); set => SetValue(BarColorModeProperty, value); }

    public static readonly DependencyProperty ProviderTintProperty = DependencyProperty.Register(
        nameof(ProviderTint), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));
    /// <summary>False keeps border, glow, header divider and selected tab neutral.</summary>
    public bool ProviderTint { get => (bool)GetValue(ProviderTintProperty); set => SetValue(ProviderTintProperty, value); }

    public static readonly DependencyProperty SelectedProviderProperty = DependencyProperty.Register(
        nameof(SelectedProvider), typeof(string), typeof(MainWindow), new PropertyMetadata("Codex"));
    public string SelectedProvider { get => (string)GetValue(SelectedProviderProperty); private set => SetValue(SelectedProviderProperty, value); }
    public static readonly DependencyProperty ClaudeEnabledProperty = DependencyProperty.Register(
        nameof(ClaudeEnabled), typeof(bool), typeof(MainWindow), new PropertyMetadata(false, OnProviderInputsChanged));
    public bool ClaudeEnabled { get => (bool)GetValue(ClaudeEnabledProperty); set => SetValue(ClaudeEnabledProperty, value); }
    public static readonly DependencyProperty CodexUsageProperty = DependencyProperty.Register(
        nameof(CodexUsage), typeof(decimal), typeof(MainWindow), new PropertyMetadata(-1m, OnProviderInputsChanged));
    public decimal CodexUsage { get => (decimal)GetValue(CodexUsageProperty); set => SetValue(CodexUsageProperty, value); }
    public static readonly DependencyProperty ClaudeUsageProperty = DependencyProperty.Register(
        nameof(ClaudeUsage), typeof(decimal), typeof(MainWindow), new PropertyMetadata(-1m, OnProviderInputsChanged));
    public decimal ClaudeUsage { get => (decimal)GetValue(ClaudeUsageProperty); set => SetValue(ClaudeUsageProperty, value); }
    private string? _rememberedProvider;
    private static void OnProviderInputsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((MainWindow)sender).UpdateSelection();
    private void UpdateSelection() => SelectedProvider = !ClaudeEnabled ? "Codex" : _rememberedProvider ?? (ClaudeUsage > CodexUsage ? "Claude" : "Codex");
    public void SelectProvider(string provider)
    {
        if (provider is not ("Codex" or "Claude") || provider == "Claude" && !ClaudeEnabled) return;
        var changed = SelectedProvider != provider;
        _rememberedProvider = provider;
        UpdateSelection();
        if (changed && IsLoaded) AnimateBody(SummaryView, 0, 83);
    }
    private void OnProviderTab(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.RadioButton { Tag: string provider }) SelectProvider(provider);
    }
    private void OnProviderTabKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return;
        var provider = !ClaudeEnabled || e.Key == Key.Home ? "Codex"
            : e.Key == Key.End ? "Claude" : SelectedProvider == "Codex" ? "Claude" : "Codex";
        SelectProvider(provider);
        (provider == "Claude" ? ClaudeTab : CodexTab).Focus();
        e.Handled = true;
    }
    private void OnUsageDashboard(object sender, RoutedEventArgs e) => ProviderLinks.OpenUsage(
        SelectedProvider == "Claude" ? AIBar.Domain.StatusProvider.Claude : AIBar.Domain.StatusProvider.Codex, _launchLink);
    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var assembly = typeof(MainWindow).Assembly;
        var version = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown version";
        AboutText.Text = $"AIBar {version}\nQuota and local usage for Codex and Claude.\nProvider marks: Simple Icons (CC0).\nPanel inspired by CodexBar; AIBar is an independent app.";
        AboutText.Visibility = AboutText.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }
    public static readonly DependencyProperty IsTranslucentProperty = DependencyProperty.Register(
        nameof(IsTranslucent), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private readonly Action<System.Diagnostics.ProcessStartInfo>? _launchLink;
    private readonly PanelMotion _motion = new();
    private int _showGeneration;
    public MainWindow() : this(null) { }
    public MainWindow(Action<System.Diagnostics.ProcessStartInfo>? launchLink)
    {
        _launchLink = launchLink;
        InitializeComponent();
        PreviewKeyDown += OnPanelKeyDown;
        IsVisibleChanged += (_, _) =>
        {
            var generation = ++_showGeneration;
            _motion.Complete();
            if (!IsVisible) return;
            // IsVisibleChanged can precede Loaded and the host's final placement.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (generation == _showGeneration && IsVisible)
                    AnimateBody((FrameworkElement)Content, 8, 167, vertical: true);
            }));
        };
        Closed += (_, _) => { ++_showGeneration; _motion.Complete(); };
        SetBinding(ClaudeEnabledProperty, new System.Windows.Data.Binding("Claude.IsEnabled") { FallbackValue = false });
        BindUsage(CodexUsageProperty, "Primary.PercentageUsed", "Weekly.PercentageUsed");
        BindUsage(ClaudeUsageProperty, "Claude.State.Primary.PercentageUsed", "Claude.State.Weekly.PercentageUsed");
    }

    private void BindUsage(DependencyProperty property, string primary, string weekly)
    {
        var binding = new System.Windows.Data.MultiBinding { Converter = new PanelMaximumUsageConverter() };
        binding.Bindings.Add(new System.Windows.Data.Binding(primary));
        binding.Bindings.Add(new System.Windows.Data.Binding(weekly));
        System.Windows.Data.BindingOperations.SetBinding(this, property, binding);
    }

    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    public event Action? UseOtherMachineRequested;
    private void OnUseOtherMachine(object sender, RoutedEventArgs e) => UseOtherMachineRequested?.Invoke();
    internal bool IsSettingsMenuOpen { get; set; }

    private void OnCost(object sender, RoutedEventArgs e)
    {
        SummaryView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        SecondaryScrollViewer.ScrollToTop();
        BackButton.Focus();
        AnimateBody(DetailView, 24, 167);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        DetailView.Visibility = Visibility.Collapsed;
        SummaryView.Visibility = Visibility.Visible;
        CostButton.Focus();
        CostButton.BringIntoView();
        AnimateBody(SummaryView, -24, 167);
    }

    private void OnNavigationKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        if (ReferenceEquals(sender, CostButton)) OnCost(sender, e);
        else if (ReferenceEquals(sender, BackButton)) OnBack(sender, e);
        e.Handled = true;
    }

    private void AnimateBody(FrameworkElement body, double offset, int duration, bool vertical = false) =>
        _motion.Begin(body, PanelMotion.CanAnimate(this, SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast),
            offset, duration, vertical);

    /// <summary>HostRuntime should use this instead of Window.Hide for outside-click dismissal.</summary>
    public void Dismiss()
    {
        if (!IsVisible) return;
        ++_showGeneration;
        _motion.Begin((FrameworkElement)Content,
            PanelMotion.CanAnimate(this, SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast),
            0, 120, closing: true, onCompleted: Hide);
    }

    internal bool ExecuteShortcut(Key key)
    {
        if (key == Key.OemComma) { SettingsRequested?.Invoke(); return true; }
        if (key == Key.Q) { QuitRequested?.Invoke(); return true; }
        var index = key == Key.R ? 2 : key == Key.C ? 3 : -1;
        if (index < 0) return false;
        var button = PanelActions.Children.OfType<System.Windows.Controls.Button>().ElementAt(index);
        if (button.IsEnabled && button.Command?.CanExecute(button.CommandParameter) == true) button.Command.Execute(button.CommandParameter);
        return true;
    }

    private void OnPanelKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Handled) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && ExecuteShortcut(e.Key)) { e.Handled = true; return; }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; return; }
        if (e.Key == Key.Back && DetailView.Visibility == Visibility.Visible) { OnBack(this, e); e.Handled = true; return; }
        if (e.Key is Key.Left or Key.Right && SummaryView.Visibility == Visibility.Visible)
        {
            SelectProvider(SelectedProvider == "Claude" ? "Codex" : "Claude");
            (SelectedProvider == "Claude" ? ClaudeTab : CodexTab).Focus();
            e.Handled = true; return;
        }
        if (e.Key is not (Key.Up or Key.Down)) return;
        // Preserve arrow navigation in the chart range and other detail disclosures.
        if (DetailView.Visibility == Visibility.Visible && !PanelActions.IsKeyboardFocusWithin) return;
        var buttons = PanelActions.Children.OfType<System.Windows.Controls.Button>().Where(button => button.IsVisible && button.IsEnabled).ToArray();
        if (buttons.Length == 0) return;
        var index = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin);
        index = index < 0 ? (e.Key == Key.Down ? 0 : buttons.Length - 1) : (index + (e.Key == Key.Down ? 1 : buttons.Length - 1)) % buttons.Length;
        buttons[index].Focus(); e.Handled = true;
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
    private void OnStatusPages(object sender, RoutedEventArgs e)
    {
        ProviderLinks.OpenStatus(SelectedProvider == "Claude" ? AIBar.Domain.StatusProvider.Claude : AIBar.Domain.StatusProvider.Codex, _launchLink);
    }

    public static readonly DependencyProperty ProviderStatusProperty = DependencyProperty.Register(
        nameof(ProviderStatus), typeof(ProviderStatusPresentation), typeof(MainWindow));
    public ProviderStatusPresentation? ProviderStatus
    {
        get => (ProviderStatusPresentation?)GetValue(ProviderStatusProperty);
        set => SetValue(ProviderStatusProperty, value);
    }

    public static readonly DependencyProperty PaceHistoryProperty = DependencyProperty.Register(
        nameof(PaceHistory), typeof(PanelPaceHistory), typeof(MainWindow));
    public PanelPaceHistory? PaceHistory
    {
        get => (PanelPaceHistory?)GetValue(PaceHistoryProperty);
        set => SetValue(PaceHistoryProperty, value);
    }

    private void OnCodexUsage(object sender, RoutedEventArgs e) => ProviderLinks.OpenUsage(AIBar.Domain.StatusProvider.Codex, _launchLink);
    private void OnClaudeUsage(object sender, RoutedEventArgs e) => ProviderLinks.OpenUsage(AIBar.Domain.StatusProvider.Claude, _launchLink);
    private void OnCodexStatus(object sender, RoutedEventArgs e) => ProviderLinks.OpenStatus(AIBar.Domain.StatusProvider.Codex, _launchLink);
    private void OnClaudeStatus(object sender, RoutedEventArgs e) => ProviderLinks.OpenStatus(AIBar.Domain.StatusProvider.Claude, _launchLink);

    /// <summary>True only while the acrylic backdrop is active; the surface then uses a translucent tint.</summary>
    public bool IsTranslucent
    {
        get => (bool)GetValue(IsTranslucentProperty);
        set => SetValue(IsTranslucentProperty, value);
    }

    internal System.Windows.Media.Color? OpaqueCompositionColor { get; set; }

    private void OnPanelDisclosureKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (ReferenceEquals(sender, e.OriginalSource) && sender is System.Windows.Controls.Expander details &&
            e.Key is Key.Space or Key.Enter)
        {
            details.IsExpanded = !details.IsExpanded;
            e.Handled = true;
        }
    }

    private void OnSecondaryPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement focused) focused.BringIntoView();
    }
}
