# AIBar foundation design

## Decision

Build AIBar as a **single-process, Windows-native .NET 8 desktop application using WPF**, with a thin tray/popover shell and framework-independent application/domain services. Use `HttpClient` for the private quota adapter, `System.Text.Json` streaming for Codex JSONL, and SQLite for AIBar-owned cache, checkpoints, daily aggregates, and settings.

Native Windows lifecycle and integration take precedence over pixel-perfect macOS imitation. The visual target is nevertheless a polished, modern, CodexBar-inspired experience: preserve the reference application's compact information hierarchy, clarity, spacing, status visualization, and perceived quality while expressing them through Windows typography, interaction, accessibility, and window behavior.

This is a Windows-only utility whose highest-risk work is credential access, private HTTP integration, incremental file processing, and lifecycle behavior—not cross-platform UI. WPF has the mature tray/window ecosystem and direct Windows integration identified by the feasibility investigation, supports Windows 10 and 11 without a Windows App SDK deployment dependency, and keeps the trusted implementation in one managed runtime.

### Stack comparison

| Criterion | Windows-native WPF/.NET 8 | Tauri 2 + Rust + web UI | Decision impact |
|---|---|---|---|
| Windows 10/11 tray and startup | Mature `NotifyIcon`/Win32 interoperability and direct registry/startup integration | Native tray exists, but popover focus, WebView2 lifecycle, IPC, and plugin behavior add layers | WPF is simpler for the MVP's primary surface |
| Security boundary | Credentials, HTTP, persistence, and UI remain in one typed process; no web IPC contract | Rust can strongly isolate secrets, but commands/events must cross a webview IPC boundary and frontend logging needs separate controls | WPF reduces credential and diagnostic exposure paths |
| Local analytics | .NET provides streaming JSON, async file I/O, cancellation, SQLite libraries, and testable pure services | Rust is excellent for streaming and correctness, but adds a second UI language/toolchain | No demonstrated workload requires Rust performance |
| Packaging footprint | Self-contained or framework-dependent Windows package; no WebView UI dependency | Requires WebView2 availability/bootstrap and Rust/web build toolchains | WPF has fewer runtime variables |
| UX | Older UI model, but sufficient for a compact tray popover; polish must be deliberate | Web UI enables rapid styling and reference architecture reuse | Styling advantage does not offset lifecycle/IPC complexity |
| Evidence from investigations | Exploration calls WPF mature and fast for Windows integration | Win-CodexBar proves Tauri/Rust feasibility and good provider/cache separation, but explicitly does not establish it as AIBar's default stack | Reuse the boundaries, not the stack coupling |

WinUI 3 was also considered as the modern Windows-native option. It is not selected because the MVP does not need Windows App SDK-only features, while its deployment and tray interop complexity provide no compensating product value. A later migration remains possible because presentation depends on application contracts rather than persistence or provider implementations.

## Architectural shape

Use one process and four dependency-directed layers:

```text
WPF presentation + tray host
        ↓ commands / immutable view state
Application coordinators
  QuotaRefreshCoordinator | AnalyticsScanCoordinator | Settings/ClearData
        ↓ ports
Domain models and policies
  QuotaSnapshot | Freshness | ScanCoverage | TokenDelta | DailyUsage
        ↑ adapters
Codex auth reader | private quota HTTP | JSONL scanner | SQLite | startup | clock
```

The UI never reads Codex files, sends HTTP requests, or executes SQL. Coordinators serialize work and publish immutable state snapshots through in-process events on the WPF dispatcher. Domain code has no WPF, filesystem, HTTP, or SQLite dependency.

### Visual direction

Use a custom WPF design system rather than default control styling. Define semantic design tokens for color, typography, spacing, radii, elevation, motion, and state before composing screens. Prefer Windows-native system fonts and accessibility behavior, with light/dark themes derived from semantic roles rather than hard-coded colors.

