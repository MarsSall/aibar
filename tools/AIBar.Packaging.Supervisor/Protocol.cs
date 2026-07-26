using System.Text;
using System.Text.Json;

namespace AIBar.Packaging.Supervisor;

public enum SupervisorOperation { PackageRecovery }
public enum SupervisorStatus
{
    Success, InvalidRequest, RootCreateFailed, RootIdentityChanged, ReparseDetected, JobCreateFailed,
    JobConfigFailed, ProcessStartFailed, JobAssignFailed, ProcessResumeFailed, Timeout, Cancelled,
    ProcessFailed, OutputDrainFailed, QuiescenceUnproved, CleanupRefused, CleanupPartial, InternalUnknown
}

public sealed record WorkerArguments(bool DisableBuildServers);
public sealed record SupervisorRequest(int ProtocolVersion, SupervisorOperation Operation, int TimeoutMilliseconds, WorkerArguments Arguments);
public sealed record SupervisorResponse(int ProtocolVersion, SupervisorStatus Status, int? ChildExitCode, long DurationMilliseconds, long StdoutBytes, long StderrBytes, bool StdoutTruncated, bool StderrTruncated);

public static class SupervisorProtocol
{
    public const int ProtocolVersion = 1;
    public const int MaximumRequestBytes = 4096;
    private const int MinimumTimeoutMilliseconds = 100;
    private const int MaximumTimeoutMilliseconds = 3_600_000;

    public static SupervisorResponse Handle(ReadOnlySpan<byte> request)
    {
        if (request.Length > MaximumRequestBytes || !TryParse(request, out _)) return InvalidRequest();
        return new(ProtocolVersion, SupervisorStatus.InternalUnknown, null, 0, 0, 0, false, false);
    }

    public static async Task<SupervisorResponse> HandleAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var request = new byte[MaximumRequestBytes + 1];
        var length = 0;
        while (length < request.Length)
        {
            var read = await input.ReadAsync(request.AsMemory(length, Math.Min(1024, request.Length - length)), cancellationToken);
            if (read == 0) break;
            length += read;
        }
        return Handle(request.AsSpan(0, length));
    }

    public static string Serialize(SupervisorResponse response)
    {
        var exitCode = response.ChildExitCode is int value ? $",\"childExitCode\":{value}" : string.Empty;
        return $"{{\"protocolVersion\":{ProtocolVersion},\"status\":\"{StatusCode(response.Status)}\"{exitCode},\"durationMilliseconds\":{Math.Max(0, response.DurationMilliseconds)},\"stdoutBytes\":{Math.Max(0, response.StdoutBytes)},\"stderrBytes\":{Math.Max(0, response.StderrBytes)},\"stdoutTruncated\":{response.StdoutTruncated.ToString().ToLowerInvariant()},\"stderrTruncated\":{response.StderrTruncated.ToString().ToLowerInvariant()}}}";
    }

    private static SupervisorResponse InvalidRequest() => new(ProtocolVersion, SupervisorStatus.InvalidRequest, null, 0, 0, 0, false, false);

    private static bool TryParse(ReadOnlySpan<byte> request, out SupervisorRequest? parsed)
    {
        parsed = null;
        try
        {
            using var document = JsonDocument.Parse(request.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnly(root, "protocolVersion", "operation", "timeoutMilliseconds", "arguments") ||
                !root.TryGetProperty("protocolVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var protocolVersion) || protocolVersion != ProtocolVersion ||
                !root.TryGetProperty("operation", out var operation) || operation.ValueKind != JsonValueKind.String || operation.GetString() != "package-recovery" ||
                !root.TryGetProperty("timeoutMilliseconds", out var timeout) || timeout.ValueKind != JsonValueKind.Number || !timeout.TryGetInt32(out var milliseconds) || milliseconds is < MinimumTimeoutMilliseconds or > MaximumTimeoutMilliseconds ||
                !root.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object || !HasOnly(arguments, "disableBuildServers") ||
                !arguments.TryGetProperty("disableBuildServers", out var disableBuildServers) || disableBuildServers.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return false;
            parsed = new(ProtocolVersion, SupervisorOperation.PackageRecovery, milliseconds, new(disableBuildServers.GetBoolean()));
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool HasOnly(JsonElement objectElement, params string[] names)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in objectElement.EnumerateObject()) if (!names.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) return false;
        return found.Count == names.Length;
    }

    private static string StatusCode(SupervisorStatus status) => status switch
    {
        SupervisorStatus.Success => "success", SupervisorStatus.InvalidRequest => "invalid-request", SupervisorStatus.RootCreateFailed => "root-create-failed",
        SupervisorStatus.RootIdentityChanged => "root-identity-changed", SupervisorStatus.ReparseDetected => "reparse-detected", SupervisorStatus.JobCreateFailed => "job-create-failed",
        SupervisorStatus.JobConfigFailed => "job-config-failed", SupervisorStatus.ProcessStartFailed => "process-start-failed", SupervisorStatus.JobAssignFailed => "job-assign-failed",
        SupervisorStatus.ProcessResumeFailed => "process-resume-failed", SupervisorStatus.Timeout => "timeout", SupervisorStatus.Cancelled => "cancelled",
        SupervisorStatus.ProcessFailed => "process-failed", SupervisorStatus.OutputDrainFailed => "output-drain-failed", SupervisorStatus.QuiescenceUnproved => "quiescence-unproved",
        SupervisorStatus.CleanupRefused => "cleanup-refused", SupervisorStatus.CleanupPartial => "cleanup-partial", _ => "internal-unknown"
    };
}
