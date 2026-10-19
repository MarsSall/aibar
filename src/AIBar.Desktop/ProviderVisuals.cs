using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
// The Desktop project also references WinForms/System.Drawing; pin the WPF types.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using SystemColors = System.Windows.SystemColors;

namespace AIBar.Desktop;

public enum PanelTone { Ok, Near, Full, Muted }

/// <summary>Approved panel identity values. The bar color states the condition, never the provider.</summary>
public static partial class PanelPalette
{
    public const decimal NearThreshold = 75;
    public const decimal FullThreshold = 90;

    public static PanelTone Tone(decimal? usage, bool isCurrent) =>
        !isCurrent ? PanelTone.Muted
        : usage >= FullThreshold ? PanelTone.Full
        : usage >= NearThreshold ? PanelTone.Near
        : PanelTone.Ok;

    public static string? FlagText(decimal? usage, bool isCurrent) => Tone(usage, isCurrent) switch
    {
        PanelTone.Near => "High usage",
        PanelTone.Full => "Near limit",
        _ => null
    };

    public static Color ToneColor(PanelTone tone, bool dark) => tone switch
    {
        PanelTone.Ok => dark ? Rgb(0x3D, 0xDC, 0x84) : Rgb(0x15, 0x92, 0x4A),
        PanelTone.Near => dark ? Rgb(0xFF, 0xC5, 0x31) : Rgb(0xA8, 0x6B, 0x00),
        PanelTone.Full => dark ? Rgb(0xFF, 0x4D, 0x5E) : Rgb(0xC8, 0x20, 0x3C),
        _ => dark ? Rgb(0xA1, 0xA2, 0xA8) : Rgb(0x66, 0x67, 0x6D)
    };

    /// <summary>Graphite surface; the alpha only drops while the acrylic backdrop is active.</summary>
    public static Color Surface(bool dark, bool translucent) => dark
        ? Color.FromArgb(translucent ? (byte)0xBD : (byte)0xFF, 0x23, 0x24, 0x28)
        : Color.FromArgb(translucent ? (byte)0xC2 : (byte)0xFF, 0xF6, 0xF6, 0xF8);

    /// <summary>Panel-only reset text; the shared countdown used by copy summary is unchanged.</summary>
    public static string? CompactReset(string? countdown)
    {
        if (countdown is null) return null;
        var days = DaysPattern().Match(countdown);
        if (days.Success) return $"{int.Parse(days.Groups[1].Value)}d {int.Parse(days.Groups[2].Value)}h";
        var hours = HoursPattern().Match(countdown);
        if (!hours.Success) return countdown;
        var (h, m) = (int.Parse(hours.Groups[1].Value), int.Parse(hours.Groups[2].Value));
        return h > 0 ? $"{h}h {m}m" : m > 0 ? $"{m}m" : "<1m";
    }

    internal static bool IsDark(Brush semanticSurface) => semanticSurface is SolidColorBrush brush && Luminance(brush.Color) < 0.5;

