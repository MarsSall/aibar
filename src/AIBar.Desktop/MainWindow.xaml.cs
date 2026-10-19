using System.Windows;
using System.Windows.Input;

namespace AIBar.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsTranslucentProperty = DependencyProperty.Register(
        nameof(IsTranslucent), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public MainWindow() => InitializeComponent();

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

    private void OnCodexUsage(object sender, RoutedEventArgs e) => ProviderLinks.OpenUsage(AIBar.Domain.StatusProvider.Codex);
    private void OnClaudeUsage(object sender, RoutedEventArgs e) => ProviderLinks.OpenUsage(AIBar.Domain.StatusProvider.Claude);
    private void OnCodexStatus(object sender, RoutedEventArgs e) => ProviderLinks.OpenStatus(AIBar.Domain.StatusProvider.Codex);
    private void OnClaudeStatus(object sender, RoutedEventArgs e) => ProviderLinks.OpenStatus(AIBar.Domain.StatusProvider.Claude);

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
