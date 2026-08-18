# Exploration: Reference-quality AIBar popup and YASB bridge

## Bottom line

Build this change as two bounded product paths sharing one existing quota authority: (1) redesign the WPF popup around semantic, automatically themed Windows resources, and (2) have AIBar publish a minimal atomic JSON projection that a stock `yasb.custom.CustomWidget` reads. Do not let YASB inspect AIBar databases, credentials, logs, analytics sources, session data, or user paths. Use an explicit `AIBar.Desktop.exe --show` activation contract so the same command starts and opens a new instance or signals the existing instance.

The current quota-first baseline at `c6acb10` is a sound accessibility and semantic-resource foundation, not a reference-finish UI. It has two clear quota cards, focus visuals, high-contrast triggers, non-interactive progress tracks, state labels, and automation names. It remains a dense, fixed-light, chrome-free WPF window containing several secondary analytics sections; it has no automatic dark palette, no polished popup placement/surface treatment, and no external sanitized state contract.

## Evidence and constraints

- Repository evidence was inspected from the current project tree and the existing OpenSpec investigations.
- Reference behavior remains pinned to `Finesssee/Win-CodexBar@4e9ab06460a266ee5eea500e7ec5e212b42472a1` and `steipete/CodexBar@9a6c74cfed418ddcd01f9559bb7a80368e5245dd`.
- The repository already contains `.codegraph/`, but no CodeGraph query interface was available in this executor. Exploration therefore used targeted file reads and searches rather than broad enumeration.
- No network, endpoint, credential, live application, user session, or private data access was performed.
- The supplied YASB contract is treated as verified: `yasb.custom.CustomWidget` can execute commands/scripts on an interval, consume JSON/string output, format `label`/`label_alt` and tooltips, apply a CSS class, and run mouse callbacks.

## Current architecture and visual gap

### Popup composition

`App.StartTray` constructs one hidden `MainWindow`, removes taskbar presence and window chrome, then wraps it with `WpfPopoverRuntime`. `TrayHostRuntime.ShowPopover` recalculates quota presentation, requests a popover-open reevaluation, shows the window, and activates it. Deactivation hides it unless an owned dialog is active.

`MainWindow.xaml` is currently one vertically scrolling stack at a fixed 420 DIP width:

1. quota title, freshness, age, and warning branches;
2. separate 5-hour and weekly quota cards;
3. local Codex analytics;
4. OpenCode/Pi local usage with expandable details;
5. disclosures and refresh.

This is quota-first in reading order, but it still feels like a compact diagnostics window rather than a finished native popup. Secondary data creates vertical density and competes with the two primary quota windows. The redesign should preserve the existing detailed panel rather than duplicate it in YASB, but improve hierarchy through a restrained header, compact paired quota summary, deliberate spacing, grouped secondary disclosure, and a stable footer/action area.

### Semantic resources and accessibility

`App.xaml` already defines semantic brushes (`SurfaceBrush`, `CardBrush`, `TextBrush`, `MutedTextBrush`, `AccentBrush`, `FocusBrush`, `ProgressTrackBrush`, and `SecondaryBorderBrush`), spacing values, radius, text styles, a custom non-animated progress template, keyboard focus visuals, and high-contrast triggers. `MainWindow.xaml` supplies automation names for both quota cards, both local-data groups, detail rows, and refresh. Expanders are keyboard focusable and expose expand/collapse behavior. `QuotaVisualDesignTests` checks resource presence, semantics, card availability, focusability, no animation, UI Automation names, and refresh command behavior.

Gaps to close without regressing those properties:

- Most semantic resources contain fixed light colors; there is no dark dictionary or runtime theme service.
- High contrast is handled locally in selected styles, but the complete redesigned surface must use system brushes consistently and must not depend on color alone for stale/error meaning.
- Static XAML assertions are useful but cannot prove contrast, clipping, scaling, theme transitions, popup placement, or all runtime automation relationships.
- Progress bars are deliberately non-focusable and need nearby textual percentage/reset names to remain the accessible source of truth.
- The borderless window has no explored native shadow, corner, title-bar, or backdrop strategy. Visual polish must not require a WinUI rewrite or obscure high-contrast behavior.
- `PopoverPlacement.Place` exists and is unit-testable, but the inspected production path does not apply it when showing the popup. Placement, DPI, work-area clamping, and taskbar edge behavior need an explicit integration seam.

### Reference-level finish with Windows identity

Adapt the references' information hierarchy and cache-first status clarity, not their platform chrome or implementation stack. Recommended visual direction:

