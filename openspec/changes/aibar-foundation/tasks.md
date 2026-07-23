# AIBar Foundation Implementation Tasks

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | Historical slices retained; 8C2A ~250–300, 8C2B1a ~360–390, 8C2B1b ~360–390, 8C2B2 ~260–340 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | Existing chain → 8B.1 → 8C1 → 8C2A → 8C2B1a → 8C2B1b → 8C2B2 → 8D → 8E |
| Delivery strategy | auto-chain |
| Chain strategy | feature-branch-chain |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High
Literal-plan tier: exceptional — chained-PR path, runtime gate, and per-child rollback evidence
Size-scope: active Slice 8C2 section only
Active-section word count: 772
Full-artifact word count: 6957

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

**Chained sequence:** `5C → 6A → 6B → 6C1A → 6C1B → 6C2A → 6C2B → 6D → 7`. Each unit is independently testable/revertible and must remain at or below 400 authored changed lines. 6C2A preserves 6C1A preparation and 6C1B durable CAS/atomic handoff; 6C2B follows 6C2A because it rebuilds the state model established there.

#### Slice 6A — Analytics policy kernel (~330 lines including evidence)

**Scope boundary:** pure domain policy only; no SQLite schema/migration, scanner wiring, checkpoint advancement, rebuild persistence, Clear Data, pricing, UI, or network access.

- [x] Implement pure component-wise cumulative deltas as `max(0, current - previous)` independently for input, cached-input, and output; aggregate only token facts with the exact ranking formula `input + cached input + output`.
- [x] Implement explicit `Unknown` attribution for absent/untrusted model evidence; never infer a named model or rank by cost; resolve equal token totals deterministically.
- [x] Implement injectable Windows local-day/timezone policy with event UTC provenance, Windows timezone ID, observed offset, deterministic DST conversion, and an explicit policy-version mismatch decision that requires rebuild rather than silently moving history.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** add pure tests for resets/decreases, `Unknown`, token-only ranking/ties, midnight/DST boundaries, UTC/timezone/offset provenance, and policy-version rebuild behavior.

**Acceptance evidence:** pure policy test report and documented no-persistence/no-scanner boundary. **Rollback:** remove the policy kernel/tests only; no user data exists or is changed.

#### Slice 6B — Daily-model SQLite schema and atomic store (~360 lines including evidence)

**Dependency:** 6A complete. **Scope boundary:** `daily_model_usage` schema/migration and transactional token-fact persistence only; no scanner wiring, checkpoint advancement, rebuild orchestration, Clear Data, or pricing.

- [x] Add SQLite migration and transactional `daily_model_usage` storage for local day, timezone ID/offset provenance, model, and input/cached-input/output totals, preserving 6A token facts.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** test aggregate/checkpoint transaction seams, crash recovery, aggregate retention, and repricing without token mutation using synthetic data.

#### Slice 6C1A — Two-phase scanner and proposed checkpoint contract (~190 lines including evidence)

**Dependency:** 6B complete. **Scope boundary:** scanner preparation only; no SQLite schema/checkpoint persistence, `AnalyticsScanCoordinator`, aggregate commit, retry transaction, policy rebuild persistence, or Clear Data.

- [x] Add a preparation API that reads/parses from a caller-supplied durable prior checkpoint and returns records, warnings, rebuild status, and an uncommitted proposed checkpoint without mutating `SessionCheckpointStore` or durable state.
- [x] Keep the proposed checkpoint path-free and limited to file identity, observed length/mtime, safe complete-line byte offset, parser version, and cumulative input/cached-input/output counters; rebuild on identity/parser/shrink/inconsistent-offset changes.
- [x] Preserve Slice 5B public `ScanAsync` compatibility by implementing it through preparation and its existing in-memory commit path.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** test preparation non-mutation/retry reproducibility, unchanged/append behavior, incomplete-tail deferral, rebuild triggers, and legacy scanner compatibility without prompt/response persistence.

#### Slice 6C1B — Coordinator-owned atomic commit and retry proof (~250 lines including evidence)

**Dependency:** 6C1A complete. **Scope boundary:** durable checkpoint load/schema/write, coordinator-owned aggregate/checkpoint transaction, and real scanner-path retry evidence; no policy rebuild persistence or Clear Data.

