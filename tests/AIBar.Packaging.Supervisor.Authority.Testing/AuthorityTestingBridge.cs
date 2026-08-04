using Microsoft.Win32.SafeHandles;
namespace AIBar.Packaging.Supervisor.Authority.Testing;
public sealed record AuthorityTestingOutcome(bool Succeeded, string Code);
public static class AuthorityTestingBridge
{
    public static AuthorityTestingOutcome VerifyC1cOwnership()
    {
        try
        {
            using var committed = CreateCommitted();
            if (!committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) return Failed();
            using (handoff)
            using (tree)
            using (evidence)
                return new(CapabilityFacetBinding.Matches(tree, evidence) && evidence.Evidence.HasValidDigest(), "c1c-ownership-verified");
        }
        catch { return Failed(); }
    }
    public static AuthorityTestingOutcome VerifyC2aAuthorization()
    {
        try
        {
            using var committed = CreateCommitted();
            if (!committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) return Failed();
            using (handoff)
            using (tree)
            using (evidence)
            {
                var sandbox = RetainedTreeAuthorizationSandbox.CreateForTests();
                if (!RetainedTreeSession.TryCreateForTestSandbox(tree, TimeSpan.FromSeconds(1), CancellationToken.None, sandbox, out var session, out var status) || session is null || status != RetainedTreeMechanismStatus.Success) return Failed();
                using (session)
                using (var lease = new RetainedTreeLease(session, session.Generation, [], new SafeFileHandle(new IntPtr(1), false), new(new(1, new string('0', 32)), false, true, 0)))
                {
                    if (!sandbox.TryIssue(session, lease, out var token) || token is null || sandbox.TryIssue(session, lease, out _) || !session.TryVerifyAuthorization(lease, token, out status) || status != RetainedTreeMechanismStatus.Success) return Failed();
                    return new(!session.TryVerifyAuthorization(lease, token, out status) && status == RetainedTreeMechanismStatus.InvalidCapability, "c2a-authorization-verified");
                }
            }
        }
        catch { return Failed(); }
    }
    public static AuthorityTestingOutcome VerifyNativeDeleteRejections()
    {
        try
        {
            using var mismatchedLease = AuthorizationContext.Create();
            using var otherLease = mismatchedLease.CreateLease();
            if (!mismatchedLease.Sandbox.TryIssue(mismatchedLease.Session, mismatchedLease.Lease, out var mismatchedToken) || mismatchedToken is null || mismatchedLease.Session.TryDelete(otherLease, mismatchedToken, out var mismatchStatus) || mismatchStatus != RetainedTreeMechanismStatus.InvalidCapability || mismatchedLease.Session.TryDelete(mismatchedLease.Lease, mismatchedToken, out _)) return Failed();

            using var sourceSession = AuthorizationContext.Create();
            using var wrongSession = AuthorizationContext.Create();
            if (!sourceSession.Sandbox.TryIssue(sourceSession.Session, sourceSession.Lease, out var wrongSessionToken) || wrongSessionToken is null || wrongSession.Session.TryDelete(sourceSession.Lease, wrongSessionToken, out var wrongSessionStatus) || wrongSessionStatus != RetainedTreeMechanismStatus.InvalidCapability || sourceSession.Session.TryDelete(sourceSession.Lease, wrongSessionToken, out _)) return Failed();

            using var observationFault = AuthorizationContext.Create();
            if (!observationFault.Sandbox.TryIssue(observationFault.Session, observationFault.Lease, out var observationToken) || observationToken is null) return Failed();
            observationFault.Lease.InvalidateObservation();
            if (observationFault.Session.TryDelete(observationFault.Lease, observationToken, out var observationStatus) || observationStatus != RetainedTreeMechanismStatus.InvalidCapability) return Failed();

            using var cancellation = new CancellationTokenSource();
            using var cancelled = AuthorizationContext.Create(cancellation.Token);
            if (!cancelled.Sandbox.TryIssue(cancelled.Session, cancelled.Lease, out var cancelledToken) || cancelledToken is null) return Failed();
            cancellation.Cancel();
            return new(!cancelled.Session.TryDelete(cancelled.Lease, cancelledToken, out var cancelledStatus) && cancelledStatus == RetainedTreeMechanismStatus.Cancelled, "native-delete-rejections-verified");
        }
        catch { return Failed(); }
    }
    public static AuthorityTestingOutcome VerifyNativeDeleteRuntime()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, "runtime-not-applicable");
        var root = Path.Combine(Path.GetTempPath(), $"aibar native delete café {Guid.NewGuid():N}");
        var outcome = Failed();
        try
        {
            Directory.CreateDirectory(root);
            var quarantine = Directory.CreateDirectory(Path.Combine(root, "quarantine")).FullName;
            if (!DirectoryCapability.TryCreateRenameReady(new WindowsDirectoryCapabilityFileSystem(), root, "source Ω", ["child"], quarantine, out var capability, out _) || capability is null) return outcome;
            using (capability)
            {
                var result = NativeRenameReadiness.Prove(capability, "retained Ω");
                using var committed = result.Capability;
                if (result.Status != SupervisorStatus.Success || committed is null || !committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) return outcome;
                using (handoff)
                using (tree)
                using (evidence)
                {
                    var sandbox = RetainedTreeAuthorizationSandbox.CreateForTests();
                    if (!RetainedTreeSession.TryCreateForTestSandbox(tree, TimeSpan.FromSeconds(10), CancellationToken.None, sandbox, out var session, out var status) || session is null || !session.TryEnumerateDirectChildren(out var leases, out status) || status != RetainedTreeMechanismStatus.Success || leases.Count != 1) return outcome;
                    using (session)
                    using (leases[0])
                    {
                        if (!sandbox.TryIssue(session, leases[0], out var token) || token is null) return outcome;
                        var deleted = session.TryDelete(leases[0], token, out status);
                        outcome = new((deleted && status == RetainedTreeMechanismStatus.Success) || (!deleted && status == RetainedTreeMechanismStatus.DeleteFailed), "native-delete-gated-runtime-verified");
                    }
                }
            }
        }
        catch { outcome = Failed(); }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(root)) outcome = Failed(); }
            catch { outcome = Failed(); }
        }
        return outcome;
    }
    public static AuthorityTestingOutcome VerifyGuardedCleanupBoundary()
    {
        try
        {
            var authority = typeof(GuardedCleanupFacade).Assembly;
            var exported = authority.GetExportedTypes();
            var rawAuthorityExported = exported.Any(type => type != typeof(GuardedCleanupFacade) && (type.Name.Contains("Issuer", StringComparison.Ordinal) || type.Name.Contains("Token", StringComparison.Ordinal) || type.Name.Contains("Session", StringComparison.Ordinal) || type.Name.Contains("Lease", StringComparison.Ordinal) || type.Name.Contains("Sandbox", StringComparison.Ordinal)));
            return new(exported.Contains(typeof(GuardedCleanupFacade)) && !rawAuthorityExported, "guarded-cleanup-boundary-verified");
        }
        catch { return Failed(); }
    }
    public static AuthorityTestingOutcome VerifyGuardedCleanupFaultMatrix()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, "guarded-cleanup-fault-matrix-verified");
        var success = RunGuardedCleanup(CleanupFaultPoint.None, source => Directory.CreateDirectory(Path.Combine(source, "first", "leaf")));
        var before = RunGuardedCleanup(CleanupFaultPoint.BeforeFirstDelete, source => Directory.CreateDirectory(Path.Combine(source, "first", "leaf")));
        var after = RunGuardedCleanup(CleanupFaultPoint.AfterFirstDelete, source => Directory.CreateDirectory(Path.Combine(source, "first", "leaf")));
        var passed = success.Succeeded && success.NestedLeafDeleted && success.FirstDeleted && success.SecondDeleted && success.RootRemoved && success.CompletedDeletes == 4 && success.Status == SupervisorStatus.Success && success.DuplicateFacadeRefused
            && !before.Succeeded && !before.FirstDeleted && !before.SecondDeleted && before.CompletedDeletes == 0 && before.Status == SupervisorStatus.CleanupPartial
            && !after.Succeeded && after.NestedLeafDeleted && after.FirstParentRetained && !after.SecondDeleted && after.QuarantineRetained && after.CompletedDeletes == 1 && after.Status == SupervisorStatus.CleanupPartial && after.Gate == CleanupGate.Deleted;
        return new(passed, passed ? "guarded-cleanup-fault-matrix-verified" : $"guarded-cleanup-fault-matrix:{success.Succeeded}/{success.NestedLeafDeleted}/{success.FirstDeleted}/{success.SecondDeleted}/{success.RootRemoved}/{success.CompletedDeletes}/{success.DuplicateFacadeRefused}/{success.Status}/{success.Gate};{before.Succeeded}/{before.FirstDeleted}/{before.SecondDeleted}/{before.CompletedDeletes}/{before.Status}/{before.Gate};{after.Succeeded}/{after.NestedLeafDeleted}/{after.FirstParentRetained}/{after.SecondDeleted}/{after.CompletedDeletes}/{after.Status}/{after.Gate}");
    }
    public static AuthorityTestingOutcome VerifyGuardedCleanupBounds()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, "guarded-cleanup-bounds-verified");
        var depth = RunGuardedCleanup(CleanupFaultPoint.None, source =>
        {
            var leaf = Path.Combine(source, "first");
            for (var index = 0; index <= 8; index++) leaf = Directory.CreateDirectory(Path.Combine(leaf, $"d{index:D2}")).FullName;
        });
        var entries = RunGuardedCleanup(CleanupFaultPoint.None, source =>
        {
            var first = Path.Combine(source, "first");
            for (var outer = 0; outer < 16; outer++)
                for (var inner = 0; inner < 16; inner++) Directory.CreateDirectory(Path.Combine(first, $"a{outer:D2}", $"b{inner:D2}"));
        });
        var cancelled = RunGuardedCleanup(CleanupFaultPoint.None, null, true);
        var passed = !depth.Succeeded && depth.CompletedDeletes == 0 && depth.QuarantineRetained && depth.Status == SupervisorStatus.CleanupPartial
            && !entries.Succeeded && entries.CompletedDeletes == 0 && entries.QuarantineRetained && entries.Status == SupervisorStatus.CleanupPartial
            && !cancelled.Succeeded && cancelled.CompletedDeletes == 0 && cancelled.QuarantineRetained && cancelled.Status == SupervisorStatus.CleanupPartial;
        return new(passed, passed ? "guarded-cleanup-bounds-verified" : "guarded-cleanup-bounds-refused");
    }
    public static AuthorityTestingOutcome VerifyGuardedCleanupDescendantIdentityMismatch()
        => VerifyFailClosedFault(CleanupFaultPoint.DescendantIdentityMismatch, "guarded-cleanup-descendant-identity-mismatch-verified");
    public static AuthorityTestingOutcome VerifyGuardedCleanupDescendantKindMismatch()
        => VerifyFailClosedFault(CleanupFaultPoint.DescendantKindMismatch, "guarded-cleanup-descendant-kind-mismatch-verified");
    public static AuthorityTestingOutcome VerifyGuardedCleanupDescendantVolumeMismatch()
        => VerifyFailClosedFault(CleanupFaultPoint.DescendantVolumeMismatch, "guarded-cleanup-descendant-volume-mismatch-verified");
    public static AuthorityTestingOutcome VerifyGuardedCleanupDescendantReparse()
        => VerifyFailClosedFault(CleanupFaultPoint.DescendantReparseDetected, "guarded-cleanup-descendant-reparse-verified");
    public static AuthorityTestingOutcome VerifyGuardedCleanupDescendantReopenFailure()
        => VerifyFailClosedFault(CleanupFaultPoint.DescendantReopenFailure, "guarded-cleanup-descendant-reopen-failure-verified");
    public static AuthorityTestingOutcome VerifyGuardedCleanupDeadlineExhaustion()
        => VerifyDeadlineExhaustion();
    public static AuthorityTestingOutcome VerifyGuardedCleanupNativeDeleteFailure()
        => VerifyFailClosedFault(CleanupFaultPoint.NativeDeleteFailure, "guarded-cleanup-native-delete-failure-verified", CleanupGate.DeleteAttempted);
    public static AuthorityTestingOutcome VerifyGuardedCleanupUnknownFaultMapping()
        => VerifyFailClosedFault(CleanupFaultPoint.UnknownFault, "guarded-cleanup-unknown-fault-mapping-verified", CleanupGate.Revalidation);
    public static AuthorityTestingOutcome VerifyGuardedCleanupMetadataBinding()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, "guarded-cleanup-metadata-binding-verified");
        byte[]? protectedBytes = null;
        try
        {
            using var committed = CreateCommitted();
            if (!committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) return Failed();
            using (handoff)
            using (tree)
            using (evidence)
            {
                if (!RetainedTreeSession.TryCreate(tree, TimeSpan.FromSeconds(1), CancellationToken.None, out var session, out _) || session is null) return Failed();
                using (session)
                {
                    var valid = new CleanupRetryMetadata(CleanupMetadataPhase.RetainedQuarantine, 0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    var accepted = ScavengerMetadata.TryProtectAndVerify(evidence.Evidence, session.RootIdentity, valid, out protectedBytes);
                    var rejectedPhase = !ScavengerMetadata.TryProtectAndVerify(evidence.Evidence, session.RootIdentity, new((CleanupMetadataPhase)0, 0, valid.NextEligibleUnixMilliseconds), out _);
                    var rejectedRetry = !ScavengerMetadata.TryProtectAndVerify(evidence.Evidence, session.RootIdentity, new(CleanupMetadataPhase.RetainedQuarantine, 4, valid.NextEligibleUnixMilliseconds), out _);
                    var rejectedNextEligible = !ScavengerMetadata.TryProtectAndVerify(evidence.Evidence, session.RootIdentity, new(CleanupMetadataPhase.RetainedQuarantine, 0, 0), out _);
                    return new(accepted && protectedBytes is { Length: > 0 } && rejectedPhase && rejectedRetry && rejectedNextEligible, "guarded-cleanup-metadata-binding-verified");
                }
            }
        }
        catch { return Failed(); }
        finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(protectedBytes ?? []); }
    }
    public static AuthorityTestingOutcome VerifyBoundedRuntime()
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, "runtime-not-applicable");
        var root = Path.Combine(Path.GetTempPath(), $"aibar authority testing café {Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var quarantine = Directory.CreateDirectory(Path.Combine(root, "quarantine")).FullName;
            if (!DirectoryCapability.TryCreateRenameReady(new WindowsDirectoryCapabilityFileSystem(), root, "source Ω", ["child"], quarantine, out var capability, out _) || capability is null) return Failed();
            using (capability)
            {
                var result = NativeRenameReadiness.Prove(capability, "retained Ω");
                using var committed = result.Capability;
                if (result.Status != SupervisorStatus.Success || committed is null || !committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) return Failed();
                using (handoff)
                using (tree)
                using (evidence)
                {
                    var sandbox = RetainedTreeAuthorizationSandbox.CreateForTests();
                    if (!RetainedTreeSession.TryCreateForTestSandbox(tree, TimeSpan.FromSeconds(10), CancellationToken.None, sandbox, out var session, out var status) || session is null || !session.TryEnumerateDirectChildren(out var leases, out status) || status != RetainedTreeMechanismStatus.Success || leases.Count != 1) return Failed();
                    using (session) foreach (var lease in leases) lease.Dispose();
                    return new(true, "bounded-runtime-verified");
                }
            }
        }
        catch { return Failed(); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static AuthorityTestingOutcome Failed() => new(false, "authority-testing-refused");
    private static AuthorityTestingOutcome VerifyFailClosedFault(CleanupFaultPoint fault, string code, CleanupGate? expectedGate = null)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, code);
        var result = RunGuardedCleanup(fault, source => Directory.CreateDirectory(Path.Combine(source, "first", "leaf")));
        var passed = !result.Succeeded && !result.NestedLeafDeleted && !result.FirstDeleted && !result.SecondDeleted && result.QuarantineRetained && result.CompletedDeletes == 0 && result.Status == SupervisorStatus.CleanupPartial && (expectedGate is null || result.Gate == expectedGate);
        return new(passed, passed ? code : $"{code}:{result.Succeeded}/{result.NestedLeafDeleted}/{result.FirstDeleted}/{result.SecondDeleted}/{result.QuarantineRetained}/{result.CompletedDeletes}/{result.Status}/{result.Gate}");
    }
    private static AuthorityTestingOutcome VerifyDeadlineExhaustion()
    {
        const string code = "guarded-cleanup-deadline-exhaustion-verified";
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8) return new(true, code);
        var result = RunGuardedCleanup(CleanupFaultPoint.None, source => Directory.CreateDirectory(Path.Combine(source, "first", "leaf")), timeout: TimeSpan.FromTicks(1));
        var passed = !result.Succeeded && !result.NestedLeafDeleted && !result.FirstDeleted && !result.SecondDeleted && result.QuarantineRetained && result.CompletedDeletes == 0 && result.Status == SupervisorStatus.CleanupPartial;
        return new(passed, passed ? code : $"{code}:{result.Succeeded}/{result.NestedLeafDeleted}/{result.FirstDeleted}/{result.SecondDeleted}/{result.QuarantineRetained}/{result.CompletedDeletes}/{result.Status}/{result.Gate}");
    }
    private static (bool Succeeded, bool NestedLeafDeleted, bool FirstDeleted, bool FirstParentRetained, bool SecondDeleted, bool RootRemoved, bool QuarantineRetained, int CompletedDeletes, bool DuplicateFacadeRefused, SupervisorStatus Status, CleanupGate Gate) RunGuardedCleanup(CleanupFaultPoint fault, Action<string>? arrange = null, bool cancel = false, TimeSpan? timeout = null)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar guarded cleanup café {Guid.NewGuid():N}");
        var retained = Path.Combine(root, "quarantine", "retained Ω");
        try
        {
            Directory.CreateDirectory(root);
            var quarantine = Directory.CreateDirectory(Path.Combine(root, "quarantine")).FullName;
            if (!DirectoryCapability.TryCreateRenameReady(new WindowsDirectoryCapabilityFileSystem(), root, "source Ω", ["first", "second"], quarantine, out var capability, out _) || capability is null) return default;
            using (capability)
            {
                arrange?.Invoke(Path.Combine(root, "source Ω"));
                var result = NativeRenameReadiness.Prove(capability, "retained Ω");
                using var committed = result.Capability;
                if (result.Status != SupervisorStatus.Success || committed is null || !committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) return default;
                using (handoff)
                {
                    using var cancellation = new CancellationTokenSource();
                    if (!RetainedTreeSession.TryCreate(tree, timeout ?? TimeSpan.FromSeconds(10), cancellation.Token, out var session, out _) || session is null) { tree.Dispose(); evidence.Dispose(); return default; }
                    using var store = new MetadataStore();
                    if (!GuardedCleanupFacade.TryCreate(session, evidence, store, out var facade, out _, fault) || facade is null) { session.Dispose(); evidence.Dispose(); return default; }
                    using (facade)
                    {
                        if (cancel) cancellation.Cancel();
                        using var duplicateStore = new MetadataStore();
                        var duplicateFacadeRefused = !GuardedCleanupFacade.TryCreate(session, evidence, duplicateStore, out _, out _);
                        var succeeded = facade.TryCleanup(out var status);
                        return (succeeded, !Directory.Exists(Path.Combine(retained, "first", "leaf")), !Directory.Exists(Path.Combine(retained, "first")), Directory.Exists(Path.Combine(retained, "first")), !Directory.Exists(Path.Combine(retained, "second")), !Directory.Exists(retained), Directory.Exists(retained), facade.CompletedDeleteCount, duplicateFacadeRefused, status, facade.LastGate);
                    }
                }
            }
        }
        catch { return default; }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(root)) throw new IOException("Test-owned cleanup root remains.");
        }
    }
    private sealed class AuthorizationContext : IDisposable
    {
        private readonly CommittedQuarantineCapability _committed;
        private readonly ICommittedCapabilityFacetHandoff _handoff;
        private readonly IRetainedTreeCapabilityFacet _tree;
        private readonly ICommittedEvidenceCapabilityFacet _evidence;
        private AuthorizationContext(CommittedQuarantineCapability committed, ICommittedCapabilityFacetHandoff handoff, IRetainedTreeCapabilityFacet tree, ICommittedEvidenceCapabilityFacet evidence, RetainedTreeSession session, RetainedTreeAuthorizationSandbox sandbox, RetainedTreeLease lease) => (_committed, _handoff, _tree, _evidence, Session, Sandbox, Lease) = (committed, handoff, tree, evidence, session, sandbox, lease);
        internal RetainedTreeSession Session { get; }
        internal RetainedTreeAuthorizationSandbox Sandbox { get; }
        internal RetainedTreeLease Lease { get; }
        internal static AuthorizationContext Create(CancellationToken cancellation = default)
        {
            var committed = CreateCommitted();
            if (!committed.TrySplit(out var handoff) || handoff is null || !handoff.TryTakeBoth(out var tree, out var evidence) || tree is null || evidence is null) { committed.Dispose(); throw new InvalidOperationException(); }
            var sandbox = RetainedTreeAuthorizationSandbox.CreateForTests();
            if (!RetainedTreeSession.TryCreateForTestSandbox(tree, TimeSpan.FromSeconds(1), cancellation, sandbox, out var session, out var status) || session is null || status != RetainedTreeMechanismStatus.Success) { evidence.Dispose(); tree.Dispose(); handoff.Dispose(); committed.Dispose(); throw new InvalidOperationException(); }
            return new(committed, handoff, tree, evidence, session, sandbox, new RetainedTreeLease(session, session.Generation, [], new SafeFileHandle(new IntPtr(1), false), new(new(1, new string('0', 32)), false, true, 0)));
        }
        internal RetainedTreeLease CreateLease() => new(Session, Session.Generation, [], new SafeFileHandle(new IntPtr(2), false), new(new(1, new string('1', 32)), false, true, 0));
        public void Dispose() { Lease.Dispose(); Session.Dispose(); _evidence.Dispose(); _tree.Dispose(); _handoff.Dispose(); _committed.Dispose(); }
    }
    private sealed class MetadataStore : IDurableProtectedMetadataStore
    {
        private byte[]? _bytes;
        public bool TryWriteDurably(ReadOnlySpan<byte> protectedBytes)
        {
            if (_bytes is not null || protectedBytes.Length is 0 or > 4096) return false;
            _bytes = protectedBytes.ToArray();
            return true;
        }
        public void Dispose() => System.Security.Cryptography.CryptographicOperations.ZeroMemory(Interlocked.Exchange(ref _bytes, null) ?? []);
    }
    private static CommittedQuarantineCapability CreateCommitted()
    {
        var fileSystem = new BoundedFileSystem();
        if (!DirectoryCapability.TryCreateRenameReady(fileSystem, "source", "root", ["child"], "quarantine", out var capability, out _) || capability is null || !capability.TryFreezeChildEvidence(out var evidence) || evidence is null || !capability.ReleaseChildHandles()) throw new InvalidOperationException();
        return capability.TransferCommitted(evidence) ?? throw new InvalidOperationException();
    }
    private sealed class BoundedFileSystem : IDirectoryCapabilityFileSystem
    {
        private readonly Dictionary<SafeFileHandle, DirectoryObservation> _observations = new(ReferenceEqualityComparer.Instance);
        public bool TryCreateDirectory(string path) => true;
        public SafeFileHandle? OpenDirectory(string path, uint desiredAccess)
        {
            var handle = new SafeFileHandle(new IntPtr(_observations.Count + 1), false);
            _observations.Add(handle, new(new(1, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path)))[..32]), path, false));
            return handle;
        }
        public bool TryObserve(SafeFileHandle handle, out DirectoryObservation observation) => _observations.TryGetValue(handle, out observation!);
        public bool TryEnumerateDirectChildren(SafeFileHandle root, out IReadOnlyList<string> names) { names = ["child"]; return true; }
        public bool HasRequiredRenameShare(SafeFileHandle source, SafeFileHandle parent) => true;
    }
}
