# Apply Progress — AIBar Foundation

## Status

**Slice 1 complete. Slice 2 blocked before implementation by its enforced 400-line budget gate.** The prior SDK blocker is resolved: .NET SDK `8.0.408` is installed alongside `6.0.424`. Only the approved Solution Skeleton and Pure Contracts work unit was implemented. Slice 2 was assessed on `feature/aibar-foundation-slice-2`; no Slice 2+ production code, fixtures, tests, credentials, HTTP, SQLite, scanner, tray behavior, polished UI, commit, push, branch, or PR was created.

## Preserved blocker history

The previous apply attempt was blocked before implementation because only .NET SDK `6.0.424` was installed and `net8.0` templates were unavailable. Its temporary empty solution was removed, no checkbox was changed, and no application source was retained. That blocker is resolved by the verified SDK `8.0.408`; it is retained here for audit continuity.

## Structured status consumed

- Change: `aibar-foundation`
- Native status: authoritative OpenSpec status, `applyState: ready`, `nextRecommended: apply`, no blocked reasons
- Action context: `repo-local`; workspace root and allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`
- Delivery: `auto-chain` / `feature-branch-chain`
- Prior PR boundary: Slice 1 only; current attempted boundary: Slice 2 only (blocked at preflight; Slice 3+ remain out of scope)
- Strict TDD configuration: false; Slice 1's explicit test-first requirement was followed. Slice 2 explicitly requires RED → GREEN → TRIANGULATE → REFACTOR, but no cycle began because the budget gate stopped work before code/test edits.

## Slice 2 budget preflight (2026-07-12)

Structured status was supplied by the parent and consumed before editing: authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, `5/52` complete, repo-local action context rooted at `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`, with no blockers. The approved delivery path is `auto-chain` / `feature-branch-chain`, and the assigned boundary is Slice 2 only.

Slice 2 cannot be implemented as its six required checkboxes under the `<=400` authored-line limit. The smallest credible forecast is **~620 authored lines**, excluding generated build output:

| Concern | Forecast |
|---|---:|
| Root resolver, minimum-field JSON reader, request-scoped credential object, feature policy, safe error/redaction boundary | 160 |
| Allowlisted `HttpClient` adapter, timeout/cancellation/redirect/retry policy, response DTO mapping | 210 |
| Synthetic HTTP/auth fixtures and contract tests (including RED/TRIANGULATE cases) | 230 |
| Project/test wiring and unsupported-integration disclosure | 20 |
| **Total** | **620** |

No partial Slice 2 work was started, no test was run, and no Slice 2 checkbox was changed. This preserves the reviewed Slice 1 baseline and prevents an unreviewable security-sensitive diff. Actual Slice 2 implementation authored lines: **0**; this progress-only record changed 29 lines (26 additions, 3 deletions).

**Concrete split proposal:**

1. **Slice 2A — credential boundary and disablement** (~350 authored lines): root resolver; minimum-field read-only credential reader; request-scoped credential lifetime; centralized safe error/redaction; feature-default-disabled policy and unsupported-endpoint disclosure; synthetic auth/redaction tests.
2. **Slice 2B — private HTTP quota adapter** (~390 authored lines): HTTPS allowlist, redirects disabled, timeout/cancellation/retry classification; version-neutral `/wham/usage` and optional-detail mapping; synthetic status/redirect/schema/retry contract tests.

Each proposed unit remains within the budget and retains synthetic-only fixtures. Slice 2B depends on 2A; neither introduces real credentials or live endpoints.

## Completed tasks and checkbox evidence

All five persisted Slice 1 checkboxes are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md` after successful build/test and dependency-inspection evidence.

- Created `AIBar.sln`, `AIBar.Domain`, `AIBar.Application`, `AIBar.Desktop` (WPF `net8.0-windows`, x64), and `AIBar.Domain.Tests`.
- Added pure domain records, ports, error/coverage/pricing taxonomies, freshness policy, component-wise token delta, fixed clock, and explicit timezone policy.
- Kept Domain/Application package-free and free of WPF, HTTP, filesystem, and SQLite references. The WPF dependency is restricted to `AIBar.Desktop`.

## TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| Runner establishment | `dotnet --version`; `dotnet --list-sdks`; `dotnet new list` | .NET `8.0.408` and WPF/xUnit templates available. |
| RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore` after writing contract tests | Failed as expected: `QuotaSnapshot` and other domain types did not exist (`CS0246`). |
| GREEN | Added only the minimal pure `Foundation.cs` contracts/policies, then reran the focused test command | Passed: 4/4 tests. |
| TRIANGULATE | Added deterministic clock/time-policy test first; reran focused test command | Failed as expected: `FixedClock` and `TimeZoneLocalTimePolicy` absent (`CS0246`); after minimal implementation, passed: 5/5 tests. |
| REFACTOR | Restricted WPF target to x64, added build-output ignores, and reran complete solution build/tests | Passed with 0 warnings and 0 errors; no infrastructure coupling added. |

## Verification evidence

```text
dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 5, Failed: 0, Skipped: 0, Total: 5.

dotnet list src/AIBar.Domain/AIBar.Domain.csproj package
No packages found.

dotnet list src/AIBar.Application/AIBar.Application.csproj package
No packages found.

Source-only dependency inspection (excluding bin/obj)
No System.Net.Http, Microsoft.Data.Sqlite, or System.IO references in Domain/Application source.
```

The runner used only synthetic pure tests; no real Codex files, credentials, network calls, SQLite, scanner, tray runtime behavior, or private adapter was introduced.

## Files changed

- `.gitignore`
- `AIBar.sln`
- `src/AIBar.Domain/AIBar.Domain.csproj`
- `src/AIBar.Domain/Foundation.cs`
- `src/AIBar.Application/AIBar.Application.csproj`
- `src/AIBar.Desktop/AIBar.Desktop.csproj`
- `src/AIBar.Desktop/App.xaml`
- `src/AIBar.Desktop/App.xaml.cs`
- `src/AIBar.Desktop/AssemblyInfo.cs`
- `src/AIBar.Desktop/MainWindow.xaml`
- `src/AIBar.Desktop/MainWindow.xaml.cs`
- `tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj`
- `tests/AIBar.Domain.Tests/FoundationContractsTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

