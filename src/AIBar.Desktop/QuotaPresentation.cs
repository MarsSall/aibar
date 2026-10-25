using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;
namespace AIBar.Desktop;
public sealed record QuotaWindowPresentation(string Label, decimal? PercentageUsed, string? ResetCountdown)
{
    public PanelPacePresentation? Pace { get; init; }
}

public sealed record BetaPresentationState(
    QuotaWindowPresentation Primary,
    QuotaWindowPresentation Weekly,
    bool IsPrimaryAvailable,
    bool IsWeeklyAvailable,
    bool IsCurrent,
    string FreshnessLabel,
    string? ErrorLabel,
    DateTimeOffset? RetrievedAt,
    string QuotaDisclosure,
    string AnalyticsDisclosure,
    string CostDisclosure,
    string PrivateEndpointDisclosure,
    bool IsLoading,
    bool IsFresh,
    bool IsCached,
    bool IsDegraded,
    bool IsMissingCredential,
    bool IsOffline,
    bool IsUnavailable,
    bool IsSafeError,
    TimeSpan? CachedAge,
    string? CachedAgeLabel,
    string? WarningLabel,
    string Disclosure,
    bool IsPrivateIntegrationDisabled = false,
    string? DiagnosticCode = null)
{
    // View-only state, derived from the existing safe diagnostic; no credential reads.
    public bool NeedsLogin => IsMissingCredential || DiagnosticCode == "quota_authentication";
    public bool ShowStaleBanner => !IsCurrent && !NeedsLogin && RetrievedAt is not null;
    public string LastReadingLabel => RetrievedAt is { } at ? $"Last sync {at.ToLocalTime():HH:mm}." : "Not updated yet.";
    public string? UpdatedLabel { get; init; }
    public string? SourceLabel { get; init; }
    public string? PlanLabel { get; init; }
}

public sealed class QuotaPresentationMapper(IClock clock, QuotaHistoryProvider provider = QuotaHistoryProvider.Codex)
{
    public BetaPresentationState Map(QuotaRefreshState state) => Map(state, []);

    public BetaPresentationState Map(QuotaRefreshState state, IReadOnlyList<QuotaHistorySample> history)
    {
        var snapshot = state.Snapshot;
        var failure = state.Failure;
        var isMissingCredential = failure?.SafeCode is "quota_credential_missing" or "quota_credential_unusable" or "quota_credentials_unavailable";
        var isOffline = failure?.Kind == QuotaErrorKind.Network;
        var isLoading = state.IsLoading;
        var isFresh = snapshot is not null && state.Freshness == FreshnessState.Current && !isLoading && failure is null;
        var isDegraded = snapshot is not null && failure is not null;
        var isCached = snapshot is not null && !isFresh;
        var isUnavailable = snapshot is null && !isLoading;
        var isSafeError = failure is not null && !isMissingCredential && !isOffline && !isDegraded && failure.Kind != QuotaErrorKind.Unavailable;
        TimeSpan? cachedAge = isCached ? NonNegative(clock.UtcNow - snapshot!.RetrievedAt) : null;
        var warning = WarningLabel(failure, isMissingCredential, isOffline);
        var freshness = isLoading ? "Loading" : isFresh ? "Current" : isCached ? "Stale" : isMissingCredential ? "Missing credential" : isOffline ? "Offline" : "Unavailable";
        return new(
            Window("5-hour quota", snapshot?.Primary, isFresh, QuotaPaceWindow.FiveHour, history),
            Window("Weekly quota", snapshot?.Weekly, isFresh, QuotaPaceWindow.Weekly, history),
            snapshot?.Primary is not null,
            snapshot?.Weekly is not null,
            isFresh,
            freshness,
            warning,
            snapshot?.RetrievedAt,
            ViewModelDisplayLabels.ServiceQuota,
            ViewModelDisplayLabels.LocallyDerivedAnalytics,
            ViewModelDisplayLabels.EstimatedCost,
            "Quota access uses a private, undocumented, unsupported endpoint.",
            isLoading,
            isFresh,
            isCached,
            isDegraded,
            isMissingCredential,
            isOffline,
            isUnavailable,
            isSafeError,
            cachedAge,
            cachedAge is null ? null : $"Cached {FormatCountdown(cachedAge.Value)}",
            warning,
            "Private quota access is optional, disabled by default, and can be revoked at any time.",
            false,
            DiagnosticCode(failure))
        {
            UpdatedLabel = snapshot is null ? null : RelativeUpdatedLabel(clock.UtcNow - snapshot.RetrievedAt)
        };
    }

