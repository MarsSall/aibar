using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Claude-only lifecycle owner. RED scaffold; never composed by production yet.</summary>
public sealed class ClaudeQuotaRuntime : IAsyncDisposable, IAiBarClearWork
{
    // The factory must remain lazy: no Claude provider/credential resources before opt-in.
    public ClaudeQuotaRuntime(ConsentSettings settings, IQuotaSnapshotStore store,
        Func<PrivateIntegrationPolicy, IQuotaProvider> providerFactory, IClock clock,
        IQuotaRefreshLifecycleEvents? lifecycle = null, TimeProvider? timeProvider = null) { }

    public static string DatabasePath(string dataDirectory) => throw new NotImplementedException();
    public string Disclosure => throw new NotImplementedException();
    public bool IsConsentEnabled => throw new NotImplementedException();
    public QuotaRefreshState State => throw new NotImplementedException();
    public TimeSpan? CachedAge => throw new NotImplementedException();
    public ValueTask InitializeAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public ValueTask GrantConsentAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public ValueTask RevokeConsentAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public ValueTask RefreshAsync(RefreshTrigger trigger, CancellationToken cancellationToken) => throw new NotImplementedException();
    public ValueTask CancelAndWaitAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public void ResumeAfterClear() => throw new NotImplementedException();
    public ValueTask DisposeAsync() => throw new NotImplementedException();
}
