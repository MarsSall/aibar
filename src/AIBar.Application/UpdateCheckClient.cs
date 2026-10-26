using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AIBar.Application;

public sealed record UpdateRelease(string Tag, string Url);

/// <summary>Anonymous, bounded metadata only. Never downloads release assets.</summary>
public sealed class UpdateCheckClient : IDisposable
{
    public const int MaximumBytes = 65536;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _http;
    public UpdateCheckClient() : this(CreateHandler()) { }
    public UpdateCheckClient(HttpMessageHandler handler) => _http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        Credentials = null, UseProxy = false, AutomaticDecompression = DecompressionMethods.None
    };
    public async Task<UpdateRelease?> CheckAsync(ConsentSettings settings, string currentVersion, CancellationToken token)
    {
        try { return await settings.LoadUpdateCheckAsync(token).ConfigureAwait(false) ? await FetchAsync(currentVersion, token).ConfigureAwait(false) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (OperationCanceledException) { return null; }
    }
    public async Task<UpdateRelease?> FetchAsync(string currentVersion, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/MarsSall/aibar/releases/latest");
            request.Headers.UserAgent.ParseAdd("AIBar-UpdateCheck/1.0");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[MaximumBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBytes) return null;
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String) return null;
            return IsNewer(tag.GetString()!, currentVersion) && IsAllowedUrl(url.GetString()) ? new(tag.GetString()!, url.GetString()!) : null;
        }
        catch (HttpRequestException) { return null; }
        catch (IOException) { return null; }
        catch (OperationCanceledException) { return null; }
        catch (JsonException) { return null; }
    }
    public static bool IsAllowedUrl(string? url) => url is not null &&
        url.StartsWith("https://github.com/MarsSall/aibar/releases/", StringComparison.Ordinal) &&
        !url.Any(char.IsControl) && !url.Contains('\\') && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath.StartsWith("/MarsSall/aibar/releases/", StringComparison.Ordinal);

    private static readonly Regex Semver = new(@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static string[]? Parse(string value)
    {
        if (value.Length > 256) return null;
        var match = Semver.Match(value);
        if (!match.Success) return null;
        var pre = match.Groups[4].Value;
        if (pre.Split('.').Any(part => Numeric(part) && part.Length > 1 && part[0] == '0')) return null;
        return [match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, pre];
    }
    private static bool Numeric(string value) => value.Length > 0 && value.All(c => c is >= '0' and <= '9');
    private static int CompareNumber(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    public static bool IsNewer(string tag, string currentVersion)
    {
        if (!tag.StartsWith('v') || Parse(tag[1..]) is not { } latest || Parse(currentVersion) is not { } current) return false;
        for (var i = 0; i < 3; i++) { var comparison = CompareNumber(latest[i], current[i]); if (comparison != 0) return comparison > 0; }
        if (latest[3] == current[3]) return false;
        if (latest[3] == "") return true;
        if (current[3] == "") return false;
        var left = latest[3].Split('.'); var right = current[3].Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var ln = Numeric(left[i]); var rn = Numeric(right[i]);
            var comparison = ln && rn ? CompareNumber(left[i], right[i]) : ln != rn ? (ln ? -1 : 1) : string.CompareOrdinal(left[i], right[i]);
            if (comparison != 0) return comparison > 0;
        }
        return left.Length > right.Length;
    }
    public void Dispose() => _http.Dispose();
}
