using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIBar.Domain;

namespace AIBar.Application;

public sealed class QuotaHttpProvider : IQuotaProvider
{
    private static readonly Uri Origin = new("https://chatgpt.com/backend-api/");
    private readonly HttpClient _client;
    private readonly PrivateIntegrationPolicy _policy;
    private readonly Func<CancellationToken, ValueTask<RequestCredential?>> _credentials;
    private readonly TimeSpan _requestTimeout;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public QuotaHttpProvider(PrivateIntegrationPolicy policy, Func<CancellationToken, ValueTask<RequestCredential?>> credentials, TimeSpan? connectTimeout = null, TimeSpan? requestTimeout = null)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5) }, policy, credentials, requestTimeout) { }

    public QuotaHttpProvider(PrivateIntegrationPolicy policy, ConsentCredentialSource credentials, TimeSpan? connectTimeout = null, TimeSpan? requestTimeout = null)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5) }, policy, credentials, requestTimeout) { }

    public QuotaHttpProvider(HttpMessageHandler handler, PrivateIntegrationPolicy policy, Func<CancellationToken, ValueTask<RequestCredential?>> credentials, TimeSpan? requestTimeout = null, Func<DateTimeOffset>? utcNow = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = new HttpClient(handler, disposeHandler: true) { BaseAddress = Origin, Timeout = Timeout.InfiniteTimeSpan };
        _policy = policy; _credentials = credentials; _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow); _delay = delay ?? Task.Delay;
    }

    public QuotaHttpProvider(HttpMessageHandler handler, PrivateIntegrationPolicy policy, ConsentCredentialSource credentials, TimeSpan? requestTimeout = null, Func<DateTimeOffset>? utcNow = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(handler, policy, async cancellationToken =>
        {
            var result = await credentials.ReadForQuotaAsync(cancellationToken);
            return result.Credential;
        }, requestTimeout, utcNow, delay)
    {
        _credentialAvailability = credentials;
    }

    private readonly ConsentCredentialSource? _credentialAvailability;

    public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
    {
        if (!_policy.IsEnabled) return Fail(QuotaErrorKind.Unavailable, "quota_disabled");
        cancellationToken.ThrowIfCancellationRequested();
        using var credential = await _credentials(cancellationToken);
        if (credential is null) return Fail(QuotaErrorKind.Unavailable, _credentialAvailability?.LastAvailability switch
        {
            CredentialAvailability.Missing => "quota_credential_missing",
            CredentialAvailability.Unusable => "quota_credential_unusable",
            CredentialAvailability.Disabled => "quota_disabled",
            _ => "quota_credentials_unavailable",
        });
        var primary = await SendAsync("wham/usage", credential, cancellationToken);
        if (primary.Failure is not null) return new(null, primary.Failure);
        if (!TrySnapshot(primary.Body!, out var snapshot)) return Fail(QuotaErrorKind.MalformedResponse, "quota_malformed");
        var detail = await SendAsync("wham/rate-limit-reset-credits", credential, cancellationToken, false);
        if (detail.Failure is not null) return new(snapshot, null, null, detail.Failure);
        return TryCredits(detail.Body!, out var credits) ? new(snapshot, null, credits) : new(snapshot, null, null, new(QuotaErrorKind.MalformedResponse, "quota_optional_malformed"));
    }

    private async Task<(string? Body, QuotaFailure? Failure, QuotaProviderResult? FailureResult)> SendAsync(string path, RequestCredential credential, CancellationToken cancellation, bool retry = true)
    {
        for (var attempt = 0; attempt < (retry ? 2 : 1); attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(_requestTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
                if (!string.IsNullOrWhiteSpace(credential.AccountId)) request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credential.AccountId);
                using var response = await _client.SendAsync(request, timeout.Token);
                if ((int)response.StatusCode is >= 300 and < 400) return (null, new(QuotaErrorKind.Redirect, "quota_redirect_rejected"), null);
                if (response.IsSuccessStatusCode) return (await response.Content.ReadAsStringAsync(timeout.Token), null, null);
                var failure = StatusFailure(response.StatusCode);
                if (retry && attempt == 0 && IsTransient(response.StatusCode)) { await DelayAsync(response.Headers.RetryAfter, cancellation); continue; }
                return (null, failure, null);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested && retry && attempt == 0) { continue; }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return (null, new(QuotaErrorKind.Network, "quota_timeout"), null); }
            catch (HttpRequestException) when (retry && attempt == 0) { continue; }
            catch (HttpRequestException) { return (null, new(QuotaErrorKind.Network, "quota_network"), null); }
        }
        return (null, new(QuotaErrorKind.Network, "quota_network"), null);
    }

    private Task DelayAsync(RetryConditionHeaderValue? retryAfter, CancellationToken cancellation)
    {
        var delay = retryAfter?.Delta ?? (retryAfter?.Date - _utcNow()) ?? TimeSpan.Zero;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5);
        return _delay(delay, cancellation);
    }
    private static bool IsTransient(HttpStatusCode code) => code == HttpStatusCode.RequestTimeout || code == (HttpStatusCode)429 || (int)code >= 500;
    private static QuotaFailure StatusFailure(HttpStatusCode code) => code switch { HttpStatusCode.Unauthorized => new(QuotaErrorKind.Authentication, "quota_authentication"), HttpStatusCode.Forbidden => new(QuotaErrorKind.Permission, "quota_permission"), _ => new(QuotaErrorKind.Service, "quota_service") };
    private static QuotaProviderResult Fail(QuotaErrorKind kind, string code) => new(null, new(kind, code));

    private static bool TrySnapshot(string body, out QuotaSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            using var document = JsonDocument.Parse(body); var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (root.TryGetProperty("rate_limit", out var rateLimit)) root = rateLimit;
            if (root.ValueKind != JsonValueKind.Object) return false;
            return TryWindow(root, "five_hour", "primary_window", out var primary) && TryWindow(root, "weekly", "secondary_window", out var weekly) && (snapshot = new(primary, weekly, DateTimeOffset.UtcNow)) is not null;
        }
        catch (JsonException) { return false; }
    }
    private static bool TryWindow(JsonElement root, string first, string second, out QuotaWindow window)
    {
        window = null!;
        if (!(root.TryGetProperty(first, out var value) || root.TryGetProperty(second, out value)) || value.ValueKind != JsonValueKind.Object) return false;
        if (!((value.TryGetProperty("percentage_used", out var percentage) || value.TryGetProperty("used_percent", out percentage)) && percentage.TryGetDecimal(out var used) && used is >= 0 and <= 100 && (value.TryGetProperty("reset_at", out var reset) || value.TryGetProperty("reset_time", out reset)) && reset.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(reset.GetString(), out var at))) return false;
        window = new(used, at); return true;
    }
    private static bool TryCredits(string body, out decimal credits)
    {
        credits = 0; try { using var doc = JsonDocument.Parse(body); return doc.RootElement.ValueKind == JsonValueKind.Object && (doc.RootElement.TryGetProperty("reset_credits", out var value) || doc.RootElement.TryGetProperty("credits", out value)) && value.TryGetDecimal(out credits); } catch (JsonException) { return false; }
    }
}
