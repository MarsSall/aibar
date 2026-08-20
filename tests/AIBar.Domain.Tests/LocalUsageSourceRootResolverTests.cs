using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class LocalUsageSourceRootResolverTests
{
    [Fact]
    public void Default_roots_follow_the_pinned_tool_contract_without_probing()
    {
        var home = Path.Combine(Path.GetTempPath(), "synthetic-home", "profile");

        var roots = LocalUsageSourceRootResolver.Resolve(new(home));

        Assert.Equal(Path.Combine(home, ".local", "share", "opencode"), roots.OpenCodeDataRoot);
        Assert.Equal(Path.Combine(home, ".pi", "agent", "sessions"), roots.PiSessionsRoot);
        Assert.False(Directory.Exists(home));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative-home")]
    [InlineData(" C:\\synthetic-home")]
    [InlineData("C:\\")]
    public void Invalid_home_facts_fail_closed(string? home)
    {
        var roots = LocalUsageSourceRootResolver.Resolve(new(home));

        Assert.Null(roots.OpenCodeDataRoot);
        Assert.Null(roots.PiSessionsRoot);
    }

    [Fact]
    public void Custom_root_requires_canonical_existing_fixed_local_directory_but_persisted_missing_root_stays_effective()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-root-{Guid.NewGuid():N}");
        var custom = Path.Combine(root, "custom"); Directory.CreateDirectory(custom);
        try
        {
            Assert.True(LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(custom, true, out var normalized));
            Assert.Equal(custom, normalized);
            Assert.Equal(custom, LocalUsageSourceRootResolver.ResolveOpenCodeDataRoot("default", custom));
            Assert.False(LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(custom + Path.DirectorySeparatorChar, true, out _));
            Assert.False(LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(@"\\server\share\opencode", true, out _));
            Assert.False(LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(Path.GetPathRoot(custom), true, out _));
            Assert.False(LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(custom, true, out _, _ => (DriveType.Network, true)));
            Directory.Delete(custom);
            Assert.Equal(custom, LocalUsageSourceRootResolver.ResolveOpenCodeDataRoot("default", custom));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void Custom_root_rejects_reparse_ancestry()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-root-link-{Guid.NewGuid():N}");
        var target = Path.Combine(root, "target"); var link = Path.Combine(root, "link"); Directory.CreateDirectory(target);
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        { if (Directory.Exists(root)) Directory.Delete(root, true); throw Xunit.Sdk.SkipException.ForSkip($"Symbolic-link test unsupported: {error.GetType().Name}"); }
        try { Assert.False(LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(link, true, out _)); }
        finally { Directory.Delete(root, true); }
    }
}
