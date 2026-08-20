namespace AIBar.Application;

public sealed record LocalUsageHomeFacts(string? UserProfile);
public sealed record LocalUsageSourceRoots(string? OpenCodeDataRoot, string? PiSessionsRoot)
{
    public static LocalUsageSourceRoots Unavailable { get; } = new(null, null);
}

public static class LocalUsageSourceRootResolver
{
    private const int MaximumPathCharacters = 1024;
    public const string InvalidOpenCodeDataRootMessage = "The selected OpenCode folder cannot be used.";

    public static LocalUsageSourceRoots Resolve(LocalUsageHomeFacts? facts)
    {
        try
        {
            var input = facts?.UserProfile;
            if (string.IsNullOrWhiteSpace(input) || input != input.Trim() || input.Length > MaximumPathCharacters
                || input.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(input)) return LocalUsageSourceRoots.Unavailable;
            var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            var volume = Path.GetPathRoot(home);
            if (volume is null || StringComparer.OrdinalIgnoreCase.Equals(home, Path.TrimEndingDirectorySeparator(volume))) return LocalUsageSourceRoots.Unavailable;
            var openCode = Path.Combine(home, ".local", "share", "opencode");
            var pi = Path.Combine(home, ".pi", "agent", "sessions");
            return openCode.Length <= MaximumPathCharacters && pi.Length <= MaximumPathCharacters
                ? new(openCode, pi) : LocalUsageSourceRoots.Unavailable;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { return LocalUsageSourceRoots.Unavailable; }
    }

    public static string? ResolveOpenCodeDataRoot(string? defaultRoot, string? customRoot) => customRoot is null
        ? defaultRoot
        : TryNormalizeOpenCodeDataRoot(customRoot, false, out var normalized) ? normalized : null;

    internal static bool TryNormalizeOpenCodeDataRoot(string? input, bool requireExisting, out string? normalized,
        Func<string, (DriveType Type, bool Ready)>? getDrive = null)
    {
        normalized = null;
        try
        {
            if (string.IsNullOrWhiteSpace(input) || input != input.Trim() || input.Length > MaximumPathCharacters
                || input.StartsWith("\\\\", StringComparison.Ordinal) || input.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                || !Path.IsPathFullyQualified(input)) return false;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
            var volume = Path.GetPathRoot(full);
            if (!StringComparer.Ordinal.Equals(input, full) || volume is null
                || StringComparer.OrdinalIgnoreCase.Equals(full, Path.TrimEndingDirectorySeparator(volume))) return false;
            var drive = getDrive?.Invoke(volume) ?? GetDrive(volume);
            if (drive.Type != DriveType.Fixed || !drive.Ready) return false;
            for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (requireExisting && (!Directory.Exists(full) || (new DirectoryInfo(full).Attributes & FileAttributes.Directory) == 0)) return false;
            normalized = full;
            return true;
        }
        catch { return false; }
    }

    private static (DriveType Type, bool Ready) GetDrive(string volume)
    {
        var drive = new DriveInfo(volume);
        return (drive.DriveType, drive.IsReady);
    }
}
