using System.Text.Json;

namespace AIBar.Application;

public sealed record AlertOptions(bool Resets = true, bool Thresholds = true, bool RunOut = true, bool EdgeGlow = true);

public sealed class AlertSettings(string path)
{
    public async ValueTask<AlertOptions> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await QuotaAlertJson.ReadAsync<Stored>(path, cancellationToken);
        return stored is { Schema: 1 } ? new(stored.Resets ?? true, stored.Thresholds ?? true, stored.RunOut ?? true, stored.EdgeGlow ?? true) : new();
    }
    public ValueTask SaveAsync(AlertOptions options, CancellationToken cancellationToken) =>
        QuotaAlertJson.WriteAsync(path, new Stored(1, options.Resets, options.Thresholds, options.RunOut, options.EdgeGlow), cancellationToken);
    private sealed record Stored(int Schema, bool? Resets = null, bool? Thresholds = null, bool? RunOut = null, bool? EdgeGlow = null);
}

internal static class QuotaAlertJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static async ValueTask<T?> ReadAsync<T>(string path, CancellationToken token) where T : class
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token);
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    public static async ValueTask WriteAsync<T>(string path, T value, CancellationToken token)
    {
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("Alert data requires a directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options), token);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
