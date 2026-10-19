# Readable local details

The opened panel keeps weekly and 5-hour quota cards visible above local analytics. The compact YASB widget is unchanged.

## Panel appearance

The panel is one module on a graphite surface: a header band with a small diamond and **AIBAR**, one section per provider, a **Details & quota access** disclosure and a darker action band. Each provider section shows its mark, a condensed uppercase name, where the quota is read (**This PC** or **Other machine**) and when it was last updated.

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

## Local details

- Select **Details & quota access** to reveal local Codex and OpenCode/Pi usage, plus the full private-quota access disclosures. These details start collapsed.
- Provider headers keep the scope, exact total and status separate. Expand a provider to reveal its exact token breakdown and daily/model totals; no totals are rounded or removed.
- Right-hand chevrons replace the operating-system expander decoration. Use Tab to focus a disclosure and Space or Enter to toggle it; UI Automation ExpandCollapse remains available.
- The secondary area scrolls and brings focused content into view. Quota meters, warnings, copy feedback and footer actions remain outside it.

Headers and expanded provider details use the existing semantic text colors in light, dark, and system high-contrast themes. No global button styling is introduced.

The local-details presentation does not change collection permissions, instance activation, or dismissal on loss of focus. There is no pin mode or saved expansion preference.
