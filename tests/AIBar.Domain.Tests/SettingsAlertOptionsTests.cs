using AIBar.Application;
using AIBar.Domain;
using AIBar.Desktop;

namespace AIBar.Domain.Tests;

public sealed class SettingsAlertOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aibar-settings-alert-{Guid.NewGuid():N}");
    [Fact]
    public async Task Legacy_schema_one_defaults_new_effects_and_thresholds()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "alerts.json");
        await File.WriteAllTextAsync(path, "{\"schema\":1,\"resets\":false,\"edgeGlow\":false}");
        var options = await new AlertSettings(path).LoadAsync(default);
        Assert.False(options.Resets); Assert.False(options.EdgeGlow);
        Assert.True(options.Notification); Assert.False(options.Confetti);
        Assert.Equal(new[] { 75, 90, 100 }, options.ThresholdValues.ToArray());
    }
    [Fact]
    public async Task New_fields_roundtrip_and_invalid_thresholds_use_defaults_without_losing_toggles()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "alerts.json"); var settings = new AlertSettings(path);
        await settings.SaveAsync(new AlertOptions { ThresholdValues = new[] { 20, 65, 99 }, Confetti = true, Notification = false }, default);
        var saved = await settings.LoadAsync(default);
        Assert.Equal(new[] { 20, 65, 99 }, saved.ThresholdValues.ToArray()); Assert.True(saved.Confetti); Assert.False(saved.Notification);
        foreach (var values in new[] { "[]", "[0,90,100]", "[75,75,100]", "[90,75,100]", "[75,90,101]" })
        {
            await File.WriteAllTextAsync(path, "{\"schema\":1,\"resets\":false,\"thresholdValues\":" + values + "}");
            var options = await settings.LoadAsync(default);
            Assert.False(options.Resets); Assert.Equal(new[] { 75, 90, 100 }, options.ThresholdValues.ToArray());
        }
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SaveAsync(new() { ThresholdValues = new[] { 90, 75 } }, default).AsTask());
    }
    [Fact]
    public void Policy_uses_configured_crossings_and_keeps_deduplication_and_stale_guards()
    {
        var now = DateTimeOffset.UtcNow;
        var sample = new QuotaAlertSample("Codex", QuotaPaceWindow.FiveHour, 91, now.AddHours(2), now);
        var alerts = QuotaAlertPolicy.Evaluate(sample, null, [], FreshnessState.Current, true, now, new[] { 25, 80 });
        Assert.Equal(new int?[] { 25, 80 }, alerts.Where(a => a.Kind == QuotaAlertKind.ThresholdCrossed).Select(a => a.Threshold).ToArray());
        Assert.Empty(QuotaAlertPolicy.Evaluate(sample, null, alerts.Select(a => a.Key).ToArray(), FreshnessState.Current, true, now, new[] { 25, 80 }));
        Assert.Empty(QuotaAlertPolicy.Evaluate(sample, null, [], FreshnessState.Stale, true, now, new[] { 25, 80 }));
    }
    [Theory]
    [InlineData("0, 90, 100")]
    [InlineData("75, 75, 100")]
    [InlineData("90, 75, 100")]
    [InlineData("75.5, 90, 100")]
    [InlineData("75, 90, 101")]
    [InlineData("")]
    public void Editor_rejects_invalid_thresholds_with_inline_message(string text)
    {
        Assert.False(SettingsThresholdEditor.TryParse(text, out _, out var error)); Assert.False(string.IsNullOrWhiteSpace(error));
    }
    [Fact]
    public void Editor_explains_the_specific_out_of_order_pair()
    {
        Assert.False(SettingsThresholdEditor.TryParse("75, 90, 85", out _, out var error));
        Assert.Equal("Keep the values ascending: 85 comes after 90. Use whole numbers from 1 to 100.", error);
    }
    [Fact]
    public void Editor_accepts_ascending_integer_boundaries()
    {
        Assert.True(SettingsThresholdEditor.TryParse("1, 75, 100", out var values, out var error));
        Assert.Equal(new[] { 1, 75, 100 }, values); Assert.Null(error);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
