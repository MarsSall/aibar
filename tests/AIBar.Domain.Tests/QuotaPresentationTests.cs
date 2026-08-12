using AIBar.Desktop;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaPresentationTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Maps_current_stale_loading_and_unavailable_without_fabricating_or_relabeling_data()
    {
        var mapper = new QuotaPresentationMapper(new FixedClock(Now));
        var current = mapper.Map(new(Snapshot(), FreshnessState.Current, false, null, null));
        var stale = mapper.Map(new(Snapshot(), FreshnessState.Stale, false, null, null));
        var loading = mapper.Map(new(Snapshot(), FreshnessState.Current, true, null, null));
        var unavailable = mapper.Map(new(null, FreshnessState.Unavailable, false, null, null));

        Assert.Equal(42, current.Primary.PercentageUsed); Assert.Equal(20, current.Weekly.PercentageUsed);
        Assert.True(current.IsCurrent); Assert.Equal("01:00:00", current.Primary.ResetCountdown);
        Assert.False(stale.IsCurrent); Assert.Equal("Stale", stale.FreshnessLabel); Assert.Equal(42, stale.Primary.PercentageUsed);
        Assert.False(loading.IsCurrent); Assert.Equal("Loading", loading.FreshnessLabel); Assert.Equal(42, loading.Primary.PercentageUsed);
        Assert.Null(unavailable.Primary.PercentageUsed); Assert.Equal("Unavailable", unavailable.FreshnessLabel);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Maps_each_quota_card_availability_from_its_service_window(bool hasPrimary, bool hasWeekly)
    {
        var snapshot = hasPrimary || hasWeekly
            ? new QuotaSnapshot(hasPrimary ? new(42, Now.AddHours(5)) : null, hasWeekly ? new(20, Now.AddDays(7)) : null, Now)
            : null;

        var view = new QuotaPresentationMapper(new FixedClock(Now)).Map(new(snapshot, FreshnessState.Current, false, null, null));

        Assert.Equal(hasPrimary, view.IsPrimaryAvailable);
        Assert.Equal(hasWeekly, view.IsWeeklyAvailable);
        Assert.Equal(hasPrimary ? 42m : null, view.Primary.PercentageUsed);
        Assert.Equal(hasWeekly ? 20m : null, view.Weekly.PercentageUsed);
    }

    [Fact]
    public void Retained_snapshot_with_failure_is_never_labeled_current()
    {
        var snapshot = new QuotaSnapshot(new(42, Now.AddMinutes(-1)), new(20, Now.AddDays(7)), Now.AddMinutes(-11));
        var view = new QuotaPresentationMapper(new FixedClock(Now)).Map(new(snapshot, FreshnessState.Current, false, new(QuotaErrorKind.Network, "quota_network"), null));

        Assert.Equal(42, view.Primary.PercentageUsed); Assert.Equal("00:00:00", view.Primary.ResetCountdown);
        Assert.False(view.IsCurrent); Assert.Equal("Stale", view.FreshnessLabel); Assert.Equal("Network unavailable", view.ErrorLabel);
    }

    [Theory]
    [InlineData(QuotaErrorKind.Authentication, "Authentication required")]
    [InlineData(QuotaErrorKind.Permission, "Permission denied")]
    [InlineData(QuotaErrorKind.MalformedResponse, "Service response unavailable")]
    [InlineData(QuotaErrorKind.Network, "Network unavailable")]
    [InlineData(QuotaErrorKind.Service, "Service unavailable")]
    public void Maps_safe_failure_states_without_sensitive_details(QuotaErrorKind kind, string label)
    {
        var state = new QuotaRefreshState(null, FreshnessState.Unavailable, false, new(kind, "safe_code"), null);
        var view = new QuotaPresentationMapper(new FixedClock(Now)).Map(state);

        Assert.Equal(label, view.ErrorLabel); Assert.Null(view.Primary.PercentageUsed); Assert.False(view.IsCurrent);
    }

    [Fact]
    public void Separates_service_quota_local_analytics_and_estimated_cost_disclosures()
    {
        var view = new QuotaPresentationMapper(new FixedClock(Now)).Map(new(Snapshot(), FreshnessState.Current, false, null, null));

        Assert.Equal("Private service-reported quota", view.QuotaDisclosure);
        Assert.Equal("Locally derived analytics", view.AnalyticsDisclosure);
        Assert.Contains("Estimated cost", view.CostDisclosure);
        Assert.Contains("private", view.PrivateEndpointDisclosure, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Now, view.RetrievedAt);
    }

    [Fact]
    public async Task Manual_refresh_command_serializes_requests_and_notifies_around_success()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0; var notifications = 0;
        var command = new ManualRefreshCommand(async _ => { calls++; started.SetResult(); await gate.Task; }, () => true);
        command.CanExecuteChanged += (_, _) => notifications++;

        var first = command.ExecuteAsync(default).AsTask(); await started.Task;
        Assert.False(command.CanExecute); await command.ExecuteAsync(default);
        Assert.Equal(1, calls); Assert.Equal(1, notifications);
        gate.SetResult(); await first;
        Assert.True(command.CanExecute); Assert.Equal(2, notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_refresh_command_resets_after_failure_or_cancellation(bool cancel)
    {
        var notifications = 0;
        var command = new ManualRefreshCommand(_ => cancel ? ValueTask.FromCanceled(new(true))
            : ValueTask.FromException(new InvalidOperationException()), () => true);
        command.CanExecuteChanged += (_, _) => notifications++;

        await Assert.ThrowsAnyAsync<Exception>(() => command.ExecuteAsync(default).AsTask());
        Assert.True(command.CanExecute); Assert.Equal(2, notifications);
    }

    private static QuotaSnapshot Snapshot() => new(new(42, Now.AddHours(1)), new(20, Now.AddDays(7)), Now);
}