## Design deviations

None. The WPF host is template-only and x64-targeted; no presentation behavior was implemented. Domain and application layers are framework-independent and contain no infrastructure adapters.

## Workload / PR boundary

Feature-branch-chain work unit: **Slice 1 only**. Total new solution/skeleton source plus solution/ignore metadata is **361 lines** (`304` source/project/XAML + `57` solution/ignore), below the **380-line forecast** and **400-line limit**. The count includes generated skeleton files; no further scope may be added to this work unit. No commit, branch, PR, push, or review transaction was created.

## Remaining tasks

The exact unchecked persisted task lines are:

- [ ] Implement the single supported Codex-root resolver (`%CODEX_HOME%`, otherwise the current-user `.codex` root) and a read-only minimum-field credential reader; never deserialize, persist, or log refresh tokens, passwords, browser cookies, prompt/response content, or unrelated secret fields.
- [ ] Implement the feature-gated `IQuotaProvider` using `HttpClient`, HTTPS origin allowlisting for `https://chatgpt.com/backend-api`, redirects disabled, cancellation/timeouts, bounded retry rules, and request-scoped credential lifetime.
- [ ] Implement version-neutral response DTO mapping for `/wham/usage` and optional `/wham/rate-limit-reset-credits`, validating required percentages/reset times and preserving optional-detail failure independently.
- [ ] Classify 401/403/429/5xx, transport/timeout, malformed/missing-field, and unavailable cases into safe error codes without raw bodies, headers, credentials, or sensitive account identifiers.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add fixture/contract tests for minimum/extra/malformed auth fields, valid and schema-drift responses, status classes, redirect rejection, retry limits, optional-detail failure, and secret-free errors.
- [ ] Add the explicit unsupported/private-endpoint disclosure and a build/application setting that can disable this integration without disabling local analytics.
- [ ] Implement `QuotaRefreshCoordinator` with cache-first publication, one in-flight refresh task, trigger coalescing, manual-refresh freshness bypass, conservative polling, cancellation, sleep/resume and clock-change re-evaluation.
- [ ] Implement SQLite schema/migration and `IQuotaSnapshotStore` for normalized quota fields and retrieval metadata only; atomically commit successful primary snapshots and never store credentials or raw responses.
- [ ] Preserve the prior snapshot and original timestamp on failure, overlay the classified error, and transition to explicit stale/unavailable states after the documented freshness threshold; optional-detail failures must not erase primary data.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add coordinator/store tests for startup cache rendering, concurrent triggers, manual bypass, stale-on-failure, atomic save/load, cancellation races, clear-data publication, and optional-detail degradation.
- [ ] Verify migration, foreign keys, WAL configuration, and crash-before-commit behavior using persistence integration tests.
- [ ] Implement the single-process WPF host, named-mutex single-instance activation, tray recreation handling, taskbar/DPI-aware borderless popover positioning, deactivation behavior, and explicit Exit command that cancels work and closes persistence.
- [ ] Implement immutable view models/state rendering for current, stale, loading, unavailable, authentication, permission, malformed, network, and service states; never show a fabricated percentage or stale data as current.
- [ ] Build the custom semantic WPF design tokens and compact CodexBar-inspired quota cards using Windows-native typography, spacing, contrast, keyboard navigation, visible focus, accessible names, reduced-motion behavior, high-contrast support, and DPI scaling.
- [ ] Show independent 5-hour/weekly percentages, service reset countdowns, source/timestamp/freshness disclosure, manual refresh, local-analytics separation, estimated-cost separation, and private-endpoint disclosure.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test view-model state mapping and coordinator/UI command behavior; add focused Windows smoke checks for tray toggling, popover focus, single-instance activation, taskbar recreation, and representative DPI.
- [ ] Capture representative Windows 10/11 visual evidence; treat accessibility and lifecycle checks as acceptance evidence, not screenshot similarity alone.
- [ ] Implement supported initial Codex `sessions` and `archived_sessions` discovery only, with bounded background batches and cancellation.
- [ ] Implement streaming JSONL parsing from validated checkpoints, file identity/size/mtime comparison, append handling, incomplete-tail deferral, parser-semantics invalidation, replacement/rebuild, and transactional checkpoint updates.
- [ ] Parse only timestamps, trustworthy model evidence, and token counters; skip/defer malformed or changing records with scan coverage warnings and never retain prompt/response bodies.
- [ ] Implement `scan_run` provenance/status including discovered/read/skipped/deferred counts, warnings, cancellation, and partial coverage.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic golden/property tests for unchanged rescans, appended events, malformed/truncated files, replaced/shrunk files, cumulative resets, cancellation, parser-version invalidation, idempotence, and non-negative deltas.
- [ ] Verify fixture data contains no real credentials, paths, prompt text, or response text, and that cancellation before commit leaves aggregates/checkpoints unchanged.
- [ ] Implement transactional `daily_model_usage` aggregation for input, cached-input, and output totals using `max(0, current - previous)` per component and the exact documented ranking formula `total tokens = input + cached input + output`.
- [ ] Implement explicit `Unknown` attribution for missing/untrusted model evidence; never infer a named model or rank by cost.
- [ ] Implement Windows local-timezone/day normalization with timezone ID, observed offset, DST behavior, UTC-derived provenance, and explicit rebuild/version behavior when the policy changes.
- [ ] Add SQLite migrations and transactional tests for checkpoint/aggregate atomicity, crash recovery, repricing without token mutation, and aggregate retention.
- [ ] Implement Clear AIBar Data to cancel work, remove only AIBar-owned cache/database/log/settings data, recreate empty state, and prove Codex-owned source files are byte-for-byte unchanged.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test model ranking, `Unknown`, midnight/DST boundaries, reset/decrease handling, clear-data isolation, and partial scan retention.
- [ ] Add a checked-in, versioned immutable pricing catalog with version/date provenance, component-specific rates, unsupported-model results, and no fallback rate for `Unknown`.
- [ ] Implement estimated-cost calculation and UI labels/warnings for estimate status, unknown models, repricing, discounts, routing, contracts, and non-authoritative billing assumptions; never use billed-cost/invoice/credit language.
- [ ] Implement named daily trend windows (initial 7 complete local days and day-over-day comparison) and simple linear quota exhaustion from observed current-window usage, remaining service quota, reset time, and valid positive rate only.
- [ ] Return insufficient/unavailable for stale quota, invalid reset, zero/negative rate, insufficient observations, or unsupported forecasting; never reconstruct hourly history or emit probabilistic predictions.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test pricing provenance/warnings, model totals, trend windows, time basis, ETA insufficiency, and linear estimate labeling.
- [ ] Verify UI/source labels distinguish service quota, locally derived analytics, and estimated cost in view-model snapshots.
- [ ] Implement per-user startup registration abstraction: packaged startup-task path where available and unpackaged HKCU Run entry with quoted executable and `--startup`; read back effective state after mutation, never elevate or write machine-wide state.
- [ ] Add native commands/settings for startup toggle, Clear AIBar Data, safe diagnostic export, and private-integration disablement; keep telemetry and remote crash reporting off by default.
- [ ] Implement central redaction before every diagnostic sink/export for bearer headers, secret fields, sensitive IDs, URLs/query strings, paths, raw bodies, auth files, JSONL lines, database rows, and exception values; bound local log size/age.
- [ ] Add privacy tests that recursively inspect AIBar-owned persistence, logs, diagnostics, exports, and UI/error snapshots for seeded prompt/response text, bearer values, sensitive IDs, and source paths.
- [ ] Add deterministic x64 Windows 10/11 packaging, SBOM, signed per-user MSIX path when signing is available, unpackaged self-contained development/recovery artifact, and upgrade/migration/uninstall/user-data retention checks.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add packaging smoke tests for clean install, upgrade, startup launch, single instance, uninstall, and data retention/removal; perform W10/W11 manual/VM checks for tray recreation, DPI, taskbar placement, sleep/resume, popover focus, and concrete unsupported behavior.
- [ ] Complete policy/legal review, dependency and source-provenance/MIT-notice review, private-endpoint compatibility validation, and remote-free kill-switch verification before enabling distribution builds.
- [ ] Run the full test suite, static analysis, dependency/license checks, and `dotnet publish` for the supported Windows target.
- [ ] Confirm all MVP non-goals remain unsupported and no code path reads multiple roots/accounts, WSL, browser cookies, passwords, prompt/response bodies, or authoritative billing data.
- [ ] Confirm generated fixtures and screenshots are synthetic/approved and remain bound to the reviewed behavior.
- [ ] Confirm each implementation slice remains independently revertible and its authored change count is at or below 400 lines before opening or advancing its chained PR.
- [ ] Preserve no credentials or user content in test evidence, logs, artifacts, screenshots, or release bundles.

