using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AIBar.Packaging.Supervisor;

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
