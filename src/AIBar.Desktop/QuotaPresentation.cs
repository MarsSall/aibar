using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;
namespace AIBar.Desktop;
public sealed record QuotaWindowPresentation(string Label, decimal? PercentageUsed, string? ResetCountdown);

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
    bool IsPrivateIntegrationDisabled = false);

public sealed class QuotaPresentationMapper(IClock clock)
{
    public BetaPresentationState Map(QuotaRefreshState state)
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
            Window("5-hour quota", snapshot?.Primary),
            Window("Weekly quota", snapshot?.Weekly),
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
            "Private quota access is optional, disabled by default, and can be revoked at any time.");
    }

    private QuotaWindowPresentation Window(string label, QuotaWindow? window) => new(
        label,
        window?.PercentageUsed,
        window is null ? null : FormatCountdown(window.CountdownAt(clock.UtcNow)));

    private static string FormatCountdown(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static TimeSpan NonNegative(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
    private static string? WarningLabel(QuotaFailure? failure, bool isMissingCredential, bool isOffline) =>
        isMissingCredential ? "Credential unavailable" : isOffline ? "Network unavailable" : failure?.Kind switch
    {
        QuotaErrorKind.Authentication => "Authentication required",
        QuotaErrorKind.Permission => "Permission denied",
        QuotaErrorKind.MalformedResponse => "Service response unavailable",
        QuotaErrorKind.Network => "Network unavailable",
        QuotaErrorKind.Service => "Service unavailable",
        QuotaErrorKind.Unavailable or QuotaErrorKind.Redirect => "Quota unavailable",
        _ => null
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
    private readonly IQuotaRefreshLifecycleEvents? _lifecycleEvents;
    private BetaPresentationState _state;
    private QuotaFailure? _reportedFailure;
    private bool _disposed;
    public QuotaPresentationHost(QuotaRefreshCoordinator coordinator, QuotaPresentationMapper mapper, bool refreshAvailable = true, Action<Exception>? report = null, IQuotaRefreshLifecycleEvents? lifecycleEvents = null)
        : this(coordinator, mapper, () => refreshAvailable, report, lifecycleEvents) { }
    public QuotaPresentationHost(QuotaRefreshCoordinator coordinator, QuotaPresentationMapper mapper, Func<bool> refreshAvailable, Action<Exception>? report = null, IQuotaRefreshLifecycleEvents? lifecycleEvents = null)
    {
        ArgumentNullException.ThrowIfNull(refreshAvailable);
        _coordinator = coordinator; _mapper = mapper; _dispatcher = Dispatcher.CurrentDispatcher; _refreshAvailable = refreshAvailable; _state = Map(coordinator.State); _report = report; _lifecycleEvents = lifecycleEvents;
        _refreshCommand = new ManualRefreshCommand(token => coordinator.RefreshAsync(RefreshTrigger.Manual, token), () => IsRefreshAvailable, report);
        _coordinator.StateChanged += OnStateChanged;
        if (_lifecycleEvents is not null) _lifecycleEvents.ClockChanged += OnClockChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRefreshAvailable => !_disposed && _refreshAvailable();
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
    private void OnClockChanged() => RecalculateFromClock();
    private void OnStateChanged(QuotaRefreshState state)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => Update(state)); return; }
        Update(state);
    }
    private void Update(QuotaRefreshState state)
    {
        _state = Map(state);
        if (state.Failure != _reportedFailure)
        {
            _reportedFailure = state.Failure;
            if (state.Failure is not null) _report?.Invoke(new InvalidOperationException(state.Failure.SafeCode));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    private BetaPresentationState Map(QuotaRefreshState state)
    {
        var mapped = _mapper.Map(state);
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
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _coordinator.StateChanged -= OnStateChanged;
            if (_lifecycleEvents is not null) _lifecycleEvents.ClockChanged -= OnClockChanged;
        }
        return ValueTask.CompletedTask;
    }
}
