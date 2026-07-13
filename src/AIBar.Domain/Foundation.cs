namespace AIBar.Domain;

public enum FreshnessState { Current, Stale, Unavailable }
public enum QuotaErrorKind { Authentication, Permission, MalformedResponse, Network, Service, Unavailable, Redirect }
public enum ScanCoverageState { Complete, Partial, Unavailable }
public enum PricingResultState { Available, UnsupportedModel }

public sealed record TokenTotals(long Input, long CachedInput, long Output)
{
    public long Total => Input + CachedInput + Output;
}

public sealed record QuotaWindow(decimal PercentageUsed, DateTimeOffset ResetAt)
{
    public TimeSpan CountdownAt(DateTimeOffset now) => ResetAt > now ? ResetAt - now : TimeSpan.Zero;
}

public sealed record QuotaSnapshot
{
    public QuotaSnapshot(QuotaWindow primary, QuotaWindow weekly, DateTimeOffset retrievedAt, decimal? resetCredits = null)
    {
        Primary = primary;
        Weekly = weekly;
        RetrievedAt = retrievedAt;
        ResetCredits = resetCredits;
    }

    public QuotaWindow Primary { get; }
    public QuotaWindow Weekly { get; }
    public DateTimeOffset RetrievedAt { get; }
    public decimal? ResetCredits { get; }
}

public sealed record ScanCoverage
{
    public ScanCoverage(ScanCoverageState state, int filesRead, int filesSkipped, IReadOnlyList<string> warnings)
    {
        State = state;
        FilesRead = filesRead;
        FilesSkipped = filesSkipped;
        Warnings = warnings;
    }

    public ScanCoverageState State { get; }
    public int FilesRead { get; }
    public int FilesSkipped { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed record DailyUsage
{
    public DailyUsage(DateOnly localDay, string timeZoneId, TimeSpan observedOffset, string model, TokenTotals tokens)
    {
        LocalDay = localDay;
        TimeZoneId = timeZoneId;
        ObservedOffset = observedOffset;
        Model = model;
        Tokens = tokens;
    }

    public DateOnly LocalDay { get; }
    public string TimeZoneId { get; }
    public TimeSpan ObservedOffset { get; }
    public string Model { get; }
    public TokenTotals Tokens { get; }
}

public sealed record PricingResult(decimal? EstimatedCost, string CatalogVersion, DateOnly CatalogDate, PricingResultState State);
public sealed record QuotaFailure(QuotaErrorKind Kind, string SafeCode);
public sealed record CredentialResult(string? AccessToken, string? AccountId, QuotaFailure? Failure);
public sealed record QuotaProviderResult(QuotaSnapshot? Snapshot, QuotaFailure? Failure, decimal? ResetCredits = null, QuotaFailure? OptionalFailure = null);
public sealed record UsageScanResult(IReadOnlyList<DailyUsage> Usage, ScanCoverage Coverage);
public enum RefreshTrigger { Poll, PopoverOpened, Resume, Manual }
public sealed record QuotaRefreshState(QuotaSnapshot? Snapshot, FreshnessState Freshness, bool IsLoading, QuotaFailure? Failure, QuotaFailure? OptionalFailure);

public sealed class FreshnessPolicy(TimeSpan threshold)
{
    public FreshnessState Evaluate(QuotaSnapshot? snapshot, DateTimeOffset now) => snapshot is null
        ? FreshnessState.Unavailable
        : now - snapshot.RetrievedAt <= threshold ? FreshnessState.Current : FreshnessState.Stale;
}

public static class TokenDelta
{
    public static TokenTotals FromCumulative(TokenTotals previous, TokenTotals current) => new(
        Math.Max(0, current.Input - previous.Input),
        Math.Max(0, current.CachedInput - previous.CachedInput),
        Math.Max(0, current.Output - previous.Output));
}

public interface IClock { DateTimeOffset UtcNow { get; } }
public interface ILocalTimePolicy { DateTimeOffset ToLocal(DateTimeOffset timestamp); }

public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow.ToUniversalTime();
}

public sealed class TimeZoneLocalTimePolicy(TimeZoneInfo timeZone) : ILocalTimePolicy
{
    public DateTimeOffset ToLocal(DateTimeOffset timestamp) => TimeZoneInfo.ConvertTime(timestamp, timeZone);
}
public interface ICodexCredentialSource { ValueTask<CredentialResult> GetAsync(CancellationToken cancellationToken); }
public interface IQuotaProvider { ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken); }
public interface IQuotaSnapshotStore
{
    ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
public interface IUsageScanner { ValueTask<UsageScanResult> ScanAsync(CancellationToken cancellationToken); }
public interface IAnalyticsStore { ValueTask SaveAsync(IReadOnlyList<DailyUsage> usage, CancellationToken cancellationToken); }
public interface IPricingCatalog { PricingResult Price(DailyUsage usage); }
public interface IStartupRegistration
{
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken);
    ValueTask<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}
