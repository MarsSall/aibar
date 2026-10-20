namespace AIBar.Application;

public sealed record AppearanceOptions(string BarColors = "brand", string YasbColors = "usage");

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
            ? new(stored.BarColors ?? "brand", stored.YasbColors ?? "usage") : new();
    }
    public async ValueTask SaveAsync(AppearanceOptions options, CancellationToken cancellationToken)
    {
        if (!Valid(options.BarColors) || !Valid(options.YasbColors)) throw new ArgumentException("Unknown appearance color mode.", nameof(options));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_paused) throw new InvalidOperationException("AIBar data is being cleared.");
            await QuotaAlertJson.WriteAsync(path, new Stored(1, options.BarColors, options.YasbColors), cancellationToken).ConfigureAwait(false);
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
    private static bool Valid(string mode) => mode is "brand" or "usage";
    private sealed record Stored(int Schema, string? BarColors = null, string? YasbColors = null);
}
