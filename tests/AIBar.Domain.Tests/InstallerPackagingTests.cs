using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace AIBar.Domain.Tests;

public sealed class InstallerPackagingTests
{
    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) directory = directory.Parent;
            return directory?.FullName ?? throw new InvalidOperationException("Repository not found");
        }
    }

    [Fact]
    public void Installer_is_per_user_x64_localized_and_uses_release_inputs()
    {
        var text = File.ReadAllText(Path.Combine(Root, "packaging/installer/AIBar.iss"));
        foreach (var directive in new[] { "AppName=AIBar", "AppPublisher=MarsSall", "AppPublisherURL=https://github.com/MarsSall/aibar", "PrivilegesRequired=lowest", "DefaultDirName={localappdata}\\Programs\\AIBar", "ArchitecturesAllowed=x64compatible", "ArchitecturesInstallIn64BitMode=x64compatible", "AppVersion={#AppVersion}", "CloseApplications=yes", "OutputBaseFilename=AIBar-{#AppVersion}-win-x64-setup", "AppId={{" })
            Assert.Contains(directive, text);
        Assert.Matches(@"AppId=\{\{[0-9A-Fa-f-]{36}\}", text);
        Assert.Contains("{#RepositoryRoot}\\src\\AIBar.Desktop\\Assets\\AIBar.ico", text);
        Assert.Contains("LicenseFile={#RepositoryRoot}\\LICENSE", text);
        Assert.Contains("Source: \"{#PublishDirectory}\\*\"", text);
        Assert.Contains("recursesubdirs createallsubdirs", text);
        Assert.Contains("{group}\\AIBar", text);
        Assert.Contains("{autodesktop}\\AIBar", text);
        Assert.Contains("Flags: unchecked", text);
        Assert.Contains("postinstall skipifsilent", text);
        Assert.Contains("compiler:Default.isl", text);
        Assert.Contains("compiler:Languages\\Spanish.isl", text);
        var app = File.ReadAllText(Path.Combine(Root, "src/AIBar.Desktop/App.xaml.cs"));
        var host = File.ReadAllText(Path.Combine(Root, "src/AIBar.Desktop/HostPrimitives.cs"));
        var name = Regex.Match(app, "new SingleInstanceHost\\(\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(name);
        var suffix = Regex.Match(host, "new Mutex\\(false, \\$\"\\{name\\}([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(suffix);
        Assert.Contains("AppMutex=" + name + suffix, text);
    }

    [Fact]
    public void Uninstall_requires_explicit_interactive_consent_and_only_removes_owned_startup_value()
    {
        var text = File.ReadAllText(Path.Combine(Root, "packaging/installer/AIBar.iss"));
        Assert.Contains("Delete my AIBar data too", text);
        Assert.Contains("Borrar también mis datos", text);
        Assert.Contains("DeleteData.Checked := False", text);
        Assert.Contains("if UninstallSilent then", text);
        Assert.Contains("DeleteDataRequested := False", text);
        Assert.Contains("DeleteDataRequested := DeleteData.Checked", text);
        Assert.Contains("if DeleteDataRequested then", text);
        Assert.Contains("DelTree(ExpandConstant('{localappdata}\\AIBar'), True, True, True)", text);
        Assert.Single(Regex.Matches(text, @"\bDelTree\("));
        Assert.Contains("usPostUninstall", text);
        var startup = File.ReadAllText(Path.Combine(Root, "src/AIBar.Application/StartupSettings.cs"));
        var key = Regex.Match(startup, "KeyPath = \"([^\"]+)\"").Groups[1].Value.Replace("\\\\", "\\");
        Assert.NotEmpty(key);
        Assert.Contains(key, text);
        var app = File.ReadAllText(Path.Combine(Root, "src/AIBar.Desktop/App.xaml.cs"));
        var valueName = Regex.Match(app, "new WindowsCurrentUserRunStore\\(\\), \"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(valueName);
        Assert.Contains($"RegDeleteValue(HKCU, RunKey, '{valueName}')", text);
        Assert.Contains("CompareText(Command, ExpectedCommand) = 0", text);
        Assert.Contains("ExpandConstant('{app}\\AIBar.Desktop.exe')", text);
        Assert.DoesNotContain("[UninstallDelete]", text);
    }

    [Fact]
    public void Builder_passes_version_and_source_defines_and_appends_exact_checksum()
    {
        using var fixture = new Fixture();
        var result = fixture.Run();
        Assert.True(result.Code == 0, result.Text);
        var args = File.ReadAllLines(fixture.Arguments);
        Assert.Contains("/DAppVersion=2.3.4-beta.1", args);
        Assert.Contains("/DPublishDirectory=" + Path.Combine(fixture.Output, "publish"), args);
        Assert.Contains("/DRepositoryRoot=" + Root, args);
        Assert.Contains("/O" + fixture.Output, args);
        Assert.Contains("/FAIBar-2.3.4-beta.1-win-x64-setup", args);
        Assert.Contains(Path.Combine(Root, "packaging", "installer", "AIBar.iss"), args);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture.Setup))).ToLowerInvariant();
        Assert.Equal(fixture.OriginalSums + hash + "  " + Path.GetFileName(fixture.Setup) + "\n", File.ReadAllText(fixture.Sums));
    }

    [Theory]
    [InlineData("missing-iscc")]
    [InlineData("missing-publish")]
    [InlineData("compiler-failure")]
    [InlineData("no-output")]
    [InlineData("invalid-version")]
    [InlineData("missing-manifest")]
    [InlineData("missing-yasb")]
    [InlineData("missing-fonts")]
    public void Builder_failures_preserve_checksums_and_leave_no_partial_setup(string failure)
    {
        using var fixture = new Fixture();
        var result = fixture.Run(failure);
        Assert.True(result.Code != 0, result.Text);
        Assert.Contains("INSTALLER_", result.Text);
        Assert.False(File.Exists(fixture.Setup));
        Assert.Equal(fixture.OriginalSums, File.ReadAllText(fixture.Sums));
    }

    [Fact]
    public void Builder_does_not_replace_an_existing_setup_or_its_checksum()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Setup, "previous setup");
        var result = fixture.Run();
        Assert.NotEqual(0, result.Code);
        Assert.Contains("INSTALLER_OUTPUT_EXISTS", result.Text);
        Assert.Equal("previous setup", File.ReadAllText(fixture.Setup));
        Assert.Equal(fixture.OriginalSums, File.ReadAllText(fixture.Sums));
        Assert.False(File.Exists(fixture.Arguments));
    }

    [Fact]
    public void Builder_separates_an_existing_checksum_without_a_final_newline()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Sums, fixture.OriginalSums.TrimEnd('\n'));
        var result = fixture.Run();
        Assert.True(result.Code == 0, result.Text);
        Assert.Equal(2, File.ReadAllLines(fixture.Sums).Length);
        Assert.StartsWith(fixture.OriginalSums, File.ReadAllText(fixture.Sums));
    }

    private sealed class Fixture : IDisposable
    {
        // Test-owned trial artifacts stay in the checkout and are removed after each case.
        private readonly string directory = Path.Combine(Root, "artifacts", "installer-tests", Guid.NewGuid().ToString("N"));
        public string Output => Path.Combine(directory, "release output");
        public string Arguments => Path.Combine(Output, "arguments.txt");
        public string Setup => Path.Combine(Output, "AIBar-2.3.4-beta.1-win-x64-setup.exe");
        public string Sums => Path.Combine(Output, "SHA256SUMS.txt");
        public string OriginalSums => new string('a', 64) + "  portable.zip\n";
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Output, "publish"));
            File.WriteAllText(Path.Combine(Output, "publish", "AIBar.Desktop.exe"), "app");
            Directory.CreateDirectory(Path.Combine(Output, "publish", "yasb"));
            Directory.CreateDirectory(Path.Combine(Output, "publish", "fonts"));
            File.WriteAllText(Path.Combine(Output, "release-manifest.json"), "{\"schemaVersion\":\"aibar-release-1\",\"target\":\"win-x64\",\"version\":\"2.3.4-beta.1\"}");
            File.WriteAllText(Sums, OriginalSums);
        }
        public (int Code, string Text) Run(string? failure = null)
        {
            var fake = Path.Combine(directory, "fake iscc.ps1");
            var escaped = Output.Replace("'", "''");
            File.WriteAllText(fake, $"$args | Set-Content -LiteralPath '{escaped}\\arguments.txt'\n" +
                (failure == "no-output" ? "exit 0" : $"[IO.File]::WriteAllText('{Setup.Replace("'", "''")}', 'setup bytes')\nexit {(failure == "compiler-failure" ? 7 : 0)}"));
            if (failure == "missing-publish") File.Delete(Path.Combine(Output, "publish", "AIBar.Desktop.exe"));
            if (failure == "missing-manifest") File.Delete(Path.Combine(Output, "release-manifest.json"));
            if (failure == "missing-yasb") Directory.Delete(Path.Combine(Output, "publish", "yasb"));
            if (failure == "missing-fonts") Directory.Delete(Path.Combine(Output, "publish", "fonts"));
            if (failure == "invalid-version") File.WriteAllText(Path.Combine(Output, "release-manifest.json"), "{\"version\":\"../unsafe\"}");
            var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-File", Path.Combine(Root, "scripts", "Build-Installer.ps1"), "-ReleaseDirectory", Output, "-IsccPath", failure == "missing-iscc" ? Path.Combine(directory, "missing.exe") : fake }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            Assert.True(process.WaitForExit(60000), "Installer build timed out");
            return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }
        public void Dispose() => Directory.Delete(directory, true);
    }
}
