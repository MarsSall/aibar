using System.Security.Cryptography;
using System.Text;
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

public sealed class LocalUsageDiscoveryResult
{
    internal LocalUsageDiscoveryResult(IEnumerable<LocalUsageDiscoverySummary> sources, IEnumerable<LocalUsageDiscoveredCandidate> candidates)
    {
        Sources = Array.AsReadOnly(sources.ToArray()); Candidates = Array.AsReadOnly(candidates.ToArray());
        Registrations = Array.AsReadOnly(Candidates.Where(candidate => Sources.Any(source => source.Tool == candidate.Tool && source.Status == LocalUsageDiscoveryStatus.Ready)).Select(candidate => candidate.Register()).ToArray());
    }
    public IReadOnlyList<LocalUsageDiscoverySummary> Sources { get; }
    internal IReadOnlyList<LocalUsageDiscoveredCandidate> Candidates { get; }
    internal IReadOnlyList<LocalUsageSourceRegistration> Registrations { get; }
    internal PiUsageFanInInput CreatePiFanInInput()
    {
        var source = Sources.SingleOrDefault(item => item.Tool == UsageTool.Pi);
        if (source?.Status is not (LocalUsageDiscoveryStatus.Ready or LocalUsageDiscoveryStatus.RequiresFanIn))
            throw new InvalidOperationException("Pi discovery is not ready for fan-in.");
        var candidates = Candidates.Where(item => item.Tool == UsageTool.Pi).ToArray();
        if (candidates.Length != source.CandidateCount || source.Status == LocalUsageDiscoveryStatus.Ready && candidates.Length != 1
            || source.Status == LocalUsageDiscoveryStatus.RequiresFanIn && candidates.Length < 2) throw new ArgumentException("Pi discovery candidate count is inconsistent.");
        return new(candidates.Select(item => item.Register()));
    }
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

public sealed class LocalUsageSourceDiscovery
{
    public const int MaximumEntries = 512;
    public const int MaximumCandidates = 128;
    public const int MaximumDepth = 2;
    public const int MaximumPathCharacters = 1024;
    private readonly ILocalUsageDiscoveryFileSystem _fileSystem;
    public LocalUsageSourceDiscovery() : this(new PhysicalLocalUsageDiscoveryFileSystem()) { }
    internal LocalUsageSourceDiscovery(ILocalUsageDiscoveryFileSystem fileSystem) => _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public LocalUsageDiscoveryResult Discover(LocalUsagePolicy policy, LocalUsageDiscoveryFacts facts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(facts); cancellationToken.ThrowIfCancellationRequested();
        var summaries = new List<LocalUsageDiscoverySummary>(2); var candidates = new List<LocalUsageDiscoveredCandidate>();
        DiscoverOne(UsageTool.OpenCode, policy.OpenCodeEnabled, () => OpenCode(facts.OpenCodeDataRoot, facts.IdentitySalt(), cancellationToken), summaries, candidates, cancellationToken);
        DiscoverOne(UsageTool.Pi, policy.PiEnabled, () => Pi(facts.PiSessionsRoot, facts.PiLayout, facts.IdentitySalt(), cancellationToken), summaries, candidates, cancellationToken);
        return new(summaries, candidates);
    }

    private static void DiscoverOne(UsageTool tool, bool enabled, Func<(LocalUsageDiscoverySummary Summary, IReadOnlyList<LocalUsageDiscoveredCandidate> Candidates)> discover,
        List<LocalUsageDiscoverySummary> summaries, List<LocalUsageDiscoveredCandidate> candidates, CancellationToken token)
    {
        if (!enabled) { summaries.Add(Summary(tool, LocalUsageDiscoveryStatus.Disabled)); return; }
        try { var result = discover(); summaries.Add(result.Summary); candidates.AddRange(result.Candidates); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (Operational(exception)) { summaries.Add(Summary(tool, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.Unreadable])); }
    }

