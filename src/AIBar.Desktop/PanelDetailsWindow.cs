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
    private readonly System.ComponentModel.DependencyPropertyDescriptor _panelBackground = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(BackgroundProperty, typeof(MainWindow))!;
    internal ScrollViewer DetailsScrollViewer { get; }
    internal bool IsTranslucent { get; set; }
    internal System.Windows.Media.Color? OpaqueCompositionColor { get; set; }

    internal PanelDetailsWindow(MainWindow panel, FrameworkElement detail)
    {
        _panel = panel;
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
        var surface = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = DetailsScrollViewer };
        surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { Source = panel });
        var border = new MultiBinding { Converter = new PanelWindowDecorationConverter() };
        foreach (var path in new[] { "Tag", "SelectedProvider", "CodexUsage", "ClaudeUsage", "DataContext.State.IsCurrent", "DataContext.Claude.State.IsCurrent", "DataContext.State.NeedsLogin", "DataContext.Claude.State.NeedsLogin", "ProviderTint" })
            border.Bindings.Add(new Binding(path) { Source = panel });
        surface.SetBinding(Border.BorderBrushProperty, border);
        Content = surface;
        SourceInitialized += (_, _) => ApplySurface();
        _panelBackground.AddValueChanged(panel, OnPanelSurfaceChanged);
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
            _panelBackground.RemoveValueChanged(panel, OnPanelSurfaceChanged);
            panel.LocationChanged -= OnPanelPlacementChanged;
            panel.SizeChanged -= OnPanelPlacementChanged;
            DetailsScrollViewer.Content = null;
        };
    }

    private void OnPanelPlacementChanged(object? sender, EventArgs e) { if (IsVisible) Place(); }
    private void OnPanelSurfaceChanged(object? sender, EventArgs e)
    {
        if (IsVisible) ApplySurface();
    }

    private void ApplySurface()
    {
        var surface = _panel.Tag as System.Windows.Media.SolidColorBrush;
        var highContrast = SystemParameters.HighContrast || surface is not null &&
            DependencyPropertyHelper.GetValueSource(surface, System.Windows.Media.SolidColorBrush.ColorProperty).IsExpression;
        var theme = highContrast ? WindowsTheme.HighContrast
            : surface is not null && PanelPalette.IsDark(surface) ? WindowsTheme.Dark : WindowsTheme.Light;
        PopupSurface.Apply(new WpfOpaquePopupSurface(this), theme, new WindowsDwmSurfaceHints());
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
