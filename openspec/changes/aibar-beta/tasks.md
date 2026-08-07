# Tasks: AIBar Private Beta

## Review Workload Forecast

Estimated changed lines: 760–940 total; Unit 1 220–260, Unit 2 170–220, Unit 3 190–240, Unit 4 180–220. Review budget: 1000 authored lines; four bounded PRs.

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High
Delivery strategy: auto-chain
Dependency diagram: `feature/aibar-beta` tracker ← PR #1 📍 ← PR #2 ← PR #3 ← PR #4; every PR targets its immediate parent branch.

### Suggested Work Units

| Unit / PR target | Focused verification | Runtime evidence; rollback boundary |
|---|---|---|
| 1. Consent, credentials, quota; PR #1 → tracker; parent `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97` | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaConsentOrQuota"` | Enabled/disabled/offline/revocation harness; revert only Unit-1 files and tests: `ConsentSettings.cs`, `CredentialBoundary.cs`, `BetaRuntime.cs`, `App.xaml.cs`; 220–260 lines. |
| 2. Shared presentation; PR #2 → PR #1; | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaPresentation"` | Clock-advance tray/popup harness; revert Unit-2 files/tests and presentation hunks only: `QuotaPresentation.cs`, `HostRuntime.cs`, `WindowsLifecycleEvents.cs`, `MainWindow.xaml`; 170–220 lines. |
| 3. Local analytics; PR #3 → PR #2 | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~BetaAnalytics"` | Synthetic local-source/cancellation harness; revert Unit-3-owned adapter/scanner/store/query files, tests, and named analytics registration/binding hunks only—not whole Unit-1 `BetaRuntime.cs` or Unit-2 `MainWindow.xaml`; 190–240 lines. |
| 4. ZIP/evidence; PR #4 → PR #3 | `pwsh -NoProfile -File tests/AIBar.Domain.Tests/Invoke-PrivateBetaLaunchSmoke.ps1 -ZipPath "$output/AIBar-win-x64-recovery.zip" -InventoryPath "$output/recovery-inventory.json" -ManifestPath "$output/artifact-manifest.json" -ExpectedParent $parent -ExpectedVersion $version` | Harness extracts ZIP to a fresh directory, starts extracted `AIBar.exe` with no arguments, captures only its PID, fails on early exit/timeout, terminates only that PID, and removes only its directory; revert `Publish-Deterministic.ps1`, `docs/private-beta.md`, harness/tests; 180–220 lines. |

## Phase 1: Consent, Credentials, and Quota

- [x] 1.1 RED→GREEN tests and implementation for default-off disclosure/acceptance, consent-only persistence, secret-free available/missing/unusable/disabled status, revocation cancellation, late-result rejection, and cancel/await/reverse-dispose exit.
- [x] 1.2 RED→GREEN tests and implementation for live five-hour/weekly quota, loading, cached offline/missing-credential fallback, **cached age/freshness presentation**, no-snapshot unavailable, safe-redacted errors, coalesced manual/popup/poll/resume/clock triggers.

## Phase 2: Shared Presentation

- [ ] 2.1 RED→GREEN immutable `BetaPresentationState`, shared tray/popup values and freshness, reset countdown remapping, disclosure/loading bindings, lifecycle disposal, and safe warnings.

## Phase 3: Local Analytics

- [ ] 3.1 RED→GREEN tests first for complete/empty/partial/unavailable/loading/failed scans, factual token/model totals, scan time, warnings, **explicit local-data/source labeling**, atomic publication, skipped/unreadable/mutating-source coverage, and disable/exit abandonment; add a Unit-3-owned adapter/view boundary and only its named integration hunks.

## Phase 4: Distribution and Evidence

- [ ] 4.1 RED tests only for missing parent/archive failure and extracted launch success/early-exit/timeout/cleanup; packaging reads the fixed commit directly, independent of index, staging, worktree, branch ancestry, or receipt authority, and the executable keeps unchanged no-argument behavior.
- [ ] 4.2 GREEN: `$parent='cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97'; $version='0.1.0-beta.1'; $run=Join-Path ([IO.Path]::GetTempPath()) "aibar-beta-$([guid]::NewGuid())"; $source=New-Item -ItemType Directory (Join-Path $run source); $output=Join-Path $run output; git archive --format=tar --output (Join-Path $run source.tar) $parent; tar -xf (Join-Path $run source.tar) -C $source; pwsh -NoProfile -File "$source/scripts/Publish-Deterministic.ps1" -OutputDirectory $output -SourceDateEpoch 1767225600 -ParentCommit $parent -Version $version`; run harness and verify unsigned/private-beta Windows x64 self-contained ZIP, full parent SHA, version, sorted inventory, ZIP SHA-256, warning, launch, checksum, manual replacement guidance, and smoke-gated distribution.

Suite: `dotnet test AIBar.sln`; Unit 4 also runs the no-.NET Windows x64 harness.

Exclusions: packaging authority/`aibar-foundation`, MSIX/installer, signing, automatic update/uninstall, SBOM/compliance, public release/store distribution, cost/trend/ETA/forecasting polish.
