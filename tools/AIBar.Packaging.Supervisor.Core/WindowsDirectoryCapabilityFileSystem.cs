using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;

namespace AIBar.Packaging.Supervisor;

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
