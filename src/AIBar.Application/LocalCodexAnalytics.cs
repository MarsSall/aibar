using AIBar.Domain;

namespace AIBar.Application;

public enum AnalyticsScanStatus { Idle, Loading, Complete, Empty, Partial, Unavailable, Failed }
public sealed record ModelTokenTotal(string Model, TokenTotals Tokens);
public sealed class LocalAnalyticsState
{
    public LocalAnalyticsState(AnalyticsScanStatus status, ScanCoverageState coverage, TokenTotals? totals, IReadOnlyList<ModelTokenTotal> models, DateTimeOffset? scannedAt, IReadOnlyList<string> warnings)
    {
        Status = status;
        Coverage = coverage;
        Totals = totals;
        Models = Array.AsReadOnly(models.ToArray());
        ScannedAt = scannedAt;
        var safeWarnings = warnings.Where(IsSafeWarning).Distinct(StringComparer.Ordinal).Order().ToArray();
        WarningCodes = Array.AsReadOnly(coverage switch
        {
            ScanCoverageState.Partial when safeWarnings.Length == 0 => ["local_data_partial"],
            ScanCoverageState.Unavailable when safeWarnings.Length == 0 => ["local_data_unavailable"],
            _ => safeWarnings
        });
    }
    public AnalyticsScanStatus Status { get; }
    public ScanCoverageState Coverage { get; }
    public TokenTotals? Totals { get; }
    public IReadOnlyList<ModelTokenTotal> Models { get; }
    public DateTimeOffset? ScannedAt { get; }
    public IReadOnlyList<string> WarningCodes { get; }
    public string SourceLabel => "Local Codex data";
    public string SourceDetail => "Source: local Codex CLI session data";
    public string TotalTokensLabel => Totals is null ? "No local token totals available" : $"Total tokens: {Totals.Total}";
    public string ModelTotalsLabel => Models.Count == 0 ? "No model totals available" : string.Join("; ", Models.Select(model => $"{model.Model}: {model.Tokens.Total}"));
    public string? LastScanLabel => ScannedAt is null ? null : $"Last local scan: {ScannedAt:O}";
    public string CoverageLabel => $"Coverage: {Coverage}";
    public string? WarningLabel => WarningCodes.Count == 0 ? null : string.Join(", ", WarningCodes);
    public bool IsLoading => Status == AnalyticsScanStatus.Loading;
    public static LocalAnalyticsState Complete(TokenTotals t, IReadOnlyList<ModelTokenTotal> m, DateTimeOffset at, IReadOnlyList<string> w) => new(AnalyticsScanStatus.Complete, ScanCoverageState.Complete, t, m, at, w);
    public static LocalAnalyticsState Partial(TokenTotals t, IReadOnlyList<ModelTokenTotal> m, DateTimeOffset at, IReadOnlyList<string> w) => new(AnalyticsScanStatus.Partial, ScanCoverageState.Partial, t, m, at, w);
    public static LocalAnalyticsState Empty(DateTimeOffset at, IReadOnlyList<string> w) => new(AnalyticsScanStatus.Empty, ScanCoverageState.Unavailable, null, [], at, w);
    public static LocalAnalyticsState Unavailable(DateTimeOffset at, IReadOnlyList<string> w) => new(AnalyticsScanStatus.Unavailable, ScanCoverageState.Unavailable, null, [], at, w);
    public static LocalAnalyticsState Failed(DateTimeOffset at) => new(AnalyticsScanStatus.Failed, ScanCoverageState.Unavailable, null, [], at, ["local_scan_failed"]);
    public LocalAnalyticsState WithStatus(AnalyticsScanStatus status) => new(status, Coverage, Totals, Models, ScannedAt, WarningCodes);

    private static bool IsSafeWarning(string warning) => warning.StartsWith("session_", StringComparison.Ordinal) || warning.StartsWith("local_", StringComparison.Ordinal);
}

public interface ILocalCodexAnalyticsScanner { ValueTask<LocalAnalyticsState> ScanAsync(CancellationToken cancellationToken); }

public sealed class LocalCodexAnalyticsAdapter : ILocalCodexAnalyticsScanner
{
    private readonly Func<CancellationToken, SessionDiscoveryResult> _discover;
    private readonly AnalyticsScanCoordinator _coordinator;
    private readonly SqliteDailyModelUsageStore _store;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<SessionDiscoveryResult>? _afterDiscovery;

    public LocalCodexAnalyticsAdapter(SessionFileDiscovery discovery, AnalyticsScanCoordinator coordinator, SqliteDailyModelUsageStore store, Func<DateTimeOffset> utcNow, Action<SessionDiscoveryResult>? afterDiscovery = null)
        : this(discovery.Discover, coordinator, store, utcNow, afterDiscovery) { }

    public LocalCodexAnalyticsAdapter(Func<CancellationToken, SessionDiscoveryResult> discover, AnalyticsScanCoordinator coordinator, SqliteDailyModelUsageStore store, Func<DateTimeOffset> utcNow, Action<SessionDiscoveryResult>? afterDiscovery = null)
    {
        _discover = discover; _coordinator = coordinator; _store = store; _utcNow = utcNow; _afterDiscovery = afterDiscovery;
    }

    public async ValueTask<LocalAnalyticsState> ScanAsync(CancellationToken cancellationToken)
    {
        var discovery = _discover(cancellationToken);
        _afterDiscovery?.Invoke(discovery);
        var warnings = new HashSet<string>(discovery.Coverage.WarningCodes.Where(IsSafeWarning), StringComparer.Ordinal);
        var files = discovery.Batches.SelectMany(batch => batch.Files).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var scannedAt = _utcNow();
        if (files.Length == 0) return LocalAnalyticsState.Unavailable(scannedAt, warnings.Order().ToArray());

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _coordinator.ScanAsync(file, cancellationToken);
            foreach (var warning in result.WarningCodes.Where(IsSafeWarning)) warnings.Add(warning);
            if (result.RebuildRequired) warnings.Add("local_analytics_rebuild_required");
        }

        var usage = await _store.LoadAsync(cancellationToken);
        if (usage.Count == 0) return LocalAnalyticsState.Empty(scannedAt, warnings.Order().ToArray());
        var models = usage.GroupBy(item => item.Model, StringComparer.Ordinal)
            .Select(group => new ModelTokenTotal(group.Key, Add(group.Select(item => item.Tokens))))
            .OrderBy(item => item.Model, StringComparer.Ordinal).ToArray();
        var totals = Add(models.Select(model => model.Tokens));
        return warnings.Count == 0
            ? LocalAnalyticsState.Complete(totals, models, scannedAt, [])
            : LocalAnalyticsState.Partial(totals, models, scannedAt, warnings.Order().ToArray());
    }

    private static TokenTotals Add(IEnumerable<TokenTotals> values) => values.Aggregate(new TokenTotals(0, 0, 0), (sum, value) => new(sum.Input + value.Input, sum.CachedInput + value.CachedInput, sum.Output + value.Output));
    private static bool IsSafeWarning(string warning) => warning.StartsWith("session_", StringComparison.Ordinal) || warning == "local_analytics_rebuild_required";
}
