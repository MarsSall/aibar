using System.Globalization;

namespace AIBar.Domain;

public enum QuotaAlertKind { Reset, ThresholdCrossed, RunsOutBeforeReset }
public sealed record QuotaAlertSample(string Provider, QuotaPaceWindow Window, decimal PercentageUsed,
    DateTimeOffset ResetAt, DateTimeOffset RetrievedAt);
public sealed record QuotaAlert(string Key, QuotaAlertKind Kind, QuotaAlertSample Sample,
    int? Threshold = null, DateTimeOffset? RunsOutAt = null);

/// <summary>Pure cycle/crossing rules. The caller persists keys before delivering events.</summary>
public static class QuotaAlertPolicy
{
    public static bool IsValid(QuotaAlertSample sample, DateTimeOffset now) =>
        sample.Provider is "Codex" or "Claude" &&
        sample.Window is QuotaPaceWindow.FiveHour or QuotaPaceWindow.Weekly &&
        sample.PercentageUsed is >= 0 and <= 100 && sample.ResetAt > now && sample.RetrievedAt <= now;

    public static IReadOnlyList<QuotaAlert> Evaluate(QuotaAlertSample current, QuotaAlertSample? previous,
        IReadOnlyCollection<string> fired, FreshnessState freshness, bool consented, DateTimeOffset now)
    {
        if (!consented || freshness != FreshnessState.Current || !IsValid(current, now)) return [];
        if (previous is not null && (previous.Provider != current.Provider || previous.Window != current.Window)) previous = null;
        if (previous is not null && current.RetrievedAt <= previous.RetrievedAt) return [];
        var events = new List<QuotaAlert>();
        void Add(QuotaAlertKind kind, int? threshold = null, DateTimeOffset? runsOut = null)
        {
            var key = string.Join("|", current.Provider, current.Window,
                current.ResetAt.UtcTicks.ToString(CultureInfo.InvariantCulture), kind,
                threshold?.ToString(CultureInfo.InvariantCulture) ?? "");
            if (!fired.Contains(key)) events.Add(new(key, kind, current, threshold, runsOut));
        }
        if (previous is not null && current.ResetAt > previous.ResetAt) Add(QuotaAlertKind.Reset);
        foreach (var threshold in new[] { 75, 90, 100 })
            if (current.PercentageUsed >= threshold &&
                (previous is null || current.ResetAt != previous.ResetAt || previous.PercentageUsed < threshold))
                Add(QuotaAlertKind.ThresholdCrossed, threshold);
        var pace = QuotaPacePolicy.Calculate(current.PercentageUsed, current.ResetAt, current.Window, now);
        if (pace.RunsOutBeforeReset) Add(QuotaAlertKind.RunsOutBeforeReset, runsOut: pace.RunsOutAt);
        return events;
    }
}
