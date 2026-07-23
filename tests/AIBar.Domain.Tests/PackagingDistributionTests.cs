using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace AIBar.Domain.Tests;

public sealed class PackagingDistributionTests
{
    [Fact]
    public void Manifest_and_png_assets_are_schema_shaped_and_dimensioned()
    {
        var root = RepositoryRoot();
        var manifest = XDocument.Load(Path.Combine(root, "packaging", "AppxManifest.xml"));
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";

        Assert.Equal("x64", manifest.Root!.Element(foundation + "Identity")!.Attribute("ProcessorArchitecture")!.Value);
        Assert.Equal("CN=AIBar Development", manifest.Root!.Element(foundation + "Identity")!.Attribute("Publisher")!.Value);
        Assert.NotNull(manifest.Root!.Element(foundation + "Capabilities")!.Element(foundation + "Capability")?.Attributes("Name").Single(attribute => attribute.Value == "internetClient"));
        Assert.NotNull(manifest.Descendants(desktop + "Extension").SingleOrDefault(extension => extension.Attribute("Category")?.Value == "windows.fullTrustProcess"));

        foreach (var (name, width, height) in new[] { ("StoreLogo.png", 50, 50), ("Square44x44Logo.png", 44, 44), ("Square150x150Logo.png", 150, 150), ("Wide310x150Logo.png", 310, 150) })
            Assert.Equal((width, height), PngDimensions(Path.Combine(root, "packaging", "Assets", name)));
    }

    [Fact]
    public void Capability_plan_is_mutually_exclusive_fail_closed_and_secret_free()
    {
        var unavailable = Plan();
        Assert.Equal("tool-unavailable", unavailable.GetProperty("msixStatus").GetString());
        Assert.Equal("signing-unavailable", unavailable.GetProperty("signingStatus").GetString());
        Assert.Empty(unavailable.GetProperty("commands").EnumerateArray());

        var unsigned = Plan("-MakeAppxPath", "makeappx.cmd");
        Assert.Equal("unsigned", unsigned.GetProperty("msixStatus").GetString());
        Assert.Equal("signing-unavailable", unsigned.GetProperty("signingStatus").GetString());

        var planned = Plan("-MakeAppxPath", "makeappx.cmd", "-SignToolPath", "signtool.cmd", "-CertificateSubject", "CN=AIBar Development", "-Secret", "seeded-secret", "-SigningRequested");
        Assert.Equal(new[] { "MakeAppx pack", "SignTool sign" }, planned.GetProperty("commands").EnumerateArray().Select(command => command.GetString()));
        Assert.DoesNotContain("seeded-secret", planned.GetRawText(), StringComparison.Ordinal);

        var requested = Plan("-MakeAppxPath", "makeappx.cmd", "-SigningRequested", "-CertificateSubject", "CN=AIBar Development", "-Secret", "Bearer seeded-secret");
        Assert.Equal("failed", requested.GetProperty("msixStatus").GetString());
        Assert.Equal("requested-signing-unavailable", requested.GetProperty("signingStatus").GetString());
        Assert.DoesNotContain("seeded-secret", requested.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Capability_plan_rejects_publisher_mismatch_stale_output_and_root_bearing_output()
    {
        using var root = new TemporaryRoot();
        var stale = Path.Combine(root.Path, "stale.msix"); File.WriteAllText(stale, "old");
        var result = Plan("-MakeAppxPath", "makeappx.cmd", "-SignToolPath", "signtool.cmd", "-CertificateSubject", "CN=wrong", "-Secret", "safe", "-SigningRequested", "-PackageOutput", stale);
        Assert.Equal("stale-output", result.GetProperty("msixStatus").GetString());
        Assert.Equal("publisher-mismatch", result.GetProperty("signingStatus").GetString());
        Assert.Equal("old", File.ReadAllText(stale));
        Assert.DoesNotContain(root.Path, result.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement Plan(params string[] arguments)
    {
        var result = Run(arguments);
        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        return json.RootElement.Clone();
    }

    private static (int ExitCode, string Output) Run(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-File"); start.ArgumentList.Add(Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1")); start.ArgumentList.Add("-CapabilityPlan");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output);
    }

    private static (int Width, int Height) PngDimensions(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        return (ReadInt32(bytes, 16), ReadInt32(bytes, 20));
    }

    private static int ReadInt32(byte[] bytes, int offset) => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    private static string RepositoryRoot() { for (var directory = new DirectoryInfo(AppContext.BaseDirectory); ; directory = directory.Parent!) if (File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) return directory.FullName; }
    private sealed class TemporaryRoot : IDisposable { public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aibar-8c2a-{Guid.NewGuid():N}")).FullName; public void Dispose() => Directory.Delete(Path, true); }
}
