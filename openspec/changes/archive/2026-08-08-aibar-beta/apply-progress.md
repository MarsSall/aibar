# Apply Progress: AIBar Private Beta

## Unit 1 — Consent, Credentials, and Quota

**Mode:** Standard behavior-first (`strict_tdd: false`)

### Completed tasks

- [x] 1.1 Default-off, disclosed and revocable consent now persists only `schema` and `privateCodexConsent`. Credential lookup is suppressed while disabled and exposes only `Disabled`, `Available`, `Missing`, or `Unusable`; revocation and exit cancel/await active quota work and the coordinator rejects late results.
- [x] 1.2 The composed quota runtime reads five-hour and weekly snapshots, preserves the latest safe snapshot for cached degraded network or missing-credential states, exposes cached age, reports no-snapshot failure as unavailable, and coalesces manual, popover, poll, resume, and clock triggers through the existing coordinator.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused test command | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed: 7/7, 0 failed (52 ms). |
| Behavior-first RED evidence | The same focused command before implementation failed to compile because `ConsentSettings` and `BetaRuntime` did not exist. |
| Runtime harness | N/A for a live external boundary: the assigned scope explicitly forbids a live private endpoint. The seven-test in-process harness covers enabled/disabled, missing credential, offline cached fallback, revocation, late completion, disposal, and trigger coalescing without credential mutation. |
| Regression build | `dotnet build tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore /m:1` — passed with 0 errors (existing unsigned-reference warnings only). |
| Full solution regression | `dotnet test AIBar.sln --no-restore` exceeded the 180-second harness limit after reporting two unrelated packaging failures: `PackagingSupervisorAuthorityTests.B2_Core_relocation_preserves_the_five_materialized_source_bytes` (fixture hash mismatch) and `PackagingDistributionTests.GraphProjection_is_canonical_and_rejects_every_authoritative_fixture_fault` (missing temporary `sbom.cdx.json`). An initially reported credential-reader regression was fixed and its focused existing test passes. |
| Rollback boundary | Revert only `ConsentSettings.cs`, `BetaRuntime.cs`, the Unit-1 edits in `CredentialBoundary.cs`, `QuotaHttpProvider.cs`, `StartupSettings.cs`, `App.xaml.cs`, and `BetaConsentOrQuotaTests.cs`; remove the two Unit-1 task checks and this progress artifact. |

### Bounded pre-commit correction

- `QuotaPresentationHost` now accepts a dynamic refresh predicate while retaining the existing `bool` constructor. Composition passes the command unconditionally, so persisted consent loaded by `BetaRuntime` makes refresh executable; revocation refreshes both property and command state without exposing runtime internals.
- `BetaRuntime` is the sole startup initializer of `QuotaRefreshCoordinator`; the presentation host no longer reloads cached state during startup.