The popover should approach CodexBar's visual polish through compact quota cards, clear hierarchy, restrained gradients, progress visualization, aligned numeric data, subtle elevation, and short state transitions. It must not reproduce macOS traffic lights, menu-bar chrome, typography, blur behavior, or interactions that conflict with Windows conventions. Acrylic/Mica-like effects are progressive enhancement only: readability, contrast, performance, and Windows 10 fallback remain mandatory.

Keep presentation replaceable and testable through view models and immutable UI state. Custom visuals must preserve keyboard navigation, visible focus, screen-reader names, reduced-motion behavior, high-contrast compatibility, and DPI scaling. Visual acceptance should include reference screenshots at representative Windows 10/11 scale factors plus interaction checks; screenshot similarity alone cannot replace accessibility and lifecycle tests.

### Primary contracts

| Contract | Responsibility |
|---|---|
| `ICodexCredentialSource` | Resolve the one supported Codex root and return a short-lived access credential plus optional account identifier without modifying source files |
| `IQuotaProvider` | Fetch and decode required quota windows; classify auth, permission, network, service, and malformed-schema failures; isolate `/backend-api/wham/*` details |
| `IQuotaSnapshotStore` | Atomically load/save the last successful normalized snapshot and timestamp; never store credentials or raw response bodies |
| `IUsageScanner` | Discover supported session/archive layouts, stream appended JSONL, return deltas, checkpoint updates, and coverage warnings |
| `IAnalyticsStore` | Transactionally persist checkpoints, scan provenance, and daily/model aggregates |
| `IPricingCatalog` | Provide immutable version/date, model rates, and unsupported-model results |
| `IStartupRegistration` | Read and set effective per-user Windows startup registration |
| `IClock` / `ILocalTimePolicy` | Make freshness, countdowns, local-day normalization, DST behavior, and tests deterministic |

## Runtime and data flow

### Process, tray, and popover

AIBar is a single-instance, per-user process. A named mutex prevents duplicates; a second launch signals the existing instance to show the popover and exits. Closing the popover hides it rather than terminating AIBar. An explicit **Exit AIBar** tray command cancels background work, checkpoints completed scan work, closes SQLite, and exits.

The tray icon is always meaningful:

- current: primary 5-hour percentage is rendered;
- stale: the retained percentage is visually marked stale and its tooltip states the last success time;
- loading: an activity state does not erase the prior snapshot;
- unavailable/error: no percentage is fabricated when no valid snapshot exists.

Left-click toggles a borderless WPF popover positioned against the taskbar work area; it closes on deactivation unless an owned dialog is active. Right-click opens native commands: Open, Refresh, Start with Windows, Clear AIBar Data, and Exit. Explorer/taskbar recreation is handled by recreating the tray icon. Display/DPI/taskbar changes trigger repositioning, not process restart.

Startup sequence is cache-first and non-blocking: open/migrate the database, render cached state, create the tray, then schedule quota refresh and bounded analytics scanning. Network and scanning never run on the UI thread.

### Credential handling and private endpoint access

1. Resolve `%CODEX_HOME%` when non-empty; otherwise the current user's `.codex` root. Only the single supported root is used.
2. Open the supported auth file read-only with sharing that tolerates Codex replacement. Parse only the access credential and optional account identifier. Do not deserialize refresh tokens, prompt data, or unrelated fields into long-lived models.
3. Hold the credential in a request-scoped disposable object, create the authorization header immediately before sending, and release references after completion. AIBar does not cache, refresh, copy, or persist the credential.
4. Send HTTPS only to a compile-time allowlisted `https://chatgpt.com/backend-api` origin. Redirects are disabled so bearer credentials cannot cross origins. Apply bounded connect/request timeouts, cancellation, and conservative retry: no automatic retry for 401/403 or malformed payload; at most one jittered retry for transient connection/408/429/5xx failures while honoring `Retry-After`.
5. Fetch `/wham/usage` as the primary operation. Decode into a version-neutral DTO and validate percentages/reset timestamps before mapping to domain data. Fetch optional `/wham/rate-limit-reset-credits` separately; its failure cannot invalidate primary windows.
6. Map 401 to authentication restoration guidance, 403 to permission, 429/5xx to service, transport/timeouts to network, and decoding/required-field failures to malformed response. UI/log errors carry safe codes and summaries, never raw bodies, headers, tokens, or sensitive account IDs.

