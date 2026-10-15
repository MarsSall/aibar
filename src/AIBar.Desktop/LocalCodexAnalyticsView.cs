using System.ComponentModel;
using AIBar.Application;

namespace AIBar.Desktop;

public sealed class LocalCodexAnalyticsView : INotifyPropertyChanged, IAsyncDisposable
{
    private bool _disposed;

    public LocalCodexAnalyticsView(LocalAnalyticsState? initial = null)
    {
        State = initial ?? LocalAnalyticsState.Unavailable(DateTimeOffset.UtcNow, ["local_scan_not_started"]);
    }

    public LocalAnalyticsState State { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<LocalAnalyticsState>? StateChanged;

    internal void ShowLoading() => Publish(State.WithStatus(AnalyticsScanStatus.Loading));
    internal void ResetAfterClear() => Publish(LocalAnalyticsState.Unavailable(DateTimeOffset.UtcNow, ["local_scan_not_started"]));
    internal void Publish(LocalAnalyticsState state)
    {
        if (_disposed) return;
        State = state;
        StateChanged?.Invoke(state);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await Task.CompletedTask;
    }
}

public enum AnalyticsShutdownKind { Completed, TimedOut, Failed }
public sealed record AnalyticsShutdownOutcome(AnalyticsShutdownKind Kind, string SafeCode, Exception? FirstFailure = null);

public sealed class AnalyticsLifecycleOwner : ILocalAnalyticsLifecycle, IAiBarClearWork, IAsyncDisposable
{
    private readonly ILocalCodexAnalyticsScanner _scanner; private readonly LocalCodexAnalyticsView _view; private readonly IAsyncDisposable _store;
    private readonly TimeSpan _shutdownBound; private readonly Action<string>? _observe; private CancellationTokenSource? _cancellation; private Task? _scan;
    private long _generation; private bool _publicationClosed; private bool _dependenciesDisposed; private AnalyticsShutdownOutcome? _outcome;
    private readonly Action<string>? _scanTiming;
    public AnalyticsLifecycleOwner(ILocalCodexAnalyticsScanner scanner, LocalCodexAnalyticsView view, IAsyncDisposable store, TimeSpan shutdownBound, Action<string>? observe = null, Action<string>? scanTiming = null)
    { _scanner = scanner; _view = view; _store = store; _shutdownBound = shutdownBound; _observe = observe; _scanTiming = scanTiming; }
    private void MarkScan(long generation, string stage) => _scanTiming?.Invoke($"scan-{generation}:{stage}");
    public AnalyticsShutdownOutcome Outcome => _outcome ?? new(AnalyticsShutdownKind.Completed, "analytics_shutdown_completed");

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (_dependenciesDisposed || _scan is not null || _outcome is not null) return ValueTask.CompletedTask;
        _publicationClosed = false; _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _view.ShowLoading();
        var generation = ++_generation;
        MarkScan(generation, "queued");
        _scan = Task.Run(() => ScanAsync(generation)); return ValueTask.CompletedTask;
    }
    public async ValueTask StopAsync(CancellationToken cancellationToken) => await EndAsync(cancellationToken, false);
    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        await EndAsync(cancellationToken, false);
        if (_outcome is { Kind: AnalyticsShutdownKind.TimedOut } timedOut) throw new TimeoutException(timedOut.SafeCode);
        if (_outcome is { Kind: AnalyticsShutdownKind.Failed } failed) throw new InvalidOperationException(failed.SafeCode, failed.FirstFailure);
    }
    public void ResumeAfterClear() { }
    public async ValueTask DisposeAsync() => await EndAsync(CancellationToken.None, true);

    private async ValueTask EndAsync(CancellationToken cancellationToken, bool disposeDependencies)
    {
        if (_outcome is not null) { if (disposeDependencies && _outcome.Kind == AnalyticsShutdownKind.Completed) await DisposeDependenciesAsync(); return; }
        _publicationClosed = true; ++_generation; var scan = _scan; _cancellation?.Cancel(); _observe?.Invoke("analytics_cancelled");
        if (scan is not null)
            try { _observe?.Invoke("analytics_awaited"); await scan.WaitAsync(_shutdownBound, cancellationToken); }
            catch (TimeoutException) { _outcome = new(AnalyticsShutdownKind.TimedOut, "analytics_shutdown_timed_out"); _observe?.Invoke("analytics_timed_out"); return; }
            catch (Exception exception) { _outcome = new(AnalyticsShutdownKind.Failed, "analytics_shutdown_failed", exception); _observe?.Invoke("analytics_failed"); return; }
        _outcome = new(AnalyticsShutdownKind.Completed, "analytics_shutdown_completed");
        if (disposeDependencies) await DisposeDependenciesAsync();
        else ResetStoppedGeneration();
    }
    private void ResetStoppedGeneration()
    {
        _scan = null; _outcome = null; _cancellation?.Dispose(); _cancellation = null;
    }
    private async Task ScanAsync(long generation)
    {
        try
        {
            MarkScan(generation, "worker_entry");
            var result = await _scanner.ScanAsync(_cancellation!.Token);
            MarkScan(generation, "scanner_return");
            if (!_publicationClosed && !_cancellation.IsCancellationRequested && generation == _generation)
            {
                MarkScan(generation, "publication_begin");
                _view.Publish(result);
                MarkScan(generation, "publication_end");
            }
        }
        catch (OperationCanceledException) when (_cancellation!.IsCancellationRequested) { MarkScan(generation, "cancelled"); }
        catch (LocalCodexAnalyticsScanException exception) { MarkScan(generation, "scan_exception"); if (!_publicationClosed) _view.Publish(LocalAnalyticsState.Failed(DateTimeOffset.UtcNow, exception.SafeCode)); throw; }
        catch (Exception) { MarkScan(generation, "exception"); if (!_publicationClosed) _view.Publish(LocalAnalyticsState.Failed(DateTimeOffset.UtcNow)); throw; }
        finally
        {
            MarkScan(generation, "finish_observer_begin");
            _observe?.Invoke("analytics_scan_finished");
            MarkScan(generation, "finish_observer_end");
        }
    }
    private async ValueTask DisposeDependenciesAsync()
    {
        if (_dependenciesDisposed) return; _dependenciesDisposed = true;
        await _view.DisposeAsync(); _observe?.Invoke("analytics_view_disposed"); await _store.DisposeAsync(); _observe?.Invoke("analytics_store_disposed"); _cancellation?.Dispose();
    }
}

