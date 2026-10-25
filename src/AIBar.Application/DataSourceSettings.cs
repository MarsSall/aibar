namespace AIBar.Application;

/// <summary>Codex source preference, independent of ClaudeQuotaSource. Codex has no remote quota reader yet.</summary>
public enum DataSource { Automatic, ThisPc, OtherMachine }

public static class DataSourceMapping
{
    public static ClaudeQuotaSource ToClaude(this DataSource source) => source switch
    {
        DataSource.Automatic => ClaudeQuotaSource.Automatic,
        DataSource.ThisPc => ClaudeQuotaSource.ThisPc,
        DataSource.OtherMachine => ClaudeQuotaSource.OtherMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };
}
