# Tasks: AIBar Private Beta

## Review Forecast

Forecast: U2 377; Planning Amendment <=300; hard stop 400; 3A 298; 3B 396; U4 <=400; U1 exception 1,221.

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High
Delivery strategy: auto-chain
Dependency: tracker ← Unit 1 📍 ← Unit 2 ← Planning Amendment ← 3A ← 3B ← Unit 4; parents.

| Unit/parent | Check | Runtime | Rollback |
|---|---|---|---|
| U1→tracker; `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97` | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota|FullyQualifiedName~QuotaPresentationHostTests" --no-restore /m:1` | In-process/no endpoint. | Revert `f2d72b63dd5a50f67f94997d1fbe8f4da39e0cba`: planning `openspec/changes/aibar-beta/{apply-progress.md,design.md,exploration.md,proposal.md,tasks.md,specs/local-usage-analytics/spec.md,specs/private-beta-distribution/spec.md,specs/private-codex-consent/spec.md,specs/quota-status-experience/spec.md}`; code `src/AIBar.Application/{BetaRuntime.cs,ConsentSettings.cs,CredentialBoundary.cs,QuotaHttpProvider.cs,StartupSettings.cs}`, `src/AIBar.Desktop/{App.xaml.cs,HostRuntime.cs,QuotaPresentation.cs}`; tests `tests/AIBar.Domain.Tests/{BetaConsentOrQuotaTests.cs,QuotaPresentationHostTests.cs}`. |
| U2→U1; `f2d72b63dd5a50f67f94997d1fbe8f4da39e0cba` | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaPresentation" --no-restore /m:1` | `IClock`/shared state. | Revert `fab8ca0b5f588319926141a3cad276317319de99`: planning `openspec/changes/aibar-beta/{apply-progress.md,tasks.md}`; code `src/AIBar.Desktop/{App.xaml.cs,HostRuntime.cs,MainWindow.xaml,QuotaPresentation.cs,WindowsLifecycleEvents.cs}`; tests `tests/AIBar.Domain.Tests/{BetaPresentationTests.cs,HostRuntimeTests.cs}`. |
| Planning→U2; future commit | `git diff --check` plus structural artifact validation. | N/A docs-only; no product code/tests. | Revert exact future Planning Amendment commit; restore U2 planning, not product behavior. |
| 3A→Planning; future Planning Amendment commit | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics" --no-restore /m:1` | Synthetic JSONL/SQLite. | Delete `src/AIBar.Application/LocalCodexAnalytics.cs`, `tests/AIBar.Domain.Tests/BetaAnalyticsTests.cs`; restore scanner seams; no WPF/lifecycle/3B. |
| 3B→3A | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~AnalyticsLifecycle" --no-restore /m:1` | Production `CompositionSeams`→`CreateComposition`→`TrayHostRuntime.ExitAsync`→`QuotaRuntimeResource`. | Delete `src/AIBar.Desktop/LocalCodexAnalyticsView.cs`, `tests/AIBar.Domain.Tests/AnalyticsLifecycleTests.cs`; restore named `src/AIBar.Application/BetaRuntime.cs`/`src/AIBar.Desktop/App.xaml.cs` seams and `src/AIBar.Desktop/MainWindow.xaml` group; preserve 3A. |
| U4→3B | From repo root: `pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -OutputDirectory "$env:TEMP\aibar-beta-unit4-$([guid]::NewGuid().ToString('N'))" -SourceDateEpoch "1767225600" -PublishCommand "dotnet"` | Windows x64/no .NET: warning/launch/replacement/version/hash/cleanup. | Revert `scripts/Publish-Deterministic.ps1`, `docs/private-beta.md`, `tests/AIBar.Domain.Tests/PrivateBetaDistributionTests.cs`; restore exact 3B parent and Units 1–3B. |

## 1 Consent/Quota

- [x] 1.1 RED→GREEN default-off disclosed, per-user consent-only persistence; revocation cancellation/late rejection; `Available`, `Missing`, `Unusable`, `Disabled`; lookup only when enabled; secrets never persisted, displayed, logged, or returned.
- [x] 1.2 RED→GREEN five-hour/weekly quota, loading, cached age/freshness, missing/offline fallback, no-snapshot unavailable, safe-redacted errors; no unauthorized trigger-coalescing.

## 2 Presentation

- [x] 2.1 RED→GREEN immutable tray/popup state, popup reset-countdown remapping, disclosure/loading bindings, distinct redacted missing-credential/offline warnings, lifecycle disposal.

## Planning Amendment — docs-only; <=300; hard stop 400

- [x] P.1 Only corrected `proposal.md`, `specs/local-usage-analytics/spec.md`, `specs/private-beta-distribution/spec.md`, `design.md`, `tasks.md`, truthful `apply-progress.md` replan; no product code/tests. Check: `git diff --check` plus structural artifact validation. Rollback: exact future Planning Amendment commit.

## 3A — hard <=400 (298)

- [x] 3A.1 RED behavior-first tests: complete/empty/partial/unavailable coverage; factual-local token/model totals, scan time, warnings/source labels, atomic results, unreadable/mutating sources, safe abandonment.
- [x] 3A.2 GREEN only approved `LocalCodexAnalytics.cs`, `BetaAnalyticsTests.cs`, named injectable `SessionJsonlScanner.cs` seams; no WPF/lifecycle/disposal.

## 3B — hard <=400 (396)

- [x] 3B.1 RED production composition/exit cooperative/non-cooperative tests.
- [x] 3B.2 GREEN exclusive owner/typed outcome plus approved `BetaRuntime.cs`, `LocalCodexAnalyticsView.cs`, `App.xaml.cs`, `MainWindow.xaml`: cancel/await once, suppress late publication, cooperative disposal, retained timeout resources, first failure, never re-await.

### 3B corrective rerun — revoke then re-enable

- [x] Added behavior-first regression coverage for a cooperative stop followed by re-enable/start: exactly one fresh generation starts, while cancellation and bounded await remain exclusively once per generation.
- [x] Cleared only the completed non-disposing generation state; timeout/failure retention and exit disposal semantics remain unchanged.

## 4 Distribution

- [x] 4.1 RED tests: canonical-repository/final-3B-parent mismatch; staged/uncommitted/empty-index source; launch/early-exit/timeout/cleanup; fixed committed source.
- [ ] 4.2 GREEN exact final Unit-4 source after 3B, not baseline `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`; record baseline/source SHA, version, sorted path/length/SHA-256 inventory, ZIP SHA-256, unsigned private-beta tester launch/replacement/version/checksum instructions; distribute only after Windows x64 self-contained smoke.

Suite: `dotnet test AIBar.sln --no-restore`; no live endpoint, credential mutation, push/remote, PR composition, public-release claim. Five implementation slices: Unit 1, Unit 2, 3A, 3B, Unit 4.

Exclusions: packaging authority/`aibar-foundation`, MSIX/installer, signing, automatic update/uninstall, SBOM/compliance, public/store distribution, cost/trend/ETA/forecasting polish, migration.
