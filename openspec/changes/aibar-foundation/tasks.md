# AIBar Foundation Implementation Tasks

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 15 reviewable units; Slice 2A ~350, Slice 2B ~390, 3A ~330, 3B ~375, 3C ~230, 4A.1 ~315, 4A.2 ~260, 4B ~385, 4C ~390; all units below 400 authored lines |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2A → PR 2B → PR 3A → PR 3B → PR 3C → PR 4A.1 → PR 4A.2 → PR 4B → PR 4C → PR 5 → PR 6 → PR 7 → PR 8 |
| Delivery strategy | auto-chain |
| Chain strategy | feature-branch-chain |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

Each slice below is a candidate commit/PR with its tests and directly related documentation. Do not merge slices together if the authored forecast exceeds 400 lines. Generated fixtures may be separated operationally, but remain bound to the behavior they verify.

## Implementation Tasks

### Slice 1 — Solution skeleton and pure contracts

- [x] Create the .NET 8 WPF solution and test projects at the repository root, targeting x64 Windows 10/11 and keeping domain/application projects free of WPF, HTTP, filesystem, and SQLite dependencies.
- [x] Add domain records and ports under the chosen project structure for `QuotaSnapshot`, freshness/state, scan coverage, token deltas, daily usage, pricing results, and the contracts listed in `design.md` (`ICodexCredentialSource`, `IQuotaProvider`, stores, scanner, startup, clock/time policy).
- [x] Add deterministic clock/time-policy primitives and the error taxonomy needed to distinguish authentication, permission, malformed response, network, service, unavailable, stale, and partial-coverage states.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** add pure tests for freshness transitions, service-reset countdown basis, component-wise non-negative deltas, and immutable/source-distinct data types; make them pass and refactor without introducing infrastructure coupling.
- [x] Verify `dotnet build` and all tests pass with the private adapter disabled and no real Codex files, credentials, or network calls.

**Acceptance evidence:** solution/build output, unit-test report, and dependency inspection showing the domain has no platform or persistence references. Rollback is removal of the skeleton projects without touching user data.

### Slice 2A — Credential boundary, feature gate, and disclosure (~350 authored lines)

- [x] Implement the single supported Codex-root resolver (`%CODEX_HOME%`, otherwise the current-user `.codex` root) and a read-only minimum-field credential reader. Parse only the access credential and optional account identifier; never deserialize, persist, or log refresh tokens, passwords, browser cookies, prompt/response content, or unrelated secret fields.
- [x] Add the request-scoped disposable credential boundary and safe redaction/error primitives so bearer values, secret fields, sensitive account identifiers, source paths, and auth-file contents cannot reach UI, logs, diagnostics, or test evidence.
- [x] Add the feature-gated private integration policy, disabled-by-default configuration/build setting, and explicit unsupported/private-endpoint disclosure without disabling local analytics.
- [x] **RED:** add focused synthetic auth, root-resolution, minimum/extra/malformed-field, lifetime, feature-default, disclosure, and secret-redaction tests that fail before the boundary exists.
- [x] **GREEN:** implement only the minimum credential boundary, policy, disclosure, and redaction behavior needed to pass the focused tests; do not add HTTP or live endpoint behavior.
- [x] **TRIANGULATE:** test Codex replacement/share-read scenarios, missing roots, malformed credentials, disabled integration, and seeded secrets/account identifiers across error and diagnostic paths.
- [x] **REFACTOR:** remove duplicated parsing/redaction paths, keep domain/application contracts infrastructure-neutral, and verify no credential is persisted or exposed.

**Acceptance evidence:** focused synthetic test report, redaction/property assertions, root-resolution matrix, and configuration/disclosure evidence showing private integration disabled by default. Start state is Slice 1 contracts only; finish state is a usable credential/policy boundary with no HTTP. Rollback removes or disables Slice 2A without touching local analytics or Codex-owned files. Independently reviewable/revertible; requires focused `review-risk`.

### Slice 2B — Private HTTP quota adapter and contract mapping (~390 authored lines)

