# Settings

Open **Settings…** from the panel or tray. The window follows Windows light, dark, and high-contrast themes. Use Tab / Shift+Tab to move between controls, arrow keys for choices and section tabs, Alt access keys for labeled controls, Enter to save, and Escape to close. Changes apply only after **Save changes**; closing without saving discards edits.

## Colors

| Choice | Default | Options |
| --- | --- | --- |
| Panel bars | Brand colors | Brand colors or Usage level (green below 75%, yellow from 75%, red from 90%). |
| YASB colors | Usage level | Existing usage ramp or Brand colors: Codex `#2FBF98`, Claude `#E0876A`. |

YASB picks up the preference on its next normal refresh. Missing, malformed, unsupported-schema, oversized, or invalid YASB preferences fall back to the existing usage ramp. Unavailable quota remains gray. The reader's six output fields and quota export schema do not change.

## Alerts

Enable window resets, crossed thresholds, and estimates of running out before reset separately. Enter thresholds as comma-separated, strictly ascending integers from **1 to 100** (default **75, 90, 100**). Duplicate, descending, fractional, empty, and out-of-range entries show an inline error and disable Save. These alert thresholds do not change the fixed appearance usage-color boundaries.

Choose effects independently:

- **Windows notification**: standard tray notification for enabled events. Windows may suppress it under Do Not Disturb.
- **Edge glow at limit (100%)**: approximately three seconds, independently of threshold notifications and whether 100 is in the custom list. High contrast disables it; reduced motion leaves a static border.
- **Confetti on window reset**: approximately 2.5 seconds on every monitor, only when reset alerts are enabled. It defaults **off**. High contrast or disabled Windows client-area animation suppresses it completely. Simultaneous resets share one active effect.

Overlays are transparent, topmost, click-through, absent from the taskbar, and do not activate or take keyboard focus. The tray **Alerts** submenu retains its existing toggles; **Configure alerts…** opens Settings. See [Quota alerts](alerts.md) for freshness, consent, deduplication, and persistence rules.

## Local persistence and clearing

`%LOCALAPPDATA%\AIBar\appearance.json`:

```json
{"schema":1,"barColors":"brand","yasbColors":"usage"}
```

Both color values accept only `"brand"` or `"usage"`. Missing settings default to brand/usage. Invalid documents fall back to defaults. Successful writes replace the destination atomically through a same-directory temporary file and raise `AppearanceSettings.Changed`. Cancelled or failed writes do not publish a change. No provider files or credentials are modified.

**Clear AIBar Data** drains appearance writes along with alert work, removes `appearance.json`, `alerts.json`, and `alert-state.json`, and publishes the actual appearance after clearing (defaults on success; surviving values after a rollback). Clear/dispose paths never await the WPF dispatcher. Saving both sections is not a cross-file transaction: a storage failure can save one section and leave another unchanged; the inline status explicitly reports failure and permits retry.

## Integration contract

`SettingsWindowHost(AppearanceSettings, QuotaAlertObserver).Show(owner)` is the UI-thread entry point for panel and tray routing. It reuses one window and saves alert options on the observer's serialized queue, so tray checkmarks and policy use the same persisted choices. `App.StartupComposition.Appearance` exposes the shared appearance instance; it also participates in the clear lifecycle.

The parallel panel unit owns `MainWindow.BarColorMode` / `PanelBarColorMode`. At integration, load `Appearance.LoadAsync` once and map `BarColors == "usage"` to `PanelBarColorMode.Usage`, otherwise `Brand`; subscribe to `Changed` and post the same mapping to the panel dispatcher. Do **not** await dispatcher work from the `Changed` callback: it also runs during clear. This worktree intentionally does not reference the parallel unit's not-yet-present enum.

## Windows verification

Run the Windows CI .NET job and pinned Pester job (24 tests). `AppearanceSettingsTests`, `SettingsAlertOptionsTests`, `SettingsWindowTests`, `SettingsDeliveryTests`, and `ConfettiPolicyTests` cover the new contract alongside the existing alert tests.

Interactive smoke checks remain necessary for native monitor placement and focus: save/reopen both sections, exercise keyboard navigation and invalid threshold errors, change Windows theme while Settings is open, trigger a reset with notifications disabled and confetti enabled, and verify clicks and focus still reach the underlying app. Repeat with multiple monitors/mixed DPI, high contrast, and animations disabled.
