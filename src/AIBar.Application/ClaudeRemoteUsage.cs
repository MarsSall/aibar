using AIBar.Domain;

namespace AIBar.Application;

public enum ClaudeQuotaSource { ThisPc, OtherMachine }

/// <summary>Read-only remote-file source. RED scaffold: intentionally not implemented.</summary>
public sealed class ClaudeRemoteUsageProvider : IQuotaProvider
{
    public ClaudeRemoteUsageProvider(PrivateIntegrationPolicy policy, string? path = null,
        ICredentialFileReader? files = null, Func<string, FileAttributes>? attributes = null,
        Func<DateTimeOffset>? utcNow = null) { }

    public static string DefaultPath() => throw new NotImplementedException();

    public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
