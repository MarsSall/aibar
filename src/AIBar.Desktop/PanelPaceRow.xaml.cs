using System.Globalization;
using System.Windows;
using System.Windows.Data;
using UserControl = System.Windows.Controls.UserControl;

namespace AIBar.Desktop;

public partial class PanelPaceRow : UserControl
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(PanelPacePresentation), typeof(PanelPaceRow), new PropertyMetadata(null));

    public PanelPacePresentation Data
    {
        get => (PanelPacePresentation)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public PanelPaceRow() => InitializeComponent();
}

/// <summary>Scale both fill and guide against the actual track, including narrow layouts.</summary>
public sealed class PanelPacePositionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var width = values.Length > 1 && values[1] is double w && double.IsFinite(w) ? Math.Max(0, w) : 0;
        var percentage = values.Length > 0 ? ProviderVisuals.Usage(values[0]) ?? 0 : 0;
        var position = width * (double)Math.Clamp(percentage, 0, 100) / 100;
        return parameter as string == "Marker" ? new Thickness(Math.Min(position, Math.Max(0, width - 2)), 0, 0, 0) : position;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
