namespace AIBar.Domain;

public enum UsageTool { OpenCode = 1, Pi = 2 }
public enum UsageSourceState { Active = 1, Unavailable = 2, Disabled = 3 }
public enum UsageEventKind { AssistantStep = 1, AssistantTerminal = 2, Compaction = 3, BranchSummary = 4, ToolResult = 5, OtherAggregate = 6 }
public enum UsagePurpose { Primary = 1, Compaction = 2, Subagent = 3, BranchSummary = 4, ToolResult = 5, Other = 6 }
public enum UsageOutcome { Success = 1, Error = 2, Aborted = 3, Deferred = 4, Unknown = 5 }
public enum UsageFinishReason { Unknown = 1, Stop = 2, Length = 3, ToolCall = 4, Error = 5, Other = 6 }
public enum UsageFidelity { SuccessfulSettledStep = 1, PersistedTerminalEvent = 2, PersistedAggregateEvent = 3 }
public enum TokenNormalizationWarning { ReasoningExceededOutput = 1 }

public abstract class UsageIdentity : IEquatable<UsageIdentity>
{
    private readonly byte[] _value;
    protected UsageIdentity(ReadOnlySpan<byte> value)
    {
        if (value.Length != 32) throw new ArgumentException("Identity must be exactly 32 bytes.", nameof(value));
        _value = value.ToArray();
    }
    public bool Equals(UsageIdentity? other) => other is not null && GetType() == other.GetType() && _value.AsSpan().SequenceEqual(other._value);
    public override bool Equals(object? obj) => Equals(obj as UsageIdentity);
    public override int GetHashCode() { var hash = new HashCode(); hash.Add(GetType()); foreach (var value in _value) hash.Add(value); return hash.ToHashCode(); }
    public override string ToString() => $"{GetType().Name}(opaque)";
    public byte[] ToArray() => _value.ToArray();
}

public sealed class UsageEventIdentity(ReadOnlySpan<byte> value) : UsageIdentity(value);
public sealed class UsageSourceIdentity(ReadOnlySpan<byte> value) : UsageIdentity(value);
public sealed class UsageSourceEventIdentity(ReadOnlySpan<byte> value) : UsageIdentity(value);

public sealed record UsageSource
{
    public UsageSource(UsageSourceIdentity identity, UsageTool tool, int identityVersion, int capabilityVersion,
        DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, UsageSourceState state, int warningFlags)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(tool) || !Enum.IsDefined(state) || identityVersion < 1 || capabilityVersion < 1 || warningFlags < 0 || firstSeenAt < DateTimeOffset.UnixEpoch || lastSeenAt < firstSeenAt)
            throw new ArgumentOutOfRangeException(nameof(identityVersion));
        Identity = identity; Tool = tool; IdentityVersion = identityVersion; CapabilityVersion = capabilityVersion;
        FirstSeenAt = firstSeenAt; LastSeenAt = lastSeenAt; State = state; WarningFlags = warningFlags;
    }
    public UsageSourceIdentity Identity { get; }
    public UsageTool Tool { get; }
    public int IdentityVersion { get; }
    public int CapabilityVersion { get; }
    public DateTimeOffset FirstSeenAt { get; }
    public DateTimeOffset LastSeenAt { get; }
    public UsageSourceState State { get; }
    public int WarningFlags { get; }
}

public sealed record UsageTokens
{
    public UsageTokens(long inputUncached, long cacheRead, long cacheWrite, long outputVisible, long outputReasoning, long? sourceReportedTotal = null)
    {
        if (new[] { inputUncached, cacheRead, cacheWrite, outputVisible, outputReasoning }.Any(value => value < 0) || sourceReportedTotal is < 0)
            throw new ArgumentOutOfRangeException(nameof(inputUncached), "Token counts cannot be negative.");
        InputUncached = inputUncached; CacheRead = cacheRead; CacheWrite = cacheWrite;
        OutputVisible = outputVisible; OutputReasoning = outputReasoning; SourceReportedTotal = sourceReportedTotal;
        checked { DisplayInput = inputUncached + cacheRead + cacheWrite; DisplayOutput = outputVisible + outputReasoning; DisplayTotal = DisplayInput + DisplayOutput; }
    }
    public long InputUncached { get; }
    public long CacheRead { get; }
    public long CacheWrite { get; }
    public long OutputVisible { get; }
    public long OutputReasoning { get; }
    public long? SourceReportedTotal { get; }
    public long DisplayInput { get; }
    public long DisplayOutput { get; }
    public long DisplayTotal { get; }
}