All remaining lines are Slice 2–8 or cross-slice gates and are intentionally not part of this apply batch.

## Slice 2A applied (2026-07-12)

Slice 2A is complete on `feature/aibar-foundation-slice-2`. The preserved 620-line Slice 2 gate and approved 2A/2B split above remain the governing delivery decision. This work unit implements the credential/policy boundary only; it adds no `HttpClient`, endpoints, DTOs, retries, redirects, or live integration.

### Completed tasks and persisted checkbox evidence

All seven Slice 2A task lines are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md` after focused synthetic and full-suite evidence:

- supported `%CODEX_HOME%`/current-user `.codex` root selection;
- read-only minimum-field `access_token` plus optional `account_id` reader using `FileShare.ReadWrite | FileShare.Delete`;
- disposable request credential that clears token/account references on disposal;
- central safe redaction for bearer, token/secret, account ID, and path values;
- disabled-by-default private-integration policy with private/undocumented/unsupported disclosure; and
- synthetic tests for roots, malformed/missing/extra fields, lifetime, defaults, disclosure, redaction, and replacement/share-read behavior.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `CredentialBoundaryTests` and Application reference; ran focused test command before implementation | Failed as expected: `AIBar.Application` and `ICredentialFileReader` did not exist (`CS0234`, `CS0246`). |
| GREEN | Added the minimum credential boundary, reader, policy, disclosure, and redactor; reran focused tests | Initially exposed platform-sensitive root separator expectation; corrected the synthetic assertion to `Path.Combine`; passed 9/9. |
| TRIANGULATE | Focused tests cover missing/malformed/blank fields, extra refresh-token field, replacement/share-read, disabled default, and seeded secret/account/path redaction | Passed 9/9; all inputs are in-memory synthetic strings. |
| REFACTOR | Kept parsing in one `JsonDocument` minimum-field path and redaction in one primitive; fixed the pre-existing WPF `Application` namespace ambiguity caused by the new application namespace | Full build and suite pass with 0 warnings/errors; no HTTP or persistence references added. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~CredentialBoundaryTests --logger "console;verbosity=minimal"
RED: failed as expected (CS0234/CS0246).
GREEN/TRIANGULATE: Passed: 9, Failed: 0, Skipped: 0, Total: 9.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 15, Failed: 0, Skipped: 0, Total: 15.
```

LSP diagnostics were requested but no LSP tool is available in this executor session; the successful compiler build is the available static diagnostic evidence. `git diff --check` passed. Source inspection found no `HttpClient`, `System.Net.Http`, `backend-api`, or `wham` references in Slice 2A paths.

### Files changed in Slice 2A

- `src/AIBar.Application/CredentialBoundary.cs`
- `tests/AIBar.Domain.Tests/CredentialBoundaryTests.cs`
- `tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj`
- `src/AIBar.Desktop/App.xaml.cs` (one-line namespace disambiguation required for the full solution build)
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

