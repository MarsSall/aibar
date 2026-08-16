using AIBar.Domain;

namespace AIBar.Application;

public enum LocalUsageAdapterKind { OpenCodeSqliteV1 = 1, PiJsonlV3 = 2 }
public enum LocalUsageRegistrationAdmissionPolicy { UnverifiedManual = 1, DiscoveryProof = 2 }
public enum LocalUsageSourceStatus { Disabled = 1, Completed = 2, Partial = 3, Unavailable = 4, RebuildRequired = 5, Failed = 6 }
public enum LocalUsageWarning
{
    Unavailable = 1, UnsupportedSchema = 2, MalformedRecord = 3, Truncated = 4, RebuildRequired = 5,
    OversizedRecord = 6, IncompleteTail = 7, InvalidAdapterResult = 8, CheckpointConflict = 9,
    EventCollision = 10, SourceFailed = 11, PathChanged = 12
}

public sealed class LocalUsageSourceRegistration
{
    public LocalUsageSourceRegistration(UsageTool tool, LocalUsageAdapterKind adapter, string path,
        UsageSourceIdentity identity, int sourceSchemaVersion)
        : this(tool, adapter, path, identity, sourceSchemaVersion, null) { }
    internal LocalUsageSourceRegistration(UsageTool tool, LocalUsageAdapterKind adapter, string path,
        UsageSourceIdentity identity, int sourceSchemaVersion, ILocalUsageSourceAdmission? admission)
    {
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(tool) || !Enum.IsDefined(adapter)) throw new ArgumentOutOfRangeException(nameof(tool));
        var matches = adapter switch
        {
            LocalUsageAdapterKind.OpenCodeSqliteV1 => tool == UsageTool.OpenCode && sourceSchemaVersion == OpenCodeSqliteUsageSourceAdapter.SupportedSourceSchemaVersion,
            LocalUsageAdapterKind.PiJsonlV3 => tool == UsageTool.Pi && sourceSchemaVersion == PiJsonlUsageSourceAdapter.SupportedSourceSchemaVersion,
            _ => false
        };
        if (!matches) throw new ArgumentException("The tool, adapter, and source schema version must match.");
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An explicit source path is required.", nameof(path));
        var fullPath = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetDirectoryName(fullPath) is null) throw new ArgumentException("A source path cannot be a filesystem root.", nameof(path));
        Tool = tool; Adapter = adapter; Path = fullPath; Identity = identity; SourceSchemaVersion = sourceSchemaVersion; Admission = admission;
    }
    public UsageTool Tool { get; }
    public LocalUsageAdapterKind Adapter { get; }
    public string Path { get; }
    public UsageSourceIdentity Identity { get; }
    public int SourceSchemaVersion { get; }
    public LocalUsageRegistrationAdmissionPolicy AdmissionPolicy => Admission is null ? LocalUsageRegistrationAdmissionPolicy.UnverifiedManual : LocalUsageRegistrationAdmissionPolicy.DiscoveryProof;
    internal ILocalUsageSourceAdmission? Admission { get; }
}

public sealed record LocalUsageSourceRunResult(UsageTool Tool, LocalUsageSourceStatus Status, int RecordsRead,
    int RecordsIgnored, int RecordsMalformed, int EventsPrepared, int EventsCommitted, IReadOnlyList<LocalUsageWarning> WarningCodes);
public sealed record LocalUsageRunResult(IReadOnlyList<LocalUsageSourceRunResult> Sources);
internal sealed record LocalUsagePreparedBatch(IReadOnlyList<NormalizedUsageEvent> Events, int RecordsRead,
    int RecordsIgnored, int RecordsMalformed, IReadOnlyList<LocalUsageWarning> Warnings, UsageSourceCheckpoint? Checkpoint);

