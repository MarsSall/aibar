using AIBar.Domain;

namespace AIBar.Application;

public enum LocalUsageProjectionStatus { Skipped = 1, Completed = 2, Unavailable = 3 }

public sealed class LocalUsageToolOutcome
{
    internal LocalUsageToolOutcome(UsageTool tool, LocalUsageSourceStatus status, int eventsCommitted,
        IEnumerable<LocalUsageWarning>? warnings = null, IEnumerable<LocalUsageDiscoveryWarning>? discoveryWarnings = null)
    {
        if (!Enum.IsDefined(tool) || !Enum.IsDefined(status) || eventsCommitted < 0) throw new ArgumentException("Local usage outcome values are invalid.");
        Tool = tool; Status = status; EventsCommitted = eventsCommitted;
        WarningCodes = Array.AsReadOnly((warnings ?? []).Distinct().Order().ToArray());
        DiscoveryWarningCodes = Array.AsReadOnly((discoveryWarnings ?? []).Distinct().Order().ToArray());
    }
    public UsageTool Tool { get; }
    public LocalUsageSourceStatus Status { get; }
    public int EventsCommitted { get; }
    public IReadOnlyList<LocalUsageWarning> WarningCodes { get; }
    public IReadOnlyList<LocalUsageDiscoveryWarning> DiscoveryWarningCodes { get; }
    public override string ToString() => $"{Tool} local usage outcome: {Status}";
}

public sealed class LocalUsageCoordinatorResult
{
    internal LocalUsageCoordinatorResult(IEnumerable<LocalUsageToolOutcome> tools, LocalUsageProjectionStatus projectionStatus,
        IEnumerable<DailyToolModelUsageFact> projectionFacts)
    {
        Tools = Array.AsReadOnly(tools.OrderBy(item => item.Tool).ToArray()); ProjectionStatus = projectionStatus;
        ProjectionFacts = Array.AsReadOnly(projectionFacts.ToArray());
    }
    public IReadOnlyList<LocalUsageToolOutcome> Tools { get; }
    public LocalUsageProjectionStatus ProjectionStatus { get; }
    public IReadOnlyList<DailyToolModelUsageFact> ProjectionFacts { get; }
    public override string ToString() => "Local usage coordinator result";
}

public sealed class LocalUsageCoordinator : IAsyncDisposable, IAiBarClearWork
{
    private readonly LocalUsageSettings _settings;
    private readonly LocalUsageIdentitySaltStore _saltStore;
    private readonly SqliteUsageEventLedger _ledger;
    private readonly string? _openCodeDataRoot, _piSessionsRoot;
    private readonly TimeZoneLocalDayPolicy _localDayPolicy;
    private readonly PiUsageDiscoveryLayout _piLayout;
    private readonly int _maximumRecords;
    private readonly object _gate = new();
    private Task<LocalUsageCoordinatorResult>? _active;
    private CancellationTokenSource? _runCancellation;
    private Task? _stopTask, _disposeTask;
    private bool _stopping, _disposed;

