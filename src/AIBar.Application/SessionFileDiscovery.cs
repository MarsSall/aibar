namespace AIBar.Application;
public sealed record SessionFileBatch(IReadOnlyList<string> Files);
public sealed record SessionDiscoveryCoverage(int FilesDiscovered, int FilesSkipped, IReadOnlyList<string> WarningCodes);
public sealed record SessionDiscoveryResult(IReadOnlyList<SessionFileBatch> Batches, SessionDiscoveryCoverage Coverage);
public interface ISessionFileSystem
{
    bool DirectoryExists(string path);
    IEnumerable<string> GetFileSystemEntries(string path);
    FileAttributes GetAttributes(string path);
}
public sealed class SessionFileSystem : ISessionFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public IEnumerable<string> GetFileSystemEntries(string path) => Directory.GetFileSystemEntries(path);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
}
public sealed class SessionFileDiscovery
{
    private readonly string _root;
    private readonly int _maxFilesPerBatch;
    private readonly ISessionFileSystem _fileSystem;
    public SessionFileDiscovery(string? codexHome, string userProfile, int maxFilesPerBatch, ISessionFileSystem? fileSystem = null)
    {
        if (maxFilesPerBatch <= 0) throw new ArgumentOutOfRangeException(nameof(maxFilesPerBatch));
        _root = Path.GetFullPath(CodexRootResolver.Resolve(codexHome, userProfile));
        _maxFilesPerBatch = maxFilesPerBatch;
        _fileSystem = fileSystem ?? new SessionFileSystem();
    }
    public SessionDiscoveryResult Discover(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var files = new List<string>();
        var skipped = 0;
        try
        {
            if (!_fileSystem.DirectoryExists(_root))
                return Result(files, skipped, ["session_root_missing"]);
        }
        catch (UnauthorizedAccessException) { return Result(files, skipped, ["session_root_unreadable"]); }
        catch (IOException) { return Result(files, skipped, ["session_root_unreadable"]); }
        if (!TryIsReparsePoint(_root, "session_root_attribute_unavailable", warnings, ref skipped, out var rootIsReparse))
            return Result(files, skipped, warnings);
        if (rootIsReparse)
            return Result(files, skipped, ["session_root_reparse_skipped"]);
        foreach (var name in new[] { "sessions", "archived_sessions" })
            Traverse(name, Path.Combine(_root, name), files, warnings, ref skipped, cancellationToken);
        return Result(files, skipped, warnings);
    }
    public IEnumerable<SessionFileBatch> EnumerateBatches(CancellationToken cancellationToken)
    { var batch = new List<string>(_maxFilesPerBatch);
        foreach (var file in StreamFiles(cancellationToken)) { batch.Add(file);
            if (batch.Count == _maxFilesPerBatch) { yield return new(batch.ToArray()); batch.Clear(); } }
        cancellationToken.ThrowIfCancellationRequested();
        if (batch.Count > 0) yield return new(batch.ToArray()); }
    private IEnumerable<string> StreamFiles(CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); bool rootExists;
        try { rootExists = _fileSystem.DirectoryExists(_root) && !IsReparsePoint(_root); } catch (UnauthorizedAccessException) { rootExists = false; } catch (IOException) { rootExists = false; }
        if (!rootExists) yield break;
        foreach (var name in new[] { "sessions", "archived_sessions" })
        { var pending = new Stack<string>();
            try { if (_fileSystem.DirectoryExists(Path.Combine(_root, name))) pending.Push(Path.Combine(_root, name)); } catch (UnauthorizedAccessException) { } catch (IOException) { }
            while (pending.TryPop(out var directory))
            { cancellationToken.ThrowIfCancellationRequested(); string[] entries;
                try { if (!IsContained(directory) || IsReparsePoint(directory)) continue; entries = _fileSystem.GetFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray(); } catch (UnauthorizedAccessException) { continue; } catch (IOException) { continue; }
                foreach (var entry in entries)
                { cancellationToken.ThrowIfCancellationRequested(); string? file = null;
                    try { var isDirectory = _fileSystem.DirectoryExists(entry); if (!IsContained(entry) || IsReparsePoint(entry)) continue; if (isDirectory) pending.Push(entry); else if (IsFile(entry)) file = entry; } catch (UnauthorizedAccessException) { } catch (IOException) { }
                    if (file is not null) yield return file; } } } }
    private void Traverse(string supportedDirectory, string start, List<string> files, HashSet<string> warnings, ref int skipped, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!DirectoryExists(start, $"session_{supportedDirectory}_missing", warnings, ref skipped)) return;
        var directories = new Stack<string>();
        directories.Push(start);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            if (!IsContained(directory))
            {
                warnings.Add("session_reparse_skipped");
                continue;
            }
            if (!TryIsReparsePoint(directory, "session_directory_attribute_unavailable", warnings, ref skipped, out var directoryIsReparse))
                continue;
            if (directoryIsReparse)
            {
                warnings.Add("session_reparse_skipped");
                continue;
            }
            string[] entries;
            try { entries = _fileSystem.GetFileSystemEntries(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray(); }
            catch (UnauthorizedAccessException) { warnings.Add("session_directory_unreadable"); continue; }
            catch (IOException) { warnings.Add("session_directory_unreadable"); continue; }
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var isDirectory = _fileSystem.DirectoryExists(entry);
                    if (!IsContained(entry) || IsReparsePoint(entry))
                    {
                        if (!isDirectory) skipped++;
                        warnings.Add("session_reparse_skipped");
                    }
                    else if (isDirectory) directories.Push(entry);
                    else if (Path.GetExtension(entry).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)) files.Add(entry);
                }
                catch (UnauthorizedAccessException) { if (IsFile(entry)) skipped++; warnings.Add("session_file_unreadable"); }
                catch (IOException) { if (IsFile(entry)) skipped++; warnings.Add("session_file_changed"); }
            }
        }
    }
    private bool DirectoryExists(string path, string failureCode, HashSet<string> warnings, ref int skipped)
    {
        try
        {
            if (_fileSystem.DirectoryExists(path)) return true;
            warnings.Add(failureCode);
            return false;
        }
        catch (UnauthorizedAccessException) { warnings.Add(failureCode); return false; }
        catch (IOException) { warnings.Add(failureCode); return false; }
    }
    private bool TryIsReparsePoint(string path, string failureCode, HashSet<string> warnings, ref int skipped, out bool isReparsePoint)
    {
        try
        {
            isReparsePoint = IsReparsePoint(path);
            return true;
        }
        catch (UnauthorizedAccessException) { if (failureCode.StartsWith("session_file_", StringComparison.Ordinal)) skipped++; warnings.Add(failureCode); isReparsePoint = false; return false; }
        catch (IOException) { if (failureCode.StartsWith("session_file_", StringComparison.Ordinal)) skipped++; warnings.Add(failureCode); isReparsePoint = false; return false; }
    }
    private SessionDiscoveryResult Result(List<string> files, int skipped, IEnumerable<string> warnings)
    {
        var ordered = files.OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var batches = ordered.Chunk(_maxFilesPerBatch).Select(chunk => new SessionFileBatch(chunk)).ToArray();
        return new(batches, new(ordered.Length, skipped, warnings.OrderBy(code => code, StringComparer.Ordinal).ToArray()));
    }
    private bool IsContained(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsFile(string path) => Path.GetExtension(path).Equals(".jsonl", StringComparison.OrdinalIgnoreCase);
    private bool IsReparsePoint(string path) => (_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
