using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Independent, opt-in owner of Claude subscription quota work and normalized cache.</summary>
public sealed class ClaudeQuotaRuntime : IAsyncDisposable, IAiBarClearWork
{
    public const string ConsentDisclosure = "Enable Claude subscription quota?\n\nAutomatic mode detects a usage file from another computer on startup and every refresh. When present, AIBar uses other machine mode, reading only that usage file; no credentials are read on this PC. Without that file, AIBar uses this PC mode: it reads the existing Claude Code login file on this PC read-only and queries Anthropic's unofficial subscription-usage endpoint. A stale or invalid remote file stays remote. You can manually choose This PC or Other machine in the tray. AIBar will never refresh or change the login. Credential contents, tokens and paths are never logged or saved in quota data. You can turn this off at any time.";
    private static readonly QuotaRefreshState Empty = new(null, FreshnessState.Unavailable, false, null, null);
    private readonly ConsentSettings _settings;
    private readonly IQuotaSnapshotStore _store;
    private readonly Func<PrivateIntegrationPolicy, IQuotaProvider> _providerFactory;
    private readonly Func<PrivateIntegrationPolicy, IQuotaProvider> _remoteProviderFactory;
    private readonly Func<bool> _remoteFileAvailable;
    private volatile ClaudeQuotaSource _source = ClaudeQuotaSource.Automatic;
    private volatile ClaudeQuotaSource _resolvedSource = ClaudeQuotaSource.ThisPc;
    private readonly IClock _clock;
    private readonly IQuotaRefreshLifecycleEvents? _lifecycle;
    private readonly TimeProvider _timeProvider;
    private readonly PrivateIntegrationPolicy _policy = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _notificationGate = new();
    private readonly List<(Notification Kind, QuotaAuthorityUpdate? Update, long Epoch)> _pendingNotifications = [];
    private Action<QuotaAuthorityUpdate>? _authorityHandler;
    // A deferral can span source replacement: never replay old-source authority after ExitGate.
    private long _authorityEpoch;
    private long _authorityRetrievalGeneration;
    private long _coordinatorRetrievalGeneration;
    private int _notificationDeferrals;
    private enum Notification { Source, State, Consent, Authority }
    public event Action<QuotaAuthorityUpdate>? AuthorityUpdated;
    private void OnAuthorityUpdated(QuotaAuthorityUpdate update, long epoch)
    {
        lock (_notificationGate)
        {
            if (epoch != Interlocked.Read(ref _authorityEpoch)) return;
            if (update.RetrievalGeneration > _coordinatorRetrievalGeneration)
            {
                _coordinatorRetrievalGeneration = update.RetrievalGeneration;
                _authorityRetrievalGeneration++;
            }
            update = update with { RetrievalGeneration = _authorityRetrievalGeneration };
        }
        Notify(Notification.Authority, update, epoch);
    }
    private readonly IQuotaExportWriter? _exportWriter;
    private QuotaExportPublisher? _publisher;
    private QuotaRefreshCoordinator? _coordinator;
    private readonly IQuotaHistoryStore? _historyStore;
    private QuotaHistoryRecorder? _historyRecorder;
    private IQuotaProvider? _provider;
    private CancellationTokenSource? _pollCancellation;
    private ITimer? _timer;
    private readonly IClaudeRemoteRefreshChannel _remoteRefresh;
    private readonly TimeSpan _remoteAbsenceConfirmation;
    private IDisposable? _remoteUsageWatch;
    private readonly object _deadlineGate = new();
    private ITimer? _remoteDeadlineTimer;
    private long _deadlineGeneration;
    private static readonly TimeSpan RemoteFreshness = TimeSpan.FromMinutes(15);
    private int _refreshMinutes = 5;
    private bool _initialized;
    private bool _disposed;
    private volatile bool _enabled;

