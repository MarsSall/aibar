# Design: AIBar Private Beta

## Technical Approach

Split Unit 3 into a deterministic analytics core (3A) and production lifecycle/presentation integration (3B). A documentation-only Planning Amendment follows completed Unit 2 and becomes 3A's exact parent. Unit 4 publishes the committed 3B successor; `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97` remains ancestry baseline only.

## Architecture Decisions

| Decision | Choice | Tradeoff / rationale |
|---|---|---|
| Core boundary | 3A owns discovery, access/mutation classification, scanning, checkpoints, aggregation, immutable factual results, coverage/warnings, and core tests | Keeps WPF and shutdown out of deterministic logic |
| Lifecycle | 3B introduces one `AnalyticsLifecycleOwner` | Central ownership is more explicit but prevents competing cancellation, awaits, and disposal |
| Delivery | Feature Branch Chain, strict 400 changed-line limit | Preserve five implementation slices and insert one independently revertible planning review because the measured planning delta (257 changed lines) plus 3A (298) exceeds 400 |
| Distribution | Build from exact final Unit 4 commit after 3B | Baseline SHA remains provenance, never source bytes |

## Data and Lifecycle Flow

```text
Codex files -> scanner/core -> analytics store -> immutable result
                       production CreateComposition
BetaRuntime trigger -> AnalyticsLifecycleOwner -> generation gate -> WPF
TrayHostRuntime.ExitAsync -> QuotaRuntimeResource -> owner shutdown
```

The owner creates one scan CTS/task generation. Shutdown closes publication first, cancels once, and performs one bounded await. `AnalyticsShutdownOutcome` records `Completed`, `TimedOut`, or `Failed`, a safe code, and the first failure. Cooperative completion disposes the view then analytics store. Timeout/failure never re-awaits the scan or disposes those dependent resources; repeated shutdown returns the recorded outcome. Generation checks suppress late publication, retained process-owned resources remain until process exit, and independent quota/lifecycle resources continue reverse disposal. Later failures are collected without replacing the typed first outcome.

## Exact Unit 3 Inventory and Forecast

Forecasts count authored additions plus deletions.

| Unit | File | Action / owned seam | Forecast |
|---|---|---|---:|
| 3A | `src/AIBar.Application/LocalCodexAnalytics.cs` | Create core contracts/adapter/result classification | 100 |
| 3A | `src/AIBar.Application/SessionJsonlScanner.cs` | Modify only injectable stream-open/pre-stability seams and unreadable/mutation classification | 18 |
| 3A | `tests/AIBar.Domain.Tests/BetaAnalyticsTests.cs` | Create complete/empty/partial/unavailable/mutation/access/factual-result tests | 180 |
|  | **3A total** |  | **298** |
| 3B | `src/AIBar.Application/BetaRuntime.cs` | Modify analytics start/revoke delegation only; no scan-task ownership | 28 |
| 3B | `src/AIBar.Desktop/LocalCodexAnalyticsView.cs` | Create view, combined presentation, owner, typed outcome | 150 |
| 3B | `src/AIBar.Desktop/App.xaml.cs` | Modify only `CreateComposition` analytics construction/test seams and `QuotaRuntimeResource.DisposeAsync` ownership/order | 60 |
| 3B | `src/AIBar.Desktop/MainWindow.xaml` | Add Local Codex binding group only | 8 |
| 3B | `tests/AIBar.Domain.Tests/AnalyticsLifecycleTests.cs` | Create production-composite/application-exit lifecycle tests | 150 |
|  | **3B total** |  | **396** |

No file is shared between 3A and 3B. Reverting 3A deletes its created files and restores the named scanner seams. Reverting 3B deletes its created files, restores BetaRuntime delegation, removes the named `CreateComposition`/`QuotaRuntimeResource` hunks and XAML group; 3A remains intact.

## Production-Path Testing Contract

Make the existing `CreateComposition` internally callable with optional `CompositionSeams`; production calls it with defaults. Seams may set an isolated data root, deterministic shutdown bound, scanner stream/pre-stability gate, and no-op lifecycle observations. They MUST NOT replace `AnalyticsLifecycleOwner`, `LocalCodexAnalyticsView`, `BetaRuntime`, or `QuotaRuntimeResource`.

Tests call actual `CreateComposition(seams)`, start the scan, and invoke actual `TrayHostRuntime.ExitAsync`, which disposes the returned `QuotaRuntimeResource`. A cooperative gate proves one cancel/await, late-publication suppression, and disposal of every resource. A cancellation-ignoring gate proves bounded return, no re-await, retained view/store, independent reverse disposal, typed first outcome, and no publication after release. Direct owner/resource surrogates do not satisfy coverage.

## Threat Matrix

| Boundary | Applicability | Threat, mitigation, evidence |
|---|---|---|
| Documentation-like paths | N/A | No executable-file classification; `requirements.txt`, `CMakeLists.txt`, Markdown/MDX, and `README.sh` are never inferred as commands |
| Git repository selection | Applicable | Wrong `git -C`/relative/absolute cwd: canonical repository root and exact SHA are authoritative; reject mismatch before output; RED selector tests |
| Commit state | Applicable | Staged, `commit -a`, or empty index cannot alter archived committed bytes; reject missing/uncommitted Unit 4 source; RED state tests |
| Push state | N/A | No push or remote destination boundary |
| PR commands | N/A | No `gh`/PR command composition or `--head` ownership |
| Application process exit | Applicable | Non-cooperative scan could outlive cleanup; exclusive bounded owner retains dependents and suppresses publication; production-path RED tests above |

## Delivery, Distribution, and Rollback

Chain: Unit 2 `fab8ca0b5f588319926141a3cad276317319de99` (377 lines) → Planning Amendment (measured 257 changed lines, forecast ≤300, hard ≤400) → 3A (≤298) → 3B (≤396) → Unit 4 (≤400). The future Planning Amendment commit is 3A's exact parent. Implementation commits contain their tests; every child targets its immediate parent and is independently reversible, while polluted child diffs require rebase/retarget.

The Planning Amendment owns only corrected `proposal.md`, delta specs, `design.md`, `tasks.md`, and the truthful `apply-progress.md` replan. It contains no product code or tests; excluded Unit 3 product/test work remains preserved in the named selective stash `aibar-beta-unit3-product-split` for later branch restoration. Reverting it restores the Unit 2 planning state without reverting Unit 2 product behavior; reverting any implementation slice removes only that slice.

Unit 4 archives its own exact final commit after 3B, whose ancestry includes the Planning Amendment and all five implementation slices, then publishes the self-contained ZIP and records in manifest/instructions: baseline SHA, final Unit 4 source SHA, version, complete sorted path/length/file-SHA-256 inventory, and exact ZIP SHA-256. Missing/mismatched provenance emits no eligible artifact. No migration is required; packaging authority, installer/signing/update, SBOM/compliance, and public/store distribution remain deferred.
