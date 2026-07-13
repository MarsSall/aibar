using System.Net;
using System.Text;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class QuotaHttpProviderTests
{
    [Fact]
    public async Task Maps_variant_windows_and_optional_credits_with_scoped_authorization()
    {
        var handler = new StubHandler(
            Json("{\"rate_limit\":{\"primary_window\":{\"used_percent\":25,\"reset_time\":\"2030-01-01T01:00:00Z\"},\"secondary_window\":{\"percentage_used\":75,\"reset_at\":\"2030-01-02T01:00:00Z\"}}}"),
            Json("{\"reset_credits\":3}"));
        var result = await Provider(handler).GetQuotaAsync(CancellationToken.None);
        Assert.Equal(25, result.Snapshot!.Primary.PercentageUsed); Assert.Equal(75, result.Snapshot.Weekly.PercentageUsed);
        Assert.Equal(3, result.ResetCredits); Assert.All(handler.Requests, r => Assert.Equal("Bearer secret-token", r.Headers.Authorization!.ToString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, QuotaErrorKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, QuotaErrorKind.Permission)]
    [InlineData(HttpStatusCode.BadRequest, QuotaErrorKind.Service)]
    public async Task Classifies_statuses_without_leaking_response_secrets(HttpStatusCode status, QuotaErrorKind kind)
    {
        var result = await Provider(new StubHandler(new HttpResponseMessage(status) { Content = new StringContent("Bearer secret-token account_id=private") })).GetQuotaAsync(CancellationToken.None);
        Assert.Equal(kind, result.Failure!.Kind); Assert.DoesNotContain("secret-token", result.Failure.SafeCode);
        var disabled = await new QuotaHttpProvider(new StubHandler(), new PrivateIntegrationPolicy(), _ => ValueTask.FromResult<RequestCredential?>(null)).GetQuotaAsync(CancellationToken.None);
        Assert.Equal(QuotaErrorKind.Unavailable, disabled.Failure!.Kind);
    }

    [Fact]
    public async Task Rejects_redirect_and_malformed_required_fields_without_retry()
    {
        var redirect = new StubHandler(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://elsewhere.invalid") } });
        var malformed = new StubHandler(Json("{\"five_hour\":{\"percentage_used\":101}}"));
        Assert.Equal(QuotaErrorKind.Redirect, (await Provider(redirect).GetQuotaAsync(CancellationToken.None)).Failure!.Kind);
        Assert.Equal(QuotaErrorKind.MalformedResponse, (await Provider(malformed).GetQuotaAsync(CancellationToken.None)).Failure!.Kind);
        Assert.Equal(1, redirect.Calls); Assert.Equal(1, malformed.Calls);
    }

    [Fact]
    public async Task Classifies_missing_credentials_and_valid_json_with_wrong_shapes()
    {
        var missing = new QuotaHttpProvider(new StubHandler(), new PrivateIntegrationPolicy(true), _ => ValueTask.FromResult<RequestCredential?>(null));
        Assert.Equal(QuotaErrorKind.Unavailable, (await missing.GetQuotaAsync(CancellationToken.None)).Failure!.Kind);
        Assert.Equal(QuotaErrorKind.MalformedResponse, (await Provider(new StubHandler(Json("[]"))).GetQuotaAsync(CancellationToken.None)).Failure!.Kind);
        var optional = await Provider(new StubHandler(Json(Valid), Json("[]"))).GetQuotaAsync(CancellationToken.None);
        Assert.NotNull(optional.Snapshot); Assert.Equal("quota_optional_malformed", optional.OptionalFailure!.SafeCode);
    }

    [Fact]
    public async Task Retries_one_transient_response_honoring_retry_after_and_keeps_primary_on_optional_failure()
    {
        var retry = new HttpResponseMessage((HttpStatusCode)429); retry.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
        var handler = new StubHandler(retry, Json(Valid), new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var result = await Provider(handler).GetQuotaAsync(CancellationToken.None);
        Assert.NotNull(result.Snapshot); Assert.Equal(3, handler.Calls); Assert.Equal(QuotaErrorKind.Service, result.OptionalFailure!.Kind);
    }

    [Fact]
    public async Task Honors_retry_after_http_dates_with_controlled_time_and_cap()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); var delays = new List<TimeSpan>();
        QuotaHttpProvider WithDate(DateTimeOffset date) { var retry = new HttpResponseMessage((HttpStatusCode)429); retry.Headers.RetryAfter = new(date); return new(new StubHandler(retry, Json(Valid), Json("{\"credits\":1}")), new PrivateIntegrationPolicy(true), _ => ValueTask.FromResult<RequestCredential?>(new("secret-token", null)), utcNow: () => now, delay: (value, _) => { delays.Add(value); return Task.CompletedTask; }); }
        await WithDate(now.AddMinutes(-1)).GetQuotaAsync(CancellationToken.None);
        await WithDate(now.AddMinutes(1)).GetQuotaAsync(CancellationToken.None);
        Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public async Task Honors_cancellation_and_classifies_timeout_or_transport_safely()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(new StubHandler()).GetQuotaAsync(cancelled.Token).AsTask());
        var result = await Provider(new StubHandler(new HttpRequestException("Bearer secret-token"), new HttpRequestException("Bearer secret-token"))).GetQuotaAsync(CancellationToken.None);
        Assert.Equal(QuotaErrorKind.Network, result.Failure!.Kind); Assert.DoesNotContain("secret-token", result.Failure.SafeCode);
        var timeout = await Provider(new StubHandler(new OperationCanceledException(), new OperationCanceledException())).GetQuotaAsync(CancellationToken.None);
        Assert.Equal("quota_timeout", timeout.Failure!.SafeCode);
    }

    private const string Valid = "{\"five_hour\":{\"percentage_used\":25,\"reset_at\":\"2030-01-01T01:00:00Z\"},\"weekly\":{\"percentage_used\":75,\"reset_at\":\"2030-01-02T01:00:00Z\"}}";
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static QuotaHttpProvider Provider(StubHandler handler) => new(handler, new PrivateIntegrationPolicy(true), _ => ValueTask.FromResult<RequestCredential?>(new("secret-token", "private")), TimeSpan.FromMilliseconds(50));

    private sealed class StubHandler(params object[] responses) : HttpMessageHandler
    {
        private readonly Queue<object> _responses = new(responses); public int Calls { get; private set; } public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; Requests.Add(request); var next = _responses.Dequeue(); return next is Exception error ? Task.FromException<HttpResponseMessage>(error) : Task.FromResult((HttpResponseMessage)next); }
    }
}
