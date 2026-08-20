using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AIBar.Application;

internal enum LocalUsageAdmissionStatus { Admitted, PathChanged, Unavailable }
internal enum LocalUsageAdmissionLayout { Direct, PiEncodedDirectory }
// Path-only adapters require bracketing Prepare; this cannot eliminate the race between validation and their open.
internal interface ILocalUsageSourceAdmission { LocalUsageAdmissionStatus Validate(CancellationToken token); }

internal sealed class LocalUsageSourceAdmission : ILocalUsageSourceAdmission
{
    private readonly ILocalUsageDiscoveryFileSystem _fileSystem;
    private readonly string _root, _path;
    private readonly LocalUsageAdmissionLayout _layout;
    private readonly Entry[] _proof;

    private LocalUsageSourceAdmission(ILocalUsageDiscoveryFileSystem fileSystem, string root, string path,
        LocalUsageAdmissionLayout layout, Entry[] proof)
    { _fileSystem = fileSystem; _root = root; _path = path; _layout = layout; _proof = proof.ToArray(); }

    internal static ILocalUsageSourceAdmission? Capture(ILocalUsageDiscoveryFileSystem fileSystem, string root,
        string path, LocalUsageAdmissionLayout layout, CancellationToken token)
    {
        if (!ValidLayout(root, path, layout)) return null;
        var proof = new List<Entry>();
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var metadata = fileSystem.Inspect(current, token);
            var kind = current == path ? LocalUsagePathKind.File : LocalUsagePathKind.Directory;
            if (metadata.Kind != kind || metadata.IsReparsePoint || metadata.Identity is null) return null;
            proof.Add(new(current, metadata));
        }
        return new LocalUsageSourceAdmission(fileSystem, root, path, layout, proof.ToArray());
    }

    public LocalUsageAdmissionStatus Validate(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (!ValidLayout(_root, _path, _layout)) return LocalUsageAdmissionStatus.PathChanged;
            foreach (var entry in _proof)
                if (_fileSystem.Inspect(entry.Path, token) != entry.Metadata) return LocalUsageAdmissionStatus.PathChanged;
            return LocalUsageAdmissionStatus.Admitted;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { return LocalUsageAdmissionStatus.Unavailable; }
    }

    public override string ToString() => "Local usage source admission proof (private)";

    private static bool ValidLayout(string root, string path, LocalUsageAdmissionLayout layout)
    {
        try
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(root, Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
                || !StringComparer.OrdinalIgnoreCase.Equals(path, Path.GetFullPath(path))) return false;
            var parent = Path.GetDirectoryName(path);
            if (layout == LocalUsageAdmissionLayout.Direct) return StringComparer.OrdinalIgnoreCase.Equals(parent, root);
            if (parent is null) return false;
            var name = Path.GetFileName(parent);
            return StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(parent), root)
                && name.Length >= 4 && name.StartsWith("--", StringComparison.Ordinal) && name.EndsWith("--", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private readonly record struct Entry(string Path, LocalUsagePathMetadata Metadata);
}

internal static class WindowsLocalUsageFileMetadata
{
    internal static LocalUsagePathMetadata Inspect(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Stable source admission requires Windows file identity.");
        using var handle = CreateFile(path, 0, FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open,
            0x02000000 | 0x00200000, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 3) return new(LocalUsagePathKind.Missing);
            throw new IOException("Source metadata could not be inspected.", new Win32Exception(error));
        }
        if (!GetAttributes(handle, 9, out var attributes, (uint)Marshal.SizeOf<FileAttributeTagInfo>())
            || !GetIdentity(handle, 18, out var identity, (uint)Marshal.SizeOf<FileIdInfo>()))
            throw new IOException("Stable source identity could not be inspected.", new Win32Exception(Marshal.GetLastWin32Error()));
        var kind = (attributes.Attributes & (uint)FileAttributes.Directory) != 0 ? LocalUsagePathKind.Directory
            : (attributes.Attributes & (uint)FileAttributes.Device) != 0 ? LocalUsagePathKind.Other : LocalUsagePathKind.File;
        return new(kind, (attributes.Attributes & (uint)FileAttributes.ReparsePoint) != 0,
            new(identity.Volume, identity.Identifier));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, FileShare share, nint security,
        FileMode mode, uint flags, nint template);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAttributes(SafeFileHandle handle, int infoClass, out FileAttributeTagInfo info, uint size);
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIdentity(SafeFileHandle handle, int infoClass, out FileIdInfo info, uint size);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct FileAttributeTagInfo(uint Attributes, uint ReparseTag);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct FileIdInfo(ulong Volume, Guid Identifier);
}
