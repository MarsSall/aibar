using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaAlertClaudeHookTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-alert-claude-{Guid.NewGuid():N}");
    [Fact]
    public async Task Authority_notifications_survive_every_source_handoff_and_consent_recreation()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new ConsentSettings(Path.Combine(_root, "settings.json"));
        await using var runtime = new ClaudeQuotaRuntime(settings, new Store(), _ => new Provider(now), new FixedClock(now),
            remoteProviderFactory: _ => new Provider(now), remoteFileAvailable: () => false);
        var generations = new List<long>();
        runtime.AuthorityUpdated += update =>
        {
            if (update.State.Snapshot is not null && update.RetrievalGeneration > generations.LastOrDefault())
                generations.Add(update.RetrievalGeneration);
        };
        await runtime.GrantConsentAsync(default);
        await runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        await runtime.SetSourceAsync(ClaudeQuotaSource.ThisPc, default);
        await runtime.RevokeConsentAsync(default);
        await runtime.GrantConsentAsync(default);
        Assert.Equal(new long[] { 1, 2, 3, 4 }, generations);
    }
    [Fact]
    public async Task Authority_callbacks_are_outside_runtime_gate_and_can_change_source()
    {
        var now = DateTimeOffset.UtcNow;
        await using var runtime = new ClaudeQuotaRuntime(new(Path.Combine(_root, "settings.json")), new Store(),
            _ => new Provider(now), new FixedClock(now), remoteProviderFactory: _ => new Provider(now), remoteFileAvailable: () => false);
        var changed = false;
        Task? handoff = null;
        runtime.AuthorityUpdated += update =>
        {
            if (changed || update.RetrievalGeneration == 0 || update.State.Snapshot is null) return;
            changed = true;
            handoff = runtime.SetSourceAsync(ClaudeQuotaSource.OtherMachine, default).AsTask();
        };
        await runtime.GrantConsentAsync(default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(changed);
        await handoff!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, runtime.Source);
    }
    private sealed class Provider(DateTimeOffset now) : IQuotaProvider
    {
        public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new QuotaProviderResult(new(new(76, now.AddHours(2)), null, now), null));
    }
    private sealed class Store : IQuotaSnapshotStore
    {
        private QuotaSnapshot? _value;
        public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(_value);
        public ValueTask SaveAsync(QuotaSnapshot snapshot, CancellationToken cancellationToken) { _value = snapshot; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken cancellationToken) { _value = null; return ValueTask.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
