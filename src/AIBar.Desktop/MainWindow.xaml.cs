using System.Windows;
using System.Windows.Input;

namespace AIBar.Desktop;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnSecondaryPreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement focused) focused.BringIntoView();
    }
}
