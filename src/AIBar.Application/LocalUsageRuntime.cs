using AIBar.Domain;

namespace AIBar.Application;

public enum LocalUsageAdapterKind { OpenCodeSqliteV1 = 1, PiJsonlV3 = 2 }
public enum LocalUsageRegistrationAdmissionPolicy { UnverifiedManual = 1, DiscoveryProof = 2 }
public enum LocalUsageSourceStatus { Disabled = 1, Completed = 2, Partial = 3, Unavailable = 4, RebuildRequired = 5, Failed = 6 }
public enum LocalUsageWarning
{
    Unavailable = 1, UnsupportedSchema = 2, MalformedRecord = 3, Truncated = 4, RebuildRequired = 5,
    OversizedRecord = 6, IncompleteTail = 7, InvalidAdapterResult = 8, CheckpointConflict = 9,
    EventCollision = 10, SourceFailed = 11, PathChanged = 12
}

public sealed class LocalUsageSourceRegistration
{
    public LocalUsageSourceRegistration(UsageTool tool, LocalUsageAdapterKind adapter, string path,
        UsageSourceIdentity identity, int sourceSchemaVersion)
        : this(tool, adapter, path, identity, sourceSchemaVersion, null) { }
    internal LocalUsageSourceRegistration(UsageTool tool, LocalUsageAdapterKind adapter, string path,
        UsageSourceIdentity identity, int sourceSchemaVersion, ILocalUsageSourceAdmission? admission)
    {
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(tool) || !Enum.IsDefined(adapter)) throw new ArgumentOutOfRangeException(nameof(tool));
        var matches = adapter switch
        {
            LocalUsageAdapterKind.OpenCodeSqliteV1 => tool == UsageTool.OpenCode && sourceSchemaVersion == OpenCodeSqliteUsageSourceAdapter.SupportedSourceSchemaVersion,
            LocalUsageAdapterKind.PiJsonlV3 => tool == UsageTool.Pi && sourceSchemaVersion == PiJsonlUsageSourceAdapter.SupportedSourceSchemaVersion,
            _ => false
        };
        if (!matches) throw new ArgumentException("The tool, adapter, and source schema version must match.");
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An explicit source path is required.", nameof(path));
        var fullPath = System.IO.Path.GetFullPath(path);
        if (System.IO.Path.GetDirectoryName(fullPath) is null) throw new ArgumentException("A source path cannot be a filesystem root.", nameof(path));
        Tool = tool; Adapter = adapter; Path = fullPath; Identity = identity; SourceSchemaVersion = sourceSchemaVersion; Admission = admission;
    }
    public UsageTool Tool { get; }
    public LocalUsageAdapterKind Adapter { get; }
    public string Path { get; }
    public UsageSourceIdentity Identity { get; }
    public int SourceSchemaVersion { get; }
    public LocalUsageRegistrationAdmissionPolicy AdmissionPolicy => Admission is null ? LocalUsageRegistrationAdmissionPolicy.UnverifiedManual : LocalUsageRegistrationAdmissionPolicy.DiscoveryProof;
    internal ILocalUsageSourceAdmission? Admission { get; }
}

public sealed record LocalUsageSourceRunResult(UsageTool Tool, LocalUsageSourceStatus Status, int RecordsRead,
    int RecordsIgnored, int RecordsMalformed, int EventsPrepared, int EventsCommitted, IReadOnlyList<LocalUsageWarning> WarningCodes);
public sealed record LocalUsageRunResult(IReadOnlyList<LocalUsageSourceRunResult> Sources);
internal sealed record LocalUsagePreparedBatch(IReadOnlyList<NormalizedUsageEvent> Events, int RecordsRead,
    int RecordsIgnored, int RecordsMalformed, IReadOnlyList<LocalUsageWarning> Warnings, UsageSourceCheckpoint? Checkpoint);
