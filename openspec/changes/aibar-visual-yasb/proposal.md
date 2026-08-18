# Proposal: Reference-quality AIBar popup and YASB quota bridge

## Intent

Deliver a reference-quality, Windows-native AIBar popup and a safe YASB quota summary without creating a second quota authority. The existing WPF popup will gain a clearer quota-first hierarchy and automatic Windows light/dark theme behavior. A stock external `yasb.custom.CustomWidget` will display both quota windows from a versioned, sanitized file published by AIBar.

AIBar remains the sole authority for quota retrieval, caching, credentials, and private data. The YASB integration is display-only: it reads one bounded projection and uses a fixed `--show` intent to start AIBar or open its popup.

## Problem statement

The current popup has a sound semantic and accessibility baseline, but it still presents as a dense diagnostics window rather than a polished Windows popup. Its palette is fixed-light, the two primary quota windows compete with secondary analytics, and popup placement and surface treatment do not yet reach reference quality.

Users who rely on YASB also lack a supported, privacy-preserving way to see AIBar quotas in the bar. Direct access to AIBar databases or credentials would create unsafe coupling, while a native or forked YASB widget would add unnecessary maintenance and distribution ownership.

## Product outcome

After this change:

- The WPF popup feels like a polished Windows-native surface while preserving keyboard, UI Automation, high-contrast, and non-color-only status behavior.
- Windows application light/dark theme selection is automatic; there is no AIBar theme selector.
- The 5-hour and weekly quotas are permanent, primary elements in both the popup and YASB summary, including explicit placeholders when data is unavailable.
- A stock external YASB CustomWidget can show both quotas, reset information, state, warning, and source age from sanitized AIBar-owned data.
- Left-clicking the widget starts AIBar when needed or opens the existing popup through `AIBar.Desktop.exe --show`.
- Refresh and authentication failures retain the last safe quota values with a visible warning and age. Explicit disable, revoke, or clear-data actions remove or null the exported values instead.

## Scope

### In scope

1. **Sanitized export contract and atomic writer**
   - Publish a versioned current-user JSON projection containing only safe quota presentation fields.
   - Include both permanent quota slots, bounded percentages, reset timestamps, safe state/warning values, publication time, and source retrieval time.
   - Preserve last safe values during operational failure while retaining their original source age.
   - Publish null/disabled values for explicit disable, revoke, or clear-data actions.
   - Use same-directory temporary-file replacement so readers never observe partial JSON.

2. **External activation contract**
   - Add the fixed command-line intent `--show`.
   - If AIBar is already running, preserve the existing single-instance signal and show the popup.
   - If AIBar is not running, complete primary startup and then show the popup once.
   - Do not pass arbitrary payloads, paths, URLs, or commands through the activation channel.

3. **Automatic semantic Windows themes**
   - Provide light and dark semantic resource dictionaries with matching keys.
   - Follow the Windows application theme automatically.
   - Give high contrast precedence and reevaluate theme state when the popup opens.
   - Treat DWM corner, shadow, or dark-mode hints as progressive enhancements with an opaque WPF fallback.

4. **Reference-quality popup structure**
   - Keep the existing WPF implementation and detailed secondary analytics panel.
   - Make the 5-hour and weekly quota cards the unmistakable primary surface.
   - Improve hierarchy, spacing, grouped disclosure, footer stability, placement, DPI/work-area handling, and restrained Windows-native surface treatment.
   - Preserve focus behavior, automation names, textual status, and accessible percentage/reset information.

5. **Stock YASB adapter assets**
   - Supply a bounded reader plus sample `yasb.custom.CustomWidget` configuration and user-configurable CSS example.
   - Permanently render both quota labels, using placeholders when either value is missing.
   - Show stale/unavailable state through text or an icon plus tooltip, not color alone.
   - Read only the sanitized AIBar JSON file and never trigger quota refreshes on the polling interval.
   - Bind left click to a stable local AIBar launcher that passes only `--show`.

6. **Distribution and documentation integration**
   - Include the reader and sample assets in the existing unsigned, self-contained Windows x64 ZIP.
   - Document a stable user-selected install/extract convention without embedding developer or user-specific absolute paths.
   - Update bounded distribution inventory and setup documentation.

### Non-goals

- Cost, spend, trend, forecasting, or ETA features.
- A native, forked, or modified YASB widget.
- A detailed or duplicate analytics panel inside YASB.
- A WinUI rewrite or replacement of the WPF host.
- Installer, signing, updater, package-manager, or public-release work.
- New access to credentials, sessions, prompts, logs, analytics databases, environment-derived roots, or private user data.
- Changes to private quota endpoint behavior, refresh cadence, credential behavior, session access, or support/policy claims.
- A general IPC, HTTP, socket, named-pipe, URL, file, or command execution interface.
- Redesigning the business content of the existing secondary analytics panel.

## Approach and delivery boundaries

Implementation should proceed as six bounded, dependency-aware slices:

1. Sanitized export contract and atomic writer.
2. External `--show` activation contract.
3. Automatic semantic light/dark/high-contrast themes.
4. Reference-quality popup hierarchy, placement, and surface treatment.
5. Stock YASB CustomWidget reader, configuration, CSS example, and fixtures.
6. Existing ZIP distribution and documentation integration.

Each slice should remain independently reviewable and target fewer than 400 changed lines. If a forecast exceeds that boundary, split tests/assets from runtime work rather than combining export, activation, theme, and visual changes. Exact file structure and dependency placement belong to design; domain/application models must not depend on WPF or YASB.

## Affected capabilities

