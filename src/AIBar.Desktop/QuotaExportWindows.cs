using System.IO;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AIBar.Application;

namespace AIBar.Desktop;

public static class WindowsQuotaExportPath
{
    public static string ForCurrentUser() => ForRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    internal static string ForRoot(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        var full = Path.GetFullPath(localApplicationData);
        static string Trim(string value) => value.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.IsPathFullyQualified(localApplicationData) || !string.Equals(Trim(localApplicationData), Trim(full), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Local application data root must be an exact canonical path.", nameof(localApplicationData));
        return Path.Combine(full, "AIBar", "yasb-quota.json");
    }
}

internal enum QuotaExportWritePoint
{
    BeforeDirectoryAcl, AfterDirectoryAcl, BeforeDestinationAcl, AfterDestinationAcl,
    BeforeTemporaryAcl, AfterTemporaryAcl, AfterWrite, BeforeCommit,
    BeforeResultAcl, AfterResultAcl, BeforeResultVerification,
}

internal sealed record QuotaExportWriterSeams(
    Action<QuotaExportWritePoint, string>? Point = null,
    Func<string, System.IO.FileAttributes>? Attributes = null,
    Func<QuotaExportWritePoint, string, bool, FileSystemSecurity>? ReadAcl = null);

[SupportedOSPlatform("windows")]
public sealed class WindowsAtomicQuotaExportWriter : IQuotaExportWriter
{
    private const FileSystemRights DirectoryRights = FileSystemRights.ListDirectory | FileSystemRights.CreateFiles | FileSystemRights.Traverse |
        FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ReadAttributes | FileSystemRights.ReadPermissions |
        FileSystemRights.ChangePermissions | FileSystemRights.Synchronize;
    private const FileSystemRights SnapshotRights = FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete |
        FileSystemRights.ChangePermissions | FileSystemRights.Synchronize;
    private const FileSystemRights TemporaryOpenRights = FileSystemRights.Write | FileSystemRights.Synchronize;
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private readonly string _path;
    private readonly string _directory;
    private readonly QuotaExportWriterSeams _seams;
    private readonly bool _protectDirectoryAcl;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _requested;

