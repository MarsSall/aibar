using System.ComponentModel;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Maps opt-in Claude panel state without exposing credential or provider details.</summary>
public sealed class ClaudeQuotaPresentationMapper
{
    private readonly QuotaPresentationMapper _mapper;
    public ClaudeQuotaPresentationMapper(IClock clock) => _mapper = new(clock);

    // RED seam: retain local behavior until source-aware guidance is implemented.
    public BetaPresentationState Map(QuotaRefreshState state, bool consentEnabled, ClaudeQuotaSource source) =>
        Map(state, consentEnabled);

    public BetaPresentationState Map(QuotaRefreshState state, bool consentEnabled)
    {
        if (!consentEnabled)
            return _mapper.Map(new(null, FreshnessState.Unavailable, false, null, null)) with
            { IsPrivateIntegrationDisabled = true, IsUnavailable = false };
        var mapped = _mapper.Map(state);
        var needsLogin = mapped.IsMissingCredential || state.Failure?.Kind == QuotaErrorKind.Authentication;
        var warning = needsLogin ? "Open Claude Code on this PC to renew the session" : mapped.WarningLabel;
        return mapped with { WarningLabel = warning, ErrorLabel = warning };
    }
}

/// <summary>Dispatcher-owned view of the independently consented Claude runtime.</summary>
public sealed class ClaudeQuotaPresentationHost : INotifyPropertyChanged, IDisposable
{
    private readonly ClaudeQuotaRuntime _runtime;
    private readonly ClaudeQuotaPresentationMapper _mapper;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;
    public ClaudeQuotaPresentationHost(ClaudeQuotaRuntime runtime, ClaudeQuotaPresentationMapper mapper)
    {
        _runtime = runtime; _mapper = mapper;
        IsEnabled = runtime.IsConsentEnabled;
        State = mapper.Map(runtime.State, IsEnabled);
        runtime.StateChanged += OnChanged;
        runtime.ConsentChanged += OnChanged;
    }
    public bool IsEnabled { get; private set; }
    public BetaPresentationState State { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ValueTask RefreshAsync(CancellationToken token) => _runtime.RefreshAsync(RefreshTrigger.Manual, token);
    private void OnChanged()
    {
        if (_disposed) return;
        if (!_dispatcher.CheckAccess()) { _dispatcher.BeginInvoke(OnChanged); return; }
        var enabled = _runtime.IsConsentEnabled;
        var changed = enabled != IsEnabled;
        IsEnabled = enabled;
        State = _mapper.Map(_runtime.State, enabled);
        PropertyChanged?.Invoke(this, new(nameof(State)));
        if (changed) PropertyChanged?.Invoke(this, new(nameof(IsEnabled)));
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.StateChanged -= OnChanged;
        _runtime.ConsentChanged -= OnChanged;
    }
}
