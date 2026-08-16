using AIBar.Domain;

namespace AIBar.Application;

public enum PiUsageFanInStatus { Disabled = 1, Completed = 2, Partial = 3, Unavailable = 4, RebuildRequired = 5, Failed = 6 }

public sealed class PiUsageFanInResult
{
    internal PiUsageFanInResult(PiUsageFanInStatus status, int attempted, int completed, int partial, int failed, int rebuild,
        int unavailable, int records, int events, IEnumerable<LocalUsageWarning> warnings)
    {
        if (attempted < 0 || completed < 0 || partial < 0 || failed < 0 || rebuild < 0 || unavailable < 0 || records < 0 || events < 0
            || (long)completed + partial + failed + rebuild + unavailable != attempted) throw new ArgumentException("Fan-in result counts are inconsistent.");
        Status = status; CandidatesAttempted = attempted; CandidatesCompleted = completed; CandidatesPartial = partial; CandidatesFailed = failed;
        CandidatesRebuildRequired = rebuild; CandidatesUnavailable = unavailable; RecordsCheckpointed = records; EventsCommitted = events;
        WarningCodes = Array.AsReadOnly(warnings.Distinct().Order().Take(16).ToArray());
    }
    public PiUsageFanInStatus Status { get; }
    public int CandidatesAttempted { get; }
    public int CandidatesCompleted { get; }
    public int CandidatesPartial { get; }
    public int CandidatesFailed { get; }
    public int CandidatesRebuildRequired { get; }
    public int CandidatesUnavailable { get; }
    public int RecordsCheckpointed { get; }
    public int EventsCommitted { get; }
    public IReadOnlyList<LocalUsageWarning> WarningCodes { get; }
}

internal sealed class PiUsageFanInInput
{
    private readonly IReadOnlyList<LocalUsageSourceRegistration> _registrations;
    internal PiUsageFanInInput(IEnumerable<LocalUsageSourceRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations); var snapshot = registrations.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null || item.Tool != UsageTool.Pi
            || item.Adapter != LocalUsageAdapterKind.PiJsonlV3 || item.SourceSchemaVersion != PiJsonlUsageSourceAdapter.SupportedSourceSchemaVersion
            || item.AdmissionPolicy != LocalUsageRegistrationAdmissionPolicy.DiscoveryProof))
            throw new ArgumentException("Fan-in requires proof-bearing Pi discovery candidates.", nameof(registrations));
        if (snapshot.Select(item => item.Identity).Distinct().Count() != snapshot.Length
            || snapshot.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != snapshot.Length)
            throw new ArgumentException("Fan-in candidate identities and paths must be unique.", nameof(registrations));
        _registrations = Array.AsReadOnly(snapshot);
    }
    internal IReadOnlyList<LocalUsageSourceRegistration> Registrations => _registrations;
}

