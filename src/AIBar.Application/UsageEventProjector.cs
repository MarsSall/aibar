using AIBar.Domain;

namespace AIBar.Application;

internal sealed record UsageProjectionInput(DateTimeOffset OccurredAt, UsageTool Tool, string? Model,
    long InputUncached, long CacheRead, long CacheWrite, long OutputVisible, long OutputReasoning);

public enum UsageProjectionScope { OpenCode = 1, Pi = 2, Combined = 3 }

public sealed record UsageProjectionTokens
{
    public UsageProjectionTokens(long inputUncached, long cacheRead, long cacheWrite, long outputVisible, long outputReasoning)
    {
        if (inputUncached < 0 || cacheRead < 0 || cacheWrite < 0 || outputVisible < 0 || outputReasoning < 0) throw new ArgumentOutOfRangeException(nameof(inputUncached));
        InputUncached = inputUncached; CacheRead = cacheRead; CacheWrite = cacheWrite; OutputVisible = outputVisible; OutputReasoning = outputReasoning;
        checked { Input = inputUncached + cacheRead + cacheWrite; Output = outputVisible + outputReasoning; Total = Input + Output; }
    }
    public long InputUncached { get; }
    public long CacheRead { get; }
    public long CacheWrite { get; }
    public long OutputVisible { get; }
    public long OutputReasoning { get; }
    public long Input { get; }
    public long Output { get; }
    public long Total { get; }
}

public sealed record DailyToolModelUsageFact(DateOnly LocalDay, string TimeZoneId, TimeSpan ObservedOffset,
    string LocalDayPolicyVersion, UsageProjectionScope Scope, string Model, UsageProjectionTokens Tokens);

public sealed class UsageEventProjector
{
    private readonly SqliteUsageEventLedger _ledger;
    private readonly TimeZoneLocalDayPolicy _localDayPolicy;
    private readonly Action? _afterFactMaterialized;
    public UsageEventProjector(SqliteUsageEventLedger ledger, TimeZoneLocalDayPolicy localDayPolicy) : this(ledger, localDayPolicy, null) { }
    internal UsageEventProjector(SqliteUsageEventLedger ledger, TimeZoneLocalDayPolicy localDayPolicy, Action? afterFactMaterialized)
    {
        ArgumentNullException.ThrowIfNull(ledger); ArgumentNullException.ThrowIfNull(localDayPolicy);
        if (string.IsNullOrWhiteSpace(localDayPolicy.PolicyVersion) || localDayPolicy.PolicyVersion.Any(char.IsControl)) throw new ArgumentException("A safe local-day policy version is required.", nameof(localDayPolicy));
        _ledger = ledger; _localDayPolicy = localDayPolicy; _afterFactMaterialized = afterFactMaterialized;
    }

    public async ValueTask<IReadOnlyList<DailyToolModelUsageFact>> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var totals = new SortedDictionary<Key, Accumulator>();
        foreach (var input in await _ledger.ReadProjectionInputsAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var day = _localDayPolicy.Assign(input.OccurredAt);
            var model = string.IsNullOrWhiteSpace(input.Model) ? AnalyticsPolicy.UnknownModel : input.Model;
            Add(new(day.LocalDay, day.TimeZoneId, day.ObservedOffset, Scope(input.Tool), model), input);
            Add(new(day.LocalDay, day.TimeZoneId, day.ObservedOffset, UsageProjectionScope.Combined, model), input);
        }
        var facts = new List<DailyToolModelUsageFact>(totals.Count);
        foreach (var item in totals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            facts.Add(new(item.Key.Day, item.Key.Zone, item.Key.Offset, _localDayPolicy.PolicyVersion, item.Key.Scope, item.Key.Model, item.Value.Tokens));
            _afterFactMaterialized?.Invoke();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return facts;

        void Add(Key key, UsageProjectionInput input)
        {
            if (!totals.TryGetValue(key, out var total)) totals.Add(key, total = new());
            total.Add(input);
        }
    }

    private static UsageProjectionScope Scope(UsageTool tool) => tool switch
    {
        UsageTool.OpenCode => UsageProjectionScope.OpenCode,
        UsageTool.Pi => UsageProjectionScope.Pi,
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };
    private readonly record struct Key(DateOnly Day, string Zone, TimeSpan Offset, UsageProjectionScope Scope, string Model) : IComparable<Key>
    {
        public int CompareTo(Key other)
        {
            var result = Day.CompareTo(other.Day); if (result != 0) return result;
            result = StringComparer.Ordinal.Compare(Zone, other.Zone); if (result != 0) return result;
            result = Offset.CompareTo(other.Offset); if (result != 0) return result;
            result = Scope.CompareTo(other.Scope); return result != 0 ? result : StringComparer.Ordinal.Compare(Model, other.Model);
        }
    }
    private sealed class Accumulator
    {
        private long _input, _read, _write, _visible, _reasoning;
        public void Add(UsageProjectionInput value)
        {
            checked { _input += value.InputUncached; _read += value.CacheRead; _write += value.CacheWrite; _visible += value.OutputVisible; _reasoning += value.OutputReasoning; }
        }
        public UsageProjectionTokens Tokens => new(_input, _read, _write, _visible, _reasoning);
    }
}