- [x] Implement the feature-gated `IQuotaProvider` with `HttpClient`, HTTPS allowlisting for only `https://chatgpt.com/backend-api`, redirects disabled, request-scoped authorization, cancellation, bounded connect/request timeouts, and conservative retry behavior honoring `Retry-After`.
- [x] Implement version-neutral DTO mapping for `/wham/usage` and optional `/wham/rate-limit-reset-credits`, validating required percentages/reset timestamps and preserving optional-detail failure without invalidating the primary quota result.
- [x] Classify 401/403/429/5xx, transport/timeout, redirect, malformed/missing-field, and unavailable outcomes into safe error codes and summaries; never expose raw bodies, headers, credentials, URLs/query secrets, or sensitive account identifiers.
- [x] **RED:** add synthetic HTTP/auth contract fixtures and tests for valid/version-variant/missing-field responses, status classes, redirect rejection, timeout/cancellation, retry limits, `Retry-After`, optional-detail failure, and secret-free errors.
- [x] **GREEN:** implement the allowlisted adapter, version-neutral mapping, failure classification, and bounded retry behavior against the synthetic handler/fixtures only.
- [x] **TRIANGULATE:** verify no cross-origin redirect can carry authorization, no retry occurs for 401/403/malformed payloads, one transient retry is bounded, and optional failure preserves primary windows.
- [x] **REFACTOR:** isolate endpoint details behind `IQuotaProvider`, reuse Slice 2A redaction/lifetime boundaries, and keep live private-endpoint tests opt-in and outside ordinary CI.

**Acceptance evidence:** synthetic adapter contract/fixture test report, redirect/timeout/retry evidence, version-neutral mapping matrix, and redaction assertions. Start state is the completed Slice 2A boundary; finish state is a disabled-by-default, independently testable adapter contract with no cache/coordinator. Rollback removes or disables Slice 2B while preserving Slice 2A and local analytics contracts. Independently reviewable/revertible; requires focused `review-risk`.

### Slice 3A — SQLite quota snapshot store (~330 authored lines)

**Dependency:** Slice 2B complete. **Scope boundary:** persistence only; no refresh coordinator, polling, tray, or UI. **Independent reviewability:** review the store, migration, privacy boundary, and integration tests as one revertible unit.

- [x] **RED:** add persistence integration tests against a temporary database for first-open schema creation/migration, normalized `quota_snapshot` round-trip, empty load, FK enforcement, WAL mode, atomic clear, and inspection proving credentials/raw HTTP bodies are not represented; add a crash-before-commit test harness that fails before commit and asserts the prior row remains intact.
- [x] **GREEN:** wire the SQLite dependency/project boundary; implement schema versioning/migration and `IQuotaSnapshotStore` with normalized primary/optional quota fields, retrieval metadata, schema-adapter version, and timestamps only; configure foreign keys and WAL; implement transactional atomic load/save/clear with no credential or raw-response persistence.
- [x] **TRIANGULATE:** exercise migration from the prior schema, rollback on injected write failure/crash-before-commit, repeated save/load, optional-detail absence, malformed/unsupported persisted data handling, and database-content assertions for seeded secrets, headers, paths, and response bodies.
- [x] **REFACTOR:** isolate SQL/mapping behind the store port, keep domain/application contracts infrastructure-neutral, document migration compatibility and non-destructive downgrade behavior, and verify deterministic disposal/locking behavior.

**Acceptance evidence:** focused RED/GREEN/TRIANGULATE/REFACTOR report; migration/FK/WAL inspection; atomicity and crash-before-commit evidence; normalized-schema/privacy inspection; `dotnet build` and relevant tests. **Rollback boundary:** remove or disable the 3A store and revert only its schema-compatible migration/project/test changes; preserve Slice 2B and never delete Codex-owned data.

### Slice 3B — Refresh coordinator state machine (~375 authored lines)

**Dependency:** Slice 3A complete. **Scope boundary:** coordinator and deterministic fakes only; no tray/popover or Windows lifecycle event wiring. **Independent reviewability:** provider/store/coordinator tests establish a complete application-service boundary and can be reverted without changing presentation.

- [x] **RED:** add fake-clock/provider/store tests for cache-first startup publication, one in-flight refresh, concurrent trigger coalescing, manual freshness bypass, conservative polling eligibility, failure overlays, original timestamp preservation, stale/unavailable threshold transitions, optional-detail degradation, cancellation, and clear-data publication suppression.
- [x] **GREEN:** implement `QuotaRefreshCoordinator` using the 3A store and 2B provider: publish cached state first, gate one asynchronous refresh task, coalesce poll/popover-open/resume/manual triggers, let manual refresh bypass freshness but not concurrency, schedule conservatively, preserve the prior snapshot/timestamp on failure, overlay classified errors, and publish explicit stale/unavailable states without fabricating percentages.
- [x] **TRIANGULATE:** use deterministic provider gates and fake time to prove no duplicate provider calls, no early polling, manual refresh behavior, cancellation before publication, clear-data races, primary snapshot retention when optional detail fails, and state transitions across the freshness threshold.
- [x] **REFACTOR:** separate policy/state transition logic from orchestration and scheduling, make publication immutable and test-observable, keep cancellation/disposal idempotent, and verify no UI/thread-affinity dependency enters the application service.