The private adapter is feature-gated by one application setting/build policy so distribution can disable it without affecting local analytics. Release remains blocked on current policy/legal review; the design does not interpret repository evidence as permission.

### Quota refresh, cache, and freshness

`QuotaRefreshCoordinator` owns a single asynchronous refresh task. Poll, popover-open, resume, and manual triggers join the in-flight task rather than overlap. Manual refresh bypasses freshness suppression but not the concurrency gate.

A successful primary response is normalized and atomically committed before state publication. The cache stores only percentages, reset instants/durations, optional non-secret details, source classification, schema adapter version, and retrieval time. It never stores raw payloads or credentials.

Freshness is a configurable product policy with a conservative initial value documented alongside implementation; the coordinator also schedules no earlier than a minimum poll interval and may schedule near the next reset. Sleep/resume and clock changes force freshness re-evaluation. Failures retain the previous snapshot with its original timestamp and an error overlay; once beyond threshold it is stale, never current. Clearing data removes the snapshot immediately and cancels active refresh publication.

### Incremental analytics and persistence

Use one SQLite database under the per-user local application data directory, opened with foreign keys and WAL mode. Logical tables are:

- `schema_meta`: schema, parser, timezone-policy, and pricing versions;
- `file_checkpoint`: canonical-path keyed identity, size, last-write time, byte offset, parser state needed for cumulative deltas, and last successful scan;
- `daily_model_usage`: local day, timezone ID/offset provenance, model or `Unknown`, and non-negative input/cached-input/output totals;
- `scan_run`: start/end, files discovered/read/skipped/deferred, warnings, cancellation, and coverage status;
- `quota_snapshot`: the single normalized last-success cache;
- `settings`: non-secret settings only.

Paths are needed transiently to reopen source files but are sensitive. Persist a keyed, application-local path fingerprint plus the minimum reopen/checkpoint locator required by the scanner; never expose paths in UI or normal logs. Do not persist project/session names. Database files inherit current-user ACLs; no machine-wide storage or fallback is used.

Scanning proceeds in bounded batches:

1. Discover only the configured root's supported `sessions` and sibling `archived_sessions` layouts.
2. Compare file identity, length, and modification metadata with checkpoints. Unchanged files contribute nothing.
3. Stream from the validated byte offset. If identity changed, length shrank, parser semantics changed, or checkpoint state is inconsistent, invalidate and rebuild that file's contribution using a transactional replacement strategy rather than adding it twice.
4. Parse only timestamps, trustworthy model evidence, and token counters. Never bind or store prompt/response fields. Malformed complete lines are skipped with coverage warnings; an incomplete final line is deferred until a later append.
5. For cumulative records calculate each component as `max(0, current - previous)`; clamp last-event values independently to zero. Missing/untrusted model evidence maps to `Unknown`.
6. In one transaction, merge deltas and advance the checkpoint. Cancellation before commit leaves both unchanged.

Daily keys use the Windows timezone active for the event conversion. Persist event UTC-derived day provenance, Windows timezone ID, and observed offset. A later timezone-policy change increments the policy version and requires explicit rebuild rather than silently moving historical totals.

`total tokens = input + cached input + output`; this exact formula determines most-used model. Cached input is a separate component and is included once in the ranking formula. Pricing applies component-specific rates from a checked-in, versioned catalog. Unknown models have no fallback rate and produce an incomplete-estimate warning. Stored aggregates remain token facts; estimates are calculated with and display the selected catalog version/date so repricing cannot masquerade as billed history.

