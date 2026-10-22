using AIBar.Domain;

namespace AIBar.Application;

/// <summary>Claude facade for the shared strict aggregate-only token reader.</summary>
public static class ClaudeRemoteTokens
{
    public const int MaxBytes = RemoteTokens.MaxBytes;
    public const int MaxRows = RemoteTokens.MaxRows;

    public static async ValueTask<UsageCostSummary> ReadAsync(string path, DateTimeOffset now, CancellationToken token = default) =>
        (await RemoteTokens.ReadRowsAsync(path, now, token)).Summary;

    public static UsageCostSummary Parse(ReadOnlyMemory<byte> bytes, DateTimeOffset now) =>
        RemoteTokens.ParseRows(bytes, now).Summary;
}