- [x] Wire supported scanner token/model/timestamp handoff through 6A to 6B, advancing the exact proposed checkpoint only in the same successful aggregate transaction.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** test unchanged/appended handoff, cancellation/failure-before-commit retry reproducibility, and partial-scan retention without prompt/response persistence.

#### Slice 6C2A.1 — Source-attributed durable contributions and migration (~260 lines including evidence)

**Dependency:** 6C1B complete; 6C1A preparation and 6C1B durable CAS/atomic handoff are immutable prerequisites. **Scope boundary:** source-attributed contribution schema, migration, and durable read/write ports; no rebuild orchestration, Clear Data, pricing, UI, or unrelated diagnostics.

- [x] **RED:** test independent per-source contribution reads, migration of multi-source legacy aggregates, policy-less legacy detection, and preservation of 6C1B checkpoint/CAS semantics.
- [x] **GREEN:** add source-attributed durable contribution rows and explicit migration/status APIs; retain legacy data read-only or require safe rebuild when attribution is impossible.
- [x] **TRIANGULATE/REFACTOR:** prove append/unchanged idempotence, source isolation, cancellation/failure-before-commit retryability, policy provenance, and no prompt/response persistence.

**Review Workload Forecast — 6C2A.1:** ~260 authored additions+deletions; risk Medium; focused command `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~SourceAttributed`; runtime harness N/A (synthetic SQLite migration; no UI/live source); rollback removes contribution schema/port/tests while preserving 6C1B.

#### Slice 6C2A.2 — Atomic multi-source rebuild and policy transition (~300 lines including evidence)

**Dependency:** 6C2A.1 complete and reviewed. **Scope boundary:** coordinator-owned atomic multi-source rebuild/policy transition using attributed contributions; no Clear Data, pricing, UI, or unrelated diagnostics.

- [x] **RED:** test source replacement/shrink/parser invalidation, policy mismatch, legacy policy-less databases, cancellation, failure-before-commit, and retry; assert unrelated sources remain unchanged.
- [x] **GREEN:** implement one transaction that rebuilds selected sources, requires an exact durable checkpoint set while clearing migration debt (or an explicit checkpoint-free legacy rescan), preserves unrelated contribution/checkpoint state, updates policy provenance, and fails closed when legacy attribution is unsafe.
- [x] **TRIANGULATE/REFACTOR:** prove multi-source atomicity, deterministic retry, concurrent CAS rejection, partial-scan retention, and no prompt/response persistence.

**Review Workload Forecast — 6C2A.2:** ~300 authored additions+deletions; risk Medium; focused command `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~MultiSourceRebuild`; runtime harness N/A (synthetic coordinator/SQLite boundary; no live Codex access); rollback removes rebuild orchestration/tests while preserving 6C2A.1.

#### Slice 6C2B — Clear AIBar Data cancellation and strict isolation (~260 lines including evidence)

**Dependency:** 6C2A.2 complete and reviewed. **Scope boundary:** Clear AIBar Data only; no rebuild or policy implementation.

- [x] **RED:** test cancellation boundaries, empty-state recreation, and byte-for-byte Codex hashes before/after clear.
- [x] **GREEN:** cancel work, suppress late commits, remove only AIBar-owned state, and recreate a valid empty state.
- [x] **TRIANGULATE/REFACTOR:** test repeated clear, races, locked/source-changing files, failure recovery, and post-clear rescan readiness.

**Review Workload Forecast — 6C2B:** ~260 authored additions+deletions; risk Low; focused command `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~ClearAiBarData`; runtime harness N/A (synthetic isolation); rollback removes clear orchestration/tests while preserving 6C2A.2.

#### Slice 6D — Aggregation integration hardening (~300 lines including evidence)

**Dependency:** 6C2A and 6C2B complete and reviewed. **Scope boundary:** synthetic end-to-end aggregation correctness and resilience only; no Slice 7 pricing/trends/UI work.

- [x] Verify transactional aggregate/checkpoint atomicity, crash recovery, partial scan retention, and repricing without token mutation across the completed Slice 6 boundary.
- [x] **RED → GREEN → TRIANGULATE → REFACTOR:** run synthetic integration/property coverage for model ranking, `Unknown`, reset/decrease, midnight/DST, rebuild, and retention behavior.

**Acceptance evidence:** aggregation/property/persistence/privacy reports and synthetic before/after Codex-fixture hashes. Rollback is schema-compatible disablement of derived analytics; source files are never deleted.

