using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Read-only history access. Clear invalidates in-flight readers and their UI results.</summary>
public sealed class PanelPaceHistory(IQuotaHistoryStore store, IClock clock) : IAiBarClearWork, IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private CancellationTokenSource _epoch = new();
    private volatile bool _paused;
    public event Action? Invalidated;
    public event Action? PaceRefreshRequested;
    public void RefreshPace()
    {
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(RefreshPace); return; }
        if (!_paused) PaceRefreshRequested?.Invoke();
    }
    public DateTimeOffset Now => clock.UtcNow;

    public async Task<IReadOnlyList<QuotaHistorySample>> ReadPaceAsync(QuotaHistoryProvider provider)
    {
        var token = _epoch.Token;
        if (_paused) return [];
        var now = Now;
        var samples = await Task.Run(async () =>
        {
            var primary = await store.QueryAsync(provider, QuotaHistoryWindow.FiveHour, now.AddHours(-5), now, token);
            var weekly = await store.QueryAsync(provider, QuotaHistoryWindow.Weekly, now.AddDays(-7), now, token);
            return primary.Concat(weekly).ToArray();
        }, token);
        token.ThrowIfCancellationRequested();
        return samples;
    }

    public async Task<QuotaHistoryChartData> ReadChartAsync(QuotaHistoryChartRange range, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_epoch.Token, cancellationToken);
        if (_paused) throw new OperationCanceledException();
        var result = await Task.Run(() => QuotaHistoryChartData.LoadAsync(store, range, Now, linked.Token), linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        return result;
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paused = true;
        _epoch.Cancel();
        if (_dispatcher.CheckAccess()) Invalidated?.Invoke();
        else await _dispatcher.InvokeAsync(() => Invalidated?.Invoke());
    }
    public void ResumeAfterClear()
    {
        _epoch.Dispose(); _epoch = new(); _paused = false;
    }
    public void Dispose()
    {
        _paused = true; _epoch.Cancel(); _epoch.Dispose();
        Invalidated = null; PaceRefreshRequested = null;
    }
}
