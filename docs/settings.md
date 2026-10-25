# Settings

Open **Settings…** from the panel or tray. One Settings window is reused. It follows Windows light, dark and high-contrast themes, with a custom 32-DIP title bar and **General / Appearance / Alerts / Providers** pivots. The default size is 520 × 600 DIPs (minimum 460 × 480); content scrolls without moving the footer.

The tray now contains only **Refresh** (when its command can execute), **Settings…**, **Start with Windows** (checkable), and **Exit AIBar**, in that order. Provider consent/source choices and alerts are configured here, not in tray submenus. OpenCode/Pi scans are no longer composed or started, even with an old opt-in; retained files remain covered by Clear AIBar Data. Start with Windows and Clear AIBar Data are also available in General.

Changes apply when you select **Save**. **Cancel**, Escape and closing the window discard edits that have not been saved. Save leaves the window open and announces success or failure. Preview buttons do not save preferences. **Start with Windows**, **Clear AIBar Data** and **Install YASB font** are immediate actions, independent of Save or Cancel.

## Keyboard and accessibility

- **Ctrl+Tab** cycles pivots; **Ctrl+Shift+Tab** cycles backwards.
- **Tab / Shift+Tab** move through controls; **Space** toggles checkboxes and switches.
- **Arrow keys** move through radio groups (panel colors, YASB colors and refresh interval) and provider Source dropdowns. **Alt+Down** opens a dropdown. **Home / End** select the first / last refresh interval; the refresh group has one Tab stop.
- **Enter** saves, unless validation fails; **Escape** cancels.
- The opacity slider supports arrows in 5-point steps and Page Up/Down in 10-point steps.

Selected pivots have an accent underline and semibold text. Controls have named automation peers and keyboard focus rings. Errors are announced inline and are never represented by color alone. High contrast uses system colors; toggle animation is omitted when Windows client-area animations are off or high contrast is active.

## General

**Start with Windows** loads the current Windows registration. Clicking it toggles the native registration and displays the actual readback result, not an assumed success.

**Clear AIBar Data** asks: “Clear AIBar Data? This removes AIBar's local data and turns off Codex quota access. Your provider logins are not touched.” Choose **Clear** or **Cancel** (the safe default). Cancel does nothing; Clear drains the existing runtime work and removes AIBar-owned data. The result appears inline and Settings reloads its controls.

## Appearance

**Follows Windows** reports the current theme and points to Windows Settings › Personalization › Colors. There is no independent app-theme override.

| Choice | Default | Options |
| --- | --- | --- |
| Background opacity | 100% | 30–100%, step 5. Applies to panel and notification background brushes, not text, bars, tabs or buttons. |
| Floating widget | Off | Always-on-top quota rings, useful without YASB. Drag to any screen edge; click to open the panel. Save preserves the current edge, offset and monitor. |
| Provider tint | On | Colors the panel border, glow, header divider and selected tab with the selected provider. Off keeps them neutral; the sign-in warning stays amber either way. |
| Chart style | Line | Line or Columns, changed directly beside the range selector in Cost details; applies to both charts and saves immediately. Settings preserves this stored choice. |
| Panel bars | Brand colors | Brand colors, or Usage level: green below 75%, yellow from 75%, red from 90%. |
| YASB colors | Usage level | Existing usage ramp, or Brand colors. |

Color radio cards show illustrative bars and YASB previews, not live quota. The usage-color boundaries are independent of alert thresholds. YASB picks up the saved preference on its next normal refresh; unavailable quota remains gray and the six output fields/export schema are unchanged.

Opacity is persisted even when Windows high contrast or disabled transparency forces surfaces to 100%. Settings and the usage-details side window stay opaque. The panel and notification consumers must apply alpha only to their background brush and use an opaque fallback when transparency is unavailable.

**Install YASB font** installs the bundled IosevkaTerm Nerd Font for your Windows user only, without administrator rights or a download. Its status shows whether it is installed; the button is disabled when installed or when the bundled font is unavailable. After installation, restart YASB.

