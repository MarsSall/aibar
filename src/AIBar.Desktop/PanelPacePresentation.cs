using System.Globalization;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Panel-only copy and tone. Shared countdowns and copy summary remain unchanged.</summary>
public sealed record PanelPacePresentation(
    string Label, string Text, decimal? Usage, decimal ExpectedPercentage,
    PanelTone Tone, string? Notice, bool IsVisible)
{
    public bool IsCurrent { get; init; }
    public string AutomationName => Label + " pace";
    public bool IsObserved { get; init; }
    public string HelpText => Label + ": " + Text + (IsObserved ? " · based on observed use" : "");

    public static PanelPacePresentation Create(string label, decimal? used, QuotaPaceResult pace, bool current, DateTimeOffset now)
    {
        var visible = pace.State != QuotaPaceState.Unavailable || (!current && used is not null);
        var difference = (pace.PaceRatio.GetValueOrDefault() - 1m) * 100m;
        // A small tolerance avoids calling a rounded 1.0x estimate Ahead or Behind.
        var direction = Math.Abs(difference) < 5m ? "On track"
            : difference > 0 ? "Ahead (+" + difference.ToString("0", CultureInfo.InvariantCulture) + "%)"
            : "Behind (−" + Math.Abs(difference).ToString("0", CultureInfo.InvariantCulture) + "%)";
        var text = !visible ? "" : !current ? "Pace: Not current" : pace.IsLimitReached ? "Pace: Limit reached"
            : pace.State == QuotaPaceState.NotEnoughData ? "Pace: Not enough data yet"
            : "Pace: " + direction + " · " +
                (pace.RunsOutBeforeReset && pace.RunsOutAt is { } end
                    ? "Runs out in " + Duration(end - now) : "Lasts to reset");
        var tone = current && !pace.IsLimitReached && pace.RunsOutBeforeReset ? PanelTone.Near : PanelTone.Muted;
        var notice = visible && current && pace.RunsOutBeforeReset
            ? $"At this pace, {label} runs out before it resets." : null;
        return new(label, text, used, pace.ExpectedPercentage, tone, notice, visible) { IsCurrent = current, IsObserved = current && pace.IsObserved };
    }

    public PanelPacePresentation ForProvider(string provider) => this with
    {
        Label = Label.Replace("Codex", provider, StringComparison.Ordinal),
        Notice = Notice?.Replace("Codex", provider, StringComparison.Ordinal)
    };

    public static string Duration(TimeSpan duration)
    {
        var value = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return value.TotalDays >= 1 ? FormattableString.Invariant($"{value.Days}d {value.Hours}h")
            : value.TotalHours >= 1 ? FormattableString.Invariant($"{value.Hours}h {value.Minutes}m")
            : value.TotalMinutes >= 1 ? FormattableString.Invariant($"{value.Minutes}m") : "<1m";
    }
}