    public WindowsAtomicQuotaExportWriter() : this(WindowsQuotaExportPath.ForCurrentUser()) { }
    public WindowsAtomicQuotaExportWriter(string path) : this(path, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), new()) { }
    internal static WindowsAtomicQuotaExportWriter ForOwnedSyntheticDataDirectory(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        return new(new DirectoryInfo(Path.GetFullPath(dataDirectory)));
    }
    private WindowsAtomicQuotaExportWriter(DirectoryInfo dataDirectory)
    {
        _directory = Path.TrimEndingDirectorySeparator(dataDirectory.FullName);
        _path = Path.Combine(_directory, "yasb-quota.json");
        _seams = new();
        _protectDirectoryAcl = false;
    }
    internal WindowsAtomicQuotaExportWriter(string localApplicationData, QuotaExportWriterSeams seams)
        : this(WindowsQuotaExportPath.ForRoot(localApplicationData), localApplicationData, seams) { }
    internal WindowsAtomicQuotaExportWriter(string path, string localApplicationData, QuotaExportWriterSeams seams)
    {
        var expected = WindowsQuotaExportPath.ForRoot(localApplicationData);
        if (!string.Equals(path, expected, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Quota export must use the exact owned snapshot path.", nameof(path));
        _path = expected;
        _directory = Path.GetDirectoryName(_path) ?? throw new ArgumentException("Quota export requires a parent directory.", nameof(localApplicationData));
        _seams = seams ?? throw new ArgumentNullException(nameof(seams));
        _protectDirectoryAcl = true;
    }

    public async ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        var request = Interlocked.Increment(ref _requested);
        await _gate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request != Volatile.Read(ref _requested)) return;
            var expected = Encoding.UTF8.GetBytes(QuotaExportWire.Serialize(document, generatedAt));
            var replacing = File.Exists(_path);
            if (Directory.Exists(_path)) throw new IOException("Quota export destination is not a file.");
            if (replacing) SecureFile(_path, QuotaExportWritePoint.BeforeDestinationAcl, QuotaExportWritePoint.AfterDestinationAcl);
            if (_protectDirectoryAcl) SecureDirectory();
            else RejectAncestors();

            temporary = Path.Combine(_directory, $".aibar-quota-{Guid.NewGuid():N}.tmp");
            Point(QuotaExportWritePoint.BeforeTemporaryAcl, temporary);
            RejectReparse(temporary, allowMissing: true);
            await using (var stream = FileSystemAclExtensions.Create(new FileInfo(temporary), FileMode.CreateNew, TemporaryOpenRights,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough, FileAcl()))
            {
                Point(QuotaExportWritePoint.AfterTemporaryAcl, temporary);
                RejectAncestors(); RejectReparse(temporary); VerifyAcl(temporary, false, QuotaExportWritePoint.AfterTemporaryAcl);
                await stream.WriteAsync(expected, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            Point(QuotaExportWritePoint.AfterWrite, temporary);
            ValidateBytes(temporary, expected); VerifyAcl(temporary, false, QuotaExportWritePoint.AfterTemporaryAcl);
            Point(QuotaExportWritePoint.BeforeCommit, temporary); cancellationToken.ThrowIfCancellationRequested();
            if (request != Volatile.Read(ref _requested)) return;
            RejectAncestors(); RejectReparse(temporary); RejectReparse(_path, allowMissing: !replacing);
            if (replacing)
            {
                if (!File.Exists(_path)) throw new IOException("Quota export destination changed before replace.");
                File.Replace(temporary, _path, null, ignoreMetadataErrors: false);
            }
            else
            {
                if (File.Exists(_path) || Directory.Exists(_path)) throw new IOException("Quota export destination appeared before first commit.");
                File.Move(temporary, _path);
            }
            temporary = null;
            Point(QuotaExportWritePoint.BeforeResultAcl, _path); SetAcl(_path, false); Point(QuotaExportWritePoint.AfterResultAcl, _path);
            VerifyAcl(_path, false, QuotaExportWritePoint.AfterResultAcl);
            Point(QuotaExportWritePoint.BeforeResultVerification, _path);
            RejectAncestors(); RejectReparse(_path); ValidateBytes(_path, expected); VerifyAcl(_path, false, QuotaExportWritePoint.AfterResultAcl);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            _gate.Release();
        }
    }

    private void SecureDirectory()
    {
        RejectAncestors(); Point(QuotaExportWritePoint.BeforeDirectoryAcl, _directory);
        var info = new DirectoryInfo(_directory);
        if (info.Exists) FileSystemAclExtensions.SetAccessControl(info, DirectoryAcl());
        else FileSystemAclExtensions.Create(info, DirectoryAcl());
        Point(QuotaExportWritePoint.AfterDirectoryAcl, _directory);
        RejectAncestors(); VerifyAcl(_directory, true, QuotaExportWritePoint.AfterDirectoryAcl);
    }

    private void SecureFile(string path, QuotaExportWritePoint before, QuotaExportWritePoint after)
    {
        RejectReparse(path); Point(before, path); SetAcl(path, false); Point(after, path); VerifyAcl(path, false, after);
    }

    private static DirectorySecurity DirectoryAcl() => (DirectorySecurity)Acl(new DirectorySecurity(), DirectoryRights);
    private static FileSecurity FileAcl() => (FileSecurity)Acl(new FileSecurity(), SnapshotRights);
    private static FileSystemSecurity Acl(FileSystemSecurity security, FileSystemRights rights)
    {
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Current Windows user SID is unavailable.");
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { user, SystemSid })
            security.AddAccessRule(new(sid, rights, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    private static void SetAcl(string path, bool directory)
    {
        if (directory) FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path), DirectoryAcl());
        else FileSystemAclExtensions.SetAccessControl(new FileInfo(path), FileAcl());
    }

    private void VerifyAcl(string path, bool directory, QuotaExportWritePoint boundary)
    {
        var security = ReadAcl(boundary, path, directory);
        if (!security.AreAccessRulesProtected) throw new UnauthorizedAccessException("Quota export ACL inheritance remains enabled.");
        var user = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException("Current Windows user SID is unavailable.");
        var expectedSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { user.Value, SystemSid.Value };
        var expectedRights = directory ? DirectoryRights : SnapshotRights;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        if (rules.Length != 2 || rules.Any(rule => rule.AccessControlType != AccessControlType.Allow ||
            rule.FileSystemRights != expectedRights || rule.InheritanceFlags != InheritanceFlags.None ||
            rule.PropagationFlags != PropagationFlags.None || !expectedSids.Remove(rule.IdentityReference.Value)) || expectedSids.Count != 0)
            throw new UnauthorizedAccessException("Quota export ACL readback is not exact and minimum-rights.");
    }

    private FileSystemSecurity ReadAcl(QuotaExportWritePoint boundary, string path, bool directory)
    {
        if (_seams.ReadAcl is not null) return _seams.ReadAcl(boundary, path, directory);
        return directory ? FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(path)) : FileSystemAclExtensions.GetAccessControl(new FileInfo(path));
    }

    private void RejectAncestors()
    {
        for (var current = _directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current) ?? string.Empty)
            if (Directory.Exists(current) && (Attributes(current) & System.IO.FileAttributes.ReparsePoint) != 0)
                throw new IOException("Quota export refuses reparse-point ancestry.");
    }

    private void RejectReparse(string path, bool allowMissing = false)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) { if (allowMissing) return; throw new IOException("Quota export path disappeared."); }
        if ((Attributes(path) & System.IO.FileAttributes.ReparsePoint) != 0) throw new IOException("Quota export refuses reparse points.");
    }

    private System.IO.FileAttributes Attributes(string path) => (_seams.Attributes ?? File.GetAttributes)(path);
    private void Point(QuotaExportWritePoint point, string path) => _seams.Point?.Invoke(point, path);
    private static void ValidateBytes(string path, byte[] expected)
    {
        var bytes = File.ReadAllBytes(path);
        using var _ = JsonDocument.Parse(bytes);
        if (!bytes.AsSpan().SequenceEqual(expected)) throw new IOException("Quota export bytes failed complete-document validation.");
    }
}
