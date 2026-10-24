using System.Security.Cryptography;
using System.Text;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class GlyphFontInstallerTests
{
    private static readonly byte[] Font = Encoding.UTF8.GetBytes("synthetic font bytes, never a real font");
    private static string Hash => Convert.ToHexString(SHA256.HashData(Font));

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Detection_checks_both_hives_and_resolves_relative_paths(bool currentUser, bool absolute)
    {
        var environment = new FakeEnvironment();
        var path = absolute ? Path.Combine(environment.UserFontsDirectory, "other.ttf") : Path.Combine(environment.WindowsFontsDirectory, "other.ttf");
        environment.Files[path] = Font;
        environment.Registrations.Add((currentUser, new("iosevkaterm nerd font Regular (TrueType)", absolute ? path : "other.ttf")));
        Assert.True(new GlyphFontInstaller(environment).IsInstalled());
        Assert.Empty(environment.Writes);
    }

    [Fact]
    public void Detection_requires_matching_name_and_an_existing_file()
    {
        var environment = new FakeEnvironment();
        var installer = new GlyphFontInstaller(environment);
        Assert.False(installer.IsInstalled());
        environment.Registrations.Add((true, new(GlyphFontInstaller.RegistryValueName, "missing.ttf")));
        environment.Registrations.Add((false, new(GlyphFontInstaller.RegistryValueName, null)));
        environment.Files[Path.Combine(environment.WindowsFontsDirectory, "unrelated.ttf")] = Font;
        environment.Registrations.Add((false, new("Unrelated font", "unrelated.ttf")));
        Assert.False(installer.IsInstalled());
    }

    [Fact]
    public void Missing_or_mismatched_source_is_refused_without_any_writes()
    {
        var environment = new FakeEnvironment();
        var installer = new GlyphFontInstaller(environment);
        Assert.Equal(GlyphFontInstallStatus.SourceMissing, installer.Install("missing.ttf").Status);
        environment.Files["bad.ttf"] = Font;
        Assert.Equal(GlyphFontInstallStatus.SourceMismatch, installer.Install("bad.ttf").Status);
        Assert.Empty(environment.Writes);
        Assert.Empty(environment.Registrations);
        Assert.Single(environment.Files);
    }

    [Fact]
    public void Install_stages_verified_bytes_and_registers_exact_per_user_path_once()
    {
        var environment = new FakeEnvironment();
        environment.Files["source.ttf"] = Font;
        var installer = new GlyphFontInstaller(environment, Hash);
        Assert.Equal(GlyphFontInstallStatus.Installed, installer.Install("source.ttf").Status);
        var destination = Path.Combine(environment.UserFontsDirectory, GlyphFontInstaller.FileName);
        Assert.Equal(Font, environment.Files[destination]);
        var registration = Assert.Single(environment.Registrations);
        Assert.True(registration.CurrentUser);
        Assert.Equal(GlyphFontInstaller.RegistryValueName, registration.Entry.Name);
        Assert.Equal(destination, registration.Entry.FilePath);
        Assert.Equal(new[] { "mkdir", "copy", "move", "add", "registry", "broadcast" }, environment.Writes);
        Assert.Equal(destination, environment.AddedFont);
        Assert.DoesNotContain(environment.Files.Keys, path => path.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.True(installer.IsInstalled());
        var writes = environment.Writes.ToArray();
        Assert.Equal(GlyphFontInstallStatus.AlreadyInstalled, installer.Install("source.ttf").Status);
        Assert.Equal(writes, environment.Writes);
    }

    [Fact]
    public void Existing_registered_font_is_reused_without_writes_but_source_is_still_verified()
    {
        var environment = new FakeEnvironment();
        environment.Files["source.ttf"] = Font;
        var path = Path.Combine(environment.WindowsFontsDirectory, GlyphFontInstaller.FileName);
        environment.Files[path] = Font;
        environment.Registrations.Add((false, new(GlyphFontInstaller.RegistryValueName, GlyphFontInstaller.FileName)));
        Assert.Equal(GlyphFontInstallStatus.AlreadyInstalled, new GlyphFontInstaller(environment, Hash).Install("source.ttf").Status);
        Assert.Equal(GlyphFontInstallStatus.SourceMismatch, new GlyphFontInstaller(environment).Install("source.ttf").Status);
        Assert.Empty(environment.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Existing_destination_is_reused_only_when_identical(bool identical)
    {
        var environment = new FakeEnvironment();
        environment.Files["source.ttf"] = Font;
        var destination = Path.Combine(environment.UserFontsDirectory, GlyphFontInstaller.FileName);
        environment.Files[destination] = identical ? Font : new byte[] { 1 };
        Assert.Equal(GlyphFontInstallStatus.Installed, new GlyphFontInstaller(environment, Hash).Install("source.ttf").Status);
        Assert.Equal(Font, environment.Files[destination]);
        Assert.Equal(!identical, environment.Writes.Contains("copy"));
        Assert.Contains("registry", environment.Writes);
    }

    [Fact]
    public void Copy_failure_returns_only_exception_type_and_cleans_owned_temporary()
    {
        var environment = new FakeEnvironment { FailCopy = true };
        environment.Files["source.ttf"] = Font;
        var result = new GlyphFontInstaller(environment, Hash).Install("source.ttf");
        Assert.Equal(GlyphFontInstallStatus.Failed, result.Status);
        Assert.Equal(nameof(IOException), result.ExceptionType);
        Assert.Empty(environment.Registrations);
        Assert.Single(environment.Files);
        Assert.DoesNotContain("add", environment.Writes);
    }

    [Fact]
    public void Rejected_font_resource_leaves_nothing_registered_and_can_be_retried()
    {
        var environment = new FakeEnvironment { FailAdd = true };
        environment.Files["source.ttf"] = Font;
        var installer = new GlyphFontInstaller(environment, Hash);
        var result = installer.Install("source.ttf");
        Assert.Equal(GlyphFontInstallStatus.Failed, result.Status);
        Assert.Equal(nameof(InvalidOperationException), result.ExceptionType);
        Assert.Empty(environment.Registrations);
        Assert.Equal(new[] { "source.ttf" }, environment.Files.Keys.ToArray());
        Assert.False(installer.IsInstalled());

        environment.FailAdd = false;
        Assert.Equal(GlyphFontInstallStatus.Installed, installer.Install("source.ttf").Status);
        Assert.True(installer.IsInstalled());
    }

    [Fact]
    public void Failed_change_broadcast_still_reports_an_installed_and_registered_font()
    {
        var environment = new FakeEnvironment { FailBroadcast = true };
        environment.Files["source.ttf"] = Font;
        var installer = new GlyphFontInstaller(environment, Hash);
        Assert.Equal(GlyphFontInstallStatus.Installed, installer.Install("source.ttf").Status);
        Assert.Single(environment.Registrations);
        Assert.True(installer.IsInstalled());
    }

    [Fact]
    public void Bundle_validation_uses_the_pinned_hash_and_expected_path()
    {
        var environment = new FakeEnvironment();
        var path = GlyphFontInstaller.BundledFontPath("application");
        Assert.Equal(Path.Combine("application", "fonts", "IosevkaTermNerdFont-Regular.ttf"), path);
        var installer = new GlyphFontInstaller(environment, Hash);
        Assert.False(installer.IsBundledFontAvailable("application"));
        environment.Files[path] = Font;
        Assert.True(installer.IsBundledFontAvailable("application"));
        Assert.False(new GlyphFontInstaller(environment).IsBundledFontAvailable("application"));
        Assert.Equal(new[] { 0xF0766, 0xF0A9E, 0xF0A9F, 0xF0AA0, 0xF0AA1, 0xF0AA2, 0xF0AA3, 0xF0AA4, 0xF0AA5 }, GlyphFontInstaller.RequiredCodepoints);
    }

    private sealed class FakeEnvironment : IGlyphFontEnvironment
    {
        public string WindowsFontsDirectory => Path.GetFullPath(Path.Combine("windows", "Fonts"));
        public string UserFontsDirectory => Path.GetFullPath(Path.Combine("local", "Microsoft", "Windows", "Fonts"));
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(bool CurrentUser, GlyphFontRegistration Entry)> Registrations { get; } = new();
        public List<string> Writes { get; } = new();
        public bool FailCopy { get; init; }
        public bool FailAdd { get; set; }
        public bool FailBroadcast { get; init; }
        public string? AddedFont { get; private set; }
        public IEnumerable<GlyphFontRegistration> ReadFontRegistrations(bool currentUser) => Registrations.Where(item => item.CurrentUser == currentUser).Select(item => item.Entry).ToArray();
        public bool FileExists(string path) => Files.ContainsKey(path);
        public Stream OpenRead(string path) => new MemoryStream(Files[path], writable: false);
        public void CreateDirectory(string path) => Writes.Add("mkdir");
        public void CopyFile(string source, string destination)
        {
            Writes.Add("copy");
            Files[destination] = Files[source].ToArray();
            if (FailCopy) throw new IOException("must not leak this message");
        }
        public void MoveFile(string source, string destination) { Writes.Add("move"); Files[destination] = Files[source]; Files.Remove(source); }
        public void DeleteFile(string path) { Writes.Add("delete"); Files.Remove(path); }
        public void WriteUserFontRegistration(string name, string path) { Writes.Add("registry"); Registrations.Add((true, new(name, path))); }
        public void AddFontResource(string path)
        {
            Writes.Add("add");
            if (FailAdd) throw new InvalidOperationException("Windows rejected the font");
            AddedFont = path;
        }
        public void BroadcastFontChange()
        {
            Writes.Add("broadcast");
            if (FailBroadcast) throw new InvalidOperationException("broadcast failed");
        }
    }
}
