using Microsoft.Win32;
using System.Runtime.Versioning;
using AIBar.Domain;

namespace AIBar.Application;

public interface IStartupTaskRegistration
{
    ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken);
    ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken);
    ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsPackagedStartupTaskRegistration(string taskId) : IStartupTaskRegistration
{
    private object? _task;
    private static readonly Type? StartupTaskType = Type.GetType("Windows.ApplicationModel.StartupTask, Windows, ContentType=WindowsRuntime");
    private async ValueTask<object?> GetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_task is not null) return _task;
        try
        {
            var operation = StartupTaskType?.GetMethod("GetAsync", [typeof(string)])?.Invoke(null, [taskId]);
            return operation is null ? null : _task = await AwaitResultAsync(operation, cancellationToken);
        }
        catch { return null; }
    }
    public async ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => await GetAsync(cancellationToken) is not null;
    public async ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) => string.Equals((await GetAsync(cancellationToken))?.GetType().GetProperty("State")?.GetValue(_task)?.ToString(), "Enabled", StringComparison.Ordinal);
    public async ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        var task = await GetAsync(cancellationToken) ?? throw new InvalidOperationException("Packaged startup task is unavailable.");
        if (enabled)
        {
            var operation = task.GetType().GetMethod("RequestEnableAsync")?.Invoke(task, null) ?? throw new InvalidOperationException();
            await AwaitResultAsync(operation, cancellationToken);
        }
        else task.GetType().GetMethod("Disable")?.Invoke(task, null);
    }

    internal static async Task<object?> AwaitResultAsync(object operation, CancellationToken cancellationToken)
    {
        var type = operation.GetType();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = type.GetProperty("Status")?.GetValue(operation)?.ToString();
            if (string.Equals(status, "Completed", StringComparison.Ordinal)) return type.GetMethod("GetResults")?.Invoke(operation, null);
            if (string.Equals(status, "Started", StringComparison.Ordinal)) { await Task.Delay(10, cancellationToken); continue; }
            throw new InvalidOperationException($"Packaged startup operation ended as '{status ?? "Unknown"}'.");
        }
    }
}

public interface ICurrentUserRunStore
{
    ValueTask<string?> GetAsync(string keyPath, string name, CancellationToken cancellationToken);
    ValueTask SetAsync(string keyPath, string name, string value, CancellationToken cancellationToken);
    ValueTask DeleteAsync(string keyPath, string name, CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCurrentUserRunStore : ICurrentUserRunStore
{
    public const string KeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

    public ValueTask<string?> GetAsync(string keyPath, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return ValueTask.FromResult(key?.GetValue(name) as string);
    }

    public ValueTask SetAsync(string keyPath, string name, string value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true) ?? throw new UnauthorizedAccessException();
        key.SetValue(name, value, RegistryValueKind.String);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteAsync(string keyPath, string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
        return ValueTask.CompletedTask;
    }
}

[SupportedOSPlatform("windows")]
public sealed class PerUserStartupRegistration : IStartupRegistration
{
    private readonly IStartupTaskRegistration _packagedTask;
    private readonly ICurrentUserRunStore _runStore;
    private readonly string _valueName;
    private readonly string _command;

    public PerUserStartupRegistration(IStartupTaskRegistration packagedTask, ICurrentUserRunStore runStore, string valueName, string executablePath)
    {
        _packagedTask = packagedTask; _runStore = runStore; _valueName = valueName;
        _command = $"\"{executablePath}\" --startup";
    }

    public async ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var fallbackEnabled = string.Equals(await _runStore.GetAsync(WindowsCurrentUserRunStore.KeyPath, _valueName, cancellationToken), _command, StringComparison.Ordinal);
        return fallbackEnabled || await _packagedTask.IsAvailableAsync(cancellationToken) && await _packagedTask.IsEnabledAsync(cancellationToken);
    }

    public async ValueTask<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken)
    {
        if (await _packagedTask.IsAvailableAsync(cancellationToken))
        {
            await _packagedTask.SetEnabledAsync(enabled, cancellationToken);
            await _runStore.DeleteAsync(WindowsCurrentUserRunStore.KeyPath, _valueName, cancellationToken);
        }
        else if (enabled) await _runStore.SetAsync(WindowsCurrentUserRunStore.KeyPath, _valueName, _command, cancellationToken);
        else await _runStore.DeleteAsync(WindowsCurrentUserRunStore.KeyPath, _valueName, cancellationToken);
        return await IsEnabledAsync(cancellationToken);
    }
}

public sealed class UnavailableStartupTaskRegistration : IStartupTaskRegistration
{
    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
    public ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);
    public ValueTask SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

public interface IAiBarDataClearCommand { ValueTask ClearAsync(CancellationToken cancellationToken); }

public sealed record PrivacyDefaults(bool TelemetryEnabled = false, bool RemoteCrashReportingEnabled = false);

public sealed class NativeSettingsCommands(IStartupRegistration startup, IAiBarDataClearCommand clear, PrivateIntegrationPolicy privateIntegration, Func<CancellationToken, ValueTask>? revokePrivate = null, Func<CancellationToken, ValueTask>? grantPrivate = null)
{
    public PrivacyDefaults Privacy { get; } = new();
    public bool PrivateIntegrationEnabled => privateIntegration.IsEnabled;
    public bool LocalAnalyticsEnabled => true;
    public async ValueTask<bool> ToggleStartupAsync(CancellationToken cancellationToken)
    {
        var enabled = await startup.IsEnabledAsync(cancellationToken);
        return await startup.SetEnabledAsync(!enabled, cancellationToken);
    }
    public ValueTask ClearAiBarDataAsync(CancellationToken cancellationToken) => clear.ClearAsync(cancellationToken);
    public ValueTask EnablePrivateIntegrationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return grantPrivate is null ? EnableAsync() : grantPrivate(cancellationToken);
    }
    public ValueTask DisablePrivateIntegrationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return revokePrivate is null ? DisableAsync() : revokePrivate(cancellationToken);
    }
    private ValueTask EnableAsync() { privateIntegration.Enable(); return ValueTask.CompletedTask; }
    private ValueTask DisableAsync() { privateIntegration.Disable(); return ValueTask.CompletedTask; }
}