- Keep Segoe UI, WPF controls, Windows focus semantics, system accent, and system high-contrast colors.
- Make both quota windows the unmistakable first surface, with permanent labels, percentages, reset text, and state text.
- Use subtle elevation/border treatment and Windows-native corner/shadow APIs only behind capability checks; fall back to an ordinary opaque bordered surface.
- Keep secondary analytics in the existing popup, visually subordinate and collapsible/contained; do not reproduce it in the bar.
- Avoid animation as a dependency. Any decorative motion remains optional and disabled for high contrast/reduced-motion scenarios.

## Startup, single instance, tray activation, and refresh

### Existing flow

- `App.OnStartup` creates `SingleInstanceHost("AIBar")`.
- A secondary process signals the named `AIBar.activate` auto-reset event, disposes itself, and exits.
- The primary process owns `AIBar.mutex`; `TrayHostRuntime` polls `DispatchPendingActivation` every 50 ms and maps activation to `ShowPopover`.
- Tray left click toggles the popup. A tray menu refresh invokes the same `IManualRefreshCommand` used by the popup.
- `ShowPopover` triggers `RefreshTrigger.PopoverOpened`; manual refresh bypasses normal freshness through `RefreshTrigger.Manual`.
- Startup registration already targets `Environment.ProcessPath`, and distribution is currently an unsigned self-contained Windows x64 ZIP.

### Safe external open/start contract

Add one narrow command-line intent: `--show`.

- If AIBar is already running, launching the same executable with `--show` follows the existing secondary-instance signal and the primary shows the popup.
- If AIBar is not running, the primary completes tray construction and then shows the popup once. Current startup does not do this, so merely launching the executable is insufficient for the confirmed YASB behavior.
- Unknown arguments should fail safely or retain ordinary tray startup; they must never become file, URL, command, or payload inputs to the activation channel.
- Keep the existing per-session named-event mechanism rather than adding a socket, HTTP listener, shell process scan, or privileged IPC surface.
- YASB's left-click callback should invoke a stable local launcher command that resolves the installed/extracted AIBar executable and passes only `--show`. It must not read the snapshot to discover an executable path.

The final design should examine kernel-object naming/ACL scope for same-user isolation. The current names are not user-qualified in source; Windows session namespace behavior reduces cross-session collision, but explicit current-user ACLs or a stable per-user suffix would make the boundary clearer if this is changed. Do not weaken single-instance takeover and abandoned-mutex behavior already covered by `HostPrimitivesTests`.

## Quota authority and sanitized YASB snapshot

### Existing state

`QuotaRefreshCoordinator` is the sole refresh/cache authority. It loads and transactionally updates `quota.db` through `SqliteQuotaSnapshotStore`, preserves the last snapshot when refresh fails, evaluates a ten-minute freshness threshold, coalesces work, and publishes `QuotaRefreshState`. `QuotaPresentationMapper` already derives current/loading/stale/offline/missing-credential/degraded/unavailable states, safe warning labels, cached age, both quota windows, and reset countdowns. `QuotaPresentationHost` publishes changes on the WPF dispatcher.

YASB must not read `quota.db`. SQLite is an internal persistence schema, can be WAL-backed/locked/migrated, and exposes more coupling than an external display needs. It must also never read `analytics.db`, `local-usage.db`, settings, logs, Codex roots, credential files, session JSONL, environment-derived paths, or diagnostic payloads.

### Recommended external contract

AIBar should write a dedicated current-user file such as `%LOCALAPPDATA%\AIBar\yasb-quota.json`. The filename/location is installation documentation, not data embedded in the JSON. Use a versioned, deliberately closed projection:

```json
{
  "schemaVersion": 1,
  "generatedAt": "2030-01-02T03:04:05Z",
  "state": "current",
  "warning": null,
  "sourceRetrievedAt": "2030-01-02T03:04:00Z",
  "fiveHour": {
    "percentageUsed": 42,
    "resetAt": "2030-01-02T08:00:00Z"
  },
  "weekly": {
    "percentageUsed": 20,
    "resetAt": "2030-01-08T00:00:00Z"
  }
}
```

Contract rules:

- Allowed top-level state values should be a small safe enum such as `current`, `refreshing`, `stale`, `unavailable`, and `disabled`.
- `warning` should be a presentation-safe enum/label, not an exception, endpoint response, internal `SafeCode`, path, account ID, plan, credential status detail, or log fragment.
- Quota objects contain only bounded percentages and reset timestamps; missing windows are `null`, never omitted through accidental serializer behavior.
- `sourceRetrievedAt` identifies the age of the last successful safe value. `generatedAt` identifies file publication and must not make old source values appear fresh.
- During refresh or a transient/auth/service failure, preserve the last safe windows, publish `refreshing` or `stale`, and retain the old `sourceRetrievedAt`. YASB computes/displays age from that timestamp and shows a visible stale marker.
- With no successful value, publish `unavailable` with both windows `null` so the widget still renders permanent 5h and weekly placeholders.
- Explicit disable/revocation and “Clear AIBar Data” should not preserve exported quota values: publish `disabled` with null windows or remove then recreate the sanitized disabled snapshot. Stale preservation is for operational failure, not for overriding a user privacy action.
- Reject NaN/out-of-range percentages, invalid/future-inconsistent timestamps, and unsupported schema versions at the projection boundary.
- Write UTF-8 without secrets to a temporary file in the same directory, flush/close it, then atomically replace or rename over the destination. A failed write leaves the previous complete snapshot intact; it must never expose partial JSON.
- Restrict the directory/file to the current user where feasible. Do not elevate, use machine-wide storage, or grant YASB write authority.
- Snapshot deletion/replacement belongs in AIBar's clear-data inventory and distribution documentation.

A small application-layer projector/writer should subscribe to authoritative state changes. It should not introduce another refresh loop or make JSON publication part of quota retrieval success; publication failure is a local integration warning and must not corrupt AIBar's cache or popup.

## YASB CustomWidget integration

Use stock `yasb.custom.CustomWidget`; do not fork YASB, add a native Python widget, or build another detailed panel.

Recommended adapter shape:

- Ship a small local reader script or command that reads only `yasb-quota.json`, validates `schemaVersion`, and emits the exact JSON/string fields used by YASB formatting.
- `label` permanently includes both windows, for example `5h 42% · 7d 20%`. Missing values remain visible as `5h -- · 7d --`.
- A stale state adds an unmistakable text/icon marker supported by tooltip text; do not rely only on CSS color.
- Tooltip includes each reset time/countdown, state, last successful freshness timestamp, and stale age. It contains no endpoint, credential, root, session, database, or exception detail.
- Left click invokes the stable AIBar launcher with `--show`. Other callbacks are optional; a detailed YASB alternate panel is out of scope.
- CSS is supplied as an example class and remains user-configurable under normal YASB styling. AIBar should not overwrite the user's stylesheet.
- Reader failures, missing files, partial legacy files, unsupported schema, clock rollback, and AIBar not yet started produce placeholders plus a clear unavailable/stale tooltip; they never erase the last complete AIBar file.
- Polling interval should be configurable and modest. YASB reads local sanitized state only; it does not trigger quota refreshes on every interval.

Because the current distribution is a manually extracted ZIP, path stability is the main integration burden. A sample config cannot assume a developer checkout or embed a user-specific absolute path. The proposal/design should choose either a documented install/extract location or a bundled launcher/reader location that users reference once in YASB configuration. Installer, updater, signing, and native YASB package management remain separate decisions.

## Automatic Windows theme strategy

Use automatic Windows application theme with no manual selector:

1. Define light and dark semantic resource dictionaries with identical keys.
2. At startup, detect high contrast first. In high contrast, resolve system brushes and avoid custom backdrop/elevation assumptions.
3. Otherwise read the current Windows app theme (`AppsUseLightTheme`) through a small injectable theme source and merge the corresponding dictionary.
4. Listen for Windows preference/theme changes (for example `SystemEvents.UserPreferenceChanged`) and update resources on the WPF dispatcher.
5. Reevaluate when the popup opens to recover from missed notifications or Explorer/session transitions.
6. Apply any supported DWM dark-mode/corner/shadow hint only as progressive enhancement; failure leaves a fully usable opaque WPF surface.

Central dictionary replacement is safer than mutating individual brushes throughout controls. It also gives tests a deterministic light/dark/high-contrast matrix. Theme changes must preserve keyboard focus, automation names, text contrast, and visible state labels.

## Product states and edge cases