**Acceptance evidence:** deterministic coordinator test report covering every trigger/state race, cache-first trace, timestamp-preservation assertions, and full build/tests for affected projects. **Rollback boundary:** revert coordinator and its tests/scheduling changes while leaving the 3A database store loadable and Slice 2B unchanged.

### Slice 3C — Lifecycle and degradation hardening (~230 authored lines)

**Dependency:** Slice 3B complete. **Scope boundary:** lifecycle event adapter and hardening tests only; no Slice 4 visual/presentation work. **Independent reviewability:** event-to-coordinator commands and degradation races are testable with fake time and cancellation gates.

- [x] **RED:** add deterministic tests for sleep/resume, system clock forward/backward changes, resume freshness re-evaluation, stale-to-current/unavailable transitions, prior timestamp preservation, optional-detail degradation, and clear-data/cancellation races using the existing coordinator contracts.
- [x] **GREEN:** add the lifecycle boundary that forwards sleep/resume and clock-change notifications to coordinator re-evaluation; harden stale/unavailable transitions, retain original successful timestamps on failures, preserve primary data when optional details fail, and prevent cancelled/cleared work from republishing.
- [x] **TRIANGULATE:** inject reordered lifecycle events, duplicate notifications, clock jumps, cancellation at each publication boundary, provider failure during resume, and store-unavailable conditions; verify deterministic state traces and no overlapping refreshes.
- [x] **REFACTOR:** make event subscriptions/disposal idempotent, centralize degradation rules, remove duplicated transition handling, and run the affected suite plus static/build diagnostics without adding UI coupling.

**Acceptance evidence:** lifecycle/degradation race report, fake-clock state traces, cancellation/clear-data evidence, and affected build/test output. **Rollback boundary:** remove the lifecycle adapter and hardening changes, retaining the stable 3B coordinator and 3A schema/store.

**Slice 3 chain:** `2B → 3A → 3B → 3C → 4A.1`. Each unit remains under the 400-line authored review budget and must be applied/reviewed independently before advancing.

### Slice 4A.1 — WPF host primitives, single-instance activation, and pure placement (~315 including evidence)

**Dependency:** Slice 3C complete at `1b9cca5`. **Scope boundary:** testable WPF host primitives, named-mutex single-instance activation/secondary-activation handoff contract, pure taskbar/DPI-aware popover placement model, and focused deterministic tests. Explicitly excludes live tray runtime/recreation, popover deactivation behavior, and Exit orchestration. **Rollback/review boundary:** revert only 4A.1 to restore the Slice 3C baseline; review and apply as one independently testable unit before 4A.2.

- [x] **RED:** add deterministic tests for named-mutex ownership, secondary-launch activation handoff, idempotent activation handling, taskbar work-area placement, monitor edges, and DPI scaling; keep tests independent of a live tray icon or process shutdown.
- [x] **GREEN:** implement the minimal WPF host primitives, named-mutex single-instance contract, secondary-activation handoff, and pure placement model using taskbar work area and DPI inputs; do not wire live tray runtime, recreation, deactivation, or Exit.
- [x] **TRIANGULATE:** exercise duplicate launches, repeated handoff signals, malformed/edge placement inputs, taskbar/display/DPI changes represented as pure inputs, and deterministic activation/placement traces.
- [x] **REFACTOR:** isolate Win32/WPF boundary code from pure activation and placement policies, make ownership/handoff disposal idempotent, and verify focused deterministic tests and diagnostics without adding runtime orchestration.

**Acceptance evidence:** deterministic activation-contract and placement test report, synthetic monitor/taskbar/DPI matrix, host primitive dependency inspection, and build diagnostics. Forecast is ~315 lines including evidence and remains below 400. No tray runtime, recreation, deactivation, or Exit smoke evidence belongs here.

### Slice 4A.2 — Tray runtime, recreation, popover lifecycle, and orderly Exit (~260 including evidence)

