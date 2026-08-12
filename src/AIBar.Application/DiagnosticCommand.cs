using System.Collections.ObjectModel;

namespace AIBar.Application;

public interface IDiagnosticClock { DateTimeOffset UtcNow { get; } }

public sealed class DiagnosticRetentionPolicy
{
    public DiagnosticRetentionPolicy(int maximumCount, int maximumBytes, TimeSpan maximumAge)
    {
        if (maximumCount < 1 || maximumBytes < 1 || maximumAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        MaximumCount = maximumCount; MaximumBytes = maximumBytes; MaximumAge = maximumAge;
    }
    public int MaximumCount { get; }
    public int MaximumBytes { get; }
    public TimeSpan MaximumAge { get; }
}

public readonly record struct DiagnosticSnapshot(string Category, string Serialized, DateTimeOffset RecordedAt);
public enum DiagnosticCommandState { Available, Unavailable }
public sealed record DiagnosticPreview(bool IsAvailable, IReadOnlyList<DiagnosticCategory> Categories, long Token);
public sealed record DiagnosticCommandResult(DiagnosticCommandState State, IReadOnlyList<DiagnosticSnapshot> Snapshots);

public sealed class DiagnosticMemorySinks
{
    private readonly object _gate = new();
    private readonly DiagnosticRetentionPolicy _retention;
    private readonly IDiagnosticClock _clock;
    private readonly List<DiagnosticSnapshot> _application = [];
    private readonly List<DiagnosticSnapshot> _quota = [];

    public DiagnosticMemorySinks(DiagnosticRetentionPolicy retention, IDiagnosticClock clock)
    {
        _retention = retention ?? throw new ArgumentNullException(nameof(retention));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool Record(StructuredDiagnosticEvent diagnostic)
    {
        var serialized = StructuredDiagnosticSerializer.Serialize(diagnostic);
        if (serialized == StructuredDiagnosticSerializer.RedactedFallback || serialized.Length > _retention.MaximumBytes) return false;
        var category = diagnostic.Category == DiagnosticCategory.Application ? _application : diagnostic.Category == DiagnosticCategory.Quota ? _quota : null;
        if (category is null) return false;
        lock (_gate)
        {
            Prune(category);
            category.Add(new(DiagnosticCategoryName(diagnostic.Category), serialized, _clock.UtcNow));
            while (category.Count > _retention.MaximumCount || category.Sum(item => item.Serialized.Length) > _retention.MaximumBytes) category.RemoveAt(0);
            return true;
        }
    }

    public IReadOnlyList<DiagnosticSnapshot> Snapshot(DiagnosticCategory category)
    {
        lock (_gate)
        {
            var sink = category == DiagnosticCategory.Application ? _application : category == DiagnosticCategory.Quota ? _quota : null;
            if (sink is null) return Array.Empty<DiagnosticSnapshot>();
            Prune(sink);
            return new ReadOnlyCollection<DiagnosticSnapshot>(sink.ToArray());
        }
    }

    public void Clear()
    {
        lock (_gate) { _application.Clear(); _quota.Clear(); }
    }

    private void Prune(List<DiagnosticSnapshot> sink)
    {
        var oldest = _clock.UtcNow - _retention.MaximumAge;
        sink.RemoveAll(item => item.RecordedAt < oldest);
    }

    private static string DiagnosticCategoryName(DiagnosticCategory category) => category == DiagnosticCategory.Application ? "application" : "quota";
}

public sealed class DiagnosticCommand
{
    private readonly DiagnosticMemorySinks _sinks;
    private long _nextToken;
    private long _activeToken;

    public DiagnosticCommand(DiagnosticMemorySinks sinks) => _sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
    internal DiagnosticPreview BeginTrustedGesture()
    {
        var token = Interlocked.Increment(ref _nextToken);
        Interlocked.Exchange(ref _activeToken, token);
        return new(true, new ReadOnlyCollection<DiagnosticCategory>([DiagnosticCategory.Application, DiagnosticCategory.Quota]), token);
    }

    internal DiagnosticCommandResult Confirm(DiagnosticPreview preview, DiagnosticCategory category)
    {
        if (preview is null || !preview.IsAvailable || preview.Token == 0 || !Enum.IsDefined(category) || Interlocked.CompareExchange(ref _activeToken, 0, preview.Token) != preview.Token)
            return Unavailable();
        var snapshots = _sinks.Snapshot(category);
        return snapshots.Count == 0 ? Unavailable() : new(DiagnosticCommandState.Available, snapshots);
    }

    internal void Cancel(DiagnosticPreview preview)
    {
        if (preview.IsAvailable) Interlocked.CompareExchange(ref _activeToken, 0, preview.Token);
    }

    private static DiagnosticCommandResult Unavailable() => new(DiagnosticCommandState.Unavailable, Array.Empty<DiagnosticSnapshot>());
}
