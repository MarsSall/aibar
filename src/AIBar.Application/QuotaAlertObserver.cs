using System.Runtime.CompilerServices;
using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Serialized, off-thread observer. Never performs I/O in authority callbacks.</summary>
public sealed class QuotaAlertObserver : IAiBarClearWork, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly string _statePath;
    private readonly IClock _clock;
    private readonly Dictionary<string, long> _generations = [];
    // Key by object identity, not alert key/provider: an old queued alert must never
    // inherit the current source's predicate. Weak keys bound retention to delivery.
    private readonly ConditionalWeakTable<QuotaAlert, Eligibility> _eligibility = new();
    private sealed record Eligibility(Func<bool> IsAllowed);
    private Task _tail = Task.CompletedTask;
    private StoredState? _state;
    private bool _paused, _disposed;
    public QuotaAlertObserver(string dataDirectory, IClock clock)
    {
        _clock = clock; _statePath = Path.Combine(dataDirectory, "alert-state.json");
        Settings = new(Path.Combine(dataDirectory, "alerts.json"));
    }
    public AlertSettings Settings { get; }
    public Task Completion { get { lock (_gate) return _tail; } }
    public event Action<QuotaAlert>? AlertReady;
    public event Action<AlertOptions>? SettingsChanged;
    public bool CanDeliver(QuotaAlert alert)
    {
        lock (_gate) return !_paused && !_disposed && alert.Sample.ResetAt > _clock.UtcNow &&
            _eligibility.TryGetValue(alert, out var eligibility) && eligibility.IsAllowed();
    }
    public void Observe(string provider, QuotaAuthorityUpdate update, Func<bool> consent)
    {
        lock (_gate)
        {
            if (_paused || _disposed || update.RetrievalGeneration <= _generations.GetValueOrDefault(provider)) return;
            _generations[provider] = update.RetrievalGeneration;
            var previous = _tail;
            _tail = Task.Run(async () =>
            {
                await previous;
                try { await ProcessAsync(provider, update.State, consent); }
                catch (Exception) { /* No persistence => no delivery; never trace private filesystem paths. */ }
            });
        }
    }
    public Task SaveSettingsAsync(AlertOptions options) => QueueSettingsAsync(_ => options, reportFailure: true);
    public Task ChangeSettingsAsync(Func<AlertOptions, AlertOptions> change) => QueueSettingsAsync(change, reportFailure: false);
    private Task QueueSettingsAsync(Func<AlertOptions, AlertOptions> change, bool reportFailure)
    {
        lock (_gate)
        {
            if (_paused || _disposed) return reportFailure ? Task.FromException(new InvalidOperationException("Alert settings are unavailable during clear or shutdown.")) : Task.CompletedTask;
            var previous = _tail;
            var operation = Task.Run(async () =>
            {
                await previous;
                try
                {
                    var options = change(await Settings.LoadAsync(default));
                    await Settings.SaveAsync(options, default);
                    SettingsChanged?.Invoke(options);
                }
                catch (Exception) when (!reportFailure) { /* Keep the persisted choice and menu unchanged on failure. */ }
            });
            // A failed editor save is reported to its caller, but must not poison the observer queue.
            _tail = operation.ContinueWith(_ => { }, TaskScheduler.Default);
            return operation;
        }
    }
    private async Task ProcessAsync(string provider, QuotaRefreshState state, Func<bool> consent)
    {
        if (!consent() || state.Freshness != FreshnessState.Current || state.Failure is not null || state.Snapshot is not { } snapshot) return;
        var now = _clock.UtcNow;
        var settings = await Settings.LoadAsync(default);
        // The hard limit remains an independent glow event even if the user's list omits 100.
        var evaluatedThresholds = settings.ThresholdValues.Append(100).Distinct().OrderBy(value => value).ToArray();
        _state ??= await QuotaAlertJson.ReadAsync<StoredState>(_statePath, default) is { Schema: 1, Fired: not null, Previous: not null } stored
            ? stored : new(1, [], []);
        var fired = new HashSet<string>(_state.Fired);
        var previous = new Dictionary<string, QuotaAlertSample>(_state.Previous);
        var events = new List<QuotaAlert>();
        foreach (var (window, value) in new[] { (QuotaPaceWindow.FiveHour, snapshot.Primary), (QuotaPaceWindow.Weekly, snapshot.Weekly) })
        {
            if (value is null) continue;
            var sample = new QuotaAlertSample(provider, window, value.PercentageUsed, value.ResetAt, snapshot.RetrievedAt);
            if (!QuotaAlertPolicy.IsValid(sample, now)) continue;
            var id = $"{provider}|{window}";
            previous.TryGetValue(id, out var prior);
            if (prior is not null && sample.RetrievedAt <= prior.RetrievedAt) continue;
            events.AddRange(QuotaAlertPolicy.Evaluate(sample, prior, fired, state.Freshness, true, now, evaluatedThresholds));
            // Store the anchored cycle so reset jitter never drifts the identity used by later keys.
            previous[id] = QuotaAlertPolicy.Anchor(sample, prior);
        }
        if (!consent()) return;
        foreach (var alert in events) fired.Add(alert.Key);
        var next = new StoredState(1, fired.ToArray(), previous);
        // Persist before delivery: a crash may lose a notification, never duplicate it.
        await QuotaAlertJson.WriteAsync(_statePath, next, default);
        _state = next;
        foreach (var alert in events)
        {
            lock (_gate) _eligibility.Add(alert, new(consent));
            if (!CanDeliver(alert)) return;
            if (alert.Kind switch
            {
                QuotaAlertKind.Reset => settings.Resets,
                QuotaAlertKind.ThresholdCrossed => settings.Thresholds && settings.ThresholdValues.Contains(alert.Threshold ?? -1) || alert.Threshold == 100 && settings.EdgeGlow,
                QuotaAlertKind.RunsOutBeforeReset => settings.RunOut,
                _ => false
            }) AlertReady?.Invoke(alert);
        }
    }
    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_gate) { _paused = true; _eligibility.Clear(); pending = _tail; }
        await pending.WaitAsync(cancellationToken);
        lock (_gate) { _state = null; _generations.Clear(); _eligibility.Clear(); }
    }
    public void ResumeAfterClear()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _paused = false;
            var previous = _tail;
            _tail = Task.Run(async () =>
            {
                await previous;
                // Clear also resumes after rollback/failure: display the actual surviving file.
                try { SettingsChanged?.Invoke(await Settings.LoadAsync(default)); } catch (Exception) { }
            });
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (_gate) { _disposed = true; _paused = true; pending = _tail; }
        await pending;
    }
    private sealed record StoredState(int Schema, string[] Fired, Dictionary<string, QuotaAlertSample> Previous);
}
