# Readable local details

The opened panel keeps weekly and 5-hour quota cards visible above local analytics. The compact YASB widget is unchanged.

## Panel appearance

The panel is one module: a compact header band, integrated weekly-first quota meters without individual card frames, and a separated action/footer band. Percentages use compact 20px type alongside each label; progress and reset information stay together. **Copy summary** and **Refresh** use small ghost buttons in a row that wraps when needed.

This treatment is inspired by [Quorb's detail-panel design](https://github.com/egdev6/quorb/blob/5d49611b222b7f31370c4132875e370b0c8b76b1/src/theme.css), using original WPF markup and AIBar's existing semantic colors. Panel-specific meter, button and disclosure styles live in `MainWindow.xaml`; application-wide resources, other windows and YASB styling are not changed. There is no new transparency, hover-to-open behavior or animation.

## Local details

- Select **Details & quota access** to reveal local Codex and OpenCode/Pi usage, plus the full private-quota access disclosures. These details start collapsed.
- Provider headers keep the scope, exact total and status separate. Expand a provider to reveal its exact token breakdown and daily/model totals; no totals are rounded or removed.
- Right-hand chevrons replace the operating-system expander decoration. Use Tab to focus a disclosure and Space or Enter to toggle it; UI Automation ExpandCollapse remains available.
- The secondary area scrolls and brings focused content into view. Quota meters, warnings, copy feedback and footer actions remain outside it.

Headers and expanded provider details use the existing semantic text colors in light, dark, and system high-contrast themes. No new palette or global button styling is introduced.

This changes presentation only: it does not change collection permissions, quota refresh, instance activation, or dismissal on loss of focus. There is no pin mode or saved expansion preference.
