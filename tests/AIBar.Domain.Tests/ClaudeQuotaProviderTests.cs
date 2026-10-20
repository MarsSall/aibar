using System.Net;
using System.Text;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ClaudeQuotaProviderTests
{
    private const string Token = "synthetic-claude-token-distinctive";
    private const string RefreshToken = "synthetic-claude-refresh-never-use";
    private const string Root = "D:/synthetic-private/.claude";
    private const string BodySecret = "synthetic-private-response-body";
    private const string Credentials = "{\"claudeAiOauth\":{\"accessToken\":\"" + Token + "\",\"refreshToken\":\"" + RefreshToken + "\",\"subscriptionType\":\"max\"}}";
    private const string Valid = """{"five_hour":{"utilization":25.5,"resets_at":"2030-01-01T05:00:00Z"},"seven_day":{"utilization":75,"resets_at":"2030-01-08T00:00:00Z"}}""";
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void Root_falls_back_to_user_profile_when_config_dir_is_blank(string? configDir)
    {
        Assert.Equal(Path.Combine("D:/synthetic-user", ".claude"), ClaudeRootResolver.Resolve(configDir, "D:/synthetic-user"));
    }

    [Fact]
    public void Root_prefers_trimmed_Claude_config_dir()
    {
        Assert.Equal("D:/synthetic-config", ClaudeRootResolver.Resolve(" D:/synthetic-config ", "D:/synthetic-user"));
    }

    [Fact]
    public async Task Reader_opens_only_credentials_json_with_permissive_read_sharing_and_disposes_stream()
    {
        var files = new StubFiles(Credentials);
        using var credential = await new ClaudeCredentialReader(files).ReadAsync(Root, CancellationToken.None);

        Assert.NotNull(credential);
        Assert.Equal(1, files.Calls);
        Assert.Equal(Path.Combine(Root, ".credentials.json"), files.LastPath);
        Assert.Equal(FileShare.ReadWrite | FileShare.Delete, files.LastShare);
        Assert.True(files.LastStream!.WasDisposed);
        Assert.Equal(0, files.LastStream.WriteAttempts);
    }

    [Fact]
    public async Task Reader_parses_nested_access_token_unix_milliseconds_and_subscription_type()
    {
        var expires = Now.AddHours(2);
        var json = $$$"""{"claudeAiOauth":{"accessToken":"{{{Token}}}","expiresAt":{{{expires.ToUnixTimeMilliseconds()}}},"subscriptionType":"max","refreshToken":"{{{RefreshToken}}}"}}""";
        using var credential = await new ClaudeCredentialReader(new StubFiles(json)).ReadAsync(Root, CancellationToken.None);

        Assert.Equal(Token, credential!.AccessToken);
        Assert.Equal(expires, credential.ExpiresAt);
        Assert.Equal("max", credential.SubscriptionType);
        AssertSafe(credential.ToString());
    }

    [Fact]
    public async Task Reader_allows_missing_optional_expiry_and_subscription()
    {
        using var credential = await new ClaudeCredentialReader(new StubFiles($$$"""{"claudeAiOauth":{"accessToken":"{{{Token}}}"}}""")).ReadAsync(Root, CancellationToken.None);
        Assert.Equal(Token, credential!.AccessToken);
        Assert.Null(credential.ExpiresAt);
        Assert.Null(credential.SubscriptionType);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{bad")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"accessToken\":\"synthetic-legacy-not-supported\"}")]
    [InlineData("{\"claudeAiOauth\":null}")]
    [InlineData("{\"claudeAiOauth\":{}}")]
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":\"\"}}")]
    [InlineData("{\"claudeAiOauth\":{\"accessToken\":\" \"}}")]
    [InlineData("{\"claudeAiOauth\":{\"refreshToken\":\"synthetic-refresh-only\"}}")]
    public async Task Missing_or_invalid_credentials_return_existing_missing_outcome_without_HTTP(string json)
    {
        var handler = new StubHandler();
        var result = await Provider(handler, new StubFiles(json)).GetQuotaAsync(CancellationToken.None);
        AssertFailure(result, QuotaErrorKind.Unavailable, "quota_credential_missing");
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public async Task Absent_file_or_root_returns_missing_outcome_without_HTTP(Type exceptionType)
    {
        var handler = new StubHandler();
        var files = new StubFiles(Credentials, (Exception)Activator.CreateInstance(exceptionType)!);
        AssertFailure(await Provider(handler, files).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Unavailable, "quota_credential_missing");
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unreadable_file_returns_safe_unusable_outcome_without_HTTP(bool denied)
    {
        var handler = new StubHandler();
        Exception error = denied ? new UnauthorizedAccessException(Root + Token) : new IOException(Root + Token);
        AssertFailure(await Provider(handler, new StubFiles(Credentials, error)).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Unavailable, "quota_credential_unusable");
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(60)]
    public async Task Expiry_at_or_inside_sixty_second_margin_requires_login_without_HTTP_or_refresh(int seconds)
    {
        var handler = new StubHandler();
        var json = $$$"""{"claudeAiOauth":{"accessToken":"{{{Token}}}","expiresAt":{{{Now.AddSeconds(seconds).ToUnixTimeMilliseconds()}}},"refreshToken":"{{{RefreshToken}}}"}}""";
        var files = new StubFiles(json);
        AssertFailure(await Provider(handler, files).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Authentication, "quota_authentication");
        Assert.Equal(0, handler.Calls);
        Assert.Equal(1, files.Calls);
        Assert.Equal(0, files.LastStream!.WriteAttempts);
    }

    [Fact]
    public async Task Expiry_outside_margin_allows_one_usage_request_without_refresh()
    {
        var json = $$$"""{"claudeAiOauth":{"accessToken":"{{{Token}}}","expiresAt":{{{Now.AddSeconds(61).ToUnixTimeMilliseconds()}}},"refreshToken":"{{{RefreshToken}}}"}}""";
        var handler = new StubHandler(Json(Valid));
        var result = await Provider(handler, new StubFiles(json)).GetQuotaAsync(CancellationToken.None);
        Assert.Null(result.Failure);
        Assert.Single(handler.Requests);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", handler.Requests[0].Uri);
    }

    [Fact]
    public async Task Request_uses_Claude_usage_endpoint_bearer_and_required_headers_only()
    {
        var handler = new StubHandler(Json(Valid));
        var result = await Provider(handler).GetQuotaAsync(CancellationToken.None);
        Assert.Null(result.Failure);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.anthropic.com/api/oauth/usage", request.Uri);
        Assert.Equal("Bearer", request.Scheme);
        Assert.Equal(Token, request.Token);
        Assert.Equal("oauth-2025-04-20", request.Beta);
        Assert.Equal("2023-06-01", request.Version);
        Assert.False(request.HasAccountId);
        Assert.False(request.HasContent);
        AssertSafe(result.ToString());
    }

    [Fact]
    public async Task Maps_five_hour_and_seven_day_percentages_resets_and_injected_retrieval_time()
    {
        var result = await Provider(new StubHandler(Json(Valid))).GetQuotaAsync(CancellationToken.None);
        Assert.Null(result.Failure);
        Assert.Equal(25.5m, result.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(Now.AddHours(5), result.Snapshot.Primary.ResetAt);
        Assert.Equal(75m, result.Snapshot.Weekly!.PercentageUsed);
        Assert.Equal(Now.AddDays(7), result.Snapshot.Weekly.ResetAt);
        Assert.Equal(Now, result.Snapshot.RetrievedAt);
        Assert.Null(result.ResetCredits);
        Assert.Null(result.OptionalFailure);
    }

    [Theory]
    [InlineData("{\"five_hour\":{\"utilization\":0,\"resets_at\":\"2030-01-01T05:00:00Z\"}}", true)]
    [InlineData("{\"five_hour\":{\"utilization\":0,\"resets_at\":\"2030-01-01T05:00:00Z\"},\"seven_day\":null}", true)]
    [InlineData("{\"seven_day\":{\"utilization\":100,\"resets_at\":\"2030-01-08T00:00:00Z\"}}", false)]
    [InlineData("{\"five_hour\":null,\"seven_day\":{\"utilization\":100,\"resets_at\":\"2030-01-08T00:00:00Z\"}}", false)]
    public async Task Null_or_missing_sibling_is_absent_not_invented(string json, bool primary)
    {
        var result = await Provider(new StubHandler(Json(json))).GetQuotaAsync(CancellationToken.None);
        Assert.Null(result.Failure);
        if (primary) { Assert.Equal(0m, result.Snapshot!.Primary!.PercentageUsed); Assert.Null(result.Snapshot.Weekly); }
        else { Assert.Null(result.Snapshot!.Primary); Assert.Equal(100m, result.Snapshot.Weekly!.PercentageUsed); }
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"five_hour\":null,\"seven_day\":null}")]
    [InlineData("{\"five_hour\":{\"utilization\":101,\"resets_at\":\"2030-01-01T05:00:00Z\"}}")]
    [InlineData("{\"five_hour\":{\"utilization\":-1,\"resets_at\":\"2030-01-01T05:00:00Z\"}}")]
    [InlineData("{\"five_hour\":{\"utilization\":\"25\",\"resets_at\":\"2030-01-01T05:00:00Z\"}}")]
    [InlineData("{\"five_hour\":{\"utilization\":25,\"resets_at\":\"invalid\"}}")]
    [InlineData("{\"five_hour\":{\"utilization\":25}}")]
    [InlineData("{\"five_hour\":{\"resets_at\":\"2030-01-01T05:00:00Z\"}}")]
    [InlineData("{\"five_hour\":{\"utilization\":25,\"resets_at\":\"2030-01-01T05:00:00Z\"},\"seven_day\":{}}")]
    public async Task Malformed_or_absent_usage_returns_safe_invalid_response_without_retry(string json)
    {
        var handler = new StubHandler(Json(json));
        AssertFailure(await Provider(handler).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.MalformedResponse, "quota_malformed");
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Ignores_model_windows_oauth_apps_and_extra_usage_even_when_malformed()
    {
        var json = Valid[..^1] + ",\"seven_day_opus\":false,\"seven_day_sonnet\":[],\"oauth_apps\":\"" + BodySecret + "\",\"extra_usage\":{\"utilization\":99}}";
        var result = await Provider(new StubHandler(Json(json))).GetQuotaAsync(CancellationToken.None);
        Assert.Null(result.Failure);
        Assert.Equal(25.5m, result.Snapshot!.Primary!.PercentageUsed);
        Assert.Equal(75m, result.Snapshot.Weekly!.PercentageUsed);
        Assert.Null(result.ResetCredits);
        AssertSafe(result.ToString());
        var ignoredOnly = """{"seven_day_opus":{"utilization":10,"resets_at":"2030-01-08T00:00:00Z"},"extra_usage":{}}""";
        AssertFailure(await Provider(new StubHandler(Json(ignoredOnly))).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.MalformedResponse, "quota_malformed");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, QuotaErrorKind.Authentication, "quota_authentication")]
    [InlineData(HttpStatusCode.Forbidden, QuotaErrorKind.Authentication, "quota_authentication")]
    [InlineData(HttpStatusCode.BadRequest, QuotaErrorKind.Service, "quota_service")]
    [InlineData(HttpStatusCode.NotFound, QuotaErrorKind.Service, "quota_service")]
    public async Task Status_failures_are_safe_and_authentication_requires_login_without_retry(HttpStatusCode status, QuotaErrorKind kind, string code)
    {
        var handler = new StubHandler(Response(status));
        AssertFailure(await Provider(handler).GetQuotaAsync(CancellationToken.None), kind, code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Redirect_is_rejected_without_following_or_retrying(HttpStatusCode status)
    {
        var response = Response(status);
        response.Headers.Location = new Uri("https://synthetic-redirect.invalid/" + Token);
        var handler = new StubHandler(response);
        AssertFailure(await Provider(handler).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Redirect, "quota_redirect_rejected");
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData((HttpStatusCode)429)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Transient_status_retries_once_then_returns_service_failure(HttpStatusCode status)
    {
        var handler = new StubHandler(Response(status), Response(status));
        AssertFailure(await Provider(handler).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Service, "quota_service");
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(-60, 0)]
    [InlineData(60, 5)]
    public async Task Retry_after_date_is_clamped_using_injected_clock(int seconds, int expectedDelay)
    {
        var retry = Response((HttpStatusCode)429);
        retry.Headers.RetryAfter = new(Now.AddSeconds(seconds));
        var handler = new StubHandler(retry, Json(Valid));
        var delays = new List<TimeSpan>();
        var result = await Provider(handler, delays: delays).GetQuotaAsync(CancellationToken.None);
        Assert.Null(result.Failure);
        Assert.Equal(TimeSpan.FromSeconds(expectedDelay), Assert.Single(delays));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Retry_after_delta_is_honored_and_transport_retry_can_succeed()
    {
        var retry = Response((HttpStatusCode)429);
        retry.Headers.RetryAfter = new(TimeSpan.FromSeconds(2));
        var delays = new List<TimeSpan>();
        Assert.Null((await Provider(new StubHandler(retry, Json(Valid)), delays: delays).GetQuotaAsync(CancellationToken.None)).Failure);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(delays));
        var handler = new StubHandler(new HttpRequestException(Token + Root), Json(Valid));
        Assert.Null((await Provider(handler).GetQuotaAsync(CancellationToken.None)).Failure);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Network_and_timeout_exceptions_are_retried_once_and_sanitized()
    {
        var network = new StubHandler(new HttpRequestException(Token + Root), new HttpRequestException(RefreshToken + BodySecret));
        AssertFailure(await Provider(network).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Network, "quota_network");
        Assert.Equal(2, network.Calls);
        var timeout = new StubHandler(new OperationCanceledException(Token), new OperationCanceledException(RefreshToken));
        AssertFailure(await Provider(timeout).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Network, "quota_timeout");
        Assert.Equal(2, timeout.Calls);
    }

    [Fact]
    public async Task Request_timeout_cancels_each_attempt_and_returns_safe_failure()
    {
        var handler = new WaitingHandler();
        AssertFailure(await Provider(handler).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Network, "quota_timeout");
        Assert.Equal(2, handler.Calls);
        Assert.Equal(2, handler.Cancellations);
    }

    [Fact]
    public async Task Caller_cancellation_is_preserved_without_opening_credentials_or_HTTP()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var files = new StubFiles(Credentials);
        var handler = new StubHandler();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(handler, files).GetQuotaAsync(cancellation.Token).AsTask());
        Assert.Equal(0, files.Calls);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Disabled_policy_never_opens_credentials_for_source_or_provider_even_after_revocation()
    {
        var policy = new PrivateIntegrationPolicy();
        var files = new StubFiles(Credentials);
        var source = new ClaudeConsentCredentialSource(policy, new ClaudeCredentialReader(files), Root);
        var handler = new StubHandler();
        Assert.Equal(CredentialAvailability.Disabled, await source.GetAvailabilityAsync(CancellationToken.None));
        AssertFailure(await new ClaudeQuotaHttpProvider(handler, policy, source).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Unavailable, "quota_disabled");
        Assert.Equal(0, files.Calls);
        Assert.Equal(0, handler.Calls);
        policy.Enable();
        Assert.Equal(CredentialAvailability.Available, await source.GetAvailabilityAsync(CancellationToken.None));
        Assert.Equal(CredentialAvailability.Available, source.LastAvailability);
        Assert.Equal(1, files.Calls);
        policy.Disable();
        Assert.Equal(CredentialAvailability.Disabled, await source.GetAvailabilityAsync(CancellationToken.None));
        Assert.Equal(1, files.Calls);
        AssertSafe(source.ToString());
    }

    [Fact]
    public async Task Credential_disposal_clears_sensitive_values_and_ToString_is_safe()
    {
        var credential = new ClaudeRequestCredential(Token, Now.AddHours(1), "max");
        AssertSafe(credential.ToString());
        credential.Dispose();
        Assert.Null(credential.AccessToken);
        AssertSafe(credential.ToString());
        using var read = await new ClaudeCredentialReader(new StubFiles(Credentials)).ReadAsync(Root, CancellationToken.None);
        AssertSafe(read!.ToString());
    }

    [Fact]
    public async Task Malformed_body_and_file_exceptions_never_escape_with_private_details()
    {
        var handler = new StubHandler(Json("{" + Token + RefreshToken + Root + BodySecret));
        AssertFailure(await Provider(handler).GetQuotaAsync(CancellationToken.None), QuotaErrorKind.MalformedResponse, "quota_malformed");
        var provider = Provider(new StubHandler(), new StubFiles(Credentials, new IOException(Token + RefreshToken + Root + BodySecret)));
        AssertSafe(provider.ToString());
        AssertFailure(await provider.GetQuotaAsync(CancellationToken.None), QuotaErrorKind.Unavailable, "quota_credential_unusable");
    }

    [Fact]
    public async Task Plan_comes_from_existing_read_only_credential_without_extra_requests()
    {
        var handler = new StubHandler(Json(Valid));
        using var provider = Provider(handler);
        Assert.Null(provider.PlanLabel);
        Assert.Null((await provider.GetQuotaAsync(default)).Failure);
        Assert.Equal("Max", provider.PlanLabel);
        Assert.Equal(1, handler.Calls);
    }

    private static ClaudeQuotaHttpProvider Provider(HttpMessageHandler handler, StubFiles? files = null, List<TimeSpan>? delays = null)
    {
        var policy = new PrivateIntegrationPolicy(true);
        var source = new ClaudeConsentCredentialSource(policy, new ClaudeCredentialReader(files ?? new StubFiles(Credentials)), Root);
        return new(handler, policy, source, requestTimeout: TimeSpan.FromMilliseconds(50), utcNow: () => Now,
            delay: (value, _) => { delays?.Add(value); return Task.CompletedTask; });
    }

    private static void AssertFailure(QuotaProviderResult result, QuotaErrorKind kind, string code)
    {
        Assert.Null(result.Snapshot);
        Assert.NotNull(result.Failure);
        Assert.Equal(kind, result.Failure!.Kind);
        Assert.Equal(code, result.Failure.SafeCode);
        AssertSafe(result.ToString());
        AssertSafe(result.Failure.ToString());
    }

    private static void AssertSafe(string? text)
    {
        Assert.NotNull(text);
        foreach (var secret in new[] { Token, RefreshToken, Root, Root.Replace('/', '\\'), BodySecret })
            Assert.DoesNotContain(secret, text!);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Response(HttpStatusCode status) => new(status) { Content = new StringContent(Token + RefreshToken + Root + BodySecret) };

    private sealed class StubFiles(string content, Exception? error = null) : ICredentialFileReader
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }
        public FileShare LastShare { get; private set; }
        public ReadOnlyTrackingStream? LastStream { get; private set; }

        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        {
            Calls++; LastPath = path; LastShare = share;
            cancellationToken.ThrowIfCancellationRequested();
            if (error is not null) return ValueTask.FromException<Stream>(error);
            LastStream = new ReadOnlyTrackingStream(Encoding.UTF8.GetBytes(content));
            return ValueTask.FromResult<Stream>(LastStream);
        }
    }

    private sealed class ReadOnlyTrackingStream(byte[] content) : MemoryStream(content, writable: false)
    {
        public bool WasDisposed { get; private set; }
        public int WriteAttempts { get; private set; }
        public override void Write(byte[] buffer, int offset, int count) { WriteAttempts++; throw new InvalidOperationException("Unexpected write."); }
        public override void Write(ReadOnlySpan<byte> buffer) { WriteAttempts++; throw new InvalidOperationException("Unexpected write."); }
        public override void WriteByte(byte value) { WriteAttempts++; throw new InvalidOperationException("Unexpected write."); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) { WriteAttempts++; return Task.FromException(new InvalidOperationException("Unexpected write.")); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) { WriteAttempts++; return ValueTask.FromException(new InvalidOperationException("Unexpected write.")); }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }

    private sealed record CapturedRequest(HttpMethod Method, string? Uri, string? Scheme, string? Token, string? Beta, string? Version, bool HasAccountId, bool HasContent);

    private sealed class StubHandler(params object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new(responses);
        public int Calls { get; private set; }
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Requests.Add(new(request.Method, request.RequestUri?.AbsoluteUri, request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("anthropic-beta", out var beta) ? string.Join(",", beta) : null,
                request.Headers.TryGetValues("anthropic-version", out var version) ? string.Join(",", version) : null,
                request.Headers.Contains("ChatGPT-Account-Id"), request.Content is not null));
            var next = _responses.Dequeue();
            return next is Exception error ? Task.FromException<HttpResponseMessage>(error) : Task.FromResult((HttpResponseMessage)next);
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public int Cancellations { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancellations++; throw; }
            throw new InvalidOperationException("Expected cancellation.");
        }
    }
}
