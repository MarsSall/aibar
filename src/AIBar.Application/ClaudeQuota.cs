using AIBar.Domain;

namespace AIBar.Application;

// RED-only API scaffold. No credential access, HTTP, or refresh is implemented yet.
public static class ClaudeRootResolver
{
    public static string Resolve(string? claudeConfigDir, string userProfile) => throw new NotImplementedException();
}

public sealed class ClaudeRequestCredential : IDisposable
{
    public ClaudeRequestCredential(string accessToken, DateTimeOffset? expiresAt, string? subscriptionType) { }

    public string? AccessToken => throw new NotImplementedException();
    public DateTimeOffset? ExpiresAt => throw new NotImplementedException();
    public string? SubscriptionType => throw new NotImplementedException();
    public void Dispose() => throw new NotImplementedException();
    public override string ToString() => throw new NotImplementedException();
}

public sealed class ClaudeCredentialReader
{
    public ClaudeCredentialReader(ICredentialFileReader files) { }

    public ValueTask<ClaudeRequestCredential?> ReadAsync(string root, CancellationToken cancellationToken) => throw new NotImplementedException();
}

public sealed class ClaudeConsentCredentialSource
{
    public ClaudeConsentCredentialSource(PrivateIntegrationPolicy policy, ClaudeCredentialReader reader, string root) { }

    public CredentialAvailability LastAvailability => throw new NotImplementedException();
    public ValueTask<CredentialAvailability> GetAvailabilityAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}

public sealed class ClaudeQuotaHttpProvider : IQuotaProvider
{
    public ClaudeQuotaHttpProvider(PrivateIntegrationPolicy policy, ClaudeConsentCredentialSource credentials, TimeSpan? connectTimeout = null, TimeSpan? requestTimeout = null) { }

    public ClaudeQuotaHttpProvider(HttpMessageHandler handler, PrivateIntegrationPolicy policy, ClaudeConsentCredentialSource credentials, TimeSpan? requestTimeout = null, Func<DateTimeOffset>? utcNow = null, Func<TimeSpan, CancellationToken, Task>? delay = null) { }

    public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
}
