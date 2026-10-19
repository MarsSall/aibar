using System.Net;
using System.Text;
using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Domain.Tests;

public sealed class ProviderStatusTests
{
    private const string Incident = "{\"page\":{},\"status\":{\"indicator\":\"minor\",\"description\":\"Degraded performance\"}}";

    [Theory]
    [InlineData("none", false)]
    [InlineData("minor", true)]
    [InlineData("major", true)]
    [InlineData("critical", true)]
    public void ParsesOnlyKnownIndicators(string indicator, bool visible)
    {
        var status = ProviderStatus.Parse(Encoding.UTF8.GetBytes(
            "{\"status\":{\"indicator\":\"" + indicator + "\",\"description\":\"Service status\"}}"));
        Assert.NotNull(status);
        Assert.Equal(indicator, status.Indicator);
        Assert.Equal("Service status", status.Description);
        Assert.Equal(visible, status.HasIncident);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"status\":null}")]
    [InlineData("{\"status\":{\"indicator\":\"MINOR\",\"description\":\"x\"}}")]
    [InlineData("{\"status\":{\"indicator\":\"unknown\",\"description\":\"x\"}}")]
    [InlineData("{\"status\":{\"indicator\":1,\"description\":\"x\"}}")]
    [InlineData("{\"status\":{\"indicator\":\"minor\"}}")]
    [InlineData("{\"status\":{\"indicator\":\"minor\",\"description\":null}}")]
    [InlineData("{\"status\":{\"indicator\":\"minor\",\"description\":\" \"}}")]
    [InlineData("{\"status\":{\"indicator\":\"minor\",\"description\":\"x\\ny\"}}")]
    [InlineData("{\"status\":{\"indicator\":\"none\",\"indicator\":\"minor\",\"description\":\"x\"}}")]
    [InlineData("{\"status\":{\"indicator\":\"minor\",\"description\":\"x\",\"url\":\"https://evil.invalid\"}}")]
    [InlineData("{\"status\":{},\"status\":{\"indicator\":\"minor\",\"description\":\"x\"}}")]
    [InlineData("{\"status\":{\"indicator\":\"minor\",\"description\":\"x\"},}")]
    public void InvalidPayloadShowsNothing(string json) => Assert.Null(ProviderStatus.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ParserEnforcesByteCapAndUtf8()
    {
        var exact = Encoding.UTF8.GetBytes(Incident.PadRight(65536));
        Assert.NotNull(ProviderStatus.Parse(exact));
        Assert.Null(ProviderStatus.Parse(Encoding.UTF8.GetBytes(Incident.PadRight(65537))));
        Assert.Null(ProviderStatus.Parse(new byte[] { 0xff }));
    }

    [Fact]
    public async Task HttpUsesOnlyFixedEndpointsAndNoRequestCredentials()
    {
        var requests = new List<string>();
        using var client = new ProviderStatusClient(new Handler((request, _) =>
        {
            requests.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Empty(request.Headers);
            Assert.Null(request.Content);
            return Task.FromResult(Response(Incident));
        }));
        Assert.True((await client.FetchAsync(StatusProvider.Codex, default))!.HasIncident);
        Assert.NotNull(await client.FetchAsync(StatusProvider.Claude, default));
        Assert.Equal(new[] { "https://status.openai.com/api/v2/status.json", "https://status.claude.com/api/v2/status.json" }, requests);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.FetchAsync((StatusProvider)99, default));
    }

    [Fact]
    public void ProductionTransportDisablesRedirectsCookiesCredentialsAndProxy()
    {
        using var handler = ProviderStatusClient.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseDefaultCredentials);
        Assert.Null(handler.Credentials);
        Assert.False(handler.UseProxy);
        Assert.Equal(TimeSpan.FromSeconds(10), ProviderStatusClient.RequestTimeout);
        Assert.Equal(65536, ProviderStatusClient.MaximumBytes);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(401)]
    [InlineData(500)]
    public async Task NonSuccessShowsNothingAndDoesNotFollowLocation(int code)
    {
        var calls = 0;
        using var client = new ProviderStatusClient(new Handler((_, _) =>
        {
            calls++;
            var response = Response(Incident);
            response.StatusCode = (HttpStatusCode)code;
            response.Headers.Location = new Uri("https://evil.invalid");
            return Task.FromResult(response);
        }));
        Assert.Null(await client.FetchAsync(StatusProvider.Codex, default));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task HttpEnforcesCapWithAndWithoutDeclaredLength()
    {
        foreach (var declared in new[] { true, false })
        {
            using var client = new ProviderStatusClient(new Handler((_, _) =>
            {
                var response = Response(Incident.PadRight(65537));
                if (!declared) response.Content = new StreamContent(new UnknownLengthStream(Encoding.UTF8.GetBytes(Incident.PadRight(65537))));
                return Task.FromResult(response);
            }));
            Assert.Null(await client.FetchAsync(StatusProvider.Codex, default));
        }
    }

    [Fact]
    public async Task TenSecondDeadlineCancelsTransportAndShowsNothing()
    {
        CancellationToken observed = default;
        using var client = new ProviderStatusClient(new Handler(async (_, token) =>
        {
            observed = token;
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response(Incident);
        }));
        Assert.Null(await client.FetchAsync(StatusProvider.Codex, default).WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(observed.IsCancellationRequested);
    }

    [Fact]
    public async Task ExactHttpByteCapIsAccepted()
    {
        using var client = new ProviderStatusClient(new Handler((_, _) => Task.FromResult(Response(Incident.PadRight(65536)))));
        Assert.NotNull(await client.FetchAsync(StatusProvider.Codex, default));
    }

    [Fact]
    public async Task NetworkAndCancellationFailuresShowNothing()
    {
        foreach (var error in new Exception[] { new HttpRequestException(), new OperationCanceledException(), new IOException() })
        {
            using var client = new ProviderStatusClient(new Handler((_, _) => Task.FromException<HttpResponseMessage>(error)));
            Assert.Null(await client.FetchAsync(StatusProvider.Claude, default));
        }
    }

    [Fact]
    public async Task EnabledProvidersOnlyPollAtFiveMinuteBoundaryAndFailureClearsIncident()
    {
        var clock = new Clock();
        var calls = new List<StatusProvider>();
        var fail = false;
        using var poller = new ProviderStatusPoller((provider, _) =>
        {
            calls.Add(provider);
            return Task.FromResult(fail ? null : ProviderStatus.Parse(Encoding.UTF8.GetBytes(Incident)));
        }, () => clock.Now);
        var changes = new List<(StatusProvider, ProviderStatus?)>();
        poller.Changed += (provider, status) => changes.Add((provider, status));
        await poller.RefreshAsync();
        Assert.Empty(calls);
        poller.SetEnabled(StatusProvider.Codex, true);
        await poller.RefreshAsync();
        Assert.Equal(new[] { StatusProvider.Codex }, calls);
        Assert.True(changes.Last().Item2!.HasIncident);
        Assert.Equal(TimeSpan.FromMinutes(5), poller.NextPollDelay);
        clock.Now += TimeSpan.FromMinutes(5) - TimeSpan.FromTicks(1);
        await poller.RefreshAsync();
        Assert.Single(calls);
        clock.Now += TimeSpan.FromTicks(1);
        fail = true;
        await poller.RefreshAsync();
        Assert.Equal(2, calls.Count);
        Assert.Null(changes.Last().Item2);
        poller.SetEnabled(StatusProvider.Claude, true);
        await poller.RefreshAsync();
        Assert.Equal(StatusProvider.Claude, calls.Last());
        poller.SetEnabled(StatusProvider.Codex, false);
        Assert.Null(changes.Last().Item2);
        clock.Now += TimeSpan.FromMinutes(5);
        await poller.RefreshAsync();
        Assert.Equal(2, calls.Count(p => p == StatusProvider.Codex));
    }

    [Fact]
    public async Task DisableCancelsAndSuppressesLateResultAndReenablePollsImmediately()
    {
        var pending = new TaskCompletionSource<ProviderStatus?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        using var poller = new ProviderStatusPoller((_, token) => { observed = token; return pending.Task; });
        var changes = new List<ProviderStatus?>();
        poller.Changed += (_, status) => changes.Add(status);
        poller.SetEnabled(StatusProvider.Claude, true);
        var refresh = poller.RefreshAsync();
        poller.SetEnabled(StatusProvider.Claude, false);
        Assert.True(observed.IsCancellationRequested);
        pending.SetResult(ProviderStatus.Parse(Encoding.UTF8.GetBytes(Incident)));
        await refresh;
        Assert.All(changes, status => Assert.Null(status));
        poller.SetEnabled(StatusProvider.Claude, true);
        await poller.RefreshAsync();
        Assert.NotNull(changes.Last());
        poller.Dispose();
        Assert.Null(changes.Last());
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class UnknownLengthStream(byte[] buffer) : MemoryStream(buffer)
    {
        public override bool CanSeek => false;
    }
    private sealed class Clock { public DateTimeOffset Now = DateTimeOffset.Parse("2026-06-01T00:00:00Z"); }
}
