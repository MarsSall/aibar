using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIBar.Domain;

namespace AIBar.Application;

public enum PiUsageReadWarning { Unavailable = 1, UnsupportedSchema = 2, MalformedRecord = 3, OversizedRecord = 4, IncompleteTail = 5, Truncated = 6, RebuildRequired = 7 }
public sealed record PiUsageSourceAuthorization(UsageSourceIdentity SourceIdentity, int SourceSchemaVersion);
public sealed record PiUsageReadResult(IReadOnlyList<NormalizedUsageEvent> Events, int RecordsRead, int RecordsIgnored,
    int RecordsMalformed, IReadOnlyList<PiUsageReadWarning> Warnings, UsageSourceCheckpoint? NextCheckpoint = null);

public sealed class PiJsonlUsageSourceAdapter
{
    public const int MaximumRecords = 10_000;
    public const int MaximumRecordBytes = 256 * 1024;
    public const int SupportedSourceSchemaVersion = 3;
    private const int PayloadVersion = 1;
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly string _path;
    private readonly PiUsageSourceAuthorization _authorization;
    private readonly Action? _afterOpen;

    public PiJsonlUsageSourceAdapter(string jsonlPath, PiUsageSourceAuthorization authorization, Action? afterOpen = null)
    {
        if (string.IsNullOrWhiteSpace(jsonlPath)) throw new ArgumentException("A JSONL path is required.", nameof(jsonlPath));
        ArgumentNullException.ThrowIfNull(authorization); ArgumentNullException.ThrowIfNull(authorization.SourceIdentity);
        if (authorization.SourceSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(authorization));
        _path = jsonlPath; _authorization = authorization; _afterOpen = afterOpen;
    }

    public ValueTask<PiUsageReadResult> ReadAsync(int maximumRecords, CancellationToken cancellationToken = default)
        => PrepareAsync(null, maximumRecords, cancellationToken);

    public async ValueTask<PiUsageReadResult> PrepareAsync(UsageSourceCheckpoint? checkpoint, int maximumRecords, CancellationToken cancellationToken = default)
    {
        if (maximumRecords is < 1 or > MaximumRecords) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        if (checkpoint is not null && (!checkpoint.SourceIdentity.Equals(_authorization.SourceIdentity) || checkpoint.Tool != UsageTool.Pi
            || checkpoint.SourceSchemaVersion != _authorization.SourceSchemaVersion)) return Empty(PiUsageReadWarning.RebuildRequired);
        if (_authorization.SourceSchemaVersion != SupportedSourceSchemaVersion) return Empty(PiUsageReadWarning.UnsupportedSchema);
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            _afterOpen?.Invoke();
            var header = await ReadLine(stream, cancellationToken);
            if (header.Kind == LineKind.Incomplete) return Empty(PiUsageReadWarning.IncompleteTail);
            if (header.Kind == LineKind.Oversized) return Empty(PiUsageReadWarning.OversizedRecord);
            if (header.Kind != LineKind.Complete || !TryHeader(header.Bytes)) return Empty(PiUsageReadWarning.UnsupportedSchema);
            var headerEnd = stream.Position;
            var cursor = checkpoint?.Cursor ?? headerEnd;
            var currentState = cursor < headerEnd ? null : await PrefixState(stream, cursor, cancellationToken);
            if (currentState is null || checkpoint is not null && checkpoint.State != currentState)
                return Empty(PiUsageReadWarning.RebuildRequired);
            stream.Position = cursor;

            var events = new List<NormalizedUsageEvent>(maximumRecords); var read = 0; var ignored = 0; var malformed = 0;
            var warnings = new List<PiUsageReadWarning>();
            while (read < maximumRecords)
            {
                cancellationToken.ThrowIfCancellationRequested(); var start = stream.Position; var line = await ReadLine(stream, cancellationToken);
                if (line.Kind == LineKind.End) break;
                if (line.Kind == LineKind.Incomplete) { stream.Position = start; warnings.Add(PiUsageReadWarning.IncompleteTail); break; }
                if (line.Kind == LineKind.Oversized) { if (malformed > 0) warnings.Add(PiUsageReadWarning.MalformedRecord); warnings.Add(PiUsageReadWarning.OversizedRecord); return new([], read, ignored, malformed, warnings, null); }
                read++; cursor = stream.Position;
                if (!TryMap(line.Bytes, out var item, out var relevant)) { if (relevant) malformed++; else ignored++; }
                else events.Add(item);
            }
            if (malformed > 0) warnings.Insert(0, PiUsageReadWarning.MalformedRecord);
            if (read == maximumRecords && stream.Position < stream.Length) warnings.Add(PiUsageReadWarning.Truncated);
            var nextState = await PrefixState(stream, cursor, cancellationToken) ?? throw new IOException("Source changed during the bounded read.");
            return new(events.AsReadOnly(), read, ignored, malformed, warnings.AsReadOnly(),
                new(_authorization.SourceIdentity, UsageTool.Pi, SupportedSourceSchemaVersion, cursor, nextState));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (IOException) { return Empty(PiUsageReadWarning.Unavailable); }
        catch (UnauthorizedAccessException) { return Empty(PiUsageReadWarning.Unavailable); }
    }