### Slice 7 — Pricing, trends, pace, burn, and ETA

**Chained sequence:** `6D → 7A → 7B → 7C → 8`. Each child targets its immediate predecessor branch, remains independently useful, and forecasts ≤400 authored additions+deletions.

#### Slice 7A — Immutable pricing catalog and estimated-cost domain policy (~260 lines)

**Dependency:** 6D complete. **Scope:** checked-in immutable version/date catalog, component rates, unsupported-model results, estimate calculation and provenance/warnings; no trends, ETA, UI wiring, or token-fact mutation. **Non-goals:** billed cost, invoices, credits, fallback pricing, repricing of stored facts.

- [x] **RED:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~Pricing` for supported components, catalog provenance, `Unknown`/unsupported models, and warnings for repricing, discounts, routing, contracts.
- [x] **GREEN:** implement catalog and estimated-cost policy with explicit estimated/unavailable results and no `Unknown` fallback.
- [x] **TRIANGULATE:** prove component totals remain factual, rates are immutable/versioned, and unsupported prerequisites never produce a number.
- [x] **REFACTOR:** isolate catalog access and centralize non-authoritative warning/source semantics.

**Evidence/runtime:** domain test report and catalog inspection; runtime N/A (pure policy). **Rollback:** remove 7A catalog/policy/tests; preserve 6D token facts and quota.

#### Slice 7B — Named trend windows and linear ETA policy (~300 lines)

**Dependency:** 7A complete. **Scope:** seven complete local-day window, day-over-day comparison, observed current-window rate, remaining service quota/reset, and linear exhaustion ETA. **Non-goals:** UI integration, hourly reconstruction, probabilistic/history forecasting.

- [x] **RED:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~DerivedMetrics` for window names/time basis, insufficient observations, stale quota, invalid reset, zero/negative rate, and unsupported forecasting.
- [x] **GREEN:** implement named trends and simple estimated ETA only for valid positive observed rate and service inputs.
- [x] **TRIANGULATE:** prove no hourly reconstruction/prediction language and preserve quota/token facts when estimates are unavailable.
- [x] **REFACTOR:** separate trend and ETA policies with deterministic clock/time-basis outputs.

**Evidence/runtime:** focused domain report with acceptance matrix; runtime N/A (pure derived policy). **Rollback:** remove 7B policies/tests while retaining 7A pricing.

#### Slice 7C — View-model source labels and warnings (~340 lines)

**Dependency:** 7B complete. **Scope:** presentation mapping for estimated cost, named trends, ETA, warnings, and source labels separating service quota, locally derived analytics, and estimated cost. **Non-goals:** new domain rules, host lifecycle, quota behavior, Slice 8 work.

- [x] **RED:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~ViewModel` for source-label separation, estimate/unavailable states, warning copy, and non-authoritative language.
- [x] **GREEN:** wire existing view-model contracts without fabricating values or relabeling derived data as service data.
- [x] **TRIANGULATE:** snapshot stale/unknown/unsupported/insufficient cases and verify factual token/quota aggregates remain unchanged.
- [x] **REFACTOR:** centralize display labels and remove duplicated warning/source mapping.

**Evidence/runtime:** view-model snapshot report; runtime N/A (presentation mapping has no live harness). **Rollback:** revert 7C mapping/tests, leaving 7A/7B domain policies usable.

### Slice 8A — Per-user startup and native settings (~260 lines)

**Dependency/base:** 7C branch; chain PR #1 targets `feature/aibar-foundation-slice-7c-viewmodel`. **Scope:** startup abstraction, settings commands, Clear AIBar Data and private-integration/telemetry policy wiring. **Non-goals:** diagnostics sinks, packaging, policy sign-off.

**Delivery decision:** `size:exception` for Slice 8A only, explicitly maintainer-approved in session 2026-07-18. Slices 8B–8E remain `auto-chain` with the <=400 authored-line budget; this exception grants no broader scope or budget waiver.

- [x] **RED:** test packaged-task vs HKCU Run selection, quoted path/`--startup`, read-back, denial, no elevation/machine writes, toggle, clear-data isolation, and disabled private/telemetry/crash defaults.
- [x] **GREEN:** implement `IStartupRegistration`, native settings/commands, effective-state read-back, and policy defaults.
- [x] **TRIANGULATE:** exercise missing task API, malformed Run value, repeated toggles, locked data, and kill-switch preserving local analytics.
- [x] **REFACTOR:** keep OS adapters isolated and disposal/idempotence explicit. Focused: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Startup|FullyQualifiedName~ClearAiBarData|FullyQualifiedName~Policy"`. Runtime: Windows 10/11 per-user toggle scenario. Rollback: remove startup/settings adapters and tests only.

