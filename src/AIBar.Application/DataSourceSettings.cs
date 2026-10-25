namespace AIBar.Application;

/// <summary>One source preference for all quota providers. Codex has no remote reader yet.</summary>
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
