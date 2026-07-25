using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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

    [Fact]
    public void Isolated_mode_keeps_repository_intermediates_unchanged_and_is_deterministic()
    {
        var repositoryState = TreeHash(Path.Combine(RepositoryRoot(), "src", "AIBar.Desktop", "obj"), Path.Combine(RepositoryRoot(), "src", "AIBar.Desktop", "bin"));
        using var one = Isolation("café one"); using var two = Isolation("two");
        var firstRun = Run(one.Output, isolation: one); var secondRun = Run(two.Output, isolation: two); Assert.True(firstRun.ExitCode == 0, firstRun.Output); Assert.True(secondRun.ExitCode == 0, secondRun.Output);
        Assert.Equal(repositoryState, TreeHash(Path.Combine(RepositoryRoot(), "src", "AIBar.Desktop", "obj"), Path.Combine(RepositoryRoot(), "src", "AIBar.Desktop", "bin")));
        var first = Contract(one.Output); var second = Contract(two.Output);
        Assert.Equal(first.Files, second.Files); Assert.Equal(first.ZipHash, second.ZipHash); Assert.Equal(first.Inventory, second.Inventory); Assert.Equal(first.Manifest, second.Manifest);
        Assert.True(File.Exists(Path.Combine(one.Restore, "AIBar.Desktop", "project.assets.json")));
    }

    [Fact]
    public void Isolated_mode_rejects_marker_mismatch_and_stale_children_before_publish()
    {
        using var markerRoot = Isolation("marker"); var marker = Path.Combine(markerRoot.Parent, "called"); var command = FakeCommand(markerRoot.Parent, marker);
        File.WriteAllText(Path.Combine(markerRoot.Parent, ".aibar-isolation-marker"), "wrong"); Assert.NotEqual(0, Run(markerRoot.Output, command, isolation: markerRoot).ExitCode); Assert.False(File.Exists(marker)); Assert.False(Directory.Exists(markerRoot.Output));
        using var staleRoot = Isolation("stale"); File.WriteAllText(staleRoot.Intermediate, "keep"); Assert.NotEqual(0, Run(staleRoot.Output, command, isolation: staleRoot).ExitCode); Assert.Equal("keep", File.ReadAllText(staleRoot.Intermediate)); Assert.False(File.Exists(marker));
    }

    [Fact]
    public void Red_isolated_mode_rejects_a_relative_parent_before_command_launch()
    {
        using var root = Isolation("relative"); var marker = Path.Combine(root.Parent, "called"); var command = FakeCommand(root.Parent, marker);
        root.RelativeParent = true;
        var result = Run(root.Output, command, isolation: root, workingDirectory: root.Parent);
        Assert.NotEqual(0, result.ExitCode); Assert.False(File.Exists(marker)); Assert.False(Directory.Exists(root.Output));
    }

    [Fact]
    public void Red_direct_invocation_completes_while_known_owned_grandchild_remains_alive()
    {
        using var harness = DescendantHarness.Create();
        using var child = harness.StartChild();

        Assert.True(child.WaitForExit(10_000), "The direct child did not complete within the bounded wait.");
        Assert.Equal(0, child.ExitCode);
        Assert.True(harness.Ready.WaitOne(10_000), "The known grandchild did not signal readiness.");

        harness.ValidateRecordedChild(child);
        using var grandchild = harness.OpenRecordedGrandchild();
        Assert.False(grandchild.HasExited);
        Assert.Equal(harness.Root, File.ReadAllText(harness.ArgumentRecord));

        // This is the retained RED: direct completion alone has no descendant-exit/tree-quiescence evidence.
        Assert.True(child.HasExited && !grandchild.HasExited);

        harness.Release.Set();
        Assert.True(grandchild.WaitForExit(10_000), "The known grandchild did not exit after release.");
        Assert.Equal(["child-stdout", "grandchild-stdout"], child.StandardOutput.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Order());
        Assert.Equal(["child-stderr", "grandchild-stderr"], child.StandardError.ReadToEnd().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Order());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(30, 5000)]
    public async Task Owned_lifecycle_terminates_known_descendant_on_timeout_or_cancellation(int timeoutSeconds, int cancelAfterMilliseconds)
    {
        using var harness = DescendantHarness.Create(); var output = Path.Combine(harness.Root, "recovery");
        var run = Task.Run(() => Run(output, harness.Executable, timeoutSeconds: timeoutSeconds, cancelAfterMilliseconds: cancelAfterMilliseconds, environment: harness.PublisherEnvironment()));
        Assert.True(harness.Ready.WaitOne(10_000), "The known grandchild did not signal readiness.");
        using var child = harness.OpenRecordedChild();
        using var grandchild = harness.OpenRecordedGrandchild();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(child.HasExited, "The known child was still alive when publisher termination returned.");
        Assert.True(grandchild.HasExited, "The known grandchild was still alive when publisher termination returned.");
        Assert.True(Directory.Exists(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owned_lifecycle_rejects_early_success_when_known_descendant_is_live_or_unprovable(bool missingIdentity)
    {
        using var harness = DescendantHarness.Create();
        var environment = new Dictionary<string, string>(harness.PublisherEnvironment()) { ["AIBAR_PUBLISH_MODE"] = "early" };
        var result = Run(Path.Combine(harness.Root, "recovery"), harness.Executable, timeoutSeconds: 2, environment: environment,
            knownDescendantIdentityPath: missingIdentity ? Path.Combine(harness.Root, "missing.json") : harness.IdentityRecord);
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(Directory.Exists(Path.Combine(harness.Root, "recovery")));
    }

    [Fact]
    public void Owned_lifecycle_concurrently_drains_saturated_stdout_and_stderr_within_bound()
    {
        using var harness = DescendantHarness.Create(); var output = Path.Combine(harness.Root, "recovery");
        var result = Run(output, harness.Executable, timeoutSeconds: 2, boundedWaitSeconds: 7, environment: new Dictionary<string, string>(harness.PublisherEnvironment()) { ["AIBAR_PUBLISH_MODE"] = "saturated" });
        Assert.Equal(0, result.ExitCode); Assert.True(Directory.Exists(output));
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

    private static RunResult Run(string output, string? command = null, string? epoch = null, IsolationRoot? isolation = null, string? workingDirectory = null, int? timeoutSeconds = null, int? cancelAfterMilliseconds = null, int? boundedWaitSeconds = null, IReadOnlyDictionary<string, string>? environment = null, string? knownDescendantIdentityPath = null)
    {
        var psi = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-File"); psi.ArgumentList.Add(Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1")); psi.ArgumentList.Add("-OutputDirectory"); psi.ArgumentList.Add(output);
        if (command is not null) { psi.ArgumentList.Add("-PublishCommand"); psi.ArgumentList.Add(command); }
        if (epoch is not null) { psi.ArgumentList.Add("-SourceDateEpoch"); psi.ArgumentList.Add(epoch); }
        if (isolation is not null) foreach (var argument in isolation.Arguments()) { psi.ArgumentList.Add(argument.Name); psi.ArgumentList.Add(argument.Value); }
        if (timeoutSeconds is not null) { psi.ArgumentList.Add("-ProcessTimeoutSeconds"); psi.ArgumentList.Add(timeoutSeconds.Value.ToString()); }
        if (cancelAfterMilliseconds is not null) { psi.ArgumentList.Add("-CancelAfterMilliseconds"); psi.ArgumentList.Add(cancelAfterMilliseconds.Value.ToString()); }
        if (knownDescendantIdentityPath is not null) { psi.ArgumentList.Add("-KnownDescendantIdentityPath"); psi.ArgumentList.Add(knownDescendantIdentityPath); }
        if (environment is not null) foreach (var pair in environment) psi.Environment[pair.Key] = pair.Value;
        lock (PublishLock) { using var process = Process.Start(psi)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); var work = Task.WhenAll(process.WaitForExitAsync(), stdout, stderr); if (boundedWaitSeconds is not null) Assert.True(work.Wait(TimeSpan.FromSeconds(boundedWaitSeconds.Value)), "PowerShell process exceeded its bounded wait."); else work.Wait(); return new(process.ExitCode, stdout.Result + stderr.Result); }
    }
    private static string RepositoryRoot() { for (var d = new DirectoryInfo(AppContext.BaseDirectory); ; d = d.Parent!) if (File.Exists(Path.Combine(d.FullName, "AIBar.sln"))) return d.FullName; }
    private static string FakeCommand(string parent, string marker) { var path = Path.Combine(parent, "marker.cmd"); File.WriteAllText(path, $"@echo invoked>\"{marker}\"\r\n@exit /b 0"); return path; }
    private static string Hash(Stream stream) => Convert.ToHexString(SHA256.HashData(stream));
    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string TreeHash(params string[] roots)
    {
        var files = roots.SelectMany(root => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => $"{Path.GetRelativePath(root, path)}:{HashFile(path)}") : ["missing"]);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", files))));
    }
    private static TemporaryParent TempParent(string suffix = "") => new(Path.Combine(Path.GetTempPath(), $"aibar-8c1-{Guid.NewGuid():N}{suffix}"));
    private static IsolationRoot Isolation(string suffix) => new(Path.Combine(Path.GetTempPath(), $"aibar-8c1-isolated-{Guid.NewGuid():N} {suffix}"));
    private sealed class TemporaryParent(string path) : IDisposable { public string Path { get; } = Directory.CreateDirectory(path).FullName; public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); } }
    private sealed record RunResult(int ExitCode, string Output);
    private sealed record Snapshot(Dictionary<string, (long, string)> Files, string ZipHash, byte[] Inventory, byte[] Manifest);
    private sealed class DescendantHarness : IDisposable
    {
        private const int BoundedWaitMilliseconds = 10_000;
        public string Root { get; }
        public string Executable { get; }
        public string IdentityRecord { get; }
        public string ArgumentRecord { get; }
        public string Marker { get; }
        public string Nonce { get; }
        public string ReadyName { get; }
        public string ReleaseName { get; }
        public EventWaitHandle Ready { get; }
        public EventWaitHandle Release { get; }

        private DescendantHarness(string root, string executable, string identityRecord, string argumentRecord, string marker, string nonce, string readyName, string releaseName, EventWaitHandle ready, EventWaitHandle release)
            => (Root, Executable, IdentityRecord, ArgumentRecord, Marker, Nonce, ReadyName, ReleaseName, Ready, Release) = (root, executable, identityRecord, argumentRecord, marker, nonce, readyName, releaseName, ready, release);

        public static DescendantHarness Create()
        {
            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"aibar descendant {Guid.NewGuid():N} café")).FullName;
            var project = Path.Combine(root, "Harness.csproj");
            var program = Path.Combine(root, "Program.cs");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><UseAppHost>true</UseAppHost><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>", Encoding.UTF8);
            File.WriteAllText(program, """
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
if (args[0] == "child")
{
    File.WriteAllText(args[5], JsonSerializer.Serialize(new { child = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("grandchild");
    start.ArgumentList.Add(args[2]); start.ArgumentList.Add(args[3]); start.ArgumentList.Add(args[4]); start.ArgumentList.Add(args[5]); start.ArgumentList.Add(args[6]); start.ArgumentList.Add(args[7]); start.ArgumentList.Add(args[8]);
    using var grandchild = Process.Start(start)!;
    using var childReady = EventWaitHandle.OpenExisting(args[2]);
    Console.Out.WriteLine("child-stdout"); Console.Error.WriteLine("child-stderr");
    Environment.Exit(childReady.WaitOne(10000) ? 0 : 3);
}
if (args[0] == "publish")
{
    var mode = Environment.GetEnvironmentVariable("AIBAR_PUBLISH_MODE");
    if (mode == "saturated") { File.WriteAllText(Environment.GetEnvironmentVariable("AIBAR_MARKER")!, $"owned-marker:{Environment.GetEnvironmentVariable("AIBAR_NONCE")}"); Console.Out.Write(new string('o', 1_048_576)); Console.Error.Write(new string('e', 1_048_576)); Environment.Exit(0); }
    var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("AIBAR_HARNESS_EXE")!) { UseShellExecute = false };
    foreach (var value in new[] { "publisher-child", Environment.GetEnvironmentVariable("AIBAR_HARNESS_EXE")!, Environment.GetEnvironmentVariable("AIBAR_READY")!, Environment.GetEnvironmentVariable("AIBAR_RELEASE")!, Environment.GetEnvironmentVariable("AIBAR_MARKER")!, Environment.GetEnvironmentVariable("AIBAR_IDENTITY")!, Environment.GetEnvironmentVariable("AIBAR_ARGUMENTS")!, Environment.GetEnvironmentVariable("AIBAR_ROOT")!, Environment.GetEnvironmentVariable("AIBAR_NONCE")! }) start.ArgumentList.Add(value);
    using var child = Process.Start(start)!;
    if (Environment.GetEnvironmentVariable("AIBAR_PUBLISH_MODE") == "early")
    {
        using var publisherReady = EventWaitHandle.OpenExisting(Environment.GetEnvironmentVariable("AIBAR_READY")!);
        Environment.Exit(publisherReady.WaitOne(10000) ? 0 : 5);
    }
    child.WaitForExit();
    Environment.Exit(child.ExitCode);
}
if (args[0] == "publisher-child")
{
    File.WriteAllText(args[5], JsonSerializer.Serialize(new { child = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
    var start = new ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("grandchild");
    start.ArgumentList.Add(args[2]); start.ArgumentList.Add(args[3]); start.ArgumentList.Add(args[4]); start.ArgumentList.Add(args[5]); start.ArgumentList.Add(args[6]); start.ArgumentList.Add(args[7]); start.ArgumentList.Add(args[8]);
    using var grandchild = Process.Start(start)!;
    using var publisherRelease = EventWaitHandle.OpenExisting(args[3]);
    Environment.Exit(publisherRelease.WaitOne(60000) ? 0 : 4);
}
using var ready = EventWaitHandle.OpenExisting(args[1]);
using var release = EventWaitHandle.OpenExisting(args[2]);
var identity = JsonDocument.Parse(File.ReadAllText(args[4]));
File.WriteAllText(args[4], JsonSerializer.Serialize(new { child = identity.RootElement.GetProperty("child"), grandchild = new { pid = Environment.ProcessId, startTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks } }));
File.WriteAllText(args[5], args[6]);
File.WriteAllText(args[3], $"owned-marker:{args[7]}");
Console.Out.WriteLine("grandchild-stdout"); Console.Error.WriteLine("grandchild-stderr");
ready.Set();
Environment.Exit(release.WaitOne(10000) ? 0 : 4);
""", Encoding.UTF8);
            var build = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, WorkingDirectory = root };
            build.ArgumentList.Add("build"); build.ArgumentList.Add(project); build.ArgumentList.Add("--nologo"); build.ArgumentList.Add("-v:q");
            var nonce = Guid.NewGuid().ToString("N");
            using var compiler = Process.Start(build)!;
            var stdout = compiler.StandardOutput.ReadToEndAsync(); var stderr = compiler.StandardError.ReadToEndAsync();
            var compilerWork = Task.WhenAll(compiler.WaitForExitAsync(), stdout, stderr);
            if (!compilerWork.Wait(BoundedWaitMilliseconds))
            {
                if (!compiler.HasExited) compiler.Kill(entireProcessTree: true);
                Assert.True(compilerWork.Wait(BoundedWaitMilliseconds), $"Compiler timeout: {Bounded(stdout, stderr)}");
            }
            var output = Bounded(stdout, stderr);
            Assert.True(compiler.ExitCode == 0, output);
            var readyName = $"Local\\aibar-ready-{nonce}"; var releaseName = $"Local\\aibar-release-{nonce}";
            var ready = new EventWaitHandle(false, EventResetMode.ManualReset, readyName);
            var release = new EventWaitHandle(false, EventResetMode.ManualReset, releaseName);
            return new(root, Path.Combine(root, "bin", "Debug", "net8.0", "Harness.exe"), Path.Combine(root, "identity.json"), Path.Combine(root, "arguments.txt"), Path.Combine(root, ".owned-marker"), nonce, readyName, releaseName, ready, release);
        }

        public Process StartChild()
        {
            var start = new ProcessStartInfo(Executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("child"); start.ArgumentList.Add(Executable); start.ArgumentList.Add(ReadyName); start.ArgumentList.Add(ReleaseName);
            start.ArgumentList.Add(Marker); start.ArgumentList.Add(IdentityRecord); start.ArgumentList.Add(ArgumentRecord); start.ArgumentList.Add(Root); start.ArgumentList.Add(Nonce);
            return Process.Start(start)!;
        }

        public IReadOnlyDictionary<string, string> PublisherEnvironment() => new Dictionary<string, string>
        {
            ["AIBAR_HARNESS_EXE"] = Executable, ["AIBAR_READY"] = ReadyName, ["AIBAR_RELEASE"] = ReleaseName,
            ["AIBAR_MARKER"] = Marker, ["AIBAR_IDENTITY"] = IdentityRecord, ["AIBAR_ARGUMENTS"] = ArgumentRecord,
            ["AIBAR_ROOT"] = Root, ["AIBAR_NONCE"] = Nonce
        };

        public void ValidateRecordedChild(Process child) => ValidateIdentity("child", child);

        public Process OpenRecordedChild() => OpenRecorded("child");

        public Process OpenRecordedGrandchild() => OpenRecorded("grandchild");

        private Process OpenRecorded(string name)
        {
            using var identity = JsonDocument.Parse(File.ReadAllText(IdentityRecord));
            var record = identity.RootElement.GetProperty(name);
            var process = Process.GetProcessById(record.GetProperty("pid").GetInt32());
            Assert.Equal(record.GetProperty("startTicks").GetInt64(), process.StartTime.ToUniversalTime().Ticks);
            return process;
        }

        public void Dispose()
        {
            try
            {
                Release.Set();
                if (File.Exists(IdentityRecord))
                    try { using var grandchild = OpenRecordedGrandchild(); if (!grandchild.WaitForExit(BoundedWaitMilliseconds)) { grandchild.Kill(true); Assert.True(grandchild.WaitForExit(BoundedWaitMilliseconds)); } }
                    catch (ArgumentException) { }
                ValidateOwnedCleanupAdmission();
                Directory.Delete(Root, true);
            }
            finally { Ready.Dispose(); Release.Dispose(); }
        }

        private void ValidateIdentity(string name, Process process)
        {
            using var identity = JsonDocument.Parse(File.ReadAllText(IdentityRecord));
            var record = identity.RootElement.GetProperty(name);
            Assert.Equal(record.GetProperty("pid").GetInt32(), process.Id);
            Assert.Equal(record.GetProperty("startTicks").GetInt64(), process.StartTime.ToUniversalTime().Ticks);
        }

        private void ValidateOwnedCleanupAdmission()
        {
            var canonicalRoot = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Assert.False(new DirectoryInfo(canonicalRoot).Attributes.HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal($"owned-marker:{Nonce}", File.ReadAllText(Marker));
            foreach (var path in new[] { Marker, IdentityRecord, ArgumentRecord, Executable })
                Assert.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
            var pending = new Stack<string>(); pending.Push(canonicalRoot);
            while (pending.Count > 0)
                foreach (var entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    Assert.False(File.GetAttributes(entry).HasFlag(FileAttributes.ReparsePoint), $"Owned cleanup rejected reparse point: {entry}");
                    if (Directory.Exists(entry)) pending.Push(entry);
                }
        }

        private static string Bounded(Task<string> stdout, Task<string> stderr)
        {
            var output = (stdout.IsCompletedSuccessfully ? stdout.Result : string.Empty) + (stderr.IsCompletedSuccessfully ? stderr.Result : string.Empty);
            return output.Length <= 4096 ? output : output[..4096];
        }
    }
    private sealed class IsolationRoot : IDisposable
    {
        public string Parent { get; } public string Marker { get; } = Guid.NewGuid().ToString("N"); public bool RelativeParent { get; set; } public string Intermediate { get; } public string Build { get; } public string Restore { get; } public string Packages { get; } public string Output { get; }
        public IsolationRoot(string parent) { Parent = Directory.CreateDirectory(parent).FullName; File.WriteAllText(Path.Combine(Parent, ".aibar-isolation-marker"), Marker); Intermediate = Path.Combine(Parent, "intermediate"); Build = Path.Combine(Parent, "build"); Restore = Path.Combine(Parent, "restore"); Packages = Path.Combine(Parent, "packages"); Output = Path.Combine(Parent, "recovery"); }
        public IEnumerable<(string Name, string Value)> Arguments() { yield return ("-IsolationParent", RelativeParent ? "." : Parent); yield return ("-IsolationMarker", Marker); yield return ("-IntermediateDirectory", Intermediate); yield return ("-BuildOutputDirectory", Build); yield return ("-RestoreMetadataDirectory", Restore); yield return ("-PackageCacheDirectory", Packages); }
        public void Dispose() { if (Directory.Exists(Parent)) Directory.Delete(Parent, true); }
    }
}
