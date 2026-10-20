using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class SettingsProviderTests
{
    [Fact]
    public async Task Declined_consent_never_enables_or_changes_source()
    {
        var calls = new List<string>();
        var commands = new SettingsProviderCommands(() => false, () => false, () => ClaudeQuotaSource.Automatic,
            () => false, () => false,
            value => { calls.Add($"codex:{value}"); return Task.CompletedTask; },
            value => { calls.Add($"claude:{value}"); return Task.CompletedTask; },
            value => { calls.Add($"source:{value}"); return Task.CompletedTask; });
        Assert.False(await commands.SaveAsync(true, true, ClaudeQuotaSource.OtherMachine));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task Revocation_needs_no_prompt_and_unchanged_source_is_not_written()
    {
        var calls = new List<string>();
        var commands = new SettingsProviderCommands(() => true, () => true, () => ClaudeQuotaSource.OtherMachine,
            () => throw new InvalidOperationException("Must not prompt on disable"),
            () => throw new InvalidOperationException("Must not prompt on disable"),
            value => { calls.Add($"codex:{value}"); return Task.CompletedTask; },
            value => { calls.Add($"claude:{value}"); return Task.CompletedTask; },
            _ => throw new InvalidOperationException("Must not rewrite an unchanged source"));
        Assert.True(await commands.SaveAsync(false, false, ClaudeQuotaSource.OtherMachine));
        Assert.Equal(new[] { "codex:False", "claude:False" }, calls);
    }

    [Fact]
    public async Task Accepted_consent_uses_source_then_existing_enable_commands()
    {
        var calls = new List<string>();
        var commands = new SettingsProviderCommands(() => false, () => false, () => ClaudeQuotaSource.Automatic,
            () => true, () => true,
            value => { calls.Add($"codex:{value}"); return Task.CompletedTask; },
            value => { calls.Add($"claude:{value}"); return Task.CompletedTask; },
            value => { calls.Add($"source:{value}"); return Task.CompletedTask; });
        Assert.True(await commands.SaveAsync(true, true, ClaudeQuotaSource.ThisPc));
        Assert.Equal(new[] { "source:ThisPc", "codex:True", "claude:True" }, calls);
    }
}
