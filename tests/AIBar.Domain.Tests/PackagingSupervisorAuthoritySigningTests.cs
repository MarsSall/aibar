using System.Security.Cryptography;

namespace AIBar.Domain.Tests;

public sealed class PackagingSupervisorAuthoritySigningTests
{
    private const string ExpectedToken = "0540e36870da7bad";
    private const string ExpectedPublicIdentityHash = "31484e36803555c161e7169682a8cc113ffd924b10866fd35f80c22cacd3e7aa";

    [Fact]
    public void Signing_policy_checks_in_public_material_and_fails_closed_for_missing_official_private_material()
    {
        var root = FindRepositoryRoot();
        var props = File.ReadAllText(Path.Combine(root, "eng", "signing", "AIBar.Signing.props"));
        var policy = File.ReadAllText(Path.Combine(root, "eng", "signing", "SigningPolicy.md"));
        var publicKey = File.ReadAllBytes(Path.Combine(root, "eng", "signing", "AIBar.PublicKey.snk"));

        Assert.Equal((byte)6, publicKey[0]);
        using var rsa = new RSACryptoServiceProvider();
        rsa.ImportCspBlob(publicKey);
        Assert.Throws<CryptographicException>(() => rsa.ExportCspBlob(true));
        Assert.Contains("<SignAssembly>true</SignAssembly>", props, StringComparison.Ordinal);
        Assert.Contains("<PublicSign Condition=\"'$(AIBarOfficialSigning)' != 'true'\">true</PublicSign>", props, StringComparison.Ordinal);
        Assert.Contains("Official signing requires externally supplied private key material.", props, StringComparison.Ordinal);
        Assert.Contains("Official signing private key material is unavailable.", props, StringComparison.Ordinal);
        Assert.Contains(ExpectedToken, policy, StringComparison.Ordinal);
        Assert.Contains(ExpectedPublicIdentityHash, policy, StringComparison.Ordinal);
        Assert.Contains("does not provide a security sandbox", policy, StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_policy_requires_identity_equality_before_official_publish()
    {
        var policy = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "eng", "signing", "SigningPolicy.md"));

        Assert.Contains("CI-only", policy, StringComparison.Ordinal);
        Assert.Contains("public identity and token equal", policy, StringComparison.Ordinal);
        Assert.Contains("Missing private material", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN PRIVATE", policy, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AIBar.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
