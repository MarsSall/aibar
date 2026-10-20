using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AIBar.Desktop;

/// <summary>Selection is UI-only; provider data and commands keep their independent lifetimes.</summary>
public sealed class PanelProviderSelectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var selected = Equals(value, parameter);
        return targetType == typeof(Visibility) ? selected ? Visibility.Visible : Visibility.Collapsed : selected;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class PanelMaximumUsageConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Select(ProviderVisuals.Usage).Where(value => value.HasValue).Select(value => value!.Value).DefaultIfEmpty(-1m).Max();
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
