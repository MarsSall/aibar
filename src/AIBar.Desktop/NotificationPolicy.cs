using System.Windows;
using AIBar.Domain;

namespace AIBar.Desktop;

public sealed record NotificationCategory(string Label, string Icon, string Token)
{
    public static NotificationCategory For(QuotaAlertKind kind) => kind switch
    {
        QuotaAlertKind.Reset => new("RESET", "✓", "NotificationResetBrush"),
        QuotaAlertKind.ThresholdCrossed => new("THRESHOLD", "△", "NotificationThresholdBrush"),
        _ => new("PACE", "◷", "NotificationPaceBrush")
    };
}

public static class NotificationLayout
{
    public const int MaximumVisible = 3;
    public static IReadOnlyList<Rect> Place(Rect workArea, IReadOnlyList<double> heights, bool narrow = false)
    {
        var width = narrow || workArea.Width < 384 ? 300 : 360;
        var bottom = workArea.Bottom - 12;
        var result = new List<Rect>();
        foreach (var height in heights.Take(MaximumVisible))
        {
            var rect = new Rect(workArea.Right - 12 - width, bottom - height, width, height);
            result.Add(rect); bottom = rect.Top - 8;
        }
        return result;
    }
}

/// <summary>Elapsed time, not timer ticks; hovering or keyboard focus pauses the six second dwell.</summary>
public sealed class NotificationDwell
{
    public TimeSpan Remaining { get; private set; } = TimeSpan.FromSeconds(6);
    public bool Expired => Remaining == TimeSpan.Zero;
    public double Fraction => Remaining.TotalSeconds / 6;
    public void Advance(TimeSpan elapsed, bool hovered, bool focused)
    {
        if (hovered || focused || elapsed <= TimeSpan.Zero) return;
        Remaining = elapsed >= Remaining ? TimeSpan.Zero : Remaining - elapsed;
    }
}

public interface INotificationDelivery : IDisposable
{
    bool TryShow(QuotaAlertKind kind, string provider, string title, string body);
}
