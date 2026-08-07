# Design: AIBar Private Beta

## Technical Approach

Compose the existing .NET 8/WPF services into one private-beta runtime. Preserve exact parent `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`, shared tray/popup state, explicit consent, analytics loading, and a manually distributed unsigned Windows x64 ZIP. Packaging authority remains unauthorized.

## Architecture Decisions

| Decision | Choice | Alternative / tradeoff | Rationale |
|---|---|---|---|
| Consent | Per-user atomic `settings.json` stores only schema and `privateCodexConsent`; adjacent disclosure and acceptance precede enablement | Registry duplication, implicit enable, stored credentials | Default-off, informed, revocable, secret-free consent |
| Runtime | `App` owns one `BetaRuntime` graph and `BetaPresentationHost` | A second shell duplicates services | Deterministic initialization and reverse disposal |
| State and time | One immutable `BetaPresentationState`; reset instants are remapped through `IClock` | Independent view models and refresh-only strings drift | Tray and popup remain consistent and countdowns remain current |
| Distribution | Reuse deterministic self-contained publishing for one ZIP; verification is external to product behavior | MSIX, installer, signing, or a product smoke CLI expands scope | Meets private manual distribution without new packaging authority |

## Data and Lifecycle Flow

```text
settings -> consent policy -> credential reader -> quota provider/coordinator -> quota.db
Codex sessions -> discovery -> scanner -> analytics store/query -> coverage
                                      \-> BetaPresentationHost -> tray + popup
exact parent -> deterministic win-x64 publish -> ZIP + inventory + manifest -> test-owned launch harness
```

Startup loads consent and cached quota. Acceptance persists before credential lookup; revoke persists false, cancels work, and rejects late results. Popup-open, manual refresh, five-minute poll, resume, and clock-change share the coordinator. Exit cancels and awaits work before reverse disposal. External failures use safe codes and `SafeRedactor`.

## File Changes

| File | Action | Purpose |
|---|---|---|
| `src/AIBar.Application/ConsentSettings.cs` | Create | Consent store |
| `src/AIBar.Application/CredentialBoundary.cs`, `BetaRuntime.cs` | Modify/Create | Credentials, quota, analytics, cancellation |
| `src/AIBar.Desktop/App.xaml.cs`, `QuotaPresentation.cs`, `HostRuntime.cs`, `MainWindow.xaml` | Modify | Composition and shared presentation |
| `src/AIBar.Desktop/WindowsLifecycleEvents.cs` | Create | Lifecycle events |
| `scripts/Publish-Deterministic.ps1`, `docs/private-beta.md` | Modify/Create | ZIP evidence and guidance |
| `tests/AIBar.Domain.Tests/*Beta*Tests.cs` | Create | Vertical-path evidence |
| `tests/AIBar.Domain.Tests/Invoke-PrivateBetaLaunchSmoke.ps1` | Create | External launch harness |

## Contracts and Testing

`CredentialAvailability = Disabled | Available | Missing | Unusable`; `AnalyticsCoverage = Complete | Partial | Unavailable`; `AnalyticsScanStatus = Idle | Loading | Complete | Failed`. Loading is explicit. Success atomically publishes totals, coverage, warning, and scan time; failure is unavailable with a safe warning. Empty records are unavailable; skipped, unreadable, or mutating sources are partial.

xUnit fakes cover consent disclosure/acceptance, quota triggers, shared state, countdowns, redaction, analytics transitions, coverage, disposal, and late-result rejection. WPF STA tests cover disclosure and loading bindings.

Distribution tasks own this deterministic release procedure from the repository root:

```powershell
$parent = 'cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97'
$version = '0.1.0-beta.1'
$run = Join-Path ([IO.Path]::GetTempPath()) "aibar-beta-$([guid]::NewGuid())"
$source = New-Item -ItemType Directory -Path (Join-Path $run source)
$output = Join-Path $run output
git archive --format=tar --output (Join-Path $run source.tar) $parent
tar -xf (Join-Path $run source.tar) -C $source
pwsh -NoProfile -File "$source/scripts/Publish-Deterministic.ps1" -OutputDirectory $output -SourceDateEpoch 1767225600 -ParentCommit $parent -Version $version
pwsh -NoProfile -File tests/AIBar.Domain.Tests/Invoke-PrivateBetaLaunchSmoke.ps1 -ZipPath "$output/AIBar-win-x64-recovery.zip" -InventoryPath "$output/recovery-inventory.json" -ManifestPath "$output/artifact-manifest.json" -ExpectedParent $parent -ExpectedVersion $version
```

The publisher records the full parent SHA and version, then emits the ZIP, sorted path/length/SHA-256 inventory, and ZIP SHA-256 manifest. The test-owned harness verifies all evidence, extracts to its fresh directory, starts extracted `AIBar.exe` with existing no-argument behavior, fails on early exit, then terminates only its PID and removes its directory. Final smoke runs on clean Windows x64 without .NET; product command-line behavior remains unchanged.

## Threat Matrix

| Boundary / adversarial cases | Applicability | Safe/failure behavior | Planned RED tests |
|---|---|---|---|
| Documentation-like executable paths | N/A: no executable-file classification | — | — |
| Git repository selection | Applicable: fixed parent materialization | Run from the intended repository; unknown parent or archive failure produces no eligible ZIP | Missing-parent and archive-failure command tests |
| Commit state | N/A: archive reads the fixed commit directly; index and worktree state are irrelevant | — | — |
| Push state | N/A: no push boundary | — | — |
| PR commands | N/A: no PR automation | — | — |
| Extracted executable launch | Applicable: external process boundary | Launch only the extracted `AIBar.exe`; timeout/early exit fails and cleanup targets only the captured process/directory | Successful persistence, early-exit, timeout, and cleanup tests |

## Delivery and Deferrals

Auto-chain four independently revertible slices, each under 1000 authored lines: **1** consent/credentials/runtime/quota; **2** tray/WPF state and countdown; **3** analytics and coverage; **4** ZIP evidence, guidance, and external smoke harness. Each owns tests, focused command, runtime evidence, files, and rollback boundary.

No migration is required; absent or invalid consent fails closed. Excluded: packaging authority and `aibar-foundation` packaging completion; MSIX/installer; signing; automatic update/uninstall; SBOM/compliance; public-release/store distribution; and cost/trend/ETA/forecasting polish.
