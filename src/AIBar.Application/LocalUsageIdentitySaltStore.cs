using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AIBar.Application;

public enum LocalUsageIdentitySaltError { InvalidPath = 1, UnsafePath = 2, InvalidEnvelope = 3, UnsupportedVersion = 4, ProtectionFailed = 5, InvalidPlaintext = 6, StorageFailure = 7 }

public sealed class LocalUsageIdentitySaltException : Exception
{
    internal LocalUsageIdentitySaltException(LocalUsageIdentitySaltError code) : base("Local usage identity salt is unavailable.") => Code = code;
    public LocalUsageIdentitySaltError Code { get; }
    public override string ToString() => $"{GetType().FullName}: {Message} ({Code})";
}

public sealed class LocalUsageIdentitySaltLease : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _value;
    internal LocalUsageIdentitySaltLease(byte[] value) => _value = value.ToArray();
    /// <summary>Returns a caller-owned copy that the caller must zero after use.</summary>
    public byte[] CopyBytes() { lock (_gate) return (_value ?? throw new ObjectDisposedException(nameof(LocalUsageIdentitySaltLease))).ToArray(); }
    public LocalUsageDiscoveryFacts CreateDiscoveryFacts(string? openCodeDataRoot, string? piSessionsRoot, PiUsageDiscoveryLayout piLayout = PiUsageDiscoveryLayout.DefaultEncodedDirectories)
    {
        var copy = CopyBytes();
        try { return new(openCodeDataRoot, piSessionsRoot, copy, piLayout); }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }
    public void Dispose() { lock (_gate) { if (_value is null) return; CryptographicOperations.ZeroMemory(_value); _value = null; } }
    public override string ToString() => "Local usage identity salt lease";
}

internal interface ILocalUsageIdentityProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] payload);
}

/// <summary>Creates a salt when the explicit path is missing. A continuity-aware composition layer must decide whether recreation is allowed after prior use.</summary>
public sealed class LocalUsageIdentitySaltStore
{
    private const int SaltLength = 32, HeaderLength = 13, MaximumProtectedLength = 4096, MaximumPathCharacters = 1024;
    private static ReadOnlySpan<byte> Magic => "AIBARSLT"u8;
    private readonly string _path;
    private readonly ILocalUsageIdentityProtector _protector;
    private readonly Func<CancellationToken, ValueTask> _beforeCommit;
    private readonly Action? _afterCommit;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalUsageIdentitySaltStore(string? path) : this(path, new WindowsCurrentUserIdentityProtector()) { }
    internal LocalUsageIdentitySaltStore(string? path, ILocalUsageIdentityProtector protector, Func<CancellationToken, ValueTask>? beforeCommit = null, Action? afterCommit = null)
    {
        _path = CanonicalPath(path); _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _beforeCommit = beforeCommit ?? (_ => ValueTask.CompletedTask); _afterCommit = afterCommit;
    }

