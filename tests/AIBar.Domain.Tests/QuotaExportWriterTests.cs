using System.Collections.Concurrent;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Desktop;
namespace AIBar.Domain.Tests;
public sealed class QuotaExportWriterTests : IDisposable
{
    private const FileSystemRights DirectoryRights = FileSystemRights.FullControl;
    private const FileSystemRights SnapshotRights = FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.Synchronize;
    private const InheritanceFlags DirectoryInheritance = InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit;
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-export-{Guid.NewGuid():N}");
    private string ExportDirectory => Path.Combine(_root, "AIBar");
    private string Destination => Path.Combine(ExportDirectory, "yasb-quota.json");
    public QuotaExportWriterTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Canonical_path_secure_temp_exact_utf8_and_replacement_have_minimum_rights()
    {
        Assert.Equal(Destination, WindowsQuotaExportPath.ForRoot(_root));
        Assert.Throws<ArgumentException>(() => WindowsQuotaExportPath.ForRoot(Path.Combine(_root, "..", "alias")));
        Assert.Throws<ArgumentException>(() => new WindowsAtomicQuotaExportWriter(Path.Combine(_root, "other.json"), _root, new()));
        AddWorldAccess(_root, true); string? temporary = null; var reads = new HashSet<QuotaExportWritePoint>();
        var writer = new WindowsAtomicQuotaExportWriter(_root, new(
            Point: (point, path) => { if (point == QuotaExportWritePoint.AfterTemporaryAcl) { temporary = path; Assert.Equal(0, new FileInfo(path).Length); AssertAcl(path, false); } },
            ReadAcl: (point, path, directory) => { reads.Add(point); return ReadAcl(path, directory); }));
        await writer.WriteAsync(Document(10), Now, default);
        AssertDocument(10); AssertAcl(ExportDirectory, true); AssertAcl(Destination, false);
        Assert.Equal(ExportDirectory, Path.GetDirectoryName(temporary));
        var tempName = Path.GetFileNameWithoutExtension(temporary); Assert.StartsWith(".aibar-quota-", tempName); Assert.True(Guid.TryParseExact(tempName[13..], "N", out _));
        AddWorldAccess(ExportDirectory, true); AddWorldAccess(Destination, false);
        await writer.WriteAsync(Document(20), Now.AddMinutes(1), default);
        var expected = Bytes(20, Now.AddMinutes(1)); Assert.Equal(expected, File.ReadAllBytes(Destination)); Assert.False(expected.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        AssertDocument(20); AssertAcl(ExportDirectory, true); AssertAcl(Destination, false); AssertNoTemporary();
        Assert.All(new[] { QuotaExportWritePoint.AfterDirectoryAcl, QuotaExportWritePoint.AfterDestinationAcl, QuotaExportWritePoint.AfterTemporaryAcl, QuotaExportWritePoint.AfterResultAcl }, point => Assert.Contains(point, reads));
    }

    [Fact]
    public async Task Secure_directory_repairs_existing_and_new_sibling_file_lifecycle_without_broad_principals()
    {
        Directory.CreateDirectory(ExportDirectory);
        var existing = Path.Combine(ExportDirectory, "quota.db");
        await File.WriteAllTextAsync(existing, "existing");

        await new WindowsAtomicQuotaExportWriter(_root, new()).WriteAsync(Document(10), Now, default);

        AssertDescendantAcl(existing);
        await File.AppendAllTextAsync(existing, "-updated");
        Assert.Equal("existing-updated", await File.ReadAllTextAsync(existing));
        var renamedExisting = Path.Combine(ExportDirectory, "quota-renamed.db");
        File.Move(existing, renamedExisting);
        File.Delete(renamedExisting);

        var created = Path.Combine(ExportDirectory, "analytics.db");
        await File.WriteAllTextAsync(created, "created");
        AssertDescendantAcl(created);
        await File.AppendAllTextAsync(created, "-updated");
        Assert.Equal("created-updated", await File.ReadAllTextAsync(created));
        var renamedCreated = Path.Combine(ExportDirectory, "analytics-renamed.db");
        File.Move(created, renamedCreated);
        File.Delete(renamedCreated);

        AssertAcl(ExportDirectory, true);
        AssertAcl(Destination, false);
    }

    [Theory]
    [InlineData((int)QuotaExportWritePoint.BeforeDirectoryAcl, false)]
    [InlineData((int)QuotaExportWritePoint.AfterDirectoryAcl, false)]
    [InlineData((int)QuotaExportWritePoint.BeforeDestinationAcl, false)]
    [InlineData((int)QuotaExportWritePoint.AfterDestinationAcl, false)]
    [InlineData((int)QuotaExportWritePoint.BeforeTemporaryAcl, false)]
    [InlineData((int)QuotaExportWritePoint.AfterTemporaryAcl, false)]
    [InlineData((int)QuotaExportWritePoint.BeforeResultAcl, true)]
    [InlineData((int)QuotaExportWritePoint.AfterResultAcl, true)]
    public async Task Acl_application_and_readback_failures_close_at_every_boundary(int failureValue, bool committed)
    {
        var failure = (QuotaExportWritePoint)failureValue;
        foreach (var readback in failure.ToString().StartsWith("After", StringComparison.Ordinal) ? new[] { false, true } : new[] { false })
        {
            Seed(5); var previous = File.ReadAllBytes(Destination);
            var seams = readback
                ? new QuotaExportWriterSeams(ReadAcl: (point, path, directory) => point == failure ? throw new UnauthorizedAccessException("synthetic readback failure") : ReadAcl(path, directory))
                : new QuotaExportWriterSeams(Point: (point, _) => { if (point == failure) throw new UnauthorizedAccessException("synthetic ACL failure"); });
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new WindowsAtomicQuotaExportWriter(_root, seams).WriteAsync(Document(30), Now, default).AsTask());
            Assert.Equal(committed ? 30m : 5m, Percentage()); if (!committed) Assert.Equal(previous, File.ReadAllBytes(Destination)); AssertNoTemporary();
        }
    }

