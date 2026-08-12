using Microsoft.Win32.SafeHandles;

namespace AIBar.Packaging.Supervisor;

public sealed record DirectoryIdentity(ulong VolumeSerialNumber, string FileId);
public sealed record DirectoryObservation(DirectoryIdentity Identity, string FinalPath, bool IsReparsePoint);
public interface IDirectoryCapabilityFileSystem
{
    bool TryCreateDirectory(string path);
    SafeFileHandle? OpenDirectory(string path, uint desiredAccess);
    bool TryObserve(SafeFileHandle handle, out DirectoryObservation observation);
    bool TryEnumerateDirectChildren(SafeFileHandle root, out IReadOnlyList<string> names);
    bool HasRequiredRenameShare(SafeFileHandle source, SafeFileHandle parent);
}

public sealed class DirectoryCapability : IDisposable
{
    internal const uint ObservationAccess = 0x00120089;
    public const uint RenameSourceAccess = 0x00130089, QuarantineParentAccess = 0x001000A0;
    private readonly IDirectoryCapabilityFileSystem _fileSystem;
    private readonly DirectoryObservation _root, _parent;
    private readonly Dictionary<string, DirectoryObservation> _children;
    private readonly Dictionary<string, SafeFileHandle> _childHandles;
    private SafeFileHandle? _rootHandle, _quarantineParentHandle;
    private bool _childrenReleased, _disposed, _transferred;
    internal SafeFileHandle RootHandle => _rootHandle ?? throw new ObjectDisposedException(nameof(DirectoryCapability));
    internal SafeFileHandle? QuarantineParentHandle => _quarantineParentHandle;
    public DirectoryObservation Source => _root;
    public DirectoryObservation? QuarantineParent => _quarantineParentHandle is null ? null : _parent;
    internal IReadOnlyDictionary<string, SafeFileHandle> ChildHandles => _childHandles;

    private DirectoryCapability(IDirectoryCapabilityFileSystem fileSystem, SafeFileHandle rootHandle, DirectoryObservation root, Dictionary<string, SafeFileHandle> childHandles, Dictionary<string, DirectoryObservation> children, SafeFileHandle? parent = null, DirectoryObservation? parentObservation = null)
    { _fileSystem = fileSystem; _rootHandle = rootHandle; _root = root; _childHandles = childHandles; _children = children; _quarantineParentHandle = parent; _parent = parentObservation ?? default!; }

    public static bool TryCreate(IDirectoryCapabilityFileSystem fileSystem, string parent, string leaf, IReadOnlyCollection<string> children, out DirectoryCapability? capability, out SupervisorStatus status)
        => TryCreateCore(fileSystem, parent, leaf, children, ObservationAccess, null, out capability, out status);

    public static bool TryCreateRenameReady(IDirectoryCapabilityFileSystem fileSystem, string parent, string leaf, IReadOnlyCollection<string> children, string quarantineParent, out DirectoryCapability? capability, out SupervisorStatus status)
        => TryCreateCore(fileSystem, parent, leaf, children, RenameSourceAccess, quarantineParent, out capability, out status);

