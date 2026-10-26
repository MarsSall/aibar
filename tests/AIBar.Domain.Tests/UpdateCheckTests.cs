using System.Net;
using System.Text;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class UpdateCheckTests
{
    [Theory]
    [InlineData("v0.2.0", true)]
    [InlineData("v0.1.0-beta.4", false)]
    [InlineData("v0.1.0-beta.3", false)]
    [InlineData("v0.1.0-beta.10", true)]
    [InlineData("v0.1.0", true)]
    [InlineData("v0.1.0-alpha", false)]
    [InlineData("v0.1.0-beta.04", false)]
    [InlineData("v01.2.0", false)]
    [InlineData("garbage", false)]
    public void Semver_precedence(string tag, bool newer) => Assert.Equal(newer, UpdateCheckClient.IsNewer(tag, "0.1.0-beta.4+build"));

    [Theory]
    [InlineData("v1.0.0-beta", "1.0.0", false)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.9", "1.0.0", false)]
    [InlineData("v1.0.0-beta.1", "1.0.0-beta", true)]
    [InlineData("v1.0.0-beta.a", "1.0.0-beta.1", true)]
    public void Release_and_identifier_precedence(string tag, string current, bool newer) => Assert.Equal(newer, UpdateCheckClient.IsNewer(tag, current));

    [Theory]
    [InlineData("https://github.com/MarsSall/aibar/releases/tag/v1.0.0", true)]
    [InlineData("https://github.com/other/aibar/releases/tag/v1.0.0", false)]
    [InlineData("http://github.com/MarsSall/aibar/releases/tag/v1.0.0", false)]
    [InlineData("https://example.com/MarsSall/aibar/releases/tag/v1.0.0", false)]
    public async Task Only_allowed_release_pages(string url, bool allowed)
    {
        using var client = new UpdateCheckClient(new Handler((_, _) => Task.FromResult(Json("v1.0.0", url))));
        Assert.Equal(allowed, await client.FetchAsync("0.1.0", default) is not null);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("403")]
    [InlineData("oversized")]
    [InlineData("bad-json")]
    [InlineData("bad-shape")]
    [InlineData("chunked-oversized")]
    [InlineData("invalid-tag")]
    public async Task Failures_are_silent(string failure)
    {
        using var client = new UpdateCheckClient(new Handler((_, _) => failure switch
        {
            "timeout" => throw new TaskCanceledException(),
            "403" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)),
            "oversized" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', UpdateCheckClient.MaximumBytes + 1)) }),
            "bad-shape" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"tag_name\":42,\"html_url\":[]}") }),
            "chunked-oversized" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new UndeclaredContent() }),
            "invalid-tag" => Task.FromResult(Json("invalid", "https://github.com/MarsSall/aibar/releases/tag/invalid")),
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{") })
        }));
        Assert.Null(await client.FetchAsync("0.1.0", default));
    }

    [Fact]
    public async Task Request_is_anonymous_and_has_github_user_agent()
    {
        using var client = new UpdateCheckClient(new Handler((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/MarsSall/aibar/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.NotEmpty(request.Headers.UserAgent);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(Json("v1.0.0", "https://github.com/MarsSall/aibar/releases/tag/v1.0.0"));
        }));
        Assert.NotNull(await client.FetchAsync("0.1.0", default));
        using var handler = UpdateCheckClient.CreateHandler();
        Assert.False(handler.UseCookies); Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseDefaultCredentials); Assert.Null(handler.Credentials);
    }

    [Fact]
    public async Task Off_makes_no_request_and_other_settings_preserve_it()
    {
        var root = Path.Combine(Path.GetTempPath(), "aibar-update-tests-" + Guid.NewGuid().ToString("N"));
        var settings = new ConsentSettings(Path.Combine(root, "settings.json"));
        try
        {
            Assert.True(await settings.LoadUpdateCheckAsync(default));
            await settings.SaveAsync(true, default);
            Assert.True(await settings.LoadUpdateCheckAsync(default));
            await settings.SaveUpdateCheckAsync(false, default);
            await new ConsentSettings(Path.Combine(root, "settings.json")).SaveClaudeAsync(true, default);
            await settings.SaveCodexSourceAsync(DataSource.OtherMachine, default);
            Assert.False(await settings.LoadUpdateCheckAsync(default));
            Assert.True(await settings.LoadAsync(default));
            Assert.True(await settings.LoadClaudeAsync(default));
            Assert.Equal(DataSource.OtherMachine, await settings.LoadCodexSourceAsync(default));
            var calls = 0;
            using var client = new UpdateCheckClient(new Handler((_, _) => { calls++; return Task.FromResult(Json("v1.0.0", "https://github.com/MarsSall/aibar/releases/tag/v1.0.0")); }));
            Assert.Null(await client.CheckAsync(settings, "0.1.0", default));
            Assert.Equal(0, calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static HttpResponseMessage Json(string tag, string url) => new(HttpStatusCode.OK)
    { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, html_url = url }), Encoding.UTF8, "application/json") };
    private sealed class UndeclaredContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(new byte[UpdateCheckClient.MaximumBytes + 1]).AsTask();
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