public sealed class BetaAnalyticsPresentation : INotifyPropertyChanged
{
    private readonly QuotaPresentationHost _quota;
    private readonly LocalCodexAnalyticsView _analytics;
    private readonly LocalUsagePresentationHost _localUsage;

    public BetaAnalyticsPresentation(QuotaPresentationHost quota, LocalCodexAnalyticsView analytics, LocalUsagePresentationHost localUsage)
    {
        _quota = quota; _analytics = analytics; _localUsage = localUsage;
        _quota.PropertyChanged += OnQuotaChanged;
        _analytics.PropertyChanged += OnAnalyticsChanged;
        _localUsage.PropertyChanged += OnLocalUsageChanged;
    }

    public QuotaWindowPresentation Primary => _quota.Primary;
    public QuotaWindowPresentation Weekly => _quota.Weekly;
    public string FreshnessLabel => _quota.FreshnessLabel;
    public string? PrivateEndpointDisclosure => _quota.PrivateEndpointDisclosure;
    public bool IsRefreshAvailable => _quota.IsRefreshAvailable;
    public IManualRefreshCommand RefreshCommand => _quota.RefreshCommand;
    public QuotaSummaryCopyCommand CopySummaryCommand => _quota.CopySummaryCommand;
    public BetaPresentationState State => _quota.State;
    public LocalAnalyticsState Analytics => _analytics.State;
    public LocalUsagePresentationState LocalUsage => _localUsage.State;
    public QuotaPresentationHost QuotaPresentation => _quota;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnQuotaChanged(object? sender, PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    private void OnAnalyticsChanged(object? sender, PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Analytics)));
    private void OnLocalUsageChanged(object? sender, PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocalUsage)));
}
