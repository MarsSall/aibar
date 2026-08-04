using System.Security.Cryptography;

namespace AIBar.Packaging.Supervisor;

internal enum CleanupFaultPoint : byte
{
    None,
    BeforeFirstDelete,
    AfterFirstDelete,
    DescendantIdentityMismatch,
    DescendantKindMismatch,
    DescendantVolumeMismatch,
    DescendantReparseDetected,
    DescendantReopenFailure,
    NativeDeleteFailure,
    UnknownFault
}
internal enum CleanupGate : byte { None, Bijection, Metadata, Revalidation, DeleteAttempted, Deleted }

public sealed class GuardedCleanupFacade : IDisposable
{
    private readonly RetainedTreeSession _session;
    private readonly ICommittedEvidenceCapabilityFacet _evidence;
    private readonly IDurableProtectedMetadataStore _metadataStore;
    private readonly CleanupFaultPoint _fault;
    private static readonly object ConsumedPairsGate = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RetainedTreeSession, ICommittedEvidenceCapabilityFacet> ConsumedPairs = new();
    private int _started;
    private int _disposed;
    internal CleanupGate LastGate { get; private set; }
    internal int CompletedDeleteCount { get; private set; }

    private GuardedCleanupFacade(RetainedTreeSession session, ICommittedEvidenceCapabilityFacet evidence, IDurableProtectedMetadataStore metadataStore, CleanupFaultPoint fault)
        => (_session, _evidence, _metadataStore, _fault) = (session, evidence, metadataStore, fault);

    internal static bool TryCreate(RetainedTreeSession? session, ICommittedEvidenceCapabilityFacet? evidence, IDurableProtectedMetadataStore? metadataStore, out GuardedCleanupFacade? facade, out SupervisorStatus status, CleanupFaultPoint fault = CleanupFaultPoint.None)
    {
        facade = null;
        if (session is null || evidence is null || metadataStore is null || evidence.IsDisposed || !session.Matches(evidence)) { status = SupervisorStatus.CleanupPartial; return false; }
        lock (ConsumedPairsGate)
        {
            if (ConsumedPairs.TryGetValue(session, out _)) { status = SupervisorStatus.CleanupPartial; return false; }
            ConsumedPairs.Add(session, evidence);
        }
        facade = new(session, evidence, metadataStore, fault);
        status = SupervisorStatus.Success;
        return true;
    }

