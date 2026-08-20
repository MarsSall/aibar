using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

public sealed record LocalUsagePresentationDetailRow(
    DateOnly LocalDay,
    string LocalDayLabel,
    string ModelLabel,
    BigInteger Total,
    string TotalLabel,
    string TokenBreakdownLabel)
{
    public string AutomationLabel => $"{LocalDayLabel}, {ModelLabel}, {TotalLabel}, {TokenBreakdownLabel}";
}

public sealed record LocalUsagePresentationRow(
    UsageProjectionScope Scope,
    string ScopeLabel,
    BigInteger? Total,
    string TotalLabel,
    string? TokenBreakdownLabel,
    string StatusLabel,
    IReadOnlyList<LocalUsagePresentationDetailRow> Details)
{
    public string AutomationLabel => string.IsNullOrEmpty(TokenBreakdownLabel)
        ? $"{ScopeLabel}, {TotalLabel}, {StatusLabel}"
        : $"{ScopeLabel}, {TotalLabel}, {TokenBreakdownLabel}, {StatusLabel}";
}

public sealed record LocalUsagePresentationState
{
    internal LocalUsagePresentationState(LocalUsageProjectionStatus? projectionStatus, string statusLabel,
        IEnumerable<LocalUsagePresentationRow> rows, bool isRefreshing = false)
    {
        ProjectionStatus = projectionStatus; StatusLabel = statusLabel;
        Rows = Array.AsReadOnly(rows.ToArray()); IsRefreshing = isRefreshing;
    }

    public string Title => LocalUsageCopy.Title;
    public string Description => LocalUsageCopy.Description;
    public LocalUsageProjectionStatus? ProjectionStatus { get; init; }
    public string StatusLabel { get; init; }
    public IReadOnlyList<LocalUsagePresentationRow> Rows { get; init; }
    public bool IsRefreshing { get; init; }
}

public sealed class LocalUsagePresentationMapper
{
    private static readonly UsageProjectionScope[] Scopes =
        [UsageProjectionScope.OpenCode, UsageProjectionScope.Pi, UsageProjectionScope.Combined];

    public LocalUsagePresentationState NotLoaded() => WithoutTotals(null, LocalUsageCopy.NotLoaded,
        LocalUsageCopy.TotalNotLoaded, [], LocalUsageCopy.NotLoaded);

    public LocalUsagePresentationState Map(LocalUsageCoordinatorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ProjectionStatus == LocalUsageProjectionStatus.Skipped)
            return WithoutTotals(result.ProjectionStatus, LocalUsageCopy.NotLoaded, LocalUsageCopy.TotalNotLoaded, result.Tools, LocalUsageCopy.NotLoaded);
        if (result.ProjectionStatus == LocalUsageProjectionStatus.Unavailable)
            return WithoutTotals(result.ProjectionStatus, LocalUsageCopy.TotalsUnavailable, LocalUsageCopy.TotalUnavailable, result.Tools, LocalUsageCopy.ProjectionUnavailable);

        var totals = new Dictionary<UsageProjectionScope, TokenBreakdown>();
        foreach (var fact in result.ProjectionFacts)
        {
            var tokens = fact.Tokens;
            var current = totals.GetValueOrDefault(fact.Scope);
            totals[fact.Scope] = new(
                current.InputUncached + tokens.InputUncached,
                current.CacheRead + tokens.CacheRead,
                current.CacheWrite + tokens.CacheWrite,
                current.OutputVisible + tokens.OutputVisible,
                current.OutputReasoning + tokens.OutputReasoning);
        }

