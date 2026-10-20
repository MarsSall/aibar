# Settings

Open **Settings…** from the panel or tray. One Settings window is reused. It follows Windows light, dark and high-contrast themes, with a custom 32-DIP title bar and **Appearance / Alerts / Providers** pivots. The default size is 520 × 600 DIPs (minimum 460 × 480); content scrolls without moving the footer.

Changes apply when you select **Save**. **Cancel**, Escape and closing the window discard edits that have not been saved. Save leaves the window open and announces success or failure. Preview buttons do not save preferences.

## Keyboard and accessibility

- **Ctrl+Tab** cycles pivots; **Ctrl+Shift+Tab** cycles backwards.
- **Tab / Shift+Tab** move through controls; **Space** toggles checkboxes and switches.
- **Arrow keys** move through radio groups (panel colors, YASB colors and Claude source).
- **Enter** saves, unless validation fails; **Escape** cancels.
- The opacity slider supports arrows in 5-point steps and Page Up/Down in 10-point steps.

Selected pivots have an accent underline and semibold text. Controls have named automation peers and keyboard focus rings. Errors are announced inline and are never represented by color alone. High contrast uses system colors; toggle animation is omitted when Windows client-area animations are off or high contrast is active.

## Appearance

**Follows Windows** reports the current theme and points to Windows Settings › Personalization › Colors. There is no independent app-theme override.

| Choice | Default | Options |
| --- | --- | --- |
| Background opacity | 100% | 30–100%, step 5. Applies to panel and notification background brushes, not text, bars, tabs or buttons. |
| Panel bars | Brand colors | Brand colors, or Usage level: green below 75%, yellow from 75%, red from 90%. |
| YASB colors | Usage level | Existing usage ramp, or Brand colors. |

Color radio cards show illustrative bars and YASB previews, not live quota. The usage-color boundaries are independent of alert thresholds. YASB picks up the saved preference on its next normal refresh; unavailable quota remains gray and the six output fields/export schema are unchanged.

Opacity is persisted even when Windows high contrast or disabled transparency forces surfaces to 100%. Settings itself stays opaque. The panel and notification consumers must apply alpha only to their background brush and use an opaque fallback when transparency is unavailable.

## Alerts

Enable **Window resets**, **Thresholds** and **Runs out before reset** independently. Each switch shows **On/Off** as well as its position.

Thresholds are comma-separated, strictly ascending whole numbers from **1 to 100** (default **75, 90, 100**). Valid values have corresponding ticks beside the input. Duplicate, descending, fractional, empty and out-of-range entries show an icon and inline error, a danger input border, a dot on the Alerts pivot, and **Fix the thresholds to save** in the footer; Save is disabled even when another pivot is selected. Descending values identify the offending pair.

Three independently selectable effect tiles have a preview button and a checkbox:

- **Windows notification**: preview uses the host's normal notification delivery entry point; no settings are changed.
- **Edge glow at limit**: preview uses the existing non-activating, click-through monitor overlays.
- **Confetti on reset**: defaults off; preview uses the existing confetti overlays.

Delivery policy, freshness checks, consent checks and deduplication remain owned by the alert runtime, not the Settings view. Preview does not create alert-state entries. See [Quota alerts](alerts.md) for delivery and persistence rules. The tray **Alerts** submenu retains its existing toggles; **Configure alerts…** opens Settings.

## Providers

**Codex** and **Claude** switches stage their enabled state until Save. Official OpenAI and Claude marks are retained rather than using the design export's placeholders.

Enabling a disabled provider shows the existing provider-specific consent disclosure. Declining either required disclosure performs no provider/source writes and leaves the draft open. Disabling requires no prompt. Runtime commands remain the only consent writers; Settings never reads or writes credentials.

Claude source radio cards select:

- **This PC** — existing Claude Code sign-in on this computer.
- **Other machine** — the existing synced usage file.
- **Automatic** — the existing runtime's automatic source-selection policy (including its existing remote-file precedence and freshness behavior).

The selected source is saved through `ClaudeQuotaRuntime.SetSourceAsync` before granting consent. It does not change the remote quota payload or its reader contract. Without a provider-command binding, provider controls are explicitly unavailable rather than silently changing only the view.

## Persistence and clearing

`%LOCALAPPDATA%\AIBar\appearance.json` remains schema 1:

```json
{"schema":1,"barColors":"brand","yasbColors":"usage","backgroundOpacity":100}
```

Existing schema-1 files without `backgroundOpacity` load with **100**. Invalid numeric opacity falls back to 100 without discarding valid color choices. Saves reject out-of-range/non-step opacity and unknown color modes. Successful writes atomically replace the destination via a same-directory temporary file and raise `AppearanceSettings.Changed` with all three values; failed/cancelled writes do not publish a change.

**Clear AIBar Data** drains appearance writes with alert work, removes the existing appearance/alert files, and publishes the actual post-clear appearance (defaults on success, surviving values after rollback). Clear/dispose paths never await the WPF dispatcher.

Saving multiple sections is **not a cross-file transaction**. A later persistence failure can leave an earlier section saved. The status reports that not all changes were saved and allows retry; this also applies to provider runtime commands.

## Integration contract

`SettingsWindowHost(AppearanceSettings, QuotaAlertObserver, SettingsProviderCommands?, Action? previewNotification)` is the UI-thread entry point. The optional parameters preserve existing call sites. Alert saves still run on the observer's serialized queue.

`App.xaml.cs` constructs `SettingsProviderCommands` from the existing `NativeSettingsCommands` and `ClaudeQuotaRuntime`, and passes the tray's notification entry point for preview. Codex uses `EnablePrivateIntegrationAsync` / `DisablePrivateIntegrationAsync`; Claude uses `GrantConsentAsync` / `RevokeConsentAsync` and `SetSourceAsync`. No `HostRuntime.cs` behavior is replaced.

The appearance instance remains shared with startup composition and the clear lifecycle. Existing panel bar-color mapping stays intact. The parallel panel/notification units must load `BackgroundOpacity` initially and subscribe to `Changed` for updates, posting to their dispatcher **without awaiting it**. Do not apply `Window.Opacity`: content must stay opaque.

Only `Settings*` tokens were added to `Semantic.Light.xaml`, `Semantic.Dark.xaml` and `Semantic.HighContrast.xaml`. Union these with panel and notification tokens during integration. The panel-owned exact semantic-key assertions must include these new keys (or validate the independently owned prefix sets separately). `SettingsProviderMarks.xaml` contains Settings-prefixed copies of the same CC0 official geometries as the panel.

## Windows verification

No local .NET/PowerShell verification is available in the Linux worktree. The parent runs tests-only RED followed by implementation GREEN in Windows CI.

Focused coverage: `AppearanceSettingsTests`, `SettingsWindowTests`, `SettingsProviderTests`, `SettingsAlertOptionsTests`; existing delivery/clear/privacy tests must remain green. Test deadlines remain at least 60 seconds for asynchronous WPF saves.

Native frame comparison is still required against export frames **09** (Alerts dark), **10** (Appearance light), **11** (threshold error light) and **12** (Providers dark), and all four spec sheets. Exercise save/reopen, Cancel with edits in all pivots, Ctrl+Tab, radio arrows, Space, Enter, Escape, declined consent, failed persistence, live theme changes, minimum size, mixed DPI, high contrast and reduced motion. Check effect preview placement and verify overlays never take focus or intercept clicks.