### Slice 8B.0 — Structured diagnostic event boundary (~280 lines)

**Dependency/base:** starts from exact reviewed base `feature/aibar-foundation-slice-8a-startup-settings`; current child branch is `feature/aibar-foundation-slice-8b0-redactor`; PR #2 targets the 8A base. **Scope:** pure Application typed event/category/code/time-basis contract; strict allowlist, deterministic serialization, idempotence, and bounded fields/events. **Non-goals:** UI, sinks, file/network export, arbitrary-text parsing, and changing credential `SafeRedactor`.

- [x] **RED:** add pure property/table tests for rejected keys/types, nested objects/collections, exceptions, IDs/paths/auth/content, raw fallback, full `[REDACTED]`, deterministic output, limits, and proof that arbitrary strings are never parsed or stored.
- [x] **GREEN:** implement typed safe enums/numbers/booleans, category/error-kind fallback, strict fail-closed allowlist, bounded event/field serialization, and no raw payload storage; keep `SafeRedactor` at its credential boundary.
- [x] **TRIANGULATE:** run adversarial casing/escaping/null/oversize/repeated-redaction cases and inspect serialized output for user content, URLs/query, paths, bodies, prompts/responses, IDs, and exception values.
- [x] **REFACTOR:** centralize contracts/serialization and document the non-parsing boundary. Focused PowerShell-safe filter: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~StructuredDiagnostic"`. Runtime: N/A (pure contract). Rollback: remove Application contract/tests only; retain 8A.

### Slice 8B.1 — Gesture-bound command and central in-memory sinks (~300 lines)

**Dependency/base:** reviewed 8B.0; PR #3 targets `feature/aibar-foundation-slice-8b0-redactor` from `feature/aibar-foundation-slice-8b1-diagnostic-command`. **Scope:** private one-shot trusted UI gesture, category preview, central App/Quota in-memory sinks, bounded retention; export unavailable. **Non-goals:** file/network I/O, raw text parsing, installers.

- [x] **RED:** test gesture authorization/one-shot expiry, tray/UI category preview-confirm-unavailable behavior, sink routing, bounded count/size/age, concurrency, clear-data, and absence of export/file/network paths.
- [x] **GREEN:** wire only structured 8B.0 events through central App/Quota sinks and enforce private gesture plus bounded memory.
- [x] **TRIANGULATE:** exercise repeated/replayed gestures, concurrent emission, cancellation, clear races, and malformed event rejection.
- [x] **REFACTOR:** isolate tray/UI adapter from Application sinks and preserve immutable snapshots. Focused PowerShell-safe filter: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~DiagnosticCommand"`. Runtime: synthetic tray/gesture scenario — invoke trusted gesture, preview a safe category, confirm, verify unavailable/empty state, and prove no file or network operation occurs. Rollback: remove command/sinks/UI tests; retain 8B.0.

### Retired Slice 8B.2 — Diagnostic filesystem export (not active)

**Status:** retired from the active chain. Diagnostic filesystem export remains unavailable and disabled. The prior 8B.2 implementation/tests are uncommitted candidate evidence only; the prior 208/208 result is historical and does not establish approval. Preserve the earlier 8B.0/8B.1 history and do not reuse the non-terminal review binding.

**Future Apply removal work unit (must run before Slice 8C; stop after verification):**

