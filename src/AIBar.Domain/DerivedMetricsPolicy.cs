namespace AIBar.Domain;

public enum DerivedMetricState { Available, InsufficientData, Unavailable, Unsupported }

public sealed record TrendMetricsResult(
    DerivedMetricState State,
    string WindowName,
    string TimeBasis,
    decimal? AverageDailyTokens,
    long? DayOverDayTokenChange,
    IReadOnlyList<DailyUsage> TokenFacts);

public sealed class NamedTrendPolicy(IClock clock, ILocalTimePolicy localTime)
{
    public const string SevenCompleteLocalDays = "7 complete local days";
    public const string LocalCalendarDays = "local calendar days";

    public TrendMetricsResult Calculate(IEnumerable<DailyUsage> usage)
    {
        var today = DateOnly.FromDateTime(localTime.ToLocal(clock.UtcNow).Date);
        var days = Enumerable.Range(1, 7).Select(offset => today.AddDays(-offset)).Reverse().ToArray();
        var facts = usage.Where(item => days.Contains(item.LocalDay)).ToArray();
        var totals = facts.GroupBy(item => item.LocalDay).ToDictionary(group => group.Key, group => group.Sum(item => item.Tokens.Total));
        if (days.Any(day => !totals.ContainsKey(day)))
            return new(DerivedMetricState.InsufficientData, SevenCompleteLocalDays, LocalCalendarDays, null, null, facts);

        var dailyTotals = days.Select(day => totals[day]).ToArray();
        return new(DerivedMetricState.Available, SevenCompleteLocalDays, LocalCalendarDays,
            dailyTotals.Average(value => (decimal)value), dailyTotals[^1] - dailyTotals[^2], facts);
    }
}

public sealed record QuotaUsageObservation(DateTimeOffset ObservedAt, decimal PercentageUsed, DateTimeOffset ResetAt);

public sealed record ExhaustionEtaResult(
    DerivedMetricState State, string WindowName, string RateBasis, string Label,
    decimal? ObservedRatePerHour, decimal? RemainingPercentage, DateTimeOffset? EstimatedExhaustionAt,
    QuotaSnapshot ServiceQuota, IReadOnlyList<QuotaUsageObservation> Observations);

public sealed class LinearExhaustionEtaPolicy(IClock clock, FreshnessPolicy freshness)
{
    public const string CurrentPrimaryServiceWindow = "current primary service window";
    public const string ObservedServiceQuotaPercentagePerHour = "observed service quota percentage per hour";
    public const string SimpleLinearEstimate = "simple linear estimate";

    public ExhaustionEtaResult Calculate(QuotaSnapshot serviceQuota, params QuotaUsageObservation[] observations) =>
        Calculate(serviceQuota, (IEnumerable<QuotaUsageObservation>)observations);

    public ExhaustionEtaResult Calculate(QuotaSnapshot serviceQuota, IEnumerable<QuotaUsageObservation> observations)
    {
        var facts = observations.OrderBy(item => item.ObservedAt).ToArray();
        if (facts.Length < 2) return Result(DerivedMetricState.Unsupported, null, null, null, serviceQuota, facts);

        var now = clock.UtcNow;
        var window = serviceQuota.Primary;
        if (!HasValidCurrentWindow(serviceQuota, facts, now))
            return Result(DerivedMetricState.Unavailable, null, null, null, serviceQuota, facts);

        var first = facts[^2];
        var last = facts[^1];
        var elapsed = last.ObservedAt - first.ObservedAt;
        var rate = elapsed > TimeSpan.Zero ? (last.PercentageUsed - first.PercentageUsed) / (decimal)elapsed.TotalHours : 0;
        var remaining = 100m - window.PercentageUsed;
        if (rate <= 0 || remaining < 0) return Result(DerivedMetricState.Unavailable, null, null, null, serviceQuota, facts);

        var eta = now.AddHours((double)(remaining / rate));
        return eta > window.ResetAt
            ? Result(DerivedMetricState.Unavailable, rate, remaining, null, serviceQuota, facts)
            : Result(DerivedMetricState.Available, rate, remaining, eta, serviceQuota, facts);
    }

    private bool HasValidCurrentWindow(QuotaSnapshot quota, IReadOnlyList<QuotaUsageObservation> observations, DateTimeOffset now) =>
        freshness.Evaluate(quota, now) == FreshnessState.Current && quota.Primary.ResetAt > now &&
        observations.All(item => item.ResetAt == quota.Primary.ResetAt && item.ObservedAt <= now &&
            item.PercentageUsed is >= 0 and <= 100);

    private static ExhaustionEtaResult Result(DerivedMetricState state, decimal? rate, decimal? remaining, DateTimeOffset? eta, QuotaSnapshot quota, IReadOnlyList<QuotaUsageObservation> observations) =>
        new(state, CurrentPrimaryServiceWindow, ObservedServiceQuotaPercentagePerHour, state == DerivedMetricState.Available ? SimpleLinearEstimate : "unavailable", rate, remaining, eta, quota, observations);
}