| Evidence | Result |
|---|---|
| RED test command | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~QuotaPresentationHostTests" --no-restore /m:1` — failed before production changes with CS1660/CS1061/CS0117: the dynamic predicate, availability refresh seam, and single-owner startup helper did not exist. |
| Focused regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed: 7/7, 0 failed. |
| Presentation and host tests | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~QuotaPresentationHostTests|FullyQualifiedName~QuotaPresentationTests|FullyQualifiedName~HostRuntimeTests" --no-restore /m:1` — passed: 20/20, 0 failed. New tests prove persisted consent enables and executes refresh, revoked consent prevents it, and startup loads/publishes cached state once while a refresh is in flight. |
| Direct affected build | `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — passed: 0 warnings, 0 errors. `dotnet build AIBar.sln --no-restore /m:1` remains blocked before source compilation by the missing pre-existing `tools/AIBar.Packaging.Supervisor/obj/project.assets.json` restore artifact. |
| Runtime harness | The new in-process persisted-settings and blocking-provider harness exercises the startup path without credential mutation or a private endpoint; no live endpoint was accessed. |
| Rollback boundary | Revert the correction hunks in `QuotaPresentation.cs`, `HostRuntime.cs`, and `App.xaml.cs`, remove `QuotaPresentationHostTests.cs`, and remove this correction section; Unit-1 consent/quota behavior remains otherwise intact. |

### Scope and deviations

None. No Unit 2 presentation bindings, Unit 3 analytics, Unit 4 distribution, packaging authority, external endpoint, credential mutation, commit, staging, or release action was performed.

**Changed-line accounting:** The original Unit-1 implementation was 493 authored lines. This bounded correction adds only dynamic refresh availability, one startup initialization seam, host notification wiring, focused behavior coverage, and evidence; the complete Unit-1 work remains below the 1,000-line native authority ceiling.

## Unit 2 — Shared Presentation

**Mode:** Standard behavior-first (`strict_tdd: false`)

### Completed tasks

- [x] 2.1 `BetaPresentationState` is an immutable shared snapshot for tray and popup. It provides current/loading/cached/degraded/missing-credential/offline/unavailable/safe-error classifications, cached age, safe warnings, and disclosure values. Reset countdowns are remapped from source reset instants against `IClock` on every popup display and clock-change event.
- [x] 2.1 The WPF host owns one current presentation state, sends that same instance to the tray, updates refresh availability dynamically, routes popup-open through the coordinator's `PopoverOpened` trigger, and wires Windows resume/time changes through the existing lifecycle adapter. Reverse disposal detaches lifecycle and presentation handlers before quota/runtime/store disposal.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| Behavior-first RED evidence | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaPresentation" --no-restore /m:1` failed before production changes with missing `BetaPresentationState`, presentation state bindings, lifecycle constructor seam, and popup trigger callback. |
| Corrective RED evidence | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Popup_display_remaps_countdowns_below_freshness_threshold" --no-restore /m:1` — failed before the host change: expected remapped `00:55:00`, actual stale `01:00:00`. |
| Focused test command | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaPresentation" --no-restore /m:1` — passed: 5/5, 0 failed (26 ms). |
| Direct presentation/host regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~QuotaPresentationHostTests|FullyQualifiedName~QuotaPresentationTests|FullyQualifiedName~QuotaVisualDesignTests|FullyQualifiedName~HostRuntimeTests" --no-restore /m:1` — passed: 24/24, 0 failed (1 s). |
| Bounded Unit-1 regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed: 7/7, 0 failed (77 ms). |
| Runtime harness | The five-test in-process tray/popup/lifecycle harness advances `IClock` by five minutes below the ten-minute freshness threshold, opens the popover, verifies the `00:55:00` remap, and proves the exact resulting immutable instance reaches both the popup property observer and tray observer without a provider call. No live endpoint was accessed; private endpoint use remains outside this work unit. |
| Direct affected build | `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — passed: 0 warnings, 0 errors (1.53 s). |
| Solution build | `dotnet build AIBar.sln --no-restore /m:1` — blocked before remaining solution compilation by the pre-existing missing `tools/AIBar.Packaging.Supervisor/obj/project.assets.json` restore artifact (`NETSDK1004`). No restore or packaging action was performed. |
| Diff check | `git diff --check` — passed with no whitespace errors. The worktree remains based exactly on `f2d72b63dd5a50f67f94997d1fbe8f4da39e0cba`. |
| Rollback boundary | Revert `QuotaPresentation.cs`, `HostRuntime.cs`, `WindowsLifecycleEvents.cs`, the Unit-2 composition and XAML hunks in `App.xaml.cs` and `MainWindow.xaml`, `BetaPresentationTests.cs`, and the Unit-2 fake-tray method; Unit-1 consent/quota behavior remains intact. |

### Scope and deviations

None. Unit 3 analytics, Unit 4 distribution, packaging authority, cost/trend/ETA polish, credential mutation, live endpoint access, commit, staging, push, PR, review, and original-worktree mutation were not performed.

**Changed-line accounting:** Unit 2 is a focused presentation slice well below the 1,000 authored-line limit. New behavior is limited to shared state, WPF/tray/lifecycle wiring, and behavior-first coverage.

## Planning Amendment — Documentation Only

**Mode:** Standard documentation review (`strict_tdd: false`)

### Completed task

- [x] P.1 The six planning artifacts now split the future analytics work into 3A and 3B, preserve Unit 1/2 history, and make the Planning Amendment the exact parent of 3A. This checkbox was set only after structural readback, exact six-file accounting, whitespace validation, status, and checkbox/word coherence review.

### Historical, blocked combined Unit 3 attempts

The former combined Unit 3 records are historical and non-authoritative. They are blocked because a single unit cannot truthfully claim both deterministic analytics core and production lifecycle ownership under the bounded shutdown contract. They do not complete 3A or 3B, and their prior evidence must not be used for either successor.

On a non-cooperative timeout or failure, the normative retained-on-timeout semantics apply: publication is suppressed, the owner does not re-await or dispose the potentially active analytics view/store, those process-owned resources remain until process exit, and independent resources continue reverse disposal without masking the typed first outcome. Dependent analytics disposal occurs only after cooperative completion within the bound.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| Structural readback | At Planning Amendment close, all six in-scope artifacts were read; Unit 1/2 and Planning were complete, while 3A/3B/4 remained unchecked. |
| Focused validation | `git diff --check` — passed with no whitespace errors. |
| Runtime harness | N/A: this is documentation-only and changes no product, test, or runtime boundary. No tests, builds, runtime/native attempts, staging, commits, or review were run. |
| Rollback boundary | Revert the exact future Planning Amendment commit affecting only the six planning artifacts; Unit 1/2 product behavior and the later preserved product/test work remain untouched. |

### Scope and preservation

The Planning Amendment diff contained only `proposal.md`, `specs/local-usage-analytics/spec.md`, `specs/private-beta-distribution/spec.md`, `design.md`, `tasks.md`, and this `apply-progress.md`; it excluded code and tests. Product/test changes were preserved, without application or inspection, in selective `stash@{0}` named `aibar-beta-unit3-product-split` for later branch restoration.

## Unit 3A — Local Analytics Core

**Mode:** Standard behavior-first (`strict_tdd: false`)

### Completed tasks

- [x] 3A.1 Behavior-first synthetic tests prove factual local token/model totals and scan time; complete, empty, partial, and unavailable coverage; local labels; immutable snapshots; and safe unreadable/mutating source warnings that preserve readable aggregates.
- [x] 3A.2 `LocalCodexAnalyticsAdapter` composes discovery, scanner, and SQLite aggregation. The scanner has injectable stream-open/pre-stability seams and classifies unreadable versus changed sources without exposing paths.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| RED → GREEN | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics" --no-restore /m:1` — RED: 2 failed, 1 passed; GREEN: 4/4 passed. |
| Scanner regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~SessionJsonlScanner" --no-restore /m:1` — 12/12 passed. |
| Bounded regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota|FullyQualifiedName~BetaPresentation" --no-restore /m:1` — 12/12 passed. |
| Runtime harness | Synthetic temporary `sessions`/`archived_sessions` discovery → injected scanner → SQLite path passed for unreadable and mutating sources; no live Codex files or endpoint. |
| Builds | `dotnet build src/AIBar.Application/AIBar.Application.csproj --no-restore /m:1` and `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — passed, 0 warnings/errors. |
| Rollback boundary | Delete `LocalCodexAnalytics.cs` and `BetaAnalyticsTests.cs`; restore only the named seams in `SessionJsonlScanner.cs` and these two task checks/progress entries. |

### Scope

No WPF, `BetaRuntime`, lifecycle/disposal orchestration, distribution, cost/trend/ETA, live Codex files, endpoint, stash action, staging, commit, review, push, or PR work was performed. The historical combined-Unit-3 progress remains non-authoritative; this slice completes only 3A.

### Cumulative task state

- [x] 1.1 Consent/quota
- [x] 1.2 Consent/quota refresh and failure behavior
- [x] 2.1 Shared presentation
- [x] P.1 Planning Amendment
- [x] 3A.1 Analytics-core RED tests
- [x] 3A.2 Analytics-core GREEN implementation
- [x] 3B.1 Production lifecycle RED tests
- [x] 3B.2 Production lifecycle GREEN implementation
- [x] 4.1 Distribution RED tests
- [x] 4.2 Distribution GREEN and smoke evidence

## Unit 3B — Production Lifecycle and Presentation

**Mode:** Standard behavior-first (`strict_tdd: false`)

### Completed tasks

- [x] 3B.1 Added production-composite cooperative and cancellation-insensitive exit-path tests through `CreateComposition` → `TrayHostRuntime.ExitAsync` → `QuotaRuntimeResource`.
- [x] 3B.2 Added the exclusive `AnalyticsLifecycleOwner`, generation-gated publication, one bounded await, typed first outcome, dependent-resource retention on timeout, and local-data presentation composition.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| Behavior-first RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~AnalyticsLifecycle" --no-restore /m:1` failed before production implementation because the restored candidate referenced missing `ILocalAnalyticsLifecycle`; the production-composite tests were already present. |
| Focused test and runtime harness | The same command passed: 2/2, 0 failed (55 ms). The tests use only temporary synthetic `sessions` JSONL and isolated SQLite data, invoke the actual composition and exit path, prove cooperative one-cancel/one-await/disposal, and prove bounded cancellation-insensitive timeout retains analytics view/store, disposes the independent quota store, suppresses late publication, never re-awaits, and drains the released scan. No live Codex files or endpoint were accessed. |
| Regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics|FullyQualifiedName~BetaPresentation|FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed: 16/16, 0 failed (248 ms). |
| Builds | `dotnet build src/AIBar.Application/AIBar.Application.csproj --no-restore /m:1` — 0 warnings, 0 errors (1.01 s). `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — 0 warnings, 0 errors (1.40 s). |
| Diff check | `git diff --check` — passed with no whitespace errors. |
| Rollback boundary | Delete `LocalCodexAnalyticsView.cs` and `AnalyticsLifecycleTests.cs`; restore the named Unit-3B hunks in `BetaRuntime.cs`, `App.xaml.cs`, and `MainWindow.xaml`, then remove these two task checks and this section. Unit 3A remains intact. |

### Scope and deviations

None. Restored only the corrected Unit-3B candidate paths from preserved `stash@{0}` object `3ff43d33cb16573f5d750767269629771867aec5`; the new lifecycle tests were authored because the stash contained no such test file. No Unit 4, live endpoint, Codex source, stash mutation, staging, commit, review, push, or PR action was performed.

### Corrective rerun — revoke then re-enable

| Evidence | Result |
|---|---|
| Behavior-first RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Cooperative_stop_then_reenable_starts_exactly_one_fresh_scan_generation" --no-restore /m:1` — failed: 0/1 passed, timeout waiting for the second scan generation. `StopAsync` left `_scan` and `_outcome` set, so the later `StartAsync` returned without starting a scan. |
| GREEN | The same command — passed: 1/1, 0 failed. The test cooperatively stops and drains generation one, starts generation two exactly once despite a duplicate start call, then stops and drains it during cleanup. |
| Lifecycle suite | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~AnalyticsLifecycle" --no-restore /m:1` — passed: 3/3, 0 failed (569 ms). |
| Bounded regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics|FullyQualifiedName~BetaPresentation|FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed: 16/16, 0 failed (245 ms). |
| Builds | `dotnet build src/AIBar.Application/AIBar.Application.csproj --no-restore /m:1` and `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — passed, 0 warnings and 0 errors. |
| Rollback boundary | Revert `ResetStoppedGeneration` in `LocalCodexAnalyticsView.cs`, the re-enable test/helpers in `AnalyticsLifecycleTests.cs`, and this corrective evidence. The prior Unit 3B lifecycle behavior and all Unit 3A files remain untouched. |

The smallest correction resets `_scan`, `_outcome`, and the completed generation's CTS only after a cooperative non-disposing stop. It does not alter timeout/failure retention, one bounded await per generation, generation-gated late-publication suppression, typed exit outcome/first failure, or process-exit disposal.

## Unit 4 — Private Beta Distribution

**Mode:** Standard behavior-first (`strict_tdd: false`)

### Completed task

- [x] 4.1 Added synthetic-repository RED coverage for canonical-repository and final-3B-parent gates, staged, unstaged, and empty-index sources, deterministic ZIP/provenance, launch, early-exit, timeout, and cleanup behavior.

### Completed final task

- [x] 4.2 Published and smoke-validated the deterministic private-beta ZIP from committed Unit-4 source `058f5bd2fce56c80307af3dafcb494a0c72d8e2f`; the retained artifact is eligible only for manual private-beta distribution.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| Behavior-first RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PrivateBetaDistribution" --no-restore /m:1` — failed 3/3 before the publisher existed: expected `BETA_SOURCE_UNCOMMITTED` and `BETA_SMOKE_EARLY_EXIT` were absent, and committed-source packaging could not succeed. |
| Focused GREEN and deterministic provenance | The same command — passed 3/3. Synthetic local Git repositories prove canonical-root, dirty/staged/empty-index, and parent-mismatch rejection; fixed committed source binding; sorted inventory; ZIP SHA-256 binding; byte-identical repeat ZIPs; extracted self-contained launch; early-exit rejection; publish timeout; and incomplete-output removal. |
| Bounded regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics|FullyQualifiedName~AnalyticsLifecycle|FullyQualifiedName~BetaPresentation|FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed 19/19. |
| Build | `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — passed with 0 warnings and 0 errors. |
| Final-package preflight | The actual command failed safely with `BETA_SOURCE_UNCOMMITTED`, exit 1, and `OUTPUT_EXISTS=False`; no ZIP or publish directory was created from uncommitted source. |
| Runtime harness | Synthetic Windows x64 clean-VM-equivalent harness uses only temporary Git repositories, fake local publishers, and copied Windows executables. It proves extracted launch and forced process cleanup without .NET, Codex files, credentials, or endpoints. The required final artifact smoke remains commit-gated. |
| Rollback boundary | Revert `Publish-Deterministic.ps1`, `AIBar.Desktop.csproj` version, `docs/private-beta.md`, `PrivateBetaDistributionTests.cs`, and these Unit-4 task/progress entries; Units 1–3B remain intact. |

### Exact continuation

Commit only the six Unit-4 authored paths, then run `pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "$env:TEMP\aibar-beta-unit4-$([guid]::NewGuid().ToString('N'))" -SourceDateEpoch "1767225600"` from that clean final Unit-4 commit. Confirm its parent is `6e2d8a46d455819c6f30a07a34bf63c9594d5c4c`, smoke-test the produced Windows x64 ZIP without .NET, record the generated manifest/instructions as distribution evidence, and only then mark 4.2 complete.

### Final package and smoke evidence

| Evidence | Result |
|---|---|
| Immutable source gate | Branch `feature/aibar-beta-unit-4`; clean `HEAD` and exact final source `058f5bd2fce56c80307af3dafcb494a0c72d8e2f`; exact parent `6e2d8a46d455819c6f30a07a34bf63c9594d5c4c`; baseline ancestor `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`; `git merge-base --is-ancestor` exited 0. `stash@{0}` remained `3ff43d33cb16573f5d750767269629771867aec5`. |
| Real deterministic package | Pre-approved parent `C:\Users\mjsal\AppData\Local\Temp\opencode` existed. From the clean commit, `pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "C:\Users\mjsal\AppData\Local\Temp\opencode\aibar-beta-unit4-f3f465ed17054b74a388d2d0a29531a0" -SourceDateEpoch "1767225600"` exited 0 using a fresh nonexistent output leaf. |
| Retained artifact and provenance | `C:\Users\mjsal\AppData\Local\Temp\opencode\aibar-beta-unit4-f3f465ed17054b74a388d2d0a29531a0\AIBar-win-x64-private-beta.zip`; version `0.1.0-beta.1`; 471 inventory entries sorted ordinally by path, each matching recorded length and SHA-256; ZIP SHA-256 `b894cd3f665c9a16d74d811ad5c5cdf3ca3bed5ef952d77d64effd543f5946fa`. The manifest's source, baseline, version, inventory, and ZIP hash all matched the produced archive bytes. |
| Distribution sidecars | `private-beta-manifest.json` and `private-beta-instructions.txt` were retained beside the ZIP. Instructions bind the same version/source/baseline/hash and state unsigned private beta, manual replacement, and no updater or uninstall. |
| Windows x64 self-contained smoke | The publisher's extracted smoke launch survived its startup interval and cleaned its `smoke` directory. An independent extraction launched `AIBar.Desktop.exe` for 1000 ms with `DOTNET_ROOT` set to an empty directory, `DOTNET_MULTILEVEL_LOOKUP=0`, and a system-only `PATH`; it remained running, was tree-killed, and exited `-1` as expected. |
| Cleanup and residual process check | Publisher smoke, independent extraction, and temporary empty DOTNET root were removed. The package smoke's pre/post snapshot found 0 new `AIBar.Desktop`, `dotnet`, or `testhost` processes. Focused test/build compilation left `dotnet exec ... VBCSCompiler.dll` PID 21552; it was tree-killed after verification, and the final snapshot found 0 residual `AIBar.Desktop`, `dotnet`, or `testhost` processes. |
| Focused suite | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PrivateBetaDistribution" --no-restore /m:1` — passed 3/3, 0 failed. |
| Bounded regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics|FullyQualifiedName~AnalyticsLifecycle|FullyQualifiedName~BetaPresentation|FullyQualifiedName~BetaConsentOrQuota" --no-restore /m:1` — passed 19/19, 0 failed. |
| Desktop build | `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1` — passed with 0 warnings and 0 errors. |
| Final repository check | `git status --porcelain=v1` was clean before package and before this documentation update; `git diff --check` exited 0. No generated artifact was copied into or tracked by the repository. |
| Rollback boundary | Revert only this final 4.2 documentation evidence and task checkbox plus the committed Unit-4 paths `scripts/Publish-Deterministic.ps1`, `src/AIBar.Desktop/AIBar.Desktop.csproj`, `docs/private-beta.md`, and `tests/AIBar.Domain.Tests/PrivateBetaDistributionTests.cs`; delete the retained external output directory manually. Units 1–3B remain intact. |

