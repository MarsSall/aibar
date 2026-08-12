using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AIBar.Domain.Tests, PublicKey=0024000004800000140100000602000000240000525341310008000001000100F51E383EAE0372C9E1223F4CD3158495C4A38F12837D75E43E85D18752622529FFB59BFC44F6D708BD71D2EF555B885765D5D6A996B9BCF73F1C3A0DF3302ED983F3F9C4C6D5F0EFF6A9A8E32524D16B862985772000E8DADF4D4E508B040D3F8C6897B81290D64795BB0E5C47760AEB41849E02B796B15989881FB9046D1175DC92E5AB21B7314D60F0AFF3072523489FDC9742C07E688F69DDB1840899567DD860BAA527DDD6F68C0A3B0AE84254D348880A2B742BA05F4773F7F6966CC30B4BBF3145F0FB6F92A8703767D10B8CC3A004248343DB28C1B9D69EEFCD286EC6A5C819CA458459066ABFAC09BE5BFFD0D88B35B98651B96957CD6ECAC06022DE")]

namespace AIBar.Packaging.Supervisor;

internal sealed class RetainedTreeLease : IDisposable
{
    private readonly RetainedTreeSession _session;
    private byte[]? _name;
    private SafeFileHandle? _handle;
    private int _disposed;
    private int _observationFaulted;
    internal RetainedTreeLease(RetainedTreeSession session, long generation, byte[] name, SafeFileHandle handle, DirectoryHandleObservation observation) => (_session, Generation, _name, _handle, Observation) = (session, generation, name, handle, observation);
    internal long Generation { get; }
    internal DirectoryHandleObservation Observation { get; }
    internal SafeFileHandle Handle => _handle ?? throw new ObjectDisposedException(nameof(RetainedTreeLease));
    internal bool IsLiveFor(RetainedTreeSession session) => ReferenceEquals(_session, session) && Generation == session.Generation && Volatile.Read(ref _disposed) == 0 && Volatile.Read(ref _observationFaulted) == 0;
    internal void InvalidateObservation() => Volatile.Write(ref _observationFaulted, 1);
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) != 0) return; Interlocked.Exchange(ref _handle, null)?.Dispose(); var name = Interlocked.Exchange(ref _name, null); if (name is not null) CryptographicOperations.ZeroMemory(name); }
}
internal sealed class RetainedTreeDeleteAuthorization
{
    private readonly RetainedTreeSession _session;
    private readonly RetainedTreeLease _lease;
    private int _used;
    internal RetainedTreeDeleteAuthorization(RetainedTreeSession session, RetainedTreeLease lease) => (_session, _lease) = (session, lease);
    internal bool TryConsume(RetainedTreeSession session, RetainedTreeLease lease)
        => ReferenceEquals(_session, session) && ReferenceEquals(_lease, lease) && Interlocked.Exchange(ref _used, 1) == 0;
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
}

