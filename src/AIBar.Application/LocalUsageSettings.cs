using System.Text;
using System.Text.Json;
using System.Diagnostics;
using AIBar.Domain;

namespace AIBar.Application;

[DebuggerDisplay("Local usage policy")]
public sealed record LocalUsagePolicy(bool OpenCodeEnabled, bool PiEnabled, string? OpenCodeDataRoot = null)
{
    public static LocalUsagePolicy Disabled { get; } = new(false, false);

    public bool IsEnabled(UsageTool tool) => tool switch
    {
        UsageTool.OpenCode => OpenCodeEnabled,
        UsageTool.Pi => PiEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unsupported usage tool."),
    };
    public override string ToString() => "Local usage policy";
}

public sealed class LocalUsageSettings
{
    private readonly string _path;
    private readonly Func<CancellationToken, ValueTask> _beforeReplace;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public LocalUsageSettings(string path) : this(path, _ => ValueTask.CompletedTask) { }

    internal LocalUsageSettings(string path, Func<CancellationToken, ValueTask> beforeReplace)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(beforeReplace);
        _path = Path.GetFullPath(path);
        _beforeReplace = beforeReplace;
    }

    public async ValueTask<LocalUsagePolicy> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!IsSafePath()) return LocalUsagePolicy.Disabled;
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return LocalUsagePolicy.Disabled;
            var seen = 0;
            var schema = 0;
            var openCode = false;
            var pi = false;
            string? openCodeDataRoot = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "schema" when (seen & 1) == 0 && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.GetRawText() is "1" or "2":
                        seen |= 1;
                        schema = property.Value.GetInt32();
                        break;
                    case "openCodeEnabled" when (seen & 2) == 0 && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        seen |= 2;
                        openCode = property.Value.GetBoolean();
                        break;
                    case "piEnabled" when (seen & 4) == 0 && property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        seen |= 4;
                        pi = property.Value.GetBoolean();
                        break;
                    case "openCodeDataRoot" when (seen & 8) == 0 && property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.String:
                        seen |= 8;
                        openCodeDataRoot = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
                        break;
                    default:
                        return LocalUsagePolicy.Disabled;
                }
            }
            if (schema == 1 && seen == 7) return new(openCode, pi);
            if (schema != 2 || seen != 15) return LocalUsagePolicy.Disabled;
            if (openCodeDataRoot is not null && (!LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(openCodeDataRoot, false, out var normalized)
                || !StringComparer.Ordinal.Equals(openCodeDataRoot, normalized))) return LocalUsagePolicy.Disabled;
            return new(openCode, pi, openCodeDataRoot);
        }
        catch (FileNotFoundException) { return LocalUsagePolicy.Disabled; }
        catch (DirectoryNotFoundException) { return LocalUsagePolicy.Disabled; }
        catch (UnauthorizedAccessException) { return LocalUsagePolicy.Disabled; }
        catch (IOException) { return LocalUsagePolicy.Disabled; }
        catch (JsonException) { return LocalUsagePolicy.Disabled; }
    }

    public async ValueTask SaveAsync(LocalUsagePolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (policy.OpenCodeDataRoot is not null && (!LocalUsageSourceRootResolver.TryNormalizeOpenCodeDataRoot(policy.OpenCodeDataRoot, false, out var normalized)
            || !StringComparer.Ordinal.Equals(policy.OpenCodeDataRoot, normalized)))
            throw new InvalidOperationException(LocalUsageSourceRootResolver.InvalidOpenCodeDataRootMessage);
        await _saveGate.WaitAsync(cancellationToken);
        string? temporary = null;
        try
        {
            var directory = Path.GetDirectoryName(_path) ?? throw new InvalidOperationException("Local usage settings require a non-root file path.");
            EnsureSafePath();
            Directory.CreateDirectory(directory);
            EnsureSafePath();
            temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            var json = JsonSerializer.Serialize(new { schema = 2, openCodeEnabled = policy.OpenCodeEnabled, piEnabled = policy.PiEnabled, openCodeDataRoot = policy.OpenCodeDataRoot });
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            await _beforeReplace(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSafePath();
            if (File.Exists(_path)) File.Replace(temporary, _path, null);
            else File.Move(temporary, _path);
            temporary = null;
        }
        finally
        {
            try { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
            finally { _saveGate.Release(); }
        }
    }

    private bool IsSafePath()
    {
        if (Path.GetDirectoryName(_path) is null) return false;
        try { EnsureSafePath(); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private void EnsureSafePath()
    {
        if (File.Exists(_path) && (File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Local usage settings cannot traverse a reparse point.");
        for (var current = new DirectoryInfo(Path.GetDirectoryName(_path)!); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Local usage settings cannot traverse a reparse point.");
    }
}
