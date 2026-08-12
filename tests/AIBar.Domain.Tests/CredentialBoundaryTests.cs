using System.Text;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class CredentialBoundaryTests
{
    [Fact]
    public void Root_prefers_non_empty_Codex_home_and_falls_back_to_user_profile()
    {
        Assert.Equal("D:/synthetic/.codex", CodexRootResolver.Resolve("D:/synthetic/.codex", "D:/user"));
        Assert.Equal(Path.Combine("D:/user", ".codex"), CodexRootResolver.Resolve(" ", "D:/user"));
    }

    [Theory]
    [InlineData("{\"tokens\":{\"access_token\":\"nested-access\",\"refresh_token\":\"ignored-refresh\",\"account_id\":\"nested-account\"}}", "nested-account")]
    [InlineData("{\"tokens\":{\"access_token\":\"nested-access\",\"refresh_token\":\"ignored-refresh\"},\"access_token\":\"legacy-access\",\"account_id\":\"legacy-account\"}", null)]
    public async Task Reader_uses_current_nested_access_token_with_optional_account_id(string content, string? expectedAccountId)
    {
        var reader = new CodexCredentialReader(new StubFiles(content));

        using var credential = await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None);

        Assert.Equal("nested-access", credential!.AccessToken);
        Assert.Equal(expectedAccountId, credential.AccountId);
    }

    [Fact]
    public async Task Reader_prefers_the_complete_nested_pair_over_legacy_fields()
    {
        var reader = new CodexCredentialReader(new StubFiles("{\"tokens\":{\"access_token\":\"nested-access\",\"account_id\":\"nested-account\"},\"access_token\":\"legacy-access\",\"account_id\":\"legacy-account\"}"));

        using var credential = await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None);

        Assert.Equal("nested-access", credential!.AccessToken);
        Assert.Equal("nested-account", credential.AccountId);
    }

    [Theory]
    [InlineData("{\"access_token\":\"legacy-access\",\"account_id\":\"legacy-account\"}")]
    [InlineData("{\"tokens\":{\"account_id\":\"nested-account\"},\"access_token\":\"legacy-access\",\"account_id\":\"legacy-account\"}")]
    [InlineData("{\"tokens\":{\"access_token\":\" \",\"account_id\":\"nested-account\"},\"access_token\":\"legacy-access\",\"account_id\":\"legacy-account\"}")]
    public async Task Reader_falls_back_to_legacy_only_without_a_usable_nested_access_token(string content)
    {
        var reader = new CodexCredentialReader(new StubFiles(content));

        using var credential = await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None);

        Assert.Equal("legacy-access", credential!.AccessToken);
        Assert.Equal("legacy-account", credential.AccountId);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{bad")]
    [InlineData("{\"access_token\":\" \"}")]
    [InlineData("{\"tokens\":{}}")]
    [InlineData("{\"tokens\":{\"access_token\":\" \"}}")]
    [InlineData("{\"OPENAI_API_KEY\":\"ignored-api-key\",\"personal_access_token\":\"ignored-personal\",\"tokens\":{\"refresh_token\":\"ignored-refresh\",\"id_token\":\"ignored-id\"}}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"token-123\"")]
    public async Task Reader_rejects_missing_or_malformed_credentials(string content)
    {
        var reader = new CodexCredentialReader(new StubFiles(content));

        Assert.Null(await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None));
    }

    [Theory]
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(DirectoryNotFoundException))]
    public async Task Reader_returns_unavailable_when_auth_file_or_root_is_missing(Type exceptionType)
    {
        var reader = new CodexCredentialReader(new ThrowingFiles((Exception)Activator.CreateInstance(exceptionType)!));

        Assert.Null(await reader.ReadAsync("D:/missing/.codex", CancellationToken.None));
    }

    [Fact]
    public async Task Reader_preserves_non_missing_io_failures()
    {
        var reader = new CodexCredentialReader(new ThrowingFiles(new IOException("synthetic failure")));

        await Assert.ThrowsAsync<IOException>(async () => await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None));
    }

    [Fact]
    public async Task Reader_preserves_cancellation()
    {
        var reader = new CodexCredentialReader(new ThrowingFiles(new OperationCanceledException()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None));
    }

    [Fact]
    public void Disposed_credential_clears_the_access_token()
    {
        var credential = new RequestCredential("token-123", "acct-456");
        credential.Dispose();

        Assert.Null(credential.AccessToken);
    }

    [Fact]
    public void Private_integration_is_disabled_by_default_and_disclosed()
    {
        var policy = new PrivateIntegrationPolicy();

        Assert.False(policy.IsEnabled);
        Assert.Contains("private", policy.Disclosure, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsupported", policy.Disclosure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redactor_removes_seeded_secrets_ids_paths_and_bearer_values()
    {
        var text = "Bearer token-123 account_id=acct-456 path D:/synthetic/.codex/auth.json refresh_token=never-read " +
            "{\"access_token\":\"json-secret\",\"account_id\":\"json-account\"} \\\\server\\share\\auth.json";

        var redacted = SafeRedactor.Redact(text, new[] { "token-123", "acct-456", "never-read" });

        Assert.DoesNotContain("token-123", redacted);
        Assert.DoesNotContain("acct-456", redacted);
        Assert.DoesNotContain("never-read", redacted);
        Assert.DoesNotContain("D:/synthetic", redacted);
        Assert.DoesNotContain("json-secret", redacted);
        Assert.DoesNotContain("json-account", redacted);
        Assert.DoesNotContain(@"\\server\share", redacted);
    }

    [Theory]
    [InlineData("{\"password\":\"p value\",\"api_key\":\"api-value\",\"client_secret\":\"client-value\",\"cookie\":\"session=value\",\"secret\":\"generic-value\"}")]
    [InlineData("password=p-value api-key='api value' client_secret=client-value cookie=session-value signing_secret=generic-value")]
    public void Redactor_removes_secret_field_classes_without_seeded_values(string text)
    {
        var redacted = SafeRedactor.Redact(text, Array.Empty<string?>());

        Assert.DoesNotContain("p-value", redacted);
        Assert.DoesNotContain("p value", redacted);
        Assert.DoesNotContain("api-value", redacted);
        Assert.DoesNotContain("api value", redacted);
        Assert.DoesNotContain("client-value", redacted);
        Assert.DoesNotContain("session-value", redacted);
        Assert.DoesNotContain("session=value", redacted);
        Assert.DoesNotContain("generic-value", redacted);
    }

    [Fact]
    public async Task Reader_uses_share_read_and_tolerates_replacement()
    {
        var files = new StubFiles("{\"access_token\":\"first\"}") { ReplaceOnFirstRead = "{\"access_token\":\"second\"}" };
        var reader = new CodexCredentialReader(files);

        using var credential = await reader.ReadAsync("D:/synthetic/.codex", CancellationToken.None);

        Assert.Equal("second", credential!.AccessToken);
        Assert.Equal(FileShare.ReadWrite | FileShare.Delete, files.LastShare);
    }

    private sealed class StubFiles(string content) : ICredentialFileReader
    {
        private string _content = content;
        public string? ReplaceOnFirstRead { get; set; }
        public FileShare LastShare { get; private set; }

        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
        {
            LastShare = share;
            if (ReplaceOnFirstRead is { } replacement)
            {
                _content = replacement;
                ReplaceOnFirstRead = null;
            }
            Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(_content));
            return ValueTask.FromResult(stream);
        }
    }

    private sealed class ThrowingFiles(Exception exception) : ICredentialFileReader
    {
        public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken) =>
            ValueTask.FromException<Stream>(exception);
    }
}
