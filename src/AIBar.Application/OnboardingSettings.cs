namespace AIBar.Application;

/// <summary>One-time startup offers already resolved: the glyph font offer was declined for good, and the floating widget offer was answered.</summary>
public sealed record OnboardingState(bool FontOfferDeclined = false, bool DockOfferAnswered = false);

/// <summary>Owns onboarding.json. Missing, malformed or unknown-schema files mean no offer was resolved yet.</summary>
public sealed class OnboardingSettings(string path) : IAiBarClearWork
{
    public const string FileName = "onboarding.json";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _paused;

    public async ValueTask<OnboardingState> LoadAsync(CancellationToken cancellationToken)
    {
        var stored = await QuotaAlertJson.ReadAsync<Stored>(path, cancellationToken).ConfigureAwait(false);
        return stored is { Schema: 1 } ? new(stored.FontOfferDeclined ?? false, stored.DockOfferAnswered ?? false) : new();
    }

    public async ValueTask SaveAsync(OnboardingState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_paused) throw new InvalidOperationException("AIBar data is being cleared.");
            await QuotaAlertJson.WriteAsync(path, new Stored(1, state.FontOfferDeclined, state.DockOfferAnswered), cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async ValueTask CancelAndWaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { _paused = true; } finally { _gate.Release(); }
    }

    public void ResumeAfterClear()
    {
        _gate.Wait();
        try { _paused = false; } finally { _gate.Release(); }
    }

    private sealed record Stored(int Schema, bool? FontOfferDeclined = null, bool? DockOfferAnswered = null);
}
