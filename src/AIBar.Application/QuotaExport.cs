using System.Text;
using System.Text.Json;
using AIBar.Domain;

namespace AIBar.Application;

public enum QuotaExportState { Current, Refreshing, Stale, Unavailable, Disabled }
public enum QuotaExportWarning { RefreshFailed, AuthenticationFailed, Unavailable, Disabled }
public sealed record QuotaExportWindow(decimal PercentageUsed, DateTimeOffset? ResetAt);
public sealed record QuotaExportDocument(int SchemaVersion, DateTimeOffset GeneratedAt, QuotaExportState State, QuotaExportWarning? Warning, DateTimeOffset? SourceRetrievedAt, QuotaExportWindow? FiveHour, QuotaExportWindow? Weekly);
public sealed record QuotaExportInput(QuotaRefreshState Refresh, bool Disabled = false);
public interface IQuotaExportWriter { ValueTask WriteAsync(QuotaExportDocument document, DateTimeOffset generatedAt, CancellationToken cancellationToken); }

public static class QuotaExportProjector
{
    public static QuotaExportDocument Project(QuotaExportInput input, DateTimeOffset now)
    {
        if (input.Disabled) return QuotaExportWire.Disabled(now);
        var refresh = input.Refresh;
        var snapshot = refresh.Snapshot;
        var failure = refresh.Failure ?? refresh.OptionalFailure;
        var state = refresh.IsLoading ? QuotaExportState.Refreshing : snapshot is null ? QuotaExportState.Unavailable : failure is not null ? QuotaExportState.Stale : refresh.Freshness switch
        {
            FreshnessState.Current => QuotaExportState.Current,
            FreshnessState.Stale => QuotaExportState.Stale,
            _ => QuotaExportState.Unavailable,
        };
        if (state == QuotaExportState.Unavailable) snapshot = null;
        var warning = refresh.IsLoading ? null : Warning(failure, snapshot is not null);
        return QuotaExportWire.Normalize(new(1, now, state, warning, snapshot?.RetrievedAt, Window(snapshot?.Primary), Window(snapshot?.Weekly)), now);
    }

    private static QuotaExportWindow? Window(QuotaWindow? value) => value is null ? null : new(value.PercentageUsed, value.ResetAt);
    private static QuotaExportWarning? Warning(QuotaFailure? failure, bool hasCache)
    {
        if (failure is null) return null;
        if (failure.Kind is QuotaErrorKind.Authentication or QuotaErrorKind.Permission || failure.Kind == QuotaErrorKind.Unavailable && failure.SafeCode is "quota_credential_missing" or "quota_credential_unusable") return QuotaExportWarning.AuthenticationFailed;
        if (!hasCache || failure.Kind == QuotaErrorKind.Unavailable || !Enum.IsDefined(failure.Kind)) return QuotaExportWarning.Unavailable;
        return QuotaExportWarning.RefreshFailed;
    }
}

public static class QuotaExportWire
{
    public static string Serialize(QuotaExportDocument? document, DateTimeOffset now)
    {
        var value = Normalize(document, now);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("schemaVersion", value.SchemaVersion);
            json.WriteString("generatedAt", value.GeneratedAt);
            json.WriteString("state", State(value.State));
            if (value.Warning is { } warning) json.WriteString("warning", Warning(warning)); else json.WriteNull("warning");
            if (value.SourceRetrievedAt is { } source) json.WriteString("sourceRetrievedAt", source); else json.WriteNull("sourceRetrievedAt");
            WriteWindow(json, "fiveHour", value.FiveHour);
            WriteWindow(json, "weekly", value.Weekly);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static QuotaExportDocument Normalize(QuotaExportDocument? document, DateTimeOffset now)
    {
        if (!TryUtc(now, out var publication)) publication = DateTimeOffset.UnixEpoch;
        var fallback = new QuotaExportDocument(1, publication, QuotaExportState.Unavailable, QuotaExportWarning.Unavailable, null, null, null);
        if (document is null || document.SchemaVersion != 1 || !Enum.IsDefined(document.State) || document.Warning is { } warning && !Enum.IsDefined(warning) || !TryUtc(document.GeneratedAt, out var candidateTime) || candidateTime > publication) return fallback;
        DateTimeOffset? source = null;
        if (document.SourceRetrievedAt is { } candidateSource)
        {
            if (!TryUtc(candidateSource, out var normalizedSource) || normalizedSource > publication) return fallback;
            source = normalizedSource;
        }
        if (!TryWindow(document.FiveHour, source, out var fiveHour) || !TryWindow(document.Weekly, source, out var weekly)) return fallback;
        var hasValues = fiveHour is not null || weekly is not null;
        var consistent = document.State switch
        {
            QuotaExportState.Current => document.Warning is null && hasValues && source is not null,
            QuotaExportState.Refreshing => document.Warning is null && hasValues == (source is not null),
            QuotaExportState.Stale => document.Warning != QuotaExportWarning.Disabled && hasValues && source is not null,
            QuotaExportState.Unavailable => (document.Warning is null or QuotaExportWarning.AuthenticationFailed or QuotaExportWarning.Unavailable) && !hasValues && source is null,
            QuotaExportState.Disabled => document.Warning == QuotaExportWarning.Disabled && !hasValues && source is null,
            _ => false,
        };
        return consistent ? document with { GeneratedAt = publication, SourceRetrievedAt = source, FiveHour = fiveHour, Weekly = weekly } : fallback;
    }

    internal static QuotaExportDocument Disabled(DateTimeOffset now) => Normalize(new(1, now, QuotaExportState.Disabled, QuotaExportWarning.Disabled, null, null, null), now);
    private static bool TryUtc(DateTimeOffset value, out DateTimeOffset utc) { try { utc = value.ToUniversalTime(); return true; } catch (ArgumentException) { utc = default; return false; } }
    private static bool TryWindow(QuotaExportWindow? value, DateTimeOffset? source, out QuotaExportWindow? window)
    {
        window = null;
        if (value is null) return true;
        if (value.PercentageUsed is < 0 or > 100) return false;
        DateTimeOffset? reset = null;
        if (value.ResetAt is { } candidate)
        {
            if (!TryUtc(candidate, out var normalized) || source is { } retrieved && normalized < retrieved) return false;
            reset = normalized;
        }
        window = new(value.PercentageUsed, reset);
        return true;
    }
    private static void WriteWindow(Utf8JsonWriter json, string name, QuotaExportWindow? window)
    {
        if (window is null) { json.WriteNull(name); return; }
        json.WriteStartObject(name); json.WriteNumber("percentageUsed", window.PercentageUsed);
        if (window.ResetAt is { } reset) json.WriteString("resetAt", reset); else json.WriteNull("resetAt");
        json.WriteEndObject();
    }
    private static string State(QuotaExportState value) => value switch { QuotaExportState.Current => "current", QuotaExportState.Refreshing => "refreshing", QuotaExportState.Stale => "stale", QuotaExportState.Disabled => "disabled", _ => "unavailable" };
    private static string Warning(QuotaExportWarning value) => value switch { QuotaExportWarning.RefreshFailed => "refresh-failed", QuotaExportWarning.AuthenticationFailed => "authentication-failed", QuotaExportWarning.Disabled => "disabled", _ => "unavailable" };
}
