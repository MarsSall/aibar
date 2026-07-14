using System.Security.Cryptography;
using System.Text;
using AIBar.Domain;

namespace AIBar.Application;

public sealed record AnalyticsScanResult(IReadOnlyList<DailyUsage> Usage, IReadOnlyList<string> WarningCodes, bool RebuildRequired);

public sealed class AnalyticsScanCoordinator(SessionJsonlScanner scanner, SqliteDailyModelUsageStore store, AnalyticsPolicy policy)
{
    public async ValueTask<AnalyticsScanResult> ScanAsync(string path, CancellationToken cancellationToken)
    {
        var fingerprint = SourceFingerprint(path);
        var prior = await store.LoadCheckpointAsync(fingerprint, cancellationToken);
        var prepared = await scanner.PrepareAsync(path, prior, cancellationToken);
        if (prepared.RebuildRequired) return new([], prepared.WarningCodes, true);
        if (prepared.ProposedCheckpoint is null) return new([], prepared.WarningCodes, prepared.RebuildRequired);
        var previous = prior is null ? new TokenTotals(0, 0, 0) : new(prior.CumulativeInputTokens, prior.CumulativeCachedInputTokens, prior.CumulativeOutputTokens);
        var observations = prepared.Records.Select(record =>
        {
            var current = new TokenTotals(record.InputTokens, record.CachedInputTokens, record.OutputTokens);
            var observation = new TokenObservation(record.Timestamp, record.Model, record.Model != AnalyticsPolicy.UnknownModel, previous, current);
            previous = current;
            return observation;
        });
        var usage = policy.Aggregate(observations).Usage;
        await store.SaveWithCheckpointAsync(usage, fingerprint, prior, prepared.ProposedCheckpoint, policy.PolicyVersion, cancellationToken);
        return new(usage, prepared.WarningCodes, prepared.RebuildRequired);
    }

    public string SourceFingerprint(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))));
}
