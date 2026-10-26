using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AIBar.Domain.Tests;

public sealed class ReleasePackagingTests
{
    [Fact]
    public void Release_keeps_publish_and_produces_versioned_deterministic_zip_manifest_and_checksums()
    {
        using var fixture = new Repository();
        var output = fixture.Output("release");
        var result = fixture.Run(output);
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.Equal(new[] { "AIBar-2.3.4-win-x64.zip", "SHA256SUMS.txt", "release-manifest.json" }, Directory.GetFiles(output).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal));
        Assert.True(Directory.Exists(Path.Combine(output, "publish")));
        Assert.False(Directory.Exists(Path.Combine(output, "smoke")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "release-manifest.json")));
        var root = manifest.RootElement;
        Assert.Equal("2.3.4", root.GetProperty("version").GetString());
        Assert.Equal(fixture.Source, root.GetProperty("sourceCommit").GetString());
        Assert.Equal("win-x64", root.GetProperty("target").GetString());
        Assert.True(root.GetProperty("selfContained").GetBoolean());
        var zipPath = Path.Combine(output, "AIBar-2.3.4-win-x64.zip");
        Assert.Equal(Hash(zipPath), root.GetProperty("zipSha256").GetString());
        var sums = File.ReadAllLines(Path.Combine(output, "SHA256SUMS.txt"));
        Assert.Equal(2, sums.Length);
        Assert.Equal(new[] { "AIBar-2.3.4-win-x64.zip", "release-manifest.json" }, sums.Select(line => line[66..]));
        foreach (var line in sums)
        {
            Assert.Equal("  ", line.Substring(64, 2));
            Assert.Matches("^[0-9a-f]{64}$", line[..64]);
            Assert.Equal(Hash(Path.Combine(output, line[66..])), line[..64]);
        }
        using var archive = ZipFile.OpenRead(zipPath);
        var paths = archive.Entries.Select(entry => entry.FullName).ToArray();
        Assert.Equal(paths.OrderBy(path => path, StringComparer.Ordinal), paths);
        Assert.DoesNotContain("PRIVATE-BETA.txt", paths);
        Assert.DoesNotContain("private beta", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private beta", File.ReadAllText(Path.Combine(output, "publish", "yasb", "README.txt")), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(8, paths.Count(path => path.StartsWith("yasb/", StringComparison.Ordinal)));
        Assert.Equal(2, paths.Count(path => path.StartsWith("fonts/", StringComparison.Ordinal)));
        Assert.Equal(paths, root.GetProperty("inventory").EnumerateArray().Select(item => item.GetProperty("path").GetString()));
        foreach (var item in root.GetProperty("inventory").EnumerateArray())
        {
            var relative = item.GetProperty("path").GetString()!;
            var file = Path.Combine(output, "publish", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(new FileInfo(file).Length, item.GetProperty("length").GetInt64());
            Assert.Equal(Hash(file), item.GetProperty("sha256").GetString());
            using var stream = archive.GetEntry(relative)!.Open();
            Assert.Equal(Hash(file), Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }
        var repeat = fixture.Output("repeat");
        var second = fixture.Run(repeat);
        Assert.True(second.ExitCode == 0, second.Text);
        foreach (var file in Directory.GetFiles(output))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(repeat, Path.GetFileName(file))));
    }

    [Theory]
    [InlineData("-PrivateBeta")]
    [InlineData("-CapabilityPlan")]
    [InlineData("-GraphProjection")]
    public void Conflicting_modes_fail_before_output_or_publish(string mode)
    {
        using var fixture = new Repository();
        var output = fixture.Output("conflict");
        var result = fixture.Run(output, mode: mode);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("RELEASE_OPTIONS_CONFLICT", result.Text);
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(fixture.Marker));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0000000000000000000000000000000000000000")]
    [InlineData("short-sha")]
    public void Source_pin_is_required_and_must_match_full_HEAD(string pin)
    {
        using var fixture = new Repository();
        var output = fixture.Output("pin");
        var result = fixture.Run(output, pin: pin);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("RELEASE_SOURCE_MISMATCH", result.Text);
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(fixture.Marker));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dirty_or_staged_source_is_rejected(bool staged)
    {
        using var fixture = new Repository();
        File.AppendAllText(Path.Combine(fixture.Root, "README.md"), "dirty");
        if (staged) fixture.Git("add", "README.md");
        var output = fixture.Output("dirty");
        var result = fixture.Run(output);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("RELEASE_SOURCE_UNCOMMITTED", result.Text);
        Assert.False(Directory.Exists(output));
        Assert.False(File.Exists(fixture.Marker));
    }

    [Fact]
    public void Private_beta_preparation_still_has_notice_legacy_names_and_no_publish_tree()
    {
        using var fixture = new Repository();
        var output = fixture.Output("beta");
        var result = fixture.Run(output, beta: true);
        Assert.True(result.ExitCode == 0, result.Text);
        Assert.False(Directory.Exists(Path.Combine(output, "publish")));
        Assert.False(File.Exists(Path.Combine(output, "SHA256SUMS.txt")));
        using var archive = ZipFile.OpenRead(Path.Combine(output, "AIBar-win-x64-private-beta.zip"));
        Assert.NotNull(archive.GetEntry("PRIVATE-BETA.txt"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "private-beta-manifest.json")));
        Assert.Equal("not-run", manifest.RootElement.GetProperty("startupSmoke").GetString());
        Assert.Equal(fixture.Source, manifest.RootElement.GetProperty("sourceCommit").GetString());
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); ; directory = directory.Parent!)
            if (File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) return directory.FullName;
    }

    private sealed class Repository : IDisposable
    {
        // One task-owned test root; Dispose removes synthetic repositories, publishers and outputs.
        private readonly string temporary = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"aibar-release-{Guid.NewGuid():N}")).FullName;
        public string Root { get; }
        public string Source { get; }
        private string Parent { get; }
        private string GlyphDirectory => Path.Combine(temporary, "glyphs");
        public string Marker => Path.Combine(temporary, "publisher-started");
        private string Publisher => Path.Combine(temporary, "publish.cmd");
        public Repository()
        {
            Root = Path.Combine(temporary, "repository");
            Directory.CreateDirectory(Path.Combine(Root, "scripts"));
            Directory.CreateDirectory(Path.Combine(Root, "src", "AIBar.Desktop"));
            File.Copy(Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1"), Path.Combine(Root, "scripts", "Publish-Deterministic.ps1"));
            foreach (var file in Directory.GetFiles(Path.Combine(RepositoryRoot(), "yasb"), "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(Root, Path.GetRelativePath(RepositoryRoot(), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            File.WriteAllText(Path.Combine(Root, "src", "AIBar.Desktop", "AIBar.Desktop.csproj"), "<Project><PropertyGroup><Version>2.3.4</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(Root, "README.md"), "release");
            Git("init"); Git("add", "."); Git("-c", "user.name=tests", "-c", "user.email=tests@example.invalid", "commit", "-m", "initial");
            Parent = Git("rev-parse", "HEAD");
            Git("-c", "user.name=tests", "-c", "user.email=tests@example.invalid", "commit", "--allow-empty", "-m", "source");
            Source = Git("rev-parse", "HEAD");
            Directory.CreateDirectory(GlyphDirectory);
            File.WriteAllText(Path.Combine(GlyphDirectory, "IosevkaTermNerdFont-Regular.ttf"), "synthetic font");
            File.WriteAllText(Path.Combine(GlyphDirectory, "IosevkaTerm-LICENSE.md"), "synthetic license");
            // Deliberately invalid executable: a successful release proves startup smoke is not run.
            File.WriteAllText(Publisher, $"@echo started>\"{Marker}\"\r\n@set output=\r\n:args\r\n@if \"%~1\"==\"\" goto done\r\n@if \"%~1\"==\"--output\" (set output=%~2&shift)\r\n@shift\r\n@goto args\r\n:done\r\n@echo not-an-executable>\"%output%\\AIBar.Desktop.exe\"\r\n");
        }
        public string Output(string name) => Path.Combine(temporary, name);
        public (int ExitCode, string Text) Run(string output, string? pin = null, string? mode = null, bool beta = false)
        {
            var arguments = new List<string> { "-NoProfile", "-File", Path.Combine(Root, "scripts", "Publish-Deterministic.ps1"), beta ? "-PrivateBeta" : "-Release", "-OutputDirectory", output, "-PublishCommand", Publisher, "-GlyphFontDirectory", GlyphDirectory, "-TestGlyphFontSha256", Hash(Path.Combine(GlyphDirectory, "IosevkaTermNerdFont-Regular.ttf")), "-TestGlyphFontLength", new FileInfo(Path.Combine(GlyphDirectory, "IosevkaTermNerdFont-Regular.ttf")).Length.ToString(), "-TestGlyphLicenseSha256", Hash(Path.Combine(GlyphDirectory, "IosevkaTerm-LICENSE.md")) };
            if (pin != "") arguments.AddRange(new[] { "-ExpectedSourceCommit", pin ?? Source });
            if (mode is not null) arguments.Add(mode);
            if (beta) arguments.AddRange(new[] { "-PrepareOnly", "-BetaBaselineCommit", Parent, "-BetaParentCommit", Parent });
            return Execute("pwsh", arguments, true);
        }
        public string Git(params string[] arguments)
        {
            var result = Execute("git", arguments);
            Assert.True(result.ExitCode == 0, result.Text);
            return result.Text.Trim();
        }
        private (int ExitCode, string Text) Execute(string command, IEnumerable<string> arguments, bool testMode = false)
        {
            var start = new ProcessStartInfo(command) { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            if (testMode) start.Environment["AIBAR_PRIVATE_BETA_TEST_MODE"] = "1";
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        public void Dispose()
        {
            foreach (var item in new DirectoryInfo(temporary).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)) item.Attributes = FileAttributes.Normal;
            Directory.Delete(temporary, true);
        }
    }
}
