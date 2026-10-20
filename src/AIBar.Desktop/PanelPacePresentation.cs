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
    public string HelpText => Label + ": " + Text;

    public static PanelPacePresentation Create(string label, decimal? used, QuotaPaceResult pace, bool current, DateTimeOffset now)
    {
        var visible = pace.State != QuotaPaceState.Unavailable || (!current && used is not null);
        var text = !visible ? "" : !current ? "Not current" : pace.IsLimitReached ? "Limit reached"
            : pace.State == QuotaPaceState.NotEnoughData ? "Not enough data yet"
            : "Pace " + pace.PaceRatio.GetValueOrDefault().ToString("0.0", CultureInfo.InvariantCulture) + "x · " +
                (pace.RunsOutBeforeReset && pace.RunsOutAt is { } end
                    ? "runs out in " + Duration(end - now) : "lasts until reset");
        var tone = !current ? PanelTone.Muted : pace.IsLimitReached ? PanelTone.Full
            : pace.RunsOutBeforeReset ? PanelTone.Near : PanelTone.Muted;
        var notice = visible && current && pace.RunsOutBeforeReset
            ? $"At this pace, {label} runs out before it resets." : null;
        return new(label, text, used, pace.ExpectedPercentage, tone, notice, visible) { IsCurrent = current };
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
