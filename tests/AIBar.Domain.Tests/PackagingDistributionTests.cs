using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [Fact]
    public void GraphProjection_is_canonical_and_rejects_every_authoritative_fixture_fault()
    {
        using var good = FixtureCopy("graph Ω"); var first = Graph(good.Path); var sbom = File.ReadAllBytes(first.Sbom); var manifest = File.ReadAllBytes(first.Manifest);
        Assert.Equal((byte)'\n', sbom[^1]); Assert.Equal((byte)'\n', manifest[^1]); Assert.Contains("CycloneDX", File.ReadAllText(first.Sbom)); Assert.Contains("urn:aibar:file:sha256-path:", File.ReadAllText(first.Sbom)); Assert.Contains("\"value\":\"transitive\"", File.ReadAllText(first.Sbom));
        using var reordered = FixtureCopy("reordered space"); var inventory = JsonNode.Parse(File.ReadAllText(Path.Combine(reordered.Path, "publish-inventory.json")))!.AsObject(); inventory["artifacts"] = new JsonArray(inventory["artifacts"]!.AsArray().Reverse().Select(item => item!.DeepClone()).ToArray()); File.WriteAllText(Path.Combine(reordered.Path, "publish-inventory.json"), inventory.ToJsonString()); var second = Graph(reordered.Path); Assert.Equal(sbom, File.ReadAllBytes(second.Sbom)); Assert.Equal(manifest, File.ReadAllBytes(second.Manifest));
        using var unicodeA = FixtureCopy("unicode A"); using var unicodeB = FixtureCopy("unicode B"); UnicodeNames(unicodeA.Path); UnicodeNames(unicodeB.Path); var unicodeFirst = Graph(unicodeA.Path); var unicodeSecond = Graph(unicodeB.Path); Assert.Equal(File.ReadAllBytes(unicodeFirst.Sbom), File.ReadAllBytes(unicodeSecond.Sbom)); Assert.Equal(File.ReadAllBytes(unicodeFirst.Manifest), File.ReadAllBytes(unicodeSecond.Manifest)); using var sbomJson = JsonDocument.Parse(File.ReadAllText(unicodeFirst.Sbom)); using var manifestJson = JsonDocument.Parse(File.ReadAllText(unicodeFirst.Manifest)); var components = sbomJson.RootElement.GetProperty("components").EnumerateArray().Where(c => c.GetProperty("name").GetString() is "Café Package" or "café Package").ToArray(); Assert.Equal(2, components.Length); var files = components.SelectMany(c => c.GetProperty("components").EnumerateArray().Select(f => (Owner: c.GetProperty("bom-ref").GetString()!, Path: f.GetProperty("name").GetString()!, Ref: f.GetProperty("bom-ref").GetString()!))).OrderBy(f => f.Path, StringComparer.Ordinal).ToArray(); Assert.Equal(new[] { "one/Café File.dll", "two/café File.dll" }, files.Select(f => f.Path)); Assert.NotEqual(files[0].Owner, files[1].Owner); Assert.NotEqual(files[0].Ref, files[1].Ref); var artifacts = manifestJson.RootElement.GetProperty("artifacts").EnumerateArray().Where(a => files.Select(f => f.Path).Contains(a.GetProperty("path").GetString())).ToArray(); Assert.Equal(2, artifacts.Length); Assert.All(files, file => Assert.Contains(artifacts, a => a.GetProperty("path").GetString() == file.Path && a.GetProperty("ownerBomRef").GetString() == file.Owner));
        Fails(node => node["targets"]!["net8.0-windows7.0/win-x64"]!["AIBar.Desktop/1.0.0"]!["dependencies"]!.AsObject().Clear(), "project.assets.json");
        Fails(node => node["targets"]![".NETCoreApp,Version=v8.0/win-x64"]!["Newtonsoft.Json/13.0.3"]!["runtime"]!["foo.dll"] = new JsonObject(), "AIBar.Desktop.deps.json");
        Fails(node => node["components"]!["Newtonsoft.Json/13.0.3"]!["componentType"] = "corrupt", "publish-inventory.json"); Fails(node => node["files"]!["Newtonsoft.Json.dll"] = "corrupt", "publish-inventory.json");
        Fails(node => node["components"]!.AsObject().Remove("Newtonsoft.Json/13.0.3"), "publish-inventory.json"); Fails(node => node["components"]!["Extra/1.0"] = node["components"]!["Newtonsoft.Json/13.0.3"]!.DeepClone(), "publish-inventory.json"); Fails(node => node["components"]!["Newtonsoft.Json/13.0.3"]!["testLeakage"] = true, "publish-inventory.json"); Fails(node => node["components"]!["Newtonsoft.Json/13.0.3"]!["root"] = true, "publish-inventory.json");
        Fails(node => node["artifacts"]!.AsArray().RemoveAt(0), "publish-inventory.json"); Fails(node => node["artifacts"]!.AsArray().Add(node["artifacts"]![0]!.DeepClone()), "publish-inventory.json"); Fails(node => node["artifacts"]!.AsArray().Add(new JsonObject { ["path"] = "extra.dll", ["length"] = 1, ["sha256"] = new string('0', 64) }), "publish-inventory.json");
        Fails(node => node["artifacts"]![0]! ["path"] = "aibar.desktop.dll", "publish-inventory.json"); Fails(node => node["artifacts"]![0]! ["length"] = 99, "publish-inventory.json"); Fails(node => node["artifacts"]![0]! ["sha256"] = new string('0', 64), "publish-inventory.json");
        Fails(node => node["artifacts"]!.AsArray().Add(new JsonObject { ["path"] = "aibar.desktop.dll", ["length"] = 8, ["sha256"] = "e1eb72e080d8498fb7988921455ab4e66cc25cefd4b1a802446ed9e798e18b8b" }), "publish-inventory.json");
        Fails(node => node.AsArray()[0]! ["sha256"] = new string('1', 64), "recovery-inventory.json"); Fails(node => { var target = node["targets"]!["net8.0-windows7.0/win-x64"]!.AsObject(); target["Test.Library/1.0.0"] = target["Newtonsoft.Json/13.0.3"]!.DeepClone(); target.Remove("Newtonsoft.Json/13.0.3"); }, "project.assets.json");
        using var bytes = FixtureCopy("bytes"); File.AppendAllText(Path.Combine(bytes.Path, "sources", "Newtonsoft.Json.dll"), "x"); Assert.NotEqual(0, Graph(bytes.Path).ExitCode);
    }

    private static void Fails(Action<JsonNode> mutate, string file) { using var root = FixtureCopy("fault"); var path = Path.Combine(root.Path, file); var node = JsonNode.Parse(File.ReadAllText(path))!; mutate(node); File.WriteAllText(path, node.ToJsonString()); Assert.NotEqual(0, Graph(root.Path).ExitCode); }
    private static void UnicodeNames(string root) { var assetsPath = Path.Combine(root, "project.assets.json"); var assets = JsonNode.Parse(File.ReadAllText(assetsPath))!.AsObject(); var target = assets["targets"]!["net8.0-windows7.0/win-x64"]!.AsObject(); var desktop = target["AIBar.Desktop/1.0.0"]!.AsObject(); desktop["dependencies"]!["Café Package"] = desktop["dependencies"]!["Newtonsoft.Json"]!.DeepClone(); desktop["dependencies"]!.AsObject().Remove("Newtonsoft.Json"); target["Café Package/13.0.3"] = target["Newtonsoft.Json/13.0.3"]!.DeepClone(); target.Remove("Newtonsoft.Json/13.0.3"); var runtime = target["Runtime.Pack/8.0.0"]!.AsObject(); runtime["dependencies"]!["café Package"] = runtime["dependencies"]!["System.Runtime"]!.DeepClone(); runtime["dependencies"]!.AsObject().Remove("System.Runtime"); target["café Package/8.0.0"] = target["System.Runtime/8.0.0"]!.DeepClone(); target.Remove("System.Runtime/8.0.0"); File.WriteAllText(assetsPath, assets.ToJsonString()); var depsPath = Path.Combine(root, "AIBar.Desktop.deps.json"); var deps = JsonNode.Parse(File.ReadAllText(depsPath))!.AsObject(); var depsTarget = deps["targets"]![".NETCoreApp,Version=v8.0/win-x64"]!.AsObject(); var package = depsTarget["Newtonsoft.Json/13.0.3"]!.AsObject(); package["runtime"]!["one/Café File.dll"] = package["runtime"]!["Newtonsoft.Json.dll"]!.DeepClone(); package["runtime"]!.AsObject().Remove("Newtonsoft.Json.dll"); depsTarget["Café Package/13.0.3"] = package.DeepClone(); depsTarget.Remove("Newtonsoft.Json/13.0.3"); var lower = depsTarget["System.Runtime/8.0.0"]!.AsObject(); lower["runtime"]!["two/café File.dll"] = lower["runtime"]!["System.Runtime.dll"]!.DeepClone(); lower["runtime"]!.AsObject().Remove("System.Runtime.dll"); depsTarget["café Package/8.0.0"] = lower.DeepClone(); depsTarget.Remove("System.Runtime/8.0.0"); File.WriteAllText(depsPath, deps.ToJsonString()); var projectionPath = Path.Combine(root, "publish-inventory.json"); var projection = JsonNode.Parse(File.ReadAllText(projectionPath))!.AsObject(); var policyComponents = projection["components"]!.AsObject(); policyComponents["Café Package/13.0.3"] = policyComponents["Newtonsoft.Json/13.0.3"]!.DeepClone(); policyComponents.Remove("Newtonsoft.Json/13.0.3"); policyComponents["café Package/8.0.0"] = policyComponents["System.Runtime/8.0.0"]!.DeepClone(); policyComponents.Remove("System.Runtime/8.0.0"); var policyFiles = projection["files"]!.AsObject(); policyFiles["one/Café File.dll"] = policyFiles["Newtonsoft.Json.dll"]!.DeepClone(); policyFiles.Remove("Newtonsoft.Json.dll"); policyFiles["two/café File.dll"] = policyFiles["System.Runtime.dll"]!.DeepClone(); policyFiles.Remove("System.Runtime.dll"); var artifacts = projection["artifacts"]!.AsArray(); artifacts.Single(n => n!["path"]!.GetValue<string>() == "Newtonsoft.Json.dll")!["path"] = "one/Café File.dll"; artifacts.Single(n => n!["path"]!.GetValue<string>() == "System.Runtime.dll")!["path"] = "two/café File.dll"; File.WriteAllText(projectionPath, projection.ToJsonString()); var recoveryPath = Path.Combine(root, "recovery-inventory.json"); var recovery = JsonNode.Parse(File.ReadAllText(recoveryPath))!.AsArray(); recovery.Single(n => n!["path"]!.GetValue<string>() == "Newtonsoft.Json.dll")!["path"] = "one/Café File.dll"; recovery.Single(n => n!["path"]!.GetValue<string>() == "System.Runtime.dll")!["path"] = "two/café File.dll"; File.WriteAllText(recoveryPath, recovery.ToJsonString()); foreach (var basePath in new[] { "publish", "sources" }) { Directory.CreateDirectory(Path.Combine(root, basePath, "one")); Directory.CreateDirectory(Path.Combine(root, basePath, "two")); File.Move(Path.Combine(root, basePath, "Newtonsoft.Json.dll"), Path.Combine(root, basePath, "one", "Café File.dll")); File.Move(Path.Combine(root, basePath, "System.Runtime.dll"), Path.Combine(root, basePath, "two", "café File.dll")); } }
    private static (int ExitCode, string Sbom, string Manifest) Graph(string fixtures) { var sbom = Path.Combine(fixtures, "sbom.cdx.json"); var manifest = Path.Combine(fixtures, "compliance-manifest.json"); var result = RunGraph(fixtures, sbom, manifest); return (result.ExitCode, sbom, manifest); }
    private static (int ExitCode, string Output) RunGraph(string fixtures, string sbom, string manifest) { var start = new ProcessStartInfo("pwsh") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(RepositoryRoot(), "scripts", "Publish-Deterministic.ps1"), "-GraphProjection", "-FixtureDirectory", fixtures, "-SbomOutput", sbom, "-ComplianceOutput", manifest }) start.ArgumentList.Add(argument); using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit(); return (process.ExitCode, output); }
    private static TemporaryRoot FixtureCopy(string suffix) { var root = new TemporaryRoot(suffix); Copy( Path.Combine(RepositoryRoot(), "tests", "AIBar.Domain.Tests", "Fixtures", "PackagingGraph"), root.Path); return root; }
    private static void Copy(string source, string target) { foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var output = Path.Combine(target, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(file, output); } }

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
    private sealed class TemporaryRoot : IDisposable { public string Path { get; } public TemporaryRoot(string suffix = "root") => Path = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aibar-8c2a-{suffix}-{Guid.NewGuid():N}")).FullName; public void Dispose() => Directory.Delete(Path, true); }
}
