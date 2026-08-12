using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AIBar.Packaging.Supervisor;

internal enum RetainedTreeMechanismStatus : byte { Success, UnsupportedRuntime, InvalidCapability, BoundExceeded, Cancelled, Timeout, EnumerationFailed, ReopenFailed, ObservationFailed, ReparseDetected, CrossVolume, IdentityChanged, DeleteFailed, InternalUnknown }

internal sealed record DirectoryHandleObservation(DirectoryIdentity Identity, bool IsReparsePoint, bool IsDirectory, long Length);

internal static class RetainedTreeReadOnly
{
    private const int StatusSuccess = 0, FileStandardInformation = 1, FileNamesInformation = 12, FileOpen = 1;
    private const uint QueryBufferBytes = 16 * 1024, FileReadAttributesSynchronize = 0x00120081, FileShareReadWrite = 3, FileDirectoryFile = 1, FileOpenReparsePoint = 0x00200000, FileSynchronousIoNonAlert = 0x20, ObjectCaseInsensitive = 0x40;
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct IoStatusBlock { [FieldOffset(0)] public int Status; [FieldOffset(0)] public IntPtr Pointer; [FieldOffset(8)] public IntPtr Information; }
    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { public uint Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)] private struct FileIdInfo { public ulong VolumeSerialNumber; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] FileId; }
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTagInfo { public uint FileAttributes, ReparseTag; }
    [StructLayout(LayoutKind.Sequential)] private struct StandardInfo { public long AllocationSize, EndOfFile; public uint NumberOfLinks; public byte DeletePending, Directory; }
    internal static bool IsCompatibleForCurrentProcess() => IsCompatible(OperatingSystem.IsWindowsVersionAtLeast(10), IntPtr.Size, HasExpectedLayouts(), HasExport("NtQueryDirectoryFile"), HasExport("NtCreateFile"));
    internal static bool IsCompatible(bool supportedWindows, int pointerSize, bool layoutsValid, bool query, bool create) => supportedWindows && pointerSize == 8 && layoutsValid && query && create;
    internal static bool TryQueryNames(SafeFileHandle root, out byte[] bytes, out RetainedTreeMechanismStatus status)
    {
        bytes = []; status = RetainedTreeMechanismStatus.EnumerationFailed; if (!IsCompatibleForCurrentProcess()) { status = RetainedTreeMechanismStatus.UnsupportedRuntime; return false; }
        var unmanaged = IntPtr.Zero; var zero = new byte[QueryBufferBytes];
        try { unmanaged = Marshal.AllocHGlobal((int)QueryBufferBytes); Marshal.Copy(zero, 0, unmanaged, zero.Length); var result = NtQueryDirectoryFile(root, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out var io, unmanaged, QueryBufferBytes, FileNamesInformation, false, IntPtr.Zero, true); if (result != StatusSuccess || io.Status != StatusSuccess || io.Information.ToInt64() is <= 0 or > QueryBufferBytes) return false; bytes = new byte[io.Information.ToInt32()]; Marshal.Copy(unmanaged, bytes, 0, bytes.Length); status = RetainedTreeMechanismStatus.Success; return true; }
        catch { status = RetainedTreeMechanismStatus.InternalUnknown; return false; }
        finally { CryptographicOperations.ZeroMemory(zero); if (unmanaged != IntPtr.Zero) { Marshal.Copy(zero, 0, unmanaged, zero.Length); Marshal.FreeHGlobal(unmanaged); } }
    }
    internal static bool TryParseDirectChildren(byte[] bytes, out IReadOnlyList<string> names, out RetainedTreeMechanismStatus status)
    {
        var parsed = new List<string>(); names = parsed; status = RetainedTreeMechanismStatus.EnumerationFailed;
        try
        {
            var sawDot = false; var sawDotDot = false;
            for (var offset = 0; ; )
            {
                if (bytes.Length - offset < 12) return false;
                var next = BitConverter.ToUInt32(bytes, offset); var length = BitConverter.ToUInt32(bytes, offset + 8);
                if (length is 0 or > 510 || (length & 1) != 0 || length > bytes.Length - offset - 12) return false;
                var record = checked(12 + (int)length); if (next != 0 && (next < record || next > bytes.Length - offset)) return false;
                var name = Encoding.Unicode.GetString(bytes, offset + 12, (int)length);
                if (name is "." or "..")
                {
                    if (name == "." ? sawDot : sawDotDot) return false;
                    if (name == ".") sawDot = true; else sawDotDot = true;
                }
                else
                {
                    if (!DirectoryCapability.IsEvidenceLeaf(name) || name.StartsWith(".", StringComparison.Ordinal) || parsed.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
                    parsed.Add(name);
                    if (parsed.Count > 16) { status = RetainedTreeMechanismStatus.BoundExceeded; return false; }
                }
                if (next == 0) { if (offset + record != bytes.Length) return false; break; }
                offset = checked(offset + (int)next);
            }
            names = parsed; status = RetainedTreeMechanismStatus.Success; return true;
        }
        catch { names = []; status = RetainedTreeMechanismStatus.EnumerationFailed; return false; }
    }
    internal static byte[] BuildNamesBufferForTests(IReadOnlyList<string> names)
    {
        var records = names.Select(name => Encoding.Unicode.GetBytes(name)).ToArray(); var length = records.Sum(record => 12 + record.Length); var buffer = new byte[length]; var offset = 0;
        foreach (var record in records) { var size = 12 + record.Length; BitConverter.GetBytes(offset + size == length ? 0u : (uint)size).CopyTo(buffer, offset); BitConverter.GetBytes(record.Length).CopyTo(buffer, offset + 8); record.CopyTo(buffer, offset + 12); offset += size; CryptographicOperations.ZeroMemory(record); }
        return buffer;
    }
    internal static bool TryReopenAndObserve(SafeFileHandle root, string name, out SafeFileHandle handle, out DirectoryHandleObservation observation, out RetainedTreeMechanismStatus status)
    {
        handle = new SafeFileHandle(IntPtr.Zero, true); observation = default!; status = RetainedTreeMechanismStatus.ReopenFailed; if (!DirectoryCapability.IsEvidenceLeaf(name)) return false;
        var utf16 = Encoding.Unicode.GetBytes(name); var buffer = IntPtr.Zero;
        try
        {
            buffer = Marshal.AllocHGlobal(utf16.Length); Marshal.Copy(utf16, 0, buffer, utf16.Length); var unicode = new UnicodeString { Length = checked((ushort)utf16.Length), MaximumLength = checked((ushort)utf16.Length), Buffer = buffer }; var unicodeBuffer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            try { Marshal.StructureToPtr(unicode, unicodeBuffer, false); var attributes = new ObjectAttributes { Length = (uint)Marshal.SizeOf<ObjectAttributes>(), RootDirectory = root.DangerousGetHandle(), ObjectName = unicodeBuffer, Attributes = ObjectCaseInsensitive }; var result = NtCreateFile(out handle, FileReadAttributesSynchronize, ref attributes, out var io, IntPtr.Zero, 0, FileShareReadWrite, FileOpen, FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert, IntPtr.Zero, 0); if (result != StatusSuccess || io.Status != StatusSuccess || handle.IsInvalid || !TryObserve(handle, out observation)) { handle.Dispose(); return false; } status = RetainedTreeMechanismStatus.Success; return true; }
            finally { Marshal.FreeHGlobal(unicodeBuffer); }
        }
        catch { handle.Dispose(); return false; }
        finally { CryptographicOperations.ZeroMemory(utf16); if (buffer != IntPtr.Zero) { Marshal.Copy(new byte[utf16.Length], 0, buffer, utf16.Length); Marshal.FreeHGlobal(buffer); } }
    }
    internal static bool TryObserve(SafeFileHandle handle, out DirectoryHandleObservation observation)
    {
        observation = default!;
        if (!GetFileInformationByHandleEx(handle, 18, out FileIdInfo id, (uint)Marshal.SizeOf<FileIdInfo>())) return false;
        if (!GetFileInformationByHandleEx(handle, 9, out AttributeTagInfo tag, (uint)Marshal.SizeOf<AttributeTagInfo>())) return false;
        if (!GetFileInformationByHandleEx(handle, FileStandardInformation, out StandardInfo standard, (uint)Marshal.SizeOf<StandardInfo>())) return false;
        observation = new(new(id.VolumeSerialNumber, Convert.ToHexString(id.FileId)), (tag.FileAttributes & 0x400) != 0 || tag.ReparseTag != 0, standard.Directory != 0, standard.EndOfFile); return true;
    }
    private static bool HasExpectedLayouts() => Marshal.SizeOf<IoStatusBlock>() == 16 && Marshal.OffsetOf<IoStatusBlock>(nameof(IoStatusBlock.Information)).ToInt32() == 8 && Marshal.SizeOf<ObjectAttributes>() == 48 && Marshal.OffsetOf<ObjectAttributes>(nameof(ObjectAttributes.RootDirectory)).ToInt32() == 8 && Marshal.OffsetOf<ObjectAttributes>(nameof(ObjectAttributes.ObjectName)).ToInt32() == 16 && Marshal.SizeOf<UnicodeString>() == 16;
    private static bool HasExport(string name) { if (!NativeLibrary.TryLoad("ntdll.dll", out var module)) return false; try { return NativeLibrary.TryGetExport(module, name, out _); } finally { NativeLibrary.Free(module); } }
    [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)] private static extern int NtQueryDirectoryFile(SafeFileHandle handle, IntPtr @event, IntPtr apcRoutine, IntPtr apcContext, out IoStatusBlock ioStatus, IntPtr buffer, uint length, int informationClass, [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry, IntPtr fileName, [MarshalAs(UnmanagedType.U1)] bool restartScan);
    [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)] private static extern int NtCreateFile(out SafeFileHandle handle, uint desiredAccess, ref ObjectAttributes attributes, out IoStatusBlock ioStatus, IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out FileIdInfo info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out AttributeTagInfo info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out StandardInfo info, uint length);
}