    public LocalUsageCoordinator(LocalUsageSettings settings, LocalUsageIdentitySaltStore saltStore, SqliteUsageEventLedger ledger,
        string? openCodeDataRoot, string? piSessionsRoot, TimeZoneLocalDayPolicy localDayPolicy,
        PiUsageDiscoveryLayout piLayout = PiUsageDiscoveryLayout.DefaultEncodedDirectories, int maximumRecords = 1000)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings)); _saltStore = saltStore ?? throw new ArgumentNullException(nameof(saltStore));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger)); _localDayPolicy = localDayPolicy ?? throw new ArgumentNullException(nameof(localDayPolicy));
        if (!Enum.IsDefined(piLayout)) throw new ArgumentOutOfRangeException(nameof(piLayout));
        if (maximumRecords is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        _openCodeDataRoot = openCodeDataRoot; _piSessionsRoot = piSessionsRoot; _piLayout = piLayout; _maximumRecords = maximumRecords;
    }

    public ValueTask<LocalUsageCoordinatorResult> StartAsync(CancellationToken cancellationToken = default) => JoinAsync(cancellationToken);
    public ValueTask<LocalUsageCoordinatorResult> RefreshAsync(CancellationToken cancellationToken = default) => JoinAsync(cancellationToken);
    public async ValueTask<LocalUsageCoordinatorResult> ApplyPolicyAsync(LocalUsagePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync().AsTask().WaitAsync(cancellationToken);
        return policy.OpenCodeEnabled || policy.PiEnabled
            ? await RefreshAsync(cancellationToken)
            : new(Failure(policy, LocalUsageSourceStatus.Disabled, null), LocalUsageProjectionStatus.Skipped, []);
    }

    private async ValueTask<LocalUsageCoordinatorResult> JoinAsync(CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested(); Task<LocalUsageCoordinatorResult> run;
        lock (_gate)
        {
            ThrowIfDisposed(); if (_stopping) throw new InvalidOperationException("The local usage coordinator is stopping.");
            run = _active ??= RunGenerationAsync(_runCancellation = new());
        }
        return await run.WaitAsync(callerToken);
    }

    private async Task<LocalUsageCoordinatorResult> RunGenerationAsync(CancellationTokenSource cancellation)
    {
        await Task.Yield();
        try
        {
            var policy = await _settings.LoadAsync(cancellation.Token);
            if (!policy.OpenCodeEnabled && !policy.PiEnabled)
                return new(Failure(policy, LocalUsageSourceStatus.Disabled, null), LocalUsageProjectionStatus.Skipped, []);
            LocalUsageIdentitySaltLease? salt = null;
            try
            {
                salt = await _saltStore.LoadExistingAsync(cancellation.Token);
                if (salt is null)
                {
                    if (await _ledger.HasSourceHistoryAsync(cancellation.Token))
                        return await ProjectAsync(Failure(policy, LocalUsageSourceStatus.RebuildRequired, LocalUsageWarning.RebuildRequired), cancellation.Token);
                    salt = await _saltStore.GetOrCreateAsync(cancellation.Token);
                }
                var openCodeDataRoot = LocalUsageSourceRootResolver.ResolveOpenCodeDataRoot(_openCodeDataRoot, policy.OpenCodeDataRoot);
                var discovery = new LocalUsageSourceDiscovery().Discover(policy,
                    salt.CreateDiscoveryFacts(openCodeDataRoot, _piSessionsRoot, _piLayout), cancellation.Token);
                return await ProjectAsync(await ProcessAsync(policy, discovery, cancellation.Token), cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (LocalUsageIdentitySaltException)
            { return await ProjectAsync(Failure(policy, LocalUsageSourceStatus.Unavailable, LocalUsageWarning.Unavailable), cancellation.Token); }
            catch
            { return await ProjectAsync(Failure(policy, LocalUsageSourceStatus.Failed, LocalUsageWarning.SourceFailed), cancellation.Token); }
            finally { salt?.Dispose(); }
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_runCancellation, cancellation)) { _active = null; _runCancellation = null; }
            cancellation.Dispose();
        }
    }

    private async Task<IReadOnlyList<LocalUsageToolOutcome>> ProcessAsync(LocalUsagePolicy policy, LocalUsageDiscoveryResult discovery, CancellationToken token)
    {
        var outcomes = discovery.Sources.ToDictionary(item => item.Tool, FromDiscovery);
        if (discovery.Registrations.Count > 0)
        {
            var runtime = new LocalUsageRuntime(_settings, _ledger, discovery.Registrations, _maximumRecords);
            try
            {
                try
                {
                    foreach (var result in (await runtime.StartAsync(token)).Sources)
                        outcomes[result.Tool] = FromDirect(result, discovery.Sources.Single(item => item.Tool == result.Tool));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { foreach (var item in discovery.Registrations) outcomes[item.Tool] = Outcome(item.Tool, LocalUsageSourceStatus.Failed, LocalUsageWarning.SourceFailed); }
            }
            finally { await runtime.DisposeAsync(); }
        }
        var pi = discovery.Sources.Single(item => item.Tool == UsageTool.Pi);
        if (pi.Status == LocalUsageDiscoveryStatus.RequiresFanIn)
        {
            try { outcomes[UsageTool.Pi] = FromFanIn(await new PiUsageFanInRuntime(_settings, _ledger, discovery.CreatePiFanInInput(), _maximumRecords).RunAsync(token), pi); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { outcomes[UsageTool.Pi] = Outcome(UsageTool.Pi, LocalUsageSourceStatus.Failed, LocalUsageWarning.SourceFailed, pi.WarningCodes); }
        }
        return [outcomes[UsageTool.OpenCode], outcomes[UsageTool.Pi]];
    }

    private async Task<LocalUsageCoordinatorResult> ProjectAsync(IReadOnlyList<LocalUsageToolOutcome> outcomes, CancellationToken token)
    {
        try { return new(outcomes, LocalUsageProjectionStatus.Completed, await new UsageEventProjector(_ledger, _localDayPolicy).RebuildAsync(token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(outcomes, LocalUsageProjectionStatus.Unavailable, []); }
    }

    private static IReadOnlyList<LocalUsageToolOutcome> Failure(LocalUsagePolicy policy, LocalUsageSourceStatus status, LocalUsageWarning? warning) =>
        [policy.OpenCodeEnabled ? Outcome(UsageTool.OpenCode, status, warning) : Outcome(UsageTool.OpenCode, LocalUsageSourceStatus.Disabled),
         policy.PiEnabled ? Outcome(UsageTool.Pi, status, warning) : Outcome(UsageTool.Pi, LocalUsageSourceStatus.Disabled)];
    private static LocalUsageToolOutcome FromDiscovery(LocalUsageDiscoverySummary source) => source.Status == LocalUsageDiscoveryStatus.Disabled
        ? Outcome(source.Tool, LocalUsageSourceStatus.Disabled) : source.Status == LocalUsageDiscoveryStatus.Unavailable
        ? Outcome(source.Tool, LocalUsageSourceStatus.Unavailable, LocalUsageWarning.Unavailable, source.WarningCodes)
        : Outcome(source.Tool, LocalUsageSourceStatus.Failed, LocalUsageWarning.SourceFailed, source.WarningCodes);
    private static LocalUsageToolOutcome FromDirect(LocalUsageSourceRunResult result, LocalUsageDiscoverySummary discovery) =>
        new(result.Tool, Normalize(result.Status, discovery.WarningCodes), result.EventsCommitted, result.WarningCodes, discovery.WarningCodes);
    private static LocalUsageToolOutcome FromFanIn(PiUsageFanInResult result, LocalUsageDiscoverySummary discovery) =>
        new(UsageTool.Pi, Normalize(Map(result.Status), discovery.WarningCodes), result.EventsCommitted, result.WarningCodes, discovery.WarningCodes);
    private static LocalUsageSourceStatus Normalize(LocalUsageSourceStatus status, IReadOnlyList<LocalUsageDiscoveryWarning> warnings) =>
        status == LocalUsageSourceStatus.Completed && warnings.Count > 0 ? LocalUsageSourceStatus.Partial : status;
    private static LocalUsageSourceStatus Map(PiUsageFanInStatus status) => status switch
    { PiUsageFanInStatus.Disabled => LocalUsageSourceStatus.Disabled, PiUsageFanInStatus.Completed => LocalUsageSourceStatus.Completed,
      PiUsageFanInStatus.Partial => LocalUsageSourceStatus.Partial, PiUsageFanInStatus.Unavailable => LocalUsageSourceStatus.Unavailable,
      PiUsageFanInStatus.RebuildRequired => LocalUsageSourceStatus.RebuildRequired, _ => LocalUsageSourceStatus.Failed };
    private static LocalUsageToolOutcome Outcome(UsageTool tool, LocalUsageSourceStatus status, LocalUsageWarning? warning = null,
        IEnumerable<LocalUsageDiscoveryWarning>? discoveryWarnings = null) => new(tool, status, 0, warning is null ? [] : [warning.Value], discoveryWarnings);

    public ValueTask StopAsync()
    {
        lock (_gate)
        {
            if (_disposed) return new(_disposeTask ?? Task.CompletedTask); if (_stopTask is not null) return new(_stopTask);
            _stopping = true; _runCancellation?.Cancel(); return new(_stopTask = FinishStopAsync(_active));
        }
    }
    async ValueTask IAiBarClearWork.CancelAndWaitAsync(CancellationToken cancellationToken) =>
        await StopAsync().AsTask().WaitAsync(cancellationToken);
    void IAiBarClearWork.ResumeAfterClear() { }
    private async Task FinishStopAsync(Task? active)
    { await Task.Yield(); try { if (active is not null) await active; } catch (OperationCanceledException) { } finally { lock (_gate) { _stopping = false; _stopTask = null; } } }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask); _disposed = true; _stopping = true; _runCancellation?.Cancel();
            return new(_disposeTask = FinishDisposeAsync(_active));
        }
    }
    private async Task FinishDisposeAsync(Task? active)
    { await Task.Yield(); try { if (active is not null) await active; } catch (OperationCanceledException) { } finally { lock (_gate) _stopping = false; } }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
