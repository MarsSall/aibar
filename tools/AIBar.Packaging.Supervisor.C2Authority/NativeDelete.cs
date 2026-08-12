using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AIBar.Packaging.Supervisor;

internal static class NativeDelete
{
    private const int StatusSuccess = 0, FileDispositionInformation = 13, FileOpen = 1, FileDirectoryFile = 1, FileOpenReparsePoint = 0x00200000, FileSynchronousIoNonAlert = 0x20, ObjectCaseInsensitive = 0x40;
    private const uint FileReadAttributesSynchronizeDelete = 0x00130081, FileShareReadWrite = 3;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct IoStatusBlock
    {
        [FieldOffset(0)] public int Status;
        [FieldOffset(0)] public IntPtr Pointer;
        [FieldOffset(8)] public IntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes { public uint Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }

    internal static bool TryDelete(SafeFileHandle handle, out RetainedTreeMechanismStatus status)
    {
        status = RetainedTreeMechanismStatus.DeleteFailed;
        if (!IsCompatible()) { status = RetainedTreeMechanismStatus.UnsupportedRuntime; return false; }
        var flag = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(flag, 1);
            var result = NtSetInformationFile(handle, out var io, flag, 1, FileDispositionInformation);
            if (result != StatusSuccess || io.Status != StatusSuccess) return false;
            status = RetainedTreeMechanismStatus.Success;
            return true;
        }
        catch
        {
            status = RetainedTreeMechanismStatus.InternalUnknown;
            return false;
        }
        finally
        {
            Marshal.WriteByte(flag, 0);
            Marshal.FreeHGlobal(flag);
        }
    }

    internal static bool TryDeleteRelative(SafeFileHandle root, RetainedTreeLease lease, out RetainedTreeMechanismStatus status)
    {
        status = RetainedTreeMechanismStatus.DeleteFailed;
        SafeFileHandle? handle = null;
        byte[]? name = null;
        var nameBuffer = IntPtr.Zero;
        var objectName = IntPtr.Zero;
        try
        {
            if (!IsCompatible() || !lease.TryCopyName(out name) || name.Length is 0 or > 510 || (name.Length & 1) != 0 || !lease.TryReleaseHandleForDelete()) return false;
            nameBuffer = Marshal.AllocHGlobal(name.Length);
            Marshal.Copy(name, 0, nameBuffer, name.Length);
            objectName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            var unicode = new UnicodeString { Length = checked((ushort)name.Length), MaximumLength = checked((ushort)name.Length), Buffer = nameBuffer };
            Marshal.StructureToPtr(unicode, objectName, false);
            var attributes = new ObjectAttributes { Length = (uint)Marshal.SizeOf<ObjectAttributes>(), RootDirectory = root.DangerousGetHandle(), ObjectName = objectName, Attributes = ObjectCaseInsensitive };
            var result = NtCreateFile(out handle, FileReadAttributesSynchronizeDelete, ref attributes, out var openIo, IntPtr.Zero, 0, FileShareReadWrite, FileOpen, FileDirectoryFile | FileOpenReparsePoint | FileSynchronousIoNonAlert, IntPtr.Zero, 0);
            if (result != StatusSuccess || openIo.Status != StatusSuccess || handle.IsInvalid || !RetainedTreeReadOnly.TryObserve(handle, out var observation) || observation != lease.Observation || observation.IsReparsePoint || !observation.IsDirectory) { status = RetainedTreeMechanismStatus.IdentityChanged; return false; }
            return TryDelete(handle, out status);
        }
        catch { status = RetainedTreeMechanismStatus.InternalUnknown; return false; }
        finally
        {
            handle?.Dispose();
            CryptographicOperations.ZeroMemory(name ?? []);
            if (objectName != IntPtr.Zero) Marshal.FreeHGlobal(objectName);
            if (nameBuffer != IntPtr.Zero) { Marshal.Copy(new byte[name?.Length ?? 0], 0, nameBuffer, name?.Length ?? 0); Marshal.FreeHGlobal(nameBuffer); }
        }
    }

    private static bool IsCompatible() => OperatingSystem.IsWindowsVersionAtLeast(10) && IntPtr.Size == 8 && Marshal.SizeOf<IoStatusBlock>() == 16 && Marshal.OffsetOf<IoStatusBlock>(nameof(IoStatusBlock.Information)).ToInt32() == 8 && Marshal.SizeOf<ObjectAttributes>() == 48 && Marshal.SizeOf<UnicodeString>() == 16 && HasExports();

    private static bool HasExports()
    {
        if (!NativeLibrary.TryLoad("ntdll.dll", out var module)) return false;
        try { return NativeLibrary.TryGetExport(module, "NtSetInformationFile", out _) && NativeLibrary.TryGetExport(module, "NtCreateFile", out _); }
        finally { NativeLibrary.Free(module); }
    }

    [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatusBlock ioStatus, IntPtr information, uint length, int informationClass);
    [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint desiredAccess, ref ObjectAttributes attributes, out IoStatusBlock ioStatus, IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);
}