**Cumulative final state:** 10/10 implementation tasks complete. The retained ZIP remains unsigned, manual private-beta distribution only; no installer, updater, signing, automatic update/uninstall, or public-release claim was made.

## Final verification remediation — bounded unmanaged correction

**Binding:** `aibar-beta-final-requirements-runtime-verification`; remediates evidence revision `sha256:1de650c383162fd95e178d1d65a7260912e5e18904bfef62e4fb7e14040ebb70`. This corrective candidate changes no completed task checkbox and does not replace `verify-report.md`.

### Findings and evidence

- A-05: Added a production-composition test that observes `LocalCodexAnalyticsView` loading, then partial-result publication with factual totals, coverage, safe warning, and scan time. `dotnet test ... --filter "FullyQualifiedName~Production_view_publishes_local_totals_partial_coverage_warning_and_scan_time_after_loading" --no-restore /m:1` — passed 1/1.
- A-07: Added a production-composition test that invokes the composed `BetaRuntime` revocation command during the active scan, observes one cancellation and one await, and rejects promotion to complete. `dotnet test ... --filter "FullyQualifiedName~BetaRuntime_revocation_stops_the_active_production_analytics_scan_without_promoting_a_result" --no-restore /m:1` — passed 1/1.
- B2 relocation: Updated only stale expected hashes for four committed Core source files after recomputing their current committed bytes; the isolated authority test passed 1/1.
- Visual automation: Updated the card expectation to include the approved `Local Codex data` card; the isolated WPF automation test passed 1/1.
- Graph fixture: Preserved canonical fixture bytes with `-text` attributes so checkout conversion cannot invalidate their authoritative inventory/recovery hashes; graph projection passed 1/1, including all fault mutations.
- Private-beta early exit: Classified the isolated test as scheduling-sensitive under aggregate suite load, then made its synthetic early-exit probe wait five seconds while leaving the actual one-second product smoke threshold unchanged; the isolated test and aggregate suites passed.
- Saturated recovery: Reproduced as non-failing on this candidate; no production or harness change was made. The isolated test passed 1/1.