| Capability | Proposed change |
|---|---|
| Popup presentation | Quota-first reference finish, responsive placement, stable grouped secondary content, and progressive Windows surface treatment. |
| Theme and accessibility | Automatic Windows light/dark resources, high-contrast precedence, runtime reevaluation, and preserved keyboard/UI Automation semantics. |
| Startup and single instance | Fixed `--show` intent that starts and opens a primary instance or signals the existing instance. |
| Quota presentation authority | Existing AIBar refresh/cache state remains authoritative and feeds a closed sanitized projection. |
| Privacy controls | Disable, revoke, and clear-data actions null or remove exported quota values rather than retaining stale disclosure. |
| YASB integration | Stock CustomWidget reads validated local output, permanently shows both windows, and opens AIBar on left click. |
| Distribution | Existing ZIP gains documented integration assets; packaging model otherwise remains unchanged. |

## Privacy and security boundaries

- AIBar is the only component allowed to access credentials, private quota responses, internal databases, and detailed remediation state.
- The YASB reader may access only the known sanitized snapshot; it must not recursively inspect `%LOCALAPPDATA%` or discover paths and processes.
- Exported JSON must exclude tokens, account IDs, plans, balances, internal safe codes, raw responses, endpoints, environment variables, database/executable/user paths, session metadata, analytics totals, prompts, logs, and diagnostics.
- Safe state and warning values are closed enums or presentation-safe labels. Exceptions and internal error payloads are never exported.
- `sourceRetrievedAt` determines freshness. `generatedAt` must not make cached source data appear newer than it is.
- Unsupported schemas, malformed data, invalid timestamps, and out-of-range or non-finite percentages fail to safe placeholders.
- Publication is atomic, current-user scoped where feasible, non-elevated, and read-only from YASB's perspective.
- `--show` is a payload-free fixed intent. Existing single-instance behavior must not be weakened, and any kernel-object scope change requires an explicit same-user isolation decision in design.
- No network, real credentials, live endpoint, real user data, or live YASB dependency is required for implementation or validation.

## Dependencies and constraints

- Existing `QuotaRefreshCoordinator`, cached state, and presentation mapping remain the source of truth.
- Existing WPF, tray, single-instance, and activation mechanisms are extended rather than replaced.
- The supplied stock `yasb.custom.CustomWidget` command, formatting, tooltip, CSS-class, interval, and mouse-callback capabilities are assumed available.
- Windows theme detection, high-contrast state, dispatcher affinity, DWM capability variation, DPI, work area, and taskbar placement constrain the popup implementation.
- The manually extracted ZIP has no guaranteed install path; documentation must establish a stable convention users configure once.
- The private quota endpoint remains undocumented and unsupported; this change must not imply stronger reliability or official support.

## Success criteria

The change is successful when all of the following are demonstrably true using synthetic state and isolated local files:

- Both the 5-hour and weekly quota slots are always present in the popup and YASB label, with `--` placeholders when unavailable.
- Current, refreshing, stale, unavailable, disabled, one-window, and no-cache states render without exposing forbidden fields.
- A failed refresh with cached data retains values and visibly reports warning/state and source age; clear, revoke, or disable removes those exported values.
- The exported document is schema-versioned, validates bounded percentages/timestamps, and can be checked against an explicit field allowlist.
- Atomic publication never exposes partial JSON, and a pre-replacement failure leaves the previous complete snapshot intact.
- YASB reader fixtures handle missing, malformed, unsupported-version, future-time, and clock-rollback inputs with safe placeholders and no raw error disclosure.
- Left click using `--show` opens an existing AIBar popup or starts AIBar and opens it once, without accepting an arbitrary activation payload.
- Light, dark, and high-contrast modes select the correct semantic resources; runtime changes and popup-open reevaluation preserve focus and automation semantics.
- Popup validation covers keyboard traversal, UI Automation names, textual stale/error meaning, 100/125/150/200% DPI, work-area clamping, and no clipping in supported layouts.
- The distribution ZIP contains the documented YASB assets, and examples contain no developer or user-specific absolute path.
- Validation performs no private endpoint call and uses no real credential, session, user dataset, YASB process, or live AIBar session.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Windows DWM behavior varies by OS build. | Keep enhancements capability-checked and preserve a fully usable opaque WPF fallback. |
| Theme notifications may be missed or arrive off-dispatcher. | Use an injectable source, marshal changes to the WPF dispatcher, and reevaluate on popup open. |
| Kernel object names or ACLs could weaken same-user activation isolation. | Preserve the current mechanism by default and require an explicit design decision before changing names or ACL scope. |
| Polling may read old but complete data. | Derive age from `sourceRetrievedAt`, retain a visible stale marker, and never infer freshness from file modification time alone. |
| Stale retention could conflict with privacy actions. | Make disable, revoke, and clear-data authoritative over retention and immediately null or remove the export. |
| Atomic replacement and ACL preservation differ across first write, replacement, and watched/open files. | Validate each path in isolated filesystem tests and leave the previous complete snapshot intact on failure. |
| Manual ZIP extraction makes launcher paths fragile. | Document one stable location/convention and fail to a clear unavailable state without process or path discovery. |
| Visual polish could regress accessibility or imply endpoint reliability. | Preserve semantic resources and textual state, validate runtime accessibility, and keep unsupported-service caveats explicit. |
| Static tests cannot prove DPI, clipping, placement, or screen-reader behavior. | Add bounded Windows runtime validation in addition to pure/static checks. |

## Rollback

The six slices are intentionally separable. If an integration causes regressions, disable or remove the YASB export/assets and `--show` entry path without changing the internal quota cache or endpoint behavior. Theme and popup refinements can revert to the existing semantic WPF resources and presentation while retaining the established accessibility baseline. Any rollback must also remove or null the sanitized exported file so obsolete quota values are not left disclosed. The existing unsigned ZIP distribution, tray startup, single-instance authority, refresh cadence, credentials, databases, and secondary analytics remain the stable fallback.
