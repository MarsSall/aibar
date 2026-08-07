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

### Remaining tasks

- [ ] 2.1 Shared presentation
- [ ] 3.1 Local analytics
- [ ] 4.1 Distribution RED tests
- [ ] 4.2 Distribution GREEN and smoke evidence