### Work Unit Evidence

| Evidence | Result |
|---|---|
| Focused beta suite | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota|FullyQualifiedName~QuotaPresentationHostTests|FullyQualifiedName~QuotaPresentationTests|FullyQualifiedName~BetaPresentation|FullyQualifiedName~BetaAnalytics|FullyQualifiedName~AnalyticsLifecycle|FullyQualifiedName~PrivateBetaDistribution" --no-restore /m:1 --logger "console;verbosity=normal"` — passed 37/37. |
| Full regression | `dotnet test AIBar.sln --no-restore /m:1 --logger "console;verbosity=normal"` — passed 284/284, exit 0, 6.7151 minutes. |
| Builds | `dotnet build src/AIBar.Application/AIBar.Application.csproj --no-restore /m:1 --verbosity normal` and `dotnet build src/AIBar.Desktop/AIBar.Desktop.csproj --no-restore /m:1 --verbosity normal` — both passed with 0 warnings and 0 errors. |
| Runtime boundary | The two added tests use `App.CreateComposition`, actual `LocalCodexAnalyticsView`, and the composed `BetaRuntime` revocation path against synthetic temporary JSONL/SQLite only; no Codex data, credentials, or endpoint was accessed. |
| Package provenance | No shipped implementation or publisher bytes remain changed; retained ZIP `b894cd3f665c9a16d74d811ad5c5cdf3ca3bed5ef952d77d64effd543f5946fa` remains bound to committed source `058f5bd2fce56c80307af3dafcb494a0c72d8e2f`. This is an uncommitted test/OpenSpec remediation candidate; no package was regenerated. |
| Rollback boundary | Revert only `.gitattributes` and the five changed test files plus this section; Units 1–4 source behavior and the retained ZIP remain intact. |
