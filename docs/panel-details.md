# CodexBar-style quota panel

The opened panel shows **one provider at a time**, selected from a CodexBar-style tab strip. Codex and enabled Claude tabs show their official marks, names, mini usage bars and incident indicators; the selected tab uses an accent pill. The highest available usage (maximum of Session and Weekly, Codex on ties) is selected until the user chooses a tab. That choice is remembered for the lifetime of the panel, including hide/reopen and refresh. Revoking Claude temporarily falls back to Codex without losing the choice. No new preferences file is written.

The compact YASB widget is unchanged. The default width is 360 px; the panel supports 300 px and scrolls when content exceeds the available height.

## Summary

The selected provider has a large bold name, an **Updated …** subtitle and an optional plan label at the right. Claude's plan comes only from the existing read-only credential's `subscriptionType` (for example `max` → **Max**, `pro` → **Pro**). Other-machine Claude omits the plan. Codex reads optional `plan_type` from the already requested `wham/usage` response. Unknown values are omitted; there are no extra network calls or credential reads for plans. Remote Claude readings add **· from other machine**. Non-current readings retain their freshness, cached age and safe warning text; missing windows do not create empty cards. Incident descriptions appear with a warning dot and open the provider's status page. Operational or unreachable status shows no incident line.

Each available window is shown as **Session** (five hours), then **Weekly**:

- A 6 px rounded bar defaults to Codex green `#10a37f` or Claude terracotta `#c4703f`. `MainWindow.BarColorMode` is a dependency property with `PanelBarColorMode.Brand` (default) and `Usage`. Usage mode restores the semantic ramp: green below 75%, yellow from 75%, red from 90%. Mini tab bars follow the same choice. Stale/unavailable values are grey in either mode; high contrast uses system highlight colors.
- Below the bar, **N% used** is left-aligned and **Resets in 3d 4h** is right-aligned. Compact resets are panel-only: copy summary and exports keep their existing formats.
- Pace is a wrapping line below **Weekly**, not a separate disclosure or second meter. Session pace is still calculated by the existing presentation/data policies but is not repeated in the summary.

The previous graphite bands, diamond AIBAR header, condensed uppercase names, source pills, mark glows, threshold flags and **Pace & details** layout have been replaced.

## Pace

Examples:

- **Pace: Behind (−42%) · Lasts to reset**
- **Pace: On track · Lasts to reset**
- **Pace: Ahead (+38%) · Runs out in 1d 8h**

Ahead/Behind expresses the percentage difference from the even-use rate: `(pace ratio − 1) × 100`. Differences smaller than 5% read **On track**. The unrounded projection, not the rounded label, decides whether exhaustion precedes reset. Only that early-exhaustion line uses the warning color (light `#c4581f`, dark `#ff9f5a`). **Pace: Limit reached**, **Pace: Not enough data yet** and **Pace: Not current** remain concise, explicit states. Missing usage/reset or an expired reset has no current pace row.

The underlying observed-pace policy is unchanged: after 5% of the cycle and at least two distinct retrievals in the same reset cycle, use the observed rate. Empty, single-sample, wrong-cycle or decreasing history falls back to the snapshot estimate. Flat observed consumption has no projected exhaustion. Accessible help retains **based on observed use**. Estimates are not guarantees; providers control resets.

## Cost and details

**Cost ›** shows the existing Today and Last 30 days aggregate labels for the selected provider only, without redundant provider prefixes. Partial data, unpriced models, unavailable values and token formatting retain the existing cost presentation rules; the panel does not reinterpret missing cost as zero.

Click **Cost ›**, or focus it and press Enter/Space, to open an in-panel detail view. Focus moves to **‹ Back**. The summary is replaced, while the menu remains available. Back (mouse, Enter or Space) restores the summary and focus to Cost.

The detail view contains:

1. **Usage history**, with a named keyboard-focusable **24h / 7d / 30d** selector (default 24h; arrow keys change range), a fixed **0–100%** chart and wrapping legends.
2. **Local usage**, retaining local Codex source, exact totals, models, coverage, scan time and warnings, plus OpenCode/Pi provider disclosures and exact daily/model/token breakdowns.
3. **Quota access**, including the full existing private-endpoint disclosure.

The four chart series retain distinct solid/dashed/dotted/dash-dot strokes, now in their provider brand colors (system highlight in high contrast). They never connect reset cycles or decreasing usage, and a single retrieval remains a dot. The legend retains accessible sample counts and latest percentages. Empty history says **No usage history yet**; read failure says **Usage history unavailable**.

Opening details, changing range or the visible 30-second timer reloads chart history. Back cancels the chart read and discards its data. Clear AIBar Data still invalidates in-flight chart and observed-pace publication before clearing storage. No clear/dispose path waits for the WPF dispatcher. The selected range is not persisted.

Local provider disclosures keep UI Automation ExpandCollapse, Tab navigation, Enter/Space toggling and focus visuals. A slim rounded scrollbar has no arrow buttons; focused content is brought into view. Exact totals and partial-data disclosures are not rounded or removed.

## Menu

The same vertical menu remains available in both views:

1. **Usage Dashboard** and **Status Page** (with icons) open the selected provider's existing fixed, allowlisted destinations.
2. After a rule: **Refresh** uses the existing combined refresh command (disabled when unavailable), then **Copy summary** uses the existing Codex-only command and polite live feedback. Copy output is unchanged even when Claude is selected.
3. After a rule: **Settings…**, **About AIBar**, **Quit**. Settings raises the existing `SettingsRequested` event; the composition owner routes this to the S8 Settings window. About toggles an in-panel version/attribution note. Quit raises the existing `QuitRequested` event and enters the tray shutdown path.

Integration: `App.xaml.cs` retains the Codex provider solely to pass its optional display metadata to `QuotaPresentationHost`. The parent owns S8 appearance persistence and routing: bind `MainWindow.BarColorMode` to the appearance setting, replace the existing tray-menu Settings handler with the new Settings window, and keep Quit wiring unchanged.

## Theme and accessibility

Windows light/dark and high contrast are followed automatically. The mockup's light/dark surface, text, secondary text, tracks, rules, spacing and Segoe UI Variable / Segoe UI font stack replace the Quorb palette. Windows 11 acrylic is retained only when the backdrop succeeds. Transparency off, remote sessions, unsupported systems, high contrast or backdrop failure use the existing opaque fallback. High contrast preserves system colors and keyboard focus indicators.

The redesign is presentation-only: consent, status polling, refresh/data flows, history, alerts, cost aggregation, Codex export v1 and `QuotaSummaryCopy` semantics are unchanged. There is no hover-to-open behavior, pin mode or saved expansion preference.

Official OpenAI and Claude path data comes from [Simple Icons](https://simpleicons.org/) (CC0-1.0). The marks identify their providers and remain their trademarks; AIBar is not affiliated with either company. The layout follows [CodexBar](https://github.com/steipete/CodexBar), implemented in original WPF markup.
