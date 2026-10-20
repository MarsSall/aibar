using System.Windows;
using System.Windows.Input;

namespace AIBar.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty IsTranslucentProperty = DependencyProperty.Register(
        nameof(IsTranslucent), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public MainWindow() => InitializeComponent();

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
