using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

// Synthetic, task-owned settings only. No user data is read.
public sealed class DockSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-dock-{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_root, DockSettings.FileName);

    [Fact]
    public async Task Missing_malformed_incomplete_invalid_and_old_files_default()
    {
        var settings = new DockSettings(SettingsPath);
        Assert.Equal(new DockOptions(), await settings.LoadAsync(default));
        Directory.CreateDirectory(_root);
        foreach (var json in new[] { "{", "null", "{}", "{\"schema\":1}",
            "{\"schema\":0,\"enabled\":true,\"edge\":\"top\",\"offset\":0.2}",
            "{\"schema\":2,\"enabled\":true,\"edge\":\"top\",\"offset\":0.2}",
            "{\"schema\":1,\"enabled\":true,\"edge\":\"Top\",\"offset\":0.2}",
            "{\"schema\":1,\"enabled\":true,\"edge\":\"top\",\"offset\":1.1}",
            "{\"schema\":1,\"enabled\":true,\"edge\":\"top\",\"offset\":-0.1}",
            "{\"schema\":1,\"enabled\":\"true\",\"edge\":\"top\",\"offset\":0.5}" })
        {
            await File.WriteAllTextAsync(SettingsPath, json);
            Assert.Equal(new DockOptions(), await settings.LoadAsync(default));
        }
    }

    [Theory]
    [InlineData("top", 0)]
    [InlineData("bottom", 0.5)]
    [InlineData("left", 1)]
    [InlineData("right", 0.3)]
    public async Task Roundtrip_and_changed_follow_successful_atomic_save(string edge, double offset)
    {
        var settings = new DockSettings(SettingsPath);
        var changes = new List<DockOptions>(); settings.Changed += changes.Add;
        var options = new DockOptions(true, edge, offset, "synthetic-monitor");
        await settings.SaveAsync(options, default);
        Assert.Equal(options, await settings.LoadAsync(default));
        Assert.Equal(options, Assert.Single(changes));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(SettingsPath));
        Assert.Equal(new[] { "schema", "enabled", "edge", "offset", "monitor" }, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.Single(Directory.GetFiles(_root));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SaveAsync(new(), cancelled.Token).AsTask());
        Assert.Single(changes);
        Assert.Equal(options, await settings.LoadAsync(default));
    }

    [Fact]
    public async Task Invalid_values_are_rejected_without_overwrite_or_notification()
    {
        var settings = new DockSettings(SettingsPath);
        var options = new DockOptions(true);
        await settings.SaveAsync(options, default);
        var changed = 0; settings.Changed += _ => changed++;
        foreach (var invalid in new[] { new DockOptions(Edge: "Top"), new DockOptions(Edge: ""),
            new DockOptions(Offset: -0.01), new DockOptions(Offset: 1.01), new DockOptions(Offset: double.NaN),
            new DockOptions(Offset: double.PositiveInfinity), new DockOptions(Monitor: " ") })
            await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveAsync(invalid, default).AsTask());
        Assert.Equal(options, await settings.LoadAsync(default)); Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Clear_gate_rejects_saves_until_resume_and_publishes_actual_disk_state()
    {
        var settings = new DockSettings(SettingsPath);
        var options = new DockOptions(true, "left");
        await settings.SaveAsync(options, default);
        var changes = new List<DockOptions>(); settings.Changed += changes.Add;
        await settings.CancelAndWaitAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => settings.SaveAsync(new(), default).AsTask());
        Assert.Empty(changes);
        // Rollback: file remains. Success: file is removed by the owning clear service.
        settings.ResumeAfterClear(); Assert.Equal(options, Assert.Single(changes));
        await settings.CancelAndWaitAsync(default);
        File.Delete(SettingsPath);
        settings.ResumeAfterClear(); Assert.Equal(new DockOptions(), changes.Last());
        await settings.SaveAsync(new(), default);
        Assert.Equal(new DockOptions(), await settings.LoadAsync(default));
    }

    [Fact]
    public async Task Failed_write_does_not_raise_changed()
    {
        Directory.CreateDirectory(SettingsPath);
        var settings = new DockSettings(SettingsPath); // A directory cannot be replaced by a settings file.
        var changed = 0; settings.Changed += _ => changed++;
        var error = await Record.ExceptionAsync(() => settings.SaveAsync(new(), default).AsTask());
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Equal(0, changed);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
