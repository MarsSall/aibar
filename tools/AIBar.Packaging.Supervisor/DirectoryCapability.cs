using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("AIBar.Domain.Tests")]

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

public sealed record NativeReadinessResult(SupervisorStatus Status, int NativeCallCount, bool ChildrenReleased, DirectoryObservation? Source, CommittedQuarantineCapability? Capability = null);
public static class NativeRenameReadiness
{
    private const int FileRenameInformation = 10, StatusSuccess = 0;
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct RenameLayout { [FieldOffset(0)] public byte ReplaceIfExists; [FieldOffset(8)] public IntPtr RootDirectory; [FieldOffset(16)] public uint FileNameLength; [FieldOffset(20)] public ushort FileName; }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct IoStatusBlock { [FieldOffset(0)] public int Status; [FieldOffset(0)] public IntPtr Pointer; [FieldOffset(8)] public IntPtr Information; }
    public static NativeReadinessResult Prove(DirectoryCapability capability, string leaf) => Prove(capability, leaf, IsCompatibleForCurrentProcess);
    internal static NativeReadinessResult Prove(DirectoryCapability capability, string leaf, Func<bool> isCompatible)
    {
        if (!isCompatible() || !capability.TryValidateRenameReady(out _) || !DirectoryCapability.IsEvidenceLeaf(leaf) || !capability.TryFreezeChildEvidence(out var evidence) || evidence is null)
            return new(SupervisorStatus.CleanupRefused, 0, false, null);

        var released = false; var issued = false; var bytes = Encoding.Unicode.GetBytes(leaf); var length = checked(20 + bytes.Length); IntPtr buffer = IntPtr.Zero;
        try
        {
            if (!capability.ReleaseChildHandles()) { evidence.Dispose(); return new(SupervisorStatus.CleanupRefused, 0, false, null); }
            released = true;
            buffer = Marshal.AllocHGlobal(length); Marshal.Copy(new byte[length], 0, buffer, length); Marshal.WriteByte(buffer, 0, 0); Marshal.WriteIntPtr(buffer, 8, capability.QuarantineParentHandle!.DangerousGetHandle()); Marshal.WriteInt32(buffer, 16, bytes.Length); Marshal.Copy(bytes, 0, IntPtr.Add(buffer, 20), bytes.Length);
            issued = true;
            var status = NtSetInformationFile(capability.RootHandle, out var ioStatus, buffer, (uint)length, FileRenameInformation);
            if (!IsSuccessfulStatus(status, ioStatus.Status)) { evidence.Dispose(); return new(SupervisorStatus.CleanupRefused, 1, true, null); }
            var sourceObserved = Observe(capability.RootHandle, out var source); var parentObserved = Observe(capability.QuarantineParentHandle, out var parent);
            var committed = capability.TransferCommitted(evidence);
            if (committed is null) { evidence.Dispose(); return new(SupervisorStatus.CleanupPartial, 1, true, sourceObserved ? source : null); }
            if (!sourceObserved || !parentObserved || source.Identity != capability.Source.Identity || parent != capability.QuarantineParent || !DirectoryCapability.IsContained(parent.FinalPath, source.FinalPath))
                return new(SupervisorStatus.CleanupPartial, 1, true, sourceObserved ? source : null, committed);
            return new(SupervisorStatus.Success, 1, true, source, committed);
        }
        catch
        {
            evidence.Dispose();
            return new(issued ? SupervisorStatus.CleanupPartial : SupervisorStatus.CleanupRefused, issued ? 1 : 0, released, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            if (buffer != IntPtr.Zero) { Marshal.Copy(new byte[length], 0, buffer, length); Marshal.FreeHGlobal(buffer); }
        }
    }
    public static bool IsSuccessfulStatus(int callStatus, int ioStatus) => callStatus == StatusSuccess && ioStatus == StatusSuccess;
    public static bool IsCompatibleForCurrentProcess() => IsCompatible(OperatingSystem.IsWindowsVersionAtLeast(10), IntPtr.Size, HasExpectedLayouts(), HasNtSetInformationFile(), FileRenameInformation);
    internal static bool IsCompatible(bool supportedWindows, int pointerSize, bool layoutsValid, bool entryPointAvailable, int informationClass) => supportedWindows && pointerSize == 8 && layoutsValid && entryPointAvailable && informationClass == FileRenameInformation;
    private static bool HasExpectedLayouts()
    {
        return Marshal.SizeOf<RenameLayout>() == 24 && Marshal.OffsetOf<RenameLayout>(nameof(RenameLayout.ReplaceIfExists)).ToInt32() == 0 && Marshal.OffsetOf<RenameLayout>(nameof(RenameLayout.RootDirectory)).ToInt32() == 8 && Marshal.OffsetOf<RenameLayout>(nameof(RenameLayout.FileNameLength)).ToInt32() == 16 && Marshal.OffsetOf<RenameLayout>(nameof(RenameLayout.FileName)).ToInt32() == 20 && Marshal.SizeOf<IoStatusBlock>() == 16 && Marshal.OffsetOf<IoStatusBlock>(nameof(IoStatusBlock.Status)).ToInt32() == 0 && Marshal.OffsetOf<IoStatusBlock>(nameof(IoStatusBlock.Pointer)).ToInt32() == 0 && Marshal.OffsetOf<IoStatusBlock>(nameof(IoStatusBlock.Information)).ToInt32() == 8;
    }
    private static bool HasNtSetInformationFile()
    {
        if (!NativeLibrary.TryLoad("ntdll.dll", out var module)) return false;
        try { return NativeLibrary.TryGetExport(module, "NtSetInformationFile", out _); }
        finally { NativeLibrary.Free(module); }
    }
    private static bool Observe(SafeFileHandle handle, out DirectoryObservation observation) => new WindowsDirectoryCapabilityFileSystem().TryObserve(handle, out observation);
    [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)] private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock ioStatus, IntPtr information, uint length, int informationClass);
}

