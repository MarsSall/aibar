using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIBar.Domain;

namespace AIBar.Application;

public sealed record RemoteTokenRows(IReadOnlyList<UsageCostDaily> Rows, DateOnly Today, bool Partial, bool Available)
{
    public static RemoteTokenRows Unavailable { get; } = new([], default, false, false);
    public UsageCostSummary Summary => Available ? UsageCostPolicy.Calculate(Rows, Today, Partial) : UsageCostSummary.Unavailable;
}

/// <summary>Strict reader for aggregate-only tokens; never reads credentials or quota JSON.</summary>
public static class RemoteTokens
{
    public const int MaxBytes = 1_048_576;
    public const int MaxRows = 3000;
    private static readonly Regex Model = new("\\A[a-z][a-z0-9.-]{0,99}\\z", RegexOptions.CultureInvariant);

    public static async ValueTask<RemoteTokenRows> ReadRowsAsync(string path, DateTimeOffset now, CancellationToken token = default)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return RemoteTokenRows.Unavailable;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (stream.Length > MaxBytes) return RemoteTokenRows.Unavailable;
            var bytes = new byte[MaxBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            return ParseRows(bytes.AsMemory(0, count), now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return RemoteTokenRows.Unavailable; }
    }

    public static RemoteTokenRows ParseRows(ReadOnlyMemory<byte> bytes, DateTimeOffset now)
    {
        if (bytes.Length > MaxBytes) return RemoteTokenRows.Unavailable;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            Exact(root, "schemaVersion", "generatedAt", "localDay", "state", "days");
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new FormatException();
            var stampText = root.GetProperty("generatedAt").GetString()!;
            if (stampText is null || !DateTimeOffset.TryParseExact(stampText,
                ["yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var stamp) || stamp > now || now - stamp > TimeSpan.FromHours(2))
                throw new FormatException();
            var today = Day(root.GetProperty("localDay"));
            // Source local day can differ from UTC by one day, never arbitrarily.
            if (Math.Abs(today.DayNumber - DateOnly.FromDateTime(stamp.UtcDateTime).DayNumber) > 1) throw new FormatException();
            var state = root.GetProperty("state").GetString();
            if (state is not ("current" or "partial" or "unavailable")) throw new FormatException();
            var days = root.GetProperty("days");
            if (days.ValueKind != JsonValueKind.Array || days.GetArrayLength() > MaxRows) throw new FormatException();
            var rows = new List<UsageCostDaily>();
            var keys = new HashSet<(DateOnly, string)>();
            foreach (var row in days.EnumerateArray())
            {
                Exact(row, "day", "model", "input", "cachedInput", "cacheWrite", "output");
                var day = Day(row.GetProperty("day"));
                var model = row.GetProperty("model").GetString()!;
                if (!Model.IsMatch(model) || day > today || day < today.AddDays(-29) || !keys.Add((day, model))) throw new FormatException();
                rows.Add(new(day, model, Count(row, "input"), Count(row, "cachedInput"), Count(row, "cacheWrite"), Count(row, "output")));
            }
            if (state == "unavailable") return RemoteTokenRows.Unavailable;
            // Validate aggregate overflow too, before exposing any rows to a caller.
            _ = UsageCostPolicy.Calculate(rows, today, state == "partial");
            return new(Array.AsReadOnly(rows.ToArray()), today, state == "partial", true);
        }
        catch (Exception e) when (e is JsonException or FormatException or InvalidOperationException or ArgumentException or OverflowException)
        { return RemoteTokenRows.Unavailable; }
    }

    /// <summary>Keep the exact local-only summary when there is no usable remote aggregate.</summary>
    public static UsageCostSummary Combine(LocalAnalyticsState local, RemoteTokenRows remote, DateOnly today)
    {
        if (!remote.Available) return local.UsageCost;
        try
        {
            return UsageCostPolicy.Calculate(local.UsageCostRows.Concat(remote.Rows), today,
                local.UsageCostIncomplete || remote.Partial);
        }
        // A valid remote file must not make the local card disappear if their sum overflows.
        catch (OverflowException) { return local.UsageCost; }
    }

    private static long Count(JsonElement row, string name)
    {
        var value = row.GetProperty(name).GetInt64();
        if (value < 0) throw new FormatException();
        return value;
    }
    private static DateOnly Day(JsonElement field) => DateOnly.ParseExact(field.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static void Exact(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw new FormatException();
        if (seen.Count != fields.Length) throw new FormatException();
    }
}
