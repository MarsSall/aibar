using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIBar.Domain;

namespace AIBar.Application;

public static class ClaudeRootResolver
{
    public static string Resolve(string? claudeConfigDir, string userProfile) =>
        string.IsNullOrWhiteSpace(claudeConfigDir) ? Path.Combine(userProfile, ".claude") : claudeConfigDir.Trim();
}

public sealed class ClaudeRequestCredential(string accessToken, DateTimeOffset? expiresAt, string? subscriptionType) : IDisposable
{
    public string? AccessToken { get; private set; } = accessToken;
    public DateTimeOffset? ExpiresAt { get; private set; } = expiresAt;
    public string? SubscriptionType { get; private set; } = subscriptionType;

    public void Dispose() { AccessToken = null; ExpiresAt = null; SubscriptionType = null; }
    public override string ToString() => nameof(ClaudeRequestCredential);
}

public sealed class ClaudeCredentialReader(ICredentialFileReader files)
{
    public async ValueTask<ClaudeRequestCredential?> ReadAsync(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = await files.OpenReadAsync(
                Path.Combine(root, ".credentials.json"), FileShare.ReadWrite | FileShare.Delete, cancellationToken);
            var fields = await JsonSerializer.DeserializeAsync<CredentialFields>(stream, cancellationToken: cancellationToken);
            var oauth = fields?.Oauth;
            if (oauth is null || string.IsNullOrWhiteSpace(oauth.AccessToken)) return null;
            var expiresAt = oauth.ExpiresAt is { } milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : (DateTimeOffset?)null;
            return new(oauth.AccessToken, expiresAt, oauth.SubscriptionType);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
        catch (ArgumentOutOfRangeException) { return null; }
        // Do not propagate file-system messages or inner exceptions containing a private path.
        catch (IOException) { throw new IOException("Credential unavailable."); }
        catch (UnauthorizedAccessException) { throw new UnauthorizedAccessException("Credential unavailable."); }
        catch (OperationCanceledException) { throw new OperationCanceledException(cancellationToken); }
    }

    // Only these fields are materialized; refresh credentials are deliberately not modeled.
    private sealed class CredentialFields
    {
        [JsonPropertyName("claudeAiOauth")]
        public OauthFields? Oauth { get; init; }
    }

    private sealed class OauthFields
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; init; }
        [JsonPropertyName("expiresAt")]
        public long? ExpiresAt { get; init; }
        [JsonPropertyName("subscriptionType")]
        public string? SubscriptionType { get; init; }
    }
}

public sealed class ClaudeConsentCredentialSource(PrivateIntegrationPolicy policy, ClaudeCredentialReader reader, string root)
{
    public CredentialAvailability LastAvailability { get; private set; } = CredentialAvailability.Disabled;

    public async ValueTask<CredentialAvailability> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        var result = await ReadForQuotaAsync(cancellationToken);
        result.Credential?.Dispose();
        return result.Availability;
    }

    internal async ValueTask<(CredentialAvailability Availability, ClaudeRequestCredential? Credential)> ReadForQuotaAsync(CancellationToken cancellationToken)
    {
        if (!policy.IsEnabled) return (LastAvailability = CredentialAvailability.Disabled, null);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var credential = await reader.ReadAsync(root, cancellationToken);
            // Consent may have been revoked while the asynchronous file read was in flight.
            if (!policy.IsEnabled)
            {
                credential?.Dispose();
                return (LastAvailability = CredentialAvailability.Disabled, null);
            }
            LastAvailability = credential is null ? CredentialAvailability.Missing : CredentialAvailability.Available;
            return (LastAvailability, credential);
        }
        catch (IOException) { return (LastAvailability = CredentialAvailability.Unusable, null); }
        catch (UnauthorizedAccessException) { return (LastAvailability = CredentialAvailability.Unusable, null); }
    }
}

