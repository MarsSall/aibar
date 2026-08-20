using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageSourceDiscoveryTests
{
    private const string OpenRoot = @"C:\synthetic\opencode";
    private const string PiRoot = @"C:\synthetic\pi\sessions";
    private static readonly byte[] Salt = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void Disabled_tools_make_no_filesystem_calls()
    {
        var fs = new SyntheticFileSystem();
        var result = new LocalUsageSourceDiscovery(fs).Discover(LocalUsagePolicy.Disabled, new(null, "relative", Salt));
        Assert.Equal(0, fs.Calls); Assert.All(result.Sources, source => Assert.Equal(LocalUsageDiscoveryStatus.Disabled, source.Status));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative")]
    [InlineData(@"C:\")]
    public void Invalid_roots_fail_closed_without_filesystem_access(string? root)
    {
        var fs = new SyntheticFileSystem();
        var result = new LocalUsageSourceDiscovery(fs).Discover(new(true, false), new(root, null, Salt));
        Assert.Equal(0, fs.Calls); Assert.Contains(LocalUsageDiscoveryWarning.InvalidRoot, result.Sources[0].WarningCodes);
    }

    [Fact]
    public void OpenCode_inspects_only_the_allowlisted_database_and_builds_a_valid_registration()
    {
        var database = Path.Combine(OpenRoot, "opencode.db");
        var fs = new SyntheticFileSystem().Entry(OpenRoot, LocalUsagePathKind.Directory).Entry(database, LocalUsagePathKind.File);
        var result = new LocalUsageSourceDiscovery(fs).Discover(new(true, false), new(OpenRoot, null, Salt));
        var source = result.Sources[0]; var registration = Assert.Single(result.Registrations);
        Assert.Equal(LocalUsageDiscoveryStatus.Ready, source.Status); Assert.Equal(1, source.CandidateCount);
        Assert.Contains(OpenRoot, fs.Inspected); Assert.Contains(database, fs.Inspected); Assert.Equal(0, fs.Enumerations);
        Assert.Equal(LocalUsageAdapterKind.OpenCodeSqliteV1, registration.Adapter); Assert.Equal(1, registration.SourceSchemaVersion); Assert.Equal(LocalUsageRegistrationAdmissionPolicy.DiscoveryProof, registration.AdmissionPolicy);
    }

    [Fact]
    public void Pi_returns_deterministic_candidates_and_requires_fan_in_instead_of_choosing_one()
    {
        var a = Path.Combine(PiRoot, "--A--"); var b = Path.Combine(PiRoot, "--B--");
        var first = Path.Combine(a, "1.jsonl"); var second = Path.Combine(b, "2.jsonl");
        var fs = new SyntheticFileSystem().Directory(PiRoot, b, a).Directory(a, first).Directory(b, second)
            .Entry(first, LocalUsagePathKind.File).Entry(second, LocalUsagePathKind.File);
        var result = new LocalUsageSourceDiscovery(fs).Discover(new(false, true), new(null, PiRoot, Salt));
        var source = result.Sources[1];
        Assert.Equal(LocalUsageDiscoveryStatus.RequiresFanIn, source.Status); Assert.Equal(2, source.CandidateCount);
        Assert.Equal([first, second], result.Candidates.Select(candidate => candidate.Path)); Assert.Empty(result.Registrations);

        var directFirst = Path.Combine(PiRoot, "1.jsonl"); var directSecond = Path.Combine(PiRoot, "2.jsonl");
        var direct = new SyntheticFileSystem().Directory(PiRoot, directSecond, a, directFirst).Entry(directFirst, LocalUsagePathKind.File).Entry(directSecond, LocalUsagePathKind.File);
        var custom = new LocalUsageSourceDiscovery(direct).Discover(new(false, true), new(null, PiRoot, Salt, PiUsageDiscoveryLayout.DirectCustom));
        Assert.Equal([directFirst, directSecond], custom.Candidates.Select(candidate => candidate.Path)); Assert.Equal(1, direct.Enumerations); Assert.Empty(custom.Registrations);
    }

    [Fact]
    public void Pi_single_candidate_registers_v3_and_reparse_escape_or_unsupported_entries_are_skipped()
    {
        var project = Path.Combine(PiRoot, "--project--"); var file = Path.Combine(project, "session.jsonl"); var link = Path.Combine(PiRoot, "--link--");
        var fs = new SyntheticFileSystem().Directory(PiRoot, link, @"C:\escape", project).Directory(project, file, Path.Combine(project, "nested"))
            .Entry(link, LocalUsagePathKind.Directory, reparse: true).Entry(file, LocalUsagePathKind.File).Entry(Path.Combine(project, "nested"), LocalUsagePathKind.Directory);
        var result = new LocalUsageSourceDiscovery(fs).Discover(new(false, true), new(null, PiRoot, Salt));
        var source = result.Sources[1]; var registration = Assert.Single(result.Registrations);
        Assert.Equal(LocalUsageDiscoveryStatus.Ready, source.Status); Assert.Equal(3, source.SkippedCount);
        Assert.Contains(LocalUsageDiscoveryWarning.ReparsePoint, source.WarningCodes); Assert.Contains(LocalUsageDiscoveryWarning.PathRejected, source.WarningCodes);
        Assert.Equal(LocalUsageAdapterKind.PiJsonlV3, registration.Adapter); Assert.Equal(3, registration.SourceSchemaVersion);
    }

    [Fact]
    public void Unreadable_and_bounded_Pi_traversals_do_not_publish_partial_registrations()
    {
        var project = Path.Combine(PiRoot, "--project--"); var file = Path.Combine(project, "session.jsonl");
        var unreadable = new SyntheticFileSystem().Directory(PiRoot, project).Directory(project, file).Unreadable(file);
        var failed = new LocalUsageSourceDiscovery(unreadable).Discover(new(false, true), new(null, PiRoot, Salt));
        Assert.Equal(LocalUsageDiscoveryStatus.Unavailable, failed.Sources[1].Status); Assert.Empty(failed.Candidates); Assert.Empty(failed.Registrations);
        Assert.Contains(LocalUsageDiscoveryWarning.Unreadable, failed.Sources[1].WarningCodes);

        var junction = new SyntheticFileSystem().Entry(PiRoot, LocalUsagePathKind.Directory).Entry(@"C:\synthetic\pi", LocalUsagePathKind.Directory, reparse: true);
        Assert.Contains(LocalUsageDiscoveryWarning.ReparsePoint, new LocalUsageSourceDiscovery(junction).Discover(new(false, true), new(null, PiRoot, Salt)).Sources[1].WarningCodes);

        var entries = Enumerable.Range(0, LocalUsageSourceDiscovery.MaximumEntries + 1).Select(index => Path.Combine(PiRoot, $"--{index:D4}--")).ToArray();
        var bounded = new SyntheticFileSystem().Directory(PiRoot, entries);
        var result = new LocalUsageSourceDiscovery(bounded).Discover(new(false, true), new(null, PiRoot, Salt));
        Assert.Equal(LocalUsageDiscoveryStatus.Unavailable, result.Sources[1].Status); Assert.Contains(LocalUsageDiscoveryWarning.BoundsExceeded, result.Sources[1].WarningCodes);

        var excessive = new SyntheticFileSystem(); var projects = Enumerable.Range(0, LocalUsageSourceDiscovery.MaximumCandidates + 1).Select(index => Path.Combine(PiRoot, $"--p{index:D3}--")).ToArray();
        excessive.Directory(PiRoot, projects); foreach (var directory in projects) { var candidatePath = Path.Combine(directory, "s.jsonl"); excessive.Directory(directory, candidatePath).Entry(candidatePath, LocalUsagePathKind.File); }
        var excess = new LocalUsageSourceDiscovery(excessive).Discover(new(false, true), new(null, PiRoot, Salt));
        Assert.Equal(LocalUsageDiscoveryStatus.Unavailable, excess.Sources[1].Status); Assert.Equal(LocalUsageSourceDiscovery.MaximumCandidates + 1, excess.Sources[1].CandidateCount); Assert.Empty(excess.Candidates);
    }

    [Fact]
    public void Identity_is_stable_case_insensitive_and_isolated_by_salt_tool_and_path_without_public_exposure()
    {
        var same = LocalUsageSourceDiscovery.CreateIdentity(UsageTool.OpenCode, @"C:\Data\opencode.db", Salt);
        Assert.Equal(same, LocalUsageSourceDiscovery.CreateIdentity(UsageTool.OpenCode, @"c:\data\OPENCODE.db", Salt));
        Assert.NotEqual(same, LocalUsageSourceDiscovery.CreateIdentity(UsageTool.OpenCode, @"C:\Data\other.db", Salt));
        Assert.NotEqual(same, LocalUsageSourceDiscovery.CreateIdentity(UsageTool.Pi, @"C:\Data\opencode.db", Salt));
        Assert.NotEqual(same, LocalUsageSourceDiscovery.CreateIdentity(UsageTool.OpenCode, @"C:\Data\opencode.db", Salt.Select(value => (byte)(value + 1)).ToArray()));

        var fs = new SyntheticFileSystem().Entry(OpenRoot, LocalUsagePathKind.Directory).Entry(Path.Combine(OpenRoot, "opencode.db"), LocalUsagePathKind.File);
        var json = JsonSerializer.Serialize(new LocalUsageSourceDiscovery(fs).Discover(new(true, false), new(OpenRoot, null, Salt)));
        Assert.DoesNotContain("synthetic", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Identity", json); Assert.DoesNotContain("Path", json);
    }

    [Fact]
    public void Cancellation_during_synthetic_traversal_propagates_and_other_tool_failures_are_isolated()
    {
        using var cancellation = new CancellationTokenSource();
        var cancelling = new SyntheticFileSystem().Directory(PiRoot).CancelOnEnumerate(cancellation);
        Assert.ThrowsAny<OperationCanceledException>(() => new LocalUsageSourceDiscovery(cancelling).Discover(new(false, true), new(null, PiRoot, Salt), cancellation.Token));

        var file = Path.Combine(PiRoot, "--p--", "s.jsonl");
        var isolated = new SyntheticFileSystem().Unreadable(OpenRoot).Directory(PiRoot, Path.GetDirectoryName(file)!).Directory(Path.GetDirectoryName(file)!, file).Entry(file, LocalUsagePathKind.File);
        var result = new LocalUsageSourceDiscovery(isolated).Discover(new(true, true), new(OpenRoot, PiRoot, Salt));
        Assert.Equal(LocalUsageDiscoveryStatus.Unavailable, result.Sources[0].Status); Assert.Equal(LocalUsageDiscoveryStatus.Ready, result.Sources[1].Status);

        var database = Path.Combine(OpenRoot, "opencode.db"); var project = Path.Combine(PiRoot, "--p--");
        foreach (var target in new[] { OpenRoot, database, project, file }) { using var source = new CancellationTokenSource(); var fs = new SyntheticFileSystem().Entry(OpenRoot, LocalUsagePathKind.Directory).Entry(database, LocalUsagePathKind.File).Directory(PiRoot, project).Directory(project, file).Entry(file, LocalUsagePathKind.File).CancelOnInspect(target, source); Assert.ThrowsAny<OperationCanceledException>(() => new LocalUsageSourceDiscovery(fs).Discover(new(true, true), new(OpenRoot, PiRoot, Salt), source.Token)); }
        Assert.Throws<InvalidOperationException>(() => new LocalUsageSourceDiscovery(new SyntheticFileSystem().DefectOnInspect(OpenRoot)).Discover(new(true, false), new(OpenRoot, null, Salt)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LocalUsageDiscoveryFacts(null, null, Salt, (PiUsageDiscoveryLayout)0));
    }

    private sealed class SyntheticFileSystem : ILocalUsageDiscoveryFileSystem
    {
        private readonly Dictionary<string, LocalUsagePathMetadata> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string[]> _children = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
        private CancellationTokenSource? _cancelEnumerate, _cancelInspectSource;
        private string? _cancelInspect, _defectInspect;
        public int Calls { get; private set; }
        public int Enumerations { get; private set; }
        public List<string> Inspected { get; } = [];
        public SyntheticFileSystem Entry(string path, LocalUsagePathKind kind, bool reparse = false) { path = Full(path); _entries[path] = new(kind, reparse, new(1, Identity(path))); AddParents(path); return this; }
        public SyntheticFileSystem Directory(string path, params string[] children) { Entry(path, LocalUsagePathKind.Directory); _children[Full(path)] = children; return this; }
        public SyntheticFileSystem Unreadable(string path) { _unreadable.Add(Full(path)); return this; }
        public SyntheticFileSystem CancelOnEnumerate(CancellationTokenSource cancellation) { _cancelEnumerate = cancellation; return this; }
        public SyntheticFileSystem CancelOnInspect(string path, CancellationTokenSource cancellation) { _cancelInspect = Full(path); _cancelInspectSource = cancellation; return this; }
        public SyntheticFileSystem DefectOnInspect(string path) { _defectInspect = Full(path); return this; }
        public LocalUsagePathMetadata Inspect(string path, CancellationToken token)
        { Calls++; token.ThrowIfCancellationRequested(); path = Full(path); Inspected.Add(path); if (path == _defectInspect) throw new InvalidOperationException(); if (path == _cancelInspect) _cancelInspectSource?.Cancel(); token.ThrowIfCancellationRequested(); if (_unreadable.Contains(path)) throw new UnauthorizedAccessException(); return _entries.GetValueOrDefault(path); }
        public IEnumerable<string> Enumerate(string directory, CancellationToken token)
        { Calls++; Enumerations++; token.ThrowIfCancellationRequested(); directory = Full(directory); if (_unreadable.Contains(directory)) throw new UnauthorizedAccessException(); _cancelEnumerate?.Cancel(); token.ThrowIfCancellationRequested(); return _children.GetValueOrDefault(directory) ?? []; }
        private static string Full(string path) => Path.GetFullPath(path); private void AddParents(string path) { for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent)) _entries.TryAdd(parent, new(LocalUsagePathKind.Directory, false, new(1, Identity(parent)))); } private static Guid Identity(string path) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
    }
}
