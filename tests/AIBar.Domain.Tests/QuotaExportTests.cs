using System.Text.Json;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Wire_schema_domains_and_allowlists_are_exact_and_closed()
    {
        var cases = new[] { Valid(QuotaExportState.Current), Valid(QuotaExportState.Refreshing), Valid(QuotaExportState.Stale, QuotaExportWarning.RefreshFailed), Valid(QuotaExportState.Stale, QuotaExportWarning.AuthenticationFailed), Valid(QuotaExportState.Unavailable, QuotaExportWarning.Unavailable, false), Valid(QuotaExportState.Disabled, QuotaExportWarning.Disabled, false) };
        var states = new[] { "current", "refreshing", "stale", "stale", "unavailable", "disabled" };
        var warnings = new string?[] { null, null, "refresh-failed", "authentication-failed", "unavailable", "disabled" };
        for (var index = 0; index < cases.Length; index++)
        {
            var root = Root(cases[index]);
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32()); Assert.Equal(states[index], root.GetProperty("state").GetString());
            Assert.Equal(warnings[index], root.GetProperty("warning").ValueKind == JsonValueKind.Null ? null : root.GetProperty("warning").GetString());
            Assert.Equal(new[] { "schemaVersion", "generatedAt", "state", "warning", "sourceRetrievedAt", "fiveHour", "weekly" }, root.EnumerateObject().Select(property => property.Name));
        }
        Assert.Equal(new[] { "percentageUsed", "resetAt" }, Root(cases[0]).GetProperty("fiveHour").EnumerateObject().Select(property => property.Name));
        Assert.Equal(typeof(decimal), typeof(QuotaExportWindow).GetProperty(nameof(QuotaExportWindow.PercentageUsed))!.PropertyType);
        Assert.Equal(new[] { "SchemaVersion", "GeneratedAt", "State", "Warning", "SourceRetrievedAt", "FiveHour", "Weekly" }, typeof(QuotaExportDocument).GetProperties().Select(property => property.Name));
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse("{\"percentageUsed\":NaN}"));
    }

    [Fact]
    public void Projector_covers_states_windows_failures_and_repository_credential_signals()
    {
        foreach (var snapshot in new[] { Snapshot(true, true), Snapshot(true, false), Snapshot(false, true) }) AssertProjection(new(snapshot, FreshnessState.Current, false, null, null), QuotaExportState.Current, null, snapshot.Primary is not null, snapshot.Weekly is not null);
        AssertProjection(new(Snapshot(), FreshnessState.Current, true, null, null), QuotaExportState.Refreshing, null, true, true);
        AssertProjection(new(Snapshot(false, true), FreshnessState.Current, true, null, null), QuotaExportState.Refreshing, null, false, true);
        AssertProjection(new(null, FreshnessState.Unavailable, true, null, null), QuotaExportState.Refreshing, null, false, false);
        AssertProjection(new(Snapshot(), FreshnessState.Stale, false, null, null), QuotaExportState.Stale, null, true, true);
        var noCache = new QuotaRefreshState(null, FreshnessState.Unavailable, false, null, null);
        AssertProjection(noCache, QuotaExportState.Unavailable, null, false, false);
        var noCacheRoot = Root(QuotaExportProjector.Project(new(noCache), Now));
        Assert.Equal(("unavailable", JsonValueKind.Null), (noCacheRoot.GetProperty("state").GetString(), noCacheRoot.GetProperty("warning").ValueKind));
        AssertProjection(new(Snapshot(), FreshnessState.Current, false, null, null), QuotaExportState.Disabled, QuotaExportWarning.Disabled, false, false, true);
        foreach (var kind in new[] { QuotaErrorKind.Network, QuotaErrorKind.Service, QuotaErrorKind.Redirect, QuotaErrorKind.MalformedResponse }) AssertProjection(Failed(Snapshot(), kind), QuotaExportState.Stale, QuotaExportWarning.RefreshFailed, true, true);
        AssertProjection(Failed(Snapshot(false, true), QuotaErrorKind.Service), QuotaExportState.Stale, QuotaExportWarning.RefreshFailed, false, true);
        foreach (var kind in new[] { QuotaErrorKind.Authentication, QuotaErrorKind.Permission }) { AssertProjection(Failed(Snapshot(), kind), QuotaExportState.Stale, QuotaExportWarning.AuthenticationFailed, true, true); AssertProjection(Failed(null, kind), QuotaExportState.Unavailable, QuotaExportWarning.AuthenticationFailed, false, false); }
        foreach (var code in new[] { "quota_credential_missing", "quota_credential_unusable" }) { AssertProjection(Failed(Snapshot(), QuotaErrorKind.Unavailable, code), QuotaExportState.Stale, QuotaExportWarning.AuthenticationFailed, true, true); var state = Failed(null, QuotaErrorKind.Unavailable, code); AssertProjection(state, QuotaExportState.Unavailable, QuotaExportWarning.AuthenticationFailed, false, false); Assert.DoesNotContain(code, QuotaExportWire.Serialize(QuotaExportProjector.Project(new(state), Now), Now)); }
        foreach (var kind in new[] { QuotaErrorKind.Network, QuotaErrorKind.Service, QuotaErrorKind.Redirect, QuotaErrorKind.MalformedResponse, (QuotaErrorKind)999 }) AssertProjection(Failed(null, kind), QuotaExportState.Unavailable, QuotaExportWarning.Unavailable, false, false);
        AssertProjection(Failed(Snapshot(), QuotaErrorKind.Unavailable), QuotaExportState.Stale, QuotaExportWarning.Unavailable, true, true);
        AssertProjection(Failed(null, QuotaErrorKind.Unavailable), QuotaExportState.Unavailable, QuotaExportWarning.Unavailable, false, false);
        AssertProjection(Failed(Snapshot(), (QuotaErrorKind)999), QuotaExportState.Stale, QuotaExportWarning.Unavailable, true, true);
        AssertProjection(new(Snapshot(), FreshnessState.Current, false, null, new(QuotaErrorKind.Service, "optional_response_secret")), QuotaExportState.Stale, QuotaExportWarning.RefreshFailed, true, true); AssertProjection(new(null, FreshnessState.Unavailable, false, null, new(QuotaErrorKind.Service, "optional_response_secret")), QuotaExportState.Unavailable, QuotaExportWarning.Unavailable, false, false);
    }

    [Fact]
    public void Values_times_and_nullable_resets_validate_and_normalize_as_one_document()
    {
        foreach (var percentage in new[] { 0m, 100m, 12.5m }) Assert.Equal(percentage, Root(Valid(window: new(percentage, null))).GetProperty("fiveHour").GetProperty("percentageUsed").GetDecimal());
        var source = Now.AddHours(-2).ToOffset(TimeSpan.FromHours(2));
        foreach (var reset in new DateTimeOffset?[] { Now.AddHours(-1).ToOffset(TimeSpan.FromHours(3)), Now, Now.AddHours(1), null })
        {
            var root = Root(Valid(window: new(42.25m, reset), source: source)); Assert.Equal(TimeSpan.Zero, root.GetProperty("sourceRetrievedAt").GetDateTimeOffset().Offset);
            if (reset is null) Assert.Equal(JsonValueKind.Null, root.GetProperty("fiveHour").GetProperty("resetAt").ValueKind); else Assert.Equal(TimeSpan.Zero, root.GetProperty("fiveHour").GetProperty("resetAt").GetDateTimeOffset().Offset);
        }
        Assert.Equal(Now, Root(Valid(source: Now)).GetProperty("sourceRetrievedAt").GetDateTimeOffset());
        Assert.Equal(Now, Root(Valid()).GetProperty("generatedAt").GetDateTimeOffset());
        foreach (var percentage in new[] { -0.01m, 100.01m, decimal.MaxValue }) AssertFallback(Valid(window: new(percentage, Now)));
        AssertFallback(Valid(source: Now.AddTicks(1))); AssertFallback(Valid(window: new(1, Now.AddHours(-3)), source: Now.AddHours(-2)));
        Assert.NotEqual("unavailable", Root(Valid(window: new(1, Now.AddHours(-2)), source: Now.AddHours(-2))).GetProperty("state").GetString());
    }

    [Fact]
    public void Retained_cache_republication_advances_generated_time_without_reaging_source()
    {
        var cached = Snapshot(false, true);
        var refresh = Failed(cached, QuotaErrorKind.Service);
        var later = Now.AddMinutes(5);
        var first = Root(QuotaExportProjector.Project(new(refresh), Now), Now);
        var second = Root(QuotaExportProjector.Project(new(refresh), later), later);
        Assert.Equal((Now, later), (first.GetProperty("generatedAt").GetDateTimeOffset(), second.GetProperty("generatedAt").GetDateTimeOffset()));
        Assert.True(second.GetProperty("generatedAt").GetDateTimeOffset() > first.GetProperty("generatedAt").GetDateTimeOffset());
        Assert.Equal(first.GetProperty("sourceRetrievedAt").GetRawText(), second.GetProperty("sourceRetrievedAt").GetRawText());
        Assert.Equal((cached.RetrievedAt, cached.RetrievedAt), (first.GetProperty("sourceRetrievedAt").GetDateTimeOffset(), second.GetProperty("sourceRetrievedAt").GetDateTimeOffset()));
        Assert.Equal((TimeSpan.Zero, TimeSpan.Zero), (first.GetProperty("sourceRetrievedAt").GetDateTimeOffset().Offset, second.GetProperty("sourceRetrievedAt").GetDateTimeOffset().Offset));
    }

    [Fact]
    public void Arbitrary_direct_documents_fail_closed_without_throwing_or_partial_retention()
    {
        var value = Valid();
        QuotaExportDocument?[] invalid = { null, value with { SchemaVersion = 2 }, value with { State = (QuotaExportState)99 }, value with { Warning = (QuotaExportWarning)99 }, value with { State = QuotaExportState.Disabled, Warning = QuotaExportWarning.Disabled }, value with { State = QuotaExportState.Unavailable, Warning = QuotaExportWarning.Unavailable }, value with { SourceRetrievedAt = null }, value with { FiveHour = null, Weekly = null }, value with { Warning = QuotaExportWarning.RefreshFailed }, value with { GeneratedAt = Now.AddTicks(1) } };
        foreach (var document in invalid) AssertFallback(document);
    }

    [Fact]
    public void Source_models_and_failure_text_cannot_enter_the_closed_wire_shape()
    {
        var source = new ForbiddenSource("credential-value", "token-value", "account-value", "plan-value", "endpoint-value", "path-value", "safeCode-value", "response-value", "exception-value", "message-value", "diagnostic-value", "session-value", "analytics-value", "prompt-value", "log-value", "environment-value", "root-value", "db-value", "database-value", "source-model-value");
        var json = QuotaExportWire.Serialize(QuotaExportProjector.Project(new(Failed(Snapshot(), QuotaErrorKind.Unavailable, JsonSerializer.Serialize(source))), Now), Now);
        var exportNames = typeof(QuotaExportDocument).GetProperties().Concat(typeof(QuotaExportWindow).GetProperties()).Select(property => property.Name);
        foreach (var property in typeof(ForbiddenSource).GetProperties()) { Assert.DoesNotContain(property.Name, exportNames, StringComparer.OrdinalIgnoreCase); Assert.DoesNotContain(property.Name, json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain(property.GetValue(source)!.ToString()!, json, StringComparison.Ordinal); }
    }

    private static void AssertProjection(QuotaRefreshState state, QuotaExportState expectedState, QuotaExportWarning? warning, bool five, bool weekly, bool disabled = false)
    {
        var value = QuotaExportProjector.Project(new(state, disabled), Now); Assert.Equal((expectedState, warning, five, weekly), (value.State, value.Warning, value.FiveHour is not null, value.Weekly is not null));
        Assert.Equal(five ? new QuotaExportWindow(state.Snapshot!.Primary!.PercentageUsed, state.Snapshot.Primary.ResetAt) : null, value.FiveHour);
        Assert.Equal(weekly ? new QuotaExportWindow(state.Snapshot!.Weekly!.PercentageUsed, state.Snapshot.Weekly.ResetAt) : null, value.Weekly);
        Assert.Equal(state.Snapshot is null || disabled ? null : state.Snapshot.RetrievedAt, value.SourceRetrievedAt); Assert.Equal(Now, value.GeneratedAt);
    }
    private static QuotaRefreshState Failed(QuotaSnapshot? snapshot, QuotaErrorKind kind, string code = "synthetic") => new(snapshot, snapshot is null ? FreshnessState.Unavailable : FreshnessState.Stale, false, new(kind, code), null);
    private static QuotaSnapshot Snapshot(bool five = true, bool weekly = true) => new(five ? new(10, Now.AddHours(1)) : null, weekly ? new(20, Now.AddDays(1)) : null, Now.AddHours(-2));
    private static QuotaExportDocument Valid(QuotaExportState state = QuotaExportState.Current, QuotaExportWarning? warning = null, bool values = true, QuotaExportWindow? window = null, DateTimeOffset? source = null) => new(1, Now.AddHours(-1), state, warning, values ? source ?? Now.AddHours(-2) : null, values ? window ?? new(10, Now.AddHours(1)) : null, null);
        private static JsonElement Root(QuotaExportDocument? value, DateTimeOffset? now = null) => JsonDocument.Parse(QuotaExportWire.Serialize(value, now ?? Now)).RootElement.Clone();
        private static void AssertFallback(QuotaExportDocument? value)
        {
            var root = Root(value);
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(Now, root.GetProperty("generatedAt").GetDateTimeOffset());
            Assert.Equal(new[] { "schemaVersion", "generatedAt", "state", "warning", "sourceRetrievedAt", "fiveHour", "weekly" }, root.EnumerateObject().Select(property => property.Name));
            Assert.Equal(("unavailable", "unavailable"), (root.GetProperty("state").GetString(), root.GetProperty("warning").GetString()));
            Assert.All(new[] { "sourceRetrievedAt", "fiveHour", "weekly" }, name => Assert.Equal(JsonValueKind.Null, root.GetProperty(name).ValueKind));
        }
    private sealed record ForbiddenSource(string Credential, string Token, string Account, string Plan, string Endpoint, string Path, string SafeCode, string Response, string Exception, string Message, string Diagnostic, string Session, string Analytics, string Prompt, string Log, string Environment, string Root, string Db, string Database, string SourceModel);
}