| Condition | Popup | YASB |
|---|---|---|
| First start, no cache | Permanent quota placeholders, loading/unavailable guidance | `5h -- · 7d --`, unavailable tooltip |
| Current complete snapshot | Both quota windows emphasized | Both values, resets, current state |
| Only one service window supplied | Existing window plus explicit unavailable placeholder | Both slots remain; missing slot is `--` |
| Refresh in progress with cache | Keep values and show refreshing | Keep values and mark refreshing/stale age source |
| Transient network/service failure | Keep cached values, visible warning and age | Keep values, explicit stale marker and age |
| Authentication/permission failure with cache | Keep values but clearly stale; popup owns remediation detail | Safe stale warning only; no credential detail |
| No cache plus failure | Unavailable placeholders and safe guidance | Both placeholders and unavailable tooltip |
| Integration disabled/revoked | Disabled state; no previous quota disclosure | Null placeholders, disabled state |
| Clear AIBar Data | Remove internal and exported values atomically | Missing/disabled placeholders on next poll |
| Clock/time-zone change | Recalculate countdown/age without changing source timestamp | Reader recalculates age; negative age clamps to zero and warns if invalid |
| Reset passes before refresh | Show reset due/refreshing rather than negative countdown | Zero/due countdown; never infer a new quota value |
| Theme/high contrast changes while hidden | Apply before next show | YASB follows its own CSS/theme behavior |
| Explorer/taskbar recreation | Recreate tray and retain one popup authority | No effect beyond next callback/poll |
| AIBar moved or replaced | Documented launcher path must be updated or remain stable | Clear unavailable result; no process/path discovery |
| Malformed/partial JSON | AIBar's atomic write should prevent it; retain prior file on write failure | Reader validates and emits safe placeholders, never raw parse text |

## Candidate file and symbol map

| Area | Existing candidate | Likely responsibility |
|---|---|---|
| WPF composition | `src/AIBar.Desktop/MainWindow.xaml`, `MainWindow.xaml.cs` | Reference-quality hierarchy, popup surface behavior, automation relationships |
| Semantic theme | `src/AIBar.Desktop/App.xaml` plus new light/dark dictionaries | Shared resource keys, high-contrast-safe styles |
| Theme runtime | `src/AIBar.Desktop/App.xaml.cs` or a focused desktop theme service | Detect/listen/apply Windows theme without user setting |
| Startup/activation | `App.OnStartup`, `App.StartTray`, `SingleInstanceHost`, `TrayHostRuntime.ShowPopover` | `--show`, first-instance show, existing-instance signal |
| Popup placement | `PopoverPlacement`, `WpfPopoverRuntime` | Work-area/DPI placement and progressive native chrome |
| Quota source | `QuotaRefreshCoordinator`, `QuotaRefreshState`, `QuotaPresentationMapper`, `QuotaPresentationHost` | Sole authority and existing safe state derivation |
| Export boundary | New application/domain projection and atomic file writer composed by `App.CreateComposition` | Versioned sanitized snapshot; no database exposure |
| Clear/revoke | `ClearAiBarDataService`, `NativeSettingsCommands`, private-integration operations | Remove/null exported values on explicit privacy action |
| YASB assets | New bounded docs/sample config, reader, and CSS example | Stock CustomWidget integration and launcher command |
| Distribution | `scripts/Publish-Deterministic.ps1`, `docs/private-beta.md`, `AIBar.Desktop.csproj` | Include integration assets and document stable paths without claiming installer support |
| Tests | `QuotaVisualDesignTests`, `QuotaPresentationTests`, `HostPrimitivesTests`, `HostRuntimeTests`, store/clear/distribution tests | Theme, activation, projection, atomicity, privacy, widget-reader fixtures |

Exact new filenames and namespaces belong in design, after deciding whether the export projector lives in `AIBar.Application` with a filesystem adapter in `AIBar.Desktop` or entirely behind an application interface. Preserve dependency direction: domain/application models must not depend on WPF or YASB.

## Security and privacy boundaries

- AIBar remains the only component allowed to read credentials and private quota responses.
- YASB receives no bearer token, account ID, plan, credit balance, internal safe code, raw response, endpoint, environment variable, database path, executable path, user/profile/root path, session metadata, analytics totals, logs, prompts, or diagnostics.
- The widget reader gets read-only access to one known sanitized file and should not recursively inspect `%LOCALAPPDATA%`.
- JSON publication must not log serialized payloads or exceptions containing paths. Diagnostics may use fixed event names and exception types only under existing redaction rules.
- Do not expose a general IPC command surface. `--show` is a fixed intent with no payload.
- Explicit disable/revoke/clear overrides stale-value retention.
- The private undocumented quota endpoint remains an existing AIBar risk; this change must not expand its use, call cadence, credential behavior, or policy claims.

## Testing seams

