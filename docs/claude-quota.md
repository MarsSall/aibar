# Claude subscription quota

Claude quota is a separate, optional integration for Claude Pro/Max subscription usage—not API billing. It is off by default and does not change Codex consent, quota storage or exports.

## Enable or disable

In the AIBar tray menu, choose **Enable Claude quota** and accept the Claude-specific disclosure. Cancelling leaves it off. Choose **Disable Claude quota** at any time to cancel Claude requests, stop polling and clear its cached quota. Codex keeps running independently.

With **Automatic** (the default), AIBar detects a usage file from another machine; if none exists, it reads the existing **Claude Code login on this PC**, read-only: `.credentials.json` under `CLAUDE_CONFIG_DIR`, or the current Windows user's `.claude` directory by default. It queries Anthropic's unofficial subscription-usage endpoint, `https://api.anthropic.com/api/oauth/usage`. This private endpoint is unsupported and may change.

AIBar never refreshes tokens or changes the login. If the local session expires or authentication fails, open Claude Code on this PC and sign in again. When a remote usage file is detected, renew the session on the other machine instead; no local Claude credentials are opened.

## Other machine (optional)

To use a Linux Claude Code login, follow
[the optional remote usage helper instructions](../tools/claude-remote-usage/README.md).
No tray change is needed after installing the helper: **Claude source: Automatic**
detects `%LOCALAPPDATA%\AIBar\claude-remote-usage.json` on initialization and each
refresh, including polls (every five minutes when resolved to This PC, every minute
when resolved to Other machine). Claude consent is still required once.
The quota helper pushes only usage percentages, reset times and availability—not credentials.
A separate log collector also pushes daily model/token aggregates to
`claude-remote-tokens.json` for [estimated cost and tokens](usage-cost.md).
This does not change the quota payload or its 4096-byte limit.
Renew an expired session in Claude Code on Linux; data that stops updating is
marked stale after 15 minutes. An existing stale or invalid file stays remote;
AIBar never silently falls back to the local login. Reparse-point files are not
eligible for automatic detection.

The tray offers **Automatic / This PC / Other machine**. Choose **This PC** to
force the Windows login even while a remote file exists, or **Other machine** to
force the remote source even when its file is missing. Existing explicit choices
are preserved. Return to **Automatic** to detect the source again. When the file
appears or is removed in Automatic, AIBar cancels and drains the previous source's
work, clears its cached quota, and refreshes the newly resolved source.

## Opened panel

While Claude is enabled, a **CLAUDE** section appears below **CODEX**, with a pill naming where its quota is read (**This PC** or **Other machine**). Each provider shows weekly quota first, then the 5-hour window: percentage used, a status-colored bar and a compact reset. Missing windows are omitted. Meter colors, flags and translucency are described in [the panel appearance](panel-details.md#panel-appearance).

Claude has its own loading, unavailable and stale-cache status. If its login is missing or expired, renewal guidance names **this PC** or **the other machine**, following the resolved source even in Automatic. Cached values remain visible after a failed refresh and are marked stale; raw errors, credential paths, tokens and plan details are not shown.

**Refresh** refreshes both enabled integrations; **Copy summary** remains Codex-only. Enabling or disabling Claude updates the open panel without restarting. When Claude is off, its section disappears from layout and UI Automation and the panel shows only the **CODEX** section.

## Cost and tokens

While Claude is enabled, **Pace & details** shows **Today** and **Last 30 days**
rows from Linux log aggregates, independently of the selected quota source.
No prompt, source path, message ID or authentication token is sent. Estimates use
the checked-in official list-price catalog; unpriced models are visibly partial,
never silently charged at zero. Missing, invalid or more-than-two-hour-old token
files show **Unavailable**. See [cost and tokens](usage-cost.md).

## Local data and privacy

While enabled, Claude polls every five minutes in This PC mode and every minute in Other machine mode. Source changes reconfigure both the timer and minimum interval. Polls use time since the last attempt, even while the cached data is current; freshness still marks local data stale after 10 minutes and remote data after 15 minutes. Opening the panel refreshes enabled providers whose last successful retrieval is older than one minute, with at least one minute between attempts. Manual **Refresh** remains unthrottled. Its normalized usage windows live in `%LOCALAPPDATA%\AIBar\claude-quota.db`, separate from Codex's `quota.db`. Consent is a separate decision in `settings.json`. Credential contents, tokens and credential paths are never logged or persisted in quota data.

**Clear AIBar Data** cancels Claude work before removing `claude-quota.db`, its `-wal` and `-shm` sidecars, consent settings and the Claude YASB export (alongside the other AIBar-owned data). Claude stays off until explicitly enabled again. The separate token reader also cancels and drains, clears its display and removes `claude-remote-tokens.json`; Linux source logs and the existing remote quota payload remain untouched. A running helper may republish the aggregate. The Claude Code login is never deleted or modified.

## YASB: add the second widget

1. In `yasb/custom-widget.example.yaml`, configure `<AIBarRoot>` as your extracted AIBar directory. Keep the existing `aibar_quota` definition and add `aibar_claude_quota` to your YASB configuration.
2. Add `aibar_claude_quota` beside `aibar_quota` in your bar's widget list, then reload YASB. Both use the same stylesheet and left-click `AIBar.Desktop.exe --show` callback to open the panel.
3. Enable Claude quota in AIBar. The second widget shows the Claude mark followed by, for example, **37%**; the weekly window is preferred, with 5-hour usage used when weekly is absent. Alternate text and the tooltip name Claude in words. Invalid or missing data displays the mark and **unavailable**.

The no-argument reader and Codex export remain unchanged. Claude opts in with the exact, case-sensitive arguments `-Provider Claude` and reads `%LOCALAPPDATA%\AIBar\yasb-claude-quota.json`, not Codex's `yasb-quota.json`. Other nonempty argument lists return the existing safe unavailable output and exit code 1.

Claude uses the same schema-v1 property order and atomic, ACL-verified, reparse-rejecting writer as Codex. Neither export contains tokens, credential paths or plan text. Cached values alone never unlock publication after enable: a new retrieval is required (or a failed attempt without cache produces a sanitized unavailable state). Revoking consent or quitting drains pending Claude writes and leaves a disabled document with null usage/source fields. **Clear AIBar Data** drains them and then deletes `yasb-claude-quota.json`, like Codex's `yasb-quota.json`. Shutdown retains consent and the internal normalized cache; it does not retain valued widget output.
