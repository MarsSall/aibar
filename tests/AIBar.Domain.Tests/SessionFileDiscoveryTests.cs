using AIBar.Application;
namespace AIBar.Domain.Tests;
public sealed class SessionFileDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-discovery-{Guid.NewGuid():N}");
    [Fact]
    public void Discovery_uses_resolved_root_and_finds_supported_layouts_in_deterministic_bounded_batches()
    {
        Write("sessions/2026/07/date.jsonl");
        Write("sessions/flat.jsonl");
        Write("archived_sessions/legacy/nested/archive.jsonl");
        Write("sessions/ignored.txt");
        Write("other/outside.jsonl");
        var discovery = new SessionFileDiscovery(_root, "unused", 2);
        var result = discovery.Discover(CancellationToken.None);
        Assert.Equal(["archive.jsonl", "date.jsonl"], Names(result.Batches[0]));
        Assert.Equal(["flat.jsonl"], Names(result.Batches[1]));
        Assert.Equal(3, result.Coverage.FilesDiscovered);
        Assert.Equal(0, result.Coverage.FilesSkipped);
        Assert.Empty(result.Coverage.WarningCodes);
    }
    [Fact]
    public void Discovery_requires_positive_batch_size_and_reports_missing_roots_without_paths()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionFileDiscovery(_root, "unused", 0));
        var result = new SessionFileDiscovery(_root, "unused", 1).Discover(CancellationToken.None);
        Assert.Empty(result.Batches);
        Assert.Equal(["session_root_missing"], result.Coverage.WarningCodes);
        Assert.Equal(0, result.Coverage.FilesDiscovered);
        Assert.Equal(0, result.Coverage.FilesSkipped);
    }
    [Fact]
    public void Discovery_honors_cancellation_before_traversal_and_between_batches()
    {
        Write("sessions/a.jsonl");
        Write("sessions/b.jsonl");
        var discovery = new SessionFileDiscovery(_root, "unused", 1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => discovery.Discover(cancelled.Token));
        using var betweenBatches = new CancellationTokenSource();
        using var enumerator = discovery.EnumerateBatches(betweenBatches.Token).GetEnumerator();
        Assert.True(enumerator.MoveNext());
        betweenBatches.Cancel();
        Assert.Throws<OperationCanceledException>(() => enumerator.MoveNext());
    }
    [Fact]
    public void Discovery_skips_reparse_points_and_exposes_only_safe_warning_codes()
    {
        Write("sessions/inside.jsonl");
        var linked = Path.Combine(_root, "sessions", "linked");
        Directory.CreateSymbolicLink(linked, Path.Combine(_root, "outside"));
        var result = new SessionFileDiscovery(_root, "unused", 4).Discover(CancellationToken.None);
        Assert.Equal(["inside.jsonl"], Names(result.Batches.Single()));
        Assert.Contains("session_reparse_skipped", result.Coverage.WarningCodes);
        Assert.All(result.Coverage.WarningCodes, code => Assert.DoesNotContain(_root, code, StringComparison.OrdinalIgnoreCase));
    }
        [Fact]
        public void Enumeration_yields_before_a_later_traversal_failure_is_reached()
        {
            var files = SupportedDirectories();
            var first = Path.Combine(_root, "sessions", "first.jsonl"); var second = Path.Combine(_root, "sessions", "second.jsonl");
            files.Entries[Path.Combine(_root, "sessions")] = [second, first];
            files.EnumerationFailures[Path.Combine(_root, "archived_sessions")] = new OperationCanceledException("later cancellation");
            using var batches = new SessionFileDiscovery(_root, "unused", 1, files).EnumerateBatches(default).GetEnumerator();
            Assert.True(batches.MoveNext());
            Assert.Equal(["first.jsonl"], Names(batches.Current)); Assert.True(batches.MoveNext()); Assert.Equal(["second.jsonl"], Names(batches.Current));
            Assert.Throws<OperationCanceledException>(() => batches.MoveNext());
        }
        [Fact]
        public void Discovery_reports_each_missing_supported_directory_without_paths()
    {
        var files = new ScriptedSessionFileSystem();
        files.AddDirectory(_root);
        var result = new SessionFileDiscovery(_root, "unused", 2, files).Discover(CancellationToken.None);
        Assert.Equal(["session_archived_sessions_missing", "session_sessions_missing"], result.Coverage.WarningCodes);
        Assert.Equal(0, result.Coverage.FilesSkipped);
        Assert.All(result.Coverage.WarningCodes, code => Assert.DoesNotContain(_root, code, StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Discovery_continues_after_injected_unreadable_and_changing_entries()
    {
        var files = SupportedDirectories();
        var changing = Path.Combine(_root, "sessions", "changing.jsonl");
        files.Entries[Path.Combine(_root, "sessions")] = [changing];
        files.AttributeFailures[changing] = new IOException("synthetic change");
        files.EnumerationFailures[Path.Combine(_root, "archived_sessions")] = new UnauthorizedAccessException("synthetic unreadable");
        var result = new SessionFileDiscovery(_root, "unused", 2, files).Discover(CancellationToken.None);
        Assert.Empty(result.Batches);
        Assert.Equal(1, result.Coverage.FilesSkipped);
        Assert.Equal(["session_directory_unreadable", "session_file_changed"], result.Coverage.WarningCodes);
    }
    [Fact]
    public void Discovery_reports_attribute_inspection_failure_without_paths()
    {
        var files = SupportedDirectories();
        files.AttributeFailures[_root] = new UnauthorizedAccessException("synthetic attributes");
        var result = new SessionFileDiscovery(_root, "unused", 2, files).Discover(CancellationToken.None);
        Assert.Empty(result.Batches);
        Assert.Equal(0, result.Coverage.FilesSkipped);
        Assert.Equal(["session_root_attribute_unavailable"], result.Coverage.WarningCodes);
        Assert.All(result.Coverage.WarningCodes, code => Assert.DoesNotContain(_root, code, StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void Discovery_preserves_injected_cancellation()
    {
        var files = SupportedDirectories();
        files.EnumerationFailures[Path.Combine(_root, "sessions")] = new OperationCanceledException("synthetic cancellation");
        Assert.Throws<OperationCanceledException>(() => new SessionFileDiscovery(_root, "unused", 2, files).Discover(CancellationToken.None));
    }
    private ScriptedSessionFileSystem SupportedDirectories()
    {
        var files = new ScriptedSessionFileSystem();
        files.AddDirectory(_root);
        files.AddDirectory(Path.Combine(_root, "sessions"));
        files.AddDirectory(Path.Combine(_root, "archived_sessions"));
        return files;
    }
    private void Write(string relative)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "synthetic");
    }
    private static string[] Names(SessionFileBatch batch) => batch.Files.Select(path => Path.GetFileName(path)!).ToArray();
    private sealed class ScriptedSessionFileSystem : ISessionFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string[]> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Exception> EnumerationFailures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Exception> AttributeFailures { get; } = new(StringComparer.OrdinalIgnoreCase);
        public void AddDirectory(string path) => _directories.Add(path);
        public bool DirectoryExists(string path) => _directories.Contains(path);
        public IEnumerable<string> GetFileSystemEntries(string path)
        {
            if (EnumerationFailures.TryGetValue(path, out var failure)) throw failure;
            return Entries.TryGetValue(path, out var entries) ? entries : [];
        }
        public FileAttributes GetAttributes(string path)
        {
            if (AttributeFailures.TryGetValue(path, out var failure)) throw failure;
            return FileAttributes.Normal;
        }
    }
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
