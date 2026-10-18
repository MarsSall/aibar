using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Independent, opt-in owner of Claude subscription quota work and normalized cache.</summary>
public sealed class ClaudeQuotaRuntime : IAsyncDisposable, IAiBarClearWork
{
    public const string ConsentDisclosure = "Enable Claude subscription quota?\n\nAIBar will read the existing Claude Code login file on this PC read-only and query Anthropic's unofficial subscription-usage endpoint. AIBar will never refresh or change the login. Credential contents, tokens and paths are never logged or saved in quota data. You can turn this off at any time.";
    private static readonly QuotaRefreshState Empty = new(null, FreshnessState.Unavailable, false, null, null);
    private readonly ConsentSettings _settings;
    private readonly IQuotaSnapshotStore _store;
    private readonly Func<PrivateIntegrationPolicy, IQuotaProvider> _providerFactory;
    private readonly IClock _clock;
    private readonly IQuotaRefreshLifecycleEvents? _lifecycle;
    private readonly TimeProvider _timeProvider;
    private readonly PrivateIntegrationPolicy _policy = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IQuotaExportWriter? _exportWriter;
    private QuotaExportPublisher? _publisher;
    private QuotaRefreshCoordinator? _coordinator;
    private IQuotaProvider? _provider;
    private CancellationTokenSource? _pollCancellation;
    private ITimer? _timer;
    private bool _initialized;
    private bool _disposed;
    private volatile bool _enabled;

    public ClaudeQuotaRuntime(ConsentSettings settings, IQuotaSnapshotStore store,
        Func<PrivateIntegrationPolicy, IQuotaProvider> providerFactory, IClock clock,
        IQuotaRefreshLifecycleEvents? lifecycle = null, TimeProvider? timeProvider = null,
        IQuotaExportWriter? exportWriter = null,
        Func<PrivateIntegrationPolicy, IQuotaProvider>? remoteProviderFactory = null)
    {
        _exportWriter = exportWriter;
        _settings = settings; _store = store; _providerFactory = providerFactory; _clock = clock;
        _lifecycle = lifecycle; _timeProvider = timeProvider ?? TimeProvider.System;
        if (_lifecycle is not null)
        {
            _lifecycle.Suspended += OnSuspended; _lifecycle.Resumed += OnResumed; _lifecycle.ClockChanged += OnClockChanged;
        }
    }

    internal Task ExportCompletion => _publisher?.PublicationCompletion ?? Task.CompletedTask;

