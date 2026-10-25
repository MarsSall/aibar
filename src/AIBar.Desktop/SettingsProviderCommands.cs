using AIBar.Application;

namespace AIBar.Desktop;

/// <summary>Stages UI choices; existing runtime commands remain the only consent/source writers.</summary>
public sealed class SettingsProviderCommands(
    Func<bool> codexEnabled, Func<bool> claudeEnabled, Func<ClaudeQuotaSource> source,
    Func<bool> confirmCodex, Func<bool> confirmClaude,
    Func<bool, Task> setCodex, Func<bool, Task> setClaude, Func<ClaudeQuotaSource, Task> setSource,
    Func<string>? sourceDescription = null, SettingsGeneralCommands? general = null,
    Func<DataSource>? codexSource = null, Func<DataSource, Task>? setCodexSource = null)
{
    public bool CodexEnabled => codexEnabled();
    public bool ClaudeEnabled => claudeEnabled();
    public ClaudeQuotaSource Source => source();
    public DataSource CodexSource => codexSource?.Invoke() ?? DataSource.Automatic;
    public SettingsGeneralCommands? General => general;
    public string SourceDescription => sourceDescription?.Invoke() ?? "Source changes apply when you save.";

    public Task<bool> SaveAsync(bool codex, bool claude, ClaudeQuotaSource selectedSource) =>
        SaveAsync(codex, claude, CodexSource, selectedSource);

    public async Task<bool> SaveAsync(bool codex, bool claude, DataSource selectedCodexSource, ClaudeQuotaSource selectedSource)
    {
        // Collect both approvals before any write, and never prompt for revocation.
        if (codex && !CodexEnabled && !confirmCodex()) return false;
        if (claude && !ClaudeEnabled && !confirmClaude()) return false;
        if (selectedCodexSource != CodexSource && setCodexSource is not null) await setCodexSource(selectedCodexSource);
        if (selectedSource != Source) await setSource(selectedSource);
        if (codex != CodexEnabled) await setCodex(codex);
        if (claude != ClaudeEnabled) await setClaude(claude);
        return true;
    }

    public static SettingsProviderCommands Create(NativeSettingsCommands settings, ClaudeQuotaRuntime claude, BetaRuntime codex) => new(
        () => settings.PrivateIntegrationEnabled, () => claude.IsConsentEnabled, () => claude.Source,
        new WindowsPrivateIntegrationConsentPrompt().Confirm, new WindowsClaudeQuotaConsentPrompt().Confirm,
        enabled => (enabled ? settings.EnablePrivateIntegrationAsync(default) : settings.DisablePrivateIntegrationAsync(default)).AsTask(),
        enabled => (enabled ? claude.GrantConsentAsync(default) : claude.RevokeConsentAsync(default)).AsTask(),
        selected => claude.SetSourceAsync(selected, default).AsTask(),
        () => "Each source applies only to its provider when you save.",
        new(() => settings.LoadStartupAsync(default).AsTask(), () => settings.ToggleStartupAsync(default).AsTask(),
            ConfirmClear, () => settings.ClearAiBarDataAsync(default).AsTask()),
        codexSource: () => codex.Source, setCodexSource: selected => codex.SetSourceAsync(selected, default).AsTask());

    public const string ClearConfirmation = "Clear AIBar Data? This removes AIBar's local data and turns off Codex quota access. Your provider logins are not touched.";
    private static bool ConfirmClear()
    {
        using var dialog = new System.Windows.Forms.Form
        {
            Text = "Clear AIBar Data", Width = 460, Height = 210,
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent,
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false
        };
        var message = new System.Windows.Forms.Label { Text = ClearConfirmation, Left = 20, Top = 20, Width = 400, Height = 85 };
        var clear = new System.Windows.Forms.Button { Text = "Clear", AccessibleName = "Clear", DialogResult = System.Windows.Forms.DialogResult.OK, Left = 250, Top = 115 };
        var cancel = new System.Windows.Forms.Button { Text = "Cancel", AccessibleName = "Cancel", DialogResult = System.Windows.Forms.DialogResult.Cancel, Left = 335, Top = 115 };
        dialog.Controls.AddRange([message, clear, cancel]);
        dialog.AcceptButton = cancel; dialog.CancelButton = cancel;
        dialog.ActiveControl = cancel;
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK;
    }
}

/// <summary>Replaceable confirmation and native commands, independent of WPF event handlers.</summary>
public sealed class SettingsGeneralCommands(Func<Task<bool>> loadStartup, Func<Task<bool>> toggleStartup,
    Func<bool> confirmClear, Func<Task> clear)
{
    public Task<bool> LoadStartupAsync() => loadStartup();
    public Task<bool> ToggleStartupAsync() => toggleStartup();
    public async Task<bool> ClearAsync()
    {
        if (!confirmClear()) return false;
        await clear();
        return true;
    }
}
