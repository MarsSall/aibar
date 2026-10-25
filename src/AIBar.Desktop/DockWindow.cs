using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AIBar.Application;
using Forms = System.Windows.Forms;
using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace AIBar.Desktop;

/// <summary>
/// UI-thread owned. Construct with BetaAnalyticsPresentation and call Apply(loadedOptions) to show/hide.
/// Marshal DockSettings.Changed to this dispatcher before calling Apply. Close on application exit.
/// No provider access or polling; nested WPF bindings observe both presentation hosts.
/// </summary>
public sealed class DockWindow : Window
{
    private readonly StackPanel _strip = new();
    private readonly Action _openPanel;
    private readonly Func<DockOptions, CancellationToken, ValueTask> _persist;
    private readonly WindowsThemeController _theme;
    private DockOptions _options;
    private Point? _down;
    private Point _origin;
    private Vector _dragScale;
    private bool _dragged, _saving, _closed;
    private HwndSource? _source;
    private readonly List<DockRing> _rings = [];
    public IReadOnlyList<DockRing> Rings => _rings.AsReadOnly();
    public Exception? LastPersistenceError { get; private set; }

    // Nested bindings matter: BetaAnalyticsPresentation does not forward Claude.PropertyChanged.
    private static readonly DependencyProperty CodexStateProperty = Observe("CodexState", typeof(BetaPresentationState));
    private static readonly DependencyProperty ClaudeStateProperty = Observe("ClaudeState", typeof(BetaPresentationState));
    private static readonly DependencyProperty ClaudeEnabledProperty = Observe("ClaudeEnabled", typeof(bool), false);
    private static readonly DependencyProperty ClaudePlanProperty = Observe("ClaudePlan", typeof(string));
    private static DependencyProperty Observe(string name, Type type, object? initial = null) =>
        DependencyProperty.Register(name, type, typeof(DockWindow), new PropertyMetadata(initial, (owner, _) => ((DockWindow)owner).UpdateRings()));

