using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ProviderLinksTests
{
    [Theory]
    [InlineData(StatusProvider.Codex, "https://chatgpt.com/codex/settings/usage", "https://status.openai.com")]
    [InlineData(StatusProvider.Claude, "https://claude.ai/settings/usage", "https://status.claude.com")]
    public void OnlyOfficialFixedUrlsCanBeOpened(StatusProvider provider, string usage, string status)
    {
        var opened = new List<System.Diagnostics.ProcessStartInfo>();
        ProviderLinks.OpenUsage(provider, info => opened.Add(info));
        ProviderLinks.OpenStatus(provider, info => opened.Add(info));
        Assert.Equal(new[] { usage, status }, opened.Select(info => info.FileName));
        Assert.All(opened, info => { Assert.True(info.UseShellExecute); Assert.Empty(info.Arguments); });
    }

    [Fact]
    public void InvalidProviderCannotLaunchAnything()
    {
        var called = false;
        Assert.Throws<ArgumentOutOfRangeException>(() => ProviderLinks.OpenUsage((StatusProvider)99, _ => called = true));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProviderLinks.OpenStatus((StatusProvider)99, _ => called = true));
        Assert.False(called);
    }

    [Fact]
    public void ShellFailureDoesNotCrashPanel()
    {
        ProviderLinks.OpenUsage(StatusProvider.Codex, _ => throw new System.ComponentModel.Win32Exception());
        ProviderLinks.OpenStatus(StatusProvider.Claude, _ => throw new InvalidOperationException());
    }
}