**Dependency:** Slice 4A.1 complete and reviewed. **Scope boundary:** actual tray runtime bridge, taskbar/Explorer recreation, popover show/hide/deactivation behavior, and orderly explicit Exit that cancels work and closes persistence, using 4A.1 primitives. No quota view-model mapping, quota-card styling, analytics disclosures, or visual token system. **Rollback/review boundary:** revert only 4A.2 to retain the reviewed 4A.1 primitives and restore a non-runtime host boundary; review and apply independently before 4B.

- [x] **RED:** add focused Windows smoke tests for tray creation/toggle, secondary activation handoff, Explorer/taskbar recreation, popover focus/deactivation, and Exit cancellation/persistence disposal ordering.
- [x] **GREEN:** implement the tray runtime bridge, recreation handling, borderless popover show/hide and deactivation behavior, and explicit Exit orchestration that cancels work, closes persistence, and exits orderly.
- [x] **TRIANGULATE:** exercise taskbar recreation, repeated toggles, owned-dialog deactivation, shutdown races, cancellation/close ordering, and activation handoff through the 4A.1 contract on a representative Windows environment.
- [x] **REFACTOR:** isolate runtime Win32/WPF adapters, make tray recreation and shutdown idempotent, keep coordinator/persistence calls at the host boundary, and rerun focused smoke tests plus diagnostics.

**Acceptance evidence:** focused Windows smoke log for tray, activation, recreation, popover lifecycle, and Exit; cancellation/persistence-close ordering evidence; and build diagnostics. Forecast is ~260 lines including evidence and remains below 400.

### Slice 4B — Immutable presentation state, commands, and disclosures (~385 authored lines)

**Dependency:** Slice 4A.2 complete. **Scope boundary:** immutable view-model/state mapping, commands, quota/analytics/cost semantics, and behavior tests; no host lifecycle changes or semantic visual token/card styling. **Rollback:** remove 4B to retain the 4A.1/4A.2 host boundary and revert only presentation-state behavior.

- [x] **RED:** add deterministic view-model and command tests for current, stale, loading, unavailable, authentication, permission, malformed, network, and service states; prove no fabricated percentage or stale value is shown as current; cover refresh command behavior.
- [x] **GREEN:** implement immutable state mapping and commands for independent 5-hour/weekly percentages, service reset countdowns, manual refresh, source/timestamp/freshness, explicit private-endpoint disclosure, and visually/conceptually separate local analytics and estimated cost data.
- [x] **TRIANGULATE:** exercise state transitions, missing windows, stale/error overlays, loading/concurrent refresh commands, reset time basis, and snapshots proving service quota, local analytics, and estimated cost remain distinct.
- [x] **REFACTOR:** centralize mapping/label semantics, remove mutable presentation leakage and duplicated disclosure text, preserve dispatcher-independent tests, and run the focused behavior suite and build diagnostics.

**Acceptance evidence:** deterministic state/command test report for every listed state, snapshots proving no fabricated current percentage, source/timestamp/freshness and disclosure evidence, and build output. Independently reviewable/testable/revertible; apply/review only after 4A.2 and before 4C.

### Slice 4C — Semantic WPF tokens, quota cards, and accessibility evidence (~390 authored lines)

**Dependency:** Slice 4B complete. **Scope boundary:** semantic WPF design tokens, compact CodexBar-inspired quota cards, typography/spacing/contrast, keyboard/focus/accessibility/reduced-motion/high-contrast/DPI behavior, and representative evidence; no new host lifecycle or state/business rules. **Rollback:** remove 4C styling/evidence while preserving 4B state contracts and 4A lifecycle.

- [x] **RED:** add rendering/interaction checks for semantic token roles, quota-card hierarchy, typography/spacing/contrast, keyboard navigation, visible focus, accessible names, reduced motion, high contrast, and DPI scaling; establish representative W10/W11 evidence expectations.
- [x] **GREEN:** build the custom semantic WPF token system and compact quota cards using Windows-native typography, spacing, contrast, keyboard/focus/accessibility behavior, reduced-motion and high-contrast support, and DPI scaling.
- [x] **TRIANGULATE:** validate representative Windows 10/11 scale factors, keyboard/focus paths, screen-reader names, reduced-motion/high-contrast behavior, and that visual evidence does not replace lifecycle/accessibility checks.
- [x] **REFACTOR:** consolidate semantic resources, remove hard-coded presentation roles, preserve readable Windows fallbacks, and rerun representative interaction/accessibility checks and build diagnostics.

**Acceptance evidence:** automatable token/card checks where available, accessibility and lifecycle checklist, focused interaction smoke results, and representative Windows 10/11 screenshots at scale factors. Independently reviewable/testable/revertible; apply/review only after 4B.

