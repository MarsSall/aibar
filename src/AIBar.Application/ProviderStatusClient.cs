using System.Net;
using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Anonymous, bounded reader of the two official public status endpoints.</summary>
public sealed class ProviderStatusClient : IDisposable
{
    public const int MaximumBytes = 65536;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _http;
    public ProviderStatusClient() : this(CreateHandler()) { }
    // Transport injection is for offline tests; production always uses CreateHandler.
    public ProviderStatusClient(HttpMessageHandler handler) => _http = new(handler) { Timeout = Timeout.InfiniteTimeSpan };
    public static HttpClientHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        Credentials = null, UseProxy = false, AutomaticDecompression = DecompressionMethods.None
    };

    public async Task<ProviderStatus?> FetchAsync(StatusProvider provider, CancellationToken cancellationToken)
    {
        var endpoint = provider switch
        {
            StatusProvider.Codex => "https://status.openai.com/api/v2/status.json",
            StatusProvider.Claude => "https://status.claude.com/api/v2/status.json",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBytes) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            // Read one extra byte to reject an oversized chunked/undeclared body as well.
            var buffer = new byte[MaximumBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            return count > MaximumBytes ? null : ProviderStatus.Parse(buffer.AsMemory(0, count));
        }
        catch (HttpRequestException) { return null; }
        catch (IOException) { return null; }
        catch (OperationCanceledException) { return null; }
    }
    public void Dispose() => _http.Dispose();
}
