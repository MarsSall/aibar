using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class RefreshIntervalSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-refresh-settings-{Guid.NewGuid():N}");
    private string PathName => Path.Combine(_root, "appearance.json");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public async Task Schema_one_roundtrip_and_changed_include_refresh_minutes(int minutes)
    {
        var settings = new AppearanceSettings(PathName);
        AppearanceOptions? changed = null;
        settings.Changed += value => changed = value;
        var options = new AppearanceOptions("usage", "brand", 70, minutes);
        await settings.SaveAsync(options, default);
        Assert.Equal(options, await settings.LoadAsync(default));
        Assert.Equal(options, changed);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(PathName));
        Assert.Equal(1, json.RootElement.GetProperty("schema").GetInt32());
        Assert.Equal(minutes, json.RootElement.GetProperty("refreshMinutes").GetInt32());
    }

    [Fact]
    public async Task Missing_file_and_legacy_field_default_to_five_minutes()
    {
        var settings = new AppearanceSettings(PathName);
        Assert.Equal(5, (await settings.LoadAsync(default)).RefreshMinutes);
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(PathName, "{\"schema\":1,\"barColors\":\"usage\",\"yasbColors\":\"brand\",\"backgroundOpacity\":70}");
        Assert.Equal(new AppearanceOptions("usage", "brand", 70, 5), await settings.LoadAsync(default));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(60)]
    [InlineData(int.MaxValue)]
    public async Task Invalid_save_never_writes_or_notifies_and_load_defaults_only_interval(int invalid)
    {
        var settings = new AppearanceSettings(PathName);
        var valid = new AppearanceOptions("usage", "brand", 70, 15);
        await settings.SaveAsync(valid, default);
        var notifications = 0;
        settings.Changed += _ => notifications++;
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveAsync(valid with { RefreshMinutes = invalid }, default).AsTask());
        Assert.Equal(valid, await settings.LoadAsync(default));
        Assert.Equal(0, notifications);
        await File.WriteAllTextAsync(PathName, $"{{\"schema\":1,\"barColors\":\"usage\",\"yasbColors\":\"brand\",\"backgroundOpacity\":70,\"refreshMinutes\":{invalid}}}");
        Assert.Equal(valid with { RefreshMinutes = 5 }, await settings.LoadAsync(default));
    }

    [Fact]
    public async Task Clear_publishes_default_interval_after_a_nondefault_choice()
    {
        var settings = new AppearanceSettings(PathName);
        await settings.SaveAsync(new(RefreshMinutes: 30), default);
        AppearanceOptions? changed = null;
        settings.Changed += value => changed = value;
        await new ClearAiBarDataService(_root, [settings], _ => ValueTask.CompletedTask).ClearAsync(default);
        Assert.Equal(new AppearanceOptions(RefreshMinutes: 5), changed);
        Assert.Equal(5, (await settings.LoadAsync(default)).RefreshMinutes);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