    private QuotaWindowPresentation Window(string label, QuotaWindow? window, bool current, QuotaPaceWindow kind, IReadOnlyList<QuotaHistorySample> history) => new(
        label,
        window?.PercentageUsed,
        window is null ? null : FormatCountdown(window.CountdownAt(clock.UtcNow)))
    {
        Pace = PanelPacePresentation.Create(kind == QuotaPaceWindow.Weekly ? "Codex weekly" : "Codex 5-hour",
            window?.PercentageUsed, ObservedPacePolicy.Calculate(window?.PercentageUsed, window?.ResetAt, kind, clock.UtcNow, provider,
                current ? history : []), current, clock.UtcNow)
    };

    private static string FormatCountdown(TimeSpan value)
    {
        var duration = NonNegative(value);
        return duration < TimeSpan.FromDays(1)
            ? $"{duration.Hours:00}h {duration.Minutes:00}m"
            : $"{duration.Days}d {duration.Hours:00}h {duration.Minutes:00}m";
    }

    private static string RelativeUpdatedLabel(TimeSpan age)
    {
        age = NonNegative(age);
        return age < TimeSpan.FromMinutes(1) ? "Updated just now"
            : age < TimeSpan.FromHours(1) ? $"Updated {(int)age.TotalMinutes}m ago"
            : age < TimeSpan.FromDays(1) ? $"Updated {(int)age.TotalHours}h ago"
            : $"Updated {(int)age.TotalDays}d ago";
    }

    private static TimeSpan NonNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
    private static string? WarningLabel(QuotaFailure? failure, bool isMissingCredential, bool isOffline) =>
        isMissingCredential ? "Credential unavailable" : isOffline ? "Network unavailable" : failure?.Kind switch
    {
        QuotaErrorKind.Authentication => "Authentication required",
        QuotaErrorKind.Permission => "Permission denied",
        QuotaErrorKind.MalformedResponse => "Service response unavailable",
        QuotaErrorKind.Network => "Network unavailable",
        QuotaErrorKind.Service => "Service unavailable",
        QuotaErrorKind.Redirect => "Redirect rejected",
        QuotaErrorKind.Unavailable when failure.SafeCode == "quota_refresh_failed" => "Refresh failed",
        QuotaErrorKind.Unavailable => "Quota unavailable",
        _ => null
    };

    private static string? DiagnosticCode(QuotaFailure? failure) => failure?.Kind switch
    {
        QuotaErrorKind.Authentication => "quota_authentication",
        QuotaErrorKind.Permission => "quota_permission",
        QuotaErrorKind.MalformedResponse => "quota_malformed_response",
        QuotaErrorKind.Network => "quota_network",
        QuotaErrorKind.Service => "quota_service",
        QuotaErrorKind.Redirect => "quota_redirect",
        QuotaErrorKind.Unavailable when failure.SafeCode is "quota_credential_missing" or "quota_credential_unusable" or "quota_credentials_unavailable" => failure.SafeCode,
        QuotaErrorKind.Unavailable when failure.SafeCode == "quota_refresh_failed" => "quota_refresh_failed",
        QuotaErrorKind.Unavailable => "quota_unavailable",
        _ => "quota_refresh_failed"
    };
}

