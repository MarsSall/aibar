using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Border = System.Windows.Controls.Border;
using Color = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;

namespace AIBar.Desktop;

/// <summary>Shared opaque Settings chrome, independent of provider tint and panel opacity.</summary>
internal static class SettingsDecoration
{
    internal static void Apply(Border surface, WindowsTheme theme)
    {
        surface.SetResourceReference(Border.BackgroundProperty, "SettingsSurfaceBrush");
        surface.BorderBrush = theme == WindowsTheme.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(
            theme == WindowsTheme.Dark ? Color.FromArgb(107, 110, 155, 255) : Color.FromRgb(214, 214, 214));
        surface.Effect = theme == WindowsTheme.Dark ? new DropShadowEffect
        {
            Color = Color.FromRgb(0x6E, 0x9B, 0xFF), ShadowDepth = 0, BlurRadius = 29, Opacity = .27
        } : null;
    }
}
