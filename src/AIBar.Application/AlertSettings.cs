using System.Text.Json;
using AIBar.Domain;

namespace AIBar.Application;

public sealed record AlertOptions(bool Resets = true, bool Thresholds = true, bool RunOut = true, bool EdgeGlow = true,
    bool Notification = true, bool Confetti = false)
{
    public IReadOnlyList<int> ThresholdValues { get; init; } = QuotaAlertPolicy.DefaultThresholds;
    public bool Equals(AlertOptions? other) => other is not null && Resets == other.Resets && Thresholds == other.Thresholds &&
        RunOut == other.RunOut && EdgeGlow == other.EdgeGlow && Notification == other.Notification && Confetti == other.Confetti &&
        ThresholdValues.SequenceEqual(other.ThresholdValues);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Resets); hash.Add(Thresholds); hash.Add(RunOut); hash.Add(EdgeGlow); hash.Add(Notification); hash.Add(Confetti);
        foreach (var value in ThresholdValues) hash.Add(value);
        return hash.ToHashCode();
    }
}

public sealed class AlertSettings(string path)
{
    public async ValueTask<AlertOptions> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await QuotaAlertJson.ReadAsync<Stored>(path, cancellationToken).ConfigureAwait(false);
        return stored is { Schema: 1 } ? new(stored.Resets ?? true, stored.Thresholds ?? true, stored.RunOut ?? true,
            stored.EdgeGlow ?? true, stored.Notification ?? true, stored.Confetti ?? false)
        {
            ThresholdValues = QuotaAlertPolicy.ValidThresholds(stored.ThresholdValues)
                ? Array.AsReadOnly(stored.ThresholdValues!) : QuotaAlertPolicy.DefaultThresholds
        } : new();
    }
    public ValueTask SaveAsync(AlertOptions options, CancellationToken cancellationToken)
    {
        if (!QuotaAlertPolicy.ValidThresholds(options.ThresholdValues)) throw new ArgumentException("Thresholds must be ascending integers from 1 to 100.", nameof(options));
        return QuotaAlertJson.WriteAsync(path, new Stored(1, options.Resets, options.Thresholds, options.RunOut, options.EdgeGlow,
            options.Notification, options.Confetti, options.ThresholdValues.ToArray()), cancellationToken);
    }
    private sealed record Stored(int Schema, bool? Resets = null, bool? Thresholds = null, bool? RunOut = null, bool? EdgeGlow = null,
        bool? Notification = null, bool? Confetti = null, int[]? ThresholdValues = null);
}

internal static class QuotaAlertJson
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static async ValueTask<T?> ReadAsync<T>(string path, CancellationToken token) where T : class
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token).ConfigureAwait(false);
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
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options), token).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