**Slice 4 requirement mapping:** original host primitives and named-mutex activation → 4A.1; original pure taskbar/DPI placement → 4A.1; original tray runtime/recreation, popover deactivation, and explicit Exit orchestration → 4A.2; original host/tray smoke obligation → 4A.2; original immutable-state task → 4B; original semantic-token/card task → 4C; original quota windows/countdowns/source/timestamp/freshness/manual refresh/analytics-cost separation/private disclosure task → 4B; original W10/W11 visual/accessibility evidence obligation → 4C. Every current 4A obligation appears exactly once across 4A.1 or 4A.2, and 4B/4C product scope is unchanged.

**Slice 4 chain:** `1b9cca5 → 4A.1 → 4A.2 → 4B → 4C → 5`. Each unit is independently testable and revertible and remains at or below 400 authored lines. Do not include scanner, pricing, packaging, or later-slice work.

### Slice 5A — Discovery and bounded enumeration

**Dependency:** Slice 4C complete. **Scope boundary:** reuse `CodexRootResolver` to inspect only the configured root's `sessions` and `archived_sessions` directories, discover synthetic `.jsonl` in date-partitioned, flat, and recursive legacy layouts, and return deterministic positive-size bounded batches. No JSON content reads, checkpoints, identity/mtime, parsing, SQLite, aggregation, UI, or real Codex access.

- [x] **RED:** add synthetic temporary-directory and injected-filesystem tests for resolved-root discovery, supported-layout batching, cancellation, containment/reparse safety, and path-free missing/unreadable/changing/attribute-failure coverage codes.
- [x] **GREEN:** implement only the `CodexRootResolver`-based bounded discovery/enumeration boundary for `sessions` and `archived_sessions`; do not read JSONL contents or add persistence.
- [x] **TRIANGULATE:** exercise mixed supported/unsupported files, nested legacy directories, deterministic ordering, cancellation, and reparse/symlink escape rejection using synthetic fixtures only.
- [x] **REFACTOR:** centralize containment and safe warning-code handling, preserve cancellation checks between directories/files/batches, and rerun focused/full/build/diff/LF/line-count checks.

**Acceptance evidence:** synthetic discovery test report with safe path-free warning codes and bounded batch matrix. **Rollback boundary:** remove the discovery boundary and tests without touching Codex-owned files or later scanner/persistence work. Independently reviewable/revertible; requires focused `review-reliability`.

### Slice 5B — Streaming parse and checkpoint semantics

**Dependency:** Slice 5A complete. **Scope boundary:** validated checkpoint offsets, file identity/size/mtime comparison, append/replacement/shrink/parser-version invalidation, incomplete-tail deferral, and transactional checkpoint updates; no aggregation/UI.

- [x] Implement streaming JSONL parsing from validated checkpoints with append handling, incomplete-tail deferral, parser-semantics invalidation, replacement/rebuild, and transactional checkpoint updates.
- [x] Parse only timestamps, trustworthy model evidence, and token counters; skip/defer malformed or changing records with scan coverage warnings and never retain prompt/response bodies.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic tests for unchanged rescans, appended events, malformed/truncated files, replaced/shrunk files, parser-version invalidation, and cancellation before checkpoint commit.

### Slice 5C — Scan provenance and coverage status

**Dependency:** Slice 5B complete. **Scope boundary:** `scan_run` provenance/status, discovered/read/skipped/deferred counts, warning/cancellation/partial coverage persistence, and synthetic evidence; no aggregation/UI.

- [x] Implement SQLite-backed `scan_run` provenance/status including discovered/read/skipped/deferred counts, safe warnings, durable cancellation, and partial coverage.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic tests for scan coverage counts, cancellation, idempotence, and non-negative delta handoff boundaries.
- [x] Verify fixture data contains no real credentials, paths, prompt text, or response text, and that cancellation before commit leaves aggregates/checkpoints unchanged.

**Slice 5 chain:** `4C → 5A → 5B → 5C → 6`. Each unit is independently testable/revertible and remains below the 400-line authored review budget.

### Slice 6 — Daily aggregation, model attribution, and clear-data isolation

