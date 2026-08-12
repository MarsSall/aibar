# AIBar Foundation Implementation Tasks

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | A 260–360; B1a raw 752–820/authored 0–40; B1b 240–320/240–320; B1c 110–170/110–170; B1d 50–100/50–100; B1e 220–360/220–360; B2 completed candidate native 1,585/authored implementation/configuration/tests 173 under the maintainer-authorized 1,800 native exception; C 300–390; D 160–300; E direct-child evidence 336 historical/partial; E descendant correction 280–360 authored, hard stop 400/1,000 native |
| 400-line budget risk | High overall; every authority-chain child stops/re-slices above 400 authored lines; every B1 child separately stops before tests at 900 raw native changed lines; completed B2 uses the recorded maintainer-authorized 1,800 native exception |
| Chained PRs recommended | Yes |
| Suggested split | A artifacts → B1a-e exact in-project clusters → S signing/key gate → B2 Core extraction + exactly two checked-identity signed Core friends → C C2Authority+Testing + B2-friend transition → D private delete → E checked direct-child evidence → E descendant correction; C3 excluded |
| Review/runtime ceiling | 400 authored per B child and 900 raw native per B1 child are hard stops; completed B2 is the expressly maintainer-authorized exception with a 1,800 native ceiling while preserving its 400 authored-line limit |
| Delivery strategy | auto-chain |
| Chain strategy | feature-branch-chain |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

Fresh Unit E correction forecast: 280–360 authored additions/deletions; Medium risk; one bounded work unit, no further split unless measurement exceeds 400; native ceiling 1,000.

Producer/C1c history remains unchanged. Units A, B1a-e, S, B2, C, D, and scoped Unit E are complete: Unit E's checked direct-child evidence remains historical/partial, while E-C1's descendant correction completed only after its dedicated fail-closed proofs passed; broad C2a/C2b/C3 gates remain unchecked. Attempt 84 remains failed/insufficient; blocked Attempt 85 established a raw native lower bound of at least 1,176 changed lines for combined B. No unapproved 1,000-line threshold is allowed: every B1 child stops at 900 raw native lines and 400 authored lines; completed B2 is governed by the maintainer-authorized 1,800 native exception and the preserved 400 authored-line limit.

Each unit below is a candidate commit/PR with its tests and directly related evidence. Historical completed marks remain historical; B1a through B1e, S, B2, C, D, and scoped Unit E are complete. Unit E tasks 621–622 remain checked historical/partial direct-child evidence; E-C1 tasks 632–635 complete the bounded descendant closure only, with broad C2a/C2b/C3 still unchecked.

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

### Slice 8C2 — Mandatory distribution split: 8C2A → 8C2B1a1 → 8C1.1 → 8C2B1a2 → 8C2B1b → 8C2B2

**Dependency chain:** reviewed 8C1 → 8C2A → B1a1 → reviewed/committed 8C1.1a → reviewed/committed 8C1.1b1 → reviewed/committed 8C1.1b2 → B1a2 → B1b → B2 → 8D → 8E. B1a1/B1a2 replace failed B1a; every child is independently reviewed, feature-branch-chain, <=400 lines, with no exception. 8D alone owns lifecycle.

#### Slice 8C2A — Manifest/assets and capability state (~250–300 lines)

**Start:** reviewed 8C1; PR #5 base is its branch. **End:** reviewed metadata/capability state. **Paths:** `scripts/Publish-Deterministic.ps1`, `packaging/AppxManifest.xml`, `packaging/Assets/*.png`, `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`.

