# Quota alerts

AIBar alerts on **Codex and Claude** subscription quota, independently for the 5-hour and weekly windows. Providers must be enabled through their existing consent controls. Alerts do not read credentials or change either provider's quota/export contract.

## Choose alerts

Open **Settings… → Alerts** to choose events, thresholds, and effects. The existing tray **Alerts** toggles still work; **Configure alerts…** opens the same window. The original four choices default to on, Windows notifications default to on, and confetti defaults to off. See [Settings](settings.md) for keyboard controls and color preferences.

| Choice | Behavior |
| --- | --- |
| Window resets | Notify when a current reading moves the reset timestamp to a later cycle. |
| Crossed thresholds | Notify once at each configured threshold in each cycle (default 75 / 90 / 100%). Values must be ascending integers from 1 to 100. A first current reading already above a threshold can notify too. |
| May run out before reset | Notify once per cycle when `QuotaPacePolicy` estimates exhaustion before the reset. This is an estimate, not a promise. |
| Edge glow at 100% | Brief edge glow on every monitor when the limit is reached, independently of threshold cards and whether the custom list includes 100. |
| Windows notification | Deliver enabled events as custom AIBar notification cards. A tray balloon is used only if the card cannot be shown. Turning this off does not disable visual effects. |
| Confetti on window reset | Provider-colored confetti on every monitor for 2.5 seconds, only for enabled reset alerts. Off by default. Disabled client-area animation suppresses it entirely; high contrast uses system colors. |

A reading that jumps from below 75% to 100% crosses all three thresholds. Each is an independent alert. The stack shows at most three cards, evicting the oldest when necessary, with an 8-DIP gap. Cards sit 12 DIPs from the right and bottom of the pointer monitor's work area (above its taskbar), at 360 DIPs wide or 300 on narrow work areas. Monitor placement converts physical screen coordinates using the destination monitor's DPI.

Each card contains a text-and-icon category (**THRESHOLD / RESET / PACE · NOW**), the official provider mark in a 36-DIP tile, the existing title/body copy, a named **Dismiss** button, and a 2-DIP timer bar. Dark cards have category-colored borders, a hairline and glow; light cards are flat. Click or **Enter** opens the panel (never toggles it closed); Dismiss and Escape only close the card. `ShowActivated=false`, `WS_EX_NOACTIVATE` and `MA_NOACTIVATE` prevent focus theft. UIA `LiveSetting=Polite` announces the title and body. Entry slides 16 DIPs from the right and fades for 250 ms; after entry, the timer runs for six seconds, pausing on hover or keyboard focus. Exit fades and moves 8 DIPs right over 167 ms. With Windows animation effects off, there are no slides and fades take 83 ms.

The custom card is an app-owned window, not an Action Center notification. Windows notification suppression settings apply to the tray-balloon fallback, not these cards; the AIBar **Windows notification** effect toggle controls both delivery paths.

The glow is transparent, topmost, click-through, absent from the taskbar, and does not activate or take focus. Its thickness is 5% of the short edge, clamped to 64–128 DIPs. Alpha stops are .60 at the edge, .18 at 40%, and zero at the interior, plus a 2-DIP inner line at .85 alpha. It fades in over 250 ms, holds 750 ms, and fades out over 2000 ms. With animation effects off it stays static for 1.5 s then fades over 250 ms. High contrast uses system Highlight, not a colored shadow. Turning off the glow does not turn off notifications.

Confetti has the same click-through/no-activate properties. There are 120 particles per 1080p screen area: 6–9 × 10–15 DIP rounded rectangles and 7-DIP dots, in provider color/tint, white and gold. Particles spawn over the first 300 ms at y=−20, with horizontal velocity ±120 DIP/s, downward velocity 200–420 DIP/s, gravity 900 DIP/s² and spin 180–720°/s. They fade in the last 600 ms and close at 2.5 s. High contrast uses Highlight and WindowText pieces; reduced motion skips confetti. Simultaneous resets or limits share one active effect of each type.

## What does not alert

Stale, unavailable, failed, future-dated, expired-reset, out-of-range, or unconsented readings do not alert. Loading/freshness-only transitions are not new samples: the observer accepts only increasing retrieval generations and increasing retrieval timestamps per provider/window. It also checks consent and current authority again before UI delivery. Claude source handoffs and consent re-enables recreate the coordinator; authority observation is reattached each time, with a runtime-level increasing retrieval generation. Claude notifications are deferred while its runtime gate is held.

A reset is not inferred from a percentage decrease alone, nor from a backward-moving reset timestamp. Reset detection needs a previously accepted current sample. Thresholds and pace alerts are deduplicated by provider, window, reset timestamp, and event/threshold. Reset timestamps represent cycle identity; a provider correction to that timestamp is therefore interpreted as a cycle change.

## Local files and clearing

Under `%LOCALAPPDATA%\AIBar`:

- `alerts.json`: schema 1, `{ "schema": 1, "resets": true, "thresholds": true, "runOut": true, "edgeGlow": true, "notification": true, "confetti": false, "thresholdValues": [75,90,100] }`. Existing schema-1 documents that omit the new fields retain their toggles and receive these defaults. Invalid integer threshold lists fall back to 75/90/100; the Settings editor rejects invalid input with an inline error before saving.
- `alert-state.json`: schema 1, fired keys and the last accepted current sample for each provider/window. This preserves deduplication and reset detection across restarts. It contains normalized quota values only.

Writes use a same-directory temporary file and atomic replacement. The serialized observer performs disk work off the UI thread, persists keys **before** delivery, and never delivers if state persistence fails. Consequently a crash after persistence but before delivery may lose a notification rather than duplicate it. Turning an event off still records its key; turning it on later does not replay old alerts. Missing, malformed, or unknown-schema settings use the defaults; a lost/corrupt state file starts new deduplication history.

**Clear AIBar Data** pauses and drains alert work, removes these files and `appearance.json`, drops in-memory keys/samples, restores the default menu checks, and resumes the observer. It leaves provider consent handling to the existing clear lifecycle. No remote payloads, Codex export v1 fields, or copied quota summaries change.

## Validation on Windows

The focused xUnit tests live in `AlertSettingsTests`, `QuotaAlertPolicyTests`, `QuotaAlertObserverTests`, `QuotaAlertClaudeHookTests`, `QuotaAlertTrayTests`, `EdgeGlowPolicyTests`, `SettingsAlertOptionsTests`, `SettingsDeliveryTests`, `ConfettiPolicyTests`, `NotificationPolicyTests`, and `NotificationWindowTests`. They cover policy boundaries, persistence/restarts, generation/time deduplication, failure suppression, clearing, independent settings, source recreation, notification copy/click routing, and accessibility planning. This Linux authoring environment has no .NET runtime; compilation and test execution must occur in Windows CI.

For a Windows desktop smoke check, use synthetic quota data, cross 100%, and verify: each monitor gets a border; another application's focus is unchanged; clicks reach the application under the border; borders disappear after about three seconds; high contrast uses system colors; disabled animations show a static border then a short fade and no confetti; a notification click or Enter opens the existing panel; Dismiss does not; hover/focus pauses the timer; a fourth card evicts the oldest without exceeding three. Check light/dark against export frames 13–14, the edge glow against frame 15, and confetti against frame 16. Native monitor/DPI placement, Narrator announcements, focus preservation and tray fallback require this interactive check, not just headless policy tests.
