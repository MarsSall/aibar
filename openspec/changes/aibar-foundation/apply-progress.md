# Apply Progress — AIBar Foundation

## Status

**Slice 1 complete.** The prior SDK blocker is resolved: .NET SDK `8.0.408` is installed alongside `6.0.424`. Only the approved Solution Skeleton and Pure Contracts work unit was implemented. No Slice 2+ implementation, credentials, HTTP, SQLite, scanner, tray behavior, polished UI, commit, push, branch, or PR was created.

## Preserved blocker history

The previous apply attempt was blocked before implementation because only .NET SDK `6.0.424` was installed and `net8.0` templates were unavailable. Its temporary empty solution was removed, no checkbox was changed, and no application source was retained. That blocker is resolved by the verified SDK `8.0.408`; it is retained here for audit continuity.

## Structured status consumed

- Change: `aibar-foundation`
- Native status: authoritative OpenSpec status, `applyState: ready`, `nextRecommended: apply`, no blocked reasons
- Action context: `repo-local`; workspace root and allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`
- Delivery: `auto-chain` / `feature-branch-chain`
- PR boundary: Slice 1 only; Slice 2+ remain out of scope
- Strict TDD configuration: false; Slice 1's explicit test-first requirement was followed.

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
