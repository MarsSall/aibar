namespace AIBar.Application;

/// <summary>Coordinates an allowlisted clear. A successful return means every owned target was removed and the supplied empty-state factory completed.</summary>
public interface IAiBarClearWork
{
    ValueTask CancelAndWaitAsync(CancellationToken cancellationToken);
    void ResumeAfterClear();
}

public sealed class ClearAiBarDataService : IAiBarDataClearCommand
{
    private static readonly string[] DefaultOwnedPaths = ["cache", "logs", "aibar.db", "aibar.db-wal", "aibar.db-shm", "quota.db", "quota.db-wal", "quota.db-shm", "settings.json",
        "local-usage-settings.json", "local-usage-identity-salt.bin", "local-usage.db", "local-usage.db-wal", "local-usage.db-shm"];
    private readonly string _root;
    private readonly IReadOnlyList<IAiBarClearWork> _work;
    private readonly Func<CancellationToken, ValueTask> _recreateEmptyState;
    private readonly string[] _ownedPaths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ClearAiBarDataService(string root, IReadOnlyList<IAiBarClearWork> work, Func<CancellationToken, ValueTask> recreateEmptyState, IReadOnlyList<string>? ownedPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _recreateEmptyState = recreateEmptyState ?? throw new ArgumentNullException(nameof(recreateEmptyState));
        _ownedPaths = (ownedPaths ?? DefaultOwnedPaths).ToArray();
        if (_ownedPaths.Length == 0 || _ownedPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != _ownedPaths.Length || _ownedPaths.Any(path => !IsExactOwnedPath(path)))
            throw new ArgumentException("Owned paths must be distinct, exact AIBar allowlist entries.", nameof(ownedPaths));
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var item in _work) await item.CancelAndWaitAsync(cancellationToken);
            VerifySafeRoot();
            var staged = new List<(string Original, string Temporary)>();
            try
            {
                foreach (var relative in _ownedPaths)
                {
                    var original = Path.Combine(_root, relative);
                    if (!File.Exists(original) && !Directory.Exists(original)) continue;
                    ValidateOwnedTarget(original);
                    var temporary = $"{original}.aibar-clear-{Guid.NewGuid():N}";
                    if (File.Exists(original)) File.Move(original, temporary); else Directory.Move(original, temporary);
                    staged.Add((original, temporary));
                }
            }
            catch (Exception failure)
            {
                try
                {
                    for (var index = staged.Count - 1; index >= 0; index--)
                    {
                        var (original, temporary) = staged[index];
                        if (File.Exists(temporary)) File.Move(temporary, original); else if (Directory.Exists(temporary)) Directory.Move(temporary, original);
                    }
                }
                catch (Exception rollbackFailure) { throw new AggregateException(failure, rollbackFailure); }
                throw;
            }
            foreach (var (_, temporary) in staged) DeleteOwnedTarget(temporary);
            Directory.CreateDirectory(_root);
            await _recreateEmptyState(cancellationToken);
        }
        finally
        {
            foreach (var item in _work) item.ResumeAfterClear();
            _gate.Release();
        }
    }

    private bool IsExactOwnedPath(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        path.IndexOfAny(Path.GetInvalidPathChars()) < 0 && path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).All(part => part is not "." and not "..") &&
        DefaultOwnedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

    private void VerifySafeRoot()
    {
        for (var current = _root; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current) ?? string.Empty)
        {
            if (!Directory.Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("AIBar data root is a reparse point.");
            break;
        }
    }

    private static void DeleteOwnedTarget(string path)
    {
        if (File.Exists(path)) { RejectReparsePoint(path); File.Delete(path); return; }
        if (!Directory.Exists(path)) return;
        RejectReparsePoint(path);
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            RejectReparsePoint(child);
            if (Directory.Exists(child)) DeleteOwnedTarget(child); else File.Delete(child);
        }
        Directory.Delete(path);
    }

    private static void ValidateOwnedTarget(string path)
    {
        RejectReparsePoint(path);
        if (File.Exists(path)) { using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return; }
        foreach (var child in Directory.EnumerateFileSystemEntries(path)) ValidateOwnedTarget(child);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("AIBar clear refuses reparse points.");
    }
}
