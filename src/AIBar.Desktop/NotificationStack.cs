using System.Windows;
using AIBar.Domain;
using Forms = System.Windows.Forms;

namespace AIBar.Desktop;

/// <summary>UI-thread owned, bounded notification lifetime. Disposal never waits for a dispatcher.</summary>
public sealed class NotificationStack(Action showPanel, Func<bool>? isNarrow = null) : INotificationDelivery
{
    private readonly List<NotificationWindow> _windows = [];
    private bool _disposed;
    private Forms.Screen? _screen;
    public double BackgroundOpacity { get; set; } = 1;
    public bool TryShow(QuotaAlertKind kind, string provider, string title, string body)
    {
        if (_disposed) return false;
        NotificationWindow? window = null;
        try
        {
            _screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            window = new NotificationWindow(kind, provider, title, body, showPanel, backgroundOpacity: BackgroundOpacity);
            window.SourceInitialized += (_, _) => Arrange();
            window.SizeChanged += (_, _) => Arrange();
            window.Closed += (_, _) => { _windows.Remove(window); Arrange(); };
            if (_windows.Count == NotificationLayout.MaximumVisible) _windows[0].Close();
            _windows.Add(window);
            window.Show();
            Arrange();
            return true;
        }
        catch
        {
            if (window is not null)
            {
                _windows.Remove(window);
                try { window.Close(); } catch { /* Failed native creation must still reach the tray fallback. */ }
            }
            return false;
        }
    }
    private void Arrange()
    {
        if (_windows.Count == 0 || _screen is not { } screen) return;
        // Screen coordinates are physical; convert using the destination monitor's DPI, not the panel's.
        var dpi = NotificationNative.MonitorScale(screen);
        var area = screen.WorkingArea;
        var workArea = new Rect(area.X / dpi.X, area.Y / dpi.Y, area.Width / dpi.X, area.Height / dpi.Y);
        var windows = _windows.AsEnumerable().Reverse().ToArray();
        var positions = NotificationLayout.Place(workArea, windows.Select(w => Math.Max(86, w.ActualHeight - 80)).ToArray(), isNarrow?.Invoke() == true);
        for (var i = 0; i < windows.Length; i++)
        {
            // Forty transparent DIPs around the card keep the dark glow from being clipped.
            windows[i].Width = positions[i].Width + 80;
            NotificationNative.PlaceCard(windows[i], (positions[i].Left - 40) * dpi.X, (positions[i].Top - 40) * dpi.Y);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows.ToArray()) window.Close();
        _windows.Clear();
    }
}
