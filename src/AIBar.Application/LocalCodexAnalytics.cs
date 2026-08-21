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
    public static LocalAnalyticsState Failed(DateTimeOffset at, string safeCode = "local_scan_failed") => new(AnalyticsScanStatus.Failed, ScanCoverageState.Unavailable, null, [], at, [safeCode]);
    public LocalAnalyticsState WithStatus(AnalyticsScanStatus status) => new(status, Coverage, Totals, Models, ScannedAt, WarningCodes);

    private static bool IsSafeWarning(string warning) => warning is
        "session_root_missing" or "session_root_unreadable" or "session_root_attribute_unavailable" or "session_root_reparse_skipped" or
        "session_sessions_missing" or "session_archived_sessions_missing" or "session_reparse_skipped" or "session_directory_attribute_unavailable" or
        "session_directory_unreadable" or "session_file_unreadable" or "session_file_changed" or "session_incomplete_tail" or "session_malformed_record" or
        "session_cancelled" or "local_data_partial" or "local_data_unavailable" or "local_analytics_rebuild_required" or "local_scan_not_started" or
        "local_scan_failed" or "local_discovery_failed" or "local_session_scan_failed" or "local_store_load_failed";
}

public sealed class LocalCodexAnalyticsScanException : Exception
{
    private static readonly HashSet<string> AllowedCodes = ["local_discovery_failed", "local_session_scan_failed", "local_store_load_failed"];
    public LocalCodexAnalyticsScanException(string safeCode, Exception _)
        : base(AllowedCodes.Contains(safeCode) ? safeCode : "local_scan_failed") => SafeCode = Message;
    public string SafeCode { get; }
}

public interface ILocalCodexAnalyticsScanner { ValueTask<LocalAnalyticsState> ScanAsync(CancellationToken cancellationToken); }

public sealed class LocalCodexAnalyticsAdapter : ILocalCodexAnalyticsScanner
{
    private readonly Func<CancellationToken, SessionDiscoveryResult> _discover;
    private readonly Func<string, CancellationToken, ValueTask<AnalyticsScanResult>> _scan;
    private readonly Func<CancellationToken, ValueTask<IReadOnlyList<DailyUsage>>> _load;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<SessionDiscoveryResult>? _afterDiscovery;

    public LocalCodexAnalyticsAdapter(SessionFileDiscovery discovery, AnalyticsScanCoordinator coordinator, SqliteDailyModelUsageStore store, Func<DateTimeOffset> utcNow, Action<SessionDiscoveryResult>? afterDiscovery = null)
        : this(discovery.Discover, coordinator, store, utcNow, afterDiscovery) { }

    public LocalCodexAnalyticsAdapter(Func<CancellationToken, SessionDiscoveryResult> discover, AnalyticsScanCoordinator coordinator, SqliteDailyModelUsageStore store, Func<DateTimeOffset> utcNow, Action<SessionDiscoveryResult>? afterDiscovery = null)
        : this(discover, coordinator.ScanAsync, store.LoadAsync, utcNow, afterDiscovery) { }

    internal LocalCodexAnalyticsAdapter(Func<CancellationToken, SessionDiscoveryResult> discover, Func<string, CancellationToken, ValueTask<AnalyticsScanResult>> scan, Func<CancellationToken, ValueTask<IReadOnlyList<DailyUsage>>> load, Func<DateTimeOffset> utcNow, Action<SessionDiscoveryResult>? afterDiscovery = null)
    {
        _discover = discover; _scan = scan; _load = load; _utcNow = utcNow; _afterDiscovery = afterDiscovery;
    }

    public async ValueTask<LocalAnalyticsState> ScanAsync(CancellationToken cancellationToken)
    {
        SessionDiscoveryResult discovery;
        try { discovery = _discover(cancellationToken); _afterDiscovery?.Invoke(discovery); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw new LocalCodexAnalyticsScanException("local_discovery_failed", exception); }
        var warnings = new HashSet<string>(discovery.Coverage.WarningCodes.Where(IsSafeWarning), StringComparer.Ordinal);
        var files = discovery.Batches.SelectMany(batch => batch.Files).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var scannedAt = _utcNow();
        if (files.Length == 0) return LocalAnalyticsState.Unavailable(scannedAt, warnings.Order().ToArray());

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AnalyticsScanResult result;
            try { result = await _scan(file, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { throw new LocalCodexAnalyticsScanException("local_session_scan_failed", exception); }
            foreach (var warning in result.WarningCodes.Where(IsSafeWarning)) warnings.Add(warning);
            if (result.RebuildRequired) warnings.Add("local_analytics_rebuild_required");
        }

        IReadOnlyList<DailyUsage> usage;
        try { usage = await _load(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw new LocalCodexAnalyticsScanException("local_store_load_failed", exception); }
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
