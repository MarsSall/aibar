# Opened quota panel

The S10 Claude Design export supersedes the earlier CodexBar visual treatment. This is a presentation change: quota retrieval, independent consent, credential read-only access, cost/history policies, Codex export v1 and `QuotaSummaryCopy` output are unchanged. UI text is English; official OpenAI and Claude paths remain the Simple Icons marks (CC0).

## Summary and navigation

The default width is 360 DIPs, minimum 300, maximum height 640. The body scrolls; the same menu remains available in summary and details. Equal-width, 48-DIP provider tabs show a 16-DIP mark, name, 2-DIP usage mini-bar and existing incident indication. Highest usage selects the initial provider (Codex on ties). Explicit selection survives refresh and hide/reopen for the panel lifetime. Revoking Claude temporarily falls back to Codex without forgetting the choice.

The provider header uses 16-DIP semibold type, an optional provider-colored plan pill and a live/offline dot. Plan metadata still comes only from the existing consented provider data. Unknown plans are omitted; other-machine Claude does not invent a plan. Updated text retains the remote-source suffix.

Each available quota window has a tracked uppercase section label, a large right-aligned 22-DIP value and a 4-DIP bar. A warning symbol accompanies values at or above 90%; percentages are never conveyed by color alone. Reset text remains compact and right aligned. Weekly meters have a midpoint tick and retain the existing pace row. Stale bars are dimmed to 45%; sign-in-needed hides the meters without changing the underlying snapshot or availability flags. Missing values remain explicitly unavailable rather than becoming zero.

`PanelSectionLabel` draws tracking while keeping its underlying text intact for UI Automation. `PanelMeter` retains the named noninteractive WPF ProgressBar and its range semantics. Dark bars use a left-to-right gradient and an 18-DIP radial head without a blur effect; light bars are solid and have no head. Bar changes tween over 250 ms when Windows animation effects are enabled.

`MainWindow.BarColorMode` remains Brand by default or Usage (75/90 thresholds). Mini-bars follow the same choice. Shared semantic colors live in `Themes/Semantic.Light.xaml`, `Semantic.Dark.xaml` and `Semantic.HighContrast.xaml`; every old key is retained. The dictionaries also expose the complete S10 shared color-token set for Settings and notification consumers. High contrast maps colors to the specified system keys, not hard-coded RGB approximations.

## Refresh cadence

Opening the panel requests fresh quota from each enabled provider when its last successful retrieval is older than one minute (or no snapshot exists), with at least one minute since the last attempt. Repeated opens after a failure do not bypass this throttle. Manual **Refresh** is unchanged and unthrottled.

