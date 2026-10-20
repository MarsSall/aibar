using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIBar.Domain;

namespace AIBar.Application;

public enum ClaudeQuotaSource { ThisPc, OtherMachine, Automatic }

/// <summary>Reads only the bounded, normalized usage file pushed by the other machine.</summary>
public sealed class ClaudeRemoteUsageProvider : IQuotaProvider
{
    private readonly PrivateIntegrationPolicy _policy;
    private readonly string _path;
    private readonly ICredentialFileReader _files;
    private readonly Func<string, FileAttributes> _attributes;
    private readonly Func<DateTimeOffset> _utcNow;

    public ClaudeRemoteUsageProvider(PrivateIntegrationPolicy policy, string? path = null,
        ICredentialFileReader? files = null, Func<string, FileAttributes>? attributes = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _policy = policy; _path = Path.GetFullPath(path ?? DefaultPath());
        _files = files ?? new ReadOnlyCredentialFileReader();
        _attributes = attributes ?? File.GetAttributes;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    // Detection is metadata-only: an invalid/stale payload still owns the remote source.
    public static bool IsAvailableForAutomatic(string? path = null,
        Func<string, FileAttributes>? attributes = null)
    {
        try
        {
            var fullPath = Path.GetFullPath(path ?? DefaultPath());
            var readAttributes = attributes ?? File.GetAttributes;
            for (string? current = fullPath; current is not null; current = Path.GetDirectoryName(current))
            {
                var value = readAttributes(current);
                if ((value & FileAttributes.ReparsePoint) != 0 ||
                    current == fullPath && (value & FileAttributes.Directory) != 0) return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIBar", "claude-remote-usage.json");

    public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_policy.IsEnabled) return Failure(QuotaErrorKind.Unavailable, "quota_disabled");
        try
        {
            RejectReparsePoints();
            await using var stream = await _files.OpenReadAsync(_path, FileShare.ReadWrite | FileShare.Delete, cancellationToken);
            RejectReparsePoints();
            var bytes = new byte[4097];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!_policy.IsEnabled) return Failure(QuotaErrorKind.Unavailable, "quota_disabled");
            if (count > 4096) return Unavailable();
            var text = new UTF8Encoding(false, true).GetString(bytes, 0, count);
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            RequireProperties(root, "schemaVersion", "generatedAt", "state", "fiveHour", "weekly");
            if (root.GetProperty("schemaVersion").GetRawText() != "1") return Unavailable();
            var generated = Timestamp(root.GetProperty("generatedAt"));
            var state = root.GetProperty("state").GetString();
            var primary = Window(root.GetProperty("fiveHour"));
            var weekly = Window(root.GetProperty("weekly"));
            if (state is not ("current" or "authentication-required" or "unavailable")) return Unavailable();
            if (state == "current" ? primary is null && weekly is null : primary is not null || weekly is not null)
                return Unavailable();
            var age = _utcNow() - generated;
            if (age < -TimeSpan.FromMinutes(2)) return Unavailable();
            if (age > TimeSpan.FromMinutes(15)) return Failure(QuotaErrorKind.Service, "quota_service");
            return state switch
            {
                "current" => new(new(primary, weekly, generated), null),
                "authentication-required" => Failure(QuotaErrorKind.Authentication, "quota_authentication"),
                _ => Unavailable()
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException
            or System.Security.SecurityException)
        {
            return Unavailable();
        }
    }

    private void RejectReparsePoints()
    {
        for (string? path = _path; path is not null; path = Path.GetDirectoryName(path))
            if ((_attributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Remote usage must not traverse reparse points.");
    }

    private static void RequireProperties(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(property => property.Name).SequenceEqual(names))
            throw new FormatException("Invalid usage shape.");
    }

    private static QuotaWindow? Window(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        RequireProperties(value, "percentageUsed", "resetAt");
        var percentage = value.GetProperty("percentageUsed");
        if (percentage.ValueKind != JsonValueKind.Number || !percentage.TryGetDecimal(out var used) || used < 0 || used > 100)
            throw new FormatException("Invalid usage percentage.");
        return new(used, Timestamp(value.GetProperty("resetAt")));
    }

    private static DateTimeOffset Timestamp(JsonElement value)
    {
        var text = value.GetString();
        if (text is null || !Regex.IsMatch(text, @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?Z\z") ||
            !DateTimeOffset.TryParseExact(text, new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            throw new FormatException("Invalid usage timestamp.");
        return timestamp;
    }

    private static QuotaProviderResult Unavailable() => Failure(QuotaErrorKind.Unavailable, "quota_unavailable");
    private static QuotaProviderResult Failure(QuotaErrorKind kind, string code) => new(null, new(kind, code));
}
