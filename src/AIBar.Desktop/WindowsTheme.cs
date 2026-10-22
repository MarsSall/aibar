using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AIBar.Desktop;

public enum WindowsTheme { Light, Dark, HighContrast }

public interface IWindowsThemeSource : IDisposable
{
    event EventHandler? Changed;
    WindowsTheme Current { get; }
}

public interface IThemeController : IDisposable
{
    void Reevaluate();
}

public sealed class WindowsThemeSource : IWindowsThemeSource
{
    public WindowsThemeSource()
    {
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? Changed;
    public WindowsTheme Current
    {
        get
        {
            var highContrast = SystemParameters.HighContrast;
            return Resolve(highContrast, () => Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("AppsUseLightTheme"));
        }
    }

    internal static WindowsTheme Resolve(bool highContrast, object? appsUseLightTheme) => Resolve(highContrast, () => appsUseLightTheme);
    internal static WindowsTheme Resolve(bool highContrast, Func<object?> appsUseLightTheme)
    {
        if (highContrast) return WindowsTheme.HighContrast;
        try { return appsUseLightTheme() is 0 ? WindowsTheme.Dark : WindowsTheme.Light; }
        catch { return WindowsTheme.Light; }
    }

    private void OnSystemParameterChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs args) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}

public sealed class WindowsThemeController : IThemeController
{
    private readonly ResourceDictionary _resources;
    private readonly IWindowsThemeSource _source;
    private readonly Dispatcher _dispatcher;
    private readonly Func<WindowsTheme, ResourceDictionary> _createDictionary;
    private ResourceDictionary? _applied;
    private WindowsTheme? _appliedTheme;
    private int _queued;
    private bool _disposed;

    public WindowsThemeController(ResourceDictionary resources, IWindowsThemeSource source, Dispatcher dispatcher, Func<WindowsTheme, ResourceDictionary>? createDictionary = null)
    {
        _resources = resources; _source = source; _dispatcher = dispatcher;
        _createDictionary = createDictionary ?? ThemeDictionaries.Create;
        _applied = resources.MergedDictionaries.LastOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Semantic.", StringComparison.Ordinal) == true);
        _source.Changed += OnThemeChanged;
        Reevaluate();
    }

    public void Reevaluate()
    {
        if (_disposed) return;
        if (_dispatcher.CheckAccess()) ApplyCurrentTheme();
        else if (Interlocked.Exchange(ref _queued, 1) == 0) _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ApplyCurrentTheme));
    }

    private void OnThemeChanged(object? sender, EventArgs args) => Reevaluate();
    private void ApplyCurrentTheme()
    {
        Interlocked.Exchange(ref _queued, 0);
        if (_disposed) return;
        var theme = _source.Current;
        if (_appliedTheme == theme) return;
        ResourceDictionary next;
        try { next = _createDictionary(theme); }
        catch { return; }

        var previous = _applied;
        try
        {
            if (previous is not null) _resources.MergedDictionaries.Remove(previous);
            _resources.MergedDictionaries.Add(next);
            _applied = next;
            _appliedTheme = theme;
        }
        catch
        {
            _resources.MergedDictionaries.Remove(next);
            if (previous is not null && !_resources.MergedDictionaries.Contains(previous)) _resources.MergedDictionaries.Add(previous);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.Changed -= OnThemeChanged;
        _source.Dispose();
    }
}

public static class ThemeDictionaries
{
    public static ResourceDictionary Create(WindowsTheme theme) => new()
    {
        Source = new Uri($"pack://application:,,,/AIBar.Desktop;component/Themes/Semantic.{theme}.xaml", UriKind.Absolute)
    };
}

public interface IOpaquePopupSurface
{
    void UseOpaqueBase();
    void UseTranslucentBase();
    void SetDarkMode();
    void SetCorners();
    void SetBackdrop();
}

public interface IDwmSurfaceHints
{
    bool SupportsHints { get; }
    bool IsRemoteSession { get; }
    bool IsTransparencyEnabled { get; }
    void ApplyDarkMode(IOpaquePopupSurface surface);
    void ApplyCorners(IOpaquePopupSurface surface);
    void ApplyBackdrop(IOpaquePopupSurface surface);
}

