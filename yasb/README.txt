AIBar YASB quota widget — private beta

This optional integration uses YASB's stock CustomWidget. AIBar remains the only quota authority; the widget reads only its sanitized snapshot. There is no installer, updater, package-manager delivery, or public release.

Setup
1. Manually extract the unsigned, self-contained Windows x64 private-beta ZIP to one stable location you choose: <AIBarRoot>.
2. Copy the widget definitions in custom-widget.example.yaml into your YASB configuration and configure <AIBarRoot> once. Keep aibar_quota for Codex; add aibar_claude_quota beside it in your bar's widget list for Claude. Reload YASB after editing.
3. Copy or customize custom-widget.example.css in your YASB stylesheet.
4. Start AIBar normally. The widget reads its local sanitized snapshot; left-click invokes AIBar.Desktop.exe --show directly, without an extra cmd /c or show-aibar.cmd layer. The legacy CMD launcher remains available for manual use.
The secondary launch makes one best-effort foreground grant to a unique existing AIBar window belonging to the same executable and Windows session, then sends the existing show signal. Windows can refuse the grant; missing/ambiguous targets or refusal do not suppress showing the popup. No focus retries are performed. After opening from YASB, verify that clicking outside dismisses the popup without first clicking inside; also check the tray path. Source/unit checks alone cannot establish foreground behavior on the interactive desktop.

Claude widget
Enable Claude quota separately in AIBar's tray menu. The second widget uses exactly -Provider Claude (case-sensitive), reads %LOCALAPPDATA%\AIBar\yasb-claude-quota.json, and shows the Claude mark beside its compact text, for example "37%" or "unavailable". The alternate label and tooltip start with "Claude ". Both widgets use the same options, stylesheet and left-click --show callback; both open the panel.
The no-argument reader still selects Codex's yasb-quota.json with unchanged output. Only the exact argument pair -Provider Claude opts into Claude; other nonempty argument lists return the default safe unavailable JSON and exit 1. There is no script parameter block.
Claude uses the same schema-v1 sanitized fields, atomic replacement, ACL verification and reparse rejection as Codex. A new post-enable retrieval unlocks valued publication; cache alone does not. When AIBar starts with consent already granted, both widgets show refreshing, or unavailable with the failure reason, until that retrieval; disabled is reserved for disable, revoke, clear and quit. Revocation and shutdown drain pending writes and leave Claude's export disabled; Clear AIBar Data drains them and deletes yasb-claude-quota.json. No token, credential path or plan text is exported or shown by the reader.

Presentation
Only supplied quota windows appear, in 5h then 7d order. Weekly-only input can display "7d: 35% | current" without an empty 5h slot.
Human warnings use "refresh failed", "authentication failed", and "service unavailable". Duplicate unavailable/disabled warnings are omitted from visible copy, not from authoritative CSS classes.
The tooltip shows state, any distinct warning, supplied-window resets as countdowns (for example "5h resets in 3h 53m", "7d resets in 1d 22h", "under 1m", or "due"), then source age. Invalid or missing input fails closed as "unavailable", with "unavailable | age: --" in the tooltip.
The compact label, progress glyph and usage-ramp color select the window that blocks first: the higher percentage USED between 5h and weekly; equal percentages select the sooner reset (a known reset precedes an unknown reset; equal or both unknown resets keep 5h). With only one window, including Codex weekly-only, they use that window. The compact percentage is exact and invariant, without rounding. The full/alternate label and tooltip keep both supplied windows. Current/refreshing show percentage only; stale retains explicit problem text. Without safe values it shows the state/warning, never a percentage.
Reader JSON is BOM-less UTF-8 with ordered label, compactText, progressGlyph, progressColor, tooltip, className fields. Full label, tooltip and classes retain their compatibility meaning.
The YAML colors only a closed progress glyph; plain percentage/problem text stays outside its markup. The glyph fills in 8 steps and, by default, is green below 50%, yellow from 50%, orange from 75% and red from 90%; it is gray when no value is available. AIBar Settings > Colors can instead select brand colors: Codex #2FBF98 and Claude #E0876A. The reader optionally reads appearance.json beside the sanitized quota files (%LOCALAPPDATA%\AIBar), with schema 1 and exact yasbColors "brand" or "usage". Missing, malformed, oversized (>4096 bytes), unsupported-schema or invalid preferences strictly fall back to the usage ramp. Unavailable remains gray. The setting is read-only here, applies on the next normal widget refresh, and does not add output fields or change the quota export.
Each widget starts with its official mark: <AIBarRoot>\yasb\icons\openai.svg for Codex and claude.svg for Claude (14x14 px, local files, no network). Path data comes from Simple Icons (CC0-1.0); the marks are trademarks of OpenAI and Anthropic and only identify each provider. IosevkaTerm Nerd Font must cover U+F0766, U+F0A9E–U+F0AA5; no font is installed automatically.
The stock YASB Reborn v2.0.6 example uses a static aibar-quota class: base CSS applies, but returned state/warning classes do not dynamically activate backgrounds/underlines. Alternate label exposes full detail; tooltip is enabled separately.
Before deployment, verify the exact YAML in an isolated v2.0.6 profile: Boolean tooltip, Qt AutoText/color/glyphs, alternate label, tooltip text, static class, 60-second polling and left-click. Offscreen Qt alone does not establish stock integration.
If glyph/color rendering is unsuitable, stop deployment; do not replace the numeric/problem text with color alone. Preserve exact reader/YAML/CSS bytes before any separately authorized active change, restore those bytes on failure, reload once, and verify hashes plus accepted 1B behavior.

Service boundary
The quota service is an unsupported private integration. The widget does not refresh quotas, authenticate, access credentials, databases, logs, or analytics, and it does not imply public support or service availability.

Upgrade
Stop AIBar before manually replacing the extracted directory. If AIBar cannot be stopped, stop the upgrade and leave the current directory in place.

Rollback
1. Disable Claude quota while AIBar is running and confirm yasb-claude-quota.json is a disabled schema-v1 document with null sourceRetrievedAt, fiveHour and weekly values; then stop AIBar. Remove aibar_claude_quota from your YASB widget list if configured. Normal shutdown also disables Claude's export.
2. From <AIBarRoot>, run yasb\remove-aibar-quota.ps1 with no arguments (this existing cleanup command targets Codex only).
3. Continue only when the cleanup command exits 0.
4. Remove or replace the YASB assets.
5. Verify that the fixed snapshot is absent or is a schema-v1 disabled document with null sourceRetrievedAt, fiveHour, and weekly values.

If cleanup fails, stop removal. Do not remove or replace the assets until cleanup succeeds.
