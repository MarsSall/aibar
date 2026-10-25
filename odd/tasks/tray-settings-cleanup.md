# Tray menu cleanup, General settings tab and a general data source

Trim the tray menu to actions, move configuration into Settings, stop unused local scans, and make the data source one setting for every provider.
Branch: `feat/tray-settings-cleanup`, stacked on `feat/floating-widget-and-font` (PR #84; both touch Settings). Verification runs on Windows CI and on the user's PC.

## Specs

- S1 Tray menu: answer → "Refresh, Settings…, Start with Windows, Exit". The tray context menu shows exactly Refresh, Settings…, Start with Windows (checkable, as today) and Exit AIBar. Removed: Read OpenCode usage data locally, Disable/Enable Private Quota Integration, Read Pi usage data locally, Choose OpenCode data folder…, Use default OpenCode data folder, Clear AIBar Data, Alerts >, Disable/Enable Claude quota, Claude source >. User: "aqui hay opciones que no se si sirven al usuario, ni se si tiene sentido que esten ahi ... todo eso deberia ir en settings, que de todas maneras no se si hacen falta todas esas opciones.."
- S2 OpenCode / Pi: answer → "Sacarlas y dejar de escanear (Recom.)": their local usage is not shown anywhere since PR #71, so AIBar no longer scans OpenCode or Pi data; the four menu items disappear. Existing local-usage files stay until Clear AIBar Data.
- S3 General tab: answer → "Nueva pestaña General (Recom.)": Settings gets a General tab with Start with Windows (toggle) and Clear AIBar Data (button with confirmation), plus the data source (S4).
- S4 General data source: user: "habiamos dicho que lo de configurar de que maquina se obtiene la info era para todos los providers y ahi hay una ocpipon (que d etodas maneras no deberia estar en ese lugar) que es la de Claude Source"; "no convendria una opcion generalo?"; answer → "General ahora + helper de Codex en el próximo PR (Recom.)": one "Data source" setting in General — Automatic / This PC / Other machine — for every provider, replacing Providers' Claude source. Automatic: each provider uses the other machine's file when it exists, otherwise this PC. This PC: local logins only. Other machine: remote files only; Codex has no remote quota helper yet, so it shows "No data from the other machine yet" and never reads the local Codex credential. The existing claudeSource choice migrates to the new setting. The Automatic description must match the runtime (remote file wins when present).
- S5 Codex remote quota helper: answer above → next PR (tracked as an issue), not in this feature.

## Tasks

- T1 [S1, S2] Tray menu trimmed to Refresh/Settings…/Start with Windows/Exit; OpenCode/Pi local usage no longer composed or scanned; tests + docs — worker — commit: pending
- T2 [S3, S4] General tab (Start with Windows, Clear AIBar Data with confirmation, Data source for all providers with claudeSource migration and Codex other-machine state); Providers loses Claude source; tests + docs — worker after T1 — commit: pending
- T3 [S1-S5] Windows CI, deploy to the user's PC, acceptance, Codex remote quota issue — inline — commit: pending

## Log

- L1 (user, original request): "perfecto, otra cosa a arreglar es.. [tray menu screenshot] aqui hay opciones que no se si sirven al usuario, ni se si tiene sentido que esten ahi, ademas habiamos dicho que lo de configurar de que maquina se obtiene la info era para todos los providers y ahi hay una ocpipon (que d etodas maneras no deberia estar en ese lugar) que es la de Claude Source, ademas la de Private quota, que ni se que es y la de disable claude quota, todo eso deberia ir en settings, que de todas maneras no se si hacen falta todas esas opciones.."
- L2 (evidence): explore map — tray built in HostRuntime.cs:357–390 + QuotaAlertTrayController.cs:24–41; Private Quota = Codex consent (= Settings "Show Codex in AIBar"); Claude quota = Settings "Show Claude in AIBar"; Claude source and Alerts already in Settings; Start with Windows and Clear AIBar Data have no Settings equivalent; LocalUsage presentation is bound nowhere since PR #71; no record found of an earlier "source for all providers" decision; Settings' Automatic text contradicts runtime precedence (SettingsWindow.xaml:240 vs ClaudeQuotaRuntime.cs:324–325).
- L3 (user, answers): tray "Refresh, Settings…, Start with Windows, Exit"; OpenCode/Pi "Sacarlas y dejar de escanear (Recom.)"; source "no convendria una opcion generalo?" then "General ahora + helper de Codex en el próximo PR (Recom.)"; General "Nueva pestaña General (Recom.)".
- L4 (decision): S2 stops composition/scanning only; the local-usage module and its tests stay for a later removal to keep this PR reviewable. T1 then T2 run sequentially (both edit App.xaml.cs and HostRuntime.cs).
