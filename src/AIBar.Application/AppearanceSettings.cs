namespace AIBar.Application;

/// <summary>ProviderTint colors the panel border, glow, header divider and selected tab with the selected provider.</summary>
public sealed record AppearanceOptions(string BarColors = "brand", string YasbColors = "usage", int BackgroundOpacity = 100, int RefreshMinutes = 5, bool ProviderTint = true, string ChartStyle = "line");

/// <summary>Owns appearance.json. Clear joins the same gate as saves; callbacks never await UI dispatch.</summary>
public sealed class AppearanceSettings(string path) : IAiBarClearWork
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _paused;
    public event Action<AppearanceOptions>? Changed;
    public async ValueTask<AppearanceOptions> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await QuotaAlertJson.ReadAsync<Stored>(path, cancellationToken).ConfigureAwait(false);
        return stored is { Schema: 1 } && Valid(stored.BarColors ?? "brand") && Valid(stored.YasbColors ?? "usage")
            ? new(stored.BarColors ?? "brand", stored.YasbColors ?? "usage", ValidOpacity(stored.BackgroundOpacity) ? stored.BackgroundOpacity : 100,
                ValidRefreshMinutes(stored.RefreshMinutes) ? stored.RefreshMinutes : 5, stored.ProviderTint ?? true, ValidChartStyle(stored.ChartStyle) ? stored.ChartStyle! : "line") : new();
    }
    public async ValueTask SaveAsync(AppearanceOptions options, CancellationToken cancellationToken)
    {
        if (!Valid(options.BarColors) || !Valid(options.YasbColors) || !ValidOpacity(options.BackgroundOpacity) || !ValidRefreshMinutes(options.RefreshMinutes) || !ValidChartStyle(options.ChartStyle))
            throw new ArgumentException("Unknown color mode or chart style, opacity outside 30–100 in steps of 5, or refresh interval outside 1/2/5/15/30 minutes.", nameof(options));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_paused) throw new InvalidOperationException("AIBar data is being cleared.");
            await QuotaAlertJson.WriteAsync(path, new Stored(1, options.BarColors, options.YasbColors, options.BackgroundOpacity, options.RefreshMinutes, options.ProviderTint, options.ChartStyle), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(options);
    }
    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _paused = true; } finally { _gate.Release(); }
    }
    public void ResumeAfterClear()
    {
        AppearanceOptions options;
        _gate.Wait();
        try
        {
            _paused = false;
            // Read the actual file after success OR rollback, without awaiting a dispatcher.
            options = LoadAsync(default).AsTask().GetAwaiter().GetResult();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(options);
    }
    public static bool ValidChartStyle(string? style) => style is "line" or "columns";
    private static bool Valid(string mode) => mode is "brand" or "usage";
    private static bool ValidOpacity(int value) => value is >= 30 and <= 100 && value % 5 == 0;
    public static bool ValidRefreshMinutes(int value) => value is 1 or 2 or 5 or 15 or 30;
    private sealed record Stored(int Schema, string? BarColors = null, string? YasbColors = null, int BackgroundOpacity = 100, int RefreshMinutes = 5, bool? ProviderTint = null, string? ChartStyle = null);
}
