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
}