    public ClaudeQuotaRuntime(ConsentSettings settings, IQuotaSnapshotStore store,
        Func<PrivateIntegrationPolicy, IQuotaProvider> providerFactory, IClock clock,
        IQuotaRefreshLifecycleEvents? lifecycle = null, TimeProvider? timeProvider = null,
        IQuotaExportWriter? exportWriter = null,
        Func<PrivateIntegrationPolicy, IQuotaProvider>? remoteProviderFactory = null,
        Func<bool>? remoteFileAvailable = null, IQuotaHistoryStore? historyStore = null,
        IClaudeRemoteRefreshChannel? remoteRefresh = null, TimeSpan? remoteAbsenceConfirmation = null)
    {
        _remoteAbsenceConfirmation = remoteAbsenceConfirmation ?? TimeSpan.Zero;
        // Like the export writer, only the desktop composition touches the real AIBar directory.
        _remoteRefresh = remoteRefresh ?? NoRemoteRefresh.Instance;
        _historyStore = historyStore;
        _remoteFileAvailable = remoteFileAvailable ?? (() => ClaudeRemoteUsageProvider.IsAvailableForAutomatic());
        _exportWriter = exportWriter;
        _remoteProviderFactory = remoteProviderFactory ?? (policy => new ClaudeRemoteUsageProvider(policy));
        _settings = settings; _store = store; _providerFactory = providerFactory; _clock = clock;
        _lifecycle = lifecycle; _timeProvider = timeProvider ?? TimeProvider.System;
        if (_lifecycle is not null)
        {
            _lifecycle.Suspended += OnSuspended; _lifecycle.Resumed += OnResumed; _lifecycle.ClockChanged += OnClockChanged;
        }
    }

    // Serialize with consent/source replacement; never hold this gate over UI dispatch.
    public async ValueTask SetRefreshMinutesAsync(int minutes, CancellationToken cancellationToken = default)
    {
        if (!AppearanceSettings.ValidRefreshMinutes(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes));
        await EnterGateAsync(cancellationToken);
        try
        {
            if (_disposed) return;
            if (_refreshMinutes == minutes) return;
            _refreshMinutes = minutes;
            if (_resolvedSource == ClaudeQuotaSource.OtherMachine) return;
            var interval = TimeSpan.FromMinutes(minutes);
            _coordinator?.SetMinimumPollInterval(interval);
            _timer?.Change(interval, interval);
        }
        finally { ExitGate(); }
    }

    internal Task ExportCompletion => _publisher?.PublicationCompletion ?? Task.CompletedTask;

