using System.Windows;
using System.Windows.Input;

namespace AIBar.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsTranslucentProperty = DependencyProperty.Register(
        nameof(IsTranslucent), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    private readonly Action<System.Diagnostics.ProcessStartInfo>? _launchLink;
    public MainWindow() : this(null) { }
    public MainWindow(Action<System.Diagnostics.ProcessStartInfo>? launchLink)
    {
        _launchLink = launchLink;
        InitializeComponent();
    }

    public event Action? SettingsRequested;
    public event Action? QuitRequested;
    internal bool IsSettingsMenuOpen { get; set; }

    private void OnCost(object sender, RoutedEventArgs e)
    {
        SummaryView.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        SecondaryScrollViewer.ScrollToTop();
        BackButton.Focus();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        DetailView.Visibility = Visibility.Collapsed;
        SummaryView.Visibility = Visibility.Visible;
        CostButton.Focus();
        CostButton.BringIntoView();
    }

    private void OnNavigationKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space)) return;
        if (ReferenceEquals(sender, CostButton)) OnCost(sender, e);
        else if (ReferenceEquals(sender, BackButton)) OnBack(sender, e);
        e.Handled = true;
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
    private void OnStatusPages(object sender, RoutedEventArgs e)
    {
        ProviderLinks.OpenStatus(AIBar.Domain.StatusProvider.Codex, _launchLink);
        if (sender is System.Windows.Controls.Button { CommandParameter: true })
            ProviderLinks.OpenStatus(AIBar.Domain.StatusProvider.Claude, _launchLink);
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
