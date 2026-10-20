using System.Globalization;
using System.Windows.Data;

namespace AIBar.Desktop;

/// <summary>Panel-only currency typography; aggregate values and exports remain unchanged.</summary>
public sealed class PanelCostLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string label ? System.Text.RegularExpressions.Regex.Replace(label, @"\$(?=\S)", "$ ") : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