public static class PopupSurface
{
    /// <summary>Returns true only when the acrylic backdrop is active and the panel may use a translucent tint.</summary>
    public static bool Apply(IOpaquePopupSurface surface, WindowsTheme theme, IDwmSurfaceHints hints)
    {
        surface.UseOpaqueBase();
        if (theme == WindowsTheme.HighContrast) return false;
        if (!hints.SupportsHints) return false;
        if (hints.IsRemoteSession) return false;
        if (theme == WindowsTheme.Dark) Try(() => hints.ApplyDarkMode(surface));
        Try(() => hints.ApplyCorners(surface));
        // Windows "Transparency effects" off means the user asked for solid surfaces.
        if (!hints.IsTransparencyEnabled) return false;
        if (!Try(() => hints.ApplyBackdrop(surface))) return false;
        surface.UseTranslucentBase();
        return true;
    }

    private static bool Try(Action apply)
    {
        try { apply(); return true; }
        catch { return false; }
    }
}

public sealed class WindowsDwmSurfaceHints : IDwmSurfaceHints
{
    public bool SupportsHints => OperatingSystem.IsWindows();
    public bool IsRemoteSession => GetSystemMetrics(0x1000) != 0;
    public bool IsTransparencyEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return WindowsTransparencySetting.IsEnabled(key?.GetValue("EnableTransparency"));
            }
            catch { return false; }
        }
    }
    public void ApplyDarkMode(IOpaquePopupSurface surface) => surface.SetDarkMode();
    public void ApplyCorners(IOpaquePopupSurface surface) => surface.SetCorners();
    public void ApplyBackdrop(IOpaquePopupSurface surface) => surface.SetBackdrop();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}

public static class WindowsTransparencySetting
{
    /// <summary>EnableTransparency is a DWORD; a missing value means the Windows default (on).</summary>
    public static bool IsEnabled(object? registryValue) => registryValue is not int value || value != 0;
}

public sealed class WpfOpaquePopupSurface(Window window) : IOpaquePopupSurface
{
    private const int SystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropTransientAcrylic = 3;

    public void UseOpaqueBase()
    {
        if (window.AllowsTransparency) window.AllowsTransparency = false;
        window.Opacity = 1;
        var translucent = window is MainWindow { IsTranslucent: true } or PanelDetailsWindow { IsTranslucent: true };
        if (!translucent) return;
        // Undo the acrylic composition so an opaque panel never shows the DWM frame.
        var opaqueColor = window is MainWindow panel ? panel.OpaqueCompositionColor : ((PanelDetailsWindow)window).OpaqueCompositionColor;
        if (window is MainWindow main) main.IsTranslucent = false;
        if (window is PanelDetailsWindow details) details.IsTranslucent = false;
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var none = BackdropNone;
            DwmSetWindowAttribute(handle, SystemBackdropType, ref none, sizeof(int));
            var margins = new Margins();
            DwmExtendFrameIntoClientArea(handle, ref margins);
            if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target && opaqueColor is { } color)
                target.BackgroundColor = color;
        }
        catch { }
    }

    public void UseTranslucentBase()
    {
        if (window is MainWindow panel) panel.IsTranslucent = true;
        if (window is PanelDetailsWindow details) details.IsTranslucent = true;
    }

    public void SetDarkMode() => Set(20, 1);
    /// <summary>Rounded corners without the Windows 11 system border, which drew a second frame outside the panel's own.</summary>
    public void SetCorners()
    {
        Set(33, 2);
        var none = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE; unsupported before Windows 11, so failure is ignored.
        DwmSetWindowAttribute(new WindowInteropHelper(window).EnsureHandle(), 34, ref none, sizeof(int));
    }

    /// <summary>Acrylic first; the frame and transparent composition change only after DWM accepts it.</summary>
    public void SetBackdrop()
    {
        if (window is PanelDetailsWindow { Owner: MainWindow { IsTranslucent: false } }) throw new InvalidOperationException();
        Set(SystemBackdropType, BackdropTransientAcrylic);
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(handle, ref margins) != 0) throw new InvalidOperationException();
        var target = HwndSource.FromHwnd(handle)?.CompositionTarget ?? throw new InvalidOperationException();
        if (window is MainWindow panel) panel.OpaqueCompositionColor ??= target.BackgroundColor;
        if (window is PanelDetailsWindow details) details.OpaqueCompositionColor ??= target.BackgroundColor;
        target.BackgroundColor = System.Windows.Media.Colors.Transparent;
    }

    private void Set(int attribute, int value)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) != 0) throw new InvalidOperationException();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);
}