    private (LocalUsageDiscoverySummary, IReadOnlyList<LocalUsageDiscoveredCandidate>) OpenCode(string? input, byte[] salt, CancellationToken token)
    {
        if (!Root(input, out var root)) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.InvalidRoot]), []);
        var rootInfo = _fileSystem.Inspect(root, token);
        if (rootInfo.IsReparsePoint) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.ReparsePoint]), []);
        if (rootInfo.Kind == LocalUsagePathKind.Missing) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.NotFound]), []);
        if (rootInfo.Kind != LocalUsagePathKind.Directory) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.InvalidRoot]), []);
        if (HasReparseParent(root, token)) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.ReparsePoint]), []);
        var path = Path.GetFullPath(Path.Combine(root, "opencode.db"));
        if (path.Length > MaximumPathCharacters || !Direct(root, path, out path)) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.BoundsExceeded]), []);
        var info = _fileSystem.Inspect(path, token);
        if (info.IsReparsePoint) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, skipped: 1, warnings: [LocalUsageDiscoveryWarning.ReparsePoint]), []);
        if (info.Kind != LocalUsagePathKind.File) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.NotFound]), []);
        var candidate = Candidate(UsageTool.OpenCode, root, path, LocalUsageAdmissionLayout.Direct, salt, token);
        if (candidate is null) return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.PathRejected]), []);
        return (Summary(UsageTool.OpenCode, LocalUsageDiscoveryStatus.Ready, 1), [candidate]);
    }

    private (LocalUsageDiscoverySummary, IReadOnlyList<LocalUsageDiscoveredCandidate>) Pi(string? input, PiUsageDiscoveryLayout layout, byte[] salt, CancellationToken token)
    {
        if (!Root(input, out var root)) return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.InvalidRoot]), []);
        var rootInfo = _fileSystem.Inspect(root, token);
        if (rootInfo.IsReparsePoint) return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.ReparsePoint]), []);
        if (rootInfo.Kind == LocalUsagePathKind.Missing) return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.NotFound]), []);
        if (rootInfo.Kind != LocalUsagePathKind.Directory) return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.InvalidRoot]), []);
        if (HasReparseParent(root, token)) return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, warnings: [LocalUsageDiscoveryWarning.ReparsePoint]), []);
        var warnings = new HashSet<LocalUsageDiscoveryWarning>(); var paths = new List<string>(); var skipped = 0; var entries = 0; var complete = true;
        var directories = layout == PiUsageDiscoveryLayout.DirectCustom ? [root] : List(root, ref entries, warnings, token, ref complete);
        if (!complete) return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, skipped: skipped, warnings: warnings), []);
        foreach (var rawDirectory in directories.Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (!Direct(root, rawDirectory, out var directory) && layout != PiUsageDiscoveryLayout.DirectCustom) { warnings.Add(LocalUsageDiscoveryWarning.PathRejected); skipped++; continue; }
            if (layout == PiUsageDiscoveryLayout.DefaultEncodedDirectories)
            {
                var name = Path.GetFileName(directory);
                if (name.Length < 4 || !name.StartsWith("--", StringComparison.Ordinal) || !name.EndsWith("--", StringComparison.Ordinal)) { warnings.Add(LocalUsageDiscoveryWarning.UnsupportedLayout); skipped++; continue; }
                LocalUsagePathMetadata directoryInfo; try { directoryInfo = _fileSystem.Inspect(directory, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; } catch (Exception exception) when (Operational(exception)) { warnings.Add(LocalUsageDiscoveryWarning.Unreadable); skipped++; complete = false; continue; }
                if (directoryInfo.IsReparsePoint) { warnings.Add(LocalUsageDiscoveryWarning.ReparsePoint); skipped++; continue; }
                if (directoryInfo.Kind != LocalUsagePathKind.Directory) { warnings.Add(LocalUsageDiscoveryWarning.UnsupportedLayout); skipped++; continue; }
            }
            var files = List(directory, ref entries, warnings, token, ref complete);
            if (!complete) break;
            foreach (var rawFile in files.Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (!Direct(directory, rawFile, out var file)) { warnings.Add(LocalUsageDiscoveryWarning.PathRejected); skipped++; continue; }
                LocalUsagePathMetadata info; try { info = _fileSystem.Inspect(file, token); } catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; } catch (Exception exception) when (Operational(exception)) { warnings.Add(LocalUsageDiscoveryWarning.Unreadable); skipped++; complete = false; continue; }
                if (info.IsReparsePoint) { warnings.Add(LocalUsageDiscoveryWarning.ReparsePoint); skipped++; continue; }
                if (info.Kind != LocalUsagePathKind.File || !file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) { warnings.Add(LocalUsageDiscoveryWarning.UnsupportedLayout); skipped++; continue; }
                paths.Add(file); if (paths.Count > MaximumCandidates) { warnings.Add(LocalUsageDiscoveryWarning.BoundsExceeded); complete = false; break; }
            }
            if (!complete && warnings.Contains(LocalUsageDiscoveryWarning.BoundsExceeded)) break;
        }
        var admitted = paths.Order(StringComparer.Ordinal).Select(path => Candidate(UsageTool.Pi, root, path,
            layout == PiUsageDiscoveryLayout.DirectCustom ? LocalUsageAdmissionLayout.Direct : LocalUsageAdmissionLayout.PiEncodedDirectory, salt, token)).ToArray();
        if (!complete || admitted.Any(candidate => candidate is null)) { warnings.Add(LocalUsageDiscoveryWarning.PathRejected); return (Summary(UsageTool.Pi, LocalUsageDiscoveryStatus.Unavailable, paths.Count, skipped, warnings), []); }
        var ordered = admitted.Select(candidate => candidate!).ToArray();
        if (ordered.Length == 0) warnings.Add(LocalUsageDiscoveryWarning.NotFound);
        var status = ordered.Length switch { 0 => LocalUsageDiscoveryStatus.Unavailable, 1 => LocalUsageDiscoveryStatus.Ready, _ => LocalUsageDiscoveryStatus.RequiresFanIn };
        return (Summary(UsageTool.Pi, status, ordered.Length, skipped, warnings), ordered);
    }

    private IReadOnlyList<string> List(string directory, ref int entries, HashSet<LocalUsageDiscoveryWarning> warnings, CancellationToken token, ref bool complete)
    {
        try
        {
            var result = new List<string>();
            foreach (var path in _fileSystem.Enumerate(directory, token))
            { if (++entries > MaximumEntries || path.Length > MaximumPathCharacters) { warnings.Add(LocalUsageDiscoveryWarning.BoundsExceeded); complete = false; break; } result.Add(path); }
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) when (Operational(exception)) { warnings.Add(LocalUsageDiscoveryWarning.Unreadable); complete = false; return []; }
    }

    private static bool Root(string? input, out string root)
    {
        root = "";
        try
        {
            if (string.IsNullOrWhiteSpace(input) || input != input.Trim() || input.Length > MaximumPathCharacters || !Path.IsPathFullyQualified(input)) return false;
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            var volumeRoot = Path.GetPathRoot(root) ?? "";
            return root.Length <= MaximumPathCharacters && !StringComparer.OrdinalIgnoreCase.Equals(root, volumeRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    private static bool Direct(string parent, string input, out string path)
    {
        path = ""; if (input.Length > MaximumPathCharacters) return false;
        try { path = Path.GetFullPath(input); return path.Length <= MaximumPathCharacters && StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(path), parent); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
    private bool HasReparseParent(string path, CancellationToken token)
    {
        for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
            if (_fileSystem.Inspect(parent, token).IsReparsePoint) return true;
        return false;
    }
    private static bool Operational(Exception exception) => exception is IOException or UnauthorizedAccessException;
    private LocalUsageDiscoveredCandidate? Candidate(UsageTool tool, string root, string path, LocalUsageAdmissionLayout layout, byte[] salt, CancellationToken token)
    {
        var admission = LocalUsageSourceAdmission.Capture(_fileSystem, root, path, layout, token);
        if (admission is null) return null;
        var identity = CreateIdentity(tool, path, salt);
        var adapter = tool == UsageTool.OpenCode ? LocalUsageAdapterKind.OpenCodeSqliteV1 : LocalUsageAdapterKind.PiJsonlV3;
        var schema = tool == UsageTool.OpenCode ? OpenCodeSqliteUsageSourceAdapter.SupportedSourceSchemaVersion : PiJsonlUsageSourceAdapter.SupportedSourceSchemaVersion;
        return new(tool, adapter, path, identity, schema, admission);
    }
    internal static UsageSourceIdentity CreateIdentity(UsageTool tool, string path, byte[] salt)
    {
        if (!Enum.IsDefined(tool) || salt.Length != 32) throw new ArgumentException("A valid tool and 32-byte salt are required.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("aibar-local-usage-source-v1\0")); hash.AppendData(salt);
        var canonical = Path.GetFullPath(path).Replace('\\', '/');
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        hash.AppendData(Encoding.UTF8.GetBytes($"\0{tool.ToString().ToLowerInvariant()}\0{canonical}"));
        return new(hash.GetHashAndReset());
    }
    private static LocalUsageDiscoverySummary Summary(UsageTool tool, LocalUsageDiscoveryStatus status, int candidates = 0, int skipped = 0, IEnumerable<LocalUsageDiscoveryWarning>? warnings = null)
        => new(tool, status, candidates, skipped, warnings ?? []);
}
