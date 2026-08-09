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
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PeriodicTimer _pollTimer = new(TimeSpan.FromMinutes(5));
    private readonly SemaphoreSlim _consentGate = new(1, 1);
    private Task? _polling;
    private bool _disposed;

    public BetaRuntime(ConsentSettings settings, PrivateIntegrationPolicy policy, QuotaRefreshCoordinator coordinator, IClock clock, ConsentCredentialSource? credentials = null, ILocalAnalyticsLifecycle? analytics = null)
    {
        _settings = settings; _policy = policy; _coordinator = coordinator; _clock = clock; _credentials = credentials; _analytics = analytics;
    }

    public string Disclosure => _policy.Disclosure;
    public bool IsConsentEnabled => _policy.IsEnabled;
    public CredentialAvailability CredentialAvailability { get; private set; } = CredentialAvailability.Disabled;
    public QuotaRefreshState State => _coordinator.State;
    public TimeSpan? CachedAge => State.Snapshot is { } snapshot ? _clock.UtcNow - snapshot.RetrievedAt : null;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (await _settings.LoadAsync(cancellationToken)) _policy.Enable(); else _policy.Disable();
        CredentialAvailability = _policy.IsEnabled ? CredentialAvailability.Missing : CredentialAvailability.Disabled;
        await _coordinator.InitializeAsync(cancellationToken);
        if (_policy.IsEnabled)
        {
            _polling = PollAsync(_lifetime.Token);
            _ = RefreshAsync(RefreshTrigger.Poll, _lifetime.Token).AsTask();
            if (_analytics is not null) await _analytics.StartAsync(_lifetime.Token);
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
        await _settings.SaveAsync(true, cancellationToken);
        _policy.Enable();
        _coordinator.ResumeAfterClear();
        _polling ??= PollAsync(_lifetime.Token);
        if (_analytics is not null) await _analytics.StartAsync(_lifetime.Token);
        await RefreshAsync(RefreshTrigger.Manual, cancellationToken);
    }

    public async ValueTask RevokeConsentAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _settings.SaveAsync(false, cancellationToken);
        _policy.Disable();
        CredentialAvailability = CredentialAvailability.Disabled;
        try { if (_analytics is not null) await _analytics.StopAsync(cancellationToken); }
        finally { await _coordinator.CancelAndWaitAsync(cancellationToken); }
    }

    public async ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!_policy.IsEnabled) return;
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
        _lifetime.Cancel();
        try { await _coordinator.CancelAndWaitAsync(CancellationToken.None); } catch (ObjectDisposedException) { }
        _pollTimer.Dispose(); if (_polling is not null) await _polling; await _coordinator.DisposeAsync(); _lifetime.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