- [ ] Implement transactional `daily_model_usage` aggregation for input, cached-input, and output totals using `max(0, current - previous)` per component and the exact documented ranking formula `total tokens = input + cached input + output`.
- [ ] Implement explicit `Unknown` attribution for missing/untrusted model evidence; never infer a named model or rank by cost.
- [ ] Implement Windows local-timezone/day normalization with timezone ID, observed offset, DST behavior, UTC-derived provenance, and explicit rebuild/version behavior when the policy changes.
- [ ] Add SQLite migrations and transactional tests for checkpoint/aggregate atomicity, crash recovery, repricing without token mutation, and aggregate retention.
- [ ] Implement Clear AIBar Data to cancel work, remove only AIBar-owned cache/database/log/settings data, recreate empty state, and prove Codex-owned source files are byte-for-byte unchanged.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test model ranking, `Unknown`, midnight/DST boundaries, reset/decrease handling, clear-data isolation, and partial scan retention.

**Acceptance evidence:** aggregation/property/persistence/privacy test reports and before/after hashes of Codex fixtures. Rollback is a schema-compatible disablement of derived analytics; source files are never deleted.

### Slice 7 — Pricing, trends, pace, burn, and ETA

- [ ] Add a checked-in, versioned immutable pricing catalog with version/date provenance, component-specific rates, unsupported-model results, and no fallback rate for `Unknown`.
- [ ] Implement estimated-cost calculation and UI labels/warnings for estimate status, unknown models, repricing, discounts, routing, contracts, and non-authoritative billing assumptions; never use billed-cost/invoice/credit language.
- [ ] Implement named daily trend windows (initial 7 complete local days and day-over-day comparison) and simple linear quota exhaustion from observed current-window usage, remaining service quota, reset time, and valid positive rate only.
- [ ] Return insufficient/unavailable for stale quota, invalid reset, zero/negative rate, insufficient observations, or unsupported forecasting; never reconstruct hourly history or emit probabilistic predictions.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test pricing provenance/warnings, model totals, trend windows, time basis, ETA insufficiency, and linear estimate labeling.
- [ ] Verify UI/source labels distinguish service quota, locally derived analytics, and estimated cost in view-model snapshots.

**Acceptance evidence:** domain/UI tests and sample output showing formula, window, rate basis, provenance, and warnings. Rollback is removal of derived metrics without changing token facts or quota behavior.

### Slice 8 — Startup, diagnostics, packaging, privacy, and compatibility hardening

- [ ] Implement per-user startup registration abstraction: packaged startup-task path where available and unpackaged HKCU Run entry with quoted executable and `--startup`; read back effective state after mutation, never elevate or write machine-wide state.
- [ ] Add native commands/settings for startup toggle, Clear AIBar Data, safe diagnostic export, and private-integration disablement; keep telemetry and remote crash reporting off by default.
- [ ] Implement central redaction before every diagnostic sink/export for bearer headers, secret fields, sensitive IDs, URLs/query strings, paths, raw bodies, auth files, JSONL lines, database rows, and exception values; bound local log size/age.
- [ ] Add privacy tests that recursively inspect AIBar-owned persistence, logs, diagnostics, exports, and UI/error snapshots for seeded prompt/response text, bearer values, sensitive IDs, and source paths.
- [ ] Add deterministic x64 Windows 10/11 packaging, SBOM, signed per-user MSIX path when signing is available, unpackaged self-contained development/recovery artifact, and upgrade/migration/uninstall/user-data retention checks.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add packaging smoke tests for clean install, upgrade, startup launch, single instance, uninstall, and data retention/removal; perform W10/W11 manual/VM checks for tray recreation, DPI, taskbar placement, sleep/resume, popover focus, and concrete unsupported behavior.
- [ ] Complete policy/legal review, dependency and source-provenance/MIT-notice review, private-endpoint compatibility validation, and remote-free kill-switch verification before enabling distribution builds.

**Acceptance evidence:** packaging artifacts/checksums/SBOM, install-upgrade-uninstall report, W10/W11 matrix, privacy inspection report, policy/legal sign-off, and rollback procedure. Requires focused `review-resilience` plus release risk review.

## Cross-Slice Completion Gates

- [ ] Run the full test suite, static analysis, dependency/license checks, and `dotnet publish` for the supported Windows target.
- [ ] Confirm all MVP non-goals remain unsupported and no code path reads multiple roots/accounts, WSL, browser cookies, passwords, prompt/response bodies, or authoritative billing data.
- [ ] Confirm generated fixtures and screenshots are synthetic/approved and remain bound to the reviewed behavior.
- [ ] Confirm each implementation slice remains independently revertible and its authored change count is at or below 400 lines before opening or advancing its chained PR.
- [ ] Preserve no credentials or user content in test evidence, logs, artifacts, screenshots, or release bundles.
