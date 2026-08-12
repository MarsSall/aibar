using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIBar.Application;

public sealed class ConsentSettings(string path)
{
    private const int Schema = 1;
    private readonly string _path = path;

    public async ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            var settings = await JsonSerializer.DeserializeAsync<StoredConsent>(stream, cancellationToken: cancellationToken);
            return settings is { Schema: Schema, PrivateCodexConsent: true };
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (JsonException) { return false; }
    }

    public async ValueTask SaveAsync(bool enabled, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Consent settings require a directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new StoredConsent(Schema, enabled)), cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record StoredConsent(
        [property: JsonPropertyName("schema")] int Schema,
        [property: JsonPropertyName("privateCodexConsent")] bool PrivateCodexConsent);
}
