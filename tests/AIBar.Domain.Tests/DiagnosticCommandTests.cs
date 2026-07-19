using AIBar.Application;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class DiagnosticCommandTests
{
    private static readonly StructuredDiagnosticEvent ApplicationEvent = new(DiagnosticCategory.Application, DiagnosticCode.DataCleared, DiagnosticTimeBasis.Utc, DiagnosticErrorKind.None, []);
    private static readonly StructuredDiagnosticEvent QuotaEvent = new(DiagnosticCategory.Quota, DiagnosticCode.RefreshCompleted, DiagnosticTimeBasis.ServiceReportedUtc, DiagnosticErrorKind.None, [DiagnosticFieldEntry.Number(DiagnosticField.Attempt, 1)]);

    [Fact]
    public void Trusted_gesture_is_one_shot_and_preview_confirm_reports_empty_unavailable_category()
    {
        var command = Command(out _);
        var adapter = new DiagnosticTrayAdapter(command);

        var preview = adapter.HandleTrustedGesture();
        var result = adapter.Confirm(preview, DiagnosticCategory.Quota);

        Assert.True(preview.IsAvailable);
        Assert.Contains(DiagnosticCategory.Quota, preview.Categories);
        Assert.Equal(DiagnosticCommandState.Unavailable, result.State);
        Assert.Empty(result.Snapshots);
        Assert.Equal(DiagnosticCommandState.Unavailable, adapter.Confirm(preview, DiagnosticCategory.Quota).State);
    }

    [Fact]
    public void Forged_or_default_preview_cannot_authorize_or_replay_without_a_trusted_gesture()
    {
        var command = Command(out var sinks);
        var adapter = new DiagnosticTrayAdapter(command);
        Assert.True(sinks.Record(QuotaEvent));

        var forged = new DiagnosticPreview(true, [DiagnosticCategory.Quota], 0);
        Assert.Equal(DiagnosticCommandState.Unavailable, adapter.Confirm(forged, DiagnosticCategory.Quota).State);
        Assert.Equal(DiagnosticCommandState.Unavailable, adapter.Confirm(forged, DiagnosticCategory.Quota).State);
        DiagnosticPreview? defaultPreview = default;
        Assert.Equal(DiagnosticCommandState.Unavailable, adapter.Confirm(defaultPreview!, DiagnosticCategory.Quota).State);

        var trusted = adapter.HandleTrustedGesture();
        Assert.Equal(DiagnosticCommandState.Available, adapter.Confirm(trusted, DiagnosticCategory.Quota).State);
    }

    [Fact]
    public void Central_sinks_route_only_valid_structured_events_and_return_immutable_snapshots()
    {
        var command = Command(out var sinks);
        Assert.True(sinks.Record(ApplicationEvent));
        Assert.True(sinks.Record(QuotaEvent));
        Assert.False(sinks.Record(new((DiagnosticCategory)99, DiagnosticCode.RefreshCompleted, DiagnosticTimeBasis.Utc, DiagnosticErrorKind.None, [])));

        var adapter = new DiagnosticTrayAdapter(command);
        var result = adapter.Confirm(adapter.HandleTrustedGesture(), DiagnosticCategory.Quota);

        Assert.Equal(DiagnosticCommandState.Available, result.State);
        Assert.Equal("quota", Assert.Single(result.Snapshots).Category);
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticSnapshot>)result.Snapshots).Add(default));
        Assert.Single(sinks.Snapshot(DiagnosticCategory.Application));
        sinks.Clear();
        Assert.Empty(sinks.Snapshot(DiagnosticCategory.Application));
        Assert.Empty(sinks.Snapshot(DiagnosticCategory.Quota));
    }

    [Fact]
    public void Retention_is_bounded_by_count_size_and_age()
    {
        var now = new MutableClock(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        var sinks = new DiagnosticMemorySinks(new DiagnosticRetentionPolicy(1, 256, TimeSpan.FromMinutes(1)), now);
        Assert.True(sinks.Record(ApplicationEvent));
        now.UtcNow = now.UtcNow.AddMinutes(2);
        Assert.True(sinks.Record(QuotaEvent));

        Assert.Empty(sinks.Snapshot(DiagnosticCategory.Application));
        Assert.Single(sinks.Snapshot(DiagnosticCategory.Quota));
        Assert.False(new DiagnosticMemorySinks(new DiagnosticRetentionPolicy(2, 1, TimeSpan.FromHours(1)), now).Record(QuotaEvent));
    }

    [Fact]
    public void Retention_evicts_oldest_entries_within_the_same_category_count_bound()
    {
        var now = new MutableClock(DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        var sinks = new DiagnosticMemorySinks(new DiagnosticRetentionPolicy(2, 256, TimeSpan.FromHours(1)), now);

        Assert.True(sinks.Record(QuotaEvent));
        now.UtcNow = now.UtcNow.AddSeconds(1);
        Assert.True(sinks.Record(QuotaEvent));
        now.UtcNow = now.UtcNow.AddSeconds(1);
        Assert.True(sinks.Record(QuotaEvent));

        var retained = sinks.Snapshot(DiagnosticCategory.Quota);
        Assert.Equal(2, retained.Count);
        Assert.Equal(DateTimeOffset.Parse("2030-01-01T00:00:01Z"), retained[0].RecordedAt);
        Assert.Equal(DateTimeOffset.Parse("2030-01-01T00:00:02Z"), retained[1].RecordedAt);
    }

    [Fact]
    public async Task Concurrent_emission_clear_cancellation_and_replay_are_safe()
    {
        var command = Command(out var sinks);
        var adapter = new DiagnosticTrayAdapter(command);
        var preview = adapter.HandleTrustedGesture();
        adapter.Cancel(preview);
        var writes = Enumerable.Range(0, 100).Select(_ => Task.Run(() => sinks.Record(QuotaEvent)));
        var clears = Enumerable.Range(0, 10).Select(_ => Task.Run(sinks.Clear));

        await Task.WhenAll(writes.Concat(clears));

        Assert.Equal(DiagnosticCommandState.Unavailable, adapter.Confirm(preview, DiagnosticCategory.Quota).State);
        Assert.InRange(sinks.Snapshot(DiagnosticCategory.Quota).Count, 0, 32);
        Assert.True(adapter.HandleTrustedGesture().IsAvailable);
    }

    [Fact]
    public void Synthetic_tray_runtime_proves_safe_preview_confirm_and_no_file_or_network_operation()
    {
        var command = Command(out var sinks);
        var adapter = new DiagnosticTrayAdapter(command);
        Assert.True(sinks.Record(QuotaEvent));

        var preview = adapter.HandleTrustedGesture();
        var confirmed = adapter.Confirm(preview, DiagnosticCategory.Quota);
        var empty = adapter.Confirm(adapter.HandleTrustedGesture(), DiagnosticCategory.Application);

        Assert.True(preview.IsAvailable);
        Assert.Equal(DiagnosticCommandState.Available, confirmed.State);
        Assert.Equal(DiagnosticCommandState.Unavailable, empty.State);
        Assert.DoesNotContain(typeof(DiagnosticMemorySinks).GetMethods(), method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
    }

    private static DiagnosticCommand Command(out DiagnosticMemorySinks sinks)
    {
        sinks = new DiagnosticMemorySinks(new DiagnosticRetentionPolicy(32, 4096, TimeSpan.FromHours(1)), new MutableClock(DateTimeOffset.UtcNow));
        return new DiagnosticCommand(sinks);
    }

    private sealed class MutableClock(DateTimeOffset value) : IDiagnosticClock
    {
        public DateTimeOffset UtcNow { get; set; } = value;
    }
}
