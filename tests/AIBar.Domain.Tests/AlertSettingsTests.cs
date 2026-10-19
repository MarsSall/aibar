using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

// Synthetic fixture only; each test instance owns and removes this directory.
public sealed class AlertSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-alerts-{Guid.NewGuid():N}");
    [Fact]
    public async Task Missing_malformed_and_unknown_schema_default_to_enabled()
    {
        var settings = new AlertSettings(Path.Combine(_root, "alerts.json"));
        Assert.Equal(new AlertOptions(), await settings.LoadAsync(default));
        Directory.CreateDirectory(_root);
        foreach (var content in new[] { "{", "{\"schema\":2,\"resets\":false}", "{\"schema\":1,\"resets\":\"invalid\"}" })
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "alerts.json"), content);
            Assert.Equal(new AlertOptions(), await settings.LoadAsync(default));
        }
    }
    [Fact]
    public async Task Omitted_settings_default_individually_and_cancellation_does_not_publish_a_choice()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "alerts.json");
        await File.WriteAllTextAsync(path, "{\"schema\":1,\"resets\":false}");
        var settings = new AlertSettings(path);
        Assert.Equal(new AlertOptions(false, true, true, true), await settings.LoadAsync(default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SaveAsync(new(false, false, false, false), cancelled.Token).AsTask());
        Assert.Equal(new AlertOptions(false, true, true, true), await settings.LoadAsync(default));
        Assert.Single(Directory.GetFiles(_root));
    }
    [Fact]
    public async Task Settings_roundtrip_exact_schema_and_leave_no_trial_files()
    {
        var settings = new AlertSettings(Path.Combine(_root, "alerts.json"));
        var options = new AlertOptions(false, false, false, false);
        await settings.SaveAsync(options, default);
        Assert.Equal(options, await settings.LoadAsync(default));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "alerts.json")));
        Assert.Equal(new[] { "edgeGlow", "resets", "runOut", "schema", "thresholds" }, json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.Single(Directory.GetFiles(_root));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
