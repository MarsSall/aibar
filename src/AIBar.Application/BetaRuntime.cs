using AIBar.Domain;

namespace AIBar.Application;

public interface ILocalAnalyticsLifecycle
{
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}

public sealed class BetaRuntime : IAsyncDisposable
{
    private readonly ConsentSettings _settings;
    private readonly PrivateIntegrationPolicy _policy;
    private readonly QuotaRefreshCoordinator _coordinator;
    private readonly IClock _clock;
    private readonly ConsentCredentialSource? _credentials;
    private readonly ILocalAnalyticsLifecycle? _analytics;
    private readonly QuotaExportPublisher? _publisher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PeriodicTimer _pollTimer;
    private readonly SemaphoreSlim _consentGate = new(1, 1);
    private Task? _polling;
    private bool _disposed;

    public BetaRuntime(ConsentSettings settings, PrivateIntegrationPolicy policy, QuotaRefreshCoordinator coordinator, IClock clock, ConsentCredentialSource? credentials = null, ILocalAnalyticsLifecycle? analytics = null, QuotaExportPublisher? publisher = null, TimeProvider? timeProvider = null)
    {
        _settings = settings; _policy = policy; _coordinator = coordinator; _clock = clock; _credentials = credentials; _analytics = analytics; _publisher = publisher;
        _pollTimer = new(TimeSpan.FromMinutes(5), timeProvider ?? TimeProvider.System);
    }

    public void SetRefreshMinutes(int minutes)
    {
        if (!AppearanceSettings.ValidRefreshMinutes(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (_disposed) return;
        var interval = TimeSpan.FromMinutes(minutes);
        _coordinator.SetMinimumPollInterval(interval);
        _pollTimer.Period = interval;
    }

    public string Disclosure => _policy.Disclosure;
    public bool IsConsentEnabled => _policy.IsEnabled;
    public CredentialAvailability CredentialAvailability { get; private set; } = CredentialAvailability.Disabled;
    private volatile DataSource _source = DataSource.Automatic;
    public DataSource Source => _source;
    public bool IsRemoteOnly => Source == DataSource.OtherMachine;
    // Used both when admitting an alert and again at queued tray delivery.
    public bool CanDeliverAlerts => _policy.IsEnabled && !IsRemoteOnly &&
        State.Freshness == FreshnessState.Current && State.Failure is null;
    public event Action? SourceChanged;
    public QuotaRefreshState State => IsRemoteOnly
        ? new(null, FreshnessState.Unavailable, false, new(QuotaErrorKind.Unavailable, "quota_other_machine_pending"), null)
        : _coordinator.State;

    public async ValueTask SetSourceAsync(DataSource source, CancellationToken cancellationToken)
    {
        _ = source.ToClaude(); // Validate before writing or cancelling anything.
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_source == source) return;
            await _settings.SaveCodexSourceAsync(source, cancellationToken);
            _source = source;
            // Drain any local request before completing the handoff; direct coordinator
            // callers (manual refresh and lifecycle) must also remain paused remotely.
            await _coordinator.CancelAndWaitAsync(CancellationToken.None);
            if (IsRemoteOnly)
            {
                if (_publisher is not null && _policy.IsEnabled) await _publisher.SuspendForSourceAsync(CancellationToken.None);
                if (_analytics is not null) await _analytics.StopAsync(CancellationToken.None);
            }
            else if (_policy.IsEnabled)
            {
                if (_publisher is not null) await _publisher.RestoreAsync(CancellationToken.None);
                _coordinator.ResumeAfterClear();
                if (_analytics is not null) await _analytics.StartAsync(_lifetime.Token);
            }
            SourceChanged?.Invoke();
            if (!IsRemoteOnly && _policy.IsEnabled)
                await RefreshAsync(RefreshTrigger.Manual, CancellationToken.None);
        }
        finally { _consentGate.Release(); }
    }
    public TimeSpan? CachedAge => State.Snapshot is { } snapshot ? _clock.UtcNow - snapshot.RetrievedAt : null;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _source = await _settings.LoadCodexSourceAsync(cancellationToken);
        if (IsRemoteOnly) await _coordinator.CancelAndWaitAsync(cancellationToken);
        var enabled = await _settings.LoadAsync(cancellationToken);
        if (_publisher is not null) await (enabled
            ? IsRemoteOnly ? _publisher.SuspendForSourceAsync(cancellationToken) : _publisher.RestoreAsync(cancellationToken)
            : _publisher.DisableAsync(cancellationToken));
        if (enabled) _policy.Enable(); else _policy.Disable();
        CredentialAvailability = _policy.IsEnabled ? CredentialAvailability.Missing : CredentialAvailability.Disabled;
        await _coordinator.InitializeAsync(cancellationToken);
        if (_policy.IsEnabled)
        {
            _polling = PollAsync(_lifetime.Token);
            _ = RefreshAsync(RefreshTrigger.Poll, _lifetime.Token).AsTask();
            if (_analytics is not null && !IsRemoteOnly) await _analytics.StartAsync(_lifetime.Token);
        }
    }

    public async ValueTask GrantConsentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_policy.IsEnabled) return;
            await GrantConsentCoreAsync(cancellationToken);
        }
        finally { _consentGate.Release(); }
    }

    private async Task GrantConsentCoreAsync(CancellationToken cancellationToken)
    {
        if (_publisher is not null) await _publisher.EnableAsync(cancellationToken);
        await _settings.SaveAsync(true, cancellationToken);
        _policy.Enable();
        if (IsRemoteOnly)
        {
            await _coordinator.CancelAndWaitAsync(cancellationToken);
            if (_publisher is not null) await _publisher.SuspendForSourceAsync(cancellationToken);
        }
        else _coordinator.ResumeAfterClear();
        _polling ??= PollAsync(_lifetime.Token);
        if (_analytics is not null && !IsRemoteOnly) await _analytics.StartAsync(_lifetime.Token);
        await RefreshAsync(RefreshTrigger.Manual, cancellationToken);
    }

    public async ValueTask RevokeConsentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_publisher is not null) await _publisher.DisableAsync(cancellationToken);
            await _settings.SaveAsync(false, cancellationToken);
            _policy.Disable();
            CredentialAvailability = CredentialAvailability.Disabled;
            try { if (_analytics is not null) await _analytics.StopAsync(cancellationToken); }
            finally { await _coordinator.CancelAndWaitAsync(cancellationToken); }
        }
        finally { _consentGate.Release(); }
    }

    public async ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_policy.IsEnabled || IsRemoteOnly) return;
        await _coordinator.ReevaluateAsync(trigger, cancellationToken);
        CredentialAvailability = _credentials?.LastAvailability ?? State.Failure?.SafeCode switch
        {
            "quota_credential_missing" => CredentialAvailability.Missing,
            "quota_credential_unusable" => CredentialAvailability.Unusable,
            _ => CredentialAvailability,
        };
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _pollTimer.WaitForNextTickAsync(cancellationToken))
                await RefreshAsync(RefreshTrigger.Poll, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_publisher is not null) await _publisher.DisposeAsync(); }
        finally
        {
            _lifetime.Cancel();
            try { await _coordinator.CancelAndWaitAsync(CancellationToken.None); } catch (ObjectDisposedException) { }
            _pollTimer.Dispose(); if (_polling is not null) await _polling; await _coordinator.DisposeAsync(); _lifetime.Dispose();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
