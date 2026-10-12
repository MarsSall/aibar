namespace AIBar.Domain;

/// <summary>Deterministic quota-projection-v1 using active-cycle, recency-weighted evidence.</summary>
public sealed class QuotaProjectionPolicy
{
    private const string Version = "quota-projection-v1";

    public QuotaProjectionResult Evaluate(QuotaProjectionRequest request)
    {
        var segment = CompatibleActiveSegment(request);
        var span = segment.Count < 2 ? TimeSpan.Zero : segment[^1].RetrievedAt - segment[0].RetrievedAt;
        var intervals = segment.Zip(segment.Skip(1), (first, last) => new Interval(first, last)).Where(item => item.Hours > 0).ToArray();
        var median = Median(intervals.Select(item => item.Rate));
        var mad = Median(intervals.Select(item => Math.Abs(item.Rate - median)));
        var kept = intervals.Where(item => Math.Abs(item.Rate - median) <= (mad == 0 ? .5m : 3m * mad)).ToArray();
        var excluded = intervals.Length - kept.Length;
        var evidence = new ProjectionEvidence(segment.Count, span, excluded, Version);
        if (request.EvaluatedAt >= request.Cycle.ResetAt)
            return Withheld(QuotaProjectionState.Learning, evidence);
        if (segment.Count < 6 || intervals.Length < 3 || excluded * 5 > intervals.Length)
            return Withheld(excluded > 0 ? QuotaProjectionState.Inconsistent : QuotaProjectionState.Learning, evidence);

        // A neighboring retained interval must not reintroduce rejected endpoints.
        var rejectedKeys = intervals.Except(kept)
            .SelectMany(item => new[] { item.First.SampleKey, item.Last.SampleKey }).ToHashSet();
        var retained = kept.SelectMany(item => new[] { item.First, item.Last })
            .Where(item => !rejectedKeys.Contains(item.SampleKey))
            .DistinctBy(item => item.SampleKey).OrderBy(item => item.RetrievedAt).ToArray();
        if (retained.Length < 6)
            return Withheld(excluded > 0 ? QuotaProjectionState.Inconsistent : QuotaProjectionState.Learning,
                evidence with { SampleCount = retained.Length });
        span = retained[^1].RetrievedAt - retained[0].RetrievedAt;
        evidence = evidence with { SampleCount = retained.Length, Span = span };
        var minimumSpan = request.Cycle.NominalDuration is { } duration ? TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(45).Ticks, duration.Ticks / 20)) : TimeSpan.FromHours(2);
        var depletion = request.CurrentPercentageUsed - retained[0].PercentageUsed;
        if (retained.Length < 6 || span < minimumSpan || depletion < 2m || kept.Max(item => item.Change) > depletion * .6m)
            return Withheld(QuotaProjectionState.Learning, evidence);

        var rate = FitRecencyWeightedRate(retained, request.Cycle.NominalDuration, request.EvaluatedAt, out var residual, out var fittedCurrent);
        if (rate <= 0 || residual > 3m || Math.Abs(fittedCurrent - request.CurrentPercentageUsed) > 3m)
            return Withheld(QuotaProjectionState.Inconsistent, evidence);

        var remaining = 100m - request.CurrentPercentageUsed;
        if (remaining < 0) return Withheld(QuotaProjectionState.Inconsistent, evidence);
        var exhaustion = RoundToQuarterHour(request.EvaluatedAt.AddHours((double)(remaining / rate)));
        var confidence = Confidence(retained.Length, span, request.Cycle.NominalDuration, residual, excluded, intervals.Length, kept);
        var margin = TimeSpan.FromTicks(Math.Min(TimeSpan.FromHours(6).Ticks, Math.Max(TimeSpan.FromMinutes(30).Ticks, (request.Cycle.ResetAt - request.EvaluatedAt).Ticks / 20)));
        var risk = exhaustion < request.Cycle.ResetAt - margin ? ResetRelativeRisk.ProjectedToRunOut
            : exhaustion > request.Cycle.ResetAt + margin ? ResetRelativeRisk.ProjectedToLast : ResetRelativeRisk.CloseToReset;
        return new(QuotaProjectionState.Available, Version, decimal.Round(rate, 1, MidpointRounding.AwayFromZero), exhaustion, confidence, risk, evidence);
    }

    private static List<QuotaSample> CompatibleActiveSegment(QuotaProjectionRequest request)
    {
        // Preserve arrival order until temporal discontinuities have been detected.
        var arrivals = request.Samples.Where(item => item.Cycle == request.Cycle && item.RetrievedAt <= request.EvaluatedAt && item.PercentageUsed is >= 0 and <= 100)
            .DistinctBy(item => item.SampleKey);
        var segment = new List<QuotaSample>();
        foreach (var sample in arrivals)
        {
            if (segment.Count > 0 && (sample.RetrievedAt < segment[^1].RetrievedAt
                || sample.PercentageUsed < segment[^1].PercentageUsed - .5m)) segment.Clear();
            segment.Add(sample);
        }
        return segment.OrderBy(item => item.RetrievedAt).ToList();
    }

    private static decimal FitRecencyWeightedRate(IReadOnlyList<QuotaSample> samples, TimeSpan? duration, DateTimeOffset evaluatedAt, out decimal residual, out decimal fittedCurrent)
    {
        var halfLife = duration is { } value ? Math.Max(2m, Hours(value) / 5m) : 12m;
        var first = samples[0].RetrievedAt; var last = samples[^1].RetrievedAt;
        var weighted = samples.Select(sample => (X: Hours(sample.RetrievedAt - first), Y: sample.PercentageUsed, W: 1m / (1m + Hours(last - sample.RetrievedAt) / halfLife))).ToArray();
        var totalWeight = weighted.Sum(item => item.W); var meanX = weighted.Sum(item => item.W * item.X) / totalWeight; var meanY = weighted.Sum(item => item.W * item.Y) / totalWeight;
        var denominator = weighted.Sum(item => item.W * (item.X - meanX) * (item.X - meanX));
        var rate = denominator == 0 ? 0 : weighted.Sum(item => item.W * (item.X - meanX) * (item.Y - meanY)) / denominator;
        var intercept = meanY - rate * meanX; fittedCurrent = intercept + rate * Hours(evaluatedAt - first);
        residual = (decimal)Math.Sqrt((double)(weighted.Sum(item => item.W * (item.Y - (intercept + rate * item.X)) * (item.Y - (intercept + rate * item.X))) / totalWeight));
        return rate;
    }

    private static ProjectionConfidence Confidence(int count, TimeSpan span, TimeSpan? duration, decimal residual, int excluded, int totalIntervals, IReadOnlyList<Interval> kept)
    {
        var coverage = duration is { } value ? span >= TimeSpan.FromTicks(value.Ticks / 5) : span >= TimeSpan.FromHours(2);
        if (count >= 16 && coverage && residual <= 1m && excluded == 0 && kept.Count(item => item.Change > 0) >= 2) return ProjectionConfidence.High;
        if (count >= 10 && (duration is not { } medium || span >= TimeSpan.FromTicks(medium.Ticks / 10)) && residual <= 2m && excluded * 10 <= totalIntervals) return ProjectionConfidence.Medium;
        return ProjectionConfidence.Low;
    }

    private static QuotaProjectionResult Withheld(QuotaProjectionState state, ProjectionEvidence evidence) => new(state, "Learning your pace", null, null, null, null, evidence);
    private static decimal Median(IEnumerable<decimal> values) { var ordered = values.OrderBy(value => value).ToArray(); return ordered.Length == 0 ? 0 : ordered.Length % 2 == 1 ? ordered[ordered.Length / 2] : (ordered[(ordered.Length / 2) - 1] + ordered[ordered.Length / 2]) / 2m; }
    private static decimal Hours(TimeSpan value) => (decimal)value.Ticks / TimeSpan.TicksPerHour;
    private static DateTimeOffset RoundToQuarterHour(DateTimeOffset value) { const long quarter = TimeSpan.TicksPerMinute * 15; return new DateTimeOffset(((value.UtcTicks + (quarter / 2)) / quarter) * quarter, TimeSpan.Zero); }
    private sealed record Interval(QuotaSample First, QuotaSample Last) { public decimal Hours => QuotaProjectionPolicy.Hours(Last.RetrievedAt - First.RetrievedAt); public decimal Rate => Hours == 0 ? 0 : Change / Hours; public decimal Change => Last.PercentageUsed - First.PercentageUsed; }
}
