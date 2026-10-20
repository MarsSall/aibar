# Claude subscription quota

Claude quota is a separate, optional integration for Claude Pro/Max subscription usage—not API billing. It is off by default and does not change Codex consent, quota storage or exports.

## Enable or disable

In the AIBar tray menu, choose **Enable Claude quota** and accept the Claude-specific disclosure. Cancelling leaves it off. Choose **Disable Claude quota** at any time to cancel Claude requests, stop polling and clear its cached quota. Codex keeps running independently.

AIBar reads the existing **Claude Code login on this PC**, read-only: `.credentials.json` under `CLAUDE_CONFIG_DIR`, or the current Windows user's `.claude` directory by default. It queries Anthropic's unofficial subscription-usage endpoint, `https://api.anthropic.com/api/oauth/usage`. This private endpoint is unsupported and may change.

AIBar never refreshes tokens or changes the login. If the session expires or authentication fails, open Claude Code on this PC and sign in again. AIBar does not use a login from another computer.

## Local data and privacy

While enabled, Claude uses the same five-minute polling cadence, cache-first freshness and throttling policy as Codex. Its normalized usage windows live in `%LOCALAPPDATA%\AIBar\claude-quota.db`, separate from Codex's `quota.db`. Consent is a separate decision in `settings.json`. Credential contents, tokens and credential paths are never logged or persisted in quota data.

**Clear AIBar Data** cancels Claude work before removing `claude-quota.db`, its `-wal` and `-shm` sidecars, and consent settings (alongside the other AIBar-owned data). Claude stays off until explicitly enabled again. The Claude Code login is never deleted or modified.

This slice adds consent, storage and background refresh only; Claude panel and YASB presentation are separate work.