    private static bool TryCreateCore(IDirectoryCapabilityFileSystem fileSystem, string parent, string leaf, IReadOnlyCollection<string> children, uint sourceAccess, string? quarantineParent, out DirectoryCapability? capability, out SupervisorStatus status)
    {
        capability = null; status = SupervisorStatus.RootCreateFailed;
        if (string.IsNullOrWhiteSpace(parent) || !IsSimpleLeaf(leaf) || children.Any(child => !IsEvidenceLeaf(child)) || children.Distinct(StringComparer.OrdinalIgnoreCase).Count() != children.Count) return false;
        SafeFileHandle? rootHandle = null, parentHandle = null; var childHandles = new Dictionary<string, SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var rootPath = Path.Combine(parent, leaf);
            if (!fileSystem.TryCreateDirectory(rootPath) || (rootHandle = fileSystem.OpenDirectory(rootPath, sourceAccess)) is null || !fileSystem.TryObserve(rootHandle, out var root)) return false;
            if (root.IsReparsePoint) { status = SupervisorStatus.ReparseDetected; return false; }
            var evidence = new Dictionary<string, DirectoryObservation>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in children)
            {
                var childPath = Path.Combine(rootPath, child);
                if (!fileSystem.TryCreateDirectory(childPath) || (childHandles[child] = fileSystem.OpenDirectory(childPath, ObservationAccess)!) is null || !fileSystem.TryObserve(childHandles[child], out var observation)) return false;
                evidence.Add(child, observation);
            }
            DirectoryObservation? parentObservation = null;
            if (quarantineParent is not null)
            {
                if ((parentHandle = fileSystem.OpenDirectory(quarantineParent, QuarantineParentAccess)) is null || !fileSystem.TryObserve(parentHandle, out var observedParent)) return false;
                if (observedParent.IsReparsePoint || observedParent.Identity == root.Identity || observedParent.Identity.VolumeSerialNumber != root.Identity.VolumeSerialNumber) { status = observedParent.IsReparsePoint ? SupervisorStatus.ReparseDetected : SupervisorStatus.RootIdentityChanged; return false; }
                parentObservation = observedParent;
            }
            var candidate = new DirectoryCapability(fileSystem, rootHandle, root, childHandles, evidence, parentHandle, parentObservation);
            if (!candidate.TryValidate(out status) || quarantineParent is not null && !candidate.TryValidateRenameReady(out status)) { candidate.Dispose(); return false; }
            capability = candidate; status = SupervisorStatus.Success; return true;
        }
        catch { return false; }
        finally { if (capability is null) { foreach (var handle in childHandles.Values) handle.Dispose(); parentHandle?.Dispose(); rootHandle?.Dispose(); } }
    }

    public bool TryValidate(out SupervisorStatus status)
    {
        status = SupervisorStatus.RootIdentityChanged;
        try
        {
            if (!Observe(RootHandle, _root, out status) || !_fileSystem.TryEnumerateDirectChildren(RootHandle, out var names) || names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count() || !names.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(_children.Keys)) return false;
            foreach (var child in _children)
                if (!_childHandles.TryGetValue(child.Key, out var handle) || !Observe(handle, child.Value, out status) || child.Value.Identity.VolumeSerialNumber != _root.Identity.VolumeSerialNumber || !IsContained(_root.FinalPath, child.Value.FinalPath)) return false;
            status = SupervisorStatus.Success; return true;
        }
        catch { status = SupervisorStatus.RootIdentityChanged; return false; }
    }

    public bool TryValidateRenameReady(out SupervisorStatus status)
    {
        status = SupervisorStatus.CleanupRefused;
        return QuarantineParentHandle is not null && !_childrenReleased && TryValidate(out _) && Observe(QuarantineParentHandle, _parent, out _) && _parent.Identity.VolumeSerialNumber == _root.Identity.VolumeSerialNumber && _fileSystem.HasRequiredRenameShare(RootHandle, QuarantineParentHandle);
    }
    internal bool ReleaseChildHandles() { if (_childrenReleased || _disposed || _transferred) return false; foreach (var handle in _childHandles.Values) handle.Dispose(); _childHandles.Clear(); return _childrenReleased = true; }
    internal bool TryFreezeChildEvidence(out CommittedChildEvidence? evidence)
    {
        evidence = null;
        if (_disposed || _transferred || _childrenReleased || !TryValidateRenameReady(out _)) return false;
        var observations = new List<(string Leaf, DirectoryObservation Observation)>(_children.Count);
        foreach (var child in _children)
        {
            if (!_childHandles.TryGetValue(child.Key, out var handle) || !_fileSystem.TryObserve(handle, out var observed) || observed != child.Value || observed.IsReparsePoint || observed.Identity.VolumeSerialNumber != _root.Identity.VolumeSerialNumber || !IsContained(_root.FinalPath, observed.FinalPath)) return false;
            observations.Add((child.Key, observed));
        }
        return CommittedChildEvidence.TryCreate(_root, _parent, observations, out evidence);
    }
    internal CommittedQuarantineCapability? TransferCommitted(CommittedChildEvidence evidence)
    {
        if (_disposed || _transferred || !_childrenReleased || _rootHandle is null || _quarantineParentHandle is null) return null;
        _transferred = true;
        return new CommittedQuarantineCapability(this, _root, _parent, evidence);
    }
    internal bool TryDetachCommittedHandles(out SafeFileHandle? root, out SafeFileHandle? quarantineParent)
    {
        root = null; quarantineParent = null;
        if (_disposed || !_transferred || _rootHandle is null || _quarantineParentHandle is null) return false;
        root = _rootHandle; quarantineParent = _quarantineParentHandle;
        _rootHandle = null; _quarantineParentHandle = null;
        return true;
    }
    public static bool IsSimpleLeaf(string value) => !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." && value.IndexOfAny(['\\', '/', ':', '\0']) < 0 && !Path.IsPathRooted(value) && !IsReserved(value);
    internal static bool IsEvidenceLeaf(string value) => IsSimpleLeaf(value) && value.Length <= 255;
    private static bool IsReserved(string value) => new[] { "CON", "PRN", "AUX", "NUL", "COM1", "LPT1" }.Contains(Path.GetFileNameWithoutExtension(value), StringComparer.OrdinalIgnoreCase);
    private bool Observe(SafeFileHandle handle, DirectoryObservation expected, out SupervisorStatus status) { status = SupervisorStatus.RootIdentityChanged; if (!_fileSystem.TryObserve(handle, out var actual)) return false; if (actual.IsReparsePoint) { status = SupervisorStatus.ReparseDetected; return false; } return actual == expected; }
    internal static bool IsContained(string root, string child) => child.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public void Dispose() { if (_disposed) return; _disposed = true; foreach (var handle in _childHandles.Values) handle.Dispose(); _childHandles.Clear(); _quarantineParentHandle?.Dispose(); _quarantineParentHandle = null; _rootHandle?.Dispose(); _rootHandle = null; }
}