### Workload / boundary / deviations

Current feature-branch-chain boundary: **Slice 2A only**; prior dependency: Slice 1; follow-up: Slice 2B. Slice 2A adds **171 source/test lines**, plus two one-line project/host corrections, before SDD metadata; it is below the 400-line authored budget. No commit, push, PR, review transaction, real Codex access, credential, or network call occurred.

Deviation: the reader is an infrastructure-neutral `ICredentialFileReader` port backed by a read-only file implementation; the existing `ICodexCredentialSource` remains unchanged so Slice 2B can compose the request boundary without introducing HTTP now. The required build fix qualifies the WPF base type as `System.Windows.Application` because `AIBar.Application` now exists.

### Remaining tasks

The following exact Slice 2B lines remain unchecked and are outside this work unit:

- [ ] Implement the feature-gated `IQuotaProvider` with `HttpClient`, HTTPS allowlisting for only `https://chatgpt.com/backend-api`, redirects disabled, request-scoped authorization, cancellation, bounded connect/request timeouts, and conservative retry behavior honoring `Retry-After`.
- [ ] Implement version-neutral DTO mapping for `/wham/usage` and optional `/wham/rate-limit-reset-credits`, validating required percentages/reset timestamps and preserving optional-detail failure without invalidating the primary quota result.
- [ ] Classify 401/403/429/5xx, transport/timeout, redirect, malformed/missing-field, and unavailable outcomes into safe error codes and summaries; never expose raw bodies, headers, credentials, URLs/query secrets, or sensitive account identifiers.
- [ ] **RED:** add synthetic HTTP/auth contract fixtures and tests for valid/version-variant/missing-field responses, status classes, redirect rejection, timeout/cancellation, retry limits, `Retry-After`, optional-detail failure, and secret-free errors.
- [ ] **GREEN:** implement the allowlisted adapter, version-neutral mapping, failure classification, and bounded retry behavior against the synthetic handler/fixtures only.
- [ ] **TRIANGULATE:** verify no cross-origin redirect can carry authorization, no retry occurs for 401/403/malformed payloads, one transient retry is bounded, and optional failure preserves primary windows.
- [ ] **REFACTOR:** isolate endpoint details behind `IQuotaProvider`, reuse Slice 2A redaction/lifetime boundaries, and keep live private-endpoint tests opt-in and outside ordinary CI.

Slice 3–8 and cross-slice gates remain unchecked exactly as listed in the persisted tasks artifact above.

## Slice 2B applied (2026-07-12)

Slice 2B is complete on `feature/aibar-foundation-slice-2b`, based on approved Slice 2A commit `e72da52`. This work unit is synthetic-only: no live endpoint, credential, Codex file, account, cache, coordinator, or Slice 3+ behavior was used or added.

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, 12/60 tasks complete before this unit, no blocked reasons; `actionContext: repo-local` with allowed root `C:\\Users\\mjsal\\Desarrollos IA\\Modificacion de Terminales\\aibar`. Delivery was resolved as `auto-chain` / `feature-branch-chain`; current PR boundary is Slice 2B only. No action-context warnings.

### Completed tasks and persisted checkbox evidence

The seven Slice 2B task lines are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md`: feature-gated `HttpClient` provider; exact HTTPS base origin; disabled redirects; per-request bearer/account headers scoped to a disposable Slice 2A credential; 5-second connect and 10-second request bounds; cancellation; one transient retry with capped `Retry-After`; primary `/wham/usage` plus optional reset-credit mapping; validated 0–100 percentages and timestamps; safe status/transport/timeout/redirect/malformed/disabled failures; and optional-detail degradation.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added synthetic `QuotaHttpProviderTests`; focused test run before the provider existed | Failed as expected: `CS0246` (`QuotaHttpProvider` absent). |
| GREEN | Added the minimal provider and domain result fields; focused tests | Passed 7/7. |
| TRIANGULATE | Variant fields, missing/invalid percentages, redirect, 401/403/400, 429 + `Retry-After`, cancellation, transport/timeout, retry bound, and optional 503 cases | Passed 7/7; primary snapshot survives optional failure. |
| REFACTOR | Kept endpoint strings private to the adapter, only safe fixed error codes, and reused `RequestCredential` disposal | Full build/suite pass with no warnings/errors. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~QuotaHttpProviderTests --logger "console;verbosity=minimal"
Passed: 7, Failed: 0, Skipped: 0, Total: 7.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 31, Failed: 0, Skipped: 0, Total: 31.

git diff --check
Passed.
```

LSP diagnostics are unavailable in this executor session; the zero-warning compiler build is the available static diagnostic evidence.

### Files changed / workload / deviations

- `src/AIBar.Application/QuotaHttpProvider.cs`
- `src/AIBar.Domain/Foundation.cs`
- `tests/AIBar.Domain.Tests/QuotaHttpProviderTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

Slice 2B authored implementation/test lines are 166 (`91` adapter + `71` tests + four domain change lines), plus this task/progress evidence; the current work unit remains below the 400-line limit. Feature-branch-chain boundary: Slice 2B only; dependency: Slice 2A; follow-up: Slice 3. No commit, push, PR, review transaction, or Judgment Day action occurred. No design deviation: optional credits are exposed only through the adapter result because cache/coordinator work is deferred to Slice 3.

### Remaining tasks

Slice 3–8 and cross-slice gates remain unchecked. The next exact unchecked line is:

- [ ] Implement `QuotaRefreshCoordinator` with cache-first publication, one in-flight refresh task, trigger coalescing, manual-refresh freshness bypass, conservative polling, cancellation, sleep/resume and clock-change re-evaluation.

## Slice 3 budget gate (2026-07-12)

**Blocked before any Slice 3 code, test, project, or task-artifact edit.** Parent-provided structured status was consumed: authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, `19/60` complete, no blocked reasons; action context is `repo-local` with workspace/allowed root `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is resolved as `auto-chain` / `feature-branch-chain`; this requested work unit is Slice 3 only, with parent Slice 2B commit `9351927`. No action-context warning applies.