public sealed class LocalUsageRuntime : IAsyncDisposable
{
    private readonly LocalUsageSettings _settings;
    private readonly SqliteUsageEventLedger _ledger;
    private readonly IReadOnlyList<LocalUsageSourceRegistration> _registrations;
    private readonly int _maximumRecords;
    private readonly Func<LocalUsageSourceRegistration, UsageSourceCheckpoint?, int, CancellationToken, ValueTask<LocalUsagePreparedBatch>> _prepare;
    private readonly Func<TimeZoneLocalDayPolicy, CancellationToken, ValueTask<IReadOnlyList<DailyToolModelUsageFact>>> _project;
    private readonly Action<UsageTool>? _afterSource;
    private readonly object _gate = new();
    private Task<LocalUsageRunResult>? _active;
    private CancellationTokenSource? _runCancellation;
    private Task<IReadOnlyList<DailyToolModelUsageFact>>? _projection;
    private CancellationTokenSource? _projectCancellation;
    private Task? _stopTask, _disposeTask;
    private bool _stopping, _disposed;

    public LocalUsageRuntime(LocalUsageSettings settings, SqliteUsageEventLedger ledger,
        IEnumerable<LocalUsageSourceRegistration> registrations, int maximumRecords = 1000)
        : this(settings, ledger, registrations, maximumRecords, PrepareAsync,
            (policy, token) => new UsageEventProjector(ledger, policy).RebuildAsync(token)) { }

