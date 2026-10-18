using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIBar.Application;

public sealed class ConsentSettings(string path)
{
    private const int Schema = 1;
    private readonly string _path = path;
    // Both providers can save through different settings instances in the same process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveGates = new(StringComparer.OrdinalIgnoreCase);

    public async ValueTask<bool> LoadAsync(CancellationToken cancellationToken) =>
        await ReadAsync<StoredCodexConsent>(cancellationToken) is { Schema: Schema, PrivateCodexConsent: true };

    public async ValueTask<bool> LoadClaudeAsync(CancellationToken cancellationToken) =>
        await ReadAsync<StoredConsent>(cancellationToken) is { Schema: Schema, PrivateClaudeConsent: true };

    // RED seams: source selection is independent of either consent decision.
    public ValueTask<ClaudeQuotaSource> LoadClaudeSourceAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    public ValueTask SaveClaudeSourceAsync(ClaudeQuotaSource source, CancellationToken cancellationToken) => throw new NotImplementedException();

    public ValueTask SaveAsync(bool enabled, CancellationToken cancellationToken) => SaveDecisionAsync(enabled, null, cancellationToken);
    public ValueTask SaveClaudeAsync(bool enabled, CancellationToken cancellationToken) => SaveDecisionAsync(null, enabled, cancellationToken);

    private async ValueTask<T?> ReadAsync<T>(CancellationToken cancellationToken) where T : class
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (JsonException) { return null; }
    }

    private async ValueTask SaveDecisionAsync(bool? codex, bool? claude, CancellationToken cancellationToken)
    {
        var gate = SaveGates.GetOrAdd(Path.GetFullPath(_path), _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var previous = await ReadAsync<StoredConsent>(cancellationToken);
            if (previous?.Schema != Schema) previous = null;
            if (previous is null && await ReadAsync<StoredCodexConsent>(cancellationToken) is { Schema: Schema } codexOnly)
                previous = new(Schema, codexOnly.PrivateCodexConsent);
            var settings = new StoredConsent(Schema, codex ?? previous?.PrivateCodexConsent ?? false, claude ?? previous?.PrivateClaudeConsent);
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Consent settings require a directory.");
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings), cancellationToken);
                File.Move(temporary, _path, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    // Codex continues to ignore unrelated JSON fields, including a malformed Claude field.
    private sealed record StoredCodexConsent(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("privateCodexConsent")] bool PrivateCodexConsent);

    private sealed record StoredConsent(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("privateCodexConsent")] bool PrivateCodexConsent,
        [property: JsonPropertyName("privateClaudeConsent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PrivateClaudeConsent = null);
}
