using System.Text.Json;
namespace AIBar.Application;
public sealed record SessionCheckpoint(long Identity, long Length, long LastWriteUtcTicks, long Offset, string ParserVersion, long CumulativeInputTokens = 0, long CumulativeCachedInputTokens = 0, long CumulativeOutputTokens = 0);
public sealed record SessionTokenRecord(DateTimeOffset Timestamp, string Model, long InputTokens, long CachedInputTokens, long OutputTokens);
public sealed record SessionParseResult(IReadOnlyList<SessionTokenRecord> Records, IReadOnlyList<string> WarningCodes, bool RebuildRequired);
public sealed record SessionPreparationResult(IReadOnlyList<SessionTokenRecord> Records, IReadOnlyList<string> WarningCodes, bool RebuildRequired, SessionCheckpoint? ProposedCheckpoint);
public sealed class SessionCheckpointStore
{
    private readonly Dictionary<string, SessionCheckpoint> _checkpoints = new(StringComparer.OrdinalIgnoreCase);
    public ValueTask<SessionCheckpoint?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_checkpoints.TryGetValue(path, out var checkpoint) ? checkpoint : null);
    }
    public ValueTask CommitAsync(string path, SessionCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _checkpoints[path] = checkpoint;
        return ValueTask.CompletedTask;
    }
}
public sealed class SessionJsonlScanner(SessionCheckpointStore checkpoints, string parserVersion, Action? beforeCommit = null, Func<string, Stream>? open = null, Action<string>? beforeStable = null)
{
        public async ValueTask<SessionParseResult> ScanAsync(string path, CancellationToken cancellationToken)
        {
            var preparation = await PrepareAsync(path, await checkpoints.LoadAsync(path, cancellationToken), cancellationToken);
            if (preparation.ProposedCheckpoint is null) return Result(preparation.Records, preparation.WarningCodes, preparation.RebuildRequired);
            beforeCommit?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            var proposed = preparation.ProposedCheckpoint;
            if (!Stable(path, new(proposed.Identity, proposed.Length, proposed.LastWriteUtcTicks)))
                return Result([], preparation.WarningCodes.Append("session_file_changed"), preparation.RebuildRequired);
            await checkpoints.CommitAsync(path, proposed, cancellationToken);
            return Result(preparation.Records, preparation.WarningCodes, preparation.RebuildRequired);
        }
        public async ValueTask<SessionPreparationResult> PrepareAsync(string path, SessionCheckpoint? checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySnapshot(path, out var initial)) return Preparation([], ["session_file_changed"]);
            var rebuild = checkpoint is not null && (!ValidOffset(path, checkpoint.Offset, checkpoint.Length, initial.Length) || checkpoint.Identity != initial.Identity || checkpoint.Length > initial.Length || checkpoint.ParserVersion != parserVersion || (checkpoint.Length == initial.Length && checkpoint.LastWriteUtcTicks != initial.LastWriteUtcTicks));
            if (checkpoint is not null && !rebuild && checkpoint.Length == initial.Length && checkpoint.LastWriteUtcTicks == initial.LastWriteUtcTicks)
                return Preparation([], checkpoint.Offset < checkpoint.Length ? ["session_incomplete_tail"] : [], false, checkpoint);
            var offset = rebuild ? 0 : checkpoint?.Offset ?? 0;
            var records = new List<SessionTokenRecord>();
            var warnings = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                await using var stream = (open ?? (file => new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan)))(path);
                stream.Position = offset;
                var line = new List<byte>(); var safeOffset = offset;
                while (stream.ReadByte() is var next && next >= 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (next != '\n') { line.Add((byte)next); continue; }
                    safeOffset = stream.Position; Parse(line, records, warnings); line.Clear();
                }
                if (line.Count > 0) warnings.Add("session_incomplete_tail");
                beforeStable?.Invoke(path);
                if (!Stable(path, initial)) return Preparation([], warnings.Append("session_file_changed"), rebuild);
                var last = records.LastOrDefault();
                var cumulativeFallback = rebuild ? null : checkpoint;
                var proposed = new SessionCheckpoint(initial.Identity, initial.Length, initial.LastWriteUtcTicks, safeOffset, parserVersion, last?.InputTokens ?? cumulativeFallback?.CumulativeInputTokens ?? 0, last?.CachedInputTokens ?? cumulativeFallback?.CumulativeCachedInputTokens ?? 0, last?.OutputTokens ?? cumulativeFallback?.CumulativeOutputTokens ?? 0);
                return Preparation(records, warnings, rebuild, proposed);
            }
            catch (UnauthorizedAccessException) { return Preparation([], warnings.Append("session_file_unreadable"), rebuild); }
            catch (IOException) { return Preparation([], warnings.Append("session_file_changed"), rebuild); }
        }
    private static void Parse(List<byte> bytes, List<SessionTokenRecord> records, HashSet<string> warnings)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("timestamp", out var time) || time.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(time.GetString(), out var timestamp)) throw new JsonException();
            if (root.TryGetProperty("model", out var value) && value.ValueKind != JsonValueKind.String) throw new JsonException();
            var model = value.ValueKind == JsonValueKind.String && Trusted(value.GetString()) ? value.GetString()! : "Unknown";
            var usage = root.TryGetProperty("usage", out var usageValue) ? usageValue : default;
            records.Add(new(timestamp, model, Counter(usage, "input_tokens"), Counter(usage, "cached_input_tokens"), Counter(usage, "output_tokens")));
        }
        catch (JsonException) { warnings.Add("session_malformed_record"); }
    }
    private static bool ValidOffset(string path, long offset, long checkpointLength, long currentLength) =>
        offset >= 0 && offset <= checkpointLength && offset <= currentLength && (offset == 0 || EndsAtLineBoundary(path, offset));
    private static bool EndsAtLineBoundary(string path, long offset)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Position = offset - 1;
            return stream.ReadByte() == '\n';
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
    }
    private static long Counter(JsonElement usage, string name) => usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var count) ? Math.Max(0, count) : 0;
    private static bool Trusted(string? value) => !string.IsNullOrWhiteSpace(value) && value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool TrySnapshot(string path, out FileSnapshot snapshot)
    {
        try
        {
            var file = new FileInfo(path);
            file.Refresh();
            snapshot = new(file.CreationTimeUtc.Ticks, file.Length, file.LastWriteTimeUtc.Ticks);
            return file.Exists;
        }
        catch (UnauthorizedAccessException) { snapshot = default; return false; }
        catch (IOException) { snapshot = default; return false; }
    }
    private static bool Stable(string path, FileSnapshot initial) => TrySnapshot(path, out var current) && current == initial;
            private static SessionPreparationResult Preparation(IReadOnlyList<SessionTokenRecord> records, IEnumerable<string> warnings, bool rebuild = false, SessionCheckpoint? proposed = null) => new(records, warnings.Distinct().OrderBy(code => code, StringComparer.Ordinal).ToArray(), rebuild, proposed);
private static SessionParseResult Result(IReadOnlyList<SessionTokenRecord> records, IEnumerable<string> warnings, bool rebuild = false) => new(records, warnings.Distinct().OrderBy(code => code, StringComparer.Ordinal).ToArray(), rebuild);
    private readonly record struct FileSnapshot(long Identity, long Length, long LastWriteUtcTicks);
}
