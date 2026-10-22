using System.Globalization;
using System.Text;

namespace AIBar.Application;

/// <summary>Asks the other machine for a fresh push and reports when one arrives.</summary>
public interface IClaudeRemoteRefreshChannel
{
    /// <summary>Best effort: a failed request only means waiting for the other machine's timer.</summary>
    ValueTask RequestAsync(DateTimeOffset now, CancellationToken cancellationToken);
    IDisposable WatchUsage(Action changed);
}

/// <summary>
/// Writes <c>claude-refresh-request.json</c> beside the pushed usage file. The request carries only
/// a timestamp; the Linux watcher reads it and starts its collector.
/// </summary>
public sealed class ClaudeRemoteRefreshChannel(string? directory = null) : IClaudeRemoteRefreshChannel
{
    public const string RequestFileName = "claude-refresh-request.json";
    private const string UsageFileName = "claude-remote-usage.json";
    private readonly string _directory = Path.GetFullPath(directory ?? Path.GetDirectoryName(ClaudeRemoteUsageProvider.DefaultPath())!);

    public async ValueTask RequestAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, RequestFileName);
        var staging = path + ".tmp";
        try
        {
            // Never create the directory: without it there is no other-machine push to ask for.
            if (!Directory.Exists(_directory)) return;
            var stamp = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            await File.WriteAllTextAsync(staging, "{\"schemaVersion\":1,\"requestedAt\":\"" + stamp + "\"}",
                new UTF8Encoding(false), cancellationToken);
            File.Move(staging, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            try { File.Delete(staging); } catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
        }
    }

    public IDisposable WatchUsage(Action changed)
    {
        try
        {
            var watcher = new FileSystemWatcher(_directory, UsageFileName)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            // File.Replace and Move publish by rename; a direct write raises Changed.
            FileSystemEventHandler handler = (_, _) => changed();
            watcher.Created += handler; watcher.Changed += handler;
            watcher.Renamed += (_, _) => changed();
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Without a watcher the one-minute poll still picks the push up.
            return new NoWatch();
        }
    }

    private sealed class NoWatch : IDisposable { public void Dispose() { } }
}

internal sealed class NoRemoteRefresh : IClaudeRemoteRefreshChannel, IDisposable
{
    public static NoRemoteRefresh Instance { get; } = new();
    public ValueTask RequestAsync(DateTimeOffset now, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    public IDisposable WatchUsage(Action changed) => this;
    public void Dispose() { }
}
