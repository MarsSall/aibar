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

    // Compatibility entry points now refer exclusively to Codex; dataSource is migration-only.
    public ValueTask<DataSource> LoadDataSourceAsync(CancellationToken cancellationToken) => LoadCodexSourceAsync(cancellationToken);
    public ValueTask SaveDataSourceAsync(DataSource source, CancellationToken cancellationToken) => SaveCodexSourceAsync(source, cancellationToken);

    public async ValueTask<DataSource> LoadCodexSourceAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadAsync<StoredSource>(cancellationToken);
        var value = stored is { Schema: Schema } ? ParseSource(stored.CodexSource.ValueKind == JsonValueKind.Undefined ? stored.DataSource : stored.CodexSource) : null;
        return value switch
        {
            ClaudeQuotaSource.ThisPc => DataSource.ThisPc,
            ClaudeQuotaSource.OtherMachine => DataSource.OtherMachine,
            _ => DataSource.Automatic
        };
    }

    public ValueTask SaveCodexSourceAsync(DataSource source, CancellationToken cancellationToken) =>
        SaveDecisionAsync(null, null, cancellationToken, codexSource: source.ToClaude());

    public async ValueTask<ClaudeQuotaSource> LoadClaudeSourceAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadAsync<StoredSource>(cancellationToken);
        return stored is { Schema: Schema } ? ParseSource(stored.ClaudeSource.ValueKind == JsonValueKind.Undefined ? stored.DataSource : stored.ClaudeSource) ?? ClaudeQuotaSource.Automatic : ClaudeQuotaSource.Automatic;
    }
    public ValueTask SaveClaudeSourceAsync(ClaudeQuotaSource source, CancellationToken cancellationToken)
    {
        if (source is not (ClaudeQuotaSource.Automatic or ClaudeQuotaSource.ThisPc or ClaudeQuotaSource.OtherMachine)) throw new ArgumentOutOfRangeException(nameof(source));
        return SaveDecisionAsync(null, null, cancellationToken, source);
    }

    // Cache provenance is independent of the user's selected mode. It prevents a restart
    // after file appearance/removal from treating the other source's saved values as current.
    internal async ValueTask<ClaudeQuotaSource?> LoadClaudeCacheSourceAsync(CancellationToken cancellationToken)
    {
        var stored = await ReadAsync<StoredSource>(cancellationToken);
        var source = stored is { Schema: Schema } ? ParseSource(stored.ClaudeCacheSource) : null;
        return source is ClaudeQuotaSource.ThisPc or ClaudeQuotaSource.OtherMachine ? source : null;
    }

    internal ValueTask SaveClaudeCacheSourceAsync(ClaudeQuotaSource source, CancellationToken cancellationToken) =>
        SaveDecisionAsync(null, null, cancellationToken, cacheSource: source);

    private static ClaudeQuotaSource? ParseSource(JsonElement? value) => value is { ValueKind: JsonValueKind.String } text
        ? text.GetString() switch
        {
            "automatic" => ClaudeQuotaSource.Automatic,
            "this-pc" => ClaudeQuotaSource.ThisPc,
            "other-machine" => ClaudeQuotaSource.OtherMachine,
            _ => null
        } : null;

    private static string SourceWireValue(ClaudeQuotaSource source) => source switch
    {
        ClaudeQuotaSource.Automatic => "automatic",
        ClaudeQuotaSource.ThisPc => "this-pc",
        ClaudeQuotaSource.OtherMachine => "other-machine",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

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

    private async ValueTask SaveDecisionAsync(bool? codex, bool? claude, CancellationToken cancellationToken,
        ClaudeQuotaSource? source = null, ClaudeQuotaSource? cacheSource = null, ClaudeQuotaSource? codexSource = null)
    {
        var gate = SaveGates.GetOrAdd(Path.GetFullPath(_path), _ => new(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Read fields independently: a malformed unrelated consent must not erase
            // source selection or cache provenance when another setting is saved.
            using var document = await ReadAsync<JsonDocument>(cancellationToken);
            StoredConsent? previous = null;
            if (document?.RootElement is { ValueKind: JsonValueKind.Object } root &&
                root.TryGetProperty("schema", out var schema) && schema.ValueKind == JsonValueKind.Number &&
                schema.TryGetInt32(out var version) && version == Schema)
            {
                bool? Decision(string name) => root.TryGetProperty(name, out var value) &&
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
                JsonElement? Field(string name) => root.TryGetProperty(name, out var value) ? value.Clone() : null;
                previous = new(Schema, Decision("privateCodexConsent") ?? false, Decision("privateClaudeConsent"),
                    Field("claudeSource") ?? Field("dataSource"), Field("claudeCacheSource"),
                    Field("codexSource") ?? Field("dataSource"));
            }
            var sourceValue = JsonSerializer.SerializeToElement(SourceWireValue(source ?? ParseSource(previous?.ClaudeSource) ?? ClaudeQuotaSource.Automatic));
            var cacheValue = cacheSource is null ? previous?.ClaudeCacheSource :
                JsonSerializer.SerializeToElement(SourceWireValue(cacheSource.Value));
            var settings = new StoredConsent(Schema, codex ?? previous?.PrivateCodexConsent ?? false,
                claude ?? previous?.PrivateClaudeConsent, sourceValue, cacheValue,
                JsonSerializer.SerializeToElement(SourceWireValue(codexSource ?? ParseSource(previous?.CodexSource) ?? ClaudeQuotaSource.Automatic)));
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
        [property: JsonPropertyName("privateClaudeConsent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PrivateClaudeConsent = null,
        [property: JsonPropertyName("claudeSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? ClaudeSource = null,
        [property: JsonPropertyName("claudeCacheSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? ClaudeCacheSource = null,
        [property: JsonPropertyName("codexSource"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? CodexSource = null);

    private sealed record StoredSource(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("claudeSource")] JsonElement ClaudeSource,
        [property: JsonPropertyName("claudeCacheSource")] JsonElement? ClaudeCacheSource,
        [property: JsonPropertyName("dataSource")] JsonElement DataSource,
        [property: JsonPropertyName("codexSource")] JsonElement CodexSource);
}