- [x] Delete only `src/AIBar.Application/DiagnosticExport.cs` and `tests/AIBar.Domain.Tests/DiagnosticExportTests.cs`; do not delete or alter approved 8B.0/8B.1 behavior. <!-- sdd-owner: implementation -->
- [x] Correct only the Slice 8B.2 progress evidence in `openspec/changes/aibar-foundation/apply-progress.md`, replacing false completion/advance claims with truthful retirement evidence while preserving all earlier history. <!-- sdd-owner: implementation -->
- [x] Verify no project, package, manifest, UI adapter, dependency, generated artifact, or test reference to `DiagnosticExport`, no `diagnostics.jsonl` creation remains, and no filesystem or network export path is advertised or enabled. <!-- sdd-owner: implementation -->
- [x] Rerun the clean reviewed 8B.1 baseline suite/build/diff/status and record that the gesture-bound preview, bounded in-memory sinks, clear-data behavior, and explicit unavailable state remain unchanged. <!-- sdd-owner: implementation -->
- [ ] Do not touch `.git/gentle-ai` authority records; stop before Slice 8C Apply and leave lifecycle/staging/commit/push/PR/publication to their owning phases. <!-- sdd-owner: parent -->

**Completion gate:** only the reviewed 8B.1 behavior may be the base for 8C; export must be absent or explicitly unsupported in source, tests, packaging, and artifacts.

### Slice 8C1 — Deterministic self-contained win-x64 recovery publish (~280–340 lines)

**Dependency/base:** reviewed 8B.1 after retired 8B.2 removal; PR #4 targets `feature/aibar-foundation-slice-8b1-diagnostic-command`. **Scope:** fresh-output-only self-contained `Release/win-x64` recovery publish and complete deterministic ZIP/inventory/identity. **Non-goals:** MSIX, SBOM, signing, provenance, notices, installability, lifecycle, and release claims.

**Correction status:** the unsafe pre-correction 8C1 candidate, its destructive-path behavior, tests, apply-progress, and prior green results remain historical and non-authoritative. A fresh-output-only 8C1 candidate has been reconstructed and execution-verified from the current scoped files. No approval, receipt, staging, commit, or Slice 8C2 work is claimed.

**Fresh-output safety contract:** `scripts/Publish-Deterministic.ps1` accepts only a nonexistent output leaf beneath an existing caller-owned directory. Before any process launch or filesystem creation it must reject empty paths, roots, repository/source overlap, an existing file, empty or non-empty directory, any reparse leaf, missing/inaccessible/unclassifiable parent, and any observed reparse ancestor. It repeats observations immediately before collision-failing leaf creation. It contains absolutely no `Remove-Item -Recurse`, deletion, cleanup, replacement, reuse, rollback, or move-over-existing operation. A post-creation failure reports the incomplete caller-owned output and leaves it untouched; retry requires a different nonexistent leaf. These path checks are only observed defense in depth, not an identity-proof claim.

**Implementation work units:**

- [x] Replace `tests/AIBar.Domain.Tests/PackagingTests.cs` with executable `PackagingRecoveryTests.cs`; remove implementation-source text assertions from acceptance evidence, and keep 8C2+ tasks unchanged and unchecked. <!-- sdd-owner: implementation -->
- [x] **RED:** accept the recorded deterministic review finding and immutable prior-candidate evidence that the legacy workflow deleted or replaced caller-owned output; do not rerun unsafe code, delete a sentinel, or reconstruct destructive conditions. The RED gate is satisfied by preserving that evidence reference and requiring executable corrected-contract rejection with sentinel preservation. <!-- sdd-owner: implementation -->
- [x] **GREEN:** rewrite only `scripts/Publish-Deterministic.ps1` (or a narrowly extracted `scripts/packaging/` helper) to perform the complete non-mutating admission sequence, collision-failing fresh leaf creation, self-contained `Release/win-x64` publish, recursive inventory, deterministic ZIP, and manifest outputs. No MSIX/SBOM/signing/provenance/notices or application edits. <!-- sdd-owner: implementation -->
- [x] **GREEN verification:** with a unique existing caller-owned temporary parent, verify sentinel preservation and fail-closed behavior for an existing file, empty/non-empty directory, leaf reparse point and observed reparse ancestor where supported, filesystem root, repository overlap, parent-as-file/unclassifiable parent, and missing parent; assert process-not-launched markers for every pre-creation rejection. `IOException` and `UnauthorizedAccessException` paths must fail closed without publish or cleanup. Actual same-identity access-denied evidence is opportunistic and non-blocking when the environment cannot provide it safely. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE:** run two real publishes under separately created absolute temporary parents and compare recursive publish path sets/hashes, ZIP bytes and entries, inventory bytes, manifest bytes, and stable artifact identity; cover spaces/non-ASCII names, reordered enumeration, and a leaf-creation collision. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE failure path:** inject a controlled post-creation publish failure, assert the reported incomplete output remains untouched by the script, and prove retry uses a new nonexistent leaf; tests clean only their GUID temporary parents after child processes exit and never use fixed repository paths. <!-- sdd-owner: implementation -->
- [x] **REFACTOR/GATE:** verify complete nested runtime/culture/configuration/native artifact inventory and ZIP equality, safe normalized entries, no diagnostic export file/reference, reproducibility, and artifact completeness. Record that no signing, MSIX, SBOM, provenance, notice, installability, lifecycle, release, or adversarial namespace-containment claim is made. <!-- sdd-owner: implementation -->
- [x] Record the failed combined-8C candidate results and review claims as historical/non-authoritative in the later apply-progress phase; stop before Apply until this corrected task set is approved. <!-- sdd-owner: parent -->