The required Slice 3 scope cannot credibly fit the enforced **<=400 authored changed-line** budget when code, focused tests, project wiring, and this required OpenSpec evidence are counted. No production code or RED test was started, so strict TDD evidence has no cycle for this blocked gate.

| Required concern | Credible forecast (lines) |
|---|---:|
| Coordinator state, cache-first publication, trigger coalescing, manual bypass, polling, cancellation, clock/lifecycle handling | 160 |
| SQLite connection/schema migration, normalized snapshot mapping, atomic save/load/clear and privacy boundary | 150 |
| Coordinator fake-clock/provider/store tests: startup cache, coalescing, bypass, stale/failure, cancellation, optional degradation | 175 |
| SQLite integration tests: migration, FK/WAL, atomicity, crash-before-commit and normalized-data inspection | 145 |
| Domain/project wiring and task/progress evidence | 35 |
| **Total** | **665** |

Actual Slice 3 authored implementation/test/project lines: **0**. This append-only audit evidence changes 29 lines in `apply-progress.md`; no persisted task checkbox changed.

### Required dependency-ordered split

1. **Slice 3A — quota snapshot SQLite store** (~330 lines including tests and OpenSpec evidence): add the SQLite package/project wiring; schema versioning/migration; `quota_snapshot` normalized fields only; foreign-key/WAL setup; atomic load/save/clear; synthetic persistence integration tests for migration, normalized-data inspection, atomic rollback/crash-before-commit, and no credentials/raw bodies. Depends on Slice 2B; no coordinator.
2. **Slice 3B — refresh coordinator state machine** (~375 lines including tests and OpenSpec evidence): cache-first publication using the 3A store; single in-flight provider operation; trigger coalescing; manual freshness bypass; conservative scheduling; failure/stale/unavailable state overlay preserving original timestamp; optional-detail degradation; cancellation/clear-data publication; fake-clock/provider/store tests. Depends on 3A; no tray/UI.
3. **Slice 3C — lifecycle re-evaluation hardening** (~230 lines including tests and OpenSpec evidence): sleep/resume and clock-change event boundary wired to coordinator reevaluation, deterministic fake-time lifecycle/cancellation race tests, plus focused full-suite/build diagnostics evidence. Depends on 3B; no Slice 4 presentation.

Each slice is independently reviewable and stays within the 400-line budget without removing required persistence or deterministic test behavior. Feature-branch-chain order: `9351927` → **3A** → **3B** → **3C** → Slice 4. 📍 Current blocked boundary: Slice 3 (must be replaced by 3A/3B/3C before implementation).

### Verification and remaining work

No test, build, or LSP command was run because the pre-edit budget gate stopped implementation. No design deviation occurred. Slice 3's five persisted `- [ ]` lines remain unchecked; the next recommended action is to approve the proposed Slice 3A/3B/3C delivery boundary and apply Slice 3A only.

## Slice 3A applied (2026-07-12)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blocked reasons. `actionContext` is `repo-local`, with workspace and allowed root `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`; no action-context warnings. Delivery is `auto-chain` / `feature-branch-chain`; parent dependency is Slice 2B commit `9351927`; current boundary is **Slice 3A only**. No coordinator, polling, lifecycle, tray, UI, or Slice 3B/3C work was added.

### Completed tasks and checkbox evidence

The four Slice 3A task lines (RED, GREEN, TRIANGULATE, REFACTOR) are visibly marked `- [x]` in the persisted `openspec/changes/aibar-foundation/tasks.md` artifact. The store uses a temporary synthetic database per test, opens SQLite with foreign keys and WAL, keeps one normalized snapshot row with primary/weekly values, optional reset-credit column, retrieval timestamp, and schema-adapter version, and supports transactional save/load/clear. It persists no credentials, raw HTTP bodies, headers, or source paths.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `SqliteQuotaSnapshotStoreTests` and the SQLite project dependency, then ran the focused test command before the store existed. | Failed as expected: `CS0246` for missing `SqliteQuotaSnapshotStore`. |
| GREEN | Implemented the smallest store, normalized schema, v1→v2 migration, transactional operations, WAL/foreign-key setup, and disposal lock. | Focused suite passed 4/4. |
| TRIANGULATE | Added repeated save/load, absent optional detail, malformed row, future-schema/no-downgrade, migration, and injected pre-commit failure cases. | Focused suite passed 5/5; failed save retained the prior row. |
| REFACTOR | Centralized schema SQL and connection initialization; disabled pooling so temporary WAL databases deterministically release locks for cleanup. | Full solution build/test passed with 0 warnings/errors. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~SqliteQuotaSnapshotStoreTests --logger "console;verbosity=minimal"
RED: failed as expected (CS0246).

dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~SqliteQuotaSnapshotStoreTests --logger "console;verbosity=minimal"
GREEN: Passed: 4, Failed: 0, Skipped: 0, Total: 4.
TRIANGULATE: Passed: 5, Failed: 0, Skipped: 0, Total: 5.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 38, Failed: 0, Skipped: 0, Total: 38.

