# CodexBar-style quota panel

The opened panel stacks **Codex**, then **Claude** when Claude quota is enabled. The compact YASB widget is unchanged. The default width is 360 px; the panel supports 300 px and scrolls when content exceeds the available height.

## Summary

Each provider has its official mark, a 17 px semibold name, a **Dashboard ↗** button to the existing official usage page, and an **Updated …** subtitle. Remote Claude readings add **· from other machine**. Non-current readings retain their freshness, cached age and safe warning text; missing windows do not create empty cards. Incident descriptions appear with a warning dot and open the provider's status page. Operational or unreachable status shows no incident line.

Each available window is shown as **Session** (five hours), then **Weekly**:

- A 6 px rounded bar uses Codex green `#10a37f` or Claude terracotta `#c4703f`, regardless of percentage. Stale/unavailable values are grey; high contrast uses system highlight colors.
- Below the bar, **N% used** is left-aligned and **Resets in 3d 4h** is right-aligned. Compact resets are panel-only: copy summary and exports keep their existing formats.
- Pace is a wrapping line immediately below its own window, not a separate disclosure or second meter.

The previous graphite bands, diamond AIBAR header, condensed uppercase names, source pills, mark glows, threshold flags and **Pace & details** layout have been replaced.

## Pace

Examples:

- **Pace: Behind (−42%) · Lasts to reset**
- **Pace: On track · Lasts to reset**
- **Pace: Ahead (+38%) · Runs out in 1d 8h**

Ahead/Behind expresses the percentage difference from the even-use rate: `(pace ratio − 1) × 100`. Differences smaller than 5% read **On track**. The unrounded projection, not the rounded label, decides whether exhaustion precedes reset. Only that early-exhaustion line uses the warning color (light `#c4581f`, dark `#ff9f5a`). **Pace: Limit reached**, **Pace: Not enough data yet** and **Pace: Not current** remain concise, explicit states. Missing usage/reset or an expired reset has no current pace row.

The underlying observed-pace policy is unchanged: after 5% of the cycle and at least two distinct retrievals in the same reset cycle, use the observed rate. Empty, single-sample, wrong-cycle or decreasing history falls back to the snapshot estimate. Flat observed consumption has no projected exhaustion. Accessible help retains **based on observed use**. Estimates are not guarantees; providers control resets.

## Cost and details

**Cost ›** shows the existing Today and Last 30 days aggregate labels, prefixed by their provider. Partial data, unpriced models, unavailable values and token formatting retain the existing cost presentation rules; the panel does not reinterpret missing cost as zero.

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

- **Refresh**, with **Updated …** on the right, uses the existing combined refresh command. It is disabled when refreshing is unavailable.
- **Copy summary** uses the existing Codex-only command and polite live feedback; its output is unchanged.
- **Status Page** opens OpenAI status and, when Claude quota is enabled, Anthropic status. All destinations remain fixed and allowlisted.
- **Settings…** opens the existing live tray settings menu, including consent, sources, local collection, startup, alerts and Clear AIBar Data commands. It does not create a second settings state.
- **Quit AIBar** enters the existing tray shutdown path.

## Theme and accessibility

Windows light/dark and high contrast are followed automatically. The mockup's light/dark surface, text, secondary text, tracks, rules, spacing and Segoe UI Variable / Segoe UI font stack replace the Quorb palette. Windows 11 acrylic is retained only when the backdrop succeeds. Transparency off, remote sessions, unsupported systems, high contrast or backdrop failure use the existing opaque fallback. High contrast preserves system colors and keyboard focus indicators.

The redesign is presentation-only: consent, status polling, refresh/data flows, history, alerts, cost aggregation, Codex export v1 and `QuotaSummaryCopy` semantics are unchanged. There is no hover-to-open behavior, pin mode or saved expansion preference.

Official OpenAI and Claude path data comes from [Simple Icons](https://simpleicons.org/) (CC0-1.0). The marks identify their providers and remain their trademarks; AIBar is not affiliated with either company. The layout follows [CodexBar](https://github.com/steipete/CodexBar), implemented in original WPF markup.
