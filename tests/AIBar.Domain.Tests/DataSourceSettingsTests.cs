using System.Text.Json;
using AIBar.Application;

namespace AIBar.Domain.Tests;

public sealed class DataSourceSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-source-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_root, "settings.json");

    [Fact]
    public async Task Missing_settings_default_both_providers_to_automatic()
    {
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(DataSource.Automatic, await settings.LoadDataSourceAsync(default));
        Assert.Equal(ClaudeQuotaSource.Automatic, await settings.LoadClaudeSourceAsync(default));
    }

    [Theory]
    [InlineData("automatic", DataSource.Automatic, ClaudeQuotaSource.Automatic)]
    [InlineData("this-pc", DataSource.ThisPc, ClaudeQuotaSource.ThisPc)]
    [InlineData("other-machine", DataSource.OtherMachine, ClaudeQuotaSource.OtherMachine)]
    public async Task General_source_migrates_to_both_providers(string wire, DataSource codex, ClaudeQuotaSource claude)
    {
        await SeedAsync($$"""{"schema":1,"dataSource":"{{wire}}"}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(codex, await settings.LoadDataSourceAsync(default));
        Assert.Equal(claude, await settings.LoadClaudeSourceAsync(default));
        await settings.SaveAsync(true, default);
        Assert.Equal(codex, await settings.LoadDataSourceAsync(default));
        Assert.Equal(claude, await settings.LoadClaudeSourceAsync(default));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath));
        Assert.False(json.RootElement.TryGetProperty("dataSource", out _));
        Assert.Equal(wire, json.RootElement.GetProperty("codexSource").GetString());
        Assert.Equal(wire, json.RootElement.GetProperty("claudeSource").GetString());
    }

    [Fact]
    public async Task Claude_only_legacy_source_does_not_choose_the_codex_source()
    {
        await SeedAsync("""{"schema":1,"claudeSource":"other-machine"}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(DataSource.Automatic, await settings.LoadDataSourceAsync(default));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, await settings.LoadClaudeSourceAsync(default));
    }

    [Fact]
    public async Task Provider_values_override_general_migration_independently()
    {
        await SeedAsync("""{"schema":1,"codexSource":"this-pc","claudeSource":"other-machine","dataSource":"automatic"}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(DataSource.ThisPc, await settings.LoadDataSourceAsync(default));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, await settings.LoadClaudeSourceAsync(default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => settings.SaveDataSourceAsync((DataSource)99, default).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => settings.SaveClaudeSourceAsync((ClaudeQuotaSource)99, default).AsTask());
    }

    [Theory]
    [InlineData("\"invalid\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task Invalid_provider_values_are_automatic_not_general_fallback(string value)
    {
        await SeedAsync($$"""{"schema":1,"dataSource":"other-machine","codexSource":{{value}},"claudeSource":{{value}}}""");
        var settings = new ConsentSettings(FilePath);
        Assert.Equal(DataSource.Automatic, await settings.LoadDataSourceAsync(default));
        Assert.Equal(ClaudeQuotaSource.Automatic, await settings.LoadClaudeSourceAsync(default));
        await settings.SaveClaudeAsync(true, default);
        Assert.Equal(DataSource.Automatic, await settings.LoadDataSourceAsync(default));
        Assert.Equal(ClaudeQuotaSource.Automatic, await settings.LoadClaudeSourceAsync(default));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Both_save_orders_roundtrip_sources_and_preserve_fields_despite_malformed_consent(bool codexFirst)
    {
        await SeedAsync("""{"schema":1,"privateCodexConsent":false,"privateClaudeConsent":"bad","dataSource":"automatic","claudeCacheSource":"other-machine"}""");
        var settings = new ConsentSettings(FilePath);
        if (codexFirst) await settings.SaveDataSourceAsync(DataSource.ThisPc, default);
        await settings.SaveClaudeSourceAsync(ClaudeQuotaSource.OtherMachine, default);
        await settings.SaveAsync(true, default);
        if (!codexFirst) await settings.SaveDataSourceAsync(DataSource.ThisPc, default);
        await settings.SaveClaudeAsync(true, default);
        var reloaded = new ConsentSettings(FilePath);
        Assert.True(await reloaded.LoadAsync(default));
        Assert.True(await reloaded.LoadClaudeAsync(default));
        Assert.Equal(DataSource.ThisPc, await reloaded.LoadDataSourceAsync(default));
        Assert.Equal(ClaudeQuotaSource.OtherMachine, await reloaded.LoadClaudeSourceAsync(default));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath));
        var root = json.RootElement;
        Assert.Equal("this-pc", root.GetProperty("codexSource").GetString());
        Assert.Equal("other-machine", root.GetProperty("claudeSource").GetString());
        Assert.Equal("other-machine", root.GetProperty("claudeCacheSource").GetString());
        Assert.False(root.TryGetProperty("dataSource", out _));
    }

    private async Task SeedAsync(string json)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(FilePath, json);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