    internal LocalUsageRuntime(LocalUsageSettings settings, SqliteUsageEventLedger ledger,
        IEnumerable<LocalUsageSourceRegistration> registrations, int maximumRecords,
        Func<LocalUsageSourceRegistration, UsageSourceCheckpoint?, int, CancellationToken, ValueTask<LocalUsagePreparedBatch>> prepare,
        Func<TimeZoneLocalDayPolicy, CancellationToken, ValueTask<IReadOnlyList<DailyToolModelUsageFact>>>? project = null,
        Action<UsageTool>? afterSource = null)
    {
        ArgumentNullException.ThrowIfNull(settings); ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(registrations); ArgumentNullException.ThrowIfNull(prepare);
        if (maximumRecords is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        var snapshot = registrations.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null) || snapshot.Select(item => item.Tool).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Each tool may be registered once.", nameof(registrations));
        if (snapshot.Select(item => item.Identity).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("A source identity cannot be shared across tools.", nameof(registrations));
        if (snapshot.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
            throw new ArgumentException("A source path cannot be shared across tools.", nameof(registrations));
        _settings = settings; _ledger = ledger; _registrations = Array.AsReadOnly(snapshot);
        _maximumRecords = maximumRecords; _prepare = prepare; _project = project ?? ((policy, token) => new UsageEventProjector(ledger, policy).RebuildAsync(token)); _afterSource = afterSource;
    }

    public ValueTask<LocalUsageRunResult> StartAsync(CancellationToken cancellationToken = default) => JoinAsync(cancellationToken);
    public ValueTask<LocalUsageRunResult> RefreshAsync(CancellationToken cancellationToken = default) => JoinAsync(cancellationToken);
    public async ValueTask<IReadOnlyList<DailyToolModelUsageFact>> ProjectAsync(TimeZoneLocalDayPolicy policy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Task<IReadOnlyList<DailyToolModelUsageFact>> project;
        lock (_gate)
        {
            ThrowIfDisposed(); if (_stopping) throw new InvalidOperationException("The local usage runtime is stopping.");
            var cancellation = _projectCancellation ??= new(); project = ProjectAfterAsync(_projection, policy, cancellation.Token); _projection = project;
        }
        return await project.WaitAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<DailyToolModelUsageFact>> ProjectAfterAsync(Task<IReadOnlyList<DailyToolModelUsageFact>>? prior, TimeZoneLocalDayPolicy policy, CancellationToken token)
    { if (prior is not null) try { await prior; } catch when (!token.IsCancellationRequested) { } token.ThrowIfCancellationRequested(); var result = await _project(policy, token); token.ThrowIfCancellationRequested(); return result; }

    private async ValueTask<LocalUsageRunResult> JoinAsync(CancellationToken callerToken)
    {
        callerToken.ThrowIfCancellationRequested();
        Task<LocalUsageRunResult> run;
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_stopping) throw new InvalidOperationException("The local usage runtime is stopping.");
            run = _active ??= RunGenerationAsync(_runCancellation = new());
        }
        return await run.WaitAsync(callerToken);
    }

    private async Task<LocalUsageRunResult> RunGenerationAsync(CancellationTokenSource cancellation)
    {
        await Task.Yield();
        try
        {
            var policy = await _settings.LoadAsync(cancellation.Token); var results = new List<LocalUsageSourceRunResult>();
            foreach (var registration in _registrations)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!policy.IsEnabled(registration.Tool)) { results.Add(Result(registration.Tool, LocalUsageSourceStatus.Disabled)); continue; }
                results.Add(await ProcessAsync(registration, cancellation.Token));
                _afterSource?.Invoke(registration.Tool);
            }
            cancellation.Token.ThrowIfCancellationRequested();
            return new(Array.AsReadOnly(results.ToArray()));
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_runCancellation, cancellation)) { _active = null; _runCancellation = null; }
            cancellation.Dispose();
        }
    }

    private async Task<LocalUsageSourceRunResult> ProcessAsync(LocalUsageSourceRegistration registration, CancellationToken token)
    {
        LocalUsagePreparedBatch? prepared = null;
        try
        {
            var expected = await _ledger.LoadCheckpointAsync(registration.Identity, registration.Tool, token);
            var admission = registration.Admission?.Validate(token) ?? LocalUsageAdmissionStatus.Admitted;
            if (admission != LocalUsageAdmissionStatus.Admitted) return await AdmissionFailedAsync(registration, admission, token);
            var batch = await _prepare(registration, expected, _maximumRecords, token);
            admission = registration.Admission?.Validate(token) ?? LocalUsageAdmissionStatus.Admitted;
            if (admission != LocalUsageAdmissionStatus.Admitted) return await AdmissionFailedAsync(registration, admission, token, batch);
            if (!Valid(registration, expected, batch)) return await FailedAsync(registration, LocalUsageSourceStatus.Failed, [LocalUsageWarning.InvalidAdapterResult], token);
            prepared = batch;
            if (batch.Checkpoint is null)
            {
                await UpsertSourceAsync(registration, UsageSourceState.Unavailable, batch.Warnings, token);
                var status = batch.Warnings.Contains(LocalUsageWarning.RebuildRequired) ? LocalUsageSourceStatus.RebuildRequired : LocalUsageSourceStatus.Unavailable;
                return Result(registration.Tool, status, batch.RecordsRead, batch.RecordsIgnored, batch.RecordsMalformed, batch.Events.Count, 0, batch.Warnings);
            }
            await UpsertSourceAsync(registration, UsageSourceState.Active, batch.Warnings, token);
            var writes = await _ledger.CommitBatchAsync(expected, batch.Checkpoint, batch.Events, token);
            if (writes.Any(item => item.State == UsageEventWriteState.Collision))
                return await FailedAsync(registration, LocalUsageSourceStatus.Failed, [.. batch.Warnings, LocalUsageWarning.EventCollision], token, batch);
            return Result(registration.Tool, batch.Warnings.Count == 0 ? LocalUsageSourceStatus.Completed : LocalUsageSourceStatus.Partial,
                batch.RecordsRead, batch.RecordsIgnored, batch.RecordsMalformed, batch.Events.Count,
                writes.Count(item => item.State is UsageEventWriteState.Inserted or UsageEventWriteState.Updated), batch.Warnings);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (LocalUsageAdmissionException exception) { return await AdmissionFailedAsync(registration, exception.Status, token); }
        catch (UsageCheckpointConflictException) { return await FailedAsync(registration, LocalUsageSourceStatus.Failed, [LocalUsageWarning.CheckpointConflict], token, prepared); }
        catch (Exception) { return await FailedAsync(registration, LocalUsageSourceStatus.Failed, [LocalUsageWarning.SourceFailed], token, prepared); }
    }

    private Task<LocalUsageSourceRunResult> AdmissionFailedAsync(LocalUsageSourceRegistration registration,
        LocalUsageAdmissionStatus admission, CancellationToken token, LocalUsagePreparedBatch? batch = null) =>
        FailedAsync(registration, admission == LocalUsageAdmissionStatus.PathChanged ? LocalUsageSourceStatus.RebuildRequired : LocalUsageSourceStatus.Unavailable,
            [admission == LocalUsageAdmissionStatus.PathChanged ? LocalUsageWarning.PathChanged : LocalUsageWarning.Unavailable], token, batch);

    private async Task<LocalUsageSourceRunResult> FailedAsync(LocalUsageSourceRegistration registration, LocalUsageSourceStatus status,
        IReadOnlyList<LocalUsageWarning> warnings, CancellationToken token, LocalUsagePreparedBatch? batch = null)
    {
        try { await UpsertSourceAsync(registration, UsageSourceState.Unavailable, warnings, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { warnings = [LocalUsageWarning.SourceFailed]; }
        return Result(registration.Tool, status, batch?.RecordsRead ?? 0, batch?.RecordsIgnored ?? 0, batch?.RecordsMalformed ?? 0, batch?.Events.Count ?? 0, 0, warnings);
    }

    private ValueTask UpsertSourceAsync(LocalUsageSourceRegistration registration, UsageSourceState state,
        IReadOnlyList<LocalUsageWarning> warnings, CancellationToken token)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var flags = warnings.Distinct().Aggregate(0, (value, warning) => (int)warning is >= 1 and <= 30 ? value | 1 << ((int)warning - 1) : value | 1 << ((int)LocalUsageWarning.InvalidAdapterResult - 1));
        return _ledger.UpsertSourceAsync(new(registration.Identity, registration.Tool, 1, registration.SourceSchemaVersion, now, now, state, flags), token);
    }

    private bool Valid(LocalUsageSourceRegistration registration, UsageSourceCheckpoint? expected, LocalUsagePreparedBatch? batch)
    {
        if (batch?.Events is null || batch.Warnings is null || batch.RecordsRead < 0 || batch.RecordsIgnored < 0 || batch.RecordsMalformed < 0
            || batch.RecordsRead > _maximumRecords || (long)batch.Events.Count + batch.RecordsIgnored + batch.RecordsMalformed != batch.RecordsRead || batch.Warnings.Any(warning => !Enum.IsDefined(warning))) return false;
        if (batch.Checkpoint is null) return batch.Events.Count == 0 && batch.Warnings.Count > 0;
        var checkpoint = batch.Checkpoint;
        return checkpoint.SourceIdentity.Equals(registration.Identity) && checkpoint.Tool == registration.Tool && checkpoint.SourceSchemaVersion == registration.SourceSchemaVersion
            && (expected is null || checkpoint.Cursor >= expected.Cursor) && batch.Events.All(item => item.SourceIdentity.Equals(registration.Identity) && item.Tool == registration.Tool);
    }

    private static LocalUsageSourceRunResult Result(UsageTool tool, LocalUsageSourceStatus status, int read = 0, int ignored = 0,
        int malformed = 0, int prepared = 0, int committed = 0, IEnumerable<LocalUsageWarning>? warnings = null) =>
        new(tool, status, read, ignored, malformed, prepared, committed, Array.AsReadOnly((warnings ?? []).Distinct().Order().ToArray()));

    private static async ValueTask<LocalUsagePreparedBatch> PrepareAsync(LocalUsageSourceRegistration registration,
        UsageSourceCheckpoint? checkpoint, int maximumRecords, CancellationToken token)
    {
        if (registration.Adapter == LocalUsageAdapterKind.OpenCodeSqliteV1)
        {
            var result = await new OpenCodeSqliteUsageSourceAdapter(registration.Path, new(registration.Identity, registration.SourceSchemaVersion), () => EnsureAdmitted(registration, token)).PrepareAsync(checkpoint, maximumRecords, token);
            return new(result.Events, result.RecordsRead, result.RecordsIgnored, result.RecordsMalformed, result.Warnings.Select(Map).ToArray(), result.NextCheckpoint);
        }
        var pi = await new PiJsonlUsageSourceAdapter(registration.Path, new(registration.Identity, registration.SourceSchemaVersion), () => EnsureAdmitted(registration, token)).PrepareAsync(checkpoint, maximumRecords, token);
        return new(pi.Events, pi.RecordsRead, pi.RecordsIgnored, pi.RecordsMalformed, pi.Warnings.Select(Map).ToArray(), pi.NextCheckpoint);
    }
    private static void EnsureAdmitted(LocalUsageSourceRegistration registration, CancellationToken token)
    { var status = registration.Admission?.Validate(token) ?? LocalUsageAdmissionStatus.Admitted; if (status != LocalUsageAdmissionStatus.Admitted) throw new LocalUsageAdmissionException(status); }
    private sealed class LocalUsageAdmissionException(LocalUsageAdmissionStatus status) : Exception { internal LocalUsageAdmissionStatus Status { get; } = status; }
    private static LocalUsageWarning Map(OpenCodeUsageReadWarning warning) => warning switch { OpenCodeUsageReadWarning.Unavailable => LocalUsageWarning.Unavailable, OpenCodeUsageReadWarning.UnsupportedSchema => LocalUsageWarning.UnsupportedSchema, OpenCodeUsageReadWarning.MalformedRecord => LocalUsageWarning.MalformedRecord, OpenCodeUsageReadWarning.Truncated => LocalUsageWarning.Truncated, OpenCodeUsageReadWarning.RebuildRequired => LocalUsageWarning.RebuildRequired, _ => LocalUsageWarning.InvalidAdapterResult };
    private static LocalUsageWarning Map(PiUsageReadWarning warning) => warning switch { PiUsageReadWarning.Unavailable => LocalUsageWarning.Unavailable, PiUsageReadWarning.UnsupportedSchema => LocalUsageWarning.UnsupportedSchema, PiUsageReadWarning.MalformedRecord => LocalUsageWarning.MalformedRecord, PiUsageReadWarning.OversizedRecord => LocalUsageWarning.OversizedRecord, PiUsageReadWarning.IncompleteTail => LocalUsageWarning.IncompleteTail, PiUsageReadWarning.Truncated => LocalUsageWarning.Truncated, PiUsageReadWarning.RebuildRequired => LocalUsageWarning.RebuildRequired, _ => LocalUsageWarning.InvalidAdapterResult };

    public ValueTask StopAsync()
    {
        lock (_gate)
        {
            if (_disposed) return new(_disposeTask ?? Task.CompletedTask);
            if (_stopTask is not null) return new(_stopTask);
            _stopping = true; _runCancellation?.Cancel(); _projectCancellation?.Cancel(); return new(_stopTask = FinishStopAsync(Work()));
        }
    }
    private async Task FinishStopAsync(Task? run)
    {
        await Task.Yield(); try { if (run is not null) await run; } catch (OperationCanceledException) { }
        finally { lock (_gate) { _stopping = false; _stopTask = null; _projection = null; _projectCancellation?.Dispose(); _projectCancellation = null; } }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            _disposed = true; _stopping = true; _runCancellation?.Cancel(); _projectCancellation?.Cancel(); return new(_disposeTask = FinishDisposeAsync(Work()));
        }
    }
    private async Task FinishDisposeAsync(Task? run)
    { await Task.Yield(); try { if (run is not null) await run; } catch (OperationCanceledException) { } finally { lock (_gate) { _stopping = false; _projection = null; _projectCancellation?.Dispose(); _projectCancellation = null; } } }
    private Task Work() => Task.WhenAll(_active ?? Task.CompletedTask, _projection ?? Task.FromResult<IReadOnlyList<DailyToolModelUsageFact>>([]));
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
