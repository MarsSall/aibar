namespace AIBar.Domain;

public enum QuotaPaceWindow { FiveHour, Weekly }
public enum QuotaPaceState { Available, NotEnoughData, Unavailable }

public sealed record QuotaPaceResult(
    QuotaPaceState State,
    decimal ElapsedFraction = 0,
    decimal ExpectedPercentage = 0,
    decimal? PaceRatio = null,
    DateTimeOffset? RunsOutAt = null,
    bool RunsOutBeforeReset = false,
    bool IsLimitReached = false)
{
    public bool IsObserved { get; init; }
}

/// <summary>A snapshot-only, constant-rate estimate, not a forecast from observation history.</summary>
public static class QuotaPacePolicy
{
    public static QuotaPaceResult Calculate(decimal? used, DateTimeOffset? resetAt, QuotaPaceWindow window, DateTimeOffset now)
    {
        if (used is null || resetAt is null || resetAt < now)
            return new(QuotaPaceState.Unavailable);
        var length = window switch
        {
            QuotaPaceWindow.FiveHour => TimeSpan.FromHours(5),
            QuotaPaceWindow.Weekly => TimeSpan.FromDays(7),
            _ => throw new ArgumentOutOfRangeException(nameof(window))
        };
        var elapsedTicks = Math.Clamp(length.Ticks - (resetAt.Value - now).Ticks, 0L, length.Ticks);
        var fraction = (decimal)elapsedTicks / length.Ticks;
        var expected = fraction * 100m;
        if (used >= 100)
            return new(QuotaPaceState.Available, fraction, expected,
                expected > 0 ? used / expected : null, now, now < resetAt, true);
        if (fraction < 0.05m)
            return new(QuotaPaceState.NotEnoughData, fraction, expected);
        var pace = used.Value / expected;
        DateTimeOffset? runsOut = null;
        if (pace > 1m)
        {
            // Decimal arithmetic preserves the even-use boundary; round only when displaying.
            var remainingTicks = (100m - used.Value) * elapsedTicks / used.Value;
            runsOut = now.AddTicks((long)remainingTicks);
        }
        return new(QuotaPaceState.Available, fraction, expected, pace, runsOut, runsOut < resetAt);
    }
}