MVP trends use named daily windows only (initially a 7-complete-local-day average and day-over-day comparison). Linear quota exhaustion uses a documented observed quota-utilization rate within the current service window and the latest service-reported remaining percentage/reset; zero/negative rate, stale quota, invalid reset, or insufficient observations yields unavailable. It is labeled a simple linear estimate, never a prediction.

## Failure and degraded modes

| Failure | Retained capability | User-visible behavior |
|---|---|---|
| Auth missing/malformed/expired | Local analytics | Auth/unavailable state; direct user to restore Codex CLI session |
| 401 / 403 | Cached quota if present, local analytics | Authentication or permission error; cached value marked stale as required |
| Network, timeout, 429, or 5xx | Cached quota and all local features | Classified error, retry timing, original snapshot timestamp retained |
| Primary schema drift | Local analytics and prior snapshot | Malformed/unsupported integration state; never map guessed fields |
| Optional detail failure | Primary quota | Optional section unavailable only |
| SQLite open/migration failure | Tray and live quota request may operate in-memory for the session | Persistence/analytics unavailable; no destructive auto-recovery; clear/rebuild offered only with confirmation |
| Unreadable/changing/truncated JSONL | Other files and prior aggregates | Partial coverage with counts; incomplete tail deferred |
| Scan cancellation or shutdown | Last committed aggregates/checkpoints | Scan marked cancelled; resume later without recounting |
| Unsupported model/pricing | Token totals | `Unknown`/unsupported warning; no invented price |
| Windows startup registration denied | Core application | Toggle reverts to effective state with permission/unsupported message |

## Packaging, startup, and compatibility

Target x64 Windows 10 and 11 initially; add arm64 only as a separately verified package. Publish a signed, per-user MSIX package when signing/distribution is available, with a documented unpackaged self-contained build for development and recovery. CI produces deterministic release artifacts and a software bill of materials; release notes disclose the private integration and its disablement risk.

Use a per-user startup registration abstraction. Packaged builds use the supported MSIX startup-task mechanism where available; unpackaged builds use a per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry with quoted executable path and a `--startup` argument. Never require elevation, scheduled tasks, services, or machine-wide registration. The UI always reads back effective state after mutation.

Automated packaging smoke tests cover clean install, upgrade with database migration, startup toggle, launch-at-login argument, single-instance activation, uninstall, and user-data retention/removal expectations. Windows 10 and 11 manual/VM checks cover tray recreation, DPI, taskbar placement, sleep/resume, and popover focus. Concrete unsupported behavior is displayed and documented rather than hidden.

## Observability and privacy

Telemetry is off and no remote crash reporter is included in MVP. Local structured diagnostics use event IDs, coarse durations/counts, adapter/parser versions, HTTP status class, and safe error codes. A central redactor runs before every sink and is tested against bearer headers, JSON secret fields, account identifiers, URLs/query strings, and source paths. Raw HTTP bodies, auth files, JSONL lines, prompts/responses, database rows, and exception objects containing those values are never logged.

Logs are local, size/age bounded, and disabled or minimal by default. An optional diagnostic export requires explicit user action, previews included categories, applies redaction again, and excludes the SQLite database and Codex files. Clear AIBar Data cancels work, closes persistence, deletes AIBar cache/database/logs/settings as selected, recreates empty state, and never traverses or deletes the Codex root.

## Testing strategy

Tests follow boundaries rather than UI screenshots alone:

