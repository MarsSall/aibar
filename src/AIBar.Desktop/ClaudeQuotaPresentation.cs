using AIBar.Application;
using AIBar.Domain;

namespace AIBar.Desktop;

/// <summary>Maps opt-in Claude panel state without exposing credential or provider details.</summary>
public sealed class ClaudeQuotaPresentationMapper
{
    public ClaudeQuotaPresentationMapper(IClock clock) { ArgumentNullException.ThrowIfNull(clock); }

    // RED scaffold: deliberately not composed into the existing Codex presentation yet.
    public BetaPresentationState Map(QuotaRefreshState state, bool consentEnabled) =>
        throw new NotImplementedException();
}
