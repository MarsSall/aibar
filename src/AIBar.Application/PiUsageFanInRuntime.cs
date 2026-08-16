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
