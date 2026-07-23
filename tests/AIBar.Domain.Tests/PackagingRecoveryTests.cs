using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AIBar.Domain.Tests;

public sealed class PackagingRecoveryTests
{
    private static readonly object PublishLock = new();
    [Fact]
    public void Red_existing_sentinel_tree_is_rejected_and_unchanged()
    {
        using var parent = TempParent(); var leaf = Path.Combine(parent.Path, "existing");
        Directory.CreateDirectory(leaf); var sentinel = Path.Combine(leaf, "sentinel.txt"); File.WriteAllText(sentinel, "keep");
        var result = Run(leaf);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("keep", File.ReadAllText(sentinel));
    }

    [Theory]
    [InlineData("file")]
    [InlineData("empty-directory")]
    [InlineData("non-empty-directory")]
    public void Existing_leaf_forms_are_rejected_without_mutation(string form)
    {
        using var parent = TempParent(); var leaf = Path.Combine(parent.Path, form);
        if (form == "file") File.WriteAllText(leaf, "keep"); else { Directory.CreateDirectory(leaf); if (form == "non-empty-directory") File.WriteAllText(Path.Combine(leaf, "sentinel"), "keep"); }
        var result = Run(leaf);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(leaf) || Directory.Exists(leaf));
    }

    [Fact]
    public void Rejects_root_repository_overlap_and_missing_parent_before_publish()
    {
        using var parent = TempParent(); var missing = Path.Combine(parent.Path, "missing", "leaf");
        var root = Path.GetPathRoot(Path.GetFullPath(parent.Path))!;
        Assert.All(new[] { root, RepositoryRoot(), missing }, path => Assert.NotEqual(0, Run(path).ExitCode));
        var invalidEpoch = Path.Combine(parent.Path, "invalid-epoch"); Assert.NotEqual(0, Run(invalidEpoch, epoch: "nope").ExitCode); Assert.False(Directory.Exists(invalidEpoch));
        Assert.False(Directory.Exists(Path.Combine(parent.Path, "missing")));
    }

    [Fact]
    public void Rejects_observed_reparse_ancestor_or_reports_unsupported_environment()
    {
        using var parent = TempParent(); var target = Path.Combine(parent.Path, "target"); var link = Path.Combine(parent.Path, "link"); Directory.CreateDirectory(target);
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { throw Xunit.Sdk.SkipException.ForSkip($"Reparse admission test unsupported: {error.GetType().Name}"); }
        Assert.NotEqual(0, Run(Path.Combine(link, "leaf")).ExitCode);
    }

    [Fact]
    public void Rejections_never_launch_publish_command_and_leaf_reparse_is_rejected()
    {
        using var parent = TempParent(); var marker = Path.Combine(parent.Path, "called"); var command = FakeCommand(parent.Path, marker); var existing = Path.Combine(parent.Path, "file"); File.WriteAllText(existing, "keep");
        foreach (var output in new[] { existing, Path.Combine(existing, "leaf"), Path.Combine(parent.Path, "missing", "leaf"), RepositoryRoot() }) Assert.NotEqual(0, Run(output, command).ExitCode);
        Assert.False(File.Exists(marker)); var target = Path.Combine(parent.Path, "target"); var link = Path.Combine(parent.Path, "leaf-link"); Directory.CreateDirectory(target);
        try { Directory.CreateSymbolicLink(link, target); } catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { throw Xunit.Sdk.SkipException.ForSkip($"Leaf reparse test unsupported: {error.GetType().Name}"); }
        Assert.NotEqual(0, Run(link, command).ExitCode); Assert.False(File.Exists(marker)); Assert.True(Directory.Exists(target));
    }

    [Fact]
    public void Fresh_runs_have_recursive_inventory_zip_and_stable_identity()
    {
        using var one = TempParent("á one"); using var two = TempParent("two"); var first = Path.Combine(one.Path, "one"); var second = Path.Combine(two.Path, "two");
        Assert.Equal(0, Run(first).ExitCode); Assert.Equal(0, Run(second).ExitCode);
        var a = Contract(first); var b = Contract(second);
        Assert.Equal(a.Files, b.Files); Assert.Equal(a.ZipHash, b.ZipHash); Assert.Equal(a.Inventory, b.Inventory); Assert.Equal(a.Manifest, b.Manifest);
    }

    [Fact]
    public void Collision_failure_and_post_creation_failure_leave_outputs_and_retry_uses_new_leaf()
    {
        using var parent = TempParent(); var collision = Path.Combine(parent.Path, "collision"); Directory.CreateDirectory(collision);
        Assert.NotEqual(0, Run(collision).ExitCode);
        var failed = Path.Combine(parent.Path, "failed"); var failedRun = Run(failed, "not-a-real-dotnet");
        Assert.NotEqual(0, failedRun.ExitCode); Assert.Contains("PUBLISH_INCOMPLETE_OUTPUT", failedRun.Output); Assert.True(Directory.Exists(failed));
        var retry = Path.Combine(parent.Path, "retry"); Assert.Equal(0, Run(retry).ExitCode); Assert.True(Directory.Exists(failed));
    }

    private static Snapshot Contract(string root)
    {
        var inventory = File.ReadAllBytes(Path.Combine(root, "recovery-inventory.json")); var manifest = File.ReadAllBytes(Path.Combine(root, "artifact-manifest.json"));
        using var json = JsonDocument.Parse(inventory); using var zip = ZipFile.OpenRead(Path.Combine(root, "AIBar-win-x64-recovery.zip"));
        var files = json.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("path").GetString()!, x => (x.GetProperty("length").GetInt64(), x.GetProperty("sha256").GetString()!), StringComparer.Ordinal);
        Assert.True(files.Count >= 400); Assert.Equal(files.Keys.OrderBy(x => x, StringComparer.Ordinal), zip.Entries.Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal));
        foreach (var entry in zip.Entries) { Assert.DoesNotContain('\\', entry.FullName); Assert.DoesNotContain("..", entry.FullName); using var stream = entry.Open(); Assert.Equal(files[entry.FullName], (entry.Length, Hash(stream))); }
        return new(files, HashFile(Path.Combine(root, "AIBar-win-x64-recovery.zip")), inventory, manifest);
    }

    private static RunResult Run(string output, string? command = null, string? epoch = null)
    {
        var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1")); psi.ArgumentList.Add("-OutputDirectory"); psi.ArgumentList.Add(output);
        if (command is not null) { psi.ArgumentList.Add("-PublishCommand"); psi.ArgumentList.Add(command); }
        if (epoch is not null) { psi.ArgumentList.Add("-SourceDateEpoch"); psi.ArgumentList.Add(epoch); }
        lock (PublishLock) { using var process = Process.Start(psi)!; var outputText = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return new(process.ExitCode, outputText); }
    }
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); ; d = d.Parent!) if (File.Exists(Path.Combine(d.FullName, "AIBar.sln"))) return d.FullName; }
    private static string FakeCommand(string parent, string marker) { var path = Path.Combine(parent, "marker.cmd"); File.WriteAllText(path, $"@echo invoked>\"{marker}\"\r\n@exit /b 0"); return path; }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static TemporaryParent TempParent(string suffix = "") => new(Path.Combine(Path.GetTempPath(), $"aibar-8c1-{Guid.NewGuid():N}{suffix}"));
    private sealed class TemporaryParent(string path) : IDisposable { public string Path { get; } = Directory.CreateDirectory(path).FullName; public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); } }
    private sealed record RunResult(int ExitCode, string Output);
    private sealed record Snapshot(Dictionary<string, (long, string)> Files, string ZipHash, byte[] Inventory, byte[] Manifest);
}
