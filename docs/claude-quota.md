# Claude subscription quota

Claude quota is a separate, optional integration for Claude Pro/Max subscription usage—not API billing. It is off by default and does not change Codex consent, quota storage or exports.

## Enable or disable

In the AIBar tray menu, choose **Enable Claude quota** and accept the Claude-specific disclosure. Cancelling leaves it off. Choose **Disable Claude quota** at any time to cancel Claude requests, stop polling and clear its cached quota. Codex keeps running independently.

AIBar reads the existing **Claude Code login on this PC**, read-only: `.credentials.json` under `CLAUDE_CONFIG_DIR`, or the current Windows user's `.claude` directory by default. It queries Anthropic's unofficial subscription-usage endpoint, `https://api.anthropic.com/api/oauth/usage`. This private endpoint is unsupported and may change.

AIBar never refreshes tokens or changes the login. If the session expires or authentication fails, open Claude Code on this PC and sign in again. AIBar does not use a login from another computer.

## Opened panel

While Claude is enabled, the panel shows **Codex** and **Claude** headings, with Claude below the Codex meters. Each provider shows weekly quota first, then the 5-hour window: percentage used, a slim progress bar and reset countdown. Missing windows are omitted. The meters use the same light, dark and high-contrast semantic styles.

Claude has its own loading, unavailable and stale-cache status. If its login is missing or expired, the panel says **Open Claude Code on this PC to renew the session**. Cached values remain visible after a failed refresh and are marked stale; raw errors, credential paths, tokens and plan details are not shown.

**Refresh** refreshes both enabled integrations; **Copy summary** remains Codex-only. Enabling or disabling Claude updates the open panel without restarting. When Claude is off, its section and both provider headings disappear from layout and UI Automation, leaving the existing Codex panel unchanged.

## Local data and privacy

While enabled, Claude uses the same five-minute polling cadence, cache-first freshness and throttling policy as Codex. Its normalized usage windows live in `%LOCALAPPDATA%\AIBar\claude-quota.db`, separate from Codex's `quota.db`. Consent is a separate decision in `settings.json`. Credential contents, tokens and credential paths are never logged or persisted in quota data.

**Clear AIBar Data** cancels Claude work before removing `claude-quota.db`, its `-wal` and `-shm` sidecars, and consent settings (alongside the other AIBar-owned data). Claude stays off until explicitly enabled again. The Claude Code login is never deleted or modified.

Claude YASB presentation is separate work; the existing Codex widget and export remain unchanged.
