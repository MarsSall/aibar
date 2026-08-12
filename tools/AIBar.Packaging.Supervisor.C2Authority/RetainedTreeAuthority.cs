using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace AIBar.Packaging.Supervisor;
internal sealed class RetainedTreeLease : IDisposable
{
    private readonly RetainedTreeSession _session;
    private readonly RetainedTreeLease? _parent;
    private byte[]? _name;
    private SafeFileHandle? _handle;
    private int _disposed;
    private int _observationFaulted;
    internal RetainedTreeLease(RetainedTreeSession session, long generation, byte[] name, SafeFileHandle handle, DirectoryHandleObservation observation, RetainedTreeLease? parent = null) => (_session, Generation, _name, _handle, Observation, _parent) = (session, generation, name, handle, observation, parent);
    internal long Generation { get; }
    internal DirectoryHandleObservation Observation { get; }
    internal SafeFileHandle Handle => _handle ?? throw new ObjectDisposedException(nameof(RetainedTreeLease));
    internal SafeFileHandle ParentHandle => _parent?.Handle ?? _session.RootHandle;
    internal bool IsLiveFor(RetainedTreeSession session) => ReferenceEquals(_session, session) && Generation == session.Generation && Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _observationFaulted) == 0;
    internal bool HasLiveParent => _parent is null || _parent.IsLiveFor(_session);
    internal bool TryCopyName(out byte[] name)
    {
        name = [];
        var value = Volatile.Read(ref _name);
        if (value is null || Volatile.Read(ref _disposed) != 0) return false;
        name = value.ToArray();
        return true;
    }
    internal bool TryReleaseHandleForDelete()
    {
        if (!IsLiveFor(_session)) return false;
        Interlocked.Exchange(ref _handle, null)?.Dispose();
        return true;
    }
    internal void InvalidateObservation() => Volatile.Write(ref _observationFaulted, 1);
    internal bool TryComputeEvidenceTag(ReadOnlySpan<byte> correlationKey, out byte[]? tag)
    {
        tag = null;
        var name = Volatile.Read(ref _name);
        if (name is null || Volatile.Read(ref _disposed) != 0) return false;
        tag = HMACSHA256.HashData(correlationKey, name);
        return true;
    }
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; Interlocked.Exchange(ref _handle, null)?.Dispose(); var name = Interlocked.Exchange(ref _name, null); if (name is not null) CryptographicOperations.ZeroMemory(name); }
}
internal sealed class RetainedTreeDeleteAuthorization
{
    private readonly RetainedTreeSession _session;
    private readonly RetainedTreeLease _lease;
    private int _used;
    internal RetainedTreeDeleteAuthorization(RetainedTreeSession session, RetainedTreeLease lease) => (_session, _lease) = (session, lease);
    internal bool TryConsume(RetainedTreeSession session, RetainedTreeLease lease) => Interlocked.Exchange(ref _used, 1) == 0 && ReferenceEquals(_session, session) && ReferenceEquals(_lease, lease);
    internal void Invalidate() => Interlocked.Exchange(ref _used, 1);
}
internal sealed class RetainedTreeCleanupAuthorization
{
    private readonly RetainedTreeSession _session;
    private readonly HashSet<RetainedTreeLease> _remaining;
    private bool _rootRemaining = true;
    private int _invalid;
    internal RetainedTreeCleanupAuthorization(RetainedTreeSession session, IEnumerable<RetainedTreeLease> leases) => (_session, _remaining) = (session, new HashSet<RetainedTreeLease>(leases, ReferenceEqualityComparer.Instance));
    internal bool TryConsume(RetainedTreeSession session, RetainedTreeLease lease)
    {
        lock (_remaining) return Volatile.Read(ref _invalid) == 0 && ReferenceEquals(_session, session) && _remaining.Remove(lease);
    }
    internal void Invalidate()
    {
        Interlocked.Exchange(ref _invalid, 1);
        lock (_remaining) _remaining.Clear();
    }
    internal bool TryConsumeRoot(RetainedTreeSession session)
    {
        lock (_remaining)
        {
            if (Volatile.Read(ref _invalid) != 0 || !ReferenceEquals(_session, session) || !_rootRemaining || _remaining.Count != 0) return false;
            _rootRemaining = false;
            return true;
        }
    }
}
internal sealed class RetainedTreeAuthorizationSandbox
{
    private RetainedTreeAuthorizationIssuer? _issuer;
    private RetainedTreeAuthorizationSandbox() { }
    internal static RetainedTreeAuthorizationSandbox CreateForTests() => new();
    internal bool HasIssuer => Volatile.Read(ref _issuer) is not null;
    internal bool TryReceive(RetainedTreeAuthorizationIssuer issuer) => Interlocked.CompareExchange(ref _issuer, issuer, null) is null;
    internal bool TryIssue(RetainedTreeSession session, RetainedTreeLease lease, out RetainedTreeDeleteAuthorization? token)
    {
        token = null;
        var issuer = Volatile.Read(ref _issuer);
        return issuer is not null && issuer.TryIssue(session, lease, out token);
    }
}
internal sealed class RetainedTreeAuthorizationIssuer
{
    private readonly RetainedTreeSession _session;
    private int _issued;
    internal RetainedTreeAuthorizationIssuer(RetainedTreeSession session) => _session = session;
    internal bool TryIssue(RetainedTreeSession session, RetainedTreeLease lease, out RetainedTreeDeleteAuthorization? token)
    {
        token = null;
        if (!ReferenceEquals(_session, session) || !session.CanBegin(out _) || !lease.IsLiveFor(session) || Interlocked.Exchange(ref _issued, 1) != 0) return false;
        token = new RetainedTreeDeleteAuthorization(session, lease);
        return true;
    }
    internal bool TryIssueCleanup(RetainedTreeSession session, IReadOnlyList<RetainedTreeLease> leases, out RetainedTreeCleanupAuthorization? authorization)
    {
        authorization = null;
        if (!ReferenceEquals(_session, session) || leases.Count is 0 or > RetainedTreeSession.MaxDescendantEntries || leases.Any(lease => !lease.IsLiveFor(session)) || leases.Distinct(ReferenceEqualityComparer.Instance).Count() != leases.Count || Interlocked.Exchange(ref _issued, 1) != 0) return false;
        authorization = new RetainedTreeCleanupAuthorization(session, leases);
        return true;
    }
}
internal sealed class RetainedTreeSession : IDisposable
{
    internal const int MaxDirectChildren = 16, MaxDescendantEntries = 64, MaxDescendantDepth = 8, MaxLeafNameBytes = 510;
    private static readonly object ConsumedGate = new();
    private static readonly HashSet<IRetainedTreeCapabilityFacet> Consumed = new(ReferenceEqualityComparer.Instance);
    private readonly IRetainedTreeCapabilityFacet _facet;
    private readonly CancellationToken _cancellation;
    private readonly long _deadline;
    private readonly List<RetainedTreeLease> _leases = [];
    private readonly DurableAuthorizationOwner? _durableOwner;
    private int _disposed;
    private RetainedTreeSession(IRetainedTreeCapabilityFacet facet, TimeSpan timeout, CancellationToken cancellation, RetainedTreeAuthorizationSandbox? sandbox)
    {
        _facet = facet; _cancellation = cancellation; _deadline = checked(Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency)); RootIdentity = facet.Source.Identity; VolumeSerialNumber = RootIdentity.VolumeSerialNumber;
        var issuer = new RetainedTreeAuthorizationIssuer(this);
        if (sandbox is not null)
        {
            if (!sandbox.TryReceive(issuer)) throw new InvalidOperationException();
        }
        else
        {
            _durableOwner = new DurableAuthorizationOwner();
            if (!_durableOwner.TryReceive(issuer)) throw new InvalidOperationException();
        }
    }
    private sealed class DurableAuthorizationOwner
    {
        private RetainedTreeAuthorizationIssuer? _issuer;
        internal bool TryReceive(RetainedTreeAuthorizationIssuer issuer) => Interlocked.CompareExchange(ref _issuer, issuer, null) is null;
        internal bool TryIssue(RetainedTreeSession session, RetainedTreeLease lease, out RetainedTreeDeleteAuthorization? authorization)
        {
            authorization = null;
            var issuer = Volatile.Read(ref _issuer);
            return issuer is not null && issuer.TryIssue(session, lease, out authorization);
        }
        internal bool TryIssueCleanup(RetainedTreeSession session, IReadOnlyList<RetainedTreeLease> leases, out RetainedTreeCleanupAuthorization? authorization)
        {
            authorization = null;
            var issuer = Volatile.Read(ref _issuer);
            return issuer is not null && issuer.TryIssueCleanup(session, leases, out authorization);
        }
    }
    internal DirectoryIdentity RootIdentity { get; }
    internal SafeFileHandle RootHandle => _facet.RootHandle;
    internal DirectoryIdentity QuarantineParentIdentity => _facet.QuarantineParent.Identity;
    internal ulong VolumeSerialNumber { get; }
    internal long Generation { get; } = Stopwatch.GetTimestamp();
    internal static bool TryCreate(IRetainedTreeCapabilityFacet? facet, TimeSpan timeout, CancellationToken cancellation, out RetainedTreeSession? session, out RetainedTreeMechanismStatus status) => TryCreateCore(facet, timeout, cancellation, null, out session, out status);
    internal static bool TryCreateForTestSandbox(IRetainedTreeCapabilityFacet? facet, TimeSpan timeout, CancellationToken cancellation, RetainedTreeAuthorizationSandbox sandbox, out RetainedTreeSession? session, out RetainedTreeMechanismStatus status) => TryCreateCore(facet, timeout, cancellation, sandbox, out session, out status);
    private static bool TryCreateCore(IRetainedTreeCapabilityFacet? facet, TimeSpan timeout, CancellationToken cancellation, RetainedTreeAuthorizationSandbox? sandbox, out RetainedTreeSession? session, out RetainedTreeMechanismStatus status)
    {
        session = null;
        if (cancellation.IsCancellationRequested) { status = RetainedTreeMechanismStatus.Cancelled; return false; }
        if (facet is null || facet.IsDisposed || timeout <= TimeSpan.Zero || !RetainedTreeReadOnly.IsCompatibleForCurrentProcess()) { status = facet is null || facet.IsDisposed ? RetainedTreeMechanismStatus.InvalidCapability : RetainedTreeMechanismStatus.UnsupportedRuntime; return false; }
        try
        {
            if (facet.Source.IsReparsePoint || facet.QuarantineParent.IsReparsePoint || facet.Source.Identity.VolumeSerialNumber != facet.QuarantineParent.Identity.VolumeSerialNumber || facet.RootHandle.IsInvalid || facet.QuarantineParentHandle.IsInvalid) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
            lock (ConsumedGate) { if (!Consumed.Add(facet)) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; } }
            session = new RetainedTreeSession(facet, timeout, cancellation, sandbox); status = RetainedTreeMechanismStatus.Success; return true;
        }
        catch { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
    }
    internal bool Matches(ICommittedEvidenceCapabilityFacet evidence) => !IsDisposed && CapabilityFacetBinding.Matches(_facet, evidence);
    internal bool TryIssueFromDurableOwner(RetainedTreeLease lease, out RetainedTreeDeleteAuthorization? authorization)
    {
        authorization = null;
        return !IsDisposed && _durableOwner is not null && _durableOwner.TryIssue(this, lease, out authorization);
    }
    internal bool TryIssueCleanupFromDurableOwner(IReadOnlyList<RetainedTreeLease> leases, out RetainedTreeCleanupAuthorization? authorization)
    {
        authorization = null;
        return !IsDisposed && _durableOwner is not null && CanBegin(out _) && _durableOwner.TryIssueCleanup(this, leases, out authorization);
    }
    internal bool TryVerifyAuthorization(RetainedTreeLease lease, RetainedTreeDeleteAuthorization? authorization, out RetainedTreeMechanismStatus status)
    {
        if (!CanBegin(out status)) { authorization?.Invalidate(); return false; }
        if (authorization is null || !authorization.TryConsume(this, lease) || !lease.IsLiveFor(this)) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
        status = RetainedTreeMechanismStatus.Success;
        return true;
    }
    internal bool TryDelete(RetainedTreeLease lease, RetainedTreeDeleteAuthorization? authorization, out RetainedTreeMechanismStatus status)
    {
        if (!TryVerifyAuthorization(lease, authorization, out status)) return false;
        return TryDeleteVerified(lease, false, out status);
    }
    internal bool TryDelete(RetainedTreeLease lease, RetainedTreeCleanupAuthorization? authorization, out RetainedTreeMechanismStatus status)
    {
        if (!CanBegin(out status)) { authorization?.Invalidate(); return false; }
        if (authorization is null || !authorization.TryConsume(this, lease) || !lease.IsLiveFor(this)) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
        return TryDeleteVerified(lease, true, out status);
    }
    private bool TryDeleteVerified(RetainedTreeLease lease, bool requireRelativeDeleteHandle, out RetainedTreeMechanismStatus status)
    {
        if (!TryReobserve(lease, out status)) return false;
        return requireRelativeDeleteHandle ? NativeDelete.TryDeleteRelative(lease.ParentHandle, lease, out status) : NativeDelete.TryDelete(lease.Handle, out status);
    }
    internal bool TryEnumerateDirectChildren(out IReadOnlyList<RetainedTreeLease> leases, out RetainedTreeMechanismStatus status)
    {
        leases = [];
        if (!CanBegin(out status) || !RetainedTreeReadOnly.TryQueryNames(_facet.RootHandle, out var bytes, out status)) return false;
        try
        {
            if (!RetainedTreeReadOnly.TryParseDirectChildren(bytes, out var names, out status) || !CanBegin(out status)) return false;
            var created = new List<RetainedTreeLease>(names.Count);
            foreach (var name in names.OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!CanBegin(out status) || !RetainedTreeReadOnly.TryReopenAndObserve(_facet.RootHandle, name, out var handle, out var observation, out status)) { foreach (var lease in created) lease.Dispose(); return false; }
                if (observation.Identity.VolumeSerialNumber != VolumeSerialNumber || observation.IsReparsePoint) { handle.Dispose(); foreach (var lease in created) lease.Dispose(); status = observation.IsReparsePoint ? RetainedTreeMechanismStatus.ReparseDetected : RetainedTreeMechanismStatus.CrossVolume; return false; }
                created.Add(new(this, Generation, Encoding.Unicode.GetBytes(name), handle, observation));
            }
            if (!CanBegin(out status)) { foreach (var lease in created) lease.Dispose(); return false; }
            _leases.AddRange(created); leases = created; status = RetainedTreeMechanismStatus.Success; return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal bool TryEnumerateChildren(RetainedTreeLease parent, out IReadOnlyList<RetainedTreeLease> leases, out RetainedTreeMechanismStatus status)
    {
        leases = [];
        if (!TryReobserve(parent, out status) || !RetainedTreeReadOnly.TryQueryNames(parent.Handle, out var bytes, out status)) return false;
        try
        {
            if (!RetainedTreeReadOnly.TryParseDirectChildren(bytes, out var names, out status) || names.Count > MaxDirectChildren || !CanBegin(out status)) return false;
            var created = new List<RetainedTreeLease>(names.Count);
            foreach (var name in names.OrderBy(name => name, StringComparer.Ordinal))
            {
                var nameBytes = Encoding.Unicode.GetBytes(name);
                if (nameBytes.Length is 0 or > MaxLeafNameBytes || (nameBytes.Length & 1) != 0 || !CanBegin(out status) || !RetainedTreeReadOnly.TryReopenAndObserve(parent.Handle, name, out var handle, out var observation, out status)) { CryptographicOperations.ZeroMemory(nameBytes); foreach (var lease in created) lease.Dispose(); return false; }
                if (!parent.HasLiveParent || observation.Identity.VolumeSerialNumber != VolumeSerialNumber || observation.IsReparsePoint || !observation.IsDirectory) { CryptographicOperations.ZeroMemory(nameBytes); handle.Dispose(); foreach (var lease in created) lease.Dispose(); status = observation.IsReparsePoint ? RetainedTreeMechanismStatus.ReparseDetected : observation.Identity.VolumeSerialNumber != VolumeSerialNumber ? RetainedTreeMechanismStatus.CrossVolume : RetainedTreeMechanismStatus.IdentityChanged; return false; }
                created.Add(new(this, Generation, nameBytes, handle, observation, parent));
            }
            _leases.AddRange(created); leases = created; status = RetainedTreeMechanismStatus.Success; return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    internal bool TryReobserve(RetainedTreeLease lease, out RetainedTreeMechanismStatus status)
    {
        if (!CanBegin(out status)) return false;
        if (!lease.IsLiveFor(this) || !lease.HasLiveParent) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
        if (!RetainedTreeReadOnly.TryObserve(lease.Handle, out var observation)) { lease.InvalidateObservation(); status = RetainedTreeMechanismStatus.ObservationFailed; return false; }
        if (observation.Identity != lease.Observation.Identity || observation.Identity.VolumeSerialNumber != VolumeSerialNumber || observation.IsReparsePoint || !observation.IsDirectory) { lease.InvalidateObservation(); status = observation.IsReparsePoint ? RetainedTreeMechanismStatus.ReparseDetected : observation.Identity.VolumeSerialNumber != VolumeSerialNumber ? RetainedTreeMechanismStatus.CrossVolume : RetainedTreeMechanismStatus.IdentityChanged; return false; }
        status = RetainedTreeMechanismStatus.Success;
        return true;
    }
    internal bool TryDeleteRoot(RetainedTreeCleanupAuthorization? authorization, out RetainedTreeMechanismStatus status)
    {
        if (!CanBegin(out status)) { authorization?.Invalidate(); return false; }
        if (authorization is null || !authorization.TryConsumeRoot(this) || !RetainedTreeReadOnly.TryObserve(_facet.RootHandle, out var observation) || observation.Identity != RootIdentity || observation.IsReparsePoint || !observation.IsDirectory) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
        if (!NativeDelete.TryDelete(_facet.RootHandle, out status)) return false;
        _facet.RootHandle.Dispose();
        return true;
    }
    internal bool CanBegin(out RetainedTreeMechanismStatus status)
    {
        if (IsDisposed) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
        if (_cancellation.IsCancellationRequested) { status = RetainedTreeMechanismStatus.Cancelled; return false; }
        if (Stopwatch.GetTimestamp() > _deadline) { status = RetainedTreeMechanismStatus.Timeout; return false; }
        status = RetainedTreeMechanismStatus.Success; return true;
    }
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; foreach (var lease in _leases) lease.Dispose(); _leases.Clear(); _facet.Dispose(); }
}
