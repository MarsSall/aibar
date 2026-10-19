using System.ComponentModel;
using System.Windows.Threading;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Maps opt-in Claude panel state without exposing credential or provider details.</summary>
public sealed class ClaudeQuotaPresentationMapper
{
    private readonly QuotaPresentationMapper _mapper;
    public ClaudeQuotaPresentationMapper(IClock clock) => _mapper = new(clock, QuotaHistoryProvider.Claude);

    public BetaPresentationState Map(QuotaRefreshState state, bool consentEnabled) =>
        Map(state, consentEnabled, ClaudeQuotaSource.ThisPc);

    public BetaPresentationState Map(QuotaRefreshState state, bool consentEnabled, ClaudeQuotaSource source) => Map(state, consentEnabled, source, []);

    public BetaPresentationState Map(QuotaRefreshState state, bool consentEnabled, ClaudeQuotaSource source, IReadOnlyList<QuotaHistorySample> history)
    {
        if (!consentEnabled)
            return _mapper.Map(new(null, FreshnessState.Unavailable, false, null, null)) with
            { IsPrivateIntegrationDisabled = true, IsUnavailable = false };
        var mapped = _mapper.Map(state, history);
        var needsLogin = mapped.IsMissingCredential || state.Failure?.Kind == QuotaErrorKind.Authentication;
        var warning = needsLogin
            ? source == ClaudeQuotaSource.OtherMachine
                ? "Open Claude Code on the other machine to renew the session"
                : "Open Claude Code on this PC to renew the session"
            : source == ClaudeQuotaSource.OtherMachine && state.Failure is not null
                ? "Usage from the other machine is stale or unavailable; check the helper on that machine"
                : mapped.WarningLabel;
        return mapped with
        {
            Primary = mapped.Primary with { Pace = mapped.Primary.Pace?.ForProvider("Claude") },
            Weekly = mapped.Weekly with { Pace = mapped.Weekly.Pace?.ForProvider("Claude") },
            WarningLabel = warning, ErrorLabel = warning,
            SourceLabel = source == ClaudeQuotaSource.OtherMachine ? "Other machine" : "This PC",
            PrivateEndpointDisclosure = source == ClaudeQuotaSource.OtherMachine
                ? "Claude quota is read from a usage file supplied by the other machine; no local credentials are read."
                : mapped.PrivateEndpointDisclosure,
            Disclosure = ClaudeQuotaRuntime.ConsentDisclosure
        };
    }
}

/// <summary>Dispatcher-owned view of the independently consented Claude runtime.</summary>
public sealed class ClaudeQuotaPresentationHost : INotifyPropertyChanged, IDisposable
{
    private readonly ClaudeQuotaRuntime _runtime;
    private readonly ClaudeQuotaPresentationMapper _mapper;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _disposed;
    private readonly PanelPaceHistory? _history;
    private long _historyRevision;
    public ClaudeQuotaPresentationHost(ClaudeQuotaRuntime runtime, ClaudeQuotaPresentationMapper mapper, PanelPaceHistory? history = null)
    {
        _history = history;
        if (_history is not null)
        {
            _history.Invalidated += OnChanged;
            _history.PaceRefreshRequested += OnChanged;
        }
        _runtime = runtime; _mapper = mapper;
        IsEnabled = runtime.IsConsentEnabled;
        State = mapper.Map(runtime.State, IsEnabled, runtime.ResolvedSource);
        runtime.StateChanged += OnChanged;
        runtime.ConsentChanged += OnChanged;
        runtime.SourceChanged += OnChanged;
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
        var state = _runtime.State;
        var source = _runtime.ResolvedSource;
        var revision = ++_historyRevision;
        State = _mapper.Map(state, enabled, source);
        if (_history is not null && enabled && State.IsCurrent) _ = ApplyHistoryAsync(state, source, revision);
        PropertyChanged?.Invoke(this, new(nameof(State)));
        if (changed) PropertyChanged?.Invoke(this, new(nameof(IsEnabled)));
    }
    private async Task ApplyHistoryAsync(QuotaRefreshState state, ClaudeQuotaSource source, long revision)
    {
        try
        {
            var historyRevision = _history!.Revision;
            var samples = await _history.ReadPaceAsync(QuotaHistoryProvider.Claude);
            if (_disposed || revision != _historyRevision || !IsEnabled || !_history.CanPublish(historyRevision)) return;
            State = _mapper.Map(state, true, source, samples);
            PropertyChanged?.Invoke(this, new(nameof(State)));
        }
        catch (Exception) { /* Keep the snapshot fallback if history is unavailable. */ }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_history is not null)
        {
            _history.Invalidated -= OnChanged;
            _history.PaceRefreshRequested -= OnChanged;
        }
        _runtime.StateChanged -= OnChanged;
        _runtime.ConsentChanged -= OnChanged;
        _runtime.SourceChanged -= OnChanged;
    }
}