    public DockWindow(object presentation, DockOptions options, Action openPanel, Func<DockOptions, CancellationToken, ValueTask> persist)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(openPanel);
        ArgumentNullException.ThrowIfNull(persist);
        if (!DockSettings.IsValid(options)) throw new ArgumentException("Invalid dock options.", nameof(options));
        _options = options; _openPanel = openPanel; _persist = persist;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Title = "AIBar floating quota widget";
        AutomationProperties.SetName(this, Title);
        var themeSource = new WindowsThemeSource();
        _theme = new WindowsThemeController(Resources, themeSource, Dispatcher);
        var surface = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1), Child = _strip };
        surface.SetResourceReference(Border.BackgroundProperty, "SettingsSurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "SettingsBorderBrush");
        // Keep content fully opaque; only the theme surface is translucent. High contrast stays opaque.
        void UpdateSurface()
        {
            var brush = ((System.Windows.Media.Brush)FindResource("SettingsSurfaceBrush")).CloneCurrentValue();
            if (themeSource.Current != WindowsTheme.HighContrast && new WindowsDwmSurfaceHints().IsTransparencyEnabled) brush.Opacity = .9;
            surface.Background = brush;
        }
        themeSource.Changed += OnThemeChanged;
        void OnThemeChanged(object? sender, EventArgs args)
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(() =>
            { if (!_closed) { _theme.Reevaluate(); UpdateSurface(); UpdateRings(); } }));
        }
        UpdateSurface();
        Content = surface;
        DataContext = presentation;
        Bind(CodexStateProperty, "State"); Bind(ClaudeStateProperty, "Claude.State");
        Bind(ClaudeEnabledProperty, "Claude.IsEnabled", false); Bind(ClaudePlanProperty, "Claude.PlanLabel");
        UpdateRings();
        SourceInitialized += (_, _) =>
        {
            NotificationNative.Initialize(this, false);
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WindowMessage);
            PlaceSaved();
        };
        MouseLeftButtonDown += (_, e) =>
        {
            if (_saving) return;
            BeginPointer(CursorPoint()); CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, _) => { if (_down is not null) MovePointer(CursorPoint()); };
        MouseLeftButtonUp += async (_, e) =>
        {
            if (_down is null) return;
            var point = CursorPoint();
            // Clear pointer state before releasing capture (LostMouseCapture fires synchronously).
            var completion = EndPointerAsync(point);
            ReleaseMouseCapture(); e.Handled = true;
            await completion;
        };
        LostMouseCapture += (_, _) => { if (_down is not null) { _down = null; _dragged = false; PlaceSaved(); } };
        Closed += (_, _) =>
        {
            _closed = true; _source?.RemoveHook(WindowMessage);
            themeSource.Changed -= OnThemeChanged; _theme.Dispose();
            BindingOperations.ClearAllBindings(this); DataContext = null;
        };
        void Bind(DependencyProperty property, string path, object? fallback = null) => BindingOperations.SetBinding(this, property,
            new Binding(path) { Mode = BindingMode.OneWay, FallbackValue = fallback, TargetNullValue = fallback });
    }

    /// <summary>UI thread only. Enabled=false hides without destroying the instance; Close releases subscriptions.</summary>
    public void Apply(DockOptions options)
    {
        Dispatcher.VerifyAccess();
        if (_closed) return;
        if (!DockSettings.IsValid(options)) throw new ArgumentException("Invalid dock options.", nameof(options));
        _options = options;
        if (!options.Enabled)
        {
            _down = null; ReleaseMouseCapture(); Hide(); return;
        }
        PlaceSaved();
        if (!IsVisible) Show();
        PlaceSaved();
    }

    private void UpdateRings()
    {
        if (_closed) return;
        var codex = GetValue(CodexStateProperty) as BetaPresentationState;
        var claude = GetValue(ClaudeStateProperty) as BetaPresentationState;
        var providers = new List<(string Name, BetaPresentationState? State, string? Plan)>();
        if (codex is { IsPrivateIntegrationDisabled: false }) providers.Add(("Codex", codex, null));
        if (GetValue(ClaudeEnabledProperty) is true) providers.Add(("Claude", claude, GetValue(ClaudePlanProperty) as string));
        if (providers.Count == 0) providers.Add(("AIBar", null, null));
        // Preserve ring instances (and an open hover card) during countdown updates.
        if (!_rings.Select(r => r.Provider).SequenceEqual(providers.Select(p => p.Name)))
        {
            _strip.Children.Clear(); _rings.Clear();
            foreach (var _ in providers) { var ring = new DockRing(); _rings.Add(ring); _strip.Children.Add(ring); }
        }
        for (var i = 0; i < providers.Count; i++) _rings[i].Update(providers[i].Name, providers[i].State, providers[i].Plan);
        if (_down is null) PlaceSaved();
    }

    private void PlaceSaved()
    {
        if (_closed) return;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _options.Monitor)
            ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var scale = NotificationNative.MonitorScale(screen);
        var area = WorkArea(screen, scale);
        var rect = DockPlacement.Place(area, _options.Edge, _options.Offset, new Size(Math.Max(1, _rings.Count) * 56 + 8, 64));
        _strip.Orientation = _options.Edge is "left" or "right" ? Orientation.Vertical : Orientation.Horizontal;
        _strip.Margin = new Thickness(3);
        Width = rect.Width; Height = rect.Height;
        if (new WindowInteropHelper(this).Handle != IntPtr.Zero)
            NotificationNative.PlaceCard(this, rect.X * scale.X, rect.Y * scale.Y);
        else { Left = rect.X; Top = rect.Y; }
    }

    internal void BeginPointer(Point physicalPoint)
    {
        if (_saving || _closed) return;
        _down = physicalPoint; _dragged = false;
        _origin = IsVisible ? PointToScreen(new Point()) : new Point(Left, Top);
        _dragScale = NotificationNative.MonitorScale(ScreenAt(physicalPoint));
    }
    internal void MovePointer(Point physicalPoint)
    {
        if (_down is not { } down) return;
        var delta = physicalPoint - down;
        if (Math.Abs(delta.X) / _dragScale.X > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(delta.Y) / _dragScale.Y > SystemParameters.MinimumVerticalDragDistance) _dragged = true;
        if (_dragged) NotificationNative.PlaceCard(this, _origin.X + delta.X, _origin.Y + delta.Y);
    }
    internal async ValueTask EndPointerAsync(Point physicalPoint)
    {
        if (_down is null) return;
        MovePointer(physicalPoint);
        _down = null;
        if (!_dragged) { _openPanel(); return; }
        _dragged = false;
        var screen = ScreenAt(physicalPoint);
        var scale = NotificationNative.MonitorScale(screen);
        var snap = DockPlacement.Snap(WorkArea(screen, scale), new Point(physicalPoint.X / scale.X, physicalPoint.Y / scale.Y));
        var previous = _options;
        var next = previous with { Edge = snap.Edge, Offset = snap.Offset, Monitor = screen.DeviceName };
        _options = next; PlaceSaved(); _saving = true; LastPersistenceError = null;
        try { await _persist(next, CancellationToken.None); }
        catch (Exception exception)
        {
            LastPersistenceError = exception;
            if (_options == next) { _options = previous; if (!_closed) PlaceSaved(); }
            System.Diagnostics.Trace.TraceWarning("AIBar dock position save failed: {0}", exception.GetType().Name);
        }
        finally { _saving = false; }
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        // Display topology, work area/taskbar, or destination-monitor DPI changed.
        if (message is 0x007E or 0x001A or 0x02E0)
            Dispatcher.BeginInvoke(new Action(() => { if (!_closed && _down is null) PlaceSaved(); }));
        return IntPtr.Zero;
    }
    private static Point CursorPoint() { var p = Forms.Cursor.Position; return new Point(p.X, p.Y); }
    private static Forms.Screen ScreenAt(Point point) => Forms.Screen.FromPoint(new System.Drawing.Point((int)Math.Round(point.X), (int)Math.Round(point.Y)));
    private static Rect WorkArea(Forms.Screen screen, Vector scale)
    {
        var area = screen.WorkingArea;
        return new Rect(area.X / scale.X, area.Y / scale.Y, area.Width / scale.X, area.Height / scale.Y);
    }
}