- Pure projection tests: every `QuotaRefreshState` maps to a schema-v1 sanitized document; property-name allowlist proves forbidden data cannot appear.
- Atomic writer tests in isolated temporary directories: complete replacement, previous-file survival on pre-replace failure, cancellation, concurrent updates, UTF-8/schema validation, clear/revoke behavior.
- Reader fixture tests: current, refreshing, stale, unavailable, disabled, one-window, unsupported version, malformed JSON, missing file, future timestamp, and clock rollback.
- Activation tests: `--show` when primary, secondary, not running, already visible, hidden, and during startup; no arbitrary argument payload enters IPC.
- Theme tests with an injected source: light, dark, high contrast precedence, runtime transition, missed notification recovered on show, and dispatcher affinity.
- WPF/UI Automation tests: names, reading order, focus traversal, percentage/reset text, stale warning not color-only, 100/125/150/200% DPI layout, narrow work area, and no clipping.
- Popup placement tests should extend existing pure `PopoverPlacement` coverage and add one bounded production-adapter test.
- Distribution tests should prove the ZIP contains the documented reader/sample assets and that examples contain no developer/user absolute path.
- All tests use synthetic state and temporary directories only; no real credentials, endpoint, user data, YASB process, or live AIBar session is required.

## Alternatives and tradeoffs

1. **Recommended: atomic sanitized file plus stock CustomWidget.** Lowest coupling, inspectable contract, easy stale preservation, no listener. Polling latency and path documentation are acceptable.
2. **Named pipe/loopback service.** Fresher push/command semantics, but adds ACL, lifetime, protocol, firewall/support, and attack-surface complexity. Not justified for read-mostly bar state; retain only the existing fixed activation event.
3. **YASB reads `quota.db`.** Fewer AIBar writes, but violates authority/privacy boundaries and couples YASB to SQLite locking, migrations, WAL, and internal schema. Reject.
4. **Native/forked YASB widget.** Better framework integration but creates Python/YASB maintenance and distribution ownership explicitly ruled out. Reject.
5. **WinUI rewrite for visual finish.** Could offer newer surfaces, but duplicates the WPF host and risks accessibility/lifecycle regression. Improve the existing WPF shell progressively instead.
6. **Manual AIBar theme selector.** Predictable but contradicts the confirmed automatic-Windows-theme decision and adds settings/state. Reject.

## Recommended bounded slices

Keep every implementation/review slice below 400 changed lines; exact forecasts must be recalculated during tasks.

1. **Sanitized export contract and atomic writer:** schema/projection, filesystem seam, state publication, clear/revoke semantics, and synthetic tests. No UI or YASB assets.
2. **External activation contract:** parse `--show`, first-instance show, existing-instance signal preservation, tests, and minimal launcher documentation.
3. **Automatic semantic themes:** split identical-key light/dark dictionaries, high-contrast precedence, injectable Windows theme source, runtime switching tests.
4. **Popup reference-finish structure:** quota-first XAML hierarchy, responsive placement/surface treatment, accessibility/runtime tests. Keep secondary analytics, but do not redesign their business content.
5. **YASB adapter assets:** safe reader, sample CustomWidget config, configurable CSS class, tooltip/stale behavior, fixture tests, and no absolute paths.
6. **Distribution/documentation integration:** include assets in the existing unsigned self-contained ZIP and update smoke/inventory documentation without starting installer, signing, updater, or public-release work.

If any slice forecasts above 400 lines, split tests/assets from runtime by a dependency-safe boundary rather than mixing theme, activation, export, and visual redesign in one review.

## Risks and unresolved design checks

- The exact `c6acb10` diff was not available through a Git command in this executor; the current files and supplied baseline identity were used. Proposal/design should verify the clean baseline and ancestry before planning edits.
- Windows dark-mode/DWM APIs vary by OS build. Treat them as optional and preserve an opaque fallback.
- Registry/theme notifications can be missed or arrive off-dispatcher; startup and popup-open reevaluation are required.
- Named kernel object scope and ACLs need an explicit same-user decision before changing activation names.
- A manually extracted ZIP has no guaranteed stable install path. YASB setup quality depends on a clear supported location or stable bundled launcher convention.
- Atomic replacement semantics and ACL preservation differ when the target does not yet exist or is watched/opened. Tests must cover first write and replace failures.
- Widget polling can observe old but complete data. Freshness must derive from `sourceRetrievedAt`, never file modification time alone.
- Persisting last safe values is useful during failure but conflicts with explicit revoke/clear unless privacy actions null/remove the export immediately.
- High contrast, text scaling, taskbar edge placement, and screen-reader behavior need bounded Windows runtime validation; static XAML tests alone are insufficient.
- The private endpoint is still undocumented and unsupported. Visual polish must not imply stronger reliability or official support.

## Recommendation for proposal

Proceed to proposal only after interactive approval. The proposal should retain the six bounded slices, make the JSON field allowlist and revoke/clear behavior explicit, keep stock YASB and automatic Windows theme as fixed decisions, and state that cost, trend, ETA, detailed YASB panels, native YASB code, installer/signing/updater work, and new credential/session access are out of scope.