    private static double Luminance(Color color) => (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    [GeneratedRegex(@"^(\d+)d (\d+)h (\d+)m$")] private static partial Regex DaysPattern();
    [GeneratedRegex(@"^(\d+)h (\d+)m$")] private static partial Regex HoursPattern();
}

/// <summary>Panel-only brushes. Inputs: a value (usage, translucency or zero), the semantic
/// SurfaceBrush, its Color (invalidates the binding when a system color changes in place),
/// optionally whether the value is current and, for MeterText, the ink brush. High contrast keeps system colors.</summary>
public sealed class ProviderVisuals : IMultiValueConverter
{
    private static readonly Dictionary<Color, Brush> Cache = [];

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not SolidColorBrush semantic)
            return DependencyProperty.UnsetValue;
        var kind = parameter as string;
        var systemColor = DependencyPropertyHelper.GetValueSource(semantic, SolidColorBrush.ColorProperty).IsExpression;
        // MeterText falls back to the ink brush passed as the fifth input.
        var ink = values.Length > 4 && values[4] is Brush inkBrush ? inkBrush : DependencyProperty.UnsetValue;
        if (SystemParameters.HighContrast || systemColor) return kind == "MeterText" ? ink : HighContrast(kind, semantic);
        var dark = PanelPalette.IsDark(semantic);
        var isCurrent = values.Length < 4 || values[3] is not bool current || current;
        return kind switch
        {
            "Surface" => Frozen(PanelPalette.Surface(dark, values[0] is true)),
            "Band" => Frozen(dark ? Color.FromArgb(0x73, 0x14, 0x15, 0x18) : Color.FromArgb(0x8C, 0xE4, 0xE4, 0xEA)),
            "Well" => Frozen(dark ? Color.FromArgb(0x99, 0x0A, 0x0B, 0x0D) : Color.FromArgb(0xB3, 0xCD, 0xCE, 0xD5)),
            "Border" => Frozen(dark ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF) : Color.FromRgb(0xD6, 0xD7, 0xDC)),
            "Line" => Frozen(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
            "Pill" => Frozen(dark ? Color.FromArgb(0x73, 0x14, 0x15, 0x18) : Color.FromArgb(0x8C, 0xE4, 0xE4, 0xEA)),
            "Muted" => Frozen(PanelPalette.ToneColor(PanelTone.Muted, dark)),
            "PaceText" => Frozen(PanelPalette.ToneColor(values[0] is PanelTone tone ? tone : PanelTone.Muted, dark)),
            "Brand" => Frozen(dark ? Color.FromRgb(0xFF, 0x00, 0x36) : Color.FromRgb(0xE0, 0x00, 0x2F)),
            "Codex" => Frozen(dark ? Color.FromRgb(0x2F, 0xBF, 0x98) : Color.FromRgb(0x0B, 0x7D, 0x61)),
            "Claude" => Frozen(dark ? Color.FromRgb(0xE0, 0x87, 0x6A) : Color.FromRgb(0xB4, 0x5A, 0x3D)),
            "Notice" => Frozen(PanelPalette.ToneColor(PanelTone.Near, dark)),
            "NoticeFill" => Frozen(dark ? Color.FromArgb(0x8C, 0x2E, 0x26, 0x14) : Color.FromArgb(0x99, 0xFB, 0xEF, 0xD6)),
            "Meter" => Frozen(PanelPalette.ToneColor(PanelPalette.Tone(Usage(values[0]), isCurrent), dark)),
            // Percent text keeps the ink color unless an alert tone needs the same color as its bar.
            "MeterText" => PanelPalette.Tone(Usage(values[0]), isCurrent) is PanelTone.Near or PanelTone.Full
                ? Frozen(PanelPalette.ToneColor(PanelPalette.Tone(Usage(values[0]), isCurrent), dark))
                : ink,
            "FlagFill" => Frozen(WithAlpha(PanelPalette.ToneColor(PanelPalette.Tone(Usage(values[0]), isCurrent), dark), 0x1F)),
            _ => DependencyProperty.UnsetValue
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static decimal? Usage(object value) => value switch
    {
        decimal number => number,
        double number when !double.IsNaN(number) => (decimal)number,
        int number => number,
        _ => null
    };

    private static object HighContrast(string? kind, SolidColorBrush semantic) => kind switch
    {
        "Meter" or "Brand" or "Codex" or "Claude" => SystemColors.HighlightBrush,
        "Notice" or "Muted" or "PaceText" => SystemColors.WindowTextBrush,
        "Border" or "Line" => SystemColors.WindowTextBrush,
        "FlagFill" or "NoticeFill" or "Band" or "Pill" or "Well" => SystemColors.WindowBrush,
        _ => semantic
    };

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Brush Frozen(Color color)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(color, out var cached)) return cached;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Cache[color] = brush;
            return brush;
        }
    }
}

/// <summary>Inputs: usage, isCurrent. Parameter "Text" returns the flag words; "Visibility" collapses without a flag.</summary>
public sealed class PanelFlagConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var usage = values.Length > 0 ? ProviderVisuals.Usage(values[0]) : null;
        var isCurrent = values.Length < 2 || values[1] is not bool current || current;
        var flag = PanelPalette.FlagText(usage, isCurrent);
        return parameter as string == "Visibility" ? flag is null ? Visibility.Collapsed : Visibility.Visible : flag ?? string.Empty;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Soft halo for provider marks; none in high contrast.</summary>
public sealed class PanelGlowConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (SystemParameters.HighContrast || values.Length < 2 || values[1] is not SolidColorBrush semantic) return null;
        if (DependencyPropertyHelper.GetValueSource(semantic, SolidColorBrush.ColorProperty).IsExpression) return null;
        var color = (Color)ColorConverter.ConvertFromString(parameter as string ?? "#D97757");
        var effect = new DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = 10, Opacity = PanelPalette.IsDark(semantic) ? 0.75 : 0.35 };
        effect.Freeze();
        return effect;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PanelResetConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        PanelPalette.CompactReset(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Collapses notice cards and pills whose text is empty.</summary>
public sealed class PanelTextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Splits the footer row in two so Refresh sits on the right half.</summary>
public sealed class PanelHalfWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && width > 0 ? Math.Floor(width / 2) : double.NaN;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
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
