# AIBar Foundation Implementation Tasks

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 9 reviewable slices; Slice 2A ~350 and Slice 2B ~390 authored lines, all slices below 400; generated fixtures/goldens remain in snapshot identity but excluded from authored estimates |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2A → PR 2B → PR 3 → PR 4 → PR 5 → PR 6 → PR 7 → PR 8 |
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

### Slice 3 — Refresh coordination and quota cache

- [ ] Implement `QuotaRefreshCoordinator` with cache-first publication, one in-flight refresh task, trigger coalescing, manual-refresh freshness bypass, conservative polling, cancellation, sleep/resume and clock-change re-evaluation.
- [ ] Implement SQLite schema/migration and `IQuotaSnapshotStore` for normalized quota fields and retrieval metadata only; atomically commit successful primary snapshots and never store credentials or raw responses.
- [ ] Preserve the prior snapshot and original timestamp on failure, overlay the classified error, and transition to explicit stale/unavailable states after the documented freshness threshold; optional-detail failures must not erase primary data.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add coordinator/store tests for startup cache rendering, concurrent triggers, manual bypass, stale-on-failure, atomic save/load, cancellation races, clear-data publication, and optional-detail degradation.
- [ ] Verify migration, foreign keys, WAL configuration, and crash-before-commit behavior using persistence integration tests.

**Acceptance evidence:** coordinator and persistence test report plus database inspection proving only normalized cache data exists. Rollback is a schema-compatible removal of the cache feature; no destructive downgrade or Codex-file mutation.

### Slice 4 — Native tray, popover, and quota presentation

- [ ] Implement the single-process WPF host, named-mutex single-instance activation, tray recreation handling, taskbar/DPI-aware borderless popover positioning, deactivation behavior, and explicit Exit command that cancels work and closes persistence.
- [ ] Implement immutable view models/state rendering for current, stale, loading, unavailable, authentication, permission, malformed, network, and service states; never show a fabricated percentage or stale data as current.
- [ ] Build the custom semantic WPF design tokens and compact CodexBar-inspired quota cards using Windows-native typography, spacing, contrast, keyboard navigation, visible focus, accessible names, reduced-motion behavior, high-contrast support, and DPI scaling.
- [ ] Show independent 5-hour/weekly percentages, service reset countdowns, source/timestamp/freshness disclosure, manual refresh, local-analytics separation, estimated-cost separation, and private-endpoint disclosure.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test view-model state mapping and coordinator/UI command behavior; add focused Windows smoke checks for tray toggling, popover focus, single-instance activation, taskbar recreation, and representative DPI.
- [ ] Capture representative Windows 10/11 visual evidence; treat accessibility and lifecycle checks as acceptance evidence, not screenshot similarity alone.

**Acceptance evidence:** view-model tests, Windows smoke results, accessibility checklist, and W10/W11 screenshots at representative scale factors. Rollback is removal of presentation while preserving application services.

### Slice 5 — Incremental scanner and checkpointing

- [ ] Implement supported initial Codex `sessions` and `archived_sessions` discovery only, with bounded background batches and cancellation.
- [ ] Implement streaming JSONL parsing from validated checkpoints, file identity/size/mtime comparison, append handling, incomplete-tail deferral, parser-semantics invalidation, replacement/rebuild, and transactional checkpoint updates.
- [ ] Parse only timestamps, trustworthy model evidence, and token counters; skip/defer malformed or changing records with scan coverage warnings and never retain prompt/response bodies.
- [ ] Implement `scan_run` provenance/status including discovered/read/skipped/deferred counts, warnings, cancellation, and partial coverage.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic golden/property tests for unchanged rescans, appended events, malformed/truncated files, replaced/shrunk files, cumulative resets, cancellation, parser-version invalidation, idempotence, and non-negative deltas.
- [ ] Verify fixture data contains no real credentials, paths, prompt text, or response text, and that cancellation before commit leaves aggregates/checkpoints unchanged.

**Acceptance evidence:** scanner golden/property and persistence test reports with coverage warnings. Rollback is disabling scanning; existing committed aggregates remain readable. Requires focused `review-reliability`.

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