    private bool TryMap(byte[] bytes, out NormalizedUsageEvent item, out bool relevant)
    {
        item = null!; relevant = true;
        try
        {
            StrictUtf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            if (!Text(root, "type", out var type) || !Text(root, "id", out var id, 256) || id.Length == 0 || !Timestamp(root, "timestamp", out var entryTime)) return false;
            JsonElement usage; string? provider = null, api = null, model = null; var outcome = UsageOutcome.Success;
            var kind = UsageEventKind.AssistantTerminal; var purpose = UsagePurpose.Primary; var finish = UsageFinishReason.Stop; var aggregate = false; var occurred = entryTime;
            if (type == "message")
            {
                if (!root.TryGetProperty("message", out var message) || !Text(message, "role", out var role) || role != "assistant") { relevant = false; return false; }
                if (!message.TryGetProperty("usage", out usage) || !Text(message, "stopReason", out var stop)) return false;
                if (stop is not ("stop" or "length" or "toolUse" or "error" or "aborted")) return false;
                if (!Text(message, "provider", out provider) || !Text(message, "api", out api) || !Text(message, "model", out model)
                    || !message.TryGetProperty("timestamp", out var timestamp) || !timestamp.TryGetInt64(out var milliseconds) || milliseconds is < 0 or > 253402300799999) return false;
                occurred = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
                (outcome, finish) = stop switch { "stop" => (UsageOutcome.Success, UsageFinishReason.Stop), "length" => (UsageOutcome.Success, UsageFinishReason.Length), "toolUse" => (UsageOutcome.Success, UsageFinishReason.ToolCall), "error" => (UsageOutcome.Error, UsageFinishReason.Error), _ => (UsageOutcome.Aborted, UsageFinishReason.Other) };
            }
            else if (type is "compaction" or "branch_summary")
            {
                if (!root.TryGetProperty("usage", out usage)) { relevant = false; return false; }
                outcome = UsageOutcome.Deferred; finish = UsageFinishReason.Other;
                aggregate = true; kind = type == "compaction" ? UsageEventKind.Compaction : UsageEventKind.BranchSummary;
                purpose = type == "compaction" ? UsagePurpose.Compaction : UsagePurpose.BranchSummary;
            }
            else { relevant = false; return false; }
            if (!Usage(usage, out var tokens)) return false;
            var sourceEvent = new UsageSourceEventIdentity(Hash("pi-source-event-v1", id));
            item = new(new UsageEventIdentity(Hash("pi-event-v1", id)), _authorization.SourceIdentity, sourceEvent, UsageTool.Pi, kind, purpose,
                outcome, aggregate ? UsageFidelity.PersistedAggregateEvent : UsageFidelity.PersistedTerminalEvent, finish,
                occurred, occurred > entryTime ? occurred : entryTime, null, new(provider, api, model), tokens, new(null, null), PayloadVersion, aggregate);
            return true;
        }
        catch (JsonException) { return false; }
        catch (DecoderFallbackException) { return false; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static bool Usage(JsonElement usage, out UsageTokens tokens)
    {
        tokens = null!;
        if (!Number(usage, "input", out var input) || !Number(usage, "output", out var output) || !Number(usage, "cacheRead", out var read)
            || !Number(usage, "cacheWrite", out var write) || !Number(usage, "totalTokens", out var total)
            || !usage.TryGetProperty("cost", out var cost) || cost.ValueKind != JsonValueKind.Object) return false;
        long reasoning = 0;
        if (usage.TryGetProperty("reasoning", out _) && !Number(usage, "reasoning", out reasoning)) return false;
        var normalized = UsageTokenNormalization.Pi(input, read, write, output, reasoning, total);
        if (normalized.Warning is not null) return false; tokens = normalized.Tokens; return true;
    }

    private static bool TryHeader(byte[] bytes)
    {
        try
        {
            StrictUtf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            if (!Text(root, "type", out var type) || type != "session" || !root.TryGetProperty("version", out var version) || version.GetInt32() != 3
                || !Text(root, "id", out var id, 256) || id.Length == 0) return false;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException or InvalidOperationException or FormatException or OverflowException) { return false; }
    }

    private static async Task<Line> ReadLine(FileStream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream(); var one = new byte[1];
        while (true)
        {
            var count = await stream.ReadAsync(one, token);
            if (count == 0) return buffer.Length == 0 ? new(LineKind.End, []) : new(LineKind.Incomplete, []);
            if (one[0] == (byte)'\n') return new(LineKind.Complete, buffer.ToArray());
            if (buffer.Length == MaximumRecordBytes) return new(LineKind.Oversized, []);
            buffer.WriteByte(one[0]);
        }
    }

    private async Task<string?> PrefixState(FileStream stream, long cursor, CancellationToken token)
    {
        if (cursor < 0 || cursor > stream.Length) return null;
        stream.Position = 0; using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("pi-prefix-v1")); hash.AppendData(_authorization.SourceIdentity.ToArray());
        var buffer = new byte[64 * 1024]; var remaining = cursor;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
            if (read == 0) return null; hash.AppendData(buffer.AsSpan(0, read)); remaining -= read;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
    private byte[] Hash(string domain, string sourceEventId)
    {
        var domainBytes = Encoding.UTF8.GetBytes(domain); var sourceBytes = _authorization.SourceIdentity.ToArray(); var eventBytes = Encoding.UTF8.GetBytes(sourceEventId);
        var input = new byte[12 + domainBytes.Length + sourceBytes.Length + eventBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(input, domainBytes.Length); domainBytes.CopyTo(input, 4);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4 + domainBytes.Length), sourceBytes.Length); sourceBytes.CopyTo(input, 8 + domainBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(8 + domainBytes.Length + sourceBytes.Length), eventBytes.Length); eventBytes.CopyTo(input, 12 + domainBytes.Length + sourceBytes.Length);
        return SHA256.HashData(input);
    }
    private static bool Text(JsonElement value, string property, out string result, int maximum = 128) { result = ""; return value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && (result = item.GetString() ?? "").Length <= maximum; }
    private static bool Timestamp(JsonElement value, string property, out DateTimeOffset result) { result = default; return Text(value, property, out var text) && DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out result) && result >= DateTimeOffset.UnixEpoch; }
    private static bool Number(JsonElement value, string property, out long result) { result = 0; return value.TryGetProperty(property, out var item) && item.TryGetInt64(out result) && result >= 0; }
    private static PiUsageReadResult Empty(PiUsageReadWarning warning) => new([], 0, 0, 0, [warning]);
    private enum LineKind { Complete, Incomplete, Oversized, End }
    private sealed record Line(LineKind Kind, byte[] Bytes);
}
