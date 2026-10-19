using System.ComponentModel;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Dispatcher-owned incident text. Public status failures never become quota errors.</summary>
public sealed class ProviderStatusPresentation : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly INotifyPropertyChanged _quota;
    private readonly INotifyPropertyChanged _claude;
    private readonly Func<bool> _codexEnabled;
    private readonly Func<bool> _claudeEnabled;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _timer;
    private readonly ProviderStatusClient _client = new();
    private readonly ProviderStatusPoller _poller;
    private Task _refresh = Task.CompletedTask;
    private bool _requested, _disposed, _started;
    public ProviderStatusPresentation(QuotaPresentationHost quota, ClaudeQuotaPresentationHost claude, Func<bool> codexEnabled)
        : this(quota, claude, codexEnabled, () => claude.IsEnabled, null) { }
    internal ProviderStatusPresentation(INotifyPropertyChanged quota, INotifyPropertyChanged claude,
        Func<bool> codexEnabled, Func<bool> claudeEnabled,
        Func<StatusProvider, CancellationToken, Task<ProviderStatus?>>? fetch)
    {
        _quota = quota; _claude = claude; _codexEnabled = codexEnabled; _claudeEnabled = claudeEnabled;
        _poller = new(fetch ?? _client.FetchAsync);
        _poller.Changed += OnStatus;
        _quota.PropertyChanged += OnConsent;
        _claude.PropertyChanged += OnConsent;
        _timer = new DispatcherTimer { Interval = ProviderStatusPoller.Cadence };
        _timer.Tick += OnTick;
    }
    public void Start()
    {
        if (_disposed || _started) return;
        _started = true;
        _timer.Start();
        RequestRefresh();
    }
    public string? CodexDescription { get; private set; }
    public string? ClaudeDescription { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnTick(object? sender, EventArgs args) => RequestRefresh();
    private void OnConsent(object? sender, PropertyChangedEventArgs args) => RequestRefresh();
    private void RequestRefresh()
    {
        if (_disposed || !_started) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(RequestRefresh); return; }
        _poller.SetEnabled(StatusProvider.Codex, _codexEnabled());
        _poller.SetEnabled(StatusProvider.Claude, _claudeEnabled());
        _requested = true;
        if (_refresh.IsCompleted) _refresh = RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        while (_requested && !_disposed)
        {
            _requested = false;
            await _poller.RefreshAsync();
        }
        if (!_disposed)
        {
            _timer.Stop();
            _timer.Interval = _poller.NextPollDelay;
            _timer.Start();
        }
    }
    private void OnStatus(StatusProvider provider, ProviderStatus? status)
    {
        // Queue even on the dispatcher: never invoke presentation/consent callbacks under the poller lock.
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            var enabled = provider == StatusProvider.Codex ? _codexEnabled() : _claudeEnabled();
            var description = enabled && status?.HasIncident == true ? status.Description : null;
            if (provider == StatusProvider.Codex) CodexDescription = description; else ClaudeDescription = description;
            PropertyChanged?.Invoke(this, new(provider == StatusProvider.Codex ? nameof(CodexDescription) : nameof(ClaudeDescription)));
        });
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _quota.PropertyChanged -= OnConsent;
        _claude.PropertyChanged -= OnConsent;
        _poller.Dispose();
        await _refresh;
        _poller.Changed -= OnStatus;
        _client.Dispose();
        CodexDescription = ClaudeDescription = null;
    }
}
