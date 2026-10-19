namespace AIBar.Domain;

/// <summary>Constant rate between the first and last distinct retrievals in this reset cycle.</summary>
public static class ObservedPacePolicy
{
    public static QuotaPaceResult Calculate(decimal? used, DateTimeOffset? resetAt, QuotaPaceWindow window,
        DateTimeOffset now, QuotaHistoryProvider provider, IReadOnlyList<QuotaHistorySample> samples)
    {
        var fallback = QuotaPacePolicy.Calculate(used, resetAt, window, now);
        if (fallback.State != QuotaPaceState.Available || fallback.IsLimitReached || fallback.ElapsedFraction <= 0.05m)
            return fallback;
        var duration = window == QuotaPaceWindow.FiveHour ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
        var key = window == QuotaPaceWindow.FiveHour ? QuotaHistoryWindow.FiveHour : QuotaHistoryWindow.Weekly;
        var start = resetAt!.Value - duration;
        var cycle = samples.Where(sample => sample.Provider == provider && sample.Window == key && sample.ResetAt == resetAt &&
                sample.RetrievedAt >= start && sample.RetrievedAt <= now && sample.Percentage is >= 0 and <= 100)
            .OrderBy(sample => sample.RetrievedAt).DistinctBy(sample => sample.RetrievedAt).ToArray();
        if (cycle.Length < 2 || cycle[^1].Percentage > used!.Value ||
            cycle.Zip(cycle.Skip(1), (a, b) => b.Percentage < a.Percentage).Any(decreased => decreased))
            return fallback;
        var ticks = (cycle[^1].RetrievedAt - cycle[0].RetrievedAt).Ticks;
        var delta = cycle[^1].Percentage - cycle[0].Percentage;
        if (ticks <= 0) return fallback;
        var ratio = delta * duration.Ticks / (100m * ticks);
        DateTimeOffset? exhaustion = null;
        if (delta > 0)
        {
            var remaining = (100m - used!.Value) * ticks;
            // Compare before division: tiny positive deltas can overflow even decimal ticks.
            if (remaining <= delta * (DateTimeOffset.MaxValue - now).Ticks)
                exhaustion = now.AddTicks((long)(remaining / delta));
        }
        return fallback with { PaceRatio = ratio, RunsOutAt = exhaustion,
            RunsOutBeforeReset = exhaustion < resetAt, IsObserved = true };
    }
}