Automatic polls refresh at the saved **Settings › Providers › Refresh every** interval for Codex and Claude This PC: **1 / 2 / 5 / 15 / 30 min**, default **5 min**. Save updates their timers and coordinator minimum intervals live; the 1-minute choice shows **May hit provider rate limits.** Claude Other machine always reads its synced local file every minute (not a change to the helper's push cadence). Intervals are measured since the last attempt regardless of snapshot freshness. Source changes update Claude's timer and minimum interval together, restoring the saved interval when switching to This PC. Resume and clock-change notifications follow the same minimum interval; sleep only reevaluates freshness. Freshness thresholds, cached values, failure handling and consent remain unchanged.

## States

- Incident: the existing safe provider status description appears in a warning banner with a warning symbol and View status. Activating it opens the existing allowlisted provider status page.
- Sign-in: a Sign-in needed card shows the existing safe renewal instructions, Try again (the existing refresh command), and, for Claude, Use other machine. The latter raises `UseOtherMachineRequested`; `App.xaml.cs` routes it through `ClaudeQuotaRuntime.SetSourceAsync(OtherMachine)` with fault containment. It does not read or write credentials.
- Stale: a Not current banner includes the local last-reading time and Refresh. Existing freshness, cached-age, safe warning and degraded/offline information remains available. `NeedsLogin`, `ShowStaleBanner` and `LastReadingLabel` are derived presentation properties, not a second authority model.

## Cost and details

Cost is now a bordered card with two amount/meta columns and a right chevron. Amounts use 16-DIP semibold type, metadata 12 DIPs. The panel-only converter separates the existing labels at their currency/token delimiter. Exact amounts, plus suffixes, token counts and partial/unpriced disclosures are preserved. Unavailable never becomes $0. Copy/export strings are not changed.

Cost opens an owned, borderless usage-details window beside the panel; the summary stays visible. It prefers the left side with a 6-DIP gap, moves right when necessary, and aligns its top with Cost within the monitor work area.

- Hover Cost for 250 ms to open details without taking focus. Leaving both Cost and details closes an unpinned window after 400 ms.
- Click, Enter, Space or Right on Cost pins and activates details, focusing Close usage details. Clicking Cost again closes it.
- Esc, Left or the close button in details closes the window and returns focus to Cost.
- An outside click leaving both windows dismisses the pair, except while the settings menu is open. Hiding, dismissing or closing the panel also closes details.

The details window uses the same opaque `SettingsSurfaceBrush` as Settings, independent of background opacity, while its border and dark glow follow the panel's provider decoration (selected provider, high-usage red, sign-in amber, stale neutral, and Provider tint off). Live Windows theme changes update both surfaces.

Two chart sections follow the selected provider tab live:

- **Usage history** shows only that provider's **5-hour** and **weekly** quota percentages. Solid 2.5-DIP lines use the provider color, with weekly at reduced opacity. Lines never bridge a reset cycle or a percentage drop. Columns show the maximum percentage per series in side-by-side buckets: 1 hour for 24h, 6 hours for 7d, and 1 day for 30d. Named legends remain available to automation.
- **Daily usage** uses local Codex analytics or Claude's existing cost summary. The 24h and 7d selections show 7 calendar days; 30d shows 30 days, including zero-filled days. A fully priced visible range shows **$ per day**; otherwise it shows **tokens per day**, including unpriced models' tokens. Each daily bar/point exposes its date, cost and token count to automation. Unavailable daily data shows **No daily usage yet**.

The **Line / Columns** segmented selector beside **24h / 7d / 30d** applies to both charts. It defaults to Line and saves immediately in `appearance.json` as `chartStyle`; Settings saves preserve that choice. Each selector has one Tab stop, with arrows and Home/End moving within it. Left within a selector changes its selection rather than closing details.

The details UI no longer includes **Local usage**, **Local Codex data**, **OpenCode and Pi local usage**, or **Quota access** and its disclosure paragraphs. Their underlying services and presentation properties remain intact; consent dialogs retain the disclosures. No new source-specific cost split is fabricated from aggregate-only data.

History reload/cancellation, observed pace fallback and clear/dispose publication guards are unchanged. Nothing in clear/dispose waits for the WPF dispatcher.

## Menu and keyboard

The menu groups remain Usage Dashboard / Status Page; Refresh / Copy summary; Settings… / About AIBar / Quit. All rows now have icons, 30-DIP minimum height and shortcut hints. Links retain their fixed allowlists; Copy summary remains the existing Codex-only command even when Claude is selected.

- Ctrl+R: existing refresh command, respecting CanExecute.
- Ctrl+C: existing copy-summary command and polite feedback.
- Ctrl+,: existing SettingsRequested event.
- Ctrl+Q: existing QuitRequested event and tray shutdown path.
- Left/Right in summary: switch provider and focus its tab, except Right on Cost opens details.
- Up/Down in the menu: focus the next/previous enabled action, wrapping.
- Enter/Space: native action activation; on Cost, open and focus usage details.
- Esc/Left in details: close details and return focus to Cost.
- Esc in the panel: animated dismissal of the panel and its details window.

The detail range and chart-style selectors keep their own arrow-key behavior. Focused scrolling content is brought into view asynchronously by WPF; tests must account for that rather than removing the behavior.

## Motion, opacity and host integration

Open fades and translates 8 DIPs vertically over 167 ms; provider changes fade the body over 83 ms; Cost/Back slide 24 DIPs and fade over 167 ms. With Windows animation effects off, slides and bar tweens are skipped and fades use 83 ms. High contrast also skips slides and decorative shadows.

`MainWindow.BackgroundOpacity` is a double dependency property, coerced to 0.30–1.00, default 1.00 (nonfinite values become 1). It changes **only the window background brush alpha**, never Window.Opacity or the opacity of text, bars, tabs or buttons. Windows transparency off and high contrast ignore the opacity setting. Existing acrylic tint is 85% dark / 80% light when the backdrop succeeds; unsupported/remote/backdrop-failure behavior still goes through the existing opaque-surface fallback. Settings persistence/step-5 slider binding is owned by the Settings integration, not the panel.

The root border and dark shadow choose the selected provider's glow color, high-usage red, sign-in amber or a neutral stale border/no glow. Header rules use a horizontal provider-color gradient. This is root decoration only, not the screen-edge alert effect.

### Integration still requiring native validation

- `HostRuntime.WpfPopoverRuntime.Hide()` calls `Window.Hide()` directly. To animate outside-click dismissal it must route MainWindow through `Dismiss()` (120 ms, or 83 ms reduced motion). Esc already uses this path. The host is outside this unit's edit surface.
- An exterior WPF glow requires a transparent HWND with a 40-DIP shadow margin and placement/hit-test accounting. The existing host intentionally forces an opaque HWND/acrylic fallback. The current root shadow may be clipped there; it is not evidence of full exterior-glow parity.
- Provider cross-fade currently fades in the newly selected body; it does not retain a second outgoing visual layer.
- Selected tabs use provider-gradient fill and high-usage border overrides; live dots include their 3-DIP ring. Stale values do not yet include per-meter last-reading time, and the sign-in mini-bar still needs the dashed empty-state treatment.
- The export's This PC / Other machine cost split cannot be reconstructed from the current aggregate-only presentation and is not fabricated; the daily chart uses only the existing provider cost-summary inputs.
- At opacity below 100%, the secondary/tertiary text and track/separator contrast adjustments from the translucent frame still need implementation and native comparison.

The parent must union these dictionaries with Settings*- and Notification*-prefixed additions, bind the persisted opacity through App initial load and Changed using BeginInvoke (never await the dispatcher), and verify Windows native frames 00–08 and high contrast before claiming visual parity. No local .NET/PowerShell execution or image render was performed in this unit.
