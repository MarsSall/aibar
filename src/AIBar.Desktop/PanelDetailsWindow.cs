using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using Binding = System.Windows.Data.Binding;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace AIBar.Desktop;

internal sealed class PanelDetailsWindow : Window
{
    private readonly MainWindow _panel;
    private readonly IWindowsThemeSource _themeSource;
    private readonly WindowsThemeController _theme;
    internal Border DetailsSurface { get; }
    internal ScrollViewer DetailsScrollViewer { get; }
    internal bool IsTranslucent { get; set; }
    internal System.Windows.Media.Color? OpaqueCompositionColor { get; set; }

    internal PanelDetailsWindow(MainWindow panel, FrameworkElement detail, IWindowsThemeSource? themeSource = null)
    {
        _panel = panel;
        _themeSource = themeSource ?? new WindowsThemeSource();
        Owner = panel;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        SetBinding(WidthProperty, new Binding(nameof(ActualWidth)) { Source = panel });
        SetBinding(TopmostProperty, new Binding(nameof(Topmost)) { Source = panel });
        SetBinding(DataContextProperty, new Binding(nameof(DataContext)) { Source = panel });
        // Only the rounded border paints the surface, as in the panel; a window background would double its alpha.
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        SetBinding(ForegroundProperty, new Binding(nameof(Foreground)) { Source = panel });
        SetBinding(FontFamilyProperty, new Binding(nameof(FontFamily)) { Source = panel });
        SetBinding(FontSizeProperty, new Binding(nameof(FontSize)) { Source = panel });
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        // Keep the MainWindow's local styles available after re-hosting.
        Resources.MergedDictionaries.Add(panel.Resources);
        DetailsScrollViewer = new ScrollViewer
        {
            Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(16, 6, 16, 6)
        };
        DetailsScrollViewer.Resources.Add(typeof(ScrollBar), panel.FindResource("PanelScrollBarStyle"));
        DetailsScrollViewer.PreviewGotKeyboardFocus += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement focused) focused.BringIntoView();
        };
        DetailsSurface = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = DetailsScrollViewer, Margin = new Thickness(16) };
        Content = DetailsSurface;
        _theme = new WindowsThemeController(Resources, _themeSource, Dispatcher);
        _themeSource.Changed += OnThemeChanged;
        ApplySurface();
        panel.LocationChanged += OnPanelPlacementChanged;
        panel.SizeChanged += OnPanelPlacementChanged;
        SizeChanged += (_, _) => Place();
        PreviewKeyDown += (_, e) =>
        {
            // Left stays with the chart range selector, which uses it to move between segments.
            if (e.Key is not (Key.Escape or Key.Left) || e.Key == Key.Left && Keyboard.FocusedElement is ListBoxItem) return;
            panel.CloseDetails(restoreFocus: true);
            e.Handled = true;
        };
        Deactivated += (_, _) => panel.CheckDetailsActivation();
        MouseEnter += (_, _) => panel.CancelDetailsClose();
        MouseLeave += (_, _) => panel.ScheduleDetailsClose();
        Closed += (_, _) =>
        {
            _themeSource.Changed -= OnThemeChanged;
            _theme.Dispose();
            panel.LocationChanged -= OnPanelPlacementChanged;
            panel.SizeChanged -= OnPanelPlacementChanged;
            DetailsScrollViewer.Content = null;
        };
    }

    private void OnPanelPlacementChanged(object? sender, EventArgs e) { if (IsVisible) Place(); }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(ApplySurface));
    }

    /// <summary>Settings surface and border, but the glow follows the selected provider like the panel's (S8, L15).</summary>
    private void ApplySurface()
    {
        SettingsDecoration.Apply(DetailsSurface, _themeSource.Current);
        var glow = new MultiBinding { Converter = new PanelWindowDecorationConverter(), ConverterParameter = "Glow" };
        foreach (var path in new[] { "Tag", "SelectedProvider", "CodexUsage", "ClaudeUsage", "DataContext.State.IsCurrent", "DataContext.Claude.State.IsCurrent", "DataContext.State.NeedsLogin", "DataContext.Claude.State.NeedsLogin", "ProviderTint" })
            glow.Bindings.Add(new Binding(path) { Source = _panel });
        DetailsSurface.SetBinding(Border.EffectProperty, glow);
    }

    internal void Place()
    {
        var handle = new WindowInteropHelper(_panel).Handle;
        var pixels = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        var transform = PresentationSource.FromVisual(_panel)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var origin = transform.Transform(new Point(pixels.Left, pixels.Top));
        var extent = transform.Transform(new Point(pixels.Right, pixels.Bottom));
        var workArea = new Rect(origin, extent);
        MaxHeight = Math.Min(640, workArea.Height);
        var cost = (FrameworkElement)_panel.FindName("CostButton");
        var costTop = _panel.Top + cost.TranslatePoint(new Point(), _panel).Y;
        var position = PanelDetailsPlacement.Place(new Rect(_panel.Left, _panel.Top, _panel.ActualWidth, _panel.ActualHeight),
            workArea, new Size(Width, Math.Min(ActualHeight, MaxHeight)), costTop);
        Left = position.X;
        Top = position.Y;
    }
}
