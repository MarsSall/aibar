namespace AIBar.Application;

public sealed record DockOptions(bool Enabled = false, string Edge = "bottom", double Offset = 0.5, string? Monitor = null);

/// <summary>Owns dock.json independently of appearance; drag saves do not reapply panel appearance.</summary>
public sealed class DockSettings(string path) : IAiBarClearWork
{
    public const string FileName = "dock.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _paused;
    public event Action<DockOptions>? Changed;

    public async ValueTask<DockOptions> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await QuotaAlertJson.ReadAsync<Stored>(path, cancellationToken).ConfigureAwait(false);
        return stored is { Schema: 1, Enabled: not null, Edge: not null, Offset: not null } &&
            IsValid(new(stored.Enabled.Value, stored.Edge, stored.Offset.Value, stored.Monitor))
            ? new(stored.Enabled.Value, stored.Edge, stored.Offset.Value, stored.Monitor) : new();
    }

    public async ValueTask SaveAsync(DockOptions options, CancellationToken cancellationToken)
    {
        if (!IsValid(options)) throw new ArgumentException("Expected a screen edge and a finite offset between zero and one.", nameof(options));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_paused) throw new InvalidOperationException("AIBar data is being cleared.");
            await QuotaAlertJson.WriteAsync(path, new Stored(1, options.Enabled, options.Edge, options.Offset, options.Monitor), cancellationToken).ConfigureAwait(false);
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
        DockOptions options;
        _gate.Wait();
        try { _paused = false; options = LoadAsync(default).AsTask().GetAwaiter().GetResult(); }
        finally { _gate.Release(); }
        Changed?.Invoke(options);
    }

    public static bool IsValid(DockOptions? options) => options is not null &&
        options.Edge is "top" or "bottom" or "left" or "right" &&
        double.IsFinite(options.Offset) && options.Offset is >= 0 and <= 1 &&
        (options.Monitor is null || !string.IsNullOrWhiteSpace(options.Monitor));

    private sealed record Stored(int Schema, bool? Enabled = null, string? Edge = null, double? Offset = null, string? Monitor = null);
}
