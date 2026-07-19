using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class StructuredDiagnosticTests
{
    [Fact]
    public void Serializes_only_allowlisted_typed_fields_in_a_deterministic_order()
    {
        var diagnostic = ValidDiagnostic([
            DiagnosticFieldEntry.Boolean(DiagnosticField.IsStale, true),
            DiagnosticFieldEntry.Number(DiagnosticField.DurationMilliseconds, 25),
            DiagnosticFieldEntry.Number(DiagnosticField.Attempt, 2)
        ]);

        var first = StructuredDiagnosticSerializer.Serialize(diagnostic);
        var second = StructuredDiagnosticSerializer.Serialize(diagnostic);

        Assert.Equal(first, second);
        Assert.Equal("{\"category\":\"quota\",\"code\":\"refresh_completed\",\"timeBasis\":\"service_reported_utc\",\"errorKind\":\"none\",\"fields\":{\"attempt\":2,\"duration_ms\":25,\"is_stale\":true}}", first);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    public void Unknown_categories_and_codes_fail_closed_without_retaining_input(int value)
    {
        var diagnostic = new StructuredDiagnosticEvent((DiagnosticCategory)value, (DiagnosticCode)value, DiagnosticTimeBasis.Utc, DiagnosticErrorKind.None, []);

        var serialized = StructuredDiagnosticSerializer.Serialize(diagnostic);

        Assert.Equal(StructuredDiagnosticSerializer.RedactedFallback, serialized);
        Assert.DoesNotContain(value.ToString(), serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_error_kind_uses_the_safe_error_fallback()
    {
        var serialized = StructuredDiagnosticSerializer.Serialize(ValidDiagnostic([], (DiagnosticErrorKind)99));

        Assert.Contains("\"errorKind\":\"unavailable\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("99", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unknown_keys_wrong_value_kinds_duplicate_keys_and_nested_values()
    {
        var unknown = ValidDiagnostic([DiagnosticFieldEntry.Number((DiagnosticField)99, 1)]);
        var wrongKind = ValidDiagnostic([DiagnosticFieldEntry.Boolean(DiagnosticField.Attempt, true)]);
        var duplicate = ValidDiagnostic([DiagnosticFieldEntry.Number(DiagnosticField.Attempt, 1), DiagnosticFieldEntry.Number(DiagnosticField.Attempt, 2)]);
        var nested = new StructuredDiagnosticEvent(DiagnosticCategory.Quota, DiagnosticCode.RefreshCompleted, DiagnosticTimeBasis.ServiceReportedUtc, DiagnosticErrorKind.None,
            [new DiagnosticFieldEntry(DiagnosticField.Attempt, (DiagnosticValueKind)99, 0, false)]);

        Assert.All(new[] { unknown, wrongKind, duplicate, nested }, diagnostic => Assert.Equal(StructuredDiagnosticSerializer.RedactedFallback, StructuredDiagnosticSerializer.Serialize(diagnostic)));
    }

    [Fact]
    public void Rejects_out_of_range_values_and_more_than_the_bounded_field_count()
    {
        var oversizedNumber = ValidDiagnostic([DiagnosticFieldEntry.Number(DiagnosticField.DurationMilliseconds, 86_400_001)]);
        var oversizedFields = ValidDiagnostic([
            DiagnosticFieldEntry.Number(DiagnosticField.Attempt, 1), DiagnosticFieldEntry.Number(DiagnosticField.DurationMilliseconds, 1),
            DiagnosticFieldEntry.Number(DiagnosticField.HttpStatusClass, 2), DiagnosticFieldEntry.Boolean(DiagnosticField.IsStale, false),
            DiagnosticFieldEntry.Boolean(DiagnosticField.WasCancelled, false), DiagnosticFieldEntry.Number(DiagnosticField.Attempt, 2),
            DiagnosticFieldEntry.Number(DiagnosticField.DurationMilliseconds, 2), DiagnosticFieldEntry.Number(DiagnosticField.HttpStatusClass, 3),
            DiagnosticFieldEntry.Boolean(DiagnosticField.IsStale, true)
        ]);

        Assert.Equal(StructuredDiagnosticSerializer.RedactedFallback, StructuredDiagnosticSerializer.Serialize(oversizedNumber));
        Assert.Equal(StructuredDiagnosticSerializer.RedactedFallback, StructuredDiagnosticSerializer.Serialize(oversizedFields));
    }

    [Fact]
    public void Rejects_null_field_collections_without_a_partial_or_raw_fallback()
    {
        var diagnostic = new StructuredDiagnosticEvent(DiagnosticCategory.Quota, DiagnosticCode.RefreshCompleted, DiagnosticTimeBasis.Utc, DiagnosticErrorKind.None, null!);

        Assert.Equal("[REDACTED]", StructuredDiagnosticSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void Serialization_contains_no_raw_content_or_exception_values_and_never_parses_strings()
    {
        const string hostile = "Bearer token-123 https://example.test/?id=acct-456 C:/secret/auth.json prompt body\\n<content>";
        var properties = typeof(StructuredDiagnosticEvent).GetProperties().Concat(typeof(DiagnosticFieldEntry).GetProperties());
        var serialized = StructuredDiagnosticSerializer.Serialize(ValidDiagnostic([DiagnosticFieldEntry.Boolean(DiagnosticField.WasCancelled, false)]));

        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(typeof(StructuredDiagnosticSerializer).GetMethods(), method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
        Assert.DoesNotContain(hostile, serialized, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(serialized);
        Assert.False(document.RootElement.TryGetProperty("raw", out _));
        Assert.False(document.RootElement.TryGetProperty("exception", out _));
    }

    [Fact]
    public void Fallback_is_completely_redacted_and_idempotent_under_repeated_redaction_inputs()
    {
        var rejected = new StructuredDiagnosticEvent(DiagnosticCategory.Application, DiagnosticCode.DiagnosticRejected, DiagnosticTimeBasis.Utc, DiagnosticErrorKind.None,
            [DiagnosticFieldEntry.Number((DiagnosticField)999, 1)]);

        var first = StructuredDiagnosticSerializer.Serialize(rejected);
        var second = StructuredDiagnosticSerializer.Serialize(rejected);

        Assert.Equal("[REDACTED]", first);
        Assert.Equal(first, second);
    }

    private static StructuredDiagnosticEvent ValidDiagnostic(IReadOnlyList<DiagnosticFieldEntry> fields, DiagnosticErrorKind errorKind = DiagnosticErrorKind.None) =>
        new(DiagnosticCategory.Quota, DiagnosticCode.RefreshCompleted, DiagnosticTimeBasis.ServiceReportedUtc, errorKind, fields);
}
