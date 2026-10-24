using System.IO;
using System.Security.Cryptography;

namespace AIBar.Desktop;

internal enum GlyphFontInstallStatus { Installed, AlreadyInstalled, SourceMissing, SourceMismatch, Failed }
internal sealed record GlyphFontInstallResult(GlyphFontInstallStatus Status, string? ExceptionType = null);
internal sealed record GlyphFontRegistration(string Name, string? FilePath);

// All machine/user state and native calls are replaceable; tests never install a real font.
internal interface IGlyphFontEnvironment
{
    string WindowsFontsDirectory { get; }
    string UserFontsDirectory { get; }
    IEnumerable<GlyphFontRegistration> ReadFontRegistrations(bool currentUser);
    bool FileExists(string path);
    Stream OpenRead(string path);
    void CreateDirectory(string path);
    void CopyFile(string source, string destination);
    void MoveFile(string source, string destination);
    void DeleteFile(string path);
    void WriteUserFontRegistration(string name, string path);
    void AddFontResource(string path);
    void BroadcastFontChange();
}

internal sealed class GlyphFontInstaller
{
    internal const string FamilyName = "IosevkaTerm Nerd Font";
    internal const string FileName = "IosevkaTermNerdFont-Regular.ttf";
    internal const string Sha256 = "5c12e7d30e97ff997c10e9b0acb2ed7f8495134e0f33b1176136f9e6d36d8e00";
    internal const string RegistryValueName = "IosevkaTerm Nerd Font Regular (TrueType)";
    internal static IReadOnlyList<int> RequiredCodepoints { get; } = Array.AsReadOnly(new[]
    {
        0xF0766, 0xF0A9E, 0xF0A9F, 0xF0AA0, 0xF0AA1, 0xF0AA2, 0xF0AA3, 0xF0AA4, 0xF0AA5
    });

    private readonly IGlyphFontEnvironment _environment;
    private readonly string _expectedSha256;

    internal GlyphFontInstaller() : this(new GlyphFontWindowsEnvironment(), Sha256) { }
    internal GlyphFontInstaller(IGlyphFontEnvironment environment) : this(environment, Sha256) { }
    // Internal seam for synthetic bytes. Production composition uses the parameterless constructor.
    internal GlyphFontInstaller(IGlyphFontEnvironment environment, string expectedSha256)
    {
        _environment = environment;
        _expectedSha256 = expectedSha256;
    }

    internal static string BundledFontPath(string appDirectory) => Path.Combine(appDirectory, "fonts", FileName);

    internal bool IsInstalled()
    {
        foreach (var currentUser in new[] { true, false })
        {
            try
            {
                foreach (var entry in _environment.ReadFontRegistrations(currentUser))
                {
                    if (!entry.Name.StartsWith(FamilyName, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(entry.FilePath)) continue;
                    var path = Path.IsPathFullyQualified(entry.FilePath)
                        ? entry.FilePath : Path.Combine(_environment.WindowsFontsDirectory, entry.FilePath);
                    if (_environment.FileExists(path)) return true;
                }
            }
            catch (Exception) { /* An unreadable hive must not prevent checking the other hive. */ }
        }
        return false;
    }

    internal bool IsBundledFontAvailable(string appDirectory)
    {
        try { return Matches(BundledFontPath(appDirectory)); }
        catch (Exception) { return false; }
    }

    private bool Matches(string path)
    {
        if (!_environment.FileExists(path)) return false;
        using var stream = _environment.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), _expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    internal GlyphFontInstallResult Install(string sourcePath)
    {
        string? temporary = null;
        try
        {
            // Even an installed machine must reject an invalid requested source without side effects.
            if (!_environment.FileExists(sourcePath)) return new(GlyphFontInstallStatus.SourceMissing);
            if (!Matches(sourcePath)) return new(GlyphFontInstallStatus.SourceMismatch);
            if (IsInstalled()) return new(GlyphFontInstallStatus.AlreadyInstalled);

            var destination = Path.Combine(_environment.UserFontsDirectory, FileName);
            var copied = false;
            if (!Matches(destination))
            {
                _environment.CreateDirectory(_environment.UserFontsDirectory);
                temporary = Path.Combine(_environment.UserFontsDirectory, $".{FileName}.{Guid.NewGuid():N}.tmp");
                _environment.CopyFile(sourcePath, temporary);
                // Verify copied bytes too, before replacing any destination or writing the registry.
                if (!Matches(temporary)) return new(GlyphFontInstallStatus.SourceMismatch);
                _environment.MoveFile(temporary, destination);
                temporary = null;
                copied = true;
            }
            // Register only a font Windows accepted; a failure leaves nothing that IsInstalled would count, so the user can retry.
            try
            {
                _environment.AddFontResource(destination);
                _environment.WriteUserFontRegistration(RegistryValueName, destination);
            }
            catch (Exception) when (copied)
            {
                try { _environment.DeleteFile(destination); } catch (Exception) { /* A loaded font stays locked until sign-out; it is unregistered either way. */ }
                throw;
            }
            // Telling running applications is best effort: the font is already installed and registered.
            try { _environment.BroadcastFontChange(); } catch (Exception) { }
            return new(GlyphFontInstallStatus.Installed);
        }
        catch (Exception exception) { return new(GlyphFontInstallStatus.Failed, exception.GetType().Name); }
        finally
        {
            if (temporary is not null)
            {
                try { _environment.DeleteFile(temporary); } catch (Exception) { /* Best effort for this owned temporary only. */ }
            }
        }
    }
}
