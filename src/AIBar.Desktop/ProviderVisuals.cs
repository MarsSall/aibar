using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
// Desktop also references WinForms and System.Drawing.
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace AIBar.Desktop;

public enum PanelTone { Ok, Near, Full, Muted }

/// <summary>Panel-only colors and copy. Brands identify providers; pace warns separately.</summary>
public static partial class PanelPalette
{
    public static Color ToneColor(PanelTone tone, bool dark) => tone == PanelTone.Near
        ? dark ? Color.FromRgb(0xFF, 0x9F, 0x5A) : Color.FromRgb(0xC4, 0x58, 0x1F)
        : dark ? Color.FromRgb(0xA1, 0xA1, 0xA6) : Color.FromRgb(0x6E, 0x6E, 0x73);

    /// <summary>Mockup tint (82% opacity), opaque unless acrylic succeeds.</summary>
    public static Color Surface(bool dark, bool translucent) => dark
        ? Color.FromArgb(translucent ? (byte)0xD1 : (byte)0xFF, 0x24, 0x24, 0x28)
        : Color.FromArgb(translucent ? (byte)0xD1 : (byte)0xFF, 0xEC, 0xEC, 0xF6);

    // Restore the pre-CodexBar semantic ramp without changing pace-warning colors.
    public static Color UsageColor(decimal usage, bool dark) => usage >= 90
        ? dark ? Color.FromRgb(0xFF, 0x4D, 0x5E) : Color.FromRgb(0xC8, 0x20, 0x3C)
        : usage >= 75
            ? dark ? Color.FromRgb(0xFF, 0xC5, 0x31) : Color.FromRgb(0xA8, 0x6B, 0x00)
            : dark ? Color.FromRgb(0x3D, 0xDC, 0x84) : Color.FromRgb(0x15, 0x92, 0x4A);

    public static Color ProviderColor(string provider) => provider == "Claude"
        ? Color.FromRgb(0xC4, 0x70, 0x3F) : Color.FromRgb(0x10, 0xA3, 0x7F);

    /// <summary>Panel only; the countdown in copy summary and exports is unchanged.</summary>
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

    internal static bool IsDark(Brush semanticSurface) => semanticSurface is SolidColorBrush brush &&
        (0.2126 * brush.Color.R + 0.7152 * brush.Color.G + 0.0722 * brush.Color.B) / 255 < 0.5;

    [GeneratedRegex(@"^(\d+)d (\d+)h (\d+)m$")] private static partial Regex DaysPattern();
    [GeneratedRegex(@"^(\d+)h (\d+)m$")] private static partial Regex HoursPattern();
}

/// <summary>Inputs: value, semantic surface, its Color (theme invalidation), optional
/// current flag and provider name. High contrast always uses system colors.</summary>
public sealed class ProviderVisuals : IMultiValueConverter
{
    private static readonly Dictionary<Color, Brush> Cache = [];

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not SolidColorBrush semantic) return DependencyProperty.UnsetValue;
        var kind = parameter as string;
        if (SystemParameters.HighContrast || DependencyPropertyHelper.GetValueSource(semantic, SolidColorBrush.ColorProperty).IsExpression)
            return kind switch
            {
                "Meter" or "Codex" or "Claude" or "ClaudeMark" => SystemColors.HighlightBrush,
                "Notice" or "Muted" or "PaceText" or "Border" or "Line" => SystemColors.WindowTextBrush,
                _ => SystemColors.WindowBrush
            };
        var dark = PanelPalette.IsDark(semantic);
        var current = values.Length < 4 || values[3] is not bool flag || flag;
        return kind switch
        {
            "Surface" => Frozen(PanelPalette.Surface(dark, values[0] is true)),
            "Well" => Frozen(dark ? Color.FromArgb(0x29, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x24, 0x3C, 0x3C, 0x43)),
            "Border" => Frozen(Color.FromArgb(dark ? (byte)0x1F : (byte)0x99, 0xFF, 0xFF, 0xFF)),
            "Line" => Frozen(dark ? Color.FromArgb(0x24, 0xEB, 0xEB, 0xF5) : Color.FromArgb(0x2E, 0x3C, 0x3C, 0x43)),
            "Muted" => Frozen(PanelPalette.ToneColor(PanelTone.Muted, dark)),
            "PaceText" => Frozen(PanelPalette.ToneColor(values[0] is PanelTone tone ? tone : PanelTone.Muted, dark)),
            "Codex" or "Claude" => Frozen(PanelPalette.ProviderColor(kind)),
            "ClaudeMark" => Frozen(Color.FromRgb(0xD9, 0x77, 0x57)),
            "Notice" => Frozen(PanelPalette.ToneColor(PanelTone.Near, dark)),
            "Meter" => Frozen(!current || Usage(values[0]) is null or < 0
                ? dark ? Color.FromRgb(0xA1, 0xA1, 0xA1) : Color.FromRgb(0x6E, 0x6E, 0x6E)
                : values.Length > 5 && values[5] is PanelBarColorMode.Usage
                    ? PanelPalette.UsageColor(Usage(values[0])!.Value, dark)
                    : PanelPalette.ProviderColor(values.Length > 4 ? values[4] as string ?? "Codex" : "Codex")),
            _ => DependencyProperty.UnsetValue
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();

    internal static decimal? Usage(object value) => value switch
    {
        decimal number => number,
        double number when !double.IsNaN(number) => (decimal)number,
        int number => number,
        _ => null
    };

    private static Brush Frozen(Color color)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(color, out var cached)) return cached;
            var brush = new SolidColorBrush(color); brush.Freeze(); Cache[color] = brush; return brush;
        }
    }
}

public sealed class PanelResetConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => PanelPalette.CompactReset(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PanelTextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Panel copy only; shared presentation/export/copy wording is unchanged.</summary>
public sealed partial class PanelUpdatedConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Format(value as string, null);
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        Format(values.ElementAtOrDefault(0) as string, values.ElementAtOrDefault(1) as string);
    private static string Format(string? updated, string? source)
    {
        var text = string.IsNullOrWhiteSpace(updated) ? "Not updated yet" : MinutesPattern().Replace(updated, "$1 min ago");
        return text + (source == "Other machine" ? " · from other machine" : "");
    }
    [GeneratedRegex(@"(\d+)m ago$")] private static partial Regex MinutesPattern();
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PanelWindowLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "Weekly quota" => "Weekly",
        "5-hour quota" or "5-hour" => "Session",
        _ => value
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