At startup, YASB users with a missing font receive an install offer (**Not now** retries next launch; **Don't ask again** suppresses it). Without YASB, AIBar offers the floating widget once. The offers are mutually exclusive; Settings remains available afterwards.

## Alerts

Enable **Window resets**, **Thresholds** and **Runs out before reset** independently. Each switch shows **On/Off** as well as its position.

Thresholds are comma-separated, strictly ascending whole numbers from **1 to 100** (default **75, 90, 100**). Valid values have corresponding ticks beside the input. Duplicate, descending, fractional, empty and out-of-range entries show an icon and inline error, a danger input border, a dot on the Alerts pivot, and **Fix the thresholds to save** in the footer; Save is disabled even when another pivot is selected. Descending values identify the offending pair.

Three independently selectable effect tiles have a preview button and a checkbox:

- **Windows notification**: preview uses the host's normal notification delivery entry point; no settings are changed.
- **Edge glow at limit**: preview uses the existing non-activating, click-through monitor overlays.
- **Confetti on reset**: defaults off; preview uses the existing confetti overlays.

Delivery policy, freshness checks, consent checks and deduplication remain owned by the alert runtime, not the Settings view. Preview does not create alert-state entries. See [Quota alerts](alerts.md) for delivery and persistence rules. There is no tray Alerts submenu; use this Alerts pivot.

## Providers

**Show Codex in AIBar** and **Show Claude in AIBar** each have a compact **Source** dropdown on the same line. Each provider stages its own enabled state and source until Save. Official OpenAI and Claude marks are retained rather than using the design export's placeholders.

| Source | Meaning for the selected provider |
| --- | --- |
| Automatic (default) | Uses data sent from another machine when it exists, otherwise this PC's login. Claude prefers its remote quota file when present; Codex remains local because it has no remote quota reader yet. |
| This PC | Only this PC's login. |
| Other machine | Only data sent from another machine. Codex has no remote quota yet: it shows **No data from the other machine yet**, hides cached local quota and never reads its local credential or calls its quota endpoint. |

The dropdowns have accessible names **Codex source** and **Claude source**; each item has a tooltip explaining its choice. You can mix choices, such as Codex Automatic and Claude Other machine. General has no source control.

Codex Other machine also publishes a value-free YASB export, drops queued local alerts and stops local analytics. Returning to This PC or Automatic resumes local refresh and analytics only with Codex consent. Source selection never grants consent. The panel's **Use other machine** action changes only Claude; a Codex remote quota helper remains a separate follow-up (#85).

Cost follows each provider's own source too: Codex Automatic combines local and remote token rows, This PC uses local rows only, and Other machine uses remote rows only. Claude uses remote token rows in Automatic and Other machine; This PC has no local token reader and shows cost as unavailable. Each provider's source change refreshes its cost view without waiting for a quota poll.

Enabling a disabled provider shows the existing provider-specific consent disclosure. Declining either required disclosure performs no provider/source writes and leaves the draft open. Disabling requires no prompt. Runtime commands remain the only consent writers; Settings never reads or writes credentials.

**Refresh every** offers **1 min / 2 min / 5 min / 15 min / 30 min**, default **5 min**. Save applies the choice live to both the poll timers and minimum intervals for Codex and Claude **This PC**, without restarting. Selecting **1 min** shows **May hit provider rate limits.** The warning disappears for the other choices. Claude **Other machine** continues reading its synced local file every **1 min**, including when Automatic resolves to Other machine; this does not change the helper's push cadence. Switching back to This PC uses the saved interval.

Opening the panel still refreshes data older than one minute, with a one-minute attempt throttle; manual **Refresh** is unchanged. The interval does not change freshness thresholds, consent, or YASB.

Save applies only changed sources to their respective runtimes before granting consent. Changing one source does not change the other. The Claude remote payload and reader contract are unchanged. Without runtime command bindings, the corresponding controls are explicitly unavailable.

## Persistence and clearing

`%LOCALAPPDATA%\AIBar\settings.json` stores independent `codexSource` and `claudeSource` values: `automatic`, `this-pc` or `other-machine`. When `codexSource` is absent, it migrates from the saved general `dataSource`, or defaults to Automatic. When `claudeSource` is absent, it also migrates from `dataSource`; an existing Claude choice wins. A Claude-only legacy choice never selects Codex's source. Present invalid provider values default to Automatic rather than falling back to the general value.

Every save writes the provider fields atomically and stops writing `dataSource`; that field is read only for migration. Consent decisions and Claude cache provenance are preserved regardless of save order, even if an unrelated consent field is malformed.

`%LOCALAPPDATA%\AIBar\appearance.json` remains schema 1:

```json
{"schema":1,"barColors":"brand","yasbColors":"usage","backgroundOpacity":100,"refreshMinutes":5,"providerTint":true,"chartStyle":"line"}
```

Existing schema-1 files without `backgroundOpacity` load with **100**; missing `refreshMinutes` loads with **5**; missing `providerTint` loads as **on**; missing or invalid `chartStyle` loads as **line**. Invalid numeric opacity falls back to 100, and invalid numeric refresh intervals fall back to 5, without discarding valid color choices. Saves reject out-of-range/non-step opacity, unknown color modes, refresh intervals outside 1/2/5/15/30, and chart styles other than `line` or `columns`. Successful writes atomically replace the destination via a same-directory temporary file and raise `AppearanceSettings.Changed` with all six values; failed/cancelled writes do not publish a change.

**Clear AIBar Data** drains appearance writes with alert work, removes the existing appearance/alert files, and publishes the actual post-clear appearance (defaults on success, surviving values after rollback). Clear/dispose paths never await the WPF dispatcher.

`dock.json` stores the floating widget's enabled state and placement separately from appearance; `onboarding.json` remembers answered startup offers. Both are included in **Clear AIBar Data**; clearing resets the widget and offer choices, but does not uninstall the font.

Saving multiple sections is **not a cross-file transaction**. A later persistence failure can leave an earlier section saved. The status reports that not all changes were saved and allows retry; this also applies to provider runtime commands.

## Integration contract

`SettingsWindowHost(AppearanceSettings, QuotaAlertObserver, SettingsProviderCommands?, Action? previewNotification, DockSettings? dock, SettingsGlyphFont? font)` is the UI-thread entry point. The optional parameters preserve existing call sites. Alert saves still run on the observer's serialized queue.

`App.xaml.cs` constructs `SettingsProviderCommands` from the existing `NativeSettingsCommands`, `BetaRuntime` and `ClaudeQuotaRuntime`, and passes the tray's notification entry point for preview. Codex uses `EnablePrivateIntegrationAsync` / `DisablePrivateIntegrationAsync`; Claude uses `GrantConsentAsync` / `RevokeConsentAsync` and `SetSourceAsync`. The tray's former provider/alert entry points are removed; the underlying runtime operations remain available to Settings.

The appearance instance remains shared with startup composition and the clear lifecycle. Existing panel bar-color mapping stays intact. The parallel panel/notification units must load `BackgroundOpacity` initially and subscribe to `Changed` for updates, posting to their dispatcher **without awaiting it**. Do not apply `Window.Opacity`: content must stay opaque.

Only `Settings*` tokens were added to `Semantic.Light.xaml`, `Semantic.Dark.xaml` and `Semantic.HighContrast.xaml`. Union these with panel and notification tokens during integration. The panel-owned exact semantic-key assertions must include these new keys (or validate the independently owned prefix sets separately). `SettingsProviderMarks.xaml` contains Settings-prefixed copies of the same CC0 official geometries as the panel.

## Windows verification

No local .NET/PowerShell verification is available in the Linux worktree. For the per-provider source change, the parent split RED-first development into a tests-only revision and a production revision. Nothing was compiled or executed locally. Windows CI must establish RED/GREEN and run the new tests.

Focused coverage: `RefreshIntervalSettingsTests`, `RefreshIntervalWindowTests`, `RefreshIntervalRuntimeTests`, `AppearanceSettingsTests`, `SettingsWindowTests`, `SettingsProviderTests`, `DataSourceSettingsTests`, `DataSourceCostTests`, `CodexDataSourceTests`, `SettingsAlertOptionsTests`; existing delivery/clear/privacy tests must remain green. Test deadlines remain at least 60 seconds for asynchronous WPF saves.

Native frame comparison is still required against export frames **09** (Alerts dark), **10** (Appearance light), **11** (threshold error light) and **12** (Providers dark), and all four spec sheets. Exercise save/reopen, Cancel with edits in all pivots, Ctrl+Tab, radio arrows, Space, Enter, Escape, declined consent, failed persistence, live theme changes, minimum size, mixed DPI, high contrast and reduced motion. Check effect preview placement and verify overlays never take focus or intercept clicks.
