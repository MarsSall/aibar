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
| Structural readback | All six in-scope artifacts were read against the Planning Amendment boundary; Unit 1/2 remain completed, Planning is complete, and 3A/3B/4 remain unchecked. |
| Focused validation | `git diff --check` — passed with no whitespace errors. |
| Runtime harness | N/A: this is documentation-only and changes no product, test, or runtime boundary. No tests, builds, runtime/native attempts, staging, commits, or review were run. |
| Rollback boundary | Revert the exact future Planning Amendment commit affecting only the six planning artifacts; Unit 1/2 product behavior and the later preserved product/test work remain untouched. |

### Scope and preservation

The current diff contains only `proposal.md`, `specs/local-usage-analytics/spec.md`, `specs/private-beta-distribution/spec.md`, `design.md`, `tasks.md`, and this `apply-progress.md`; no code or test path is in scope. Product/test changes are explicitly excluded and preserved, without application or inspection, in selective `stash@{0}` named `aibar-beta-unit3-product-split` for later branch restoration.

### Cumulative task state

- [x] 1.1 Consent/quota
- [x] 1.2 Consent/quota refresh and failure behavior
- [x] 2.1 Shared presentation
- [x] P.1 Planning Amendment
- [ ] 3A.1 Analytics-core RED tests
- [ ] 3A.2 Analytics-core GREEN implementation
- [ ] 3B.1 Production lifecycle RED tests
- [ ] 3B.2 Production lifecycle GREEN implementation
- [ ] 4.1 Distribution RED tests
- [ ] 4.2 Distribution GREEN and smoke evidence