internal sealed class RetainedTreeSession : IDisposable
{
    private static readonly object ConsumedGate = new();
    private static readonly HashSet<IRetainedTreeCapabilityFacet> Consumed = new(ReferenceEqualityComparer.Instance);
    private readonly IRetainedTreeCapabilityFacet _facet;
    private readonly CancellationToken _cancellation;
    private readonly long _deadline;
    private readonly List<RetainedTreeLease> _leases = [];
    private int _disposed;
    private RetainedTreeSession(IRetainedTreeCapabilityFacet facet, TimeSpan timeout, CancellationToken cancellation, RetainedTreeAuthorizationSandbox? sandbox)
    {
        _facet = facet; _cancellation = cancellation; _deadline = checked(Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency)); RootIdentity = facet.Source.Identity; VolumeSerialNumber = RootIdentity.VolumeSerialNumber;
        var issuer = new RetainedTreeAuthorizationIssuer(this);
        if (sandbox is not null ? !sandbox.TryReceive(issuer) : !new ProductionIssuerOwner().TryReceive(issuer)) throw new InvalidOperationException();
    }
    private sealed class ProductionIssuerOwner
    {
        private RetainedTreeAuthorizationIssuer? _issuer;
        internal bool TryReceive(RetainedTreeAuthorizationIssuer issuer) => Interlocked.CompareExchange(ref _issuer, issuer, null) is null;
    }
    internal DirectoryIdentity RootIdentity { get; }
    internal ulong VolumeSerialNumber { get; }
    internal long Generation { get; } = Stopwatch.GetTimestamp();
    internal static bool TryCreate(IRetainedTreeCapabilityFacet? facet, TimeSpan timeout, CancellationToken cancellation, out RetainedTreeSession? session, out RetainedTreeMechanismStatus status)
        => TryCreateCore(facet, timeout, cancellation, null, out session, out status);
    internal static bool TryCreateForTestSandbox(IRetainedTreeCapabilityFacet? facet, TimeSpan timeout, CancellationToken cancellation, RetainedTreeAuthorizationSandbox sandbox, out RetainedTreeSession? session, out RetainedTreeMechanismStatus status)
        => TryCreateCore(facet, timeout, cancellation, sandbox, out session, out status);
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
    internal bool TryVerifyAuthorization(RetainedTreeLease lease, RetainedTreeDeleteAuthorization? authorization, out RetainedTreeMechanismStatus status)
    {
        if (!CanBegin(out status)) return false;
        if (authorization is null || !lease.IsLiveFor(this) || !authorization.TryConsume(this, lease)) { status = RetainedTreeMechanismStatus.InvalidCapability; return false; }
        status = RetainedTreeMechanismStatus.Success;
        return true;
    }
    internal bool TryEnumerateDirectChildren(out IReadOnlyList<RetainedTreeLease> leases, out RetainedTreeMechanismStatus status)
    {
        leases = [];
        if (!CanBegin(out status) || !RetainedTreeReadOnly.TryQueryNames(_facet.RootHandle, out var bytes, out status)) return false;
        try
        {
            if (!RetainedTreeReadOnly.TryParseDirectChildren(bytes, out var names, out status) || !CanBegin(out status)) return false;
            var created = new List<RetainedTreeLease>(names.Count);
            foreach (var name in names)
            {
                if (!CanBegin(out status) || !RetainedTreeReadOnly.TryReopenAndObserve(_facet.RootHandle, name, out var handle, out var observation, out status)) { foreach (var lease in created) lease.Dispose(); return false; }
                if (observation.Identity.VolumeSerialNumber != VolumeSerialNumber) { handle.Dispose(); foreach (var lease in created) lease.Dispose(); status = RetainedTreeMechanismStatus.CrossVolume; return false; }
                if (observation.IsReparsePoint) { handle.Dispose(); foreach (var lease in created) lease.Dispose(); status = RetainedTreeMechanismStatus.ReparseDetected; return false; }
                created.Add(new(this, Generation, Encoding.Unicode.GetBytes(name), handle, observation));
            }
            if (!CanBegin(out status)) { foreach (var lease in created) lease.Dispose(); return false; }
            _leases.AddRange(created); leases = created; status = RetainedTreeMechanismStatus.Success; return true;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
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

internal static class NativeDirectory
{
    internal static bool TryQueryNames(SafeFileHandle root, out byte[] bytes, out RetainedTreeMechanismStatus status) => RetainedTreeReadOnly.TryQueryNames(root, out bytes, out status);
    internal static bool TryParseDirectChildren(byte[] bytes, out IReadOnlyList<string> names, out RetainedTreeMechanismStatus status) => RetainedTreeReadOnly.TryParseDirectChildren(bytes, out names, out status);
    internal static byte[] BuildNamesBufferForTests(IReadOnlyList<string> names) => RetainedTreeReadOnly.BuildNamesBufferForTests(names);
    internal static bool TryReopenAndObserve(SafeFileHandle root, string name, out SafeFileHandle handle, out DirectoryHandleObservation observation, out RetainedTreeMechanismStatus status) => RetainedTreeReadOnly.TryReopenAndObserve(root, name, out handle, out observation, out status);
}
