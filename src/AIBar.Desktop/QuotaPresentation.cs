using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;
namespace AIBar.Desktop;
public sealed record QuotaWindowPresentation(string Label, decimal? PercentageUsed, string? ResetCountdown);

public sealed record QuotaPresentationState(
    QuotaWindowPresentation Primary,
    QuotaWindowPresentation Weekly,
    bool IsCurrent,
    string FreshnessLabel,
    string? ErrorLabel,
    DateTimeOffset? RetrievedAt,
    string QuotaDisclosure,
    string AnalyticsDisclosure,
    string CostDisclosure,
    string PrivateEndpointDisclosure);

public sealed class QuotaPresentationMapper(IClock clock)
{
    public QuotaPresentationState Map(QuotaRefreshState state)
    {
        var snapshot = state.Snapshot;
        var freshness = state.IsLoading
            ? "Loading"
            : snapshot is not null && state.Failure is not null ? "Stale" : state.Freshness.ToString();
        return new(
            Window("5-hour quota", snapshot?.Primary),
            Window("Weekly quota", snapshot?.Weekly),
            snapshot is not null && state.Freshness == FreshnessState.Current && !state.IsLoading && state.Failure is null,
            freshness,
            ErrorLabel(state.Failure),
            snapshot?.RetrievedAt,
            "Private service-reported quota",
            "Locally derived analytics",
            "Estimated cost; not billed cost.",
            "Quota access uses a private, undocumented, unsupported endpoint.");
    }

    private QuotaWindowPresentation Window(string label, QuotaWindow? window) => new(
        label,
        window?.PercentageUsed,
        window is null ? null : FormatCountdown(window.CountdownAt(clock.UtcNow)));

    private static string FormatCountdown(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private static string? ErrorLabel(QuotaFailure? failure) => failure?.Kind switch
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
    private static void ReportFault(Exception exception) => System.Diagnostics.Trace.TraceError("AIBar refresh unavailable: {0}", exception.GetType().Name);
}

public sealed class UnavailableQuotaPresentation
{
    public QuotaWindowPresentation Primary { get; } = new("5-hour quota", null, null); public QuotaWindowPresentation Weekly { get; } = new("Weekly quota", null, null);
    public string FreshnessLabel => "Unavailable"; public string PrivateEndpointDisclosure => "Quota access is unavailable."; public bool IsRefreshAvailable => false;
}
public sealed class QuotaPresentationHost : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly QuotaRefreshCoordinator _coordinator;
    private readonly QuotaPresentationMapper _mapper;
    private readonly Dispatcher _dispatcher;
    private readonly Action<Exception>? _report;
    private QuotaPresentationState _state;
    private QuotaFailure? _reportedFailure;
    private bool _disposed;
    public QuotaPresentationHost(QuotaRefreshCoordinator coordinator, QuotaPresentationMapper mapper, bool refreshAvailable = true, Action<Exception>? report = null)
    {
        _coordinator = coordinator; _mapper = mapper; _dispatcher = Dispatcher.CurrentDispatcher; _state = mapper.Map(coordinator.State); _report = report;
        IsRefreshAvailable = refreshAvailable;
        RefreshCommand = new ManualRefreshCommand(token => coordinator.RefreshAsync(RefreshTrigger.Manual, token), () => !_disposed && refreshAvailable, report);
        _coordinator.StateChanged += OnStateChanged;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool IsRefreshAvailable { get; }
    public IManualRefreshCommand RefreshCommand { get; }
    public QuotaWindowPresentation Primary => _state.Primary;
    public QuotaWindowPresentation Weekly => _state.Weekly;
    public string FreshnessLabel => _state.FreshnessLabel;
    public string? PrivateEndpointDisclosure => _state.PrivateEndpointDisclosure;
    public async ValueTask InitializeAsync(CancellationToken cancellationToken) => await _coordinator.InitializeAsync(cancellationToken);
    public void ReportUnavailable() => Update(new(null, FreshnessState.Unavailable, false, new(QuotaErrorKind.Unavailable, "quota_initialization_failed"), null));
    private void OnStateChanged(QuotaRefreshState state)
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(() => Update(state)); return; }
        Update(state);
    }
    private void Update(QuotaRefreshState state)
    {
        _state = _mapper.Map(state);
        if (state.Failure != _reportedFailure)
        {
            _reportedFailure = state.Failure;
            if (state.Failure is not null) _report?.Invoke(new InvalidOperationException(state.Failure.SafeCode));
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public ValueTask DisposeAsync()
    {
        if (!_disposed) { _disposed = true; _coordinator.StateChanged -= OnStateChanged; }
        return ValueTask.CompletedTask;
    }
}
