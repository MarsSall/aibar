using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace AIBar.Packaging.Supervisor;

internal interface IDurableProtectedMetadataStore : IDisposable
{
    bool TryWriteDurably(ReadOnlySpan<byte> protectedBytes);
}

internal enum CleanupMetadataPhase : byte { RetainedQuarantine = 1 }
internal readonly record struct CleanupRetryMetadata(CleanupMetadataPhase Phase, byte RetryCount, long NextEligibleUnixMilliseconds);

internal static class ScavengerMetadata
{
    private const byte MetadataVersion = 1, EvidenceVersionMarker = 1, MaximumRetryCount = 3;
    private const int MetadataLength = 100;

    internal static bool TryProtectAndVerify(CommittedChildEvidence evidence, DirectoryIdentity root, CleanupRetryMetadata metadata, out byte[]? protectedBytes)
    {
        protectedBytes = null;
        byte[]? plain = null;
        byte[]? roundTrip = null;
        byte[]? correlation = null;
        byte[]? rootId = null;
        try
        {
            if (evidence.Version != CommittedChildEvidence.VersionV1 || !evidence.HasValidDigest() || root.VolumeSerialNumber != evidence.Root.VolumeSerialNumber || !HasValidMetadata(metadata)) return false;
            rootId = Convert.FromHexString(root.FileId);
            if (rootId.Length != 16) return false;
            correlation = HMACSHA256.HashData(evidence.CorrelationKey, evidence.Digest);
            plain = new byte[MetadataLength];
            plain[0] = MetadataVersion;
            plain[1] = EvidenceVersionMarker;
            plain[2] = (byte)metadata.Phase;
            plain[3] = metadata.RetryCount;
            BitConverter.TryWriteBytes(plain.AsSpan(4, 8), metadata.NextEligibleUnixMilliseconds);
            Buffer.BlockCopy(evidence.Digest, 0, plain, 12, 32);
            Buffer.BlockCopy(correlation, 0, plain, 44, 32);
            BitConverter.TryWriteBytes(plain.AsSpan(76, 8), root.VolumeSerialNumber);
            rootId.CopyTo(plain, 84);
            if (!TryProtect(plain, true, out protectedBytes) || !TryProtect(protectedBytes, false, out roundTrip)) return false;
            return HasExpectedProtectedMetadata(roundTrip, evidence, root, metadata, correlation, rootId) && CryptographicOperations.FixedTimeEquals(plain, roundTrip);
        }
        catch { CryptographicOperations.ZeroMemory(protectedBytes ?? []); protectedBytes = null; return false; }
        finally
        {
            CryptographicOperations.ZeroMemory(plain ?? []);
            CryptographicOperations.ZeroMemory(roundTrip ?? []);
            CryptographicOperations.ZeroMemory(correlation ?? []);
            CryptographicOperations.ZeroMemory(rootId ?? []);
        }
    }

    private static bool HasValidMetadata(CleanupRetryMetadata metadata)
        => metadata.Phase == CleanupMetadataPhase.RetainedQuarantine && metadata.RetryCount <= MaximumRetryCount && metadata.NextEligibleUnixMilliseconds is > 0 and <= 253402300799999;

    private static bool HasExpectedProtectedMetadata(ReadOnlySpan<byte> value, CommittedChildEvidence evidence, DirectoryIdentity root, CleanupRetryMetadata metadata, ReadOnlySpan<byte> correlation, ReadOnlySpan<byte> rootId)
    {
        if (value.Length != MetadataLength || !HasValidMetadata(metadata) || value[0] != MetadataVersion || value[1] != EvidenceVersionMarker || value[2] != (byte)metadata.Phase || value[3] != metadata.RetryCount || BitConverter.ToInt64(value.Slice(4, 8)) != metadata.NextEligibleUnixMilliseconds || BitConverter.ToUInt64(value.Slice(76, 8)) != root.VolumeSerialNumber) return false;
        return CryptographicOperations.FixedTimeEquals(value.Slice(12, 32), evidence.Digest)
            && CryptographicOperations.FixedTimeEquals(value.Slice(44, 32), correlation)
            && CryptographicOperations.FixedTimeEquals(value.Slice(84, 16), rootId);
    }

    private static bool TryProtect(byte[] input, bool protect, out byte[] output)
    {
        output = [];
        var inputPointer = IntPtr.Zero;
        DataBlob result = default;
        try
        {
            inputPointer = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, inputPointer, input.Length);
            var source = new DataBlob(input.Length, inputPointer);
            var succeeded = protect
                ? CryptProtectData(ref source, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result)
                : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out result);
            if (!succeeded || result.Length is <= 0 or > 4096 || result.Pointer == IntPtr.Zero) return false;
            output = new byte[result.Length];
            Marshal.Copy(result.Pointer, output, 0, output.Length);
            return true;
        }
        catch { CryptographicOperations.ZeroMemory(output); output = []; return false; }
        finally
        {
            if (inputPointer != IntPtr.Zero) { Marshal.Copy(new byte[input.Length], 0, inputPointer, input.Length); Marshal.FreeHGlobal(inputPointer); }
            if (result.Pointer != IntPtr.Zero) { Marshal.Copy(new byte[result.Length], 0, result.Pointer, result.Length); _ = LocalFree(result.Pointer); }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob(int length, IntPtr pointer) { public int Length = length; public IntPtr Pointer = pointer; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
