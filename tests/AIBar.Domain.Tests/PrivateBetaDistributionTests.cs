using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AIBar.Domain.Tests;

public sealed class PrivateBetaDistributionTests
{
    [Fact]
    public void Private_beta_production_default_is_bound_to_the_final_beta4_parent()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1"));

        Assert.Contains("if ([string]::IsNullOrWhiteSpace($BetaParentCommit)) { \"816c28b6747a6e9aa62c5c23c216cd4feb7a55fd\" }", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_beta_rejects_dirty_or_wrong_parent_committed_sources()
    {
        using var fixture = new SyntheticRepository();
        var dirtyOutput = fixture.Output("dirty");
        File.AppendAllText(Path.Combine(fixture.Root, "README.md"), "dirty");
        Assert.Contains("BETA_SOURCE_UNCOMMITTED", fixture.Run(dirtyOutput).Output);
        Assert.False(Directory.Exists(dirtyOutput));

        File.WriteAllText(Path.Combine(fixture.Root, "README.md"), "staged");
        fixture.Git("add", "README.md");
        var staged = fixture.Run(fixture.Output("staged"));
        Assert.NotEqual(0, staged.ExitCode);
        Assert.Contains("BETA_SOURCE_UNCOMMITTED", staged.Output);
        fixture.Git("reset", "--hard", "HEAD");

        fixture.Git("read-tree", "--empty");
        var emptyIndex = fixture.Run(fixture.Output("empty-index"));
        Assert.NotEqual(0, emptyIndex.ExitCode);
        Assert.Contains("BETA_SOURCE_UNCOMMITTED", emptyIndex.Output);
        fixture.Git("reset", "--hard", "HEAD");

        var mismatch = fixture.Run(fixture.Output("mismatch"), parent: "0000000000000000000000000000000000000000");
        Assert.NotEqual(0, mismatch.ExitCode);
        Assert.Contains("BETA_PARENT_MISMATCH", mismatch.Output);

        var nested = Path.Combine(fixture.Root, "nested", "scripts"); Directory.CreateDirectory(nested);
        var nestedScript = Path.Combine(nested, "Publish-Deterministic.ps1"); File.Copy(Path.Combine(fixture.Root, "scripts", "Publish-Deterministic.ps1"), nestedScript);
        var repositoryMismatch = fixture.Run(fixture.Output("repository-mismatch"), script: nestedScript);
        Assert.NotEqual(0, repositoryMismatch.ExitCode);
        Assert.Contains("BETA_REPOSITORY_MISMATCH", repositoryMismatch.Output);
    }

    [Fact]
    public void Private_beta_binds_a_clean_committed_source_and_smoke_cleans_up()
    {
        using var fixture = new SyntheticRepository();
        var output = fixture.Output("good");
        var result = fixture.Run(output, publisher: fixture.Publisher("stay"));

        Assert.Equal(0, result.ExitCode);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "private-beta-manifest.json")));
        var baseline = manifest.RootElement.GetProperty("baselineCommit").GetString();
        var parent = manifest.RootElement.GetProperty("parentCommit").GetString();
        var source = manifest.RootElement.GetProperty("sourceCommit").GetString();
        var version = manifest.RootElement.GetProperty("version").GetString();
        Assert.Equal(fixture.Baseline, baseline);
        Assert.Equal(fixture.Parent, parent);
        Assert.Equal(fixture.Source, source);
        Assert.Equal("0.1.0-beta.4", version);
        Assert.Equal("win-x64", manifest.RootElement.GetProperty("target").GetString());
        Assert.True(manifest.RootElement.GetProperty("selfContained").GetBoolean());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(output, "AIBar-win-x64-private-beta.zip")))).ToLowerInvariant(), manifest.RootElement.GetProperty("zipSha256").GetString());
        Assert.Equal(manifest.RootElement.GetProperty("inventory").EnumerateArray().Select(item => item.GetProperty("path").GetString()).OrderBy(path => path, StringComparer.Ordinal), manifest.RootElement.GetProperty("inventory").EnumerateArray().Select(item => item.GetProperty("path").GetString()));
        Assert.False(Directory.Exists(Path.Combine(output, "smoke")));
        using var archive = ZipFile.OpenRead(Path.Combine(output, "AIBar-win-x64-private-beta.zip"));
        Assert.Equal(archive.Entries.Select(entry => entry.FullName).OrderBy(path => path, StringComparer.Ordinal), archive.Entries.Select(entry => entry.FullName));
        Assert.Contains(archive.Entries, entry => entry.FullName == "PRIVATE-BETA.txt");
        foreach (var relative in new[] { "yasb/read-aibar-quota.ps1", "yasb/remove-aibar-quota.ps1", "yasb/show-aibar.cmd", "yasb/custom-widget.example.yaml", "yasb/custom-widget.example.css", "yasb/README.txt" })
        {
            var entry = archive.Entries.Single(item => item.FullName == relative);
            using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes);
            Assert.Equal(File.ReadAllBytes(Path.Combine(RepositoryRoot(), relative.Replace('/', Path.DirectorySeparatorChar))), bytes.ToArray());
        }
        var instructions = File.ReadAllText(Path.Combine(output, "private-beta-instructions.txt"));
        Assert.Contains($"AIBar private beta {version}", instructions, StringComparison.Ordinal);
        Assert.Contains($"Baseline ancestor: {baseline}.", instructions, StringComparison.Ordinal);
        Assert.Contains($"Parent commit: {parent}.", instructions, StringComparison.Ordinal);
        Assert.Contains($"Source commit: {source}.", instructions, StringComparison.Ordinal);
        var repeat = fixture.Output("repeat");
        Assert.Equal(0, fixture.Run(repeat, publisher: fixture.Publisher("stay")).ExitCode);
        Assert.Equal(File.ReadAllBytes(Path.Combine(output, "AIBar-win-x64-private-beta.zip")), File.ReadAllBytes(Path.Combine(repeat, "AIBar-win-x64-private-beta.zip")));
    }

    [Fact]
    public void Distribution_guidance_is_path_neutral_and_requires_cleanup_before_asset_removal()
    {
        var documentation = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "private-beta.md"));
        var readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "yasb", "README.txt"));
        foreach (var text in new[] { documentation, readme })
        {
            Assert.Contains("<AIBarRoot>", text, StringComparison.Ordinal);
            Assert.Contains("unsigned", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Windows x64", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Stop AIBar", text, StringComparison.Ordinal);
            Assert.Contains("exits 0", text, StringComparison.Ordinal);
            Assert.Contains("no installer", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("unsupported private integration", documentation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("absent or is schema-v1 `disabled`", documentation, StringComparison.Ordinal);
    }

    [Fact]
    public void Private_beta_removes_incomplete_output_after_early_exit_or_publish_timeout()
    {
        using var fixture = new SyntheticRepository();
        var early = fixture.Output("early");
        var earlyResult = fixture.Run(early, publisher: fixture.Publisher("exit"), smokeStartupMilliseconds: 5000);
        Assert.NotEqual(0, earlyResult.ExitCode);
        Assert.Contains("BETA_SMOKE_EARLY_EXIT", earlyResult.Output);
        Assert.False(Directory.Exists(early));

        var timeout = fixture.Output("timeout");
        var timeoutResult = fixture.Run(timeout, publisher: fixture.Publisher("timeout"), timeout: 1);
        Assert.NotEqual(0, timeoutResult.ExitCode);
        Assert.False(Directory.Exists(timeout));
    }

    private sealed class SyntheticRepository : IDisposable
    {
        private readonly TemporaryRoot _temporary = new("private-beta");
        public string Root { get; }
        public string Baseline { get; }
        public string Parent { get; }
        public string Source { get; }

        public SyntheticRepository()
        {
            Root = Path.Combine(_temporary.Path, "repository");
            Directory.CreateDirectory(Path.Combine(Root, "scripts"));
            Directory.CreateDirectory(Path.Combine(Root, "src", "AIBar.Desktop"));
            Directory.CreateDirectory(Path.Combine(Root, "yasb"));
            File.Copy(Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1"), Path.Combine(Root, "scripts", "Publish-Deterministic.ps1"));
            foreach (var name in new[] { "read-aibar-quota.ps1", "remove-aibar-quota.ps1", "show-aibar.cmd", "custom-widget.example.yaml", "custom-widget.example.css", "README.txt" })
                File.Copy(Path.Combine(RepositoryRoot(), "yasb", name), Path.Combine(Root, "yasb", name));
            File.WriteAllText(Path.Combine(Root, "src", "AIBar.Desktop", "AIBar.Desktop.csproj"), "<Project><PropertyGroup><Version>0.1.0-beta.4</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(Root, "README.md"), "baseline");
            Git("init"); Git("add", "."); Git("-c", "user.name=tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "baseline");
            Baseline = Git("rev-parse", "HEAD").Output.Trim();
            File.WriteAllText(Path.Combine(Root, "README.md"), "three-b"); Git("add", "README.md"); Git("-c", "user.name=tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "three-b");
            Parent = Git("rev-parse", "HEAD").Output.Trim();
            File.WriteAllText(Path.Combine(Root, "README.md"), "unit4"); Git("add", "README.md"); Git("-c", "user.name=tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "unit4");
            Source = Git("rev-parse", "HEAD").Output.Trim();
        }

        public string Output(string name) => Path.Combine(_temporary.Path, $"output-{name}");
        public string Publisher(string mode)
        {
            var path = Path.Combine(_temporary.Path, $"publish-{mode}.cmd");
            var source = mode switch { "stay" => "%ComSpec%", "exit" => "%SystemRoot%\\System32\\whoami.exe", _ => "" };
            File.WriteAllText(path, mode == "timeout" ? "@for /l %%A in (1,1,1000000000) do @set /a 1+1 > nul\r\n" : $"@set output=\r\n:args\r\n@if \"%~1\"==\"\" goto done\r\n@if \"%~1\"==\"--output\" (set output=%~2&shift)\r\n@shift\r\n@goto args\r\n:done\r\n@if not exist \"%output%\" mkdir \"%output%\"\r\n@copy /y \"{source}\" \"%output%\\AIBar.Desktop.exe\" > nul\r\n");
            return path;
        }

        public (int ExitCode, string Output) Run(string output, string? publisher = null, string? parent = null, int timeout = 5, int smokeStartupMilliseconds = 1000, string? script = null)
        {
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["AIBAR_PRIVATE_BETA_TEST_MODE"] = "1";
            foreach (var argument in new[] { "-NoProfile", "-File", script ?? Path.Combine(Root, "scripts", "Publish-Deterministic.ps1"), "-PrivateBeta", "-OutputDirectory", output, "-BetaBaselineCommit", Baseline, "-BetaParentCommit", parent ?? Parent, "-PublishCommand", publisher ?? Publisher("stay"), "-ProcessTimeoutSeconds", timeout.ToString(), "-SmokeStartupMilliseconds", smokeStartupMilliseconds.ToString() }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
            return (process.ExitCode, text);
        }

        public (int ExitCode, string Output) Git(params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); Assert.True(process.ExitCode == 0, output); return (process.ExitCode, output);
        }

        public void Dispose() => _temporary.Dispose();
    }

    private static string RepositoryRoot() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); ; directory = directory.Parent!) if (File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) return directory.FullName; }
    private sealed class TemporaryRoot : IDisposable { public string Path { get; } public TemporaryRoot(string suffix) => Path = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aibar-{suffix}-{Guid.NewGuid():N}")).FullName; public void Dispose() { if (Directory.Exists(Path)) { foreach (var item in new DirectoryInfo(Path).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) item.Attributes = FileAttributes.Normal; Directory.Delete(Path, true); } } }
}
