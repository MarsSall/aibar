# Quota alerts

AIBar alerts on **Codex and Claude** subscription quota, independently for the 5-hour and weekly windows. Providers must be enabled through their existing consent controls. Alerts do not read credentials or change either provider's quota/export contract.

## Choose alerts

Open the tray menu → **Alerts**. All four choices default to on:

| Choice | Behavior |
| --- | --- |
| Window resets | Notify when a current reading moves the reset timestamp to a later cycle. |
| 75 / 90 / 100% thresholds | Notify once at each crossed threshold in each cycle. A first current reading already above a threshold can notify too. |
| May run out before reset | Notify once per cycle when `QuotaPacePolicy` estimates exhaustion before the reset. This is an estimate, not a promise. |
| Edge glow at 100% | Brief red glow on every monitor when the limit is reached, independently of threshold balloons. |

A reading that jumps from below 75% to 100% crosses all three thresholds. Each is an independent alert. Windows may coalesce or suppress tray notifications under its notification settings or Do Not Disturb; AIBar does not bypass those settings. Clicking an AIBar notification **opens the panel** (it never toggles an already open panel closed).

The glow is transparent, topmost, click-through, absent from the taskbar, and does not activate or take focus. It fades for approximately three seconds. High contrast disables the glow entirely. With Windows client-area animations disabled, the border is static for three seconds instead. Turning off the glow does not turn off notifications.

## What does not alert

Stale, unavailable, failed, future-dated, expired-reset, out-of-range, or unconsented readings do not alert. Loading/freshness-only transitions are not new samples: the observer accepts only increasing retrieval generations and increasing retrieval timestamps per provider/window. It also checks consent and current authority again before UI delivery. Claude source handoffs and consent re-enables recreate the coordinator; authority observation is reattached each time, with a runtime-level increasing retrieval generation. Claude notifications are deferred while its runtime gate is held.

A reset is not inferred from a percentage decrease alone, nor from a backward-moving reset timestamp. Reset detection needs a previously accepted current sample. Thresholds and pace alerts are deduplicated by provider, window, reset timestamp, and event/threshold. Reset timestamps represent cycle identity; a provider correction to that timestamp is therefore interpreted as a cycle change.

## Local files and clearing

Under `%LOCALAPPDATA%\AIBar`:

- `alerts.json`: schema 1, `{ "schema": 1, "resets": true, "thresholds": true, "runOut": true, "edgeGlow": true }`.
- `alert-state.json`: schema 1, fired keys and the last accepted current sample for each provider/window. This preserves deduplication and reset detection across restarts. It contains normalized quota values only.

Writes use a same-directory temporary file and atomic replacement. The serialized observer performs disk work off the UI thread, persists keys **before** delivery, and never delivers if state persistence fails. Consequently a crash after persistence but before delivery may lose a notification rather than duplicate it. Turning an event off still records its key; turning it on later does not replay old alerts. Missing, malformed, or unknown-schema settings use the defaults; a lost/corrupt state file starts new deduplication history.

**Clear AIBar Data** pauses and drains alert work, removes both files, drops in-memory keys/samples, restores the default menu checks, and resumes the observer. It leaves provider consent handling to the existing clear lifecycle. No remote payloads, Codex export v1 fields, or copied quota summaries change.

## Validation on Windows

The focused xUnit tests live in `AlertSettingsTests`, `QuotaAlertPolicyTests`, `QuotaAlertObserverTests`, `QuotaAlertClaudeHookTests`, `QuotaAlertTrayTests`, and `EdgeGlowPolicyTests`. They cover policy boundaries, persistence/restarts, generation/time deduplication, failure suppression, clearing, independent settings, source recreation, notification copy/click routing, and accessibility planning. This Linux authoring environment has no .NET runtime; compilation and test execution must occur in Windows CI.

For a Windows desktop smoke check, use synthetic quota data, cross 100%, and verify: each monitor gets a border; another application's focus is unchanged; clicks reach the application under the border; borders disappear after about three seconds; high contrast shows no border; disabled animations show a static border; a notification click opens the existing panel. Native monitor/DPI placement and actual Windows balloon presentation require this interactive check, not just headless policy tests.