    public async ValueTask<LocalUsageIdentitySaltLease> GetOrCreateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); EnsureSafePath();
            var directory = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(directory); EnsureSafePath();
            return File.Exists(_path) ? await LoadAsync(cancellationToken) : await CreateAsync(directory, cancellationToken);
        }
        catch (LocalUsageIdentitySaltException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (StorageException(exception)) { throw Failure(LocalUsageIdentitySaltError.StorageFailure); }
        finally { _gate.Release(); }
    }

    public async ValueTask<LocalUsageIdentitySaltLease?> LoadExistingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); EnsureSafePath();
            return File.Exists(_path) ? await LoadAsync(cancellationToken) : null;
        }
        catch (LocalUsageIdentitySaltException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (StorageException(exception)) { throw Failure(LocalUsageIdentitySaltError.StorageFailure); }
        finally { _gate.Release(); }
    }

    public override string ToString() => "Local usage identity salt store";

    private async ValueTask<LocalUsageIdentitySaltLease> LoadAsync(CancellationToken token)
    {
        EnsureSafePath(); byte[]? plaintext = null;
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length is < HeaderLength or > HeaderLength + MaximumProtectedLength) throw Failure(LocalUsageIdentitySaltError.InvalidEnvelope);
            var envelope = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(envelope, token);
            if (!envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic)) throw Failure(LocalUsageIdentitySaltError.InvalidEnvelope);
            if (envelope[8] != 1) throw Failure(LocalUsageIdentitySaltError.UnsupportedVersion);
            var length = BinaryPrimitives.ReadInt32LittleEndian(envelope.AsSpan(9, 4));
            if (length is <= 0 or > MaximumProtectedLength || envelope.Length != HeaderLength + length) throw Failure(LocalUsageIdentitySaltError.InvalidEnvelope);
            try { plaintext = _protector.Unprotect(envelope[HeaderLength..]); }
            catch (Exception exception) when (ProtectionException(exception)) { throw Failure(LocalUsageIdentitySaltError.ProtectionFailed); }
            if (plaintext.Length != SaltLength) throw Failure(LocalUsageIdentitySaltError.InvalidPlaintext);
            return new(plaintext);
        }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async ValueTask<LocalUsageIdentitySaltLease> CreateAsync(string directory, CancellationToken token)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength); string? temporary = null;
        try
        {
            byte[] payload;
            try { payload = _protector.Protect(salt); }
            catch (Exception exception) when (ProtectionException(exception)) { throw Failure(LocalUsageIdentitySaltError.ProtectionFailed); }
            if (payload.Length is <= 0 or > MaximumProtectedLength) throw Failure(LocalUsageIdentitySaltError.ProtectionFailed);
            var envelope = new byte[HeaderLength + payload.Length]; Magic.CopyTo(envelope); envelope[8] = 1;
            BinaryPrimitives.WriteInt32LittleEndian(envelope.AsSpan(9, 4), payload.Length); payload.CopyTo(envelope, HeaderLength);
            temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await stream.WriteAsync(envelope, token); await stream.FlushAsync(token); stream.Flush(flushToDisk: true); }
            await _beforeCommit(token); token.ThrowIfCancellationRequested(); EnsureSafePath();
            try { File.Move(temporary, _path); }
            catch (IOException) when (File.Exists(_path))
            { File.Delete(temporary); temporary = null; return await LoadAsync(token); }
            temporary = null; _afterCommit?.Invoke();
            return new(salt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(salt);
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception exception) when (StorageException(exception)) { throw Failure(LocalUsageIdentitySaltError.StorageFailure); }
        }
    }

    private void EnsureSafePath()
    {
        if (File.Exists(_path) && (File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0) throw Failure(LocalUsageIdentitySaltError.UnsafePath);
        for (var current = new DirectoryInfo(Path.GetDirectoryName(_path)!); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw Failure(LocalUsageIdentitySaltError.UnsafePath);
    }

    private static string CanonicalPath(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path != path.Trim() || Path.EndsInDirectorySeparator(path) || !Path.IsPathFullyQualified(path)) throw Failure(LocalUsageIdentitySaltError.InvalidPath);
            var full = Path.GetFullPath(path); var root = Path.GetPathRoot(full);
            if (full.Length > MaximumPathCharacters - 40 || Path.GetDirectoryName(full) is null || root is null || StringComparer.OrdinalIgnoreCase.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(root))) throw Failure(LocalUsageIdentitySaltError.InvalidPath);
            return full;
        }
        catch (LocalUsageIdentitySaltException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { throw Failure(LocalUsageIdentitySaltError.InvalidPath); }
    }

    private static bool StorageException(Exception exception) => exception is IOException or UnauthorizedAccessException or NotSupportedException;
    private static bool ProtectionException(Exception exception) => exception is CryptographicException or PlatformNotSupportedException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;
    private static LocalUsageIdentitySaltException Failure(LocalUsageIdentitySaltError code) => new(code);
}

internal sealed class WindowsCurrentUserIdentityProtector : ILocalUsageIdentityProtector
{
    private const uint UiForbidden = 1;
    private const string Description = "AIBar local usage identity salt v1";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("aibar-local-usage-identity-salt-dpapi-v1");
    public byte[] Protect(byte[] plaintext) => Transform(plaintext, true);
    public byte[] Unprotect(byte[] payload) => Transform(payload, false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var input = Allocate(value); var entropy = Allocate(Entropy); DataBlob output = default; IntPtr description = IntPtr.Zero;
        try
        {
            var success = protect
                ? CryptProtectData(ref input, Description, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref input, out description, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!success || output.Length is <= 0 or > 4096 || output.Data == IntPtr.Zero) throw new CryptographicException("Data protection failed.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            Release(input, false); Release(entropy, false); Release(output, true);
            if (description != IntPtr.Zero) LocalFree(description);
        }
    }

    private static DataBlob Allocate(byte[] value)
    { var blob = new DataBlob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) }; Marshal.Copy(value, 0, blob.Data, value.Length); return blob; }
    private static void Release(DataBlob blob, bool local)
    { if (blob.Data == IntPtr.Zero) return; for (var index = 0; index < blob.Length; index++) Marshal.WriteByte(blob.Data, index, 0); if (local) LocalFree(blob.Data); else Marshal.FreeHGlobal(blob.Data); }

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { internal int Length; internal IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, out IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