git diff --check
Passed.
```

No LSP diagnostic tool is injected in this executor session; the zero-warning compiler build is the available static diagnostic evidence.

### Files changed / migration / rollback

- `src/AIBar.Application/AIBar.Application.csproj` — `Microsoft.Data.Sqlite` 8.0.8 boundary.
- `src/AIBar.Application/SqliteQuotaSnapshotStore.cs` — serialized/disposable SQLite store, schema v2, v1 migration, future-version rejection, atomic save/clear.
- `src/AIBar.Domain/Foundation.cs` — adds `IQuotaSnapshotStore.ClearAsync`.
- `tests/AIBar.Domain.Tests/SqliteQuotaSnapshotStoreTests.cs` — temporary synthetic integration tests only.
- `openspec/changes/aibar-foundation/tasks.md` and this cumulative progress record.

The v1→v2 migration preserves the existing normalized row; a database newer than v2 is rejected before mutation, so downgrade is non-destructive. Rollback removes only the 3A dependency/store/tests and leaves existing data untouched; no Codex-owned file is accessed or deleted. The injected pre-commit failure throws before transaction commit, and the test proves the previous committed row survives.

### Workload / remaining tasks

Current feature-branch-chain PR boundary: **Slice 3A** (`9351927` → 3A → 3B → 3C → Slice 4). This work unit adds 178 implementation/test/project lines before the 3A task/progress evidence. The complete current work-tree diff from `9351927`, including inherited Slice-3 split metadata and this 3A OpenSpec evidence, is 336 changed lines (314 additions, 22 deletions), below the 400-line authored budget. No commit, push, PR, or review transaction occurred.

Remaining Slice 3 work is exactly:

- [ ] **RED:** add fake-clock/provider/store tests for cache-first startup publication, one in-flight refresh, concurrent trigger coalescing, manual freshness bypass, conservative polling eligibility, failure overlays, original timestamp preservation, stale/unavailable threshold transitions, optional-detail degradation, cancellation, and clear-data publication suppression.
- [ ] **GREEN:** implement `QuotaRefreshCoordinator` using the 3A store and 2B provider: publish cached state first, gate one asynchronous refresh task, coalesce poll/popover-open/resume/manual triggers, let manual refresh bypass freshness but not concurrency, schedule conservatively, preserve the prior snapshot/timestamp on failure, overlay classified errors, and publish explicit stale/unavailable states without fabricating percentages.
- [ ] **RED:** add deterministic tests for sleep/resume, system clock forward/backward changes, resume freshness re-evaluation, stale-to-current/unavailable transitions, prior timestamp preservation, optional-detail degradation, and clear-data/cancellation races using the existing coordinator contracts.
- [ ] **GREEN:** add the lifecycle boundary that forwards sleep/resume and clock-change notifications to coordinator re-evaluation; harden stale/unavailable transitions, retain original successful timestamps on failures, preserve primary data when optional details fail, and prevent cancelled/cleared work from republishing.

## Slice 3B applied (2026-07-13)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blocked reasons; workspace and allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is `auto-chain` / `feature-branch-chain`; the boundary is **Slice 3B only**, based on approved Slice 3A commit `73919f3`. Strict TDD is false, so Standard Mode applied with focused behavior tests. No action-context warnings.

### Completed tasks and persisted checkbox evidence

The four Slice 3B task lines (RED, GREEN, TRIANGULATE, REFACTOR) are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md`. `QuotaRefreshCoordinator` publishes the persisted cache first, exposes immutable test-observable state, coalesces in-flight triggers, allows manual freshness bypass without bypassing the concurrency gate, avoids fresh ordinary polls, saves successful normalized snapshots, retains the prior timestamp on failure, exposes stale/unavailable/error/optional-degradation state, and suppresses late publication after clear/cancellation. It contains no UI, dispatcher, tray, lifecycle adapter, or thread-affinity dependency.

### Focused behavior evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~QuotaRefreshCoordinatorTests --logger "console;verbosity=minimal"
RED: failed as expected: CS0246 (QuotaRefreshCoordinator absent).
GREEN/TRIANGULATE: Passed: 8, Failed: 0, Skipped: 0, Total: 8.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 48, Failed: 0, Skipped: 0, Total: 48.

git diff --check
Passed.
```

LSP diagnostics were requested before command execution, but no LSP diagnostic tool is injected in this executor session. The zero-warning compiler build is the available static diagnostic evidence.

### Files / workload / deviations

- `src/AIBar.Domain/Foundation.cs` — immutable refresh trigger/state contracts.
- `src/AIBar.Application/QuotaRefreshCoordinator.cs` — application-only cache/refresh state machine.
- `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs` — deterministic fake clock/provider/store behavior tests.
- `openspec/changes/aibar-foundation/tasks.md` — completed Slice 3B checkboxes.
- `openspec/changes/aibar-foundation/apply-progress.md` — this cumulative Slice 3B record.

Authored implementation/test lines: **215** (106 coordinator + 107 tests + 2 domain contracts). The Slice 3B implementation/test change is below the 400-line budget; OpenSpec evidence is separately required delivery metadata. No design deviation: lifecycle re-evaluation remains intentionally deferred to Slice 3C. No commit, stage, push, PR, review transaction, Judgment Day, branch creation, credential, network call, or UI work occurred.

### Remaining work

Slice 3B has no unchecked tasks. Slice 3C remains the next bounded work unit:

- [ ] **RED:** add deterministic tests for sleep/resume, system clock forward/backward changes, resume freshness re-evaluation, stale-to-current/unavailable transitions, prior timestamp preservation, optional-detail degradation, and clear-data/cancellation races using the existing coordinator contracts.
- [ ] **GREEN:** add the lifecycle boundary that forwards sleep/resume and clock-change notifications to coordinator re-evaluation; harden stale/unavailable transitions, retain original successful timestamps on failures, preserve primary data when optional details fail, and prevent cancelled/cleared work from republishing.
- [ ] **TRIANGULATE:** inject reordered lifecycle events, duplicate notifications, clock jumps, cancellation at each publication boundary, provider failure during resume, and store-unavailable conditions; verify deterministic state traces and no overlapping refreshes.
- [ ] **REFACTOR:** make event subscriptions/disposal idempotent, centralize degradation rules, remove duplicated transition handling, and run the affected suite plus static/build diagnostics without adding UI coupling.

Slice 4 and later UI/wiring work remain out of scope.

## Slice 3C applied (2026-07-13)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, `verify: blocked`, and no blocked reasons. The allowed repository edit root is `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`; no action-context warnings apply. Delivery is `feature-branch-chain`; this boundary is **Slice 3C only**, based on approved Slice 3B commit `cac832e`. Strict TDD is false, so Standard Mode applied with focused deterministic behavior tests.

### Completed tasks and persisted checkbox evidence

The four Slice 3C task lines (RED, GREEN, TRIANGULATE, REFACTOR) are visibly marked `- [x]` in the persisted tasks artifact. The application-only lifecycle adapter subscribes idempotently to sleep, resume, and clock-change events, forwards them to coordinator freshness re-evaluation, and unsubscribes on disposal. Re-evaluation republishes only a changed freshness state; sleep never starts a request, while resume/clock-change joins the existing coordinator request gate. Existing generation/cancellation safeguards continue to prevent clear/cancelled work from publishing late data.

### Focused behavior evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~QuotaRefreshCoordinatorTests"
RED: failed as expected with CS0246 because IQuotaRefreshLifecycleEvents did not yet exist.
GREEN/TRIANGULATE/correction: Passed: 14, Failed: 0, Skipped: 0, Total: 14.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore
Passed: 54, Failed: 0, Skipped: 0, Total: 54.

git diff --check
Passed (only Git LF-to-CRLF conversion warnings were emitted).
```

