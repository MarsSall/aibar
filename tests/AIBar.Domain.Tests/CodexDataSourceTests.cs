using AIBar.Application;
using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class CodexDataSourceTests
{
    [Theory]
    [InlineData(DataSource.ThisPc)]
    [InlineData(DataSource.Automatic)]
    public async Task Remote_only_blocks_credentials_endpoint_and_cached_values_then_local_refresh_resumes(DataSource local)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-codex-source-{Guid.NewGuid():N}");
        try
        {
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(true, default);
            await settings.SaveDataSourceAsync(DataSource.OtherMachine, default);
            var policy = new PrivateIntegrationPolicy();
            var files = new Files();
            var credentials = new ConsentCredentialSource(policy, new CodexCredentialReader(files), "synthetic");
            var provider = new Provider(credentials);
            var clock = new FixedClock();
            var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            await using var runtime = new BetaRuntime(settings, policy, coordinator, clock);
            await runtime.InitializeAsync(default);
            await runtime.RefreshAsync(RefreshTrigger.Manual, default);
            // Also protect callers that use the coordinator directly (panel and lifecycle).
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(0, files.Reads); Assert.Equal(0, provider.Calls);
            Assert.Null(runtime.State.Snapshot);
            var shown = new QuotaPresentationMapper(clock).Map(runtime.State);
            Assert.True(shown.IsUnavailable);
            Assert.Equal("No data from the other machine yet", shown.ErrorLabel);
            await using var claude = new ClaudeQuotaRuntime(settings, new Store(), _ => provider, clock,
                remoteProviderFactory: _ => provider, remoteFileAvailable: () => false);
            await claude.InitializeAsync(default);
            await SettingsProviderCommands.ApplySourceAsync(local, runtime, claude);
            Assert.Equal(local, runtime.Source);
            Assert.Equal(local.ToClaude(), claude.Source);
            Assert.Equal(local, await settings.LoadDataSourceAsync(default));
            Assert.False(claude.IsConsentEnabled); // Changing source never grants consent.
            Assert.True(provider.Calls > 0); Assert.True(files.Reads > 0);
            await runtime.SetSourceAsync(DataSource.OtherMachine, default);
            var calls = provider.Calls;
            await runtime.RevokeConsentAsync(default);
            await runtime.GrantConsentAsync(default);
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(calls, provider.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Switching_remote_drains_inflight_request_and_hides_its_late_local_result()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aibar-source-drain-{Guid.NewGuid():N}");
        try
        {
            var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
            await settings.SaveAsync(true, default);
            var provider = new BlockingProvider(); var clock = new FixedClock();
            var coordinator = new QuotaRefreshCoordinator(new Store(), provider, clock, new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
            await using var runtime = new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock);
            await runtime.InitializeAsync(default);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(60));
            var change = runtime.SetSourceAsync(DataSource.OtherMachine, default).AsTask();
            await provider.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(60));
            Assert.False(change.IsCompleted);
            provider.Result.SetResult(new(new(new(80, clock.UtcNow.AddHours(1)), null, clock.UtcNow), null));
            await change;
            Assert.Null(runtime.State.Snapshot);
            await coordinator.RefreshAsync(RefreshTrigger.Manual, default);
            Assert.Equal(1, provider.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class BlockingProvider : IQuotaProvider
    {
        public int Calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<QuotaProviderResult> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++; Started.TrySetResult();
            using var registration = cancellationToken.Register(() => Cancelled.TrySetResult());
            return await Result.Task;
        }
    }
    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2030-01-01T12:00:00Z"); }
    private sealed class Files : ICredentialFileReader
    {
        public int Reads;
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        { Reads++; return ValueTask.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"))); }
    }
    private sealed class Provider(ConsentCredentialSource credentials) : IQuotaProvider
    {
        public int Calls;
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        { Calls++; await credentials.GetAvailabilityAsync(cancellationToken); return new(null, new(QuotaErrorKind.Unavailable, "quota_credential_missing")); }
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<QuotaSnapshot?>(new(new(42, DateTimeOffset.UtcNow), null, DateTimeOffset.UtcNow));
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