public sealed record UsageCost
{
    public UsageCost(long? nanos, string? currency)
    {
        if (nanos.HasValue != (currency is not null)) throw new ArgumentException("Cost and currency must both be present or absent.");
        if (nanos is < 0) throw new ArgumentOutOfRangeException(nameof(nanos));
        if (currency is not null && (currency.Length != 3 || currency.Any(character => character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z'))))
            throw new ArgumentException("Currency must contain exactly three ASCII letters.", nameof(currency));
        Nanos = nanos; Currency = currency?.ToUpperInvariant();
    }
    public long? Nanos { get; }
    public string? Currency { get; }
}

public sealed record UsageDisplayMetadata
{
    public UsageDisplayMetadata(string? provider, string? api, string? model)
    { Provider = Clean(provider); Api = Clean(api); Model = Clean(model); }
    public string? Provider { get; }
    public string? Api { get; }
    public string? Model { get; }
    private static string? Clean(string? value)
    {
        if (value?.Any(char.IsControl) is true) throw new ArgumentException("Display metadata must be at most 128 characters and contain no controls.");
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > 128) throw new ArgumentException("Display metadata must be at most 128 characters and contain no controls.");
        return value;
    }
}

public sealed record NormalizedUsageEvent
{
    public NormalizedUsageEvent(UsageEventIdentity eventIdentity, UsageSourceIdentity sourceIdentity, UsageSourceEventIdentity sourceEventIdentity,
        UsageTool tool, UsageEventKind kind, UsagePurpose purpose, UsageOutcome outcome, UsageFidelity fidelity,
        UsageFinishReason finishReason,
        DateTimeOffset occurredAt, DateTimeOffset observedAt, DateTimeOffset? sourceUpdatedAt, UsageDisplayMetadata metadata,
        UsageTokens tokens, UsageCost cost, int payloadVersion, bool isAggregate)
    {
        ArgumentNullException.ThrowIfNull(eventIdentity); ArgumentNullException.ThrowIfNull(sourceIdentity); ArgumentNullException.ThrowIfNull(sourceEventIdentity);
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(tokens); ArgumentNullException.ThrowIfNull(cost);
        if (!Enum.IsDefined(tool) || !Enum.IsDefined(kind) || !Enum.IsDefined(purpose) || !Enum.IsDefined(outcome) || !Enum.IsDefined(fidelity) || !Enum.IsDefined(finishReason)) throw new ArgumentOutOfRangeException(nameof(tool));
        if (payloadVersion < 1) throw new ArgumentOutOfRangeException(nameof(payloadVersion));
        if (occurredAt < DateTimeOffset.UnixEpoch || observedAt < occurredAt || sourceUpdatedAt is { } updatedAt && updatedAt < occurredAt)
            throw new ArgumentException("Occurrence must be on or after Unix epoch; observation and source update cannot precede occurrence.");
        EventIdentity = eventIdentity; SourceIdentity = sourceIdentity; SourceEventIdentity = sourceEventIdentity;
        Tool = tool; Kind = kind; Purpose = purpose; Outcome = outcome; FinishReason = finishReason; Fidelity = fidelity;
        OccurredAt = occurredAt; ObservedAt = observedAt; SourceUpdatedAt = sourceUpdatedAt; Metadata = metadata;
        Tokens = tokens; Cost = cost; PayloadVersion = payloadVersion; IsAggregate = isAggregate;
    }
    public UsageEventIdentity EventIdentity { get; }
    public UsageSourceIdentity SourceIdentity { get; }
    public UsageSourceEventIdentity SourceEventIdentity { get; }
    public UsageTool Tool { get; }
    public UsageEventKind Kind { get; }
    public UsagePurpose Purpose { get; }
    public UsageOutcome Outcome { get; }
    public UsageFinishReason FinishReason { get; }
    public UsageFidelity Fidelity { get; }
    public DateTimeOffset OccurredAt { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset? SourceUpdatedAt { get; }
    public UsageDisplayMetadata Metadata { get; }
    public UsageTokens Tokens { get; }
    public UsageCost Cost { get; }
    public int PayloadVersion { get; }
    public bool IsAggregate { get; }
}

public sealed record TokenNormalizationResult(UsageTokens Tokens, TokenNormalizationWarning? Warning);
public static class UsageTokenNormalization
{
    public static TokenNormalizationResult OpenCode(long input, long cacheRead, long cacheWrite, long output, long reasoning, long? total = null)
        => new(new(input, cacheRead, cacheWrite, output, reasoning, total), null);
    public static TokenNormalizationResult Pi(long input, long cacheRead, long cacheWrite, long outputIncludingReasoning, long reasoning, long? total = null)
    {
        if (outputIncludingReasoning < 0 || reasoning < 0) throw new ArgumentOutOfRangeException(nameof(outputIncludingReasoning));
        TokenNormalizationWarning? warning = reasoning > outputIncludingReasoning ? TokenNormalizationWarning.ReasoningExceededOutput : null;
        return new(new(input, cacheRead, cacheWrite, Math.Max(outputIncludingReasoning - reasoning, 0), reasoning, total), warning);
    }
}