        var rows = Scopes.Select(scope => totals.TryGetValue(scope, out var total)
            ? new LocalUsagePresentationRow(scope, ScopeLabel(scope), total.Total,
                $"{total.Total.ToString(CultureInfo.InvariantCulture)} retained tokens", total.Label, RowStatus(scope, result.Tools), Details(scope, result.ProjectionFacts))
            : new LocalUsagePresentationRow(scope, ScopeLabel(scope), null, LocalUsageCopy.NoFacts, null, RowStatus(scope, result.Tools), []));
        return new(result.ProjectionStatus, totals.Count == 0 ? LocalUsageCopy.NoFacts : LocalUsageCopy.Loaded, rows);
    }

    private static LocalUsagePresentationState WithoutTotals(LocalUsageProjectionStatus? projectionStatus, string status,
        string total, IReadOnlyList<LocalUsageToolOutcome> tools, string combinedStatus) =>
        new(projectionStatus, status, Scopes.Select(scope => new LocalUsagePresentationRow(scope, ScopeLabel(scope), null,
            total, null, scope == UsageProjectionScope.Combined ? combinedStatus : RowStatus(scope, tools), [])));

    private static IReadOnlyList<LocalUsagePresentationDetailRow> Details(UsageProjectionScope scope,
        IReadOnlyList<DailyToolModelUsageFact> facts) => Array.AsReadOnly(facts
        .Where(fact => fact.Scope == scope)
        .OrderByDescending(fact => fact.LocalDay)
        .ThenBy(fact => fact.Model, StringComparer.Ordinal)
        .Select(Detail)
        .ToArray());

    private static LocalUsagePresentationDetailRow Detail(DailyToolModelUsageFact fact)
    {
        var total = TokenBreakdown.From(fact.Tokens);
        return new(fact.LocalDay, fact.LocalDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), SafeModelLabel(fact.Model),
            total.Total, $"{total.Total.ToString(CultureInfo.InvariantCulture)} retained tokens", total.Label);
    }

    private static string SafeModelLabel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return LocalUsageCopy.UnknownModel;
        var label = new string(model.Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(label) ? LocalUsageCopy.UnknownModel : label;
    }

    private readonly record struct TokenBreakdown(BigInteger InputUncached, BigInteger CacheRead, BigInteger CacheWrite,
        BigInteger OutputVisible, BigInteger OutputReasoning)
    {
        internal static TokenBreakdown From(UsageProjectionTokens tokens) => new(tokens.InputUncached, tokens.CacheRead,
            tokens.CacheWrite, tokens.OutputVisible, tokens.OutputReasoning);
        internal BigInteger Total => InputUncached + CacheRead + CacheWrite + OutputVisible + OutputReasoning;
        internal string Label
        {
            get
            {
                var cacheTotal = CacheRead + CacheWrite;
                return $"Input {Format(InputUncached)} · Cache {Format(cacheTotal)} (read {Format(CacheRead)}, write {Format(CacheWrite)}) · Output {Format(OutputVisible)} · Reasoning {Format(OutputReasoning)}";
            }
        }

        private static string Format(BigInteger value) => value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ScopeLabel(UsageProjectionScope scope) => scope switch
    {
        UsageProjectionScope.OpenCode => "OpenCode",
        UsageProjectionScope.Pi => "Pi",
        UsageProjectionScope.Combined => "Combined",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    private static string RowStatus(UsageProjectionScope scope, IReadOnlyList<LocalUsageToolOutcome> tools)
    {
        if (scope == UsageProjectionScope.Combined) return LocalUsageCopy.RetainedHistory;
        var tool = scope == UsageProjectionScope.OpenCode ? UsageTool.OpenCode : UsageTool.Pi;
        return tools.FirstOrDefault(item => item.Tool == tool)?.Status switch
        {
            LocalUsageSourceStatus.Disabled => LocalUsageCopy.ReadingOff,
            LocalUsageSourceStatus.Completed => LocalUsageCopy.RetainedHistory,
            LocalUsageSourceStatus.Partial => LocalUsageCopy.Partial,
            LocalUsageSourceStatus.Unavailable => LocalUsageCopy.SourceUnavailable,
            LocalUsageSourceStatus.RebuildRequired => LocalUsageCopy.RebuildRequired,
            LocalUsageSourceStatus.Failed => LocalUsageCopy.RefreshFailed,
            _ => LocalUsageCopy.NotLoaded
        };
    }
}

public sealed class LocalUsagePresentationHost : INotifyPropertyChanged
{
    private readonly LocalUsagePresentationMapper _mapper;
    private readonly Func<CancellationToken, ValueTask<LocalUsageCoordinatorResult>> _start, _refresh;
    private readonly Func<LocalUsagePolicy, CancellationToken, ValueTask<LocalUsageCoordinatorResult>> _applyPolicy;
    private LocalUsagePresentationState _state;

    public LocalUsagePresentationHost(LocalUsageCoordinator coordinator, LocalUsagePresentationMapper mapper)
        : this(mapper, coordinator.StartAsync, coordinator.ApplyPolicyAsync, coordinator.RefreshAsync) { }

    internal LocalUsagePresentationHost(LocalUsagePresentationMapper mapper,
        Func<CancellationToken, ValueTask<LocalUsageCoordinatorResult>> start,
        Func<LocalUsagePolicy, CancellationToken, ValueTask<LocalUsageCoordinatorResult>> applyPolicy,
        Func<CancellationToken, ValueTask<LocalUsageCoordinatorResult>> refresh)
    {
        _mapper = mapper; _start = start; _applyPolicy = applyPolicy; _refresh = refresh; _state = mapper.NotLoaded();
    }

    public LocalUsagePresentationState State => _state;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ValueTask<LocalUsageCoordinatorResult> StartAsync(CancellationToken token = default) => PublishAsync(() => _start(token));
    public ValueTask<LocalUsageCoordinatorResult> ApplyPolicyAsync(LocalUsagePolicy policy, CancellationToken token = default) => PublishAsync(() => _applyPolicy(policy, token));
    public ValueTask<LocalUsageCoordinatorResult> RefreshAsync(CancellationToken token = default) => PublishAsync(() => _refresh(token));

    private async ValueTask<LocalUsageCoordinatorResult> PublishAsync(Func<ValueTask<LocalUsageCoordinatorResult>> operation)
    {
        var previous = _state;
        Publish(previous with { IsRefreshing = true, StatusLabel = LocalUsageCopy.Refreshing });
        try
        {
            var result = await operation();
            Publish(_mapper.Map(result));
            return result;
        }
        catch { Publish(previous); throw; }
    }

    private void Publish(LocalUsagePresentationState state)
    {
        _state = state;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
    }
}

internal static class LocalUsageCopy
{
    internal const string Title = "OpenCode and Pi local usage";
    internal const string Description = "Accumulated retained history from enabled local readers.";
    internal const string Loaded = "Retained history loaded";
    internal const string NotLoaded = "Retained history was not loaded";
    internal const string TotalsUnavailable = "Retained totals unavailable";
    internal const string TotalNotLoaded = "Retained total not loaded";
    internal const string TotalUnavailable = "Retained total unavailable";
    internal const string NoFacts = "No retained usage facts";
    internal const string Refreshing = "Refreshing retained history";
    internal const string ReadingOff = "Reading off";
    internal const string RetainedHistory = "Retained history";
    internal const string Partial = "Partial refresh";
    internal const string SourceUnavailable = "Source unavailable";
    internal const string RebuildRequired = "Rebuild required";
    internal const string RefreshFailed = "Refresh failed";
    internal const string ProjectionUnavailable = "Projection unavailable";
    internal const string UnknownModel = "Unknown";
}
