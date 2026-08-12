using System.Text;
using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class BetaConsentOrQuotaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-beta-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consent_is_default_off_and_persists_only_the_disclosed_decision()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));

        Assert.False(await settings.LoadAsync(default));
        await settings.SaveAsync(true, default);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "settings.json")));
        Assert.Equal(2, document.RootElement.EnumerateObject().Count());
        Assert.True(document.RootElement.GetProperty("privateCodexConsent").GetBoolean());
        Assert.Equal(1, document.RootElement.GetProperty("schema").GetInt32());
    }

    [Fact]
    public async Task Credential_availability_is_secret_free_and_disabled_lookup_is_suppressed()
    {
        var policy = new PrivateIntegrationPolicy();
        var files = new ScriptedCredentialFiles("{\"tokens\":{\"access_token\":\"synthetic-access\",\"account_id\":\"synthetic-account\"}}");
        var source = new ConsentCredentialSource(policy, new CodexCredentialReader(files), "codex");

        Assert.Equal(CredentialAvailability.Disabled, await source.GetAvailabilityAsync(default));
        Assert.Equal(0, files.Calls);

        policy.Enable();
        Assert.Equal(CredentialAvailability.Available, await source.GetAvailabilityAsync(default));
        Assert.Equal(1, files.Calls);

        files.Content = "{bad";
        Assert.Equal(CredentialAvailability.Missing, await source.GetAvailabilityAsync(default));
        files.Failure = new FileNotFoundException();
        Assert.Equal(CredentialAvailability.Missing, await source.GetAvailabilityAsync(default));
        files.Failure = new IOException("synthetic unreadable file");
        Assert.Equal(CredentialAvailability.Unusable, await source.GetAvailabilityAsync(default));
    }

    [Fact]
    public async Task Revocation_persists_disabled_cancels_work_and_rejects_late_results()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var provider = new BlockingProvider();
        await using var runtime = Runtime(settings, provider);
        await runtime.InitializeAsync(default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var revocation = runtime.RevokeConsentAsync(default).AsTask();
        Assert.False(revocation.IsCompleted);
        await WaitUntilAsync(() => !runtime.IsConsentEnabled);
        provider.Complete(Snapshot(Now));
        await revocation;

        Assert.False(await settings.LoadAsync(default));
        Assert.True(provider.CancellationObserved);
        Assert.Null(runtime.State.Snapshot);
        Assert.Equal(FreshnessState.Unavailable, runtime.State.Freshness);
    }

    [Fact]
    public async Task Runtime_composes_live_windows_cached_fallbacks_and_coalesced_triggers()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var clock = new MutableClock(Now);
        var provider = new ScriptedProvider(new(Snapshot(Now), null));
        await using var runtime = Runtime(settings, provider, clock, Snapshot(Now.AddMinutes(-12)));
        await runtime.InitializeAsync(default);
        await WaitUntilAsync(() => runtime.State.Snapshot?.RetrievedAt == Now && !runtime.State.IsLoading);

        Assert.Equal(42, runtime.State.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(20, runtime.State.Snapshot.Weekly!.PercentageUsed);
        Assert.Equal(FreshnessState.Current, runtime.State.Freshness);

        var gate = new TaskCompletionSource<QuotaProviderResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        provider.Next = gate.Task;
        var manual = runtime.RefreshAsync(RefreshTrigger.Manual, default).AsTask();
        var popup = runtime.RefreshAsync(RefreshTrigger.PopoverOpened, default).AsTask();
        var poll = runtime.RefreshAsync(RefreshTrigger.Poll, default).AsTask();
        var resume = runtime.RefreshAsync(RefreshTrigger.Resume, default).AsTask();
        var clockChange = runtime.RefreshAsync(RefreshTrigger.ClockChanged, default).AsTask();
        await WaitUntilAsync(() => provider.Calls == 2);
        Assert.True(runtime.State.IsLoading);

        clock.UtcNow = Now.AddMinutes(12);
        gate.SetResult(new(null, new(QuotaErrorKind.Network, "quota_network")));
        await manual;
        await Task.WhenAll(popup, poll, resume, clockChange);
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        Assert.Equal("quota_network", runtime.State.Failure!.SafeCode);
        Assert.Equal(TimeSpan.FromMinutes(12), runtime.CachedAge);
    }

    [Fact]
    public async Task Missing_credential_preserves_a_cached_snapshot_without_requesting_the_endpoint()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var policy = new PrivateIntegrationPolicy();
        policy.Enable();
        var files = new ScriptedCredentialFiles("{}");
        var provider = new QuotaHttpProvider(policy, new ConsentCredentialSource(policy, new CodexCredentialReader(files), "codex"));
        var store = new MemoryStore(Snapshot(Now.AddMinutes(-12)));
        var coordinator = new QuotaRefreshCoordinator(store, provider, new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        await using var runtime = new BetaRuntime(settings, policy, coordinator, new FixedClock(Now));
        await runtime.InitializeAsync(default);
        await WaitUntilAsync(() => runtime.State.Failure?.SafeCode == "quota_credential_missing");

        Assert.Equal(CredentialAvailability.Missing, runtime.CredentialAvailability);
        Assert.Equal(FreshnessState.Stale, runtime.State.Freshness);
        Assert.Equal("quota_credential_missing", runtime.State.Failure!.SafeCode);
        Assert.Equal(1, files.Calls);
    }

    [Fact]
    public async Task No_snapshot_and_unsafe_failure_are_unavailable_without_leaking_the_failure_content()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var provider = new ScriptedProvider(new(null, null)) { Next = Task.FromException<QuotaProviderResult>(new InvalidOperationException("Bearer secret-token")) };
        await using var runtime = Runtime(settings, provider);

        await runtime.InitializeAsync(default);
        await WaitUntilAsync(() => runtime.State.Failure is not null);

        Assert.Null(runtime.State.Snapshot);
        Assert.Equal(FreshnessState.Unavailable, runtime.State.Freshness);
        Assert.Equal("quota_refresh_failed", runtime.State.Failure!.SafeCode);
        Assert.DoesNotContain("secret-token", runtime.State.Failure.SafeCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_cancels_awaits_and_prevents_a_late_result_from_becoming_available()
    {
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await settings.SaveAsync(true, default);
        var provider = new BlockingProvider();
        var runtime = Runtime(settings, provider);
        await runtime.InitializeAsync(default);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var dispose = runtime.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        provider.Complete(Snapshot(Now));
        await dispose;

        Assert.True(provider.CancellationObserved);
        Assert.Null(runtime.State.Snapshot);
    }

    private BetaRuntime Runtime(ConsentSettings settings, IQuotaProvider provider, IClock? clock = null, QuotaSnapshot? cached = null)
    {
        var coordinator = new QuotaRefreshCoordinator(new MemoryStore(cached), provider, clock ?? new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
        return new BetaRuntime(settings, new PrivateIntegrationPolicy(), coordinator, clock ?? new FixedClock(Now));
    }

    private static QuotaSnapshot Snapshot(DateTimeOffset retrieved) => new(new(42, Now.AddHours(1)), new(20, Now.AddDays(1)), retrieved);
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class ScriptedCredentialFiles(string content) : ICredentialFileReader
    {
        public int Calls { get; private set; }
        public string Content { get; set; } = content;
        public Exception? Failure { get; set; }
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Content)));
        }
    }

    private sealed class MemoryStore(QuotaSnapshot? snapshot) : IQuotaSnapshotStore
    {
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(snapshot);
        public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class MutableClock(DateTimeOffset value) : IClock { public DateTimeOffset UtcNow { get; set; } = value; }

    private sealed class ScriptedProvider(QuotaProviderResult initial) : IQuotaProvider
    {
        public object Next { get; set; } = initial;
        public int Calls { get; private set; }
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Next switch
            {
                QuotaProviderResult result => ValueTask.FromResult(result),
                Task<QuotaProviderResult> pending => new(pending),
                _ => throw new InvalidOperationException(),
            };
        }
    }

    private sealed class BlockingProvider : IQuotaProvider
    {
        private readonly TaskCompletionSource<QuotaProviderResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool CancellationObserved { get; private set; }
        public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            using var registration = cancellationToken.Register(() => CancellationObserved = true);
            try { return await _result.Task; }
            finally { CancellationObserved |= cancellationToken.IsCancellationRequested; }
        }
        public void Complete(QuotaSnapshot snapshot) => _result.TrySetResult(new(snapshot, null));
    }
}
