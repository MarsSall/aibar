using AIBar.Domain;

namespace AIBar.Application;

public interface ILocalAnalyticsLifecycle
{
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}

public sealed class BetaRuntime : IAsyncDisposable, IQuotaPlanMetadata
{
    private readonly ConsentSettings _settings;
    private readonly PrivateIntegrationPolicy _policy;
    private readonly QuotaRefreshCoordinator _coordinator;
    private readonly IClock _clock;
    private readonly ConsentCredentialSource? _credentials;
    private readonly ILocalAnalyticsLifecycle? _analytics;
    private readonly QuotaExportPublisher? _publisher;
    private readonly CodexSourceProvider _provider;
    private readonly Func<bool> _remoteFileAvailable;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _remoteAbsenceConfirmation;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PeriodicTimer _pollTimer;
    private readonly SemaphoreSlim _consentGate = new(1, 1);
    private readonly object _deadlineGate = new();
    private ITimer? _deadline;
    private long _deadlineGeneration;
    private long _sourceEpoch;
    private Task? _polling;
    private int _refreshMinutes = 5;
    private volatile bool _resolvedRemote;
    private volatile bool _handoff;
    private bool _initialized;
    private bool _disposed;
    private volatile DataSource _source = DataSource.Automatic;

    public BetaRuntime(ConsentSettings settings, PrivateIntegrationPolicy policy, QuotaRefreshCoordinator coordinator, IClock clock,
        ConsentCredentialSource? credentials = null, ILocalAnalyticsLifecycle? analytics = null, QuotaExportPublisher? publisher = null,
        TimeProvider? timeProvider = null, Func<PrivateIntegrationPolicy, IQuotaProvider>? remoteProviderFactory = null,
        Func<bool>? remoteFileAvailable = null, TimeSpan? remoteAbsenceConfirmation = null)
    {
        _settings = settings; _policy = policy; _coordinator = coordinator; _clock = clock;
        _credentials = credentials; _analytics = analytics; _publisher = publisher;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _remoteAbsenceConfirmation = remoteAbsenceConfirmation ?? TimeSpan.Zero;
        _remoteFileAvailable = remoteFileAvailable ?? (() => CodexRemoteUsageProvider.IsAvailableForAutomatic());
        _provider = new(policy, coordinator.Provider, remoteProviderFactory ?? (p => new CodexRemoteUsageProvider(p)));
        coordinator.UseProvider(_provider);
        coordinator.RefreshRouter = RefreshAsync;
        coordinator.StateChanged += OnStateChanged;
        _pollTimer = new(TimeSpan.FromMinutes(5), _timeProvider);
    }

    public string Disclosure => _policy.Disclosure;
    public bool IsConsentEnabled => _policy.IsEnabled;
    public CredentialAvailability CredentialAvailability { get; private set; } = CredentialAvailability.Disabled;
    public DataSource Source => _source;
    // Explicit preference governs analytics/costs; resolution governs only quota retrieval.
    public bool IsRemoteOnly => Source == DataSource.OtherMachine;
    public bool IsUsingRemote => _resolvedRemote;
    public string? PlanLabel => _provider.PlanLabel;
    public bool CanDeliverAlerts => !_disposed && !_handoff && _policy.IsEnabled &&
        State.Freshness == FreshnessState.Current && State.Failure is null;
    public Func<bool> CaptureAlertEligibility()
    {
        var epoch = Interlocked.Read(ref _sourceEpoch);
        var admitted = !_handoff && _policy.IsEnabled;
        return () => admitted && epoch == Interlocked.Read(ref _sourceEpoch) && CanDeliverAlerts;
    }
    public event Action? SourceChanged;
    public QuotaRefreshState State => _coordinator.State;
    public TimeSpan? CachedAge => State.Snapshot is { } snapshot ? _clock.UtcNow - snapshot.RetrievedAt : null;

    public void SetRefreshMinutes(int minutes)
    {
        if (!AppearanceSettings.ValidRefreshMinutes(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes));
        if (_disposed) return;
        _refreshMinutes = minutes;
        ConfigurePolling();
    }