### Slice 8C2 — Mandatory distribution split: 8C2A → 8C2B1a → 8C2B1b → 8C2B2

**Dependency chain:** reviewed 8C1 → reviewed 8C2A → reviewed 8C2B1a → reviewed 8C2B1b → reviewed 8C2B2 → 8D → 8E. The unsplit forecast was 790–1,240 lines; this split is mandatory, feature-branch-chain, and has no size exception. 8D owns lifecycle only.

#### Slice 8C2A — Manifest/assets and capability state (~250–300 lines)

**Start:** immutable reviewed 8C1 head; PR #5 base is `feature/aibar-foundation-slice-8c1`. **End:** reviewed valid metadata inputs and deterministic capability state, ready for 8C2B. **Paths:** `scripts/Publish-Deterministic.ps1`, `packaging/AppxManifest.xml`, `packaging/Assets/{StoreLogo.png,Square44x44Logo.png,Square150x150Logo.png,Wide310x150Logo.png}`, `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`.

- [x] **RED:** add executable tests rejecting the current manifest/schema/assets and ambiguous capability claims; cover missing MakeAppx, missing SignTool/certificate/secret, publisher mismatch, stale `.msix`, and secret-bearing logs.
- [x] **GREEN:** implement schema-valid x64 full-trust WPF manifest, decodable dimensioned PNG variants, controlled publisher binding, injected capability discovery/command planning, and explicit `tool-unavailable`, `signing-unavailable`, unsigned, and fail-closed requested-signing states.
- [x] **TRIANGULATE:** fake command tests assert exact sanitized inputs, no secret disclosure, no stale output survival, and no signed/installable claim; separate-root metadata output is canonical and path-free. **Focused:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`. **Runtime:** N/A for 8C2A; MakeAppx/signing execution belongs to 8C2B.
- [x] **GATE/REFACTOR:** verify no lifecycle work, export, SBOM, notices, or application edits; retain explicit unavailable behavior. **Rollback:** revert only 8C2A script/helper, manifest/assets, configuration, and tests; 8C1 remains usable.

#### Slice 8C2B1a — Restore/publish graph reconciliation and truthful hashes (~360–390 lines)

**Start:** reviewed 8C2A outputs/contracts from `f0d15b1`; **end:** reviewed deterministic graph, inventory, and hash contracts for 8C2B1b. Failed combined/B1 completion, checked tasks, generations 3–6, outputs, and claims are historical non-authoritative records; no B1 review, receipt, or commit exists. No legal/provenance promotion, MakeAppx, or signing.

**Allowed paths:** `scripts/Publish-Deterministic.ps1`; `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`; `packaging/{sbom.cdx.json,compliance-manifest.json}`; ignored caller roots under `artifacts/8c2b1/`. No `PROVENANCE.md` alternative.

- [ ] **RED:** in `PackagingDistributionTests.cs`, fail for missing/extra/duplicate/case-colliding nodes, edges, owners/files, leakage, RID/runtime/native/resource omissions, ambiguous ownership, and wrong file-byte SHA-256/length. Copy the process cases unchanged: fixed timeout cancels acceptance and yields `B1_SUBPROCESS_TIMEOUT` with no promotion; timeout/cancel terminates the owned child tree, waits for a grandchild holding output, and accepts nothing; asynchronously drain saturated stdout/stderr and complete or time out deterministically with sanitized capture; startup/nonzero yields `B1_SUBPROCESS_FAILED` and no success receipt; partial output followed by failure leaves staging unaccepted; pre-existing publish/staging file, directory, or journal yields `B1_STALE_OUTPUT` with bytes unchanged. <!-- sdd-owner: implementation -->
- [ ] **GREEN:** in `Publish-Deterministic.ps1`, restore real `project.assets.json`/RID/dependency graphs, publish self-contained `win-x64`, derive component ownership from the graph, reconcile restore↔publish bidirectionally, and emit nested CycloneDX file components with artifact-byte hashes and canonical deterministic inventory/SBOM output. <!-- sdd-owner: implementation -->
- [ ] **TRIANGULATE:** run two caller roots with reordered enumeration, spaces/non-ASCII names, missing/extra files, process timeout/start/nonzero/partial/stale output, and real restore/deps inventory; prove byte-identical canonical outputs without MakeAppx, SignTool, legal promotion, or signing. <!-- sdd-owner: implementation -->
- [ ] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`; runtime is two fresh `artifacts/8c2b1/` roots and real `dotnet restore/publish`; rollback removes only this script/test/manifest-SBOM work and ignored outputs, preserving 8C2A. <!-- sdd-owner: implementation -->