    public bool TryCleanup(out SupervisorStatus status)
    {
        status = SupervisorStatus.CleanupPartial;
        if (Interlocked.Exchange(ref _started, 1) != 0 || Volatile.Read(ref _disposed) != 0 || !TryMatchEvidence(out var leases)) return false;
        var allLeases = leases.ToList();
        LastGate = CleanupGate.Bijection;
        try
        {
            var metadata = new CleanupRetryMetadata(CleanupMetadataPhase.RetainedQuarantine, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (!ScavengerMetadata.TryProtectAndVerify(_evidence.Evidence, _session.RootIdentity, metadata, out var protectedBytes) || protectedBytes is null) return false;
            try { if (!_metadataStore.TryWriteDurably(protectedBytes) || _fault == CleanupFaultPoint.BeforeFirstDelete) return false; }
            finally { CryptographicOperations.ZeroMemory(protectedBytes); }
            LastGate = CleanupGate.Metadata;

            var postOrder = new List<RetainedTreeLease>(leases.Count);
            var entryCount = allLeases.Count;
            foreach (var lease in leases)
                if (!TryBuildPostOrder(lease, 1, ref entryCount, postOrder, allLeases)) return false;
            if (!_session.TryIssueCleanupFromDurableOwner(postOrder, out var authorization) || authorization is null) return false;
            try
            {
                foreach (var lease in postOrder)
                {
                    if (_fault is CleanupFaultPoint.DescendantIdentityMismatch or CleanupFaultPoint.DescendantKindMismatch or CleanupFaultPoint.DescendantVolumeMismatch or CleanupFaultPoint.DescendantReparseDetected) { LastGate = CleanupGate.Revalidation; return false; }
                    if (!_session.TryReobserve(lease, out _)) return false;
                    LastGate = CleanupGate.Revalidation;
                    if (_fault == CleanupFaultPoint.UnknownFault) throw new InvalidOperationException();
                    LastGate = CleanupGate.DeleteAttempted;
                    if (_fault == CleanupFaultPoint.NativeDeleteFailure) return false;
                    var deleted = _session.TryDelete(lease, authorization, out _);
                    if (!deleted) return false;
                    CompletedDeleteCount++;
                    LastGate = CleanupGate.Deleted;
                    if (_fault == CleanupFaultPoint.AfterFirstDelete && CompletedDeleteCount == 1) return false;
                }
                if (!_session.TryDeleteRoot(authorization, out _)) return false;
                CompletedDeleteCount++;
                LastGate = CleanupGate.Deleted;
            }
            finally { authorization.Invalidate(); }
            status = SupervisorStatus.Success;
            return true;
        }
        catch { return false; }
        finally
        {
            foreach (var lease in allLeases) lease.Dispose();
        }
    }

    private bool TryBuildPostOrder(RetainedTreeLease parent, int depth, ref int entryCount, List<RetainedTreeLease> postOrder, List<RetainedTreeLease> allLeases)
    {
        if (_fault == CleanupFaultPoint.DescendantReopenFailure) return false;
        if (depth > RetainedTreeSession.MaxDescendantDepth || !_session.TryEnumerateChildren(parent, out var children, out _)) return false;
        if (entryCount > RetainedTreeSession.MaxDescendantEntries - children.Count || depth == RetainedTreeSession.MaxDescendantDepth && children.Count != 0) { foreach (var child in children) child.Dispose(); return false; }
        entryCount += children.Count;
        foreach (var child in children)
        {
            allLeases.Add(child);
            if (!TryBuildPostOrder(child, depth + 1, ref entryCount, postOrder, allLeases)) return false;
        }
        postOrder.Add(parent);
        return true;
    }

    private bool TryMatchEvidence(out IReadOnlyList<RetainedTreeLease> leases)
    {
        leases = [];
        var evidence = _evidence.Evidence;
        if (!_session.Matches(_evidence) || evidence.Version != CommittedChildEvidence.VersionV1 || !evidence.HasValidDigest() || !Matches(evidence.Root, _session.RootIdentity) || !Matches(evidence.QuarantineParent, _session.QuarantineParentIdentity) || !_session.TryEnumerateDirectChildren(out leases, out _)) return false;
        if (leases.Count != evidence.Children.Count) return false;
        var matched = new bool[evidence.Children.Count];
        foreach (var lease in leases)
        {
            if (!lease.IsLiveFor(_session) || !lease.TryComputeEvidenceTag(evidence.CorrelationKey, out var tag) || tag is null) return false;
            try
            {
                var index = evidence.Children.Select((record, position) => (record, position)).SingleOrDefault(candidate => !matched[candidate.position] && CryptographicOperations.FixedTimeEquals(tag, candidate.record.LeafTag) && Matches(candidate.record, lease.Observation));
                if (index.record is null) return false;
                matched[index.position] = true;
            }
            finally { CryptographicOperations.ZeroMemory(tag); }
        }
        return matched.All(value => value);
    }

    private static bool Matches(CommittedFileIdentity expected, DirectoryIdentity actual)
        => expected.VolumeSerialNumber == actual.VolumeSerialNumber && TryMatchFileId(expected.FileId, actual.FileId);
    private static bool Matches(CommittedChildRecord expected, DirectoryHandleObservation actual)
        => expected.Kind == CommittedChildObjectKind.Directory && !expected.IsReparsePoint && actual.IsDirectory && !actual.IsReparsePoint && expected.VolumeSerialNumber == actual.Identity.VolumeSerialNumber && TryMatchFileId(expected.FileId, actual.Identity.FileId);
    private static bool TryMatchFileId(byte[] expected, string actual)
    {
        byte[]? parsed = null;
        try { parsed = Convert.FromHexString(actual); return parsed.Length == 16 && CryptographicOperations.FixedTimeEquals(expected, parsed); }
        catch { return false; }
        finally { CryptographicOperations.ZeroMemory(parsed ?? []); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _metadataStore.Dispose();
        _evidence.Dispose();
        _session.Dispose();
    }
}
