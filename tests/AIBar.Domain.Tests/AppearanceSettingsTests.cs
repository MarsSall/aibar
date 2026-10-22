using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

// Synthetic owned fixtures only; no user settings are read.
public sealed class AppearanceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-appearance-{Guid.NewGuid():N}");
    [Fact]
    public async Task Defaults_strict_enums_and_schema_fail_closed()
    {
        var path = Path.Combine(_root, "appearance.json");
        var settings = new AppearanceSettings(path);
        Assert.Equal(new AppearanceOptions(), await settings.LoadAsync(default));
        Directory.CreateDirectory(_root);
        foreach (var json in new[] { "{", "{\"schema\":2,\"barColors\":\"usage\"}", "{\"schema\":1,\"barColors\":\"Brand\"}", "{\"schema\":1,\"yasbColors\":false}" })
        {
            await File.WriteAllTextAsync(path, json);
            Assert.Equal(new AppearanceOptions(), await settings.LoadAsync(default));
        }
    }
    [Fact]
    public async Task Atomic_roundtrip_changed_only_after_success_and_clear_restores_defaults()
    {
        var path = Path.Combine(_root, "appearance.json");
        var settings = new AppearanceSettings(path);
        var changes = new List<AppearanceOptions>();
        settings.Changed += changes.Add;
        var options = new AppearanceOptions("usage", "brand");
        await settings.SaveAsync(options, default);
        Assert.Equal(options, await settings.LoadAsync(default));
        Assert.Equal(options, Assert.Single(changes));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(new[] { "schema", "barColors", "yasbColors", "backgroundOpacity", "refreshMinutes", "providerTint", "chartStyle" }, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => settings.SaveAsync(new(), cancelled.Token).AsTask());
        Assert.Single(changes); Assert.Single(Directory.GetFiles(_root));
        await new ClearAiBarDataService(_root, [settings], _ => ValueTask.CompletedTask).ClearAsync(default);
        Assert.False(File.Exists(path));
        Assert.Equal(new AppearanceOptions(), changes.Last());
        Assert.Equal(new AppearanceOptions(), await settings.LoadAsync(default));
    }
    [Fact]
    public async Task Invalid_save_never_overwrites_persisted_choice()
    {
        var settings = new AppearanceSettings(Path.Combine(_root, "appearance.json"));
        await settings.SaveAsync(new(), default);
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveAsync(new("invalid", "usage"), default).AsTask());
        Assert.Equal(new AppearanceOptions(), await settings.LoadAsync(default));
    }
    [Fact]
    public async Task Opacity_roundtrips_and_notifies_with_schema_one_legacy_default()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appearance.json");
        await File.WriteAllTextAsync(path, "{\"schema\":1,\"barColors\":\"usage\",\"yasbColors\":\"brand\"}");
        var settings = new AppearanceSettings(path);
        Assert.Equal(100, (await settings.LoadAsync(default)).BackgroundOpacity);
        AppearanceOptions? changed = null;
        settings.Changed += value => changed = value;
        foreach (var opacity in new[] { 30, 35, 70, 100 })
        {
            var options = new AppearanceOptions("usage", "brand", opacity);
            await settings.SaveAsync(options, default);
            Assert.Equal(options, await settings.LoadAsync(default));
            Assert.Equal(options, changed);
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(opacity, json.RootElement.GetProperty("backgroundOpacity").GetInt32());
        }
        foreach (var invalid in new[] { 0, 29, 31, 101 })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveAsync(new("brand", "usage", invalid), default).AsTask());
            Assert.Equal(100, (await settings.LoadAsync(default)).BackgroundOpacity);
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    [InlineData(101)]
    public async Task Invalid_numeric_opacity_defaults_without_losing_valid_colors(int opacity)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appearance.json");
        await File.WriteAllTextAsync(path, $"{{\"schema\":1,\"barColors\":\"usage\",\"yasbColors\":\"brand\",\"backgroundOpacity\":{opacity}}}");
        Assert.Equal(new AppearanceOptions("usage", "brand", 100), await new AppearanceSettings(path).LoadAsync(default));
    }
    [Fact]
    public async Task Provider_tint_roundtrips_and_legacy_file_keeps_it_on()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appearance.json");
        await File.WriteAllTextAsync(path, "{\"schema\":1,\"barColors\":\"usage\",\"yasbColors\":\"brand\"}");
        var settings = new AppearanceSettings(path);
        Assert.True((await settings.LoadAsync(default)).ProviderTint);
        AppearanceOptions? changed = null;
        settings.Changed += value => changed = value;
        var off = new AppearanceOptions("usage", "brand", ProviderTint: false);
        await settings.SaveAsync(off, default);
        Assert.Equal(off, await settings.LoadAsync(default));
        Assert.Equal(off, changed);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.False(json.RootElement.GetProperty("providerTint").GetBoolean());
    }
    [Fact]
    public async Task Chart_style_roundtrip_defaults_and_invalid_values_preserve_other_options()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "appearance.json");
        var settings = new AppearanceSettings(path);
        foreach (var suffix in new[] { "", ",\"chartStyle\":\"invalid\"", ",\"chartStyle\":null" })
        {
            await File.WriteAllTextAsync(path, "{\"schema\":1,\"barColors\":\"usage\"" + suffix + "}");
            Assert.Equal(new AppearanceOptions("usage"), await settings.LoadAsync(default));
        }
        foreach (var style in new[] { "line", "columns" })
        {
            var options = new AppearanceOptions("usage", ChartStyle: style);
            await settings.SaveAsync(options, default);
            Assert.Equal(options, await settings.LoadAsync(default));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(style, json.RootElement.GetProperty("chartStyle").GetString());
        }
        foreach (var style in new[] { "", "Line", "bars", "invalid" })
            await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveAsync(new(ChartStyle: style), default).AsTask());
        Assert.Equal("columns", (await settings.LoadAsync(default)).ChartStyle);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