public sealed class ClaudeQuotaHttpProvider : IQuotaProvider, IQuotaPlanMetadata, IDisposable
{
    private static readonly Uri Endpoint = new("https://api.anthropic.com/api/oauth/usage");
    private readonly HttpClient _client;
    private readonly PrivateIntegrationPolicy _policy;
    private readonly ClaudeConsentCredentialSource _credentials;
    private readonly TimeSpan _requestTimeout;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public ClaudeQuotaHttpProvider(PrivateIntegrationPolicy policy, ClaudeConsentCredentialSource credentials, TimeSpan? connectTimeout = null, TimeSpan? requestTimeout = null)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5) }, policy, credentials, requestTimeout) { }

    public ClaudeQuotaHttpProvider(HttpMessageHandler handler, PrivateIntegrationPolicy policy, ClaudeConsentCredentialSource credentials, TimeSpan? requestTimeout = null, Func<DateTimeOffset>? utcNow = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _policy = policy;
        _credentials = credentials;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    private string? _planLabel;
    public string? PlanLabel => _policy.IsEnabled ? _planLabel : null;

    public async ValueTask<QuotaProviderResult> GetQuotaAsync(CancellationToken cancellationToken)
    {
        _planLabel = null;
        if (!_policy.IsEnabled) return Fail(QuotaErrorKind.Unavailable, "quota_disabled");
        cancellationToken.ThrowIfCancellationRequested();
        var read = await _credentials.ReadForQuotaAsync(cancellationToken);
        using var credential = read.Credential;
        if (credential is null) return Fail(QuotaErrorKind.Unavailable, read.Availability switch
        {
            CredentialAvailability.Disabled => "quota_disabled",
            CredentialAvailability.Missing => "quota_credential_missing",
            CredentialAvailability.Unusable => "quota_credential_unusable",
            _ => "quota_credentials_unavailable",
        });
        _planLabel = QuotaPlanLabels.FromKnownValue(credential.SubscriptionType);
        if (credential.ExpiresAt is { } expiresAt && expiresAt - _utcNow() <= TimeSpan.FromSeconds(60))
            return Fail(QuotaErrorKind.Authentication, "quota_authentication");

        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_policy.IsEnabled) return Fail(QuotaErrorKind.Unavailable, "quota_disabled");
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_requestTimeout);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.AccessToken);
                request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
                request.Headers.Add("anthropic-version", "2023-06-01");
                using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is >= 300 and < 400) return Fail(QuotaErrorKind.Redirect, "quota_redirect_rejected");
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(timeout.Token);
                    return TrySnapshot(body, _utcNow(), out var snapshot)
                        ? new(snapshot, null)
                        : Fail(QuotaErrorKind.MalformedResponse, "quota_malformed");
                }
                if (attempt == 0 && IsTransient(response.StatusCode))
                {
                    await DelayAsync(response.Headers.RetryAfter, cancellationToken);
                    continue;
                }
                return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? Fail(QuotaErrorKind.Authentication, "quota_authentication")
                    : Fail(QuotaErrorKind.Service, "quota_service");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            { throw new OperationCanceledException(cancellationToken); }
            catch (OperationCanceledException) when (attempt == 0) { continue; }
            catch (OperationCanceledException) { return Fail(QuotaErrorKind.Network, "quota_timeout"); }
            catch (HttpRequestException) when (attempt == 0) { continue; }
            catch (HttpRequestException) { return Fail(QuotaErrorKind.Network, "quota_network"); }
            catch (IOException) when (attempt == 0) { continue; }
            catch (IOException) { return Fail(QuotaErrorKind.Network, "quota_network"); }
            catch (FormatException) { return Fail(QuotaErrorKind.Authentication, "quota_authentication"); }
            finally
            {
                // HttpRequestMessage.Dispose does not clear headers; remove our secret reference explicitly.
                request.Headers.Authorization = null;
            }
        }
        return Fail(QuotaErrorKind.Network, "quota_network");
    }

    public void Dispose() => _client.Dispose();

    private Task DelayAsync(RetryConditionHeaderValue? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter?.Delta ?? (retryAfter?.Date - _utcNow()) ?? TimeSpan.Zero;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromSeconds(5)) delay = TimeSpan.FromSeconds(5);
        return _delay(delay, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode status) => status == (HttpStatusCode)429 || status == HttpStatusCode.RequestTimeout || (int)status >= 500;
    private static QuotaProviderResult Fail(QuotaErrorKind kind, string code) => new(null, new(kind, code));

    private static bool TrySnapshot(string body, DateTimeOffset retrievedAt, out QuotaSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryWindow(root, "five_hour", out var primary) ||
                !TryWindow(root, "seven_day", out var weekly) ||
                (primary is null && weekly is null)) return false;
            snapshot = new(primary, weekly, retrievedAt);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool TryWindow(JsonElement root, string name, out QuotaWindow? window)
    {
        window = null;
        if (CountProperties(root, name) > 1) return false;
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return true;
        if (value.ValueKind != JsonValueKind.Object ||
            CountProperties(value, "utilization") != 1 || CountProperties(value, "resets_at") != 1 ||
            !value.TryGetProperty("utilization", out var utilization) || utilization.ValueKind != JsonValueKind.Number ||
            !utilization.TryGetDecimal(out var used) || used is < 0 or > 100 ||
            !value.TryGetProperty("resets_at", out var reset) || reset.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(reset.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var resetAt)) return false;
        window = new(used, resetAt);
        return true;
    }

    private static int CountProperties(JsonElement value, string name)
    {
        var count = 0;
        foreach (var property in value.EnumerateObject())
            if (property.NameEquals(name)) count++;
        return count;
    }
}
