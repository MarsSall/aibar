namespace AIBar.Application;

public sealed record LocalUsageHomeFacts(string? UserProfile);
public sealed record LocalUsageSourceRoots(string? OpenCodeDataRoot, string? PiSessionsRoot)
{
    public static LocalUsageSourceRoots Unavailable { get; } = new(null, null);
}

public static class LocalUsageSourceRootResolver
{
    private const int MaximumPathCharacters = 1024;

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
}