public interface IManualRefreshCommand
{
    event EventHandler? CanExecuteChanged;
    bool CanExecute { get; }
    ValueTask ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class ManualRefreshCommand(Func<CancellationToken, ValueTask> refresh, Func<bool> canExecute, Action<Exception>? report = null) : IManualRefreshCommand, ICommand
{
    private int _isRunning;

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute => Volatile.Read(ref _isRunning) == 0 && canExecute();

    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        if (!canExecute() || Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await refresh(cancellationToken); }
        catch (Exception exception) { (report ?? ReportFault)(exception); throw; }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    bool ICommand.CanExecute(object? parameter) => CanExecute;
    async void ICommand.Execute(object? parameter) { try { await ExecuteAsync(default); } catch (Exception) { } }
    public void RefreshCanExecute() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private static void ReportFault(Exception exception) => System.Diagnostics.Trace.TraceError("AIBar refresh unavailable: {0}", exception.GetType().Name);
}

public sealed class UnavailableQuotaPresentation
{
    public QuotaSummaryCopyCommand CopySummaryCommand { get; } = new(() => null, _ => { });
    public QuotaWindowPresentation Primary { get; } = new("5-hour quota", null, null); public QuotaWindowPresentation Weekly { get; } = new("Weekly quota", null, null);
    public bool IsPrimaryAvailable => false; public bool IsWeeklyAvailable => false; public string FreshnessLabel => "Unavailable"; public string PrivateEndpointDisclosure => "Quota access is unavailable."; public bool IsRefreshAvailable => false;
}
public sealed class QuotaPresentationHost : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly QuotaRefreshCoordinator _coordinator;
    private readonly QuotaPresentationMapper _mapper;
    private readonly Dispatcher _dispatcher;
    private readonly Action<Exception>? _report;
    private readonly Func<bool> _refreshAvailable;
    private readonly ManualRefreshCommand _refreshCommand;
    private readonly ClaudeQuotaPresentationHost? _claude;
    private readonly IQuotaPlanMetadata? _planSource;
    private readonly BetaRuntime? _runtime;
    public QuotaSummaryCopyCommand CopySummaryCommand { get; }
    private readonly IQuotaRefreshLifecycleEvents? _lifecycleEvents;
    private BetaPresentationState _state;
    private QuotaFailure? _reportedFailure;
    private bool _disposed;
    private readonly PanelPaceHistory? _history;
    private long _historyRevision;
    public QuotaPresentationHost(QuotaRefreshCoordinator coordinator, QuotaPresentationMapper mapper, bool refreshAvailable = true, Action<Exception>? report = null, IQuotaRefreshLifecycleEvents? lifecycleEvents = null)
        : this(coordinator, mapper, () => refreshAvailable, report, lifecycleEvents) { }
    public QuotaPresentationHost(QuotaRefreshCoordinator coordinator, QuotaPresentationMapper mapper, Func<bool> refreshAvailable, Action<Exception>? report = null, IQuotaRefreshLifecycleEvents? lifecycleEvents = null, ClaudeQuotaPresentationHost? claude = null, PanelPaceHistory? history = null, IQuotaPlanMetadata? planSource = null, BetaRuntime? runtime = null)
    {
        _runtime = runtime;
        if (_runtime is not null) _runtime.SourceChanged += OnSourceChanged;
        ArgumentNullException.ThrowIfNull(refreshAvailable);
        _planSource = planSource;
        _coordinator = coordinator; _mapper = mapper; _dispatcher = Dispatcher.CurrentDispatcher; _refreshAvailable = refreshAvailable; _state = Map(coordinator.State); _report = report; _lifecycleEvents = lifecycleEvents;
        _claude = claude;
        _history = history;
        if (_history is not null)
        {
            _history.Invalidated += OnHistoryInvalidated;
            _history.PaceRefreshRequested += OnHistoryInvalidated;
        }
        if (_claude is not null) _claude.PropertyChanged += OnClaudeChanged;
        _refreshCommand = new ManualRefreshCommand(RefreshAllAsync, () => IsRefreshAvailable, report);
        CopySummaryCommand = new(() => _disposed ? null : _state, System.Windows.Clipboard.SetText);
        _coordinator.StateChanged += OnStateChanged;
        if (_lifecycleEvents is not null) _lifecycleEvents.ClockChanged += OnClockChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRefreshAvailable => !_disposed && (_refreshAvailable() || _claude?.IsEnabled == true);
    private async ValueTask RefreshAllAsync(CancellationToken token)
    {
        var codex = _refreshAvailable()
            ? (_runtime is not null ? _runtime.RefreshAsync(RefreshTrigger.Manual, token) : _coordinator.RefreshAsync(RefreshTrigger.Manual, token)).AsTask()
            : Task.CompletedTask;
        var claude = _claude?.IsEnabled == true ? _claude.RefreshAsync(token).AsTask() : Task.CompletedTask;
        await Task.WhenAll(codex, claude);
    }
    private void OnClaudeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ClaudeQuotaPresentationHost.IsEnabled)) return;
        _refreshCommand.RefreshCanExecute();
        PropertyChanged?.Invoke(this, new(nameof(IsRefreshAvailable)));
    }
    public IManualRefreshCommand RefreshCommand => _refreshCommand;
    public BetaPresentationState State => _state;
    public QuotaWindowPresentation Primary => _state.Primary;
    public QuotaWindowPresentation Weekly => _state.Weekly;
    public bool IsPrimaryAvailable => _state.IsPrimaryAvailable;
    public bool IsWeeklyAvailable => _state.IsWeeklyAvailable;
    public string FreshnessLabel => _state.FreshnessLabel;
    public string? PrivateEndpointDisclosure => _state.PrivateEndpointDisclosure;
    public async ValueTask InitializeAsync(CancellationToken cancellationToken) => await _coordinator.InitializeAsync(cancellationToken);
    public void RefreshAvailabilityChanged()
    {
        if (_disposed) return;
        Update(_coordinator.State);
        _refreshCommand.RefreshCanExecute();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRefreshAvailable)));
    }
    public void ReportUnavailable() => Update(new(null, FreshnessState.Unavailable, false, new(QuotaErrorKind.Unavailable, "quota_initialization_failed"), null));
    public void RecalculateFromClock()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(RecalculateFromClock); return; }
        Update(_coordinator.State);
    }
    private void OnSourceChanged() => OnStateChanged(_coordinator.State);
    private void OnClockChanged() => RecalculateFromClock();
    private void OnStateChanged(QuotaRefreshState state)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => Update(state)); return; }
        Update(state);
    }
    private void OnHistoryInvalidated() => OnStateChanged(_coordinator.State);
    private void Update(QuotaRefreshState state)
    {
        if (_disposed) return;
        var revision = ++_historyRevision;
        _state = Map(state);
        if (_history is not null && _state.IsCurrent && _refreshAvailable() && _runtime?.IsUsingRemote != true) _ = ApplyHistoryAsync(state, revision);
        CopySummaryCommand.RefreshState();
        if (state.Failure != _reportedFailure)
        {
            _reportedFailure = state.Failure;
            if (state.Failure is not null) _report?.Invoke(new InvalidOperationException(state.Failure.SafeCode));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private async Task ApplyHistoryAsync(QuotaRefreshState state, long revision)
    {
        try
        {
            var historyRevision = _history!.Revision;
            var samples = await _history.ReadPaceAsync(QuotaHistoryProvider.Codex);
            if (_disposed || revision != _historyRevision || !_refreshAvailable() || _runtime?.IsUsingRemote == true || !_history.CanPublish(historyRevision)) return;
            _state = Decorate(_mapper.Map(_runtime?.State ?? state, samples));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
        catch (Exception) { /* History is optional; the snapshot estimate remains visible. */ }
    }
    private BetaPresentationState Map(QuotaRefreshState state)
    {
        // Dispatcher callbacks may have been queued before a source handoff. Always
        // map the current authority, never a captured value from the previous source.
        var mapped = Decorate(_mapper.Map(_runtime?.State ?? state));
        return _refreshAvailable() ? mapped : mapped with
        {
            FreshnessLabel = "Private quota integration disabled",
            ErrorLabel = null,
            IsLoading = false,
            IsFresh = false,
            IsCached = false,
            IsDegraded = false,
            IsMissingCredential = false,
            IsOffline = false,
            IsUnavailable = false,
            IsSafeError = false,
            CachedAge = null,
            CachedAgeLabel = null,
            WarningLabel = null,
            IsPrivateIntegrationDisabled = true,
        };
    }
    private BetaPresentationState Decorate(BetaPresentationState mapped)
    {
        mapped = mapped with { PlanLabel = _refreshAvailable() ? _planSource?.PlanLabel : null };
        if (_runtime is null) return mapped;
        if (!_runtime.IsUsingRemote) return mapped with { SourceLabel = "This PC" };
        var guidance = mapped.NeedsLogin
            ? "Open Codex on the other machine to renew the session"
            : mapped.IsUnavailable || mapped.ShowStaleBanner
                ? "Usage from the other machine is stale or unavailable; check the helper on the other machine"
                : mapped.WarningLabel;
        return mapped with
        {
            SourceLabel = "Other machine",
            WarningLabel = guidance,
            ErrorLabel = guidance,
            PrivateEndpointDisclosure = "Codex quota is read from a usage file supplied by the other machine; no local credentials are read."
        };
    }

    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            CopySummaryCommand.RefreshState();
            _coordinator.StateChanged -= OnStateChanged;
            if (_runtime is not null) _runtime.SourceChanged -= OnSourceChanged;
            if (_history is not null)
            {
                _history.Invalidated -= OnHistoryInvalidated;
                _history.PaceRefreshRequested -= OnHistoryInvalidated;
            }
            if (_claude is not null) { _claude.PropertyChanged -= OnClaudeChanged; _claude.Dispose(); }
            if (_lifecycleEvents is not null) _lifecycleEvents.ClockChanged -= OnClockChanged;
        }
        return ValueTask.CompletedTask;
    }
}