LSP diagnostics were requested before validation commands, but no LSP diagnostic tool is injected in this executor session. The zero-warning compiler build is the available static diagnostic evidence. Tests cover suspended/resumed/clock-change forwarding, forward and backward clock changes, duplicate resume/clock notifications coalescing to one provider call, stale-to-current transition, resume failure with original timestamp preservation, optional-detail degradation, store-save degradation, and clear/dispose late-publication suppression.

### Files / workload / deviations

- `src/AIBar.Application/QuotaRefreshLifecycleAdapter.cs` — lifecycle event boundary and idempotent disposal.
- `src/AIBar.Application/QuotaRefreshCoordinator.cs` — explicit lifecycle freshness re-evaluation.
- `src/AIBar.Domain/Foundation.cs` — lifecycle refresh trigger values.
- `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs` — deterministic lifecycle/degradation race tests.
- `openspec/changes/aibar-foundation/tasks.md` — completed Slice 3C checkboxes.
- `openspec/changes/aibar-foundation/apply-progress.md` — this cumulative record.

Authored implementation/test changes are **241 lines** (238 additions, 3 deletions, including the new adapter); the bounded reliability correction is **95 lines** relative to the frozen review tree, within its 114-line budget and the 400-line Slice 3C limit. OpenSpec task/progress metadata is required delivery evidence. No UI/tray work, Slice 4+, commit, stage, push, PR, branch creation, review transaction, Judgment Day, network call, credential access, or writes outside the repository occurred. No design deviation: the boundary remains application-only and does not reference WPF, tray, or dispatcher APIs.

### Remaining work

No Slice 3C task remains unchecked. Slice 4 and later work remain out of scope; the next exact unchecked persisted line is:

- [ ] Implement the single-process WPF host, named-mutex single-instance activation, tray recreation handling, taskbar/DPI-aware borderless popover positioning, deactivation behavior, and explicit Exit command that cancels work and closes persistence.

The remaining unchecked Slice 4–8 and cross-slice-gate lines remain unchanged in `tasks.md`; verification is the next phase only after the entire change's remaining tasks are complete.

## Slice 4 planning gate (2026-07-13)

Planning-only status: Slice 4 implementation/test/project lines remain **0**. The approved split and current next action are recorded in the final `Slice 4A split approval` note below; no duplicate workload history is repeated here.

## Slice 4A split approval (2026-07-13)

**Approved decision recorded; implementation not started.** Replace the unchecked ~465-line 4A unit with two independently testable/revertible units: **4A.1** (~315 including evidence) for WPF host primitives, named-mutex single-instance activation/secondary-activation handoff, pure taskbar/DPI-aware placement, and deterministic tests; and **4A.2** (~260) for the live tray bridge, taskbar recreation, popover show/hide/deactivation, orderly explicit Exit, and focused Windows smoke tests using 4A.1. Neither unit includes 4B/4C presentation scope.

**Approved chain and next action:** `1b9cca5 → 4A.1 → 4A.2 → 4B → 4C → 5`. Apply 4A.1 only after task review. All new task checkboxes remain unchecked. No implementation, XAML, project, test, screenshot, build, stage, commit, branch, push, or review action was performed. The complete planning diff from `1b9cca5` still requires `git diff --stat`/`git diff --check` verification by an execution environment with Git command access; no such command tool is available in this phase.

## Slice 4A.1 applied (2026-07-13)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`; repo-local edit root; no blockers. Boundary: **4A.1 only** in the approved `auto-chain` / `feature-branch-chain` path. Strict TDD is false; test-first evidence is retained.

### Completed tasks / evidence

The four 4A.1 RED/GREEN/TRIANGULATE/REFACTOR lines are visibly `- [x]` in `tasks.md`. `SingleInstanceHost` owns a named mutex and uses a named auto-reset activation event: secondaries signal, while the primary drains one pending activation per dispatch. `PopoverPlacement` is pure and consumes monitor/work-area rectangles and DPI scale; it clamps size/position to the work area. No tray runtime/recreation, deactivation/show-hide behavior, Exit orchestration, presentation state, cards, or disclosures was added.

| Cycle | Evidence | Result |
|---|---|---|
| RED | `HostPrimitivesTests` compiled before the primitives existed | Failed as expected with `CS0246`. |
| GREEN | Minimal host/placement primitives | Focused tests passed 4/4. |
| TRIANGULATE | Same-process takeover with another handle alive, bounded real child-process election/handoff, abandoned-owner recovery, every NaN/±Infinity field, invalid dimensions/DPI, and overflow | Focused tests passed 7/7. |
| REFACTOR | Kept WPF/platform code in Desktop and policies pure | Full build/tests passed. |

### Verification / workload

Correction focused command `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~HostPrimitivesTests"` passed 7/7, including a timeout-bounded `dotnet test --no-build` child process that is terminated and awaited for abandoned-owner evidence. Full-suite/build/diff checks are recorded from the final correction validation. LSP diagnostics remain unavailable in this executor session; the compiler build is the available static diagnostic evidence.

