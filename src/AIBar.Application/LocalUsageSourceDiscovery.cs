using AIBar.Domain;

namespace AIBar.Application;

public enum LocalUsageDiscoveryStatus { Disabled = 1, Unavailable = 2, Ready = 3, RequiresFanIn = 4 }
public enum LocalUsageDiscoveryWarning { InvalidRoot = 1, NotFound = 2, Unreadable = 3, ReparsePoint = 4, PathRejected = 5, UnsupportedLayout = 6, BoundsExceeded = 7 }
public enum PiUsageDiscoveryLayout { DefaultEncodedDirectories = 1, DirectCustom = 2 }

public sealed class LocalUsageDiscoverySummary
{
    internal LocalUsageDiscoverySummary(UsageTool tool, LocalUsageDiscoveryStatus status, int candidates, int skipped, IEnumerable<LocalUsageDiscoveryWarning> warnings)
    { Tool = tool; Status = status; CandidateCount = candidates; SkippedCount = skipped; WarningCodes = Array.AsReadOnly(warnings.Distinct().Order().ToArray()); }
    public UsageTool Tool { get; }
    public LocalUsageDiscoveryStatus Status { get; }
    public int CandidateCount { get; }
    public int SkippedCount { get; }
    public IReadOnlyList<LocalUsageDiscoveryWarning> WarningCodes { get; }
}
public sealed class LocalUsageDiscoveryFacts
{
    private readonly byte[] _identitySalt;
    public LocalUsageDiscoveryFacts(string? openCodeDataRoot, string? piSessionsRoot, byte[] identitySalt, PiUsageDiscoveryLayout piLayout = PiUsageDiscoveryLayout.DefaultEncodedDirectories)
    {
        ArgumentNullException.ThrowIfNull(identitySalt);
        if (identitySalt.Length != 32) throw new ArgumentException("The installation identity salt must be exactly 32 bytes.", nameof(identitySalt));
        if (!Enum.IsDefined(piLayout)) throw new ArgumentOutOfRangeException(nameof(piLayout));
        OpenCodeDataRoot = openCodeDataRoot; PiSessionsRoot = piSessionsRoot; PiLayout = piLayout; _identitySalt = identitySalt.ToArray();
    }
    public string? OpenCodeDataRoot { get; }
    public string? PiSessionsRoot { get; }
    public PiUsageDiscoveryLayout PiLayout { get; }
    internal byte[] IdentitySalt() => _identitySalt.ToArray();
}

internal enum LocalUsagePathKind { Missing, File, Directory, Other }
internal readonly record struct LocalUsageFileIdentity(ulong Volume, Guid Identifier);
internal readonly record struct LocalUsagePathMetadata(LocalUsagePathKind Kind, bool IsReparsePoint = false, LocalUsageFileIdentity? Identity = null);
internal interface ILocalUsageDiscoveryFileSystem
{
    LocalUsagePathMetadata Inspect(string path, CancellationToken token);
    IEnumerable<string> Enumerate(string directory, CancellationToken token);
}

internal sealed class PhysicalLocalUsageDiscoveryFileSystem : ILocalUsageDiscoveryFileSystem
{
    public LocalUsagePathMetadata Inspect(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return WindowsLocalUsageFileMetadata.Inspect(path);
    }
    public IEnumerable<string> Enumerate(string directory, CancellationToken token)
    { foreach (var path in Directory.EnumerateFileSystemEntries(directory)) { token.ThrowIfCancellationRequested(); yield return path; } }
}

internal sealed class LocalUsageDiscoveredCandidate
{
    internal LocalUsageDiscoveredCandidate(UsageTool tool, LocalUsageAdapterKind adapter, string path, UsageSourceIdentity identity, int sourceSchemaVersion, ILocalUsageSourceAdmission admission)
    { Tool = tool; Adapter = adapter; Path = path; Identity = identity; SourceSchemaVersion = sourceSchemaVersion; Admission = admission; }
    internal UsageTool Tool { get; }
    internal LocalUsageAdapterKind Adapter { get; }
    internal string Path { get; }
    internal UsageSourceIdentity Identity { get; }
    internal int SourceSchemaVersion { get; }
    private ILocalUsageSourceAdmission Admission { get; }
    internal LocalUsageSourceRegistration Register() => new(Tool, Adapter, Path, Identity, SourceSchemaVersion, Admission);
    public override string ToString() => $"{Tool} usage candidate (private)";
}
