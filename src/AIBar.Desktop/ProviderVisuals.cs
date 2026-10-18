using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIBar.Desktop;

/// <summary>Panel-only palette. Semantic brushes remain authoritative in high contrast.</summary>
public sealed class ProviderVisuals : IMultiValueConverter
{
    private static readonly Brush Codex = Frozen("#10A37F");
    private static readonly Brush Claude = Frozen("#D97757");
    private static readonly Brush Amber = Frozen("#D99A25");
    private static readonly Brush Red = Frozen("#EF5B67");
    private static readonly Brush DarkSurface = Frozen("#1F1F23");
    private static readonly Brush LightSurface = Frozen("#F7F7F9");
    private static readonly Brush DarkBorder = Frozen("#39393F");
    private static readonly Brush LightBorder = Frozen("#DDDEE3");

    // Inputs: usage (or zero), semantic brush, semantic brush.Color. The last input
    // invalidates the binding when a dynamic system color changes in place.
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not SolidColorBrush semantic)
            return DependencyProperty.UnsetValue;
        var kind = parameter as string;
        var systemColor = DependencyPropertyHelper.GetValueSource(semantic, SolidColorBrush.ColorProperty).IsExpression;
        if (SystemParameters.HighContrast || systemColor) return semantic;
        if (kind is "Surface" or "Border")
        {
            var dark = semantic.Color.R < 128;
            return kind == "Surface" ? dark ? DarkSurface : LightSurface : dark ? DarkBorder : LightBorder;
        }
        var usage = values[0] is double number ? number : 0;
        return usage >= 90 ? Red : usage >= 75 ? Amber : kind == "Claude" ? Claude : Codex;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}

public sealed class PanelWindowLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "Weekly quota" => "Weekly",
        "5-hour quota" => "5-hour",
        _ => value
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
