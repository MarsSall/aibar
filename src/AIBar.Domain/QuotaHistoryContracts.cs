namespace AIBar.Domain;

public enum QuotaHistoryProvider { Codex, Claude }
public enum QuotaHistoryWindow { FiveHour, Weekly }
public enum QuotaHistorySource { CodexLocal, ClaudeLocal, ClaudeRemote }

/// <summary>A normalized retrieval sample. Contains no credentials, account identifiers or paths.</summary>
public sealed record QuotaHistorySample(QuotaHistoryProvider Provider, QuotaHistoryWindow Window,
    decimal Percentage, DateTimeOffset ResetAt, DateTimeOffset RetrievedAt, QuotaHistorySource Source);

public interface IQuotaHistoryStore
{
    /// <summary>Atomically appends a batch, preserving the first sample for each provider/window/instant.
    /// Prunes all providers strictly before now minus 30 days, including old incoming samples.</summary>
    ValueTask AppendAsync(IReadOnlyList<QuotaHistorySample> samples, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Returns samples in ascending retrieval order, with both UTC-instant range endpoints inclusive.</summary>
    ValueTask<IReadOnlyList<QuotaHistorySample>> QueryAsync(QuotaHistoryProvider provider, QuotaHistoryWindow window,
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken);
}
