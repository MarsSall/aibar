using System.Globalization;
using System.Windows.Data;

namespace AIBar.Desktop;

/// <summary>Panel-only currency typography; aggregate values and exports remain unchanged.</summary>
public sealed class PanelCostLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string label) return value;
        var formatted = System.Text.RegularExpressions.Regex.Replace(label, @"\$(?=\S)", "$ ");
        if (parameter is not string part) return formatted;
        var colon = formatted.IndexOf(':');
        var separator = formatted.IndexOf('·');
        if (part == "Amount") return separator >= 0 ? formatted[(colon + 1)..separator].Trim() : formatted[(colon + 1)..].Trim();
        if (part == "Meta") return (formatted.StartsWith("Today", StringComparison.Ordinal) ? "Today" : "30 days") +
            (separator >= 0 ? " · " + formatted[(separator + 1)..].Trim() : "");
        return formatted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