Files: `src/AIBar.Desktop/HostPrimitives.cs`, `tests/AIBar.Domain.Tests/HostPrimitivesTests.cs`, `tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj`, plus persisted tasks/progress. No design deviation. The pre-existing planning diff was 69 lines; the post-implementation complete authored count is recorded after final validation and remains within 400. No stage, commit, push, branch, PR, or review transaction occurred.

**Remaining:** all 4A.2, 4B, 4C, Slice 5+, and cross-slice lines remain unchecked; next boundary is 4A.2 only.

Exact next unchecked Slice 4 lines:

- [ ] **RED:** add focused Windows smoke tests for tray creation/toggle, secondary activation handoff, Explorer/taskbar recreation, popover focus/deactivation, and Exit cancellation/persistence disposal ordering.
- [ ] **GREEN:** implement the tray runtime bridge, recreation handling, borderless popover show/hide and deactivation behavior, and explicit Exit orchestration that cancels work, closes persistence, and exits orderly.
- [ ] **TRIANGULATE:** exercise taskbar recreation, repeated toggles, owned-dialog deactivation, shutdown races, cancellation/close ordering, and activation handoff through the 4A.1 contract on a representative Windows environment.
- [ ] **REFACTOR:** isolate runtime Win32/WPF adapters, make tray recreation and shutdown idempotent, keep coordinator/persistence calls at the host boundary, and rerun focused smoke tests plus diagnostics.

The 4B and 4C task lines remain unchecked as persisted in `tasks.md`; they are intentionally deferred with Slice 5+ and cross-slice gates. Final complete authored diff from `1b9cca5`: **282** lines (tracked `git diff --numstat 1b9cca5 --`: 118 additions + 38 deletions = 156; two unstaged new source/test files: 126 additions). This is within the 400-line limit.

## Slice 4A.2 applied (2026-07-13)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`; repo-local allowed root; no blockers. Delivery is `auto-chain` / `feature-branch-chain`; boundary: **4A.2 only**, based on approved 4A.1 commit `5e13ef5`. Strict TDD is false; focused test-first evidence was retained. No action-context warnings.

### Completed tasks / persisted checkbox evidence

The four 4A.2 RED/GREEN/TRIANGULATE/REFACTOR task lines are visibly `- [x]` in `tasks.md`. `TrayHostRuntime` composes the approved `SingleInstanceHost` with an actual WinForms `NotifyIcon`, WPF borderless popover adapter, and `TaskbarCreated` Win32 monitor. It toggles/show-hides the popover, preserves an owned dialog on deactivation, recreates the tray icon after Explorer/taskbar recreation, and blocks late activation after Exit. Exit is idempotent: it cancels active work, awaits cancellation, disposes injected persistence before the tray resource, and requests process shutdown. Event/fire-and-forget boundaries swallow exceptions.

### RED → GREEN → TRIANGULATE → REFACTOR evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `HostRuntimeTests`, then ran the focused filter before runtime contracts existed. | Failed as expected with `CS0246` for `ITrayRuntime`, `IPopoverRuntime`, and taskbar recreation contracts. |
| GREEN | Added minimal runtime adapters and lifecycle orchestration. | Focused tests passed 2/2. |
| TRIANGULATE | Covered repeated toggle, owned-dialog deactivation, taskbar recreation, secondary activation handoff, repeated Exit, cancellation-before-disposal, and late activation suppression. | Focused tests passed 2/2. |
| REFACTOR | Kept Win32/WPF/WinForms inside Desktop; host shutdown depends only on injected lifecycle resources. | Full build/tests passed with 0 warnings/errors. |

### Verification / smoke evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~HostRuntimeTests"
Passed: 2, Failed: 0.

dotnet test AIBar.sln --no-restore
Passed: 63, Failed: 0.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

git diff --check
Passed.

Bounded Windows smoke: launched Debug AIBar.Desktop.exe, observed it remained running for two seconds, then forcibly stopped it.
```

The bounded smoke proves the composed Windows host launches; it does not automate Explorer restart, owned-dialog focus, or orderly Exit interaction. Those behaviors have deterministic adapter tests; manual Windows 10/11 matrix evidence remains a later compatibility obligation.

### Files / workload / remaining work

- `src/AIBar.Desktop/HostRuntime.cs` — tray, WPF popover, Explorer/taskbar monitor, and orderly shutdown boundary.
- `src/AIBar.Desktop/App.xaml`, `App.xaml.cs`, `AIBar.Desktop.csproj` — live host composition and Windows Forms support.
- `tests/AIBar.Domain.Tests/HostRuntimeTests.cs` — deterministic runtime/lifecycle tests.
- `openspec/changes/aibar-foundation/tasks.md` and this cumulative progress record.

No quota view-models, quota/analytics/cost disclosures, visual tokens/cards/accessibility visuals, scanner, packaging, or Slice 5+ scope was added. LSP diagnostics are unavailable in this executor session; the zero-warning compiler build is the available static diagnostic evidence. No commit, stage, push, PR, branch, review transaction, credential, network, or user-data access occurred. The next remaining boundary is Slice 4B, followed by 4C; all of their persisted task lines remain unchecked.

## Bounded reliability correction (2026-07-13)

- `RELIABILITY-001`: the live runtime now starts one UI-dispatcher timer that drains secondary activation signals automatically and stops/unsubscribes before shutdown; the focused test pumps the dispatcher and observes activation without manual dispatch.
- `RELIABILITY-002`: process-exit failures now fault `ExitAsync`; retry reuses the completed cleanup and invokes only process exit again, preserving cancellation → persistence → tray → process-exit order.
- Focused tests: 3/3 passed. Full suite: 64/64 passed. Build: 0 warnings/errors. `git diff --check`: passed with line-ending warnings only.
- LSP diagnostics were requested but unavailable because no LSP tool is injected in this executor session; compiler diagnostics are clean.