#### Slice 8C2B1b — Legal/provenance evidence and durable promotion/recovery (~360–390 lines)

**Start:** reviewed 8C2B1a graph/inventory/hash contracts; **end:** reviewed durable legal/provenance/compliance promotion and receipt for 8C2B2. No MakeAppx or signing.

**Allowed paths:** `scripts/Publish-Deterministic.ps1`; `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`; `packaging/{provenance.json,license-evidence.json,compliance-manifest.json,THIRD-PARTY-NOTICES.md,LICENSES/**,receipts/8c2b1.json}`; ignored caller roots under `artifacts/8c2b1/`.

- [ ] **RED:** reject incomplete sourced licenses/notices, every closed-schema required/type/pattern/format/additional-property/cardinality/uniqueness/ref violation, mutable origins, secrets/paths, noncanonical bytes, unresolved refs, self-inclusion, and test-only legal fail-open behavior. <!-- sdd-owner: implementation -->
- [ ] **GREEN:** emit complete sourced license/notice evidence and closed provenance/compliance/receipt schemas; implement test-only legal fail-closed logic, canonical tracked evidence, rollback journal, allowlisted atomic per-file promotion, manifest-last acceptance, and durable receipt. <!-- sdd-owner: implementation -->
- [ ] **TRIANGULATE:** inject collision/failure/termination at every allowlisted step; prove journal recovery, `B1_ROLLBACK_INCOMPLETE` blocking, prior-present/prior-absent bidirectional restore, publish-omission cleanup, self-exclusion, unchanged bytes after failure, and byte-identical separate-root evidence. <!-- sdd-owner: implementation -->
- [ ] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`; runtime uses reviewed 8C2B1a outputs in two fresh caller roots, no MakeAppx/SignTool; rollback removes only B1b evidence/promotion/tests and ignored outputs, preserving 8C2B1a. <!-- sdd-owner: implementation -->

#### Slice 8C2B2 — Truthful MakeAppx/SignTool runtime gate (~260–340 lines)

**Dependency/base:** reviewed 8C2B1b; PR #8 base is the reviewed 8C2B1b branch. **End:** truthful packaging capability evidence handed to 8D; 8D depends on reviewed 8C2B2. **Paths:** `scripts/Publish-Deterministic.ps1`, `packaging/AppxManifest.xml`, `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`, ignored `artifacts/8c2b/` only.

- [ ] **RED:** tests fail for unresolved executables, invalid manifest schema/runFullTrust/publisher, publisher mismatch, stale `.msix`, unsafe cleanup, secret-bearing logs, and overclaimed signed/installable states.
- [ ] **GREEN:** implement verified executable resolution and manifest validation; run real `MakeAppx.exe pack`/inspect when available; optionally run SignTool and verify signature/publisher when credentials exist; otherwise emit explicit unavailable/not-run/unsigned state and no overclaim.
- [ ] **TRIANGULATE:** inject missing tools/credentials, tool failure, publisher mismatch, stale output, output-root variation, and signing-request failure; prove cleanup is non-destructive and stale outputs cannot survive as evidence.
- [ ] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`; runtime harness: controlled Windows SDK pack/inspect and optional credentialed sign/verify, otherwise recorded not-run state; rollback only 8C2B2 runtime gate/tests and ignored outputs, preserving reviewed 8C2B1.

