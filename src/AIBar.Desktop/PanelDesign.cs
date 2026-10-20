using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Application = System.Windows.Application;

namespace AIBar.Desktop;

public static class PanelMenuHints
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.RegisterAttached("Shortcut", typeof(string), typeof(PanelMenuHints), new PropertyMetadata(""));
    public static void SetShortcut(DependencyObject target, string value) => target.SetValue(ShortcutProperty, value);
    public static string GetShortcut(DependencyObject target) => (string)target.GetValue(ShortcutProperty);
}

/// <summary>WPF has no CharacterSpacing. Draw tracked headings without changing UIA text.</summary>
public sealed class PanelSectionLabel : TextBlock
{
    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        return new Size(size.Width + Math.Max(0, Text.Length - 1) * FontSize * .08, size.Height);
    }

    protected override void OnRender(DrawingContext context)
    {
        var x = 0d;
        foreach (var character in Text)
        {
            var glyph = new FormattedText(character.ToString(), CultureInfo.CurrentUICulture,
                FlowDirection, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), FontSize,
                Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            context.DrawText(glyph, new Point(x, 0));
            x += glyph.WidthIncludingTrailingWhitespace + FontSize * .08;
        }
    }
}

public sealed class PanelLoginVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PanelPercentConverter : IValueConverter
{
    public object Convert(object value, Type type, object parameter, CultureInfo culture) =>
        ProviderVisuals.Usage(value) is { } usage ? $"{(usage >= 90 ? "⚠ " : "")}{usage:0}%" : "--";
    public object ConvertBack(object value, Type type, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>All color-only decoration has a text equivalent elsewhere in the meter.</summary>
public sealed class PanelBarDecorationConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
    {
        var brush = values.ElementAtOrDefault(0) as SolidColorBrush;
        var surface = values.ElementAtOrDefault(1) as SolidColorBrush;
        var dark = surface is not null && PanelPalette.IsDark(surface);
        var current = values.ElementAtOrDefault(2) is not false;
        var color = brush?.Color ?? Colors.Transparent;
        var start = Color.FromRgb((byte)(color.R * .58), (byte)(color.G * .58), (byte)(color.B * .58));
        if (dark)
        {
            if (color == PanelPalette.ProviderColor("Claude")) { color = Color.FromRgb(0xE8, 0x95, 0x5F); start = Color.FromRgb(0x8A, 0x4A, 0x27); }
            else if (color == PanelPalette.ProviderColor("Codex")) { color = Color.FromRgb(0x2C, 0xC7, 0x9E); start = Color.FromRgb(0x0B, 0x5F, 0x4A); }
            else if (color == PanelPalette.UsageColor(90, true)) { color = Color.FromRgb(0xFF, 0x6B, 0x5B); start = Color.FromRgb(0x7A, 0x1E, 0x14); }
            else if (color == PanelPalette.UsageColor(75, true)) { color = Color.FromRgb(0xF5, 0xC2, 0x5A); start = Color.FromRgb(0x6E, 0x52, 0x10); }
        }
        if (parameter as string == "Head")
        {
            if (!dark || !current || SystemParameters.HighContrast) return Brushes.Transparent;
            var radial = new RadialGradientBrush();
            radial.GradientStops.Add(new(Color.FromArgb(145, 255, 245, 235), 0));
            radial.GradientStops.Add(new(Color.FromArgb(84, color.R, color.G, color.B), .35));
            radial.GradientStops.Add(new(Colors.Transparent, .7));
            radial.Freeze(); return radial;
        }
        if (!dark || SystemParameters.HighContrast) return brush ?? Brushes.Transparent;
        var fill = new LinearGradientBrush(start, color, 0);
        fill.Freeze(); return fill;
    }
    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Selected-tab treatment is independent of provider selection authority.</summary>
public sealed class PanelTabDecorationConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
    {
        var claude = values.ElementAtOrDefault(0) as string == "Claude";
        var surface = values.ElementAtOrDefault(1) as SolidColorBrush;
        var hc = SystemParameters.HighContrast || surface is not null && DependencyPropertyHelper.GetValueSource(surface, SolidColorBrush.ColorProperty).IsExpression;
        if (hc) return parameter as string == "Border" ? System.Windows.SystemColors.WindowTextBrush : System.Windows.SystemColors.HighlightBrush;
        var dark = surface is not null && PanelPalette.IsDark(surface);
        var usage = ProviderVisuals.Usage(values.ElementAtOrDefault(claude ? 3 : 2) ?? -1m) ?? -1;
        var current = values.ElementAtOrDefault(claude ? 5 : 4) is not false;
        var color = !current ? Color.FromRgb(0x8E, 0x8E, 0x98) : usage >= 90 ? Color.FromRgb(0xFF, 0x6B, 0x5B)
            : claude ? Color.FromRgb(0xE8, 0x95, 0x5F) : Color.FromRgb(0x2C, 0xC7, 0x9E);
        if (parameter as string == "Border") return new SolidColorBrush(dark ? Color.FromArgb(143, color.R, color.G, color.B)
            : usage >= 90 ? Color.FromRgb(0xD1, 0x3B, 0x2A) : claude ? Color.FromRgb(0xC4, 0x70, 0x3F) : Color.FromRgb(0x10, 0xA3, 0x7F));
        if (!dark) return new SolidColorBrush(claude ? Color.FromRgb(0xFB, 0xEF, 0xE7) : Color.FromRgb(0xDD, 0xF2, 0xEB));
        return new LinearGradientBrush(Color.FromArgb(33, color.R, color.G, color.B), Color.FromArgb(8, color.R, color.G, color.B), 90);
    }
    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Root decoration uses the selected provider; stale wins over usage, login over stale.</summary>
public sealed class PanelWindowDecorationConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
    {
        var surface = values.ElementAtOrDefault(0) as SolidColorBrush;
        var dark = surface is not null && PanelPalette.IsDark(surface);
        var claude = values.ElementAtOrDefault(1) as string == "Claude";
        var usage = ProviderVisuals.Usage(values.ElementAtOrDefault(claude ? 3 : 2) ?? -1m) ?? -1m;
        var current = values.ElementAtOrDefault(claude ? 5 : 4) is not false;
        var login = values.ElementAtOrDefault(claude ? 7 : 6) is true;
        var hc = SystemParameters.HighContrast || surface is not null && DependencyPropertyHelper.GetValueSource(surface, SolidColorBrush.ColorProperty).IsExpression;
        var color = login ? Color.FromRgb(0xF5, 0xC2, 0x5A) : !current ? Colors.White : usage >= 90
            ? Color.FromRgb(0xFF, 0x6B, 0x5B) : claude ? Color.FromRgb(0xE8, 0x95, 0x5F) : Color.FromRgb(0x2C, 0xC7, 0x9E);
        if (parameter as string == "Glow") return dark && !hc && (current || login)
            ? new DropShadowEffect { Color = color, ShadowDepth = 0, BlurRadius = 29, Opacity = .27 }
            : new DropShadowEffect { Opacity = 0 };
        if (hc) return System.Windows.SystemColors.WindowTextBrush;
        if (parameter as string == "Divider")
        {
            if (!dark) return Application.Current.TryFindResource("SecondaryBorderBrush") ?? Brushes.Gray;
            var line = new LinearGradientBrush();
            line.GradientStops.Add(new(Colors.Transparent, 0));
            line.GradientStops.Add(new(Color.FromArgb(115, color.R, color.G, color.B), .5));
            line.GradientStops.Add(new(Colors.Transparent, 1));
            return line;
        }
        return new SolidColorBrush(!dark ? Color.FromRgb(0xD6, 0xD6, 0xD6) : Color.FromArgb((byte)(current || login ? 107 : 31), color.R, color.G, color.B));
    }
    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
