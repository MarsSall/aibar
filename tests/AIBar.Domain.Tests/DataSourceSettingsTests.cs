using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class DataSourceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_root, "settings.json");
    [Fact]
    public async Task Missing_settings_default_to_automatic() =>
        Assert.Equal(DataSource.Automatic, await new ConsentSettings(FilePath).LoadDataSourceAsync(default));

    [Theory]
    [InlineData("automatic", DataSource.Automatic)]
    [InlineData("this-pc", DataSource.ThisPc)]
    [InlineData("other-machine", DataSource.OtherMachine)]
    public async Task Legacy_source_migrates_and_new_source_roundtrips(string wire, DataSource expected)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(FilePath, $$"""{"schema":1,"privateCodexConsent":true,"claudeSource":"{{wire}}"}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(expected, await settings.LoadDataSourceAsync(default));
        await settings.SaveDataSourceAsync(expected, default);
        await settings.SaveClaudeAsync(true, default);
        Assert.Equal(expected, await new ConsentSettings(FilePath).LoadDataSourceAsync(default));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath));
        Assert.Equal(wire, json.RootElement.GetProperty("dataSource").GetString());
        Assert.True(await settings.LoadAsync(default));
    }

    [Theory]
    [InlineData("\"invalid\"")]
    [InlineData("42")]
    [InlineData("null")]
    public async Task Invalid_new_value_is_automatic_not_legacy(string value)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(FilePath, $$"""{"schema":1,"claudeSource":"other-machine","dataSource":{{value}}}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(DataSource.Automatic, await settings.LoadDataSourceAsync(default));
        await settings.SaveClaudeAsync(true, default);
        Assert.Equal(DataSource.Automatic, await settings.LoadDataSourceAsync(default));
    }

    [Fact]
    public async Task General_value_takes_precedence_over_legacy_and_rejects_invalid_enum()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(FilePath, """{"schema":1,"claudeSource":"other-machine","dataSource":"this-pc"}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(DataSource.ThisPc, await settings.LoadDataSourceAsync(default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => settings.SaveDataSourceAsync((DataSource)99, default).AsTask());
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