### Slice 8D — Install, upgrade, launch, uninstall, and retention smoke (~340 lines)

**Dependency/base:** reviewed 8C2B2, including its export-absence proof; PR #9 targets reviewed 8C2B2. **Scope:** packaging/runtime smoke harness for clean install, migration/upgrade, startup launch, single instance, uninstall and retention/removal. **Non-goals:** manual DPI matrix and legal gates. Smoke coverage must preserve explicit export-unavailable behavior and prove no export artifact is installed or created.

- [ ] **RED:** add smoke tests for clean install, upgrade migration, `--startup`, single-instance activation, uninstall, retained/removed user data, and Codex-file hashes.
- [ ] **GREEN:** wire the install lifecycle harness and migration-safe upgrade/uninstall behavior.
- [ ] **TRIANGULATE:** rerun with existing DB, locked files, failed migration, disabled integration, and explicit data-removal selection.
- [ ] **REFACTOR:** make setup/cleanup idempotent and evidence path-free. Focused: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSmoke|FullyQualifiedName~Lifecycle"`. Runtime: clean Windows sandbox install → launch → upgrade → uninstall. Rollback: remove smoke harness/lifecycle changes; retain reviewed 8C1/8C2A/8C2B1/8C2B2 outputs.

### Slice 8E — Windows matrix and release gates (~280 lines)

**Dependency/base:** reviewed 8D branch, including packaging and smoke proof that export is absent/unsupported; PR #10 targets 8D. **Scope:** W10/W11 manual/VM matrix, private compatibility validation, policy/legal review, remote-free kill-switch and release evidence. **Non-goals:** new product behavior; Slice 7B tiny-positive-rate ETA warning remains deferred. Release evidence must continue to prove no diagnostic export path or artifact exists.

- [ ] **RED:** record failing matrix/gate checks for tray recreation, DPI, taskbar placement, sleep/resume, popover focus, unsupported behavior, policy/legal, dependency/license, provenance, private endpoint compatibility, and kill-switch.
- [ ] **GREEN:** execute and document the matrix and sign-off gates; make unsupported behavior visible and distribution disabled until all pass.
- [ ] **TRIANGULATE:** repeat on representative W10/W11 VMs and with private integration disabled; verify local analytics remain usable and no remote sink exists.
- [ ] **REFACTOR:** consolidate release checklist and rollback procedure. Focused: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Windows|FullyQualifiedName~ReleaseGate"`. Runtime: W10/W11 VM smoke matrix. Rollback: remove gate evidence/configuration and ship disabled; preserve 8D lifecycle.

**Slice 8 chain:** `7C → 8A → 8B.0 → 8B.1 → 8C1 → 8C2A → 8C2B1a → 8C2B1b → 8C2B2 → 8D → 8E`; 8B.2 is retired and not an active dependency. PR #6 starts at `f0d15b1`, PR #7 targets reviewed 8C2B1a, PR #8 targets reviewed 8C2B1b, PR #9 targets reviewed 8C2B2, and PR #10 targets reviewed 8D. Historical failed evidence/attempt records remain non-authoritative; no child inherits their completion claims. No size exception is authorized. 8C2B1a proves graph/SBOM reconciliation, 8C2B1b proves legal/provenance promotion/recovery, 8C2B2 proves truthful MakeAppx/SignTool runtime states, and 8D depends on reviewed 8C2B2.

## Cross-Slice Completion Gates

- [ ] Keep authored changes at or below 400 lines for every slice except the explicitly recorded, maintainer-approved Slice 8A-only `size:exception`; no future slice inherits, extends, or receives that exception.
- [ ] Run the full test suite, static analysis, dependency/license checks, and `dotnet publish` for the supported Windows target.
- [ ] Confirm all MVP non-goals remain unsupported and no code path reads multiple roots/accounts, WSL, browser cookies, passwords, prompt/response bodies, or authoritative billing data.
- [ ] Confirm generated fixtures and screenshots are synthetic/approved and remain bound to the reviewed behavior.
- [ ] Confirm each implementation slice remains independently revertible before opening or advancing its chained PR, and enforce the line-budget rule above with only the recorded Slice 8A exception.
- [ ] Preserve no credentials or user content in test evidence, logs, artifacts, screenshots, or release bundles.