    public static string DatabasePath(string dataDirectory) => Path.Combine(dataDirectory, "claude-quota.db");
    public string Disclosure => ConsentDisclosure;
    public ClaudeQuotaSource Source => _source;
    // Selected mode remains Automatic while this reports ThisPc or OtherMachine.
    public ClaudeQuotaSource ResolvedSource => _resolvedSource;
    public event Action? SourceChanged;
    public async ValueTask SetSourceAsync(ClaudeQuotaSource source, CancellationToken cancellationToken)
    {
        if (source is not (ClaudeQuotaSource.Automatic or ClaudeQuotaSource.ThisPc or ClaudeQuotaSource.OtherMachine)) throw new ArgumentOutOfRangeException(nameof(source));
        await EnterGateAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_source == source) return;
            await _settings.SaveClaudeSourceAsync(source, cancellationToken);
            // Once persisted, finish the handoff even if the menu caller cancels.
            var enabled = _enabled;
            await StopCoreAsync();
            if (!enabled) await _store.ClearAsync(CancellationToken.None);
            _source = source;
            _resolvedSource = ResolveSource(source);
            if (enabled) await EnableCoreAsync(CancellationToken.None, cacheCleared: true);
            Notify(Notification.Source);
            Notify(Notification.State);
        }
        finally { ExitGate(); }
        await RefreshAsync(RefreshTrigger.Manual, CancellationToken.None);
    }
    public bool IsConsentEnabled => _enabled;
    public QuotaRefreshState State
    {
        get
        {
            var state = _enabled ? _coordinator?.State ?? Empty : Empty;
            // Project freshness as well as notifying at the deadline: a delayed UI dispatch
            // must never turn an expired remote snapshot back into Current.
            return _resolvedSource == ClaudeQuotaSource.OtherMachine && state.Snapshot is { } snapshot &&
                (state.Failure is not null || _clock.UtcNow >= snapshot.RetrievedAt + RemoteFreshness)
                ? state with { Freshness = FreshnessState.Stale } : state;
        }
    }
    public string? PlanLabel => _enabled && _resolvedSource == ClaudeQuotaSource.ThisPc
        ? (_provider as IQuotaPlanMetadata)?.PlanLabel : null;

    public TimeSpan? CachedAge => State.Snapshot is { } snapshot ? _clock.UtcNow - snapshot.RetrievedAt : null;
    public event Action? ConsentChanged;
    public event Action? StateChanged;
    private void OnStateChanged(QuotaRefreshState state)
    {
        ScheduleRemoteDeadline();
        Notify(Notification.State);
    }

    // Deferral starts before waiting: coordinator callbacks on work being drained
    // must return immediately even when another runtime operation is already queued.
    private async ValueTask EnterGateAsync(CancellationToken cancellationToken = default)
    {
        lock (_notificationGate) _notificationDeferrals++;
        try { await _gate.WaitAsync(cancellationToken); }
        catch { EndNotificationDeferral(); throw; }
    }

    private void ExitGate()
    {
        _gate.Release();
        EndNotificationDeferral();
    }

    private void EndNotificationDeferral()
    {
        (Notification Kind, QuotaAuthorityUpdate? Update, long Epoch)[] pending;
        lock (_notificationGate)
        {
            if (--_notificationDeferrals != 0) return;
            pending = _pendingNotifications.ToArray();
            _pendingNotifications.Clear();
        }
        // Initialization/teardown may forward State before the handoff queues Source.
        // Publish source identity first, retaining every queued notification thereafter.
        Exception? failure = null;
        foreach (var notification in pending.OrderBy(value => value.Kind == Notification.Source ? 0 : 1))
        {
            try { RaiseNotification(notification.Kind, notification.Update, notification.Epoch); }
            catch (Exception exception) { failure ??= exception; }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void Notify(Notification notification, QuotaAuthorityUpdate? update = null, long epoch = 0)
    {
        lock (_notificationGate)
        {
            if (_notificationDeferrals != 0)
            {
                _pendingNotifications.Add((notification, update, epoch));
                return;
            }
        }
        RaiseNotification(notification, update, epoch);
    }

    private void RaiseNotification(Notification notification, QuotaAuthorityUpdate? update = null, long epoch = 0)
    {
        switch (notification)
        {
            case Notification.Source: SourceChanged?.Invoke(); break;
            case Notification.State: StateChanged?.Invoke(); break;
            case Notification.Consent: ConsentChanged?.Invoke(); break;
            case Notification.Authority:
                if (_enabled && epoch == Interlocked.Read(ref _authorityEpoch) && update is not null) AuthorityUpdated?.Invoke(update);
                break;
        }
    }

    private void ScheduleRemoteDeadline()
    {
        lock (_deadlineGate)
        {
            CancelRemoteDeadlineLocked();
            if (!_enabled || _disposed || _resolvedSource != ClaudeQuotaSource.OtherMachine ||
                _coordinator?.State.Snapshot is not { } snapshot) return;
            var remaining = snapshot.RetrievedAt + RemoteFreshness - _clock.UtcNow;
            if (remaining <= TimeSpan.Zero) return;
            var generation = _deadlineGeneration;
            _remoteDeadlineTimer = _timeProvider.CreateTimer(_ =>
            {
                try { ExpireRemoteDeadline(generation); }
                catch (Exception) { /* Never trace subscriber exceptions or private state. */ }
            }, null, DeadlineDelay(remaining), Timeout.InfiniteTimeSpan);
        }
    }

    private void ExpireRemoteDeadline(long generation)
    {
        lock (_deadlineGate)
        {
            // Disposed/replaced timers may already have queued callbacks. They cannot
            // publish on behalf of an old source, consent, or snapshot generation.
            if (generation != _deadlineGeneration || !_enabled || _disposed ||
                _resolvedSource != ClaudeQuotaSource.OtherMachine) return;
            if (_coordinator?.State.Snapshot is { } snapshot)
            {
                var remaining = snapshot.RetrievedAt + RemoteFreshness - _clock.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    _remoteDeadlineTimer?.Change(DeadlineDelay(remaining), Timeout.InfiniteTimeSpan);
                    return;
                }
            }
            CancelRemoteDeadlineLocked();
        }
        // No provider read, polling, or new failure: retain values and existing guidance.
        Notify(Notification.State);
    }

    // Keep system timers in range even after a large backward wall-clock correction.
    private static TimeSpan DeadlineDelay(TimeSpan remaining) =>
        remaining > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : remaining;

    private void CancelRemoteDeadline()
    {
        lock (_deadlineGate) CancelRemoteDeadlineLocked();
    }

    private void CancelRemoteDeadlineLocked()
    {
        _deadlineGeneration++;
        _remoteDeadlineTimer?.Dispose();
        _remoteDeadlineTimer = null;
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken)
    {
        await EnterGateAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_initialized) return;
            _source = await _settings.LoadClaudeSourceAsync(cancellationToken);
            _resolvedSource = ResolveSource(_source);
            _initialized = true;
            Notify(Notification.Source);
            if (await _settings.LoadClaudeAsync(cancellationToken)) await EnableCoreAsync(cancellationToken, restore: true);
            else await StopExportAsync();
        }
        finally { ExitGate(); }
        RequestRemoteRefresh();
        _ = RefreshInitiallyAsync();
    }

    private async Task RefreshInitiallyAsync()
    {
        try { await RefreshAsync(RefreshTrigger.Poll, CancellationToken.None); }
        catch (Exception) { /* Coordinator maps provider failures to safe state; never trace exceptions. */ }
    }

    public async ValueTask GrantConsentAsync(CancellationToken cancellationToken)
    {
        await EnterGateAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (_enabled) return;
            await _settings.SaveClaudeAsync(true, cancellationToken);
            var resolved = ResolveSource(_source);
            var changed = resolved != _resolvedSource;
            _resolvedSource = resolved;
            await EnableCoreAsync(cancellationToken);
            if (changed) Notify(Notification.Source);
        }
        finally { ExitGate(); }
        // Never hold the consent gate over a network retrieval or save: revoke must interrupt it.
        await RefreshAsync(RefreshTrigger.Manual, cancellationToken);
    }

    private ClaudeQuotaSource ResolveSource(ClaudeQuotaSource source) => source == ClaudeQuotaSource.Automatic
        ? (_remoteFileAvailable() ? ClaudeQuotaSource.OtherMachine : ClaudeQuotaSource.ThisPc) : source;

    private async ValueTask EnableCoreAsync(CancellationToken cancellationToken, bool cacheCleared = false, bool restore = false)
    {
        var cachedSource = await _settings.LoadClaudeCacheSourceAsync(cancellationToken);
        // Before Automatic existed, an absent source meant This PC. An explicit legacy
        // choice identifies its cache; later runs use the independently saved provenance.
        var previousSource = cachedSource ?? (_source == ClaudeQuotaSource.Automatic ? ClaudeQuotaSource.ThisPc : _source);
        if (!cacheCleared && previousSource != _resolvedSource) await _store.ClearAsync(cancellationToken);
        // Local-only installs keep the legacy settings shape; provenance is needed only
        // after remote use, when absence of a marker would otherwise mean a local cache.
        if (cachedSource != _resolvedSource && (_resolvedSource == ClaudeQuotaSource.OtherMachine || cachedSource is not null))
            await _settings.SaveClaudeCacheSourceAsync(_resolvedSource, cancellationToken);
        _policy.Enable();
        _pollCancellation = new();
        try
        {
            _provider = (_resolvedSource == ClaudeQuotaSource.OtherMachine ? _remoteProviderFactory : _providerFactory)(_policy);
            // Source handoffs recreate both timer and coordinator through this path.
            var pollInterval = TimeSpan.FromMinutes(_resolvedSource == ClaudeQuotaSource.OtherMachine ? 1 : _refreshMinutes);
            _coordinator = new(new ConsentStore(_store, _policy, _pollCancellation.Token), _provider, _clock,
                new FreshnessPolicy(TimeSpan.FromMinutes(_resolvedSource == ClaudeQuotaSource.OtherMachine ? 15 : 10)), pollInterval);
            _coordinatorRetrievalGeneration = 0;
            var epoch = Interlocked.Increment(ref _authorityEpoch);
            _authorityHandler = update => OnAuthorityUpdated(update, epoch);
            _coordinator.AuthorityUpdated += _authorityHandler;
            if (_historyStore is not null)
                _historyRecorder = new(_coordinator, _historyStore, _clock, QuotaHistoryProvider.Claude,
                    _resolvedSource == ClaudeQuotaSource.OtherMachine ? QuotaHistorySource.ClaudeRemote : QuotaHistorySource.ClaudeLocal);
            _coordinator.StateChanged += OnStateChanged;
            await _coordinator.InitializeAsync(cancellationToken);
            if (_exportWriter is not null)
            {
                _publisher = new QuotaExportPublisher(_coordinator, _exportWriter, _clock);
                // Persisted consent is not a privacy action: publish value-free truthful states, never "disabled".
                await (restore ? _publisher.RestoreAsync(cancellationToken) : _publisher.EnableAsync(cancellationToken));
            }
            _enabled = true;
            ScheduleRemoteDeadline();
            _timer = _timeProvider.CreateTimer(_ => Forward(RefreshTrigger.Poll), null,
                pollInterval, pollInterval);
            if (_resolvedSource == ClaudeQuotaSource.OtherMachine)
                _remoteUsageWatch = _remoteRefresh.WatchUsage(ForwardRemoteUsageChanged);
            Notify(Notification.Consent);
        }
        catch
        {
            await StopCoreAsync();
            throw;
        }
    }

    public async ValueTask RevokeConsentAsync(CancellationToken cancellationToken)
    {
        await EnterGateAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            // Once revocation starts, caller cancellation cannot leave credential work running.
            try { await StopCoreAsync(); }
            finally
            {
                await _settings.SaveClaudeAsync(false, CancellationToken.None);
                Notify(Notification.Consent);
            }
        }
        finally { ExitGate(); }
    }

    public ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken) =>
        // Only a person-driven refresh asks the other machine for a push; polls never do.
        RefreshCoreAsync(trigger, trigger is RefreshTrigger.Manual or RefreshTrigger.PopoverOpened, cancellationToken);

    private async ValueTask RefreshCoreAsync(RefreshTrigger trigger, bool requestRemote, CancellationToken cancellationToken)
    {
        QuotaRefreshCoordinator? coordinator;
        // The other machine publishes with File.Replace, which leaves the file briefly absent.
        // Automatic must not fall back to this PC (and its missing login) on that gap.
        if (_remoteAbsenceConfirmation > TimeSpan.Zero && _source == ClaudeQuotaSource.Automatic &&
            _resolvedSource == ClaudeQuotaSource.OtherMachine && !_remoteFileAvailable())
            await Task.Delay(_remoteAbsenceConfirmation, _timeProvider, cancellationToken);
        await EnterGateAsync(cancellationToken);
        try
        {
            if (_disposed) return;
            var resolved = ResolveSource(_source);
            if (resolved != _resolvedSource)
            {
                var enabled = _enabled;
                if (enabled) await StopCoreAsync();
                _resolvedSource = resolved;
                if (enabled) await EnableCoreAsync(CancellationToken.None, cacheCleared: true);
                Notify(Notification.Source);
                Notify(Notification.State);
                // A newly resolved source must refresh even when the old source was
                // current or the caller was a throttled poll/lifecycle notification.
                trigger = RefreshTrigger.Manual;
            }
            if (!_enabled) return;
            coordinator = _coordinator;
        }
        finally { ExitGate(); }
        if (coordinator is null) return;
        if (requestRemote) RequestRemoteRefresh();
        // Resolution/handoff is serialized, never retrieval: revoke or another source
        // change must be able to cancel and drain even a cancellation-ignoring provider.
        try { await coordinator.ReevaluateAsync(trigger, cancellationToken); }
        catch (ObjectDisposedException) when (!_enabled || !ReferenceEquals(coordinator, _coordinator)) { }
    }

    private async ValueTask StopCoreAsync(bool discardPendingHistory = false)
    {
        _enabled = false; _policy.Disable();
        Interlocked.Increment(ref _authorityEpoch);
        CancelRemoteDeadline();
        _pollCancellation?.Cancel();
        StopRemoteUsageWatch();
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
                    if (discardPendingHistory && _historyRecorder is not null)
                        await _historyRecorder.CancelAndWaitAsync(CancellationToken.None);
                    await StopHistoryAsync();
                    await _coordinator.ClearAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            if (_coordinator is not null)
            {
                _coordinator.AuthorityUpdated -= _authorityHandler;
                _coordinator.StateChanged -= OnStateChanged;
                await _coordinator.DisposeAsync();
            }
            await StopHistoryAsync();
            if (_provider is IAsyncDisposable asyncProvider) await asyncProvider.DisposeAsync();
            else if (_provider is IDisposable provider) provider.Dispose();
            _coordinator = null; _provider = null; _timer = null;
            _pollCancellation?.Dispose(); _pollCancellation = null;
        }
    }

    private async ValueTask StopHistoryAsync()
    {
        var recorder = _historyRecorder;
        _historyRecorder = null;
        if (recorder is not null) await recorder.DisposeAsync();
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
        await EnterGateAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            try { await StopCoreAsync(discardPendingHistory: true); }
            finally
            {
                await _settings.SaveClaudeAsync(false, CancellationToken.None);
                Notify(Notification.Consent);
            }
        }
        finally { ExitGate(); }
    }

    // Clear service resumes every participant. Claude stays off until a new explicit grant.
    public void ResumeAfterClear() { }
    private void OnSuspended() => Forward(RefreshTrigger.Sleep);
    private void OnResumed() => Forward(RefreshTrigger.Resume);
    private void OnClockChanged()
    {
        ScheduleRemoteDeadline();
        Forward(RefreshTrigger.ClockChanged);
        // Recalculate reset countdowns even when freshness and polling eligibility did not change.
        Notify(Notification.State);
    }
    private async void Forward(RefreshTrigger trigger)
    {
        try { await RefreshAsync(trigger, CancellationToken.None); }
        catch (Exception) { /* Never trace a provider/file-system exception or its private path. */ }
    }

    // A push just arrived: read it now instead of at the next poll. Requesting again here
    // would make the other machine push in a loop.
    private async void ForwardRemoteUsageChanged()
    {
        try
        {
            // Let the replace that raised the event finish before reading.
            if (_remoteAbsenceConfirmation > TimeSpan.Zero) await Task.Delay(_remoteAbsenceConfirmation, _timeProvider);
            if (_enabled) await RefreshCoreAsync(RefreshTrigger.Manual, requestRemote: false, CancellationToken.None);
        }
        catch (Exception) { /* Never trace a provider/file-system exception or its private path. */ }
    }

    private void RequestRemoteRefresh()
    {
        if (!_enabled || _resolvedSource != ClaudeQuotaSource.OtherMachine) return;
        _ = RequestRemoteRefreshAsync();
    }

    private async Task RequestRemoteRefreshAsync()
    {
        try { await _remoteRefresh.RequestAsync(_clock.UtcNow, CancellationToken.None); }
        catch (Exception) { /* Best effort: the other machine's timer still pushes. */ }
    }

    private void StopRemoteUsageWatch()
    {
        var watch = Interlocked.Exchange(ref _remoteUsageWatch, null);
        try { watch?.Dispose(); } catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        await EnterGateAsync();
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
            Interlocked.Increment(ref _authorityEpoch);
            CancelRemoteDeadline();
            StopRemoteUsageWatch();
            if (_timer is not null) await _timer.DisposeAsync();
            try { await StopExportAsync(); }
            finally
            {
                if (_coordinator is not null)
                {
                    _coordinator.AuthorityUpdated -= _authorityHandler;
                    _coordinator.StateChanged -= OnStateChanged;
                    await _coordinator.DisposeAsync();
                }
                await StopHistoryAsync();
                if (_provider is IAsyncDisposable asyncProvider) await asyncProvider.DisposeAsync();
                else if (_provider is IDisposable provider) provider.Dispose();
                _pollCancellation?.Dispose();
                if (_store is IAsyncDisposable store) await store.DisposeAsync();
            }
        }
        finally { ExitGate(); }
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
