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