public sealed class QuotaExportPublisherTests
{
private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

[Fact]
public async Task Generation_is_an_exclusive_post_enable_unlock_not_freshness()
{
    await using var authority = Authority(); var writer = new MemoryWriter(); await using var publisher = new QuotaExportPublisher(authority, writer, new FixedClock(Now));
    await publisher.EnableAsync(default);
    var current = State(); var loading = current with { IsLoading = true }; var failed = current with { Freshness = FreshnessState.Stale, Failure = new(QuotaErrorKind.Service, "synthetic") };
    publisher.Accept(new(current, 1, 0)); publisher.Accept(new(loading, 2, 0)); publisher.Accept(new(failed, 3, 0)); publisher.Accept(new(failed, 4, 0));
    Assert.Single(writer.Documents);
    publisher.Accept(new(current, 5, 1)); await publisher.PublicationCompletion;
    Assert.Equal(QuotaExportState.Current, writer.Documents[^1].State);
    await publisher.DisableAsync(default); await publisher.EnableAsync(default); var baselineCount = writer.Documents.Count;
    publisher.Accept(new(current, 6, 1)); Assert.Equal(baselineCount, writer.Documents.Count);
    publisher.Accept(new(current, 7, 2)); await publisher.PublicationCompletion;
    Assert.Equal(QuotaExportState.Current, writer.Documents[^1].State);
}

[Fact]
public async Task Enabled_attempt_without_snapshot_replaces_disabled_export_with_sanitized_unavailable()
{
    await using var authority = Authority(); var writer = new MemoryWriter(); await using var publisher = new QuotaExportPublisher(authority, writer, new FixedClock(Now));
    await publisher.EnableAsync(default);

    publisher.Accept(new(new(null, FreshnessState.Unavailable, true, null, null), 1, 0));
    publisher.Accept(new(new(null, FreshnessState.Unavailable, false, new(QuotaErrorKind.Redirect, "https://secret.example/path?token=private"), null), 2, 0));
    await publisher.PublicationCompletion;

    Assert.Equal(QuotaExportState.Unavailable, writer.Documents[^1].State);
    Assert.Equal(QuotaExportWarning.Unavailable, writer.Documents[^1].Warning);
    Assert.DoesNotContain("secret", QuotaExportWire.Serialize(writer.Documents[^1], Now), StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task Concurrent_bursts_coalesce_intermediates_but_retain_sequence_latest_freshness()
{
    var provider = new ProbeProvider(); await using var authority = Authority(provider); var writer = new MemoryWriter(); await using var publisher = new QuotaExportPublisher(authority, writer, new FixedClock(Now));
    await publisher.EnableAsync(default); writer.Block = writer.IgnoreCancellation = true;
    var current = State(); publisher.Accept(new(current, 1, 1)); await writer.Started.Task;
    var loading = current with { IsLoading = true }; var stale = current with { Freshness = FreshnessState.Stale, Failure = new(QuotaErrorKind.Service, "synthetic") };
    Parallel.Invoke(() => publisher.Accept(new(stale, 3, 1)), () => publisher.Accept(new(loading, 2, 1)));
    writer.Release.SetResult(); await publisher.PublicationCompletion;
    Assert.Equal(new[] { QuotaExportState.Disabled, QuotaExportState.Current, QuotaExportState.Stale }, writer.Documents.Select(value => value.State));
    var count = writer.Documents.Count; publisher.Accept(new(stale, 4, 1)); await publisher.PublicationCompletion;
    Assert.Equal(count + 1, writer.Documents.Count); Assert.Equal(0, provider.Calls);
}

[Theory]
[InlineData(false)]
[InlineData(true)]
public async Task Privacy_and_disposal_cancel_resistant_old_writes_before_safe_completion(bool dispose)
{
    await using var authority = Authority(); var writer = new MemoryWriter(); var publisher = new QuotaExportPublisher(authority, writer, new FixedClock(Now));
    await publisher.EnableAsync(default); writer.Block = writer.IgnoreCancellation = true;
    publisher.Accept(new(State(), 1, 1)); await writer.Started.Task;
    var privacy = dispose ? publisher.DisposeAsync().AsTask() : publisher.DisableAsync(default).AsTask();
    await WaitUntilAsync(() => writer.CancellationObserved); Assert.False(privacy.IsCompleted);
    writer.Release.SetResult(); await privacy;
    Assert.Equal(QuotaExportState.Disabled, writer.Documents[^1].State);
    if (!dispose) { await publisher.DisableAsync(default); await publisher.DisposeAsync(); }
    publisher.Accept(new(State(), 2, 2)); Assert.Equal(QuotaExportState.Disabled, writer.Documents[^1].State);
}

[Fact]
public async Task Writer_and_privacy_failures_are_isolated_or_propagated_fail_closed()
{
    var clock = new MutableClock(Now); var provider = new ProbeProvider(); await using var authority = Authority(provider, clock); var writer = new MemoryWriter(); var publisher = new QuotaExportPublisher(authority, writer, clock);
    await publisher.EnableAsync(default); writer.FailNext = true;
    await authority.InitializeAsync(default); await authority.RefreshAsync(RefreshTrigger.Manual, default); await publisher.PublicationCompletion;
    Assert.NotNull(authority.State.Snapshot); Assert.Single(writer.Documents);
    clock.UtcNow = Now.AddHours(1); await authority.ReevaluateAsync(RefreshTrigger.Sleep, default); await publisher.PublicationCompletion;
    Assert.Equal(QuotaExportState.Stale, writer.Documents[^1].State);
    writer.FailNext = true; await Assert.ThrowsAsync<IOException>(() => publisher.DisableAsync(default).AsTask());
    var count = writer.Documents.Count; publisher.Accept(new(State(), 99, 3)); await publisher.PublicationCompletion; Assert.Equal(count, writer.Documents.Count);
    await publisher.DisposeAsync();
}

private static QuotaRefreshState State() => new(new(new(10, Now.AddHours(1)), new(20, Now.AddDays(1)), Now), FreshnessState.Current, false, null, null);
private static QuotaRefreshCoordinator Authority(ProbeProvider? provider = null, IClock? clock = null) => new(new MemoryStore(), provider ?? new(), clock ?? new FixedClock(Now), new FreshnessPolicy(TimeSpan.FromMinutes(10)), TimeSpan.Zero);
private static async Task WaitUntilAsync(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(10); Assert.True(condition()); }

private sealed class MemoryWriter : IQuotaExportWriter
{
    private readonly object _gate = new(); private readonly List<QuotaExportDocument> _documents = [];
    public IReadOnlyList<QuotaExportDocument> Documents { get { lock (_gate) return [.. _documents]; } }
    public bool Block { get; set; } public bool IgnoreCancellation { get; set; } public bool FailNext { get; set; } public bool CancellationObserved { get; private set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
using var registration = cancellationToken.Register(() => CancellationObserved = true);
if (FailNext) { FailNext = false; throw new IOException("synthetic writer failure"); }
if (Block) { Started.TrySetResult(); if (IgnoreCancellation) await Release.Task; else await Release.Task.WaitAsync(cancellationToken); }
lock (_gate) _documents.Add(document);
    }
}
private sealed class MemoryStore : IQuotaSnapshotStore { public ValueTask<QuotaSnapshot?> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult<QuotaSnapshot?>(null); public ValueTask SaveAsync(QuotaSnapshot value, CancellationToken cancellationToken) => ValueTask.CompletedTask; public ValueTask ClearAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask; }
private sealed class ProbeProvider : IQuotaProvider { public int Calls { get; private set; } public ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken) { Calls++; return ValueTask.FromResult(new QuotaProviderResult(State().Snapshot, null)); } }
private sealed class MutableClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }
}
