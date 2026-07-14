namespace AIBar.Domain;

public sealed record TokenObservation(DateTimeOffset EventUtc, string? Model, bool IsModelTrusted, TokenTotals Previous, TokenTotals Current);

public sealed record LocalDayAssignment(DateTimeOffset EventUtc, DateOnly LocalDay, string TimeZoneId, TimeSpan ObservedOffset, string PolicyVersion);

public enum AnalyticsRebuildDecision { None, RebuildRequired }

public sealed class TimeZoneLocalDayPolicy(TimeZoneInfo timeZone, string policyVersion)
{
    public string PolicyVersion => policyVersion;
    public LocalDayAssignment Assign(DateTimeOffset eventUtc)
    {
        var utc = eventUtc.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTime(utc, timeZone);
        return new LocalDayAssignment(utc, DateOnly.FromDateTime(local.DateTime), timeZone.Id, local.Offset, policyVersion);
    }
}

public sealed record AnalyticsAggregation(IReadOnlyList<DailyUsage> Usage, IReadOnlyList<LocalDayAssignment> Provenance);

public sealed class AnalyticsPolicy(TimeZoneLocalDayPolicy localDayPolicy)
{
    public const string UnknownModel = "Unknown";
    public string PolicyVersion => localDayPolicy.PolicyVersion;

    public AnalyticsAggregation Aggregate(IEnumerable<TokenObservation> observations)
    {
        var usage = new Dictionary<(DateOnly Day, string Zone, TimeSpan Offset, string Model), TokenTotals>();
        var provenance = new List<LocalDayAssignment>();
        foreach (var observation in observations)
        {
            var assignment = localDayPolicy.Assign(observation.EventUtc);
            provenance.Add(assignment);
            var key = (assignment.LocalDay, assignment.TimeZoneId, assignment.ObservedOffset,
                observation.IsModelTrusted && !string.IsNullOrWhiteSpace(observation.Model) ? observation.Model : UnknownModel);
            usage[key] = Add(usage.GetValueOrDefault(key, new TokenTotals(0, 0, 0)), TokenDelta.FromCumulative(observation.Previous, observation.Current));
        }
        return new AnalyticsAggregation(usage.Select(pair => new DailyUsage(pair.Key.Day, pair.Key.Zone, pair.Key.Offset, pair.Key.Model, pair.Value)).ToArray(), provenance);
    }

    public static string? MostUsedModel(IEnumerable<DailyUsage> usage) => usage
        .GroupBy(item => item.Model, StringComparer.Ordinal)
        .OrderByDescending(group => group.Sum(item => item.Tokens.Total))
        .ThenBy(group => group.Key, StringComparer.Ordinal)
        .Select(group => group.Key)
        .FirstOrDefault();

    public static AnalyticsRebuildDecision RebuildDecision(string persistedPolicyVersion, string activePolicyVersion) =>
        StringComparer.Ordinal.Equals(persistedPolicyVersion, activePolicyVersion) ? AnalyticsRebuildDecision.None : AnalyticsRebuildDecision.RebuildRequired;

    private static TokenTotals Add(TokenTotals left, TokenTotals right) => new(left.Input + right.Input, left.CachedInput + right.CachedInput, left.Output + right.Output);
}
