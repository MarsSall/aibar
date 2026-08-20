using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageIdentitySaltStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-salt-{Guid.NewGuid():N}");
    private string SaltPath(string name) => Path.Combine(_root, name);

    [Fact]
    public void Invalid_explicit_paths_are_rejected_without_resolution()
    {
        foreach (var path in new string?[] { null, "", " ", "relative.salt", Path.GetPathRoot(_root) })
            Assert.Equal(LocalUsageIdentitySaltError.InvalidPath, Assert.Throws<LocalUsageIdentitySaltException>(() => new LocalUsageIdentitySaltStore(path)).Code);
    }

    [Fact]
    public async Task Salt_is_protected_stable_independent_and_drives_installation_scoped_identities()
    {
        var protector = new SyntheticProtector(17); var path = SaltPath("identity.salt");
        using var first = await new LocalUsageIdentitySaltStore(path, protector).GetOrCreateAsync(default);
        var firstBytes = first.CopyBytes();
        try
        {
            Assert.Equal(32, firstBytes.Length); Assert.Equal(-1, (await File.ReadAllBytesAsync(path)).AsSpan().IndexOf(firstBytes));
            using var reopened = await new LocalUsageIdentitySaltStore(path, protector).GetOrCreateAsync(default);
            var reopenedBytes = reopened.CopyBytes();
            try
            {
                Assert.Equal(firstBytes, reopenedBytes); Assert.NotSame(firstBytes, reopenedBytes);
                first.Dispose(); var independent = reopened.CopyBytes(); try { Assert.Equal(firstBytes, independent); } finally { CryptographicOperations.ZeroMemory(independent); }
                var identity = Identity(reopened);
                using var other = await new LocalUsageIdentitySaltStore(SaltPath("other.salt"), protector).GetOrCreateAsync(default);
                Assert.Equal(identity, Identity(reopened)); Assert.NotEqual(identity, Identity(other));
                Assert.Equal("Local usage identity salt lease", reopened.ToString()); Assert.Equal("{}", JsonSerializer.Serialize(reopened));
                Assert.Equal("Local usage identity salt store", new LocalUsageIdentitySaltStore(path, protector).ToString());
            }
            finally { CryptographicOperations.ZeroMemory(reopenedBytes); }
        }
        finally { CryptographicOperations.ZeroMemory(firstBytes); }

        var stored = await File.ReadAllBytesAsync(path);
        var mismatch = await Assert.ThrowsAsync<LocalUsageIdentitySaltException>(() => new LocalUsageIdentitySaltStore(path, new SyntheticProtector(18)).GetOrCreateAsync(default).AsTask());
        Assert.Equal(LocalUsageIdentitySaltError.ProtectionFailed, mismatch.Code); Assert.Equal(stored, await File.ReadAllBytesAsync(path));
        Assert.DoesNotContain(_root, mismatch.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, mismatch.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, mismatch.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("truncated")]
    [InlineData("excess")]
    [InlineData("oversized")]
    [InlineData("zero")]
    [InlineData("corrupt")]
    [InlineData("plaintext-length")]
    public async Task Invalid_envelopes_fail_closed_without_replacement(string mutation)
    {
        var protector = new SyntheticProtector(23); var path = SaltPath($"{mutation}.salt");
        using (await new LocalUsageIdentitySaltStore(path, protector).GetOrCreateAsync(default)) { }
        var invalid = Mutate(await File.ReadAllBytesAsync(path), mutation, protector); await File.WriteAllBytesAsync(path, invalid);

        var error = await Assert.ThrowsAsync<LocalUsageIdentitySaltException>(() => new LocalUsageIdentitySaltStore(path, protector).GetOrCreateAsync(default).AsTask());

        var expected = mutation switch { "version" => LocalUsageIdentitySaltError.UnsupportedVersion, "corrupt" => LocalUsageIdentitySaltError.ProtectionFailed, "plaintext-length" => LocalUsageIdentitySaltError.InvalidPlaintext, _ => LocalUsageIdentitySaltError.InvalidEnvelope };
        Assert.Equal(expected, error.Code); Assert.Equal(invalid, await File.ReadAllBytesAsync(path)); Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Concurrent_calls_serialize_and_cross_instance_creation_converges_without_overwrite()
    {
        var protector = new SyntheticProtector(31); var same = new LocalUsageIdentitySaltStore(SaltPath("same.salt"), protector);
        var sameLeases = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => same.GetOrCreateAsync(default).AsTask()));
        try { var values = sameLeases.Select(item => item.CopyBytes()).ToArray(); try { Assert.All(values, value => Assert.Equal(values[0], value)); Assert.Equal(4, values.Distinct(ReferenceEqualityComparer.Instance).Count()); } finally { foreach (var value in values) CryptographicOperations.ZeroMemory(value); } }
        finally { foreach (var lease in sameLeases) lease.Dispose(); }

        var arrived = 0; var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask BeforeCommit(CancellationToken token) { if (Interlocked.Increment(ref arrived) == 2) release.SetResult(); await release.Task.WaitAsync(token); }
        var path = SaltPath("race.salt"); var left = new LocalUsageIdentitySaltStore(path, protector, BeforeCommit); var right = new LocalUsageIdentitySaltStore(path, protector, BeforeCommit);
        var raced = await Task.WhenAll(left.GetOrCreateAsync(default).AsTask(), right.GetOrCreateAsync(default).AsTask());
        try { var a = raced[0].CopyBytes(); var b = raced[1].CopyBytes(); try { Assert.Equal(a, b); } finally { CryptographicOperations.ZeroMemory(a); CryptographicOperations.ZeroMemory(b); } }
        finally { foreach (var lease in raced) lease.Dispose(); }
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));
    }

    [Fact]
    public async Task Cancellation_and_failure_before_commit_clean_up_but_cancellation_after_move_is_success()
    {
        using var before = new CancellationTokenSource(); var cancelledPath = SaltPath("cancelled.salt");
        var cancelling = new LocalUsageIdentitySaltStore(cancelledPath, new SyntheticProtector(41), token => { before.Cancel(); token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelling.GetOrCreateAsync(before.Token).AsTask());
        Assert.False(File.Exists(cancelledPath)); Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));

        var failedPath = SaltPath("failed.salt"); var failure = new LocalUsageIdentitySaltStore(failedPath, new ThrowingProtector());
        Assert.Equal(LocalUsageIdentitySaltError.ProtectionFailed, (await Assert.ThrowsAsync<LocalUsageIdentitySaltException>(() => failure.GetOrCreateAsync(default).AsTask())).Code);
        Assert.False(File.Exists(failedPath)); Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp"));

        using var after = new CancellationTokenSource(); var committedPath = SaltPath("committed.salt");
        using var committed = await new LocalUsageIdentitySaltStore(committedPath, new SyntheticProtector(42), afterCommit: after.Cancel).GetOrCreateAsync(after.Token);
        Assert.True(after.IsCancellationRequested); Assert.True(File.Exists(committedPath)); var copy = committed.CopyBytes(); try { Assert.Equal(32, copy.Length); } finally { CryptographicOperations.ZeroMemory(copy); }
    }

    [Fact]
    public async Task Reparse_target_is_rejected_without_mutating_its_target()
    {
        Directory.CreateDirectory(_root); var target = SaltPath("target.salt"); await File.WriteAllTextAsync(target, "keep"); var link = SaltPath("link.salt");
        try { File.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { throw Xunit.Sdk.SkipException.ForSkip($"Symbolic-link test unsupported: {error.GetType().Name}"); }
        Assert.Equal(LocalUsageIdentitySaltError.UnsafePath, (await Assert.ThrowsAsync<LocalUsageIdentitySaltException>(() => new LocalUsageIdentitySaltStore(link, new SyntheticProtector(51)).GetOrCreateAsync(default).AsTask())).Code);
        Assert.Equal("keep", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Windows_DPAPI_roundtrips_without_plaintext_at_rest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var path = SaltPath("dpapi.salt"); using var first = await new LocalUsageIdentitySaltStore(path).GetOrCreateAsync(default); var plaintext = first.CopyBytes();
        try { Assert.Equal(-1, (await File.ReadAllBytesAsync(path)).AsSpan().IndexOf(plaintext)); using var reopened = await new LocalUsageIdentitySaltStore(path).GetOrCreateAsync(default); var copy = reopened.CopyBytes(); try { Assert.Equal(plaintext, copy); } finally { CryptographicOperations.ZeroMemory(copy); } }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static UsageSourceIdentity Identity(LocalUsageIdentitySaltLease lease)
    {
        var facts = lease.CreateDiscoveryFacts(null, null); var salt = facts.IdentitySalt();
        try { return LocalUsageSourceDiscovery.CreateIdentity(UsageTool.OpenCode, @"C:\synthetic\opencode.db", salt); }
        finally { CryptographicOperations.ZeroMemory(salt); }
    }

    private static byte[] Mutate(byte[] valid, string mutation, SyntheticProtector protector)
    {
        var result = valid.ToArray();
        if (mutation == "magic") result[0] ^= 1; else if (mutation == "version") result[8] = 2; else if (mutation == "truncated") Array.Resize(ref result, result.Length - 1); else if (mutation == "excess") result = [.. result, 0];
        else if (mutation is "oversized" or "zero") { Array.Resize(ref result, 13); BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(9), mutation == "zero" ? 0 : 4097); }
        else if (mutation == "corrupt") result[^1] ^= 1; else { var payload = protector.Protect(new byte[31]); result = [.. valid.AsSpan(0, 9), .. BitConverter.GetBytes(payload.Length), .. payload]; }
        return result;
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class SyntheticProtector(byte userKey) : ILocalUsageIdentityProtector
    {
        public byte[] Protect(byte[] plaintext) { var cipher = plaintext.Select(value => (byte)(value ^ userKey)).ToArray(); return [.. HMACSHA256.HashData(new byte[] { userKey }, plaintext), .. cipher]; }
        public byte[] Unprotect(byte[] payload)
        {
            if (payload.Length < 33) throw new CryptographicException(); var plaintext = payload[32..].Select(value => (byte)(value ^ userKey)).ToArray(); var tag = HMACSHA256.HashData(new byte[] { userKey }, plaintext);
            if (!CryptographicOperations.FixedTimeEquals(tag, payload.AsSpan(0, 32))) { CryptographicOperations.ZeroMemory(plaintext); throw new CryptographicException(); } return plaintext;
        }
    }

    private sealed class ThrowingProtector : ILocalUsageIdentityProtector
    { public byte[] Protect(byte[] plaintext) => throw new CryptographicException(); public byte[] Unprotect(byte[] payload) => throw new CryptographicException(); }
}