    [Fact]
    public async Task Precommit_failures_cancellation_sharing_and_destination_races_preserve_bytes()
    {
        Seed(9); var previous = File.ReadAllBytes(Destination); var unrelated = Path.Combine(ExportDirectory, ".aibar-quota-not-owned.tmp"); await File.WriteAllTextAsync(unrelated, "keep");
        foreach (var point in new[] { QuotaExportWritePoint.AfterWrite, QuotaExportWritePoint.BeforeCommit })
        {
            var writer = new WindowsAtomicQuotaExportWriter(_root, new((value, _) => { if (value == point) throw new IOException("synthetic failure"); }));
            await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(Document(40), Now, default).AsTask()); Assert.Equal(previous, File.ReadAllBytes(Destination)); AssertNoTemporary();
        }
        var tampered = new WindowsAtomicQuotaExportWriter(_root, new((point, path) => { if (point == QuotaExportWritePoint.AfterWrite) File.WriteAllBytes(path, Bytes(99)); }));
        await Assert.ThrowsAsync<IOException>(() => tampered.WriteAsync(Document(41), Now, default).AsTask()); Assert.Equal(previous, File.ReadAllBytes(Destination)); AssertNoTemporary();
        using (var locked = new FileStream(Destination, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => new WindowsAtomicQuotaExportWriter(_root, new()).WriteAsync(Document(41), Now, default).AsTask());
        Assert.Equal(previous, File.ReadAllBytes(Destination)); AssertNoTemporary();
        var cancellation = new CancellationTokenSource();
        var cancelled = new WindowsAtomicQuotaExportWriter(_root, new((point, _) => { if (point == QuotaExportWritePoint.BeforeCommit) cancellation.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WriteAsync(Document(42), Now, cancellation.Token).AsTask()); Assert.Equal(previous, File.ReadAllBytes(Destination));
        var disappearing = new WindowsAtomicQuotaExportWriter(_root, new((point, _) => { if (point == QuotaExportWritePoint.BeforeCommit) File.Delete(Destination); }));
        await Assert.ThrowsAsync<IOException>(() => disappearing.WriteAsync(Document(43), Now, default).AsTask()); Assert.False(File.Exists(Destination)); AssertNoTemporary();
        var raced = new WindowsAtomicQuotaExportWriter(_root, new((point, _) => { if (point == QuotaExportWritePoint.BeforeCommit) File.WriteAllText(Destination, "raced"); }));
        await Assert.ThrowsAsync<IOException>(() => raced.WriteAsync(Document(44), Now, default).AsTask()); Assert.Equal("raced", await File.ReadAllTextAsync(Destination)); Assert.True(File.Exists(unrelated)); AssertNoTemporary();
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("destination")]
    [InlineData("temporary")]
    [InlineData("commit-race")]
    [InlineData("result-race")]
    public async Task Parent_destination_temporary_commit_and_result_reparse_substitution_is_rejected(string target)
    {
        Seed(50); var previous = File.ReadAllBytes(Destination); var raced = false;
        var seams = new QuotaExportWriterSeams((point, _) => { if (target == "commit-race" && point == QuotaExportWritePoint.BeforeCommit || target == "result-race" && point == QuotaExportWritePoint.BeforeResultVerification) raced = true; },
            path => target == "parent" && path == ExportDirectory || target == "destination" && path == Destination || target == "temporary" && IsOwnedTemporary(path) || raced && path == Destination ? FileAttributes.ReparsePoint : File.GetAttributes(path));
        await Assert.ThrowsAsync<IOException>(() => new WindowsAtomicQuotaExportWriter(_root, seams).WriteAsync(Document(51), Now, default).AsTask());
        AssertNoTemporary(); if (target != "result-race") Assert.Equal(previous, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Concurrent_readers_across_first_and_replace_see_only_complete_old_or_new_documents()
    {
        async Task<decimal[]> Publish(decimal value)
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var views = new ConcurrentBag<decimal>();
            var writer = new WindowsAtomicQuotaExportWriter(_root, new((point, _) => { if (point == QuotaExportWritePoint.BeforeCommit) { entered.Set(); release.Wait(); } }));
            var write = Task.Run(() => writer.WriteAsync(Document(value), Now, default).AsTask()); Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            var reader = Task.Run(async () => { while (!write.IsCompleted) { var view = TryReadShared(); if (view is not null) views.Add(view.Value); await Task.Delay(1); } var final = TryReadShared(); if (final is not null) views.Add(final.Value); });
            if (File.Exists(Destination)) Assert.True(SpinWait.SpinUntil(() => !views.IsEmpty, TimeSpan.FromSeconds(2))); else await Task.Delay(20);
            release.Set(); await Task.WhenAll(write, reader); return views.ToArray();
        }
        var first = await Publish(60); Assert.NotEmpty(first); Assert.All(first, value => Assert.Equal(60m, value));
        using var held = new FileStream(Destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var replacement = await Publish(70); Assert.Contains(60m, replacement); Assert.Contains(70m, replacement); Assert.All(replacement, value => Assert.Contains(value, new[] { 60m, 70m }));
        Assert.Equal(60m, ReadPercentage(held)); AssertDocument(70); AssertAcl(Destination, false); AssertNoTemporary();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_first_or_replace_requests_suppress_stale_writes_and_commit_latest(bool replacing)
    {
        if (replacing) Seed(60); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var writes = 0;
        var writer = new WindowsAtomicQuotaExportWriter(_root, new((point, _) => { if (point == QuotaExportWritePoint.AfterWrite && Interlocked.Increment(ref writes) == 1) { entered.Set(); release.Wait(); } }));
        var first = Task.Run(() => writer.WriteAsync(Document(70), Now, default).AsTask()); Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
        var stale = writer.WriteAsync(Document(80), Now.AddMinutes(1), default).AsTask(); var latest = writer.WriteAsync(Document(90), Now.AddMinutes(2), default).AsTask();
        release.Set(); await Task.WhenAll(first, stale, latest); AssertDocument(90); AssertAcl(Destination, false); AssertNoTemporary();
    }

    private static QuotaExportDocument Document(decimal value) => new(1, Now, QuotaExportState.Current, null, Now, new(value, Now.AddHours(1)), new(value, Now.AddDays(1)));
    private static byte[] Bytes(decimal value, DateTimeOffset? generatedAt = null) => Encoding.UTF8.GetBytes(QuotaExportWire.Serialize(Document(value), generatedAt ?? Now));
    private void Seed(decimal value) { Directory.CreateDirectory(ExportDirectory); File.WriteAllBytes(Destination, Bytes(value)); }
    private decimal Percentage() { using var stream = File.OpenRead(Destination); return ReadPercentage(stream); }
    private void AssertDocument(decimal value) => Assert.Equal(value, Percentage());
    private decimal? TryReadShared() { try { using var stream = new FileStream(Destination, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); return ReadPercentage(stream); } catch (IOException) { return null; } }
    private static decimal ReadPercentage(Stream stream) { using var json = JsonDocument.Parse(stream); Assert.Equal(7, json.RootElement.EnumerateObject().Count()); Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32()); Assert.Equal("current", json.RootElement.GetProperty("state").GetString()); return json.RootElement.GetProperty("fiveHour").GetProperty("percentageUsed").GetDecimal(); }
    private static FileSystemSecurity ReadAcl(string path, bool directory) => directory ? FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(path)) : FileSystemAclExtensions.GetAccessControl(new FileInfo(path));
    private static void AssertAcl(string path, bool directory)
    {
        var security = ReadAcl(path, directory); Assert.True(security.AreAccessRulesProtected); var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray(); var rights = directory ? DirectoryRights : SnapshotRights;
        var inheritance = directory ? DirectoryInheritance : InheritanceFlags.None;
        Assert.Equal(2, rules.Length); Assert.All(rules, rule => { Assert.Equal(AccessControlType.Allow, rule.AccessControlType); Assert.Equal(rights, rule.FileSystemRights); Assert.Equal(inheritance, rule.InheritanceFlags); Assert.Equal(PropagationFlags.None, rule.PropagationFlags); if (!directory) Assert.NotEqual(FileSystemRights.FullControl, rule.FileSystemRights); });
        Assert.Equal(ExpectedSids(), rules.Select(rule => rule.IdentityReference.Value).Order());
    }
    private static void AssertDescendantAcl(string path)
    {
        var security = ReadAcl(path, false); var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
        Assert.False(security.AreAccessRulesProtected); Assert.Equal(2, rules.Length); Assert.All(rules, rule => { Assert.Equal(AccessControlType.Allow, rule.AccessControlType); Assert.Equal(DirectoryRights, rule.FileSystemRights); Assert.True(rule.IsInherited); });
        Assert.Equal(ExpectedSids(), rules.Select(rule => rule.IdentityReference.Value).Order());
    }
    private static IOrderedEnumerable<string> ExpectedSids() => new[] { WindowsIdentity.GetCurrent().User!.Value, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value }.Order();
    private static void AddWorldAccess(string path, bool directory)
    {
        var security = ReadAcl(path, directory); var inheritance = directory ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
        if (directory) FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(path), (DirectorySecurity)security); else FileSystemAclExtensions.SetAccessControl(new FileInfo(path), (FileSecurity)security);
    }
    private static bool IsOwnedTemporary(string path) { var name = Path.GetFileNameWithoutExtension(path); return Path.GetExtension(path) == ".tmp" && name.StartsWith(".aibar-quota-", StringComparison.Ordinal) && Guid.TryParseExact(name[13..], "N", out _); }
    private void AssertNoTemporary() => Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(ExportDirectory), IsOwnedTemporary);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
