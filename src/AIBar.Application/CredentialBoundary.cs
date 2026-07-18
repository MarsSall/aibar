using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AIBar.Application;

public static class CodexRootResolver
{
    public static string Resolve(string? codexHome, string userProfile) =>
        string.IsNullOrWhiteSpace(codexHome) ? Path.Combine(userProfile, ".codex") : codexHome.Trim();
}

public interface ICredentialFileReader
{
    ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken);
}

public sealed class ReadOnlyCredentialFileReader : ICredentialFileReader
{
    public ValueTask<Stream> OpenReadAsync(string path, FileShare share, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, share, 4096, FileOptions.Asynchronous);
        return ValueTask.FromResult(stream);
    }
}

public sealed class CodexCredentialReader(ICredentialFileReader files)
{
    public async ValueTask<RequestCredential?> ReadAsync(string root, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await files.OpenReadAsync(
                Path.Combine(root, "auth.json"),
                FileShare.ReadWrite | FileShare.Delete,
                cancellationToken);
            var fields = await JsonSerializer.DeserializeAsync<CredentialFields>(stream, cancellationToken: cancellationToken);
            return string.IsNullOrWhiteSpace(fields?.AccessToken)
                ? null
                : new RequestCredential(fields.AccessToken, fields.AccountId);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }

    private sealed class CredentialFields
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("account_id")]
        public string? AccountId { get; init; }
    }
}

public sealed class RequestCredential(string accessToken, string? accountId) : IDisposable
{
    public string? AccessToken { get; private set; } = accessToken;
    public string? AccountId { get; private set; } = accountId;
    public void Dispose() { AccessToken = null; AccountId = null; }
}

public sealed class PrivateIntegrationPolicy(bool enabled = false)
{
    public bool IsEnabled { get; private set; } = enabled;
    public void Disable() => IsEnabled = false;
    public string Disclosure => "Quota integration is private, undocumented, and unsupported; it is disabled by default.";
}

public static partial class SafeRedactor
{
    public static string Redact(string? value, IEnumerable<string?> secrets)
    {
        var result = value ?? string.Empty;
        foreach (var secret in secrets.Where(secret => !string.IsNullOrWhiteSpace(secret)))
            result = result.Replace(secret!, "[REDACTED]", StringComparison.Ordinal);
        return SensitivePattern().Replace(result, "[REDACTED]");
    }

    [GeneratedRegex("(?i)bearer\\s+[^\\s]+|(?:access|refresh)[_-]?token[\\\"']?\\s*[=:]\\s*(?:\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'|[^\\s,;}]+)|account[_-]?id[\\\"']?\\s*[=:]\\s*(?:\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'|[^\\s,;}]+)|(?:password|api[_-]?key|client[_-]?secret|cookie|(?:[a-z0-9]+[_-])?secret)[\\\"']?\\s*[=:]\\s*(?:\\\"(?:\\\\.|[^\\\"\\\\])*\\\"|'(?:\\\\.|[^'\\\\])*'|[^\\s,;}]+)|[a-z]:[/\\\\][^\\s]+|\\\\\\\\[^\\s\\\\]+\\\\[^\\s]+")]
    private static partial Regex SensitivePattern();
}