- **Domain unit tests:** freshness transitions, countdown clocks, component-wise deltas, `Unknown`, model ranking formula, pricing warnings, DST/local-day policy, trend windows, and linear ETA insufficiency rules.
- **Adapter contract tests:** auth fixtures with minimum/extra/malformed fields; private endpoint fixtures for valid, missing, changed, 401/403/429/5xx, redirect, timeout, and optional-detail failure; verify no secret reaches error output.
- **Scanner golden/property tests:** supported layouts, unchanged/appended/truncated/replaced files, malformed lines, cumulative resets, cancellation, parser-version invalidation, and randomized non-negative/idempotence properties. Fixtures contain synthetic metadata and no real content or credentials.
- **Persistence integration tests:** transactional checkpoint/aggregate atomicity, migration, crash-before-commit recovery, clear-data isolation, and repricing without token mutation.
- **Coordinator tests with fakes:** cache-first startup, trigger coalescing, manual bypass, stale-on-failure, sleep/resume, and cancellation races.
- **WPF/Windows tests:** view-model state rendering plus focused Windows VM smoke tests for tray, popover, DPI, Explorer restart, startup, and single instance on Windows 10/11.
- **Privacy tests:** recursively inspect AIBar-owned database, logs, diagnostic exports, and UI/error snapshots for seeded prompt text, bearer values, sensitive IDs, and source paths.

Live private-endpoint tests are opt-in, never run in ordinary CI, require an explicitly provisioned disposable account/session, redact evidence, and cannot become the sole acceptance proof because the endpoint is unsupported.

## Reviewable implementation slices

Each slice is independently testable and MUST be forecast below the 400 authored changed-line review budget; split contracts/fixtures from implementation when the forecast exceeds it.

1. **Solution skeleton and pure contracts** — .NET/WPF host, domain records, clocks, error taxonomy, and test projects; no credential or network behavior.
2. **Credential and private quota adapter** — minimum-field auth reader, allowlisted HTTP client, schema mapping, redaction, and failure contract. Security-focused risk review.
3. **Refresh state and quota cache** — coalescing, freshness, stale preservation, optional-detail degradation, SQLite snapshot persistence. Reliability review.
4. **Tray and quota popover** — single instance, tray states, window lifecycle, both quota windows, countdowns, manual refresh, and disclosure copy.
5. **Scanner discovery and checkpoints** — supported roots/layouts, streaming offsets, invalidation, cancellation, and coverage, without analytics UI. Reliability review.
6. **Daily aggregation and model attribution** — component deltas, timezone policy, transactional daily/model rows, `Unknown`, ranking formula, and clear-data protection.
7. **Pricing and derived metrics** — versioned catalog, incomplete estimates, trends, pace/burn/ETA, and strict source labels.
8. **Startup, packaging, privacy, and compatibility hardening** — effective startup toggle, diagnostics/export, installers, migration/upgrade checks, Windows 10/11 matrix. Resilience review.

No slice may copy investigated source without a provenance and MIT-notice decision. Generated fixture volume is kept separate from authored-logic forecasts while remaining part of behavioral review identity.

## Rollout and release gates

1. Developer-only builds with synthetic fixtures and the private adapter disabled by default.
2. Opt-in internal build after credential/redaction review and live compatibility validation; show unsupported-endpoint disclosure.
3. Signed limited release only after policy/legal approval, dependency/SBOM review, Windows 10/11 packaging evidence, privacy inspection, and a tested remote-free kill switch/build policy for disabling private quota access.
4. Broader release only after observing schema/failure behavior without collecting user content or secrets.

Rollback is an application downgrade plus schema-compatible database handling; destructive database downgrade is not automatic. If private access becomes prohibited or incompatible, ship/configure it disabled while preserving local analytics and explicit quota-unavailable behavior.

## Design constraints carried forward

- Private service quota, local analytics, and estimated cost remain distinct types and UI sections.
- AIBar never modifies Codex-owned files and never implements browser cookies, passwords, token refresh, multiple roots/accounts, WSL, project analytics, authoritative billing, advanced deduplication, hourly reconstruction, or probabilistic forecasting in MVP.
- Any future move to WinUI or Tauri must preserve the application contracts, credential lifetime, persistence schema semantics, failure taxonomy, and privacy tests defined here.
