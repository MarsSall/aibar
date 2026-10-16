# Readable local details

The opened panel keeps weekly and 5-hour quota cards visible above local analytics. The compact YASB widget is unchanged.

## Panel appearance

The panel uses quieter rounded card borders, larger quota percentages, a single freshness badge in the header, and more space between sections. **Copy summary** and **Refresh** share a compact row that wraps when space is limited; their commands and keyboard access are unchanged.

This treatment is inspired by [Quorb's detail-panel design](https://github.com/egdev6/quorb/blob/5d49611b222b7f31370c4132875e370b0c8b76b1/src/theme.css), using original WPF markup and AIBar's existing semantic colors. Panel-specific card and button styles live in `MainWindow.xaml`; application-wide resources, other windows and YASB styling are not changed. There is no new transparency, hover-to-open behavior or animation.

## Local details

- Select **Local details** to expand or collapse the local Codex and OpenCode/Pi sections. Details start collapsed when the window is created.
- Expand an individual provider to inspect its daily and model totals. These provider rows still start collapsed.
- Use Tab to reach the expanders and Space to toggle them. The existing secondary scroll area brings focused content into view; quota cards and the footer stay outside it.

Headers and expanded provider details use the existing semantic text colors in light, dark, and system high-contrast themes. No new palette or global button styling is introduced.

This changes presentation only: it does not change collection permissions, quota refresh, instance activation, or dismissal on loss of focus. There is no pin mode or saved expansion preference.
