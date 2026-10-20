namespace AIBar.Domain;

/// <summary>Optional display-only metadata from an already authorized quota read.</summary>
public interface IQuotaPlanMetadata { string? PlanLabel { get; } }

public static class QuotaPlanLabels
{
    // Never display arbitrary credential/response text in the panel.
    public static string? FromKnownValue(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "max" => "Max", "pro" => "Pro", "plus" => "Plus", "free" => "Free",
        "team" => "Team", "business" => "Business", "enterprise" => "Enterprise",
        "edu" => "Edu", _ => null
    };
}

public readonly record struct QuotaProviderId(string Value);
public readonly record struct QuotaWindowKey(string Value);
public enum QuotaWindowRole { Hero, Supporting }
public enum AccountIdentityOutcome { Verified, Unavailable, Uncertain }
public enum QuotaProjectionState { Learning, Inconsistent, Available }
public enum ProjectionConfidence { Low, Medium, High }
public enum ResetRelativeRisk { ProjectedToRunOut, CloseToReset, ProjectedToLast }
public enum QuotaExperienceState { Unavailable, Learning, Current, Stale, FailedRefresh }

public sealed record QuotaWindowCapability(QuotaWindowKey Key, QuotaWindowRole Role, string DisplayName, TimeSpan? NominalDuration);
public sealed record RemoteQuotaWindow(QuotaWindowCapability Capability, decimal PercentageUsed, DateTimeOffset ResetAt);
public sealed record ProviderQuotaEnvelope(QuotaProviderId Provider, IReadOnlyList<RemoteQuotaWindow> Windows, DateTimeOffset RetrievedAt, AccountIdentityOutcome Identity, QuotaFailure? OptionalFailure);
public sealed record QuotaCycle(QuotaProviderId Provider, QuotaWindowKey WindowKey, DateTimeOffset ResetAt, TimeSpan? NominalDuration);
public sealed record QuotaSample(QuotaCycle Cycle, DateTimeOffset RetrievedAt, decimal PercentageUsed, string SampleKey);
public sealed record ProjectionEvidence(int SampleCount, TimeSpan Span, int ExcludedIntervals, string Version);
public sealed record QuotaProjectionRequest(QuotaCycle Cycle, IReadOnlyList<QuotaSample> Samples, decimal CurrentPercentageUsed, DateTimeOffset EvaluatedAt);
public sealed record QuotaProjectionResult(QuotaProjectionState State, string Label, decimal? RatePerHour, DateTimeOffset? EstimatedExhaustionAt, ProjectionConfidence? Confidence, ResetRelativeRisk? Risk, ProjectionEvidence Evidence);
