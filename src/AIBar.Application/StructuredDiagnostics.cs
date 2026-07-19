using System.Text;

namespace AIBar.Application;

/// <summary>Defines the non-parsing diagnostic boundary: only fixed contracts and scalar values may cross it.</summary>
public enum DiagnosticCategory { Application, Quota }
public enum DiagnosticCode { RefreshStarted, RefreshCompleted, RefreshFailed, DataCleared, DiagnosticRejected }
public enum DiagnosticTimeBasis { Utc, ServiceReportedUtc }
public enum DiagnosticErrorKind { None, Authentication, Permission, Network, Service, Malformed, Unavailable }
public enum DiagnosticField { Attempt, DurationMilliseconds, HttpStatusClass, IsStale, WasCancelled }
public enum DiagnosticValueKind { Number, Boolean }

public readonly record struct DiagnosticFieldEntry(DiagnosticField Field, DiagnosticValueKind Kind, long NumericValue, bool BooleanValue)
{
    public static DiagnosticFieldEntry Number(DiagnosticField field, long value) => new(field, DiagnosticValueKind.Number, value, false);
    public static DiagnosticFieldEntry Boolean(DiagnosticField field, bool value) => new(field, DiagnosticValueKind.Boolean, 0, value);
}

public sealed record StructuredDiagnosticEvent(
    DiagnosticCategory Category,
    DiagnosticCode Code,
    DiagnosticTimeBasis TimeBasis,
    DiagnosticErrorKind ErrorKind,
    IReadOnlyList<DiagnosticFieldEntry> Fields);

public static class StructuredDiagnosticSerializer
{
    public const string RedactedFallback = "[REDACTED]";
    private const int MaximumFields = 8;

    public static string Serialize(StructuredDiagnosticEvent? diagnostic)
    {
        if (diagnostic is null || !IsKnown(diagnostic.Category) || !IsKnown(diagnostic.Code) || !IsKnown(diagnostic.TimeBasis) || diagnostic.Fields is null || !TryValidateFields(diagnostic.Fields))
            return RedactedFallback;

        var errorKind = IsKnown(diagnostic.ErrorKind) ? diagnostic.ErrorKind : DiagnosticErrorKind.Unavailable;
        var fields = diagnostic.Fields.OrderBy(field => (int)field.Field).ToArray();
        var builder = new StringBuilder(192);
        builder.Append("{\"category\":\"").Append(CategoryName(diagnostic.Category))
            .Append("\",\"code\":\"").Append(CodeName(diagnostic.Code))
            .Append("\",\"timeBasis\":\"").Append(TimeBasisName(diagnostic.TimeBasis))
            .Append("\",\"errorKind\":\"").Append(ErrorKindName(errorKind)).Append("\",\"fields\":{");
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0) builder.Append(',');
            var field = fields[index];
            builder.Append('"').Append(FieldName(field.Field)).Append("\":");
            if (field.Kind == DiagnosticValueKind.Number) builder.Append(field.NumericValue); else builder.Append(field.BooleanValue ? "true" : "false");
        }
        return builder.Append("}}").ToString();
    }

    private static bool TryValidateFields(IReadOnlyList<DiagnosticFieldEntry> fields)
    {
        if (fields.Count > MaximumFields) return false;
        var seen = new HashSet<DiagnosticField>();
        foreach (var field in fields)
        {
            if (!seen.Add(field.Field) || !IsKnown(field.Field) || !IsKnown(field.Kind) || !IsValidValue(field)) return false;
        }
        return true;
    }

    private static bool IsValidValue(DiagnosticFieldEntry field) => field.Field switch
    {
        DiagnosticField.Attempt => field.Kind == DiagnosticValueKind.Number && field.NumericValue is >= 0 and <= 1000,
        DiagnosticField.DurationMilliseconds => field.Kind == DiagnosticValueKind.Number && field.NumericValue is >= 0 and <= 86_400_000,
        DiagnosticField.HttpStatusClass => field.Kind == DiagnosticValueKind.Number && field.NumericValue is >= 1 and <= 5,
        DiagnosticField.IsStale or DiagnosticField.WasCancelled => field.Kind == DiagnosticValueKind.Boolean,
        _ => false
    };

    private static bool IsKnown<T>(T value) where T : struct, Enum => Enum.IsDefined(value);
    private static string CategoryName(DiagnosticCategory value) => value == DiagnosticCategory.Application ? "application" : "quota";
    private static string CodeName(DiagnosticCode value) => value switch { DiagnosticCode.RefreshStarted => "refresh_started", DiagnosticCode.RefreshCompleted => "refresh_completed", DiagnosticCode.RefreshFailed => "refresh_failed", DiagnosticCode.DataCleared => "data_cleared", _ => "diagnostic_rejected" };
    private static string TimeBasisName(DiagnosticTimeBasis value) => value == DiagnosticTimeBasis.Utc ? "utc" : "service_reported_utc";
    private static string ErrorKindName(DiagnosticErrorKind value) => value switch { DiagnosticErrorKind.None => "none", DiagnosticErrorKind.Authentication => "authentication", DiagnosticErrorKind.Permission => "permission", DiagnosticErrorKind.Network => "network", DiagnosticErrorKind.Service => "service", DiagnosticErrorKind.Malformed => "malformed", _ => "unavailable" };
    private static string FieldName(DiagnosticField value) => value switch { DiagnosticField.Attempt => "attempt", DiagnosticField.DurationMilliseconds => "duration_ms", DiagnosticField.HttpStatusClass => "http_status_class", DiagnosticField.IsStale => "is_stale", _ => "was_cancelled" };
}
