# Readable local details

The opened panel keeps weekly and 5-hour quota cards visible above local analytics. The compact YASB widget is unchanged.

## Panel appearance

The panel is one module on a graphite surface: a header band with a small diamond and **AIBAR**, one section per provider, a **Pace & details** disclosure and a darker action band. Each provider section shows its mark, a condensed uppercase name, where the quota is read (**This PC** or **Other machine**) and when it was last updated.

Each quota meter reads as one row: a dot and the window name, the percentage used on the right, an 8px bar, then a compact reset such as "Resets in 3d 20h". The compact reset is panel-only; **Copy summary** keeps its existing text.

| Usage | Bar, dot and percentage | Flag |
| --- | --- | --- |
| Below 75% | Green | none |
| 75% to below 90% | Yellow | **High usage** |
| 90% and above | Red | **Near limit** |
| Not current (cached or stale) | Gray | none |

The flag always names the condition in words, so color is never the only signal. Warnings such as a required login or stale data from the other machine appear as amber notice cards.

### Translucency

On Windows 11 the panel uses the system acrylic backdrop with a translucent graphite tint, so the desktop shows through blurred. It stays opaque when any of these hold:

- Windows **Transparency effects** are off.
- A high-contrast theme is active (system colors are kept).
- The session is remote, or the system does not support backdrops.
- The backdrop request fails.

### Provider marks

The OpenAI and Claude marks use path data from [Simple Icons](https://simpleicons.org/) (CC0-1.0). The marks are trademarks of OpenAI and Anthropic and are used only to identify each provider; AIBar is not affiliated with either company.

The visual language is inspired by [Quorb](https://github.com/egdev6/quorb/blob/5d49611b222b7f31370c4132875e370b0c8b76b1/src/theme.css) and [CodexBar](https://github.com/steipete/CodexBar); colors and spacing are reimplemented in original WPF markup. Panel-specific styles live in `MainWindow.xaml`, `PanelMeter.xaml` and `ProviderVisuals.cs`; application-wide resources, other windows and YASB styling are not changed. There is no hover-to-open behavior or animation.

Claude subscription quota is [optional and separately enabled](claude-quota.md). When enabled, its weekly and 5-hour meters appear below Codex inside the existing quota area, with provider headings and independent loading, login, unavailable and stale-cache status. Disabling Claude hides the section and both headings without restarting; the default Codex-only layout is unchanged. **Refresh** includes Claude only while enabled; **Copy summary** stays Codex-only.

## Pace

Select **Pace & details** to see the **PACE** section, **Usage history**, cost and tokens, followed by smaller **Local usage** and **Quota access** details. The disclosure starts collapsed and keeps its keyboard and screen-reader behavior.

Each quota window has its own pace row: Codex weekly and 5-hour first, then Claude windows only while Claude is enabled. With sufficient history, pace compares the observed consumption rate with the even-use rate (100% over 5 hours or 7 days). Otherwise it compares the percentage used with the percentage of the window that has elapsed. **1.0x** means on track to use exactly 100% at reset; **0.7x** is slower, **1.4x** is faster. The ratio is displayed to one decimal with a decimal point; calculations use the unrounded value.

The slim bar shows usage with the same green/yellow/red thresholds as the quota meters. Its vertical marker is the **even-use guide**: where usage would be if it were spread evenly over the window. Pace text stays muted when lasting until reset, turns yellow when projected to run out early, and red at **Limit reached**. High contrast keeps system colors.

A faster rate shows a compact estimate such as **runs out in 1h 10m**, plus an amber notice: “At this pace, Claude 5-hour runs out before it resets.” This is a constant-rate estimate, not a guarantee. Once more than 5% of the cycle has elapsed and at least two distinct retrievals share the current reset instant, pace uses the rate between the earliest and latest samples in that cycle. Its accessible help says **based on observed use**; the visible pace wording is unchanged. Empty, single-sample, wrong-cycle or decreasing history falls back to the snapshot estimate. A flat observed rate means no projected exhaustion. Usage can change, and the provider controls resets.

The first 5% of a window shows **Not enough data yet**. Missing usage/reset information or an expired reset has no current pace row. Cached or stale windows show **Not current** in muted text without an early-exhaustion notice.

## Usage history

Below the pace rows, **Usage history** plots percentages used on a fixed **0–100%** scale. Choose **24h**, **7d** or **30d** (default 24h) using the named, keyboard-focusable range selector. Tab reaches it; arrow keys select a range. The four labeled series are Codex 5-hour, Codex weekly, Claude 5-hour and Claude weekly. Solid, dashed, dotted and dash-dot strokes distinguish them without relying on color. They use the panel's dynamic text brush, including system high contrast. The plot scales to the available width; legends wrap.

The chart reads the shared [quota history store](quota-history.md), including samples exactly on either range edge. It does not invent intermediate samples or connect different reset cycles or decreasing usage. A single retrieval remains visible as a dot. The legend exposes sample counts and latest percentages to assistive technology; empty history says **No usage history yet**. A read failure says **Usage history unavailable** without affecting quota retrieval.

Opening the disclosure or changing range reloads the chart; while visible, it refreshes every 30 seconds and recalculates pace from history. Closing it discards chart data and cancels its read. Clear AIBar Data invalidates chart and observed-pace results before the database is cleared; in-flight reads cannot restore old results. The range is not persisted.

## Local details

- Below pace, the smaller **Local usage** and **Quota access** sections retain local Codex and OpenCode/Pi usage and the full private-quota access disclosures.
- Provider headers keep the scope, exact total and status separate. Expand a provider to reveal its exact token breakdown and daily/model totals; no totals are rounded or removed.
- Right-hand chevrons replace the operating-system expander decoration. Use Tab to focus a disclosure and Space or Enter to toggle it; UI Automation ExpandCollapse remains available.
- The secondary area scrolls with a slim rounded scroll bar (no arrow buttons) and brings focused content into view. Quota meters, warnings, copy feedback and footer actions remain outside it.

Headers and expanded provider details use the existing semantic text colors in light, dark, and system high-contrast themes. No global button styling is introduced.

The local-details presentation does not change collection permissions, instance activation, or dismissal on loss of focus. There is no pin mode or saved expansion preference.