public sealed class WindowsDirectoryCapabilityFileSystem : IDirectoryCapabilityFileSystem
{
    private const uint FileShareReadWrite = 3, OpenExisting = 3, BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000, FileAttributeReparsePoint = 0x400;
    public bool TryCreateDirectory(string path) => Native.CreateDirectoryW(path, IntPtr.Zero);
    public SafeFileHandle? OpenDirectory(string path, uint desiredAccess) { var handle = Native.CreateFileW(path, desiredAccess, FileShareReadWrite, IntPtr.Zero, OpenExisting, BackupSemantics | OpenReparsePoint, IntPtr.Zero); if (handle.IsInvalid) { handle.Dispose(); return null; } return handle; }
    public bool HasRequiredRenameShare(SafeFileHandle source, SafeFileHandle parent) => !source.IsInvalid && !parent.IsInvalid;
    public bool TryObserve(SafeFileHandle handle, out DirectoryObservation observation)
    {
        observation = default!;
        if (!Native.GetFileInformationByHandleEx(handle, 18, out FileIdInfo id, (uint)Marshal.SizeOf<FileIdInfo>()) || !Native.GetFileInformationByHandleEx(handle, 9, out AttributeTagInfo attributes, (uint)Marshal.SizeOf<AttributeTagInfo>())) return false;
        var path = new StringBuilder(32768); var length = Native.GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, 0); if (length == 0 || length >= path.Capacity) return false;
        observation = new(new(id.VolumeSerialNumber, Convert.ToHexString(id.FileId)), path.ToString(), (attributes.FileAttributes & FileAttributeReparsePoint) != 0 || attributes.ReparseTag != 0); return true;
    }
    public bool TryEnumerateDirectChildren(SafeFileHandle root, out IReadOnlyList<string> names) { names = []; if (!TryObserve(root, out var observation)) return false; try { names = Directory.EnumerateFileSystemEntries(observation.FinalPath).Select(path => Path.GetFileName(path)!).ToArray(); return true; } catch { return false; } }
    [StructLayout(LayoutKind.Sequential)] private struct FileIdInfo { public ulong VolumeSerialNumber; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] FileId; }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint FileAttributes, ReparseTag; }
    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateDirectoryW(string path, IntPtr attributes);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out FileIdInfo info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out AttributeTagInfo info, uint length);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] internal static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
    }
}
