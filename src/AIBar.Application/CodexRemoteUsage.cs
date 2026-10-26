using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Reads only normalized percentages pushed into AIBar's data directory.</summary>
public sealed class CodexRemoteUsageProvider : IQuotaProvider, IQuotaPlanMetadata
{
    private readonly RemoteUsageReader _reader;
    public CodexRemoteUsageProvider(PrivateIntegrationPolicy policy, string? path = null,
        ICredentialFileReader? files = null, Func<string, FileAttributes>? attributes = null,
        Func<DateTimeOffset>? utcNow = null) =>
        _reader = new(policy, path ?? DefaultPath(), files, attributes, utcNow);
    public string? PlanLabel => _reader.PlanLabel;
    public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) => _reader.GetQuotaAsync(cancellationToken);
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar", "codex-remote-usage.json");
    public static bool IsAvailableForAutomatic(string? path = null, Func<string, FileAttributes>? attributes = null) =>
        RemoteUsageReader.IsAvailableForAutomatic(path ?? DefaultPath(), attributes);
}

// Only BetaRuntime may switch this delegate, after draining the shared authority.
internal sealed class CodexSourceProvider(PrivateIntegrationPolicy policy, IQuotaProvider local,
    Func<PrivateIntegrationPolicy, IQuotaProvider> remoteFactory) : IQuotaProvider, IQuotaPlanMetadata
{
    private IQuotaProvider? _remote;
    private bool _isRemote;
    public void Select(bool remote) => _isRemote = remote;
    public string? PlanLabel => policy.IsEnabled ? ((_isRemote ? _remote : local) as IQuotaPlanMetadata)?.PlanLabel : null;
    public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!policy.IsEnabled) return ValueTask.FromResult(new QuotaProviderResult(null, new(QuotaErrorKind.Unavailable, "quota_disabled")));
        // Selecting remote never constructs or invokes the local credential source.
        return (_isRemote ? _remote ??= remoteFactory(policy) : local).GetQuotaAsync(cancellationToken);
    }
}
