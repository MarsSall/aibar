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
    void SetDarkMode();
    void SetCorners();
    void SetBackdrop();
}

public interface IDwmSurfaceHints
{
    bool SupportsHints { get; }
    bool IsRemoteSession { get; }
    void ApplyDarkMode(IOpaquePopupSurface surface);
    void ApplyCorners(IOpaquePopupSurface surface);
    void ApplyBackdrop(IOpaquePopupSurface surface);
}

public static class PopupSurface
{
    public static void Apply(IOpaquePopupSurface surface, WindowsTheme theme, IDwmSurfaceHints hints)
    {
        surface.UseOpaqueBase();
        if (theme == WindowsTheme.HighContrast) return;
        if (!hints.SupportsHints) return;
        if (hints.IsRemoteSession) return;
        if (theme == WindowsTheme.Dark) Try(() => hints.ApplyDarkMode(surface));
        Try(() => hints.ApplyCorners(surface));
        Try(() => hints.ApplyBackdrop(surface));
    }

    private static void Try(Action apply) { try { apply(); } catch { } }
}

public sealed class WindowsDwmSurfaceHints : IDwmSurfaceHints
{
    public bool SupportsHints => OperatingSystem.IsWindows();
    public bool IsRemoteSession => GetSystemMetrics(0x1000) != 0;
    public void ApplyDarkMode(IOpaquePopupSurface surface) => surface.SetDarkMode();
    public void ApplyCorners(IOpaquePopupSurface surface) => surface.SetCorners();
    public void ApplyBackdrop(IOpaquePopupSurface surface) => surface.SetBackdrop();
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}

public sealed class WpfOpaquePopupSurface(Window window) : IOpaquePopupSurface
{
    public void UseOpaqueBase()
    {
        if (window.AllowsTransparency) window.AllowsTransparency = false;
        window.Opacity = 1;
    }
    public void SetDarkMode() => Set(20, 1);
    public void SetCorners() => Set(33, 2);
    public void SetBackdrop() => Set(38, 2);
    private void Set(int attribute, int value)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int)) != 0) throw new InvalidOperationException();
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