- [x] **RED:** add executable tests rejecting the current manifest/schema/assets and ambiguous capability claims; cover missing MakeAppx, missing SignTool/certificate/secret, publisher mismatch, stale `.msix`, and secret-bearing logs.
- [x] **GREEN:** implement schema-valid x64 full-trust WPF manifest, decodable dimensioned PNG variants, controlled publisher binding, injected capability discovery/command planning, and explicit `tool-unavailable`, `signing-unavailable`, unsigned, and fail-closed requested-signing states.
- [x] **TRIANGULATE:** fake command tests assert exact sanitized inputs, no secret disclosure, no stale output survival, and no signed/installable claim; separate-root metadata output is canonical and path-free. **Focused:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`. **Runtime:** N/A for 8C2A; MakeAppx/signing execution belongs to 8C2B.
- [x] **GATE/REFACTOR:** verify no lifecycle work, export, SBOM, notices, or application edits; retain explicit unavailable behavior. **Rollback:** revert only 8C2A script/helper, manifest/assets, configuration, and tests; 8C1 remains usable.

#### Slice 8C2B1a1 — Pure graph projection, reconciliation, and CycloneDX model (~300–370 lines)

**Start:** reviewed 8C2A plus selective restoration of implementation/test bytes to HEAD `28a2f46`; PR #6 base is the reviewed 8C2A branch. **End:** reviewed pure deterministic model ready for B1a2. **Allowed paths:** `scripts/Publish-Deterministic.ps1`, `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`, `tests/AIBar.Domain.Tests/Fixtures/PackagingGraph/**`, `packaging/{sbom.cdx.json,compliance-manifest.json}`. No subprocess, legal/provenance promotion, MakeAppx, or signing.

- [x] **RED:** consume real-format `project.assets.json`, `.deps.json`, publish inventory, and 8C1 `recovery-inventory.json`; mutate each authoritative input to require exact edges/reachability and fail bidirectional omission/extra/duplicate/case collision/ambiguity/test leakage/artifact-byte/hash/length mismatches. <!-- sdd-owner: implementation -->
- [x] **GREEN:** implement deterministic graph projection with authoritative direct/transitive/runtime/native/resource/first-party ownership, exact dependency edges/reachability, no heuristic or fallback owner, and nested CycloneDX file components with recomputed artifact-byte SHA-256/length. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE:** reconcile checked real-format snapshots and approved synthetic inputs under reordered enumeration and spaces/non-ASCII names; require canonical ordinal JSON plus terminal LF and byte-identical output while every authoritative-input mutation fails closed. <!-- sdd-owner: implementation -->
- [x] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution&FullyQualifiedName~GraphProjection"`; runtime harness N/A because B1a1 is a pure consumer and B1a2 proves live acquisition. Rollback reverts only B1a1 script/model, fixtures/tests, and SBOM/manifest outputs, preserving 8C2A. <!-- sdd-owner: implementation -->

#### Prerequisite Slice 8C1.1 — Opt-in isolated restore and MSBuild roots (replanned)

**Status:** the generation-22/23 candidate remains unaccepted after generation 24 found its admission, process-tree, cleanup, repository-wide isolation, deterministic-evidence, and RED coverage incomplete. The maintainer authorized this split on 2026-07-24. Do not claim either child, the prerequisite, or B1a2 complete from the existing candidate or its prior evidence.

**Shared boundary:** reviewed/committed B1a1; each child targets its immediate predecessor in the `feature-branch-chain`. Only `scripts/Publish-Deterministic.ps1` and `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` may change. No packaging policy, B1a1 graph, runtime artifact, MakeAppx, or SignTool path. B1a2 remains blocked until 8C1.1a and both b1 children are reviewed, receipted, and committed. Every child has a hard <=400 authored-line cap, hard 20-minute apply deadline, stop-new-work at minute 18, commands <=5 minutes, and no size exception.

##### Slice 8C1.1a — Isolated admission and deterministic MSBuild routing (~260–340 authored lines)

- [x] **RED:** prove default invocation remains exactly compatible; before launch reject partial parameters, non-absolute/root/overlapping/NFC-invalid children, repository/worktree containment in either direction, missing/mismatched marker, existing file/directory/collision, inaccessible/unclassifiable parent, observed reparse leaf/ancestor, and per-project child collisions while preserving sentinels and a no-launch marker. <!-- sdd-owner: implementation -->
- [x] **GREEN/TRIANGULATE:** admit only one marker-owned external parent with distinct fresh children; revalidate admission immediately before restore and publish; forward identical project-separated `BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath`, `RestoreOutputPath`, `BaseOutputPath`, and `RestorePackagesPath` through explicit restore and `publish --no-restore`; prove two Unicode/space roots preserve repository-wide `obj/**` and `bin/**`, and yield byte-identical inventory, ZIP, manifest, and artifact identity. <!-- sdd-owner: implementation -->
- [x] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingRecovery"`; runtime harness uses two caller-owned external roots. Rollback reverts only this child’s routing/admission/tests, preserving reviewed 8C1 defaults. <!-- sdd-owner: implementation -->

##### Slice 8C1.1b1a — Deterministic executable child/grandchild harness and RED proof (implementation complete; evidence closure recorded)

**Dependency:** reviewed/receipted/committed 8C1.1a. Chain remains **b1a → b1b → b2 → B1a2**. **Review Workload Forecast:** 180–260 authored additions/deletions; risk Low; hard maximum 400; no chained PR recommendation. Apply: ask-always, one isolated work unit; hard 20 minutes; stop new work at minute 18; each command <=5 minutes. Scope is harness/test infrastructure only; no production lifecycle implementation.

- [x] **RED:** Add failing coverage for marker nonce/content, canonical containment, nested-tree reparse rejection, direct-child plus grandchild PID/start-time records, and finite non-saturating stdout/stderr writes; assert no cleanup or saturated-pipe premise.
- [x] **GREEN:** Correct only `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`: concurrently drain compiler streams under an enforceable timeout/termination/wait, validate both explicit identities/owned handles, and gate recursive deletion on exact marker, canonical containment, and a complete owned-tree reparse scan.
- [x] **TRIANGULATE:** Prove publisher/direct completion with the known grandchild alive, finite stdout/stderr coverage without saturation, bounded release/targeted teardown, immediate repeatability, no descendants, and no prohibited sleeps, polling, `.cmd`, global scan, or unbounded wait.
- [x] **GATE:** Focused b1a test, immediate repeat, exact outputs/hashes where the contract requires, and `git diff --check`; leave the root untouched on any failed ownership check. b1b remains responsible for production lifecycle integration.

**Independent verification disposition:** Native attempt 36 and its receipt remain historical evidence. The four implementation gaps were remediated in terminal attempt 37, and approved receipt `review-246b855802905e01` binds the post-correction test bytes and resolves `R3-001`. Maintainer-authorized evidence closure recorded fresh exact focused-test and current build output digests without changing code. The verifier-owned report remains the authority for lifting the evidence gate; b1b stays blocked until that verification is accepted and the existing review/receipt/commit dependency is satisfied.

##### Slice 8C1.1b1a remediation — bounded correction completed; evidence closure complete

**Forecast:** 220–300 authored lines, risk Medium, one PR/work unit, no chain split; correction stays below the 400-line budget. Allowed implementation path is `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`; this task ledger is the only planning artifact to change now.

- [x] **RED:** Encode the four failing cases above before correction; preserve the accepted descendant-liveness RED and explicitly avoid saturation/deadlock as proof.
- [x] **GREEN:** Make the harness satisfy the exact identity, concurrent-drain/timeout, finite-stream, marker/nonce, canonical-containment, and nested-reparse contracts without touching production, b1b, b2, B1a2, native authority, or review records.
- [x] **TRIANGULATE/GATE:** The correction executed under native authority. The later maintainer-authorized evidence-closure unit captured the current focused b1a test and build output bytes/digests, confirmed no scoped descendants, and passed `git diff --check`; it made no functional code or test behavior change.

**Work-unit evidence:** runtime harness is the generated external child/grandchild scenario; rollback is reverting only the remediation changes in `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` plus this b1a remediation/task evidence, preserving reviewed 8C1.1a, defaults, and all downstream unchecked tasks. Dependency remains `b1a → b1b → b2 → B1a2`; downstream stays blocked until correction is independently verified, newly reviewed/receipted, and committed.

##### Slice 8C1.1b — Owned process lifecycle implementation (~180–260 authored lines)

**Dependency:** reviewed/receipted/committed 8C1.1b1a. **Review Workload Forecast:** 180–260 authored additions/deletions; risk Medium; hard maximum 400; no exception. Apply: hard 20 minutes; stop new work at minute 18; each command <=5 minutes. Runtime: reviewed b1a deterministic child/grandchild harness. Rollback removes only lifecycle implementation/integration tests/evidence marks; preserve b1a, 8C1.1a, and reviewed 8C1 defaults.

- [x] **RED:** deterministic tests cover bounded timeout/cancellation, fail-closed known-descendant identity/quiescence, launch failure, cleanup guarantees, and saturated concurrent stdout/stderr draining.
- [x] **GREEN:** the lifecycle helper passes the narrowed contract: bounded cancellation, fail-closed known-descendant identity/quiescence, launch-failure handling, cleanup guarantees, and concurrent saturated-stream draining.
- [x] **TRIANGULATE:** the narrowed lifecycle matrix is executable and passing for timeout/cancellation, known-descendant refusal, launch failure, cleanup, and saturated stdout/stderr streams.
- [x] **GATE:** final independent scoped verification passed the focused owned-lifecycle tests, full `PackagingRecovery` filter, solution build, whitespace check, task/evidence alignment, and zero-Harness-process check. No marker-gated cleanup integration or containment revalidation belongs here. B1a2 remains blocked.

**Scope-correction rationale:** The prior nonzero-exit test could not distinguish exit code 7 from cancellation and placed its sentinel outside the recovery output. Further proof was judged disproportionate to current AIBar progress. The deferred hardening backlog item **B1B-HARDEN-NONZERO-PARTIAL-OUTPUT** will restore deterministic nonzero-exit-with-partial-output preservation proof outside the current b1b closure path; it is not a b1b acceptance requirement.

##### Slice 8C1.1b2 — Replanned kernel-owned supervisor (b2a committed; b2b only)

**Dependency:** reviewed/committed b2a; b2c remains unchanged and out of scope. Base `feature/aibar-foundation-slice-8c1-1b2b`; child units target their immediate predecessor in the feature-branch chain. No cleanup/scavenger, PowerShell, policy, packaging, signing, `.gitignore`, receipts, or application runtime work.

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

Historical mapping (not fixed here): `JD-B2B-001` → units 1–2; `JD-B2B-002` → units 2–3; `JD-B2B-003` → unit 3; suspects `JD-A-B2B-004` and `JD-B-B2B-003` → unit 3. Preserve the ledger unchanged.

###### 8C1.1b2a — Typed protocol, state machine, and supervisor foundation (~300–360 lines)

- [x] **8C1.1b2a-RED:** Add fake-driven tests for schema/version/operation/timeout/argument bounds, rejection of PID/root/nonce/shell/environment authority, every state transition, cancellation and terminal-status precedence, bounded diagnostics, and response secret/path freedom.
- [x] **8C1.1b2a-GREEN:** Add the BCL/Win32-only project, typed protocol, stable statuses (`INVALID_REQUEST` through `INTERNAL_UNKNOWN`), state machine, injected clock/randomness/interop ports, and one length-bounded JSON stdin/stdout boundary; keep the application projects independent.
- [x] **8C1.1b2a-TRIANGULATE:** Mutate every closed-schema field, reorder/duplicate packets and callbacks, exhaust limits, cancel at each transition, and assert deterministic response bytes plus no arbitrary command text, root spelling, PID, nonce, exception, or secret.
- [x] **8C1.1b2a-GATE:** Run `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`, `dotnet build AIBar.sln --no-restore --nologo`, and `git diff --check`. Rollback removes only this project/reference/protocol/tests; b1b remains runnable.

###### Unit 1 — Production interop and suspended launch (220–300 review lines)

**Paths:** `JobObjectInterop.cs`, `ProcessSupervisor.cs`, b2a project files, focused tests. **Start:** b2a fake seam; **end:** production `IProcessSupervisorInterop`, dedicated SafeHandles, Job creation/configuration (`KILL_ON_JOB_CLOSE`, no breakaway), completion-port ownership, pipes, explicit inherited-handle list, suspended `CreateProcessW`, assignment-before-resume, safe process/thread/job/port/pipe closure, and root exit-code retrieval.

- [x] **RED:** fake tests fail for every pre-resume native failure and assert exact call order, handle ownership, inherited-handle allowlist, and deterministic failure codes.
- [x] **GREEN:** implement only production native interop/launch and safe-handle disposal; never expose paths, PIDs, commands, secrets, or exceptions.
- [x] **TRIANGULATE:** fake fault matrix plus a Windows native `ProcessSupervisor`/`WindowsProcessSupervisorInterop` event-gated helper proves it signals only after launch resumes, is already Job-contained, and exits after launch disposal.
- [x] **GATE:** focused supervisor tests, build, `git diff --check`, and zero helper-process check. Rollback only this unit; b2a stays runnable.

###### Unit 2 — Concurrent bounded drains and authoritative quiescence (250–330 review lines)

**Start:** Unit 1; **end:** concurrent bounded stdout/stderr 64-KiB tails with discard counts, EOF grace, completion-port advisory wakeups, deadline handling, and authoritative repeated live `ActiveProcesses == 0` proof after normal exit and termination.

- [x] **RED:** event-gated saturation and fake tests cover lost/duplicate/reordered packets, descendant-delayed zero, root exit retrieval, and two queries separated by the observation interval.
- [x] **GREEN:** implement bounded drains and query loop; success requires signaled root, zero exit, two successful zero-active queries while handles remain open.
- [x] **TRIANGULATE:** prove packets never decide outcome, saturation never hangs, and descendants delay success.
- [x] **GATE:** focused tests, Windows helper with child/descendant events, build, diff check, and no surviving helpers. Rollback only Unit 2.

###### Unit 3 — Cancellation/timeout containment and classification (180–260 review lines)

**Start:** Unit 2; **end:** timeout/cancellation/query-failure containment, draining-task cancellation/cleanup, deterministic status classification, and safe terminal ownership.

- [x] **RED:** inject cancellation/timeout/query/EOF failures and assert `TIMEOUT`, `CANCELLED`, `OUTPUT_DRAIN_FAILED`, or `QUIESCENCE_UNPROVED` without premature success.
- [x] **GREEN:** terminate the Job, use the same bounded repeated proof, cancel/await drains safely, retrieve exit where possible, and close every handle deterministically.
- [x] **TRIANGULATE:** repeat fake matrix and event-gated saturation/descendant tests; assert no hangs, leaked pipes, paths/PIDs/secrets, or b2c scavenging.
- [x] **GATE:** focused test, build, `git diff --check`, zero-helper/process check, and review-line receipt. Rollback Unit 3 only.

###### 8C1.1b2c — Replanned cleanup chain (C1a → C1b0 → C1b1 → producer → C1c → C2a → C2b; C3 excluded)

**Dependency:** reviewed/committed b2b Unit 3. Feature-branch-chain order is C1a → C1b0 → C1b1 → producer commit `03ee654` → C1c → C2a → C2b; C3 is excluded. C1c, C2a, and C2b are separate feature-branch-chain units with rollback-safe boundaries. C1c implementation completed under its historical 400 cap; its distinct independent-verification objective has a native changed-line ceiling of 1000. Future C2a and C2b each have a native changed-line ceiling of 1000, while crossing 400 retains mandatory workload visibility/review-splitting pressure. The larger ceilings are evidence/correction headroom only and grant no scope, dependency, unit-merging, C3, or delivery authority. No unit claims complete cleanup until its own failure semantics are proven.

**Review mapping:** `JD-B2C-C1-001` maps to C1a's retained-capability admission and C1b0/C1b1's identity-bound commit chain. The confirmed TOCTOU is not accepted as repaired by path revalidation alone; C1b1 must prove the amended handle-bound Windows strategy without a path fallback.

##### b2c-C1a — Retained capability and admission foundation (~180–260 lines)

**Dependency/base:** reviewed/committed b2b Unit 3; C1b targets reviewed C1a. **Allowed paths:** `tools/AIBar.Packaging.Supervisor/DirectoryCapability.cs`; `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`; this task ledger only. **End:** live retained root capability with exact root/direct-child identities, final paths, volume, canonical containment, reparse, and allowlist validation. No rename, mutation, deletion, scavenger, or PowerShell.

- [x] **RED:** Add failing tests for root/child identity, final handle paths, same-volume proof, containment, non-reparse state, exact allowlist, missing/extra/substituted entries, access faults, and identity/reparse races; prove admission failure leaves bytes untouched.
- [x] **GREEN:** Implement supervisor-created collision-failing capability handles and read-only admission/revalidation that retains live handles and captured identities through the commit boundary; expose only safe statuses and no sensitive values.
- [x] **TRIANGULATE:** Reorder enumeration and vary Unicode/space roots, volumes, access, identity, final-path, containment, and reparse observations; assert deterministic refusal, no mutation, and no path/secret output.
- [x] **GATE:** Run `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor"`, `dotnet build AIBar.sln --no-restore --nologo`, `git diff --check`, and zero-helper check. Rollback removes only C1a code/tests/task marks.

##### b2c-C1b0 — Native API contract proof/readiness gate (~120–190 authored lines)

**Dependency/base:** reviewed/committed C1a; C1b1 is blocked until this unit independently passes. **Paths:** `tools/AIBar.Packaging.Supervisor/DirectoryCapability.cs`, `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`, this ledger only. **End:** proven native readiness; no production quarantine mutation. **Forbidden:** C1b1 production commit, Win32 rename, path fallback, deletion, rename-back, scavenger, PowerShell, shell, or helper fallback.

- [x] **RED:** Add test-first fake/contract cases for the applicable threat-matrix boundaries: external-root identity/reparse/collision/extra-entry refusal, cleanup ownership before commit, and native security integration. On Windows 10/11 x64 assert pointer size 8, `FILE_RENAME_INFORMATION` size 24/offsets 0,8,16,20, `IO_STATUS_BLOCK` size 16/offsets 0,8, `ntdll!NtSetInformationFile` class 10, direct `STATUS_SUCCESS` only, source `DELETE|SYNCHRONIZE`, parent `FILE_TRAVERSE|FILE_READ_ATTRIBUTES|SYNCHRONIZE`, and both handles without `FILE_SHARE_DELETE`; cover invalid-leaf no-call, collision no-overwrite, and retained source plus quarantine-parent relative success.
- [x] **GREEN:** Extend `DirectoryCapability.cs` to retain source `DELETE|SYNCHRONIZE` and a distinct non-reparse same-volume quarantine-parent handle with no `FILE_SHARE_DELETE`; prove `ntdll!NtSetInformationFile(FileRenameInformation)` with exact checked native buffer/layout and `RootDirectory` contract, without production commit behavior.
- [x] **TRIANGULATE/GATE:** Exercise source/parent identity substitution, reparse, containment, same-volume, exact child-set, access/share, unsupported OS/architecture/entrypoint/class, pending/non-success status, one-way child release, exact buffer/handle disposal, and zero residue. Run focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`, `dotnet build AIBar.sln --no-restore --nologo`, `git diff --check`, and `Get-Process -Name Harness -ErrorAction SilentlyContinue` (zero helpers).

**Rollback:** remove only C1b0 capability/test changes and task marks; retain verified C1a and the preserved historical C1b1 boundary. C2a, C2b, and C3 remain unchecked.

##### b2c-C1b1 — Historical identity-bound quarantine commit (preserved)

**Historical boundary:** commit `864bb14` completed the native C1b quarantine transition after verified C1b0. Its existing checked marks remain historical and are not rewritten. They prove the native commit boundary only; they do **not** prove the newly amended `CommittedChildEvidence/v1` transfer. C2 attempt 58 failed before code with evidence `sha256:0f78dfff7797e09d12bcdb10d06ee7ed50f1c448cabd0a708a6c4ca80460ecde`, so no C2 behavior is inferred from that attempt.

**Preserved scope:** `tools/AIBar.Packaging.Supervisor/Cleanup.cs`, `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`, and this ledger. The historical unit forbids `SetFileInformationByHandle`, Win32/path/absolute-target move, reopen-by-source-path, shell, subprocess/helper fallback, replacement, copy/delete, target-changing retry, deletion, rename-back, scavenger, PowerShell, and C2/C3 behavior. The new producer amendment below is a separate unit.

- [x] **RED:** Preserve the historical pre-production tests for identity/reparse/share/containment/same-volume/complete-child-set gates, source/parent substitution, invalid leaf, collision, unsupported/native failures, child transition/disposal, and post-success uncertainty. <!-- sdd-owner: implementation -->
- [x] **GREEN:** Preserve the historical single native relative rename, bounded buffer, closed `NTSTATUS` handling, `CLEANUP_REFUSED` pre-commit classification, and retained `CLEANUP_PARTIAL` post-issue boundary. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE/GATE:** Preserve the historical focused/full/build/runtime evidence and no-fallback/no-deletion/no-rename-back boundary; do not reinterpret it as C1b→C2 child-evidence authority. <!-- sdd-owner: implementation -->

**Historical rollback:** reverting `864bb14` is not part of this amendment. Any rollback involving the new consumer must disable/remove C2 before removing the producer amendment; the historical C1b capability remains the predecessor boundary.

##### b2c-C1b-evidence — Immutable child-evidence producer and capability transfer (completed at `03ee654`)

**Dependency/base:** reviewed/committed `864bb14` and verified C1b0; native attempt 58 is terminal failed with `decision_required` and is not retryable. **Allowed paths:** `tools/AIBar.Packaging.Supervisor/DirectoryCapability.cs`, `tools/AIBar.Packaging.Supervisor/Cleanup.cs`, `tools/AIBar.Packaging.Supervisor/CommittedChildEvidence.cs` (new if needed), `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`, and this task ledger. **Start:** C1b can commit a retained quarantine but has no immutable child-evidence transfer. **End:** C1b freezes and transfers `CommittedChildEvidence/v1` exactly once with the live retained source/parent capability. **Forbidden:** changes to `.gitignore` or `.git/gentle-ai`; Win32 rename projections, path fallback, arbitrary-root selection, deletion, rename-back, scavenging, PowerShell, shell/helper fallback, C2 consumer deletion, and C3 behavior.

- [x] **RED:** Add failing fake and Windows-contract tests for the closed v1 shape: retained root and quarantine-parent `FILE_ID_INFO`, one 32-byte evidence digest, one 32-byte per-operation correlation key, at most 16 ordinal-ignore-case collision-checked direct-child records, simple leaves of at most 255 UTF-16 code units, HMAC-SHA-256 leaf tags, object kind, volume/file ID, and expected non-reparse state. Reject missing/duplicate/unknown/overflow/larger records before any native call. <!-- sdd-owner: implementation -->
- [x] **GREEN:** Implement immutable capture while root, parent, and all admitted child handles remain live; derive records only from handles, compute tags/digest, freeze the value before child-handle release and before the native rename, and transfer evidence plus correlation key and live source/parent handles exactly once on success. No plaintext leaf, absolute path, nonce, handle value, credential, command text, or mutable caller replacement may be stored or emitted. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE:** Prove capture-before-release/call ordering, one-way ownership transfer, idempotent refusal disposal, immutable post-freeze behavior, exact digest/key binding, source/parent identity continuity, collision and invalid-leaf no-call behavior, and the documented non-atomic child-release window. Preserve every historical C1b0/C1b1 refusal case and prove no C2 deletion is reachable from this producer-only unit. <!-- sdd-owner: implementation -->
- [x] **GATE:** Run focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor|FullyQualifiedName~CommittedChildEvidence" --no-restore -m:1 --nologo`, full `dotnet test AIBar.sln --no-restore -m:1 --nologo`, `dotnet build AIBar.sln --no-restore --nologo`, `git diff --check`, and the bounded Windows 10/11 x64 native C1b runtime proof. Record zero helper/process and zero temporary-root residue; do not claim C2 deletion, C3, packaging, or release. <!-- sdd-owner: implementation -->

**Acceptance evidence:** immutable v1 schema/serialization inspection, fake ordering/disposal matrix, focused/full test results, clean build, native runtime identity/collision/no-residue proof, and source inspection showing no path or secret leakage. **Review boundary:** one feature-branch-chain PR targeting the reviewed `864bb14` line; maintainer-approved hard cap 800, forecast 260–360, risk Medium. The larger ceiling covers planning/evidence and bounded corrections only; it does not authorize C2, C3, or unrelated scope. **Native attempt handoff:** before apply, the parent must run `gentle-ai sdd-attempt status --cwd <repo> --change aibar-foundation`; it must not reset, retry, begin, or finish attempt 58. If native status remains `decision_required`, use only the maintainer-authorized reset for this distinct producer objective before any actor launch. **Rollback:** if C2 has not landed, remove only the producer amendment and its tests/evidence, retaining `864bb14`; if C2 has landed, disable/remove the C2 consumer first, then remove this producer. Never leave an enabled consumer without its producer contract.

##### b2c-C1c — Atomic capability-facet transfer (220–320 authored additions/deletions)

**Start state:** committed producer `03ee654`; failed Attempts 60/61 are historical and non-reusable; the partial C2a functional candidate was rolled back. **Forward chain:** producer `03ee654` → C1c → C2a → C2b. **Rollback:** C2b → C2a → C1c → producer. C1c is a separate feature-branch-chain boundary with intended branch `feature/aibar-foundation-slice-8c1-1b2c-c1c-facets`; under `auto-chain`, branch creation/change follows the parent-owned apply phase without a fresh delivery decision.

**Allowed implementation paths only:** `tools/AIBar.Packaging.Supervisor/CommittedChildEvidence.cs`; the minimum transfer seam in `tools/AIBar.Packaging.Supervisor/DirectoryCapability.cs` only if proven necessary; focused ownership tests in `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`; and C1c task/progress/verification artifacts only. **Excluded:** C2a native operations, C2b policy/cleanup, C3, enumeration/reopen/observation/delete, new native APIs, DPAPI/HMAC, path fallback, shell/PowerShell/subprocess, stage/commit/push/PR/review. C1c implementation retained a hard cap of 400 authored changed lines with no exception.

**Review workload:** implementation forecast 220–320 authored additions/deletions; High implementation risk; Medium 400-line risk requiring continuous monitoring; chained PR recommended Yes because this is a separate prerequisite/review boundary, not because it exceeds 400. Completed apply Attempt 62 recorded 347/400 and remains historical; no checked implementation task is reclassified as independent verification.

**RED**

- [x] Add deterministic failing tests before production edits for paired-only exact-once split, no one-facet overload, repeated/concurrent split, and split-versus-dispose linearization; assert one winner and no visible partial handoff. <!-- sdd-owner: implementation -->
- [x] Add deterministic failure-injection tests at validation, binding creation, facet allocation, factory, and pre-publication boundaries; capture original ownership intact on pre-publication failure with no leak, duplication, or double ownership. <!-- sdd-owner: implementation -->
- [x] Add tests for original inertness after publication, independent disposal in both orders with exact handle-close and evidence-zeroization counters, same-handoff binding acceptance, cross-handoff/legacy/reconstructed rejection, orphaned tree/evidence facets, C2a failure preserving evidence, and diagnostics free of sensitive values. <!-- sdd-owner: implementation -->
- [x] Capture a real RED compile or assertion failure from the deterministic suite and record that no production implementation is permitted during RED. <!-- sdd-owner: implementation -->

**GREEN**

- [x] Implement the smallest producer-side seam: one atomic exact-once paired handoff consuming one `CommittedQuarantineCapability`, with no tree-only or evidence-only overload and no behavior outside C1c. <!-- sdd-owner: implementation -->
- [x] Implement two non-cloneable, non-serializable, non-reconstructible facets with disjoint ownership: `RetainedTreeCapabilityFacet` for only the exact root/quarantine-parent handles and captured observations, and `CommittedEvidenceCapabilityFacet` for only `CommittedChildEvidence/v1`, digest, correlation key, child-record sensitive buffers, and zeroization. <!-- sdd-owner: implementation -->
- [x] Implement the synchronized `Whole`/`Splitting`/`Split`/`Disposed` state machine with one linearization gate; only the `Whole` to `Splitting` winner may stage a pair, losers obtain no facet, pre-publication failures deterministically clean staged resources while retaining original ownership, and an unexpected post-detach publication failure disposes unpublished resources without duplicating or restoring ownership. <!-- sdd-owner: implementation -->
- [x] Preserve one internal opaque same-handoff `HandoffIdentity` reference in both facets for future C2a/C2b consumers without exposing raw representation; publish both facets together, make the original permanently inert, and never restore ownership by duplicating handles or sensitive buffers. <!-- sdd-owner: implementation -->
- [x] Implement independent idempotent disposal and deterministic cleanup for every failure boundary: the tree facet closes only transferred root/parent handles, the evidence facet zeroizes/disposes only evidence-owned buffers, and orphaned peers are never implicitly disposed or reconstructed. <!-- sdd-owner: implementation -->

**TRIANGULATE/REFACTOR**

- [x] Run bounded repeated race schedules for concurrent/repeated split and split-versus-dispose; prove exactly one winner, no deadlock, no partial visibility, and no duplicated/restored ownership. <!-- sdd-owner: implementation -->
- [x] Exercise every failure-injection boundary and post-publication disposal order; prove paired-only visibility, exact-once cleanup counters, original inertness, and C2a-failure evidence preservation. <!-- sdd-owner: implementation -->
- [x] Run a real producer-handoff sandbox using only a fresh test-owned capability through the producer seam; prove handle/evidence lifetime and both disposal orders with no C2a operation, cleanup authorization, enumeration, reopen, observation refresh, delete, DPAPI, HMAC, or C2 completion claim. <!-- sdd-owner: implementation -->
- [x] Remove test-only seams that could construct or substitute production facets; retain only the minimum internal injection needed for deterministic failure tests and keep binding identity authority-internal. <!-- sdd-owner: implementation -->
- [x] Track authored additions/deletions continuously and stop for replanning at 400; audit that production changes remain limited to the allowed C1c paths and that C2a/C2b/C3 behavior is absent. <!-- sdd-owner: implementation -->

**GATE/DELIVERY**

- [x] Retain the RED evidence, run the focused C1c/PackagingSupervisor tests and an immediate repeat, execute the bounded real Windows x64 producer-handoff sandbox with exact cleanup/process-residue checks, run the serialized full solution suite, and record a clean zero-warning/zero-error build, `git diff --check`, exact authored-line count, and allowed-path audit. <!-- sdd-owner: implementation -->
- [x] Prove there is no scoped helper/process/root residue, `.gitignore` is untouched and unstaged, no sensitive diagnostic value is emitted, and no C2a native operation, C2b policy/cleanup, C3, new native API, DPAPI/HMAC, path fallback, shell, PowerShell, subprocess, stage, commit, push, PR, or review action occurred. <!-- sdd-owner: implementation -->
- [x] Keep every C1c checkbox unchecked until its corresponding proof actually passes; do not claim implementation, verification, review, commit, or completion from Attempts 60/61 or the rolled-back C2a candidate. <!-- sdd-owner: implementation -->

**Parent-owned C1c lifecycle gates**

- [x] Plan one fresh native implementation objective for C1c only with proposed max attempts 1 and max changed lines 400; under `auto-chain`, begin when fresh `gentle-ai sdd-attempt status --cwd <repo> --change aibar-foundation` confirms `next_action=begin`, without reusing Attempts 60/61 or requiring a fresh delivery decision. <!-- sdd-owner: parent -->
- [x] Create or change `feature/aibar-foundation-slice-8c1-1b2c-c1c-facets` within the parent-owned `auto-chain` apply phase; perform no stage/commit/push/PR/review without separate maintainer authorization, and leave native-ledger operations and any C2a/C2b advancement to their parent-owned phases. <!-- sdd-owner: parent -->

**Next objective — independent C1c verification**

- [x] Complete the distinct corrected-C1c independent-verification objective under parent-owned Attempt 65 (max attempts 1; native changed-line ceiling 1000), bound to exact Attempt 64 tree `7e6985758d6d3568f83ccd3a399713832c48cd4e` and corrected apply evidence `sha256:70c234cfcea9cb2b6ed03fde60cd62a10404e344fc90e6f48c5c9554bc5367fb`; supersede failed independent evidence `sha256:f5ee850b454268844b76d4bf6f0168ccac305395c5d87fb9f670d0fa4a14feb0` only for corrected C1c, with no functional edit, attempt lifecycle mutation, scope expansion, C3, stage, commit, push, PR, or review action. <!-- sdd-owner: parent -->

##### C1c verification correction — Attempt 64 only

- [x] **RED:** retain the focused compile failure proving the prior handoff had no atomic take-both operation and no deterministic post-detach containment point. <!-- sdd-owner: implementation -->
- [x] **GREEN:** replace independent facet extraction with producer-issued atomic take-both; make concrete facets, identity, constructors, and attachment authority private; contain a post-detach failure by disposing unpublished resources and terminally inverting the original. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE:** use instance-scoped failure injection to cover every pre-detach stage and post-detach containment; repeat concurrent split/dispose schedules; prove wrapper abandonment and either peer disposal leave the released peer independently owned; reject cross-handoff binding. <!-- sdd-owner: implementation -->
- [x] **GATE:** run C1c focused + immediate repeat, Windows x64 producer lifetime proof, all PackagingSupervisor tests, serialized solution tests, clean build, diff/cleanup checks, and preserve the separate independent-verification checkbox as unchecked. <!-- sdd-owner: implementation -->

##### b2c-C2 strong-boundary chain — A → B1a → B1b → B1c → B1d → B1e → S → B2 → C → D → E

Units B1a through B1e, S, B2, C, D, and scoped Unit E are complete; E's checked 621–622 direct-child evidence remains historical/partial, and E-C1 632–635 closes only bounded descendant cleanup after the dedicated fail-closed proofs pass. Delivery is `auto-chain` using `feature-branch-chain`; Unit A base is the feature/tracker branch and each later unit targets the immediate predecessor branch. Any child above 400 authored additions plus deletions MUST stop and re-slice; every B1 child also stops before tests at 900 raw native changed lines. Completed B2 is the maintainer-authorized exception with an authoritative 1,585 native lines under the 1,800 ceiling, while its 173 authored implementation/configuration/test lines remain under 400. Broad C2a, C2b, and C3 completion remain unchecked.

###### Unit A — Artifact authority amendment (260–360 changed lines)

- [ ] Amend only `openspec/changes/aibar-foundation/{specs/aibar-foundation/spec.md,design.md,tasks.md}` to establish the physical graph, ownership, signing/friend, publish exclusion, migration, rollback, threat-model, and raw-delete contracts; preserve Attempt 84 as failed and preserve dot-record, `FileStandardInfo=1`, status-normalization, and unrelated task history. <!-- sdd-owner: implementation -->
- [ ] **Contract command:** `git diff --check -- openspec/changes/aibar-foundation/specs/aibar-foundation/spec.md openspec/changes/aibar-foundation/design.md openspec/changes/aibar-foundation/tasks.md`; **runtime:** N/A, artifact-only with no executable change; **forecast:** 260–360 changed lines; **measured scoped artifact diff before this corrective rerun:** `+135/-185 = 320`, inside forecast and below 400; **stop:** only these three paths, with a hard stop/re-slice at 400 changed lines; **rollback:** revert only this amendment; **base:** feature/tracker branch. <!-- sdd-owner: implementation -->

###### Unit B1a — Materialize complete evidence/facet file in-project

- [x] Move the complete current `tools/AIBar.Packaging.Supervisor/CommittedChildEvidence.cs:1-376`, including using directives and namespace, byte-identically to `tools/AIBar.Packaging.Supervisor/Core/CommittedChildEvidence.cs`; no subset extraction or semantic edit. **Dependency/base:** Unit A branch. **Forecast:** raw native 752–820; authored review 0–40 after proof; hard stops 900 raw/400 authored. **Proof:** compare the complete 1-376 file as one whole-file raw-byte blob; record pre/post SHA-256 + Git blob IDs, raw-byte equality, rename-aware `R100`/100% similarity, `numstat`, and explicit edited-line list; normalization may be diagnostic only and cannot support a partial or non-raw identity claim. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`; **runtime:** N/A, byte-identical in-project whole-file relocation; **rollback:** move only this complete file back byte-identically. <!-- sdd-owner: implementation -->

###### Unit B1b — Materialize directory capability cluster

- [x] Extract exact current `DirectoryCapability.cs:11-130` (`DirectoryIdentity`, `DirectoryObservation`, `IDirectoryCapabilityFileSystem`, `DirectoryCapability`) to `tools/AIBar.Packaging.Supervisor/Core/DirectoryCapability.cs`. **Dependency/base:** B1a branch. **Forecast:** raw 240–320; authored 240–320, with NO relocation exclusion because this is a monolithic-file subset; stops 900 raw/400 authored. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`; **runtime:** bounded read-only Unicode/space capability admission; **rollback:** restore this exact cluster to its prior anchor after line 9. <!-- sdd-owner: implementation -->

###### Unit B1c — Materialize native quarantine-rename cluster

- [x] Extract exact current `DirectoryCapability.cs:132-186` (`NativeReadinessResult`, `NativeRenameReadiness`) to `tools/AIBar.Packaging.Supervisor/Core/NativeRenameReadiness.cs`. **Dependency/base:** B1b branch. **Forecast:** raw 110–170; authored 110–170, no relocation exclusion; stops 900 raw/400 authored. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`; **runtime:** bounded Windows x64 retained source-to-parent rename harness; **rollback:** restore this exact cluster after `DirectoryCapability`. <!-- sdd-owner: implementation -->

###### Unit B1d — Materialize Windows capability adapter

- [x] Extract exact current `DirectoryCapability.cs:421-445` (`WindowsDirectoryCapabilityFileSystem`) to `tools/AIBar.Packaging.Supervisor/Core/WindowsDirectoryCapabilityFileSystem.cs`. **Dependency/base:** B1c branch. **Forecast:** raw 50–100; authored 50–100, no relocation exclusion; stops 900 raw/400 authored. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`; **runtime:** bounded read-only Unicode/space observation adapter harness; **rollback:** restore this exact cluster after `NativeDirectory`. <!-- sdd-owner: implementation -->

###### Unit B1e — Materialize read-only retained-tree native cluster

- [x] Materialize `RetainedTreeMechanismStatus` at current line 188, `DirectoryHandleObservation` at 324, and only `NativeDirectory` query/parser/reopen/observe mechanics at 326-395, 403-414, and 416-419 in `tools/AIBar.Packaging.Supervisor/Core/RetainedTreeReadOnly.cs`; edit only session call sites at 276, 296, 299, and 303 to use the read-only type. Leave `RetainedTreeLease` 190-204, authorization 206-214, sandbox 216-229, issuer 231-243, session 245-322, `TryDelete` 396-402, `FileDispositionInformation` at 328, delete compatibility/export at 336-337, and `NtSetInformationFile` at 415 in Supervisor. **Dependency/base:** B1d branch. **Forecast:** raw 220–360; authored 220–360, no relocation exclusion; stops 900 raw/400 authored. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo`; **runtime:** bounded read-only Unicode/space retained-handle enumeration/reopen harness; **rollback:** restore declarations/mechanics and four call sites only. <!-- sdd-owner: implementation -->

###### Unit S — Signing, CI, and developer-key policy gate

- [x] As an independent chain unit after B1e and before B2, establish and validate strong-name key location and availability, checked public keys, CI secret/access mechanism, local developer signing workflow, rotation/revocation, secret handling, and package/publish metadata policy. Record a focused key-availability/signing contract that fails closed when CI or developer keys are absent or mismatched; do not create Core, migrate authority source/API, or activate friendship in this unit. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisorAuthority&FullyQualifiedName~Signing" -m:1 --nologo`; **runtime:** CI and local developer signing dry run with no secret output; **stop:** any unavailable key path, unchecked public key, secret disclosure, or policy ambiguity; **rollback:** remove only S policy/signing configuration and validation evidence; **base:** B1e branch. <!-- sdd-owner: implementation -->

###### Unit B2 — Physical Core project extraction

**B2 Completion Accounting:** authoritative native count `1,585` = `1,380` byte-identical relocation lines + `173` implementation/configuration/test lines + `10` task-checkbox lines + `22` progress-evidence lines. The maintainer expressly authorized B2's exceptional `1,800` native ceiling; the `173` authored implementation/configuration/test lines preserve the `400` authored-line limit. Validator finding: the completed candidate otherwise passed, and its only blocker was stale B2 evidence falsely applying the historical `900`/`1,000` native ceilings. Focused command is the B2 authority filter below; runtime is the bounded Core retained-handle harness; **Decision needed before apply: No** (`auto-chain`, `feature-branch-chain`).

- [x] **RED:** Add `tests/AIBar.Domain.Tests/PackagingSupervisorAuthorityTests.cs` contract tests that fail until Core, Supervisor, and Domain.Tests are signed from Unit S's checked public identity; require exactly two fully public-key-qualified Core friends (`AIBar.Packaging.Supervisor` and `AIBar.Domain.Tests`), and preserve the existing C1c/C2a internal-seam tests without public API widening. <!-- sdd-owner: implementation -->
- [x] **RED negative cases:** Add fixtures for unsigned, wrong-public-key/token, unqualified, duplicate, and additional Core friends; assert fail-closed rejection before candidate acceptance, with no public test hook, facade, constructor, factory, or substitute production API. <!-- sdd-owner: implementation -->
- [x] **GREEN:** After Unit S passes, create signed `tools/AIBar.Packaging.Supervisor.Core/AIBar.Packaging.Supervisor.Core.csproj` using its checked signing policy for public-sign developer/test builds; move the five materialized files byte-identically, update `AIBar.sln`, Supervisor, and Domain.Tests references, and activate exactly the two checked-identity Core friends. Limit the non-production Domain.Tests friend to existing C1c/C2a seams; keep Core free of C2Authority, issuer/token/session/lease/sandbox, `FileDispositionInformation`, delete APIs/imports, and authority construction; leave official private-key custody with Unit S. <!-- sdd-owner: implementation -->
- [x] **GREEN verification:** Mark Domain.Tests non-packable/non-publishable and make explicit pack/publish attempts refuse; inspect production reference closure, publish output, `.deps.json`, runtime assets, package components, and SBOM inputs to prove Domain.Tests and every test-only dependency are absent. <!-- sdd-owner: implementation -->
- [x] **TRIANGULATE/GATE:** Run `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisorAuthority|FullyQualifiedName~PackagingSupervisor|FullyQualifiedName~C2a" -m:1 --nologo`; prove preserved C1c/C2a seams, unchanged Core public API, exact friend metadata, and bounded Unicode/space read-only behavior. For EACH file require raw whole-file equality (encoding/BOM/line endings), pre/post SHA-256 or Git blobs, `R100`/100% similarity, `numstat`, and an authored-line list; stop at the maintainer-authorized B2 exception of 1,800 native lines or 400 authored lines. **Dependency/base:** Unit S branch. **Runtime:** bounded Core retained-handle harness. **Rollback:** remove both friends, signing/non-publish changes, project/references/tests, and restore all five files byte-identically; preserve B1a-e and S, with no delete movement. <!-- sdd-owner: implementation -->

###### Unit C — C2Authority, Authority.Testing, and atomic final friendship transition (300–390 authored lines; 1,000 native ceiling)

- [x] **RED:** Add failing contracts in `tests/AIBar.Domain.Tests/PackagingSupervisorAuthorityTests.cs` for the final graph and APIs: Core friends exactly signed `AIBar.Packaging.Supervisor.C2Authority` plus signed `AIBar.Packaging.Supervisor.Authority.Testing`; C2Authority's sole friend exactly signed Authority.Testing; Supervisor/Domain.Tests absent from Core; Domain.Tests has no direct Core-internal compilation. <!-- sdd-owner: implementation -->
- [x] **RED — negative identity:** Add unsigned, token-only, unqualified, renamed, wrong-key, duplicate, stale-B2, and extra-friend fixtures; require fail-closed rejection before acceptance with no fallback friend, public hook, authority export, or production-to-Testing reference. <!-- sdd-owner: implementation -->
- [x] **GREEN — ownership/graph:** Add signed `tools/AIBar.Packaging.Supervisor.C2Authority/AIBar.Packaging.Supervisor.C2Authority.csproj` and non-production `tests/AIBar.Packaging.Supervisor.Authority.Testing/AIBar.Packaging.Supervisor.Authority.Testing.csproj` to `AIBar.sln`; move session/lease/verification, exactly-once issuer, and durable-owner internals from Supervisor into C2Authority, retaining no native delete and no public issuer/token/session/sandbox constructor, factory, or export. <!-- sdd-owner: implementation -->
- [x] **GREEN — bounded seam route:** Make Authority.Testing own the fresh bounded sandbox and bridge the existing C1c/C2a internal seams into bounded outcomes; migrate/bridge those tests through it, rewrite `tests/AIBar.Domain.Tests` to reference only Authority.Testing, and keep Core/production public APIs unchanged. <!-- sdd-owner: implementation -->
- [x] **GREEN — atomic transition:** In one accepted transition, verify the complete signed route and final friend sets, then replace both temporary B2 Core friends; no candidate may expose an intermediate graph, retain Supervisor/Domain.Tests Core friendship, add a Core-to-Authority project edge, or add testing assets to production. <!-- sdd-owner: implementation -->
- [x] **RED/GREEN — production exclusion:** Extend `PackagingSupervisorAuthorityTests.cs` to refuse pack/publish and prove Authority.Testing, Domain.Tests, and test-only dependencies are absent from production references, `.deps.json`, runtime assets, package/SBOM inputs, and distribution metadata; prove no native delete import/call and no authority-object export. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisorAuthority|FullyQualifiedName~AuthorityTesting|FullyQualifiedName~C1c|FullyQualifiedName~C2a" -m:1 --nologo`. <!-- sdd-owner: implementation -->
- [x] **GATE:** Repeat the focused command plus the bounded Authority.Testing C1c/C2a bridge scenario; inspect signed identities, exact friend/reference sets, compile closure, public API/native-import diff, and production artifact exclusion. **Runtime:** bounded fresh test-owned sandbox only; **stop:** 390 authored lines or 1,000 native lines, any intermediate graph, identity mismatch, public widening, authority export, native delete, production testing edge/asset, or D/E/C2b/C3 scope. <!-- sdd-owner: implementation -->
- [x] **ROLLBACK:** Disable C2Authority/Authority.Testing consumers, restore B2's exact two signed Core friends as one set, restore Domain.Tests' historical seam route, then remove Unit C's final friends, bridge, projects, references, and moved ownership in reverse order; never leave a half-transition or consumer without its predecessor. <!-- sdd-owner: implementation -->

###### Unit D — Private token-gated native delete route (160–300 authored lines)

- [x] Move raw `NtSetInformationFile(FileDispositionInformation)` from Core/current Supervisor into likely `tools/AIBar.Packaging.Supervisor.C2Authority/NativeDelete.cs`; make it private behind matching session/lease/one-use-token verification and immediate observation. Authority.Testing owns the only concrete bounded sandbox and returns outcomes without issuer/token/session/raw handle export. <!-- sdd-owner: implementation -->
- [x] Extend `PackagingSupervisorAuthorityTests.cs` for arbitrary-handle rejection, wrong session/lease, reuse/invalidation, private API/import containment, and production publish/`.deps.json`/runtime-assets/package/SBOM exclusion of Authority.Testing. **Command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisorAuthority&FullyQualifiedName~NativeDelete" --no-restore -m:1 --nologo`; **runtime:** bounded Windows x64 Authority.Testing sandbox with zero root/handle residue; **stop:** 300 lines, any public/raw delete route, or any testing asset in production; **rollback:** disable private route, restore no delete to Core, then remove D tests; **base:** Unit C branch. <!-- sdd-owner: implementation -->

###### Unit E — Future C2b guarded integration (300–390 authored lines; direct-child portion only)

- [x] **Historical/partial direct-child implementation:** In `tools/AIBar.Packaging.Supervisor.C2Authority/CleanupPolicy.cs` and its existing metadata/session seams, implement the exact-bijection and DPAPI gates, durable-owner invocation, bounded direct-child deletion, zeroization, and truthful `CLEANUP_PARTIAL`; Supervisor receives only `GuardedCleanupFacade`, never issuer/token/session/sandbox/raw delete. This checked work does not establish descendant post-order closure. <!-- sdd-owner: implementation -->
- [x] **Historical/partial direct-child proof:** Extend `tests/AIBar.Packaging.Supervisor.Authority.Testing/AuthorityTestingBridge.cs` and `tests/AIBar.Domain.Tests/PackagingSupervisorAuthorityTests.cs` for guarded-facade-only host access, no authority export, pre-first-delete zero mutation, completed-first-delete partial retention, production exclusion, and graph/cycle proof. **Recorded command:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisorAuthority|FullyQualifiedName~Cleanup" --no-restore -m:1 --nologo`; direct-child evidence remains partial. <!-- sdd-owner: implementation -->

###### Unit E-C1 — Bounded descendant post-order correction (280–360 authored lines; applied)

**Dependency:** checked Unit E direct-child evidence 621–622. **Allowed paths only:** `tools/AIBar.Packaging.Supervisor.C2Authority/CleanupPolicy.cs`, `tools/AIBar.Packaging.Supervisor.C2Authority/RetainedTreeAuthority.cs`, `tests/AIBar.Packaging.Supervisor.Authority.Testing/AuthorityTestingBridge.cs`, `tests/AIBar.Domain.Tests/PackagingSupervisorAuthorityTests.cs`, and OpenSpec evidence/task updates in `openspec/changes/aibar-foundation/{apply-progress.md,tasks.md}`. No other source, project, package, host, Core, D, C3, or discovery path.

**Latest gate finding:** direct-child exact-once and partial-failure evidence passed, but closure failed because the implementation enumerates/deletes only direct children; the normative requirement still demands bounded descendant re-observation and child-before-parent post-order cleanup.

- [x] **RED:** Add failing nested-tree tests for bounded descendant enumeration, root-bound lease identity/kind/volume/non-reparse capture, immediate re-observation of every direct child and descendant before each mutation, child-before-parent order, limit overflow/cancellation/unknown faults, nested success, and a fault after the first completed delete retaining the remainder as `CLEANUP_PARTIAL`. <!-- sdd-owner: implementation -->
- [x] **GREEN:** Implement only handle-anchored bounded descendant enumeration and explicit post-order deletion in `CleanupPolicy.cs`/`RetainedTreeAuthority.cs`; preserve the existing at-most-16 direct-child evidence cap, enforce finite descendant entry/depth/name-byte/deadline limits, lease-generation identity checks, fail-closed reparse/reopen/identity/deletion behavior, zeroization/disposal, and no path, arbitrary-root, recursive-unbounded, or C3 discovery. <!-- sdd-owner: implementation -->
- [x] **GATE:** Run `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisorAuthority|FullyQualifiedName~Cleanup" --no-restore -m:1 --nologo`; prove fresh nested-tree success deletes every leaf before each parent, after-first-delete fault proves exactly one completed deletion plus retained descendants/ancestors and `CLEANUP_PARTIAL`, and residue is zero helpers/processes, zero leaked handles/buffers, zero temporary roots, and zero files outside the allowed paths. Also run `git diff --check`; stop/replan above 400 authored or 1,000 native lines. <!-- sdd-owner: implementation -->
- [x] **ROLLBACK:** Revert only this correction's C2Authority descendant changes, bridge/tests, and its `apply-progress.md`/task evidence; preserve checked direct-child evidence as historical/partial and preserve D, C, B2, B1, all predecessor history, and unrelated tasks. No C3, arbitrary-root selection, scavenger scheduling, PowerShell, or discovery expansion. <!-- sdd-owner: implementation -->

**Preserved broad gates:** C2a remains unchecked until Units B1a-e, B2, C, and D and all native/ownership/runtime contracts pass; Unit E-C1 completes only the bounded descendant correction while broad C2b and C3 gates remain unchecked and C3 remains excluded. Attempt 84 remains failed/insufficient and blocked Attempt 85 remains non-completion evidence; neither is rewritten.

##### b2c-C3 — Explicitly excluded from this amendment

C3 is not authorized by the amended C1b→C2 contract. Do not implement or plan checkboxes for scavenger discovery, age/owner/ACL admission, canonical temporary-base selection, retry scheduling, arbitrary-root selection, metadata-based root discovery, PowerShell integration, or any other C3 behavior. A future C3 proposal/spec/design/tasks sequence must define a separate capability contract and approval boundary. No C3 path may be touched by the producer or C2 units.

#### C1b→C2 Review Workload and Delivery Forecast

| Unit | Estimated changed lines | Review risk | Hard cap | Feature-branch-chain boundary |
| --- | ---: | --- | ---: | --- |
| A artifact amendment | 260–360; measured `+135/-185 = 320` before corrective rerun | Medium | 400; hard stop/re-slice | Base = feature/tracker branch |
| B1a evidence/facets whole-file move | raw 752–820 / authored 0–40 after proof | High | 400 authored / 900 raw | Base = A branch |
| B1b directory capability extraction | raw 240–320 / authored 240–320 | High | 400 authored / 900 raw | Base = B1a branch |
| B1c native rename extraction | raw 110–170 / authored 110–170 | High | 400 authored / 900 raw | Base = B1b branch |
| B1d Windows adapter extraction | raw 50–100 / authored 50–100 | High | 400 authored / 900 raw | Base = B1c branch |
| B1e retained-tree read-only extraction | raw 220–360 / authored 220–360 | High | 400 authored / 900 raw | Base = B1d branch |
| S signing/key policy gate | Policy/config/evidence only; measure before apply | High | 400 | Base = B1e branch |
| B2 physical Core extraction and two signed friends | completed candidate: native 1,585 / authored implementation/configuration/tests 173 | High | 400 authored / 1,800 native (maintainer-authorized exception) | Base = S branch |
| C Authority + Testing and atomic friend transition | 300–390 | High | 400 authored / 1,000 native | Base = B2 branch |
| D private native delete | 160–300 | High | 400 | Base = C branch |
| E direct-child evidence / descendant correction | 336 historical partial / 280–360 correction | High / Medium | 400 authored / 1,000 native | Base = D branch; correction follows checked partial evidence |

Decision needed before authority-chain apply: No — `auto-chain`; B1a-e may precede signing, but Unit S signing/key policy and availability validation blocks B2 and C
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High; stop/re-slice any authority-chain child above 400 authored lines; every B1 child separately stops at 900 raw native lines; completed B2 is excepted at an authoritative 1,585 native lines under the maintainer-approved 1,800 ceiling, with 173 authored implementation/configuration/test lines under 400
Delivery strategy: auto-chain
No combined C2 PR; broad C2a/C2b/C3 remain unchecked

#### Remaining Work Forecast for the Parent

1. **Immediate authority chain:** apply A → B1a → B1b → B1c → B1d → B1e → S signing/key gate → B2 exactly two checked-identity signed Core friends (`AIBar.Packaging.Supervisor` and `AIBar.Domain.Tests`) → C authority migration and same-gate B2-friend transition → D → E, only after each immediate predecessor gate. Preserve Attempts 60/61/84 and blocked Attempt 85 as historical evidence and keep broad C2a/C2b/C3 unchecked until their independent proof.
2. **Packaging, distribution, lifecycle, and release:** after the b2c dependency is resolved, the unchecked downstream units are 8C2B1a2 live-policy/external acquisition, 8C2B1b legal/provenance/promotion, 8C2B2 MakeAppx/SignTool truthfulness, 8D install/upgrade/startup/uninstall smoke, 8E Windows/release gates, and the cross-slice completion gates. Each remains a separate review unit; the current plan supplies ranges of roughly 260–398 authored lines per unit, not calendar dates.
3. **What “functioning” can mean:** today it is truthful to claim a checked developer foundation and local tray/analytics behavior, with private integration disabled by default and no release/package/lifecycle approval. An MVP claim requires the remaining product and distribution dependencies to pass their own acceptance gates; a release-ready claim additionally requires 8D/8E, policy/legal/dependency evidence, supported Windows validation, and the cross-slice gates. The artifacts do not support a calendar date; progress should be reported as completed review units and these rough ranges.

#### Parent-owned lifecycle gates

- [ ] Enforce A → B1a → B1b → B1c → B1d → B1e → S → B2 → C → D → E feature-branch-chain order, immediate-predecessor bases, reverse rollback, the 400-line authored stop/re-slice boundary, and the separate 900-raw-native-line stop for each B1 child; retain completed B2's maintainer-approved exception at an authoritative 1,585 native lines under the 1,800 ceiling, with 173 authored implementation/configuration/test lines under 400. <!-- sdd-owner: parent -->
- [ ] Allow read-only/non-authority Units B1a-e before signing; require Unit S to establish, approve, and validate key storage/availability, public keys, CI/dev signing, rotation, and secret policy before B2 activates exactly two checked-identity Core friends (`AIBar.Packaging.Supervisor` and non-production `AIBar.Domain.Tests`); require C to transition both B2 friends in the same gate that moves session/leases/authority, and ensure no broad test friendship or Authority.Testing production artifact. <!-- sdd-owner: parent -->
- [ ] Preserve producer/C1c history, failed Attempts 60/61/84, and blocked Attempt 85; 1,000 is never an allowed threshold, size exception, or completion authority. <!-- sdd-owner: parent -->
- [ ] Keep C3 excluded and route any future scavenger or PowerShell request through a separately approved SDD contract. <!-- sdd-owner: parent -->

#### Slice 8C2B1a2 — Versioned live-policy authority, external acquisition, and two-root proof (~390 authored lines)

**Dependency:** reviewed/receipted/committed 8C1.1b2, after reviewed/receipted/committed 8C1.1b1b, after reviewed/receipted/committed 8C1.1b1a; B1a2 must invoke the reviewed isolated mode and cannot absorb, bypass, or reimplement it. **Start:** reviewed B1a1 plus the complete 8C1.1 receipt chain. **End:** real acquisition feeds B1a1 only through maintainer-reviewed policy. **Allowed paths:** `scripts/Publish-Deterministic.ps1`; `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`; authorized checked-in `packaging/policies/{win-x64.publish-policy.v1.json,live-publish-policy-1.schema.json,review/win-x64.publish-policy-candidate.v1.json}` only. Runtime/candidate/acquisition leaves are external caller-owned temporary roots, never `artifacts/` or any repository path; 8C1 defaults remain unchanged. No legal/provenance/promotion, MakeAppx, or signing paths.

**Review Workload Forecast:** 390–398 authored additions/deletions (hard maximum 400); generated policy/output is excluded only from authored counting, never from snapshot/hash/receipt binding. Risk: High. Chained PRs: Yes; decision needed: No (force-chained `feature-branch-chain`, no exception or split). If the estimate exceeds 400, stop and replan rather than compress or waive checks.

- [ ] **RED — policy authority:** test closed schema/additional-property/cardinality, missing/extra/duplicate/ambiguous/NFC-invalid mappings, ordinal exact-key/ref sorting, Windows `OrdinalIgnoreCase` path collisions, drifted four fingerprints/policy hash, unresolved-null candidate → `B1_POLICY_REVIEW_REQUIRED` before B1a1, and rejection of fixture/inferred ID/name/root authority. <!-- sdd-owner: implementation -->
- [ ] **RED — external-root admission:** for each restore/publish/candidate root test pre-existing file, empty/non-empty directory, marker/sentinel, repository/source overlap, absolute/rooted or missing parent, inaccessible/unclassifiable parent, reparse leaf and ancestor, non-unique leaf, and stale staging/publish/journal; assert fail-closed rejection, no child launch, and unchanged markers/bytes. <!-- sdd-owner: implementation -->
- [ ] **RED — joins/process/cleanup:** mutate restore/deps/publish/recovery inputs for missing, extra, unreachable, test-only, duplicate, unowned, hash/length/source-byte mismatch; test timeout/cancel, child+grandchild termination/wait, saturated stdout/stderr drain, startup/nonzero/partial failures, exact safe codes, and cleanup failure. Child exit must precede any root deletion; cleanup failure is recorded as evidence and never acceptance. <!-- sdd-owner: implementation -->
- [ ] **GREEN:** add the checked-in reviewed schema/policy/candidate-review contract; acquire into unique nonexistent leaves under external existing caller roots, invoke fixed `dotnet`/reviewed 8C1 entry points via `ArgumentList`, bind policy/version and four input fingerprints, and pass only exact policy-resolved inputs to B1a1. Never overwrite policy, infer authority, reuse roots, or leave runtime files in-repo. <!-- sdd-owner: implementation -->
- [ ] **TRIANGULATE/GATE:** run two fresh external GUID roots with spaces/non-ASCII names and reordered enumeration; prove byte-identical policy/fingerprints/B1a1 output, repeat-run hashes, marker containment, complete child cleanup, and documented cleanup-failure evidence. Focused: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution&FullyQualifiedName~RestorePublishRunner"`. Runtime: two external caller-owned temp roots; remove only after all descendants exit. Rollback: revert runner/tests/policy/schema/candidate-review files; delete only external roots, preserving B1a1 and 8C1. <!-- sdd-owner: implementation -->

#### Slice 8C2B1b — Legal/provenance evidence and durable promotion/recovery (~360–390 lines)

**Start/base:** reviewed B1a2 over B1a1; PR #8 targets B1a2. **End:** reviewed legal/provenance/compliance promotion and receipt for B2. No MakeAppx/signing.

**Allowed paths:** `scripts/Publish-Deterministic.ps1`; `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`; `packaging/{provenance.json,license-evidence.json,compliance-manifest.json,THIRD-PARTY-NOTICES.md,LICENSES/**,receipts/8c2b1.json}`; ignored caller roots under `artifacts/8c2b1/`.

- [ ] **RED:** reject incomplete sourced licenses/notices; closed-schema/ref violations; mutable origins; secrets/paths; noncanonical bytes; self-inclusion; test-only fail-open; and missing/duplicate/wrong 8C1 recovery-inventory hash. <!-- sdd-owner: implementation -->
- [ ] **GREEN:** emit sourced legal evidence and closed provenance/compliance/receipt schemas whose compliance inputs contain exactly one 8C1 recovery-inventory hash; add fail-closed logic, canonical evidence, rollback journal, allowlisted atomic promotion, manifest-last acceptance, and durable receipt. <!-- sdd-owner: implementation -->
- [ ] **TRIANGULATE:** inject collision/failure/termination at every allowlisted step; prove journal recovery, `B1_ROLLBACK_INCOMPLETE` blocking, prior-present/prior-absent bidirectional restore, publish-omission cleanup, self-exclusion, unchanged bytes after failure, and byte-identical separate-root evidence. <!-- sdd-owner: implementation -->
- [ ] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`; runtime uses reviewed B1a2 outputs in two fresh caller roots, no MakeAppx/SignTool; rollback removes only B1b evidence/promotion/tests and ignored outputs, preserving B1a1/B1a2. <!-- sdd-owner: implementation -->

#### Slice 8C2B2 — Truthful MakeAppx/SignTool runtime gate (~260–340 lines)

**Base:** reviewed B1b; PR #9 targets B1b. **End:** truthful packaging capability evidence for 8D. **Paths:** script, manifest, `PackagingDistributionTests.cs`, ignored `artifacts/8c2b/` only.

- [ ] **RED:** reject unresolved tools, invalid manifest/full-trust/publisher, mismatch, stale `.msix`, unsafe cleanup, secret logs, and signed/installable overclaims.
- [ ] **GREEN:** verify tools/manifest; run real MakeAppx pack/inspect and optional credentialed SignTool verify; otherwise emit unavailable/not-run/unsigned without overclaim.
- [ ] **TRIANGULATE:** inject missing tools/credentials, tool failure, publisher mismatch, stale output, output-root variation, and signing-request failure; prove cleanup is non-destructive and stale outputs cannot survive as evidence.
- [ ] **GATE:** focused `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"`; runtime harness: controlled Windows SDK pack/inspect and optional credentialed sign/verify, otherwise recorded not-run state; rollback only 8C2B2 runtime gate/tests and ignored outputs, preserving reviewed 8C2B1.

### Slice 8D — Lifecycle smoke (~340 lines)

**Base:** reviewed B2; PR #10 targets B2. **Scope:** install, upgrade, startup, single-instance, uninstall, retention/removal smoke; preserve export absence. No manual DPI/legal gates.

- [ ] **RED:** test install, upgrade, `--startup`, single instance, uninstall, retention/removal, and Codex hashes.
- [ ] **GREEN:** wire the install lifecycle harness and migration-safe upgrade/uninstall behavior.
- [ ] **TRIANGULATE:** vary existing DB, locks, failed migration, disabled integration, and removal selection.
- [ ] **REFACTOR:** make setup/cleanup idempotent and path-free. Focused: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSmoke|FullyQualifiedName~Lifecycle"`. Runtime: Windows sandbox install → launch → upgrade → uninstall. Rollback only 8D harness/lifecycle.

### Slice 8E — Windows release gates (~280 lines)

**Base:** reviewed 8D; PR #11 targets 8D. **Scope:** W10/W11 VM matrix, compatibility, policy/legal, kill-switch, export-absence, and release evidence. No new behavior.

- [ ] **RED:** record failing tray, DPI, placement, sleep/resume, focus, unsupported, policy/legal, license, provenance, compatibility, and kill-switch gates.
- [ ] **GREEN:** execute and document the matrix and sign-off gates; make unsupported behavior visible and distribution disabled until all pass.
- [ ] **TRIANGULATE:** repeat on W10/W11 with integration disabled; prove local analytics work and no remote sink exists.
- [ ] **REFACTOR:** consolidate release checklist and rollback procedure. Focused: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Windows|FullyQualifiedName~ReleaseGate"`. Runtime: W10/W11 VM smoke matrix. Rollback: remove gate evidence/configuration and ship disabled; preserve 8D lifecycle.

**Active chain:** `8C2A → B1a1 (#6) → 8C1.1a (#7a) → 8C1.1b1a → 8C1.1b1b → 8C1.1b2 → B1a2 (#8) → B1b (#9) → B2 (#10) → 8D (#11) → 8E (#12)`; each PR targets its reviewed predecessor. 8B.2 is retired; failed B1a evidence is non-authoritative; no exception. 8C1.1b1b depends on reviewed/receipted/committed 8C1.1b1a; 8C1.1b2 depends on reviewed/receipted/committed 8C1.1b1b; B1a2 depends on reviewed/receipted/committed 8C1.1b2.

## Cross-Slice Completion Gates

- [ ] Keep authored changes at or below 400 lines except for the explicitly recorded, maintainer-approved Slice 8A-only `size:exception` and the separately approved native changed-line ceilings of 1000 for independent C1c verification and future C2a/C2b. C1c implementation remains historical at 347/400; C2a/C2b retain their 300–390 forecasts and High risk. Crossing 400 remains mandatory workload visibility/review-splitting pressure; each 1000 ceiling is evidence/correction headroom only, not authority to expand behavior, allowed paths, dependencies, absorb another unit, merge C1c/C2a/C2b, authorize C3, or authorize stage/commit/push/PR/review. No future slice inherits or extends these approvals.
- [ ] Run the full test suite, static analysis, dependency/license checks, and `dotnet publish` for the supported Windows target.
- [ ] Confirm all MVP non-goals remain unsupported and no code path reads multiple roots/accounts, WSL, browser cookies, passwords, prompt/response bodies, or authoritative billing data.
- [ ] Confirm generated fixtures and screenshots are synthetic/approved and remain bound to the reviewed behavior.
- [ ] Confirm each implementation slice remains independently revertible before opening or advancing its chained PR, and enforce the line-budget rule above with only the recorded Slice 8A exception.
- [ ] Preserve no credentials or user content in test evidence, logs, artifacts, screenshots, or release bundles.
