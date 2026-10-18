# Claude subscription quota

Claude quota is a separate, optional integration for Claude Pro/Max subscription usage—not API billing. It is off by default and does not change Codex consent, quota storage or exports.

## Enable or disable

In the AIBar tray menu, choose **Enable Claude quota** and accept the Claude-specific disclosure. Cancelling leaves it off. Choose **Disable Claude quota** at any time to cancel Claude requests, stop polling and clear its cached quota. Codex keeps running independently.

AIBar reads the existing **Claude Code login on this PC**, read-only: `.credentials.json` under `CLAUDE_CONFIG_DIR`, or the current Windows user's `.claude` directory by default. It queries Anthropic's unofficial subscription-usage endpoint, `https://api.anthropic.com/api/oauth/usage`. This private endpoint is unsupported and may change.

AIBar never refreshes tokens or changes the login. If the session expires or authentication fails, open Claude Code on this PC and sign in again. In this default mode AIBar does not use a login from another computer; see **Other machine** below for the optional usage-file source.

## Other machine (optional)

**This PC** remains the default. To use a Linux Claude Code login instead, follow
[the optional remote usage helper instructions](../tools/claude-remote-usage/README.md)
and select **Claude source: Other machine** in the Windows AIBar tray menu.
Claude consent is still required. The helper pushes only usage percentages, reset
times and availability—not credentials. Renew an expired session in Claude Code
on Linux; data that stops updating is marked stale after 15 minutes.

## Opened panel

While Claude is enabled, the panel shows **Codex** and **Claude** headings, with Claude below the Codex meters. Each provider shows weekly quota first, then the 5-hour window: percentage used, a slim progress bar and reset countdown. Missing windows are omitted. The meters use the same light, dark and high-contrast semantic styles.

Claude has its own loading, unavailable and stale-cache status. If its login is missing or expired, the panel says **Open Claude Code on this PC to renew the session**. Cached values remain visible after a failed refresh and are marked stale; raw errors, credential paths, tokens and plan details are not shown.

**Refresh** refreshes both enabled integrations; **Copy summary** remains Codex-only. Enabling or disabling Claude updates the open panel without restarting. When Claude is off, its section and both provider headings disappear from layout and UI Automation, leaving the existing Codex panel unchanged.

## Local data and privacy

While enabled, Claude uses the same five-minute polling cadence, cache-first freshness and throttling policy as Codex. Its normalized usage windows live in `%LOCALAPPDATA%\AIBar\claude-quota.db`, separate from Codex's `quota.db`. Consent is a separate decision in `settings.json`. Credential contents, tokens and credential paths are never logged or persisted in quota data.

**Clear AIBar Data** cancels Claude work before removing `claude-quota.db`, its `-wal` and `-shm` sidecars, consent settings and the Claude YASB export (alongside the other AIBar-owned data). Claude stays off until explicitly enabled again. The Claude Code login is never deleted or modified.

## YASB: add the second widget

1. In `yasb/custom-widget.example.yaml`, configure `<AIBarRoot>` as your extracted AIBar directory. Keep the existing `aibar_quota` definition and add `aibar_claude_quota` to your YASB configuration.
2. Add `aibar_claude_quota` beside `aibar_quota` in your bar's widget list, then reload YASB. Both use the same stylesheet and left-click `AIBar.Desktop.exe --show` callback to open the panel.
3. Enable Claude quota in AIBar. The second widget displays, for example, **Claude 37%**; the weekly window is preferred, with 5-hour usage used when weekly is absent. Alternate text and the tooltip identify Claude too. Invalid or missing data displays **Claude unavailable**.

The no-argument reader and Codex export remain unchanged. Claude opts in with the exact, case-sensitive arguments `-Provider Claude` and reads `%LOCALAPPDATA%\AIBar\yasb-claude-quota.json`, not Codex's `yasb-quota.json`. Other nonempty argument lists return the existing safe unavailable output and exit code 1.

Claude uses the same schema-v1 property order and atomic, ACL-verified, reparse-rejecting writer as Codex. Neither export contains tokens, credential paths or plan text. Cached values alone never unlock publication after enable: a new retrieval is required (or a failed attempt without cache produces a sanitized unavailable state). Revoking consent or quitting drains pending Claude writes and leaves a disabled document with null usage/source fields. **Clear AIBar Data** drains them and then deletes `yasb-claude-quota.json`, like Codex's `yasb-quota.json`. Shutdown retains consent and the internal normalized cache; it does not retain valued widget output.
