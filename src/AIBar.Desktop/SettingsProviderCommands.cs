using AIBar.Application;

namespace AIBar.Desktop;

/// <summary>Stages UI choices; existing runtime commands remain the only consent/source writers.</summary>
public sealed class SettingsProviderCommands(
    Func<bool> codexEnabled, Func<bool> claudeEnabled, Func<ClaudeQuotaSource> source,
    Func<bool> confirmCodex, Func<bool> confirmClaude,
    Func<bool, Task> setCodex, Func<bool, Task> setClaude, Func<ClaudeQuotaSource, Task> setSource,
    Func<string>? sourceDescription = null)
{
    public bool CodexEnabled => codexEnabled();
    public bool ClaudeEnabled => claudeEnabled();
    public ClaudeQuotaSource Source => source();
    public string SourceDescription => sourceDescription?.Invoke() ?? "Source changes apply when you save.";

    public async Task<bool> SaveAsync(bool codex, bool claude, ClaudeQuotaSource selectedSource)
    {
        // Collect both approvals before any write, and never prompt for revocation.
        if (codex && !CodexEnabled && !confirmCodex()) return false;
        if (claude && !ClaudeEnabled && !confirmClaude()) return false;
        if (selectedSource != Source) await setSource(selectedSource);
        if (codex != CodexEnabled) await setCodex(codex);
        if (claude != ClaudeEnabled) await setClaude(claude);
        return true;
    }

    public static SettingsProviderCommands Create(NativeSettingsCommands settings, ClaudeQuotaRuntime claude) => new(
        () => settings.PrivateIntegrationEnabled, () => claude.IsConsentEnabled, () => claude.Source,
        new WindowsPrivateIntegrationConsentPrompt().Confirm, new WindowsClaudeQuotaConsentPrompt().Confirm,
        enabled => (enabled ? settings.EnablePrivateIntegrationAsync(default) : settings.DisablePrivateIntegrationAsync(default)).AsTask(),
        enabled => (enabled ? claude.GrantConsentAsync(default) : claude.RevokeConsentAsync(default)).AsTask(),
        selected => claude.SetSourceAsync(selected, default).AsTask(),
        () =>
        {
            if (!claude.IsConsentEnabled) return "Claude is off. Source is used when you enable it.";
            var sourceName = claude.ResolvedSource == ClaudeQuotaSource.OtherMachine ? "other machine" : "this PC";
            var age = claude.CachedAge;
            var freshness = age is null ? "no reading yet" : age.Value.TotalMinutes < 1 ? "updated just now" : $"last update {Math.Max(1, (int)age.Value.TotalMinutes)} min ago";
            return $"Now using: {sourceName} · {freshness}";
        });
}