public sealed class PiUsageFanInRuntime
{
    private readonly LocalUsageSettings _settings;
    private readonly SqliteUsageEventLedger _ledger;
    private readonly PiUsageFanInInput _input;
    private readonly int _maximumRecords;
    private readonly Action<int>? _beforeCandidate;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    internal PiUsageFanInRuntime(LocalUsageSettings settings, SqliteUsageEventLedger ledger, PiUsageFanInInput input,
        int maximumRecords = 1000, Action<int>? beforeCandidate = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings)); _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        if (maximumRecords is < 1 or > PiJsonlUsageSourceAdapter.MaximumRecords) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        _maximumRecords = maximumRecords; _beforeCandidate = beforeCandidate;
    }

    public async ValueTask<PiUsageFanInResult> RunAsync(CancellationToken cancellationToken = default)
    {
        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var policy = await _settings.LoadAsync(cancellationToken);
            if (!policy.PiEnabled) return Result(PiUsageFanInStatus.Disabled);
            var outcomes = new List<CandidateOutcome>(); var ordinal = 0;
            foreach (var registration in _input.Registrations.OrderBy(item => item.Path, StringComparer.Ordinal))
            {
                _beforeCandidate?.Invoke(ordinal++); cancellationToken.ThrowIfCancellationRequested();
                outcomes.Add(await ProcessAsync(registration, cancellationToken));
            }
            var completed = outcomes.Count(item => item.Status == PiUsageFanInStatus.Completed);
            var partial = outcomes.Count(item => item.Status == PiUsageFanInStatus.Partial);
            var failed = outcomes.Count(item => item.Status == PiUsageFanInStatus.Failed);
            var rebuild = outcomes.Count(item => item.Status == PiUsageFanInStatus.RebuildRequired);
            var unavailable = outcomes.Count(item => item.Status == PiUsageFanInStatus.Unavailable);
            var succeeded = completed + partial;
            var status = completed == outcomes.Count ? PiUsageFanInStatus.Completed
                : succeeded > 0 ? PiUsageFanInStatus.Partial
                : failed > 0 ? PiUsageFanInStatus.Failed
                : rebuild > 0 ? PiUsageFanInStatus.RebuildRequired : PiUsageFanInStatus.Unavailable;
            return new(status, outcomes.Count, completed, partial, failed, rebuild, unavailable, outcomes.Sum(item => item.RecordsCheckpointed),
                outcomes.Sum(item => item.EventsCommitted), outcomes.SelectMany(item => item.Warnings));
        }
        finally { _runGate.Release(); }
    }

    private async Task<CandidateOutcome> ProcessAsync(LocalUsageSourceRegistration registration, CancellationToken token)
    {
        PiUsageReadResult? batch = null;
        try
        {
            var expected = await _ledger.LoadCheckpointAsync(registration.Identity, UsageTool.Pi, token);
            EnsureAdmitted(registration, token);
            batch = await new PiJsonlUsageSourceAdapter(registration.Path, new(registration.Identity, registration.SourceSchemaVersion),
                () => EnsureAdmitted(registration, token)).PrepareAsync(expected, _maximumRecords, token);
            EnsureAdmitted(registration, token);
            var warnings = batch.Warnings.Select(Map).ToArray();
            if (!Valid(registration, expected, batch)) return await Fail(registration, PiUsageFanInStatus.Failed, [LocalUsageWarning.InvalidAdapterResult], token);
            if (batch.NextCheckpoint is null)
            {
                var status = warnings.Contains(LocalUsageWarning.RebuildRequired) ? PiUsageFanInStatus.RebuildRequired : PiUsageFanInStatus.Unavailable;
                return await Fail(registration, status, warnings, token);
            }
            await Source(registration, UsageSourceState.Active, warnings, token);
            var writes = await _ledger.CommitBatchAsync(expected, batch.NextCheckpoint, batch.Events, token);
            if (writes.Any(item => item.State == UsageEventWriteState.Collision))
                return await Fail(registration, PiUsageFanInStatus.Failed, [.. warnings, LocalUsageWarning.EventCollision], token);
            return new(warnings.Length == 0 ? PiUsageFanInStatus.Completed : PiUsageFanInStatus.Partial, batch.RecordsRead,
                writes.Count(item => item.State is UsageEventWriteState.Inserted or UsageEventWriteState.Updated), warnings);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (AdmissionException exception) { return await Fail(registration, exception.Status == LocalUsageAdmissionStatus.PathChanged ? PiUsageFanInStatus.RebuildRequired : PiUsageFanInStatus.Unavailable, [exception.Status == LocalUsageAdmissionStatus.PathChanged ? LocalUsageWarning.PathChanged : LocalUsageWarning.Unavailable], token); }
        catch (UsageCheckpointConflictException) { return await Fail(registration, PiUsageFanInStatus.Failed, [LocalUsageWarning.CheckpointConflict], token); }
        catch { return await Fail(registration, PiUsageFanInStatus.Failed, [LocalUsageWarning.SourceFailed], token); }
    }

    private async Task<CandidateOutcome> Fail(LocalUsageSourceRegistration registration, PiUsageFanInStatus status,
        IReadOnlyList<LocalUsageWarning> warnings, CancellationToken token)
    {
        try { await Source(registration, UsageSourceState.Unavailable, warnings, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { status = PiUsageFanInStatus.Failed; warnings = [LocalUsageWarning.SourceFailed]; }
        return new(status, 0, 0, warnings);
    }

    private ValueTask Source(LocalUsageSourceRegistration registration, UsageSourceState state, IReadOnlyList<LocalUsageWarning> warnings, CancellationToken token)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var flags = warnings.Distinct().Aggregate(0, (value, warning) => value | 1 << ((int)(Enum.IsDefined(warning) ? warning : LocalUsageWarning.InvalidAdapterResult) - 1));
        return _ledger.UpsertSourceAsync(new(registration.Identity, UsageTool.Pi, 1, registration.SourceSchemaVersion, now, now, state, flags), token);
    }

    private bool Valid(LocalUsageSourceRegistration registration, UsageSourceCheckpoint? expected, PiUsageReadResult batch)
    {
        if (batch.RecordsRead < 0 || batch.RecordsIgnored < 0 || batch.RecordsMalformed < 0 || batch.RecordsRead > _maximumRecords
            || (long)batch.Events.Count + batch.RecordsIgnored + batch.RecordsMalformed != batch.RecordsRead || batch.Warnings.Any(warning => !Enum.IsDefined(warning))) return false;
        if (batch.NextCheckpoint is null) return batch.Events.Count == 0 && batch.Warnings.Count > 0;
        return batch.NextCheckpoint.SourceIdentity.Equals(registration.Identity) && batch.NextCheckpoint.Tool == UsageTool.Pi
            && batch.NextCheckpoint.SourceSchemaVersion == registration.SourceSchemaVersion && (expected is null || batch.NextCheckpoint.Cursor >= expected.Cursor)
            && batch.Events.All(item => item.SourceIdentity.Equals(registration.Identity) && item.Tool == UsageTool.Pi);
    }

    private static void EnsureAdmitted(LocalUsageSourceRegistration registration, CancellationToken token)
    { var status = registration.Admission!.Validate(token); if (status != LocalUsageAdmissionStatus.Admitted) throw new AdmissionException(status); }
    private static LocalUsageWarning Map(PiUsageReadWarning warning) => warning switch { PiUsageReadWarning.Unavailable => LocalUsageWarning.Unavailable, PiUsageReadWarning.UnsupportedSchema => LocalUsageWarning.UnsupportedSchema, PiUsageReadWarning.MalformedRecord => LocalUsageWarning.MalformedRecord, PiUsageReadWarning.OversizedRecord => LocalUsageWarning.OversizedRecord, PiUsageReadWarning.IncompleteTail => LocalUsageWarning.IncompleteTail, PiUsageReadWarning.Truncated => LocalUsageWarning.Truncated, PiUsageReadWarning.RebuildRequired => LocalUsageWarning.RebuildRequired, _ => LocalUsageWarning.InvalidAdapterResult };
    private static PiUsageFanInResult Result(PiUsageFanInStatus status) => new(status, 0, 0, 0, 0, 0, 0, 0, 0, []);
    private sealed record CandidateOutcome(PiUsageFanInStatus Status, int RecordsCheckpointed, int EventsCommitted, IReadOnlyList<LocalUsageWarning> Warnings);
    private sealed class AdmissionException(LocalUsageAdmissionStatus status) : Exception { internal LocalUsageAdmissionStatus Status { get; } = status; }
}
