using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaAlertObserverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-alert-observer-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
    private static QuotaAuthorityUpdate Update(long generation, decimal used = 100, FreshnessState freshness = FreshnessState.Current) =>
        new(new(new(new(used, Now.AddHours(2)), null, Now), freshness, false, null, null), generation, generation);

    [Fact]
    public async Task Retrieval_generation_and_retrieved_time_dedupe_and_restart_preserves_fired_keys()
    {
        var delivered = new List<QuotaAlert>();
        await using (var owner = new QuotaAlertObserver(_root, new FixedClock(Now)))
        {
            owner.AlertReady += delivered.Add;
            owner.Observe("Codex", Update(0), () => true);
            owner.Observe("Codex", Update(1), () => true);
            owner.Observe("Codex", Update(1), () => true);
            owner.Observe("Codex", Update(2), () => true);
            await owner.Completion;
            Assert.Equal(3, delivered.Count(a => a.Kind == QuotaAlertKind.ThresholdCrossed));
        }
        delivered.Clear();
        await using var restarted = new QuotaAlertObserver(_root, new FixedClock(Now));
        restarted.AlertReady += delivered.Add;
        restarted.Observe("Codex", Update(1), () => true);
        await restarted.Completion;
        Assert.Empty(delivered);
        restarted.Observe("Claude", Update(1), () => true);
        await restarted.Completion;
        Assert.Equal(3, delivered.Count(a => a.Kind == QuotaAlertKind.ThresholdCrossed));
    }

    [Fact]
    public async Task Stale_unconsented_and_failed_samples_have_no_effect_and_clear_drains_then_resets()
    {
        await using var owner = new QuotaAlertObserver(_root, new FixedClock(Now));
        var delivered = new List<QuotaAlert>(); owner.AlertReady += delivered.Add;
        owner.Observe("Codex", Update(1, freshness: FreshnessState.Stale), () => true);
        owner.Observe("Claude", Update(1), () => false);
        await owner.Completion;
        Assert.Empty(delivered);
        owner.Observe("Codex", Update(2), () => true);
        await owner.Completion;
        Assert.NotEmpty(delivered);
        await owner.Settings.SaveAsync(new(false, false, false, false), default);
        await new ClearAiBarDataService(_root, [owner], _ => ValueTask.CompletedTask).ClearAsync(default);
        Assert.False(File.Exists(Path.Combine(_root, "alert-state.json")));
        Assert.False(File.Exists(Path.Combine(_root, "alerts.json")));
        Assert.Equal(new AlertOptions(), await owner.Settings.LoadAsync(default));
        delivered.Clear();
        owner.Observe("Codex", Update(3), () => true);
        await owner.Completion;
        Assert.NotEmpty(delivered);
    }

    [Fact]
    public async Task Disabled_events_are_not_replayed_when_reenabled()
    {
        await using var owner = new QuotaAlertObserver(_root, new FixedClock(Now));
        await owner.Settings.SaveAsync(new(false, false, false, false), default);
        var delivered = new List<QuotaAlert>(); owner.AlertReady += delivered.Add;
        owner.Observe("Codex", Update(1), () => true); await owner.Completion;
        Assert.Empty(delivered);
        await owner.Settings.SaveAsync(new(), default);
        owner.Observe("Codex", Update(2), () => true); await owner.Completion;
        Assert.Empty(delivered);
    }
    [Fact]
    public async Task Persistence_failure_never_delivers_and_does_not_escape_authority_callback()
    {
        Directory.CreateDirectory(_root);
        // A directory occupying the exact state filename makes atomic replacement fail.
        Directory.CreateDirectory(Path.Combine(_root, "alert-state.json"));
        await using var owner = new QuotaAlertObserver(_root, new FixedClock(Now));
        var delivered = new List<QuotaAlert>(); owner.AlertReady += delivered.Add;
        owner.Observe("Codex", Update(1), () => true);
        await owner.Completion;
        Assert.Empty(delivered);
        Assert.True(Directory.Exists(Path.Combine(_root, "alert-state.json")));
    }

    [Fact]
    public async Task Failure_state_never_delivers_or_persists_samples()
    {
        await using var owner = new QuotaAlertObserver(_root, new FixedClock(Now));
        var delivered = new List<QuotaAlert>(); owner.AlertReady += delivered.Add;
        var failed = Update(1);
        owner.Observe("Codex", failed with { State = failed.State with { Failure = new(QuotaErrorKind.Network, "synthetic") } }, () => true);
        await owner.Completion;
        Assert.Empty(delivered);
        Assert.False(File.Exists(Path.Combine(_root, "alert-state.json")));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