    private void ConfigurePolling()
    {
        var interval = TimeSpan.FromMinutes(_resolvedRemote ? 1 : _refreshMinutes);
        _coordinator.SetMinimumPollInterval(interval);
        _pollTimer.Period = interval;
    }

    private bool ResolveRemote() => Source == DataSource.OtherMachine ||
        Source == DataSource.Automatic && _remoteFileAvailable();

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            _source = await _settings.LoadCodexSourceAsync(cancellationToken);
            _resolvedRemote = ResolveRemote();
            _provider.Select(_resolvedRemote);
            // Remote snapshots are never persisted. A local startup may load only a local
            // cache; remote startup clears that cache before admitting any remote values.
            await _coordinator.ConfigureSourceAsync(_resolvedRemote, _resolvedRemote, cancellationToken);
            ConfigurePolling();
            var enabled = await _settings.LoadAsync(cancellationToken);
            if (enabled) _policy.Enable(); else _policy.Disable();
            CredentialAvailability = enabled ? CredentialAvailability.Missing : CredentialAvailability.Disabled;
            if (_publisher is not null) await (enabled ? _publisher.RestoreAsync(cancellationToken) : _publisher.DisableAsync(cancellationToken));
            await _coordinator.InitializeAsync(cancellationToken);
            _initialized = true;
            if (enabled)
            {
                _polling = PollAsync(_lifetime.Token);
                if (_analytics is not null && !IsRemoteOnly) await _analytics.StartAsync(_lifetime.Token);
            }
        }
        finally { _consentGate.Release(); }
        _ = RefreshInitiallyAsync();
    }

    private async Task RefreshInitiallyAsync()
    {
        try { await RefreshAsync(RefreshTrigger.Poll, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception) { /* Safe provider failures are exposed by the coordinator. */ }
    }

    public async ValueTask SetSourceAsync(DataSource source, CancellationToken cancellationToken)
    {
        _ = source.ToClaude();
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_source == source) return;
            await _settings.SaveCodexSourceAsync(source, cancellationToken);
            _source = source;
            var remote = ResolveRemote();
            if (remote != _resolvedRemote || _handoff) await HandoffAsync(remote);
            if (_analytics is not null)
            {
                if (IsRemoteOnly) await _analytics.StopAsync(CancellationToken.None);
                else if (_policy.IsEnabled) await _analytics.StartAsync(_lifetime.Token);
            }
            SourceChanged?.Invoke();
        }
        finally { _consentGate.Release(); }
        await RefreshAsync(RefreshTrigger.Manual, CancellationToken.None);
    }

    // The gate serializes resolution, but never spans retrieval: another source/consent
    // change must be able to cancel even a cancellation-ignoring provider or export writer.
    private async ValueTask HandoffAsync(bool remote)
    {
        _handoff = true;
        Interlocked.Increment(ref _sourceEpoch);
        CancelDeadline();
        // Leave _handoff set if a step fails: publication stays closed until retry.
        var retrieval = _coordinator.CancelAndWaitAsync(CancellationToken.None).AsTask();
        var publication = _publisher?.CancelAndWaitAsync(CancellationToken.None).AsTask() ?? Task.CompletedTask;
        await Task.WhenAll(retrieval, publication);
        await _coordinator.ConfigureSourceAsync(remote, clear: true, CancellationToken.None);
        _provider.Select(remote);
        _resolvedRemote = remote;
        ConfigurePolling();
        if (_policy.IsEnabled)
        {
            if (_publisher is not null) await _publisher.RestoreAsync(CancellationToken.None);
            _coordinator.ResumeAfterClear();
        }
        _handoff = false;
    }

    public async ValueTask GrantConsentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_policy.IsEnabled) return;
            await _settings.SaveAsync(true, cancellationToken);
            var remote = ResolveRemote();
            if (remote != _resolvedRemote || _handoff) await HandoffAsync(remote);
            Interlocked.Increment(ref _sourceEpoch);
            _policy.Enable();
            if (_publisher is not null) await _publisher.EnableAsync(cancellationToken);
            _coordinator.ResumeAfterClear();
            _polling ??= PollAsync(_lifetime.Token);
            if (_analytics is not null && !IsRemoteOnly) await _analytics.StartAsync(_lifetime.Token);
        }
        finally { _consentGate.Release(); }
        await RefreshAsync(RefreshTrigger.Manual, cancellationToken);
    }

    public async ValueTask RevokeConsentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            _policy.Disable();
            Interlocked.Increment(ref _sourceEpoch);
            CancelDeadline();
            CredentialAvailability = CredentialAvailability.Disabled;
            // Once revocation starts, caller cancellation must not abandon work in flight.
            await _coordinator.CancelAndWaitAsync(CancellationToken.None);
            if (_publisher is not null) await _publisher.DisableAsync(CancellationToken.None);
            if (_analytics is not null) await _analytics.StopAsync(CancellationToken.None);
            await _settings.SaveAsync(false, CancellationToken.None);
        }
        finally { _consentGate.Release(); }
    }

    public async ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        // File.Replace may leave a brief absence. Confirm it before Automatic can
        // switch back to local credentials; injected tests can keep this delay zero.
        if (_remoteAbsenceConfirmation > TimeSpan.Zero && _source == DataSource.Automatic &&
            _resolvedRemote && !_remoteFileAvailable())
            await Task.Delay(_remoteAbsenceConfirmation, _timeProvider, cancellationToken);
        await _consentGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || !_policy.IsEnabled) return;
            var remote = ResolveRemote();
            if (remote != _resolvedRemote || _handoff)
            {
                await HandoffAsync(remote);
                SourceChanged?.Invoke();
                trigger = RefreshTrigger.Manual;
            }
        }
        finally { _consentGate.Release(); }
        await _coordinator.ReevaluateCoreAsync(trigger, cancellationToken);
        if (!_resolvedRemote)
            CredentialAvailability = _credentials?.LastAvailability ?? State.Failure?.SafeCode switch
            {
                "quota_credential_missing" => CredentialAvailability.Missing,
                "quota_credential_unusable" => CredentialAvailability.Unusable,
                _ => CredentialAvailability,
            };
    }

    private void OnStateChanged(QuotaRefreshState state)
    {
        lock (_deadlineGate)
        {
            CancelDeadlineLocked();
            if (_disposed || _handoff || !_policy.IsEnabled || !_resolvedRemote || state.Snapshot is not { } snapshot) return;
            var remaining = snapshot.RetrievedAt + TimeSpan.FromMinutes(15) - _clock.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            var generation = _deadlineGeneration;
            _deadline = _timeProvider.CreateTimer(_ =>
            {
                try
                {
                    lock (_deadlineGate)
                    {
                        if (generation != _deadlineGeneration || _disposed || _handoff || !_policy.IsEnabled || !_resolvedRemote) return;
                        // Sleep reevaluates freshness only: no provider/file read at the deadline.
                        _coordinator.ReevaluateCoreAsync(RefreshTrigger.Sleep, CancellationToken.None).GetAwaiter().GetResult();
                        // A backward clock correction may leave freshness unchanged.
                        OnStateChanged(_coordinator.State);
                    }
                }
                catch (Exception) { /* Timer/subscriber failures must not terminate the process. */ }
            }, null, remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelDeadline() { lock (_deadlineGate) CancelDeadlineLocked(); }
    private void CancelDeadlineLocked() { _deadlineGeneration++; _deadline?.Dispose(); _deadline = null; }
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
        Interlocked.Increment(ref _sourceEpoch);
        CancelDeadline();
        _coordinator.StateChanged -= OnStateChanged;
        _lifetime.Cancel();
        await _consentGate.WaitAsync();
        try
        {
            await _coordinator.CancelAndWaitAsync(CancellationToken.None);
            if (_publisher is not null) await _publisher.DisposeAsync();
        }
        finally
        {
            _pollTimer.Dispose();
            try { await _coordinator.DisposeAsync(); }
            finally
            {
                _consentGate.Release();
                if (_polling is not null) try { await _polling; } catch (ObjectDisposedException) { }
                _lifetime.Dispose();
            }
        }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