    public static string DatabasePath(string dataDirectory) => Path.Combine(dataDirectory, "claude-quota.db");
    public string Disclosure => ConsentDisclosure;
    // RED seams; existing local callers remain unchanged until GREEN.
    public ClaudeQuotaSource Source => throw new NotImplementedException();
    public ValueTask SetSourceAsync(ClaudeQuotaSource source, CancellationToken cancellationToken) => throw new NotImplementedException();
    public bool IsConsentEnabled => _enabled;
    public QuotaRefreshState State => _enabled ? _coordinator?.State ?? Empty : Empty;
    public TimeSpan? CachedAge => State.Snapshot is { } snapshot ? _clock.UtcNow - snapshot.RetrievedAt : null;
    public event Action? ConsentChanged;
    public event Action? StateChanged;
    private void OnStateChanged(QuotaRefreshState state) => StateChanged?.Invoke();

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_initialized) return;
            _initialized = true;
            if (await _settings.LoadClaudeAsync(cancellationToken)) await EnableCoreAsync(cancellationToken);
            else await StopExportAsync();
        }
        finally { _gate.Release(); }
        _ = RefreshInitiallyAsync();
    }

    private async Task RefreshInitiallyAsync()
    {
        try { await RefreshAsync(RefreshTrigger.Poll, CancellationToken.None); }
        catch (Exception) { /* Coordinator maps provider failures to safe state; never trace exceptions. */ }
    }

    public async ValueTask GrantConsentAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_enabled) return;
            await _settings.SaveClaudeAsync(true, cancellationToken);
            await EnableCoreAsync(cancellationToken);
        }
        finally { _gate.Release(); }
        // Never hold the consent gate over a network retrieval or save: revoke must interrupt it.
        await RefreshAsync(RefreshTrigger.Manual, cancellationToken);
    }

    private async ValueTask EnableCoreAsync(CancellationToken cancellationToken)
    {
        _policy.Enable();
        _pollCancellation = new();
        try
        {
            _provider = _providerFactory(_policy);
            _coordinator = new(new ConsentStore(_store, _policy, _pollCancellation.Token), _provider, _clock,
                new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.FromMinutes(5));
            _coordinator.StateChanged += OnStateChanged;
            await _coordinator.InitializeAsync(cancellationToken);
            if (_exportWriter is not null)
            {
                _publisher = new QuotaExportPublisher(_coordinator, _exportWriter, _clock);
                await _publisher.EnableAsync(cancellationToken);
            }
            _enabled = true;
            _timer = _timeProvider.CreateTimer(_ => Forward(RefreshTrigger.Poll), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            ConsentChanged?.Invoke();
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
    }

    public async ValueTask RevokeConsentAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            // Once revocation starts, caller cancellation cannot leave credential work running.
            try { await StopCoreAsync(); }
            finally
            {
                await _settings.SaveClaudeAsync(false, CancellationToken.None);
                ConsentChanged?.Invoke();
            }
        }
        finally { _gate.Release(); }
    }

    public async ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken)
    {
        if (!_enabled || _disposed) return;
        var coordinator = _coordinator;
        if (coordinator is null) return;
        try { await coordinator.ReevaluateAsync(trigger, cancellationToken); }
        catch (ObjectDisposedException) when (!_enabled || !ReferenceEquals(coordinator, _coordinator)) { }
    }

    private async ValueTask StopCoreAsync()
    {
        _enabled = false; _policy.Disable();
        _pollCancellation?.Cancel();
        if (_timer is not null) await _timer.DisposeAsync();
        try
        {
            // Cancel and drain publication before clearing the authority: no late valued write
            // may outlive a privacy exit, even when the writer ignores cancellation.
            try { await StopExportAsync(); }
            finally
            {
                if (_coordinator is not null)
                {
                    // The wrapper rejects not-yet-entered saves. Awaiting drains an already-entered
                    // save before clearing it, even if an injected provider/store ignores cancellation.
                    await _coordinator.CancelAndWaitAsync(CancellationToken.None);
                    await _coordinator.ClearAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            if (_coordinator is not null)
            {
                _coordinator.StateChanged -= OnStateChanged;
                await _coordinator.DisposeAsync();
            }
            if (_provider is IAsyncDisposable asyncProvider) await asyncProvider.DisposeAsync();
            else if (_provider is IDisposable provider) provider.Dispose();
            _coordinator = null; _provider = null; _timer = null;
            _pollCancellation?.Dispose(); _pollCancellation = null;
        }
    }

    private async ValueTask StopExportAsync()
    {
        var publisher = _publisher;
        try
        {
            if (publisher is not null) await publisher.DisposeAsync();
            else if (_exportWriter is not null)
            {
                var now = _clock.UtcNow;
                await _exportWriter.WriteAsync(QuotaExportWire.Disabled(now), now, CancellationToken.None);
            }
        }
        finally { _publisher = null; }
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            try { await StopCoreAsync(); }
            finally
            {
                await _settings.SaveClaudeAsync(false, CancellationToken.None);
                ConsentChanged?.Invoke();
            }
        }
        finally { _gate.Release(); }
    }

    // Clear service resumes every participant. Claude stays off until a new explicit grant.
    public void ResumeAfterClear() { }
    private void OnSuspended() => Forward(RefreshTrigger.Sleep);
    private void OnResumed() => Forward(RefreshTrigger.Resume);
    private void OnClockChanged()
    {
        Forward(RefreshTrigger.ClockChanged);
        // Recalculate reset countdowns even when freshness and polling eligibility did not change.
        StateChanged?.Invoke();
    }
    private async void Forward(RefreshTrigger trigger)
    {
        try { await RefreshAsync(trigger, CancellationToken.None); }
        catch (Exception) { /* Never trace a provider/file-system exception or its private path. */ }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_lifecycle is not null)
            {
                _lifecycle.Suspended -= OnSuspended; _lifecycle.Resumed -= OnResumed; _lifecycle.ClockChanged -= OnClockChanged;
            }
            // Shutdown retains the normalized cache and consent; unlike revoke/clear it is not a reset.
            _enabled = false; _policy.Disable(); _pollCancellation?.Cancel();
            if (_timer is not null) await _timer.DisposeAsync();
            try { await StopExportAsync(); }
            finally
            {
                if (_coordinator is not null)
                {
                    _coordinator.StateChanged -= OnStateChanged;
                    await _coordinator.DisposeAsync();
                }
                if (_provider is IAsyncDisposable asyncProvider) await asyncProvider.DisposeAsync();
                else if (_provider is IDisposable provider) provider.Dispose();
                _pollCancellation?.Dispose();
                if (_store is IAsyncDisposable store) await store.DisposeAsync();
            }
        }
        finally { _gate.Release(); }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class ConsentStore(IQuotaSnapshotStore store, PrivateIntegrationPolicy policy, CancellationToken generation) : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => store.LoadAsync(cancellationToken);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken)
        {
            generation.ThrowIfCancellationRequested(); cancellationToken.ThrowIfCancellationRequested();
            if (!policy.IsEnabled) throw new OperationCanceledException(generation);
            return store.SaveAsync(snapshot, cancellationToken);
        }
        public ValueTask ClearAsync(CancellationToken cancellationToken) => store.ClearAsync(cancellationToken);
    }
}
