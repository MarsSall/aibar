# Apply Progress — aibar-visual-yasb

## Parent lifecycle reconciliation

- **Audit evidence:** `sha256:228d3a153595ed22725633079baac6e55f9c3771b818dce231db1dc559ca4d75`.
- **Disposition:** Per-unit bounded independent validation is complete for all eleven below-400 units. RDD is disabled/unmanaged, so this reconciliation adds no review claim. Final integrated confirmation remains pending as an archive-only gate after final `sdd-verify`; it is not an apply or verify dependency.

## Unit 6 — completed distribution cleanup and verification; commit boundary pending

- **Scope and baseline:** Completed tasks `6.1` through `6.5` on branch `feat/aibar-visual-yasb-6` against `39d50b9`. The final candidate scope is exactly `docs/private-beta.md`, `scripts/Publish-Deterministic.ps1`, `tests/AIBar.Domain.Tests/PrivateBetaDistributionTests.cs`, `yasb/tests/read-aibar-quota.Tests.ps1`, `yasb/README.txt`, and `yasb/remove-aibar-quota.ps1`.
- **Correction and guarantees:** The final correction validates the raw JSON `schemaVersion` literal as `1` before Windows PowerShell object conversion can coerce it to `Int32`. It retains exact BOM-less UTF-8, ordinal snapshot equality, raw canonical UTC `generatedAt`, reparse and replacement-race checks, bounded atomic backup cleanup, and rollback behavior; prior failure history remains preserved below rather than being relabeled as passing evidence.
- **Focused evidence:** The corrected final Pester target passed **11/11**, including causal assertions for post-replacement reparse and exact-readback mutation, at `sha256:e815621230a8bf1beacb2eb4ed9f0aa3115db955bd2c3bf2a1c977dabfa629dd`. The unchanged filtered C# `PrivateBetaDistributionTests` target remains **5/5**, exit 0 with no skipped tests, at `sha256:c62f57d03027fb7d1afd56b3cb110604b9d0b76b533e37dfe3ae6145b92c2366`.
- **Gate, integrity, and size:** Fresh phase-contract reverification passed tasks `6.1` through `6.5` with no blockers at `sha256:9d2d92220eaa729a8424bb2260d418ce53f0aac969b63415b88432c47fe91ee3`. The candidate is **270 authored lines** against the **330-line** Unit 6 cap. The parser and `git diff --check` passed; pre/post-validation Git status was identical; no temporary or Git-visible artifact remained; and the preserved backup was untouched.
- **Lifecycle boundary:** Task `6.6` is completed by the authorized local Unit 6 work-unit commit; review, push, PR, publish, and release remain unperformed and unauthorized.

## Unit 4b — completed after final manual remediation

- **Gate history/remediation:** The automatic gate exhausted on the preserved compile ambiguity; final independent manual remediation was VERIFIED. Legacy `Place` behavior remains distinct from `PlaceInContext`; production edge detection covers bottom/top/left/right/default with the full edge×DPI/negative/cursor/primary/narrow/min260/nonfinite/overflow/clamp matrix.
- **Production wiring/surface:** App composition passes live Windows placement and the real current-theme source, and every Show recomputes. `PopupSurface` is opaque-first, enables dark mode only for Dark, suppresses high-contrast/remote/unsupported hints, and isolates dark/corner/backdrop failures independently.
- **Rendered validation:** Actual `App.xaml` plus the `MainWindow` Content Grid was STA measured, arranged, and rendered through `RenderTargetBitmap` without live show. Binding/layout/visibility fixture corrections proved unclipped fixed regions, real secondary overflow/scroll, focus/tab/focus visuals, card/progress/reset/state automation, cached age/warning, stable hierarchy, and nonzero Light/Dark/HighContrast render.
- **Focused evidence:** The exact complete focused selection passed **9/9**, 0 failed, exit 0; evidence `sha256:b8f4917d51ac923dd8171ce431961642bf3874de33a1bb55112e3a7a50513b28`.
- **Integrity/delivery:** Direct LSP and diff-check were clean; the candidate is exactly **352 authored source/test lines** versus `f2716d2`, below 370. The native ledger is complete, independent verification is VERIFIED, and no blockers/processes remain. Only Unit 4b is complete; tasks `4b.1`–`4b.6` include the parent-owned atomic commit boundary. This docs-only closeout made no source/test edit and ran no review, push, or PR.

## Unit 4b — placement/DPI/DWM/render attempt blocked by compile ambiguity

- **Status consumed:** Manual status was produced because the parent prohibited `sdd-status`: authoritative OpenSpec change `aibar-visual-yasb`, repo-local root `C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2`, branch `feat/aibar-visual-yasb-4b`, baseline `f2716d2`, and only that repository as the allowed edit root. Proposal, popup-presentation/windows-theme specs, design, tasks, and prior apply progress were readable. Feature-branch-chain Unit 4b is the assigned PR boundary; strict TDD is inactive. Parent retains runtime token and settlement.
- **Candidate scope:** Added a pure placement context/matrix with taskbar edges, DPI scaling, clamp, cursor/primary fallback, and no live discovery in the calculator; recomputes optional injected placement on every WPF show. Added an opaque-first, capability/remote/high-contrast-gated DWM seam with independently isolated dark/corner/backdrop hints. Added measurement-only popup limits and synthetic fixed-region/secondary-scroll/focus/automation and surface fallback tests. No YASB, network, credential, packaging, WinUI, analytics, staging, commit, review, push, or PR work occurred.
- **Pre-harness evidence:** `git diff --check f2716d2 --` passed (only CRLF conversion notices). Check-only `dotnet format ... whitespace --verify-no-changes --no-restore` exited 0. Static FQN discovery identified exactly four Unit 4b targets: placement, synthetic rendered/focus, DWM fallback, and Unit 3 measurement/theme preservation. Candidate authored delta including the untracked rendered test was **267 lines**, below the 370-line cap.
- **Focused harness (exactly once):** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --no-restore --filter "(FullyQualifiedName=AIBar.Domain.Tests.HostPrimitivesTests.Unit_4b_placement_matrix_is_edge_adjacent_dpi_aware_and_clamped|FullyQualifiedName=AIBar.Domain.Tests.PopupRenderedTests.Unit_4b_rendered_popup_keeps_fixed_regions_visible_and_secondary_scrollable|FullyQualifiedName=AIBar.Domain.Tests.PopupRenderedTests.Unit_4b_surface_hints_are_isolated_and_always_restore_an_opaque_base|FullyQualifiedName=AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4b_measurement_limits_preserve_the_Unit_3_semantic_surface)"` exited **1** during compilation; **0 passed, 0 failed, 0 skipped**. `HostPrimitives.cs(68,16)` reports CS0121: `PopoverPlacement.Place(...)` is ambiguous between `PopoverPlacementInput` and `PopoverPlacementContext`.
- **Post-failure discipline:** No source/test edit or test rerun followed the failure. No `testhost.exe` or `vstest.console.exe` process remained. The index remains unstaged. No Unit 4b checkbox is complete or modified.
- **Path deltas:** `HostPrimitives.cs` +27/-51, `HostRuntime.cs` +16/-7, `MainWindow.xaml` +1/-1, `WindowsTheme.cs` +57/-0, `HostPrimitivesTests.cs` +23/-0, `QuotaVisualDesignTests.cs` +9/-0, plus untracked `PopupRenderedTests.cs` (75 lines). No changed path is outside the assigned Unit 4b set.
- **Remaining tasks:** `- [ ]` 4b.1 through 4b.6 remain unchecked. A parent-authorized bounded correction is required to disambiguate the legacy placement overload before another complete Unit 4b focused harness may run.

## Unit 4a — completed popup hierarchy, accessibility, permanent slots, and tray flow

- **Correction sequence:** Preserved both incomplete attempt records below. Corrected the fixed hierarchy's freshness-binding assertion count, used the corrected exact four-test filter, remediated the phase-contract gate, and corrected mixed XAML namespace handling for qualified automation properties and unqualified attached layout attributes.
- **Popup and accessibility:** The fixed header, permanent ordered 5-hour and 7-day cards, fixed footer, and secondary-only `ScrollViewer` retain placeholders, nullable reset/state/age text, exact names/help/live text, ordered rows and tab behavior, visible focus resources, noninteractive progress, runtime `RequestBringIntoView`, and stable window identity.
- **Tray matrix:** Both, partial, no-cache unavailable, loading, stale cached, disabled, error, future-source, and bounded-length cases retain permanent `5h`/`7d` slots with truthful state/age and no private or detail leakage.
- **Host flow:** The host sends the exact initial `BetaPresentationState`, accepts relevant presentation changes, rejects unrelated `PropertyChanged` notifications, and preserves the existing tray instance.
- **Focused evidence:** The corrected complete target ran once and passed **4/4**, with 0 failed, 0 skipped, and exit 0. Native settlement is complete at evidence revision `sha256:34607e3cd9120f7f8d397f83b7270db6934111ac6b5b7b163b6843a3c3fae009`.
- **Diagnostics, size, and scope:** Direct diagnostics were clean on the changed C# paths, `git diff --check` was clean, and the candidate is exactly **298 authored source/test lines** versus `9e88f4d`, below the 380-line Unit 4a cap. The final fresh phase-contract gate passed, no test process remained, and no placement, DPI, DWM, render-harness, YASB, or other later-unit scope was added.
- **Task and delivery state:** Tasks `4a.1` through `4a.7` are complete; Unit 4b and later tasks remain untouched. No staging, commit, review, push, or PR occurred.

## Unit 4a — corrective accessibility/tray test attempt blocked

- **Status produced/consumed:** Manual authoritative OpenSpec status was produced because the parent explicitly prohibited `sdd-status`: change `aibar-visual-yasb`, `artifactStore: openspec`, change root `openspec/changes/aibar-visual-yasb`, readable proposal, seven specs (including `popup-presentation` and `quota-status-experience`), design, tasks, and prior apply progress. `applyState: ready`; `nextRecommended: apply`; action context is repo-local at `C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2`, which is the only edit root. No action-context warning. Parent retains runtime token/settlement.
- **Scope and delivery:** Corrective Unit 4a slice only on `feat/aibar-visual-yasb-4a`, baseline `9e88f4d`, feature-branch-chain boundary, hard cap 380. No production change was needed. Changed only the existing Unit 4a tests: `HostRuntimeTests.cs` and `QuotaVisualDesignTests.cs`; no task, documentation, staging, commit, review, push, or PR action occurred.
- **Remediation attempted:** Replaced brittle source-text checks with XAML-tree assertions for window/summary/card names and descriptions, fixed region order, tab navigation, permanent placeholders and corrected three `FreshnessLabel` bindings, secondary-only scrolling, live status, named noninteractive progress, and `AccessibleExpanderStyle` focus-visual wiring. The focus handler was invoked on an STA WPF element and its `RequestBringIntoView` event was observed. The tray matrix now covers permanent fixed slots, both/partial/no-cache unavailable/loading/stale/disabled/error/future-source age/length/no-detail cases; the host test covers constructor state, accepted `State` callbacks, and an explicitly rejected unrelated `Primary` callback while preserving the exact state reference.
- **Pre-harness checks:** `git diff --check 9e88f4d` passed (only pre-existing CRLF conversion notices). Static FQN discovery found exactly four Unit 4a tests: `AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4a_popup_hierarchy_keeps_permanent_slots_and_secondary_scrolling`, `AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4a_popup_accessibility_keeps_summary_live_status_and_noninteractive_progress`, `AIBar.Domain.Tests.HostRuntimeTests.Unit_4a_tray_formatter_keeps_both_truthful_slots_and_a_bounded_status`, and `AIBar.Domain.Tests.HostRuntimeTests.Unit_4a_host_passes_initial_and_state_updates_to_the_tray`. Check-only `dotnet format ... whitespace --verify-no-changes --no-restore` reported existing unrelated whitespace errors in `QuotaRefreshCoordinatorTests.cs`, `ScanRunProvenanceTests.cs`, `SessionFileDiscoveryTests.cs`, `Sqlite*Tests.cs`, `StructuredDiagnosticTests.cs`, and earlier regions of `QuotaVisualDesignTests.cs`; it reported none in the changed Unit 4a test regions or `HostRuntimeTests.cs`.
- **Focused harness (exactly once):** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --no-restore --filter "(FullyQualifiedName=AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4a_popup_hierarchy_keeps_permanent_slots_and_secondary_scrolling|FullyQualifiedName=AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4a_popup_accessibility_keeps_summary_live_status_and_noninteractive_progress|FullyQualifiedName=AIBar.Domain.Tests.HostRuntimeTests.Unit_4a_tray_formatter_keeps_both_truthful_slots_and_a_bounded_status|FullyQualifiedName=AIBar.Domain.Tests.HostRuntimeTests.Unit_4a_host_passes_initial_and_state_updates_to_the_tray)"` exited **1**: **2 passed, 2 failed, 0 skipped, total 4**. The only failures are new test assertions that looked up WPF attached `Grid.Row` attributes in the presentation XML namespace; the parsed attributes are unqualified, so hierarchy expected `[0,1,2,3]` but observed `[0,0,0,0]`, and the summary row expected `0` but observed null. This is test assertion shape, not a production behavior finding.
- **Warnings:** Harness warnings were existing unrelated `QuotaExportWriterTests.cs` CS8602, `BetaPresentationTests.cs`/`AnalyticsLifecycleTests.cs` unused-event and xUnit1031 warnings, and unsigned assembly CS8002 warnings. No warning named a changed Unit 4a path.
- **Post-failure discipline:** No source/test edit or test rerun followed the failing harness. No `testhost.exe`, `vstest.console.exe`, or test-owned `dotnet.exe` process remained. The index remains unstaged.
- **Candidate delta vs `9e88f4d`:** **298 authored source/test lines** (205 additions, 93 deletions), leaving 82 lines under the 380 cap: `HostRuntime.cs` 15/5, `MainWindow.xaml` 75/59, `MainWindow.xaml.cs` 14/23, `HostRuntimeTests.cs` 46/1, `QuotaVisualDesignTests.cs` 55/5. Exactly these five paths changed.
- **Task state:** No Unit 4a implementation task earned completion credit; `4a.1` through `4a.7` remain unchecked. This corrective attempt is not ready for verify.
- **Remaining corrective action:** Parent must authorize a new bounded corrective attempt to change only the two namespace-sensitive XAML test assertions before another complete Unit 4a harness may run.

## Unit 4a — popup hierarchy/accessibility/tray attempt blocked by focused-test assertion

- **Status consumed:** Parent-bound change `aibar-visual-yasb`, OpenSpec root, branch `feat/aibar-visual-yasb-4a`, baseline `9e88f4d`, repository-local action context with the repository as the edit root. The parent owns the runtime token and settlement; this worker did not acquire, reset, settle, stage, commit, review, push, or open a PR. Feature-branch-chain delivery applies to Unit 4a only.
- **Implementation candidate:** Replaced the all-popup scroll hierarchy with fixed header, permanent 5-hour/weekly cards, secondary-only scroll viewport, and fixed footer. Both cards bind `--` and unavailable-reset placeholders without availability-based layout collapse; names/help text, polite live status, non-interactive named progress, visible-focus resources, and keyboard scroll-into-view are retained. The tray now formats permanent `5h`/`7d` slots, bounded literal state and cached age from `BetaPresentationState`; existing constructor and filtered presentation update flow remain the only host wiring.
- **Static evidence:** Exact expected five-path scope confirmed; XML parsed; Unit 4a filter was statically resolved; `git diff --check 9e88f4d` passed. No source/test diagnostic warning named a changed path.
- **Focused harness (exactly once):** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --no-restore --filter "(FullyQualifiedName=AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4a_popup_hierarchy_keeps_permanent_slots_and_secondary_scrolling|FullyQualifiedName=AIBar.Domain.Tests.QuotaVisualDesignTests.Unit_4a_popup_accessibility_keeps_summary_live_status_and_noninteractive_progress|FullyQualifiedName=AIBar.Domain.Tests.HostRuntimeTests.Unit_4a_tray_formatter_keeps_both_truthful_slots_and_a_bounded_status|FullyQualifiedName=AIBar.Domain.Tests.HostRuntimeTests.Unit_4a_host_passes_initial_and_state_updates_to_the_tray)"` exited **1**: **3 passed, 1 failed, 0 skipped, total 4**. The only failure is the new structural assertion in `QuotaVisualDesignTests.cs:314`: it expected two `{Binding FreshnessLabel}` values but the fixed hierarchy correctly has three (header plus both cards).
- **Post-failure discipline:** No source/test edit and no test rerun followed the failing harness. `testhost` and `vstest.console` are absent; the only remaining `dotnet` process is the shared Roslyn `VBCSCompiler.dll` server (PID 26112), not a test process.
- **Candidate delta vs `9e88f4d`:** **268 authored source/test lines** (175 additions, 93 deletions), below the 380-line hard maximum: `HostRuntime.cs` 15/5, `MainWindow.xaml` 75/59, `MainWindow.xaml.cs` 14/23, `HostRuntimeTests.cs` 33/1, `QuotaVisualDesignTests.cs` 38/5. No extra paths changed.
- **Task state:** No Unit 4a task earned completion credit and no checkbox was changed. Remaining exact lines: `- [ ] **4a.1` through `- [ ] **4a.7`.
- **Deviation:** None from Unit 4a design intent; the unresolved failure is a test-count assertion, not a reported production behavior failure. Under the no-post-failure-edit/rerun constraint, parent authorization is required for a bounded corrective attempt.
- **Warnings:** Build emitted existing warnings only from unrelated `QuotaExportWriterTests.cs`, `AnalyticsLifecycleTests.cs`, `BetaPresentationTests.cs`, and unsigned assembly references; none named a changed path.

# Apply Progress — aibar-visual-yasb

## Unit 2 — completed payload-free `--show` activation

- **Scope and delta:** Completed only Unit 2 on `feat/aibar-visual-yasb-2` against `bc8d79f`, with exactly **115 authored source/test lines**: 8 in `App.xaml.cs`, 8 in `HostPrimitives.cs`, 20 in `HostRuntime.cs`, 34 in `HostPrimitivesTests.cs`, and 45 in `HostRuntimeTests.cs`; this is below the 290-line unit cap.
- **Behavior and coverage:** Only exact, case-sensitive `--show` requests one show and forwards no payload. Primary, secondary, readiness/pending, and startup/disposal race paths preserve one authority and the existing fixed activation event.
- **Focused evidence:** The final exact four-test payload-free activation target passed **4/4**, with 0 failed, 0 skipped, and exit 0.
- **Native settlement:** Complete with evidence revision `sha256:a43f2a0122b3a008428566228138aa72402e7bc228b63f28a1a7029d4ebd7495`, remediating revision `sha256:3ca3e11bd2908f28b2d43024de511759b78968c11d13c77bfa8d112b9dd1632e`.
- **Diagnostics and delivery:** Parent LSP diagnostics were clean on all five source/test paths and the independent `pi-lens` reported no issues. Tasks `2.1` through `2.6` are complete; Unit 3 and parent-policy tasks remain untouched. RDD remained off, no review ran, and no push or PR occurred. The failed-attempt history below is preserved.

## Unit 1b-clear — completed exact clear and Desktop composition

- **Scope and delta:** Completed only Unit 1b-clear on `feat/aibar-visual-yasb-1b-clear` against `3d13717`, with exactly **117 authored source/test lines**: 2 in `ClearAiBarDataService.cs`, 21 in `QuotaExport.cs`, 20 in `App.xaml.cs`, and 74 in `ClearAiBarDataTests.cs`. This is below the 160-line hard stop.
- **Behavior outcome:** Clear coordinates queued/in-flight publication before exact owned-path mutation, suppresses stale completion and queued resurrection, leaves a disabled/null snapshot or proven safe absence, composes the writer with failure isolation, and disposes publisher/writer-facing resources before coordinator/store dependencies.
- **Focused evidence:** The exact isolated final authorized correction target ran once and passed **11/11**, with 0 failed, 0 skipped, and exit 0.
- **Native settlement:** Complete with evidence revision `sha256:610ed5a7a547cefd03f399b6187f319188962f3cfb133187feab444ead71b930`, remediating revision `sha256:893a71127c79f4538d1c8ca9036c48df2d1ab49bd604bc7e027a7f3f8a5a2b88`.
- **Diagnostics and delivery:** Parent LSP diagnostics were clean on all four source/test paths and the independent `pi-lens` reported no issues. Tasks `1b-clear.1` through `.7` are complete; Unit 2 and unrelated parent-policy tasks remain open. RDD remained off, no review ran, and no push or PR occurred.

## Unit 1b-clear — failed focused harness; no completion credit

- **Status consumed:** Native OpenSpec status is authoritative: `applyState: ready`, `nextRecommended: apply`, repo-local action context with the repository as the allowed edit root; the parent-owned runtime attempt was already active and was not acquired, reset, or settled by this worker.
- **Scope and delivery:** Unit 1b-clear only, feature-branch-chain child based on `3d13717`; 160 authored source/test lines maximum, no size exception, no staging/commit/push/PR/review. The initial three restored files matched the parent-provided SHA-256 values.
- **Attempted implementation:** Added publisher cancellation/await coordination before clear path mutation, composed that publisher as clear work, isolated writer injection for synthetic composition, and made publisher-before-store disposal observable. The publisher coordination seam necessarily touched `src/AIBar.Application/QuotaExport.cs`; it was the existing type that owns the queued/in-flight drain and epoch cancellation and cannot be coordinated from the three restored paths alone.
- **Static checks:** `git diff --check` passed. Check-only `dotnet format ... whitespace --verify-no-changes --no-restore` failed only on pre-existing unrelated formatting findings in `QuotaRefreshCoordinatorTests.cs`, `QuotaVisualDesignTests.cs`, `ScanRunProvenanceTests.cs`, other unrelated test files, and packaging tools; it reported no changed Unit 1b-clear path.
- **Focused harness (exactly once):** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --filter "FullyQualifiedName~ClearAiBarDataTests"` exited **1** during compilation. No test executed (pass 0, fail 0, skip 0). Diagnostic: `App.xaml.cs(83,63) CS0029` cannot implicitly convert `QuotaExportPublisher` to `IAiBarClearWork`.
- **Post-failure discipline:** No production/test edit or test rerun followed the failed harness. The index is empty; no `testhost.exe`, `vstest.console.exe`, or `dotnet.exe` process remained. An untracked `NUL` entry appeared after the failed command and was left untouched to honor no-post-failure-edit discipline.
- **Candidate delta vs `3d13717`:** 112 authored source/test lines: `ClearAiBarDataService.cs` 2 (1 add/1 delete), `QuotaExport.cs` 19, `App.xaml.cs` 20 (11 adds/9 deletes), and `ClearAiBarDataTests.cs` 71. Source/test paths are the three intended paths plus the necessary publisher seam; no writer files changed.
- **Remaining tasks:** `- [ ]` 1b-clear.1 through 1b-clear.7 remain unchecked. Parent must decide whether to authorize a bound correction that explicitly has `QuotaExportPublisher : IAiBarClearWork`; no completion or verify readiness is claimed.

## Unit 1b-writer — completed canonical-path atomic writer

- **Scope:** Completed only the Windows writer child in `src/AIBar.Desktop/QuotaExportWindows.cs` and `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`, exactly **349 authored source/test lines** (195 + 154), below the 360-line child stop and 400-line review budget.
- **Outcome:** The canonical-path, durable same-directory atomic writer now enforces protected minimum-rights ACLs, reparse/race defenses, complete old/new reader views, and exact owned-temp cleanup. The ACL ordering correction was applied before settlement.
- **Focused evidence:** The isolated writer target passed **18/18** with 0 failed and 0 skipped. LSP diagnostics and the selected independent lens were clean.
- **Native settlement:** Complete with evidence `sha256:2eadabbcbebf597cdfc448d8df48a59f664b76ee718e71fe625c44ff45de9e01`, remediating `sha256:335227fe919b1374f676155e84e1c4e0c7bdba0948f2d419799596e6762810dd`.
- **Task and delivery state:** `1b-writer.1` through `.8` are complete. RDD remained off, no review was started, and no push or PR occurred. Unit 1b-clear and parent-owned policy tasks remain open.

## Unit 1b — historical failed candidate and writer/clear split

- **Maintainer decision:** `Dividir writer/clear`. The active chain is now `1a-privacy -> 1b-writer -> 1b-clear -> 2`; 1b-clear may start only from a committed 1b-writer child.
- **Historical failed candidate identity:** At split-planning time, the candidate was recorded as exactly **359 authored source/test lines** across five paths, uncommitted and unstaged: 164 lines in untracked `src/AIBar.Desktop/QuotaExportWindows.cs`, 144 lines in untracked `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`, 2 changed lines in `src/AIBar.Application/ClearAiBarDataService.cs`, 12 changed lines in `src/AIBar.Desktop/App.xaml.cs`, and 37 changed lines in `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`. Evidence: `sha256:6c7c9a9be9234e8b006d18f16b66c057122d6443d5ba0c45750c9b09fd548381`.
- **Observed outcome:** Static acceptance failed and **no test was run**. The candidate grants current user and SYSTEM `FullControl` and tests that same permissive descriptor instead of minimum rights; it does not prove concurrent readers observe only complete old/new documents. The clear/composition portion does not yet prove queued/in-flight coordination, stale-completion suppression, bounded disabled/null recreation or safe absence, writer-failure isolation in the clear child, or explicit disposal order. No 1b implementation, check, or commit task is complete.
- **Native state:** The failed Unit 1b native attempt was reset. The reset is procedural state only; it does not erase the failed evidence, authorize another attempt, or create completion credit.
- **1b-writer forecast:** **335–360 authored source/test lines** (approximately 175–190 production and 160–170 tests) in `src/AIBar.Desktop/QuotaExportWindows.cs` and `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`, plus only unavoidable project metadata. This includes replacing `FullControl` with protected minimum rights and adding real complete-reader atomicity coverage. Hard stop: **360**, never 400 and no `size:exception`.
- **1b-clear forecast:** **90–140 authored source/test lines** (approximately 20–40 production and 70–100 tests) in `src/AIBar.Application/ClearAiBarDataService.cs`, `src/AIBar.Desktop/App.xaml.cs`, and `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`. It owns exact-path clear coordination, stale suppression, disabled/null recreation or safe absence, composition, writer-failure isolation, and explicit disposal order. Hard stop: **160**.
- **Rollback boundaries:** Roll back 1b-clear only after proving the snapshot disabled/null or safely absent; remove its clear/composition wiring while retaining committed 1b-writer and all Application privacy behavior. Roll back 1b-writer by first securing/removing or atomically nulling the snapshot, then removing the writer while leaving 1a-privacy intact.

### Historical candidate-preservation proposal — provenance unavailable

The following named-stash/selective-restoration plan is preserved as the original proposal only. No immutable stash object ID, `refs/stash`, named stash/ref, or stash reflog survives, so the plan cannot now be claimed as executed. The writer (`3d137171a9c6c3a8fd4d079e8d9de6bf819a0043`) and clear (`bc8d79f719989a21f72b5c6cf843117c5001228d`) children exist as separate commits, but their existence does not prove stash/selective-restoration provenance.

**Original proposal (historical; not execution evidence):** Use one **named Git stash including untracked files**, scoped to the exact five candidate paths, only after explicit parent authorization. Do not create a WIP commit: it would pollute the feature-branch chain with a non-deliverable mixed candidate and could be mistaken for reviewed history.

1. From `feat/aibar-visual-yasb-1b`, create a stash named `aibar-visual-yasb-1b-preserved-359-6c7c9a9b` with `--include-untracked` and exact pathspecs for:
   - `src/AIBar.Desktop/QuotaExportWindows.cs`
   - `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`
   - `src/AIBar.Application/ClearAiBarDataService.cs`
   - `src/AIBar.Desktop/App.xaml.cs`
   - `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`
2. Immediately record the immutable stash object ID rather than relying on the movable `stash@{0}` label. Because the writer files were untracked, restore only those two paths from `<stash-oid>^3` into the worktree with `git restore --source=<stash-oid>^3 --worktree -- <writer-paths>`. Do not use `stash pop` or restore the clear trio on the writer child.
3. Implement, validate, and commit 1b-writer as the first child. Create 1b-clear from that committed child, then restore only the three tracked clear/composition paths from `<stash-oid>` with `git restore --source=<stash-oid> --worktree -- <clear-paths>`.
4. Keep the named stash and recorded object ID until both children are committed and their exact path scopes are verified. Any later stash removal is a separate parent-owned action.

**Historical tradeoff:** this proposal would have kept both child diffs honest and avoided a WIP commit, but a stash is local-only and easy to misreference if its ordinal moves. The recorded object ID, exact pathspecs, separate untracked-parent restore, and prohibition on `pop` were therefore mandatory to the proposal.

**Current reconciled disposition:** No stash/selective-restoration provenance is available or claimed; the independently existing writer and clear commits are the durable child boundaries.

## Unit 1a-privacy — accepted publisher and consent-lifecycle result

- **Scope:** Work remained on child branch `feat/aibar-visual-yasb-1a-privacy` based on accepted authority commit `130c626`. The candidate changes only the six intended Application/test paths; no Desktop, filesystem adapter, paths, ACL, YASB, staging, commit, push, or PR work occurred.
- **Implemented candidate:** `QuotaExportPublisher` consumes serialized authority updates, gates post-enable disclosure on retrieval generation, uses event sequence/state for freshness and latest-state ordering, owns disclosure-epoch cancellation, rejects stale completions, isolates routine publication failures, and integrates persisted/manual consent lifecycle ordering.
- **Runtime evidence:** The absolute lifecycle/contract selection compiled and passed **29/29**. Its filter omitted `QuotaExportPublisherTests`; that incomplete evidence was recorded rather than relabeled. After explicit maintainer authorization, the corrected exclusive absolute target `FullyQualifiedName~QuotaExportPublisherTests` passed **5/5** without code changes. Combined runtime evidence is **34/34**.
- **Native settlement:** Objective completed with evidence `sha256:216fd2e690e97810e3986133bc3c7ca2962e45fb1bbd5e3f70212866173d2145`, remediating omitted-filter evidence `sha256:e0fd70b2b93b983c8bb982c2278e34226afbc590a7fbf34dbc5a6ad5a1e62f18`.
- **Candidate evidence:** The authored source/test delta is exactly **260 lines**: 36 in `BetaRuntime.cs`, 103 in `QuotaExport.cs`, 1 in `StartupSettings.cs`, 17 in `BetaConsentOrQuotaTests.cs`, 87 in `QuotaExportTests.cs`, and 16 in `StartupSettingsTests.cs`.
- **Integrity:** Static independent inspection passed without correction; `git diff --check` passed with only line-ending notices, the index remains empty, and no test process remained. The source/test candidate is exactly at the unit hard stop and below the native 400-line ceiling.
- **Task state and next action:** `1a-privacy.1` through `.9` are complete. Native settlement and independent inspection passed, and the maintainer authorized the local commit on child branch `feat/aibar-visual-yasb-1a-privacy`. All later tasks remain unchecked.

## Unit 1a-authority — coherent attempt result

- **Superseding authority:** The maintainer rejected the proposed `1a-authority-stream` / `1a-authority-races` split and authorized tasks `.1`–`.8` as one coherent causal-protocol unit with a hard cap of **300 authored source/test lines** and no `size:exception`. This decision supersedes the historical 180-line interruption and proposed split below.
- **Scope:** Work remained on branch `feat/aibar-visual-yasb-1a-authority` at accepted contract commit `7c6887983302ecfd8e06fbeab5c2c618931c76bb`. No native SDD, review, attempt, dispatcher, settlement, staging, commit, privacy, publisher, Desktop, filesystem, ACL, YASB, or later-unit work was performed.
- **Context and forecast:** The full design, sanitized-export and quota-status specifications, authority tasks, cumulative progress, current coordinator and tests, provider/store/clock/freshness/clear/lifecycle interfaces, and `StateChanged` consumer policy were inspected. The complete **235–285** forecast fit below 300, so source/test implementation proceeded without a split.
- **Implemented candidate:** `QuotaAuthorityUpdate` is authority-owned outside `QuotaExport.cs`; the coordinator candidate centralizes state/sequence/generation enqueue under one gate and drains paired `AuthorityUpdated`/`StateChanged` callbacks outside that gate. Synthetic tests were added for transition/generation matrices, same-reference/equal-content retrievals, blocked/reentrant/throwing subscribers, reevaluate/refresh races, replay, provider/store versus cancel/clear, and disposal.
- **Focused outcome:** **passed**. After the maintainer-authorized fixture correction, the exact absolute `QuotaRefreshCoordinatorTests` target ran once and passed **18/18** with zero failures or skips. Native settlement completed with evidence `sha256:5d158001dd07f9a5fabe9ece4bb70c65ee5d9e5110518f1f68136d4fc97b05b8`, remediating the prior compile-failure evidence, and independent acceptance returned PASS.
- **Candidate evidence:** The authored source/test delta is **271 lines**: 161 in `QuotaRefreshCoordinator.cs`, 5 in `QuotaAuthorityUpdate.cs`, and 105 in `QuotaRefreshCoordinatorTests.cs`. `QuotaExportTests.cs` is unchanged.
- **Integrity:** The source/test candidate stays within the 300-line hard cap, uses only the three intended authority paths, has an empty index, and passes whitespace checks. LSP diagnostics were not exposed; focused compilation/tests and independent source inspection passed.
- **Task state and next action:** `1a-authority.1` through `.9` are complete. Native settlement and independent acceptance passed, and the maintainer authorized the local commit on child branch `feat/aibar-visual-yasb-1a-authority`. Privacy and all later tasks remain unchecked; the rejected split remains superseded.

## Unit 1a-contract — bounded acceptance remediation result

- **Authorization and scope:** The maintainer authorized one bounded remediation of Unit `1a-contract` only, raised this unit's hard source/test cap from 220 to **260 authored lines**, and authorized no `size:exception`. No native SDD, review, attempt, dispatcher, authority, publisher, consent/privacy, Desktop, filesystem, ACL, YASB, or later-unit work was performed.
- **Pre-edit forecast:** The requested mapping correction, partial-cache/weekly-only/generic-unavailable coverage, two-publication timestamp proof, canonical fallback expansion, and xUnit2017 cleanup were forecast at **230–242 total authored source/test lines** from the 208-line candidate. Complete remediation therefore fit below 260 without compressing assertions into unreadable chains.
- **Outcome:** **passed**. No-cache/no-failure now projects `state: unavailable` with JSON `warning: null`. Explicit failures without cache remain bounded `warning: unavailable`; partial weekly-only cache is retained exactly during refresh and operational failure.
- **Publication-time evidence:** Two distinct trusted UTC publication times prove `generatedAt` advances while retained `sourceRetrievedAt` remains byte-identical, value-identical, and UTC.
- **Fail-closed evidence:** The shared fallback assertion now verifies numeric schema version 1, trusted UTC `generatedAt`, the complete ordered top-level allowlist, `state: unavailable`, `warning: unavailable`, and null source/window fields for every existing arbitrary inconsistent direct-document case.
- **Mandatory matrix inspection:** The approved Unit 1a-contract matrix was inspected once after edits. The newly required partial-cache, weekly-only, generic unavailable, no-cache/no-failure JSON-null, and republish-age rows completed the genuinely absent coverage; no additional specified row remained absent.
- **Focused target:** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --no-restore --filter "FullyQualifiedName~QuotaExportTests"` ran exactly once after static corrections and passed **6/6** with 0 failed and 0 skipped. Reported compiler/analyzer warnings came from unchanged test files or existing unsigned assembly references; no warning named either changed source/test file, and changed-file xUnit2017 is absent.
- **Diagnostics:** A primary LSP diagnostics tool was not exposed in this worker runtime. Focused compilation and tests succeeded.
- **Candidate identity:** `sha256:c01b2d0ff4bd6f5767a9c0e0ba64222cf28e09524c5827b3d2dfbac38a3529da` over the two ordered path-bound source/test files. The candidate is **238 authored lines**: 114 in `src/AIBar.Application/QuotaExport.cs` and 124 in `tests/AIBar.Domain.Tests/QuotaExportTests.cs`.
- **Integrity:** Both source/test files and this progress artifact pass no-index `git diff --check`; only the exact intended source/test paths are present for this unit. The tracker planning commit normalized Markdown hard breaks before branch creation, and the child diff remains whitespace-clean.
- **Task state:** `1a-contract.1` through `1a-contract.9` are complete. Native settlement and independent acceptance both passed, and the maintainer authorized the local commit on child branch `feat/aibar-visual-yasb-1a-contract`. Every authority, privacy, platform, and later-unit task remains unchecked.

> The full cumulative history below is intentionally retained. The Unit 1b writer/clear split at the top of this file is the active plan. Earlier sections that describe a failed attempt, a 223-line mixed candidate, preservation pending authorization, a zero-progress split-decision snapshot, or an unsplit `Unit 1b` are historical only; they remain evidence of what occurred and are not active tasks or dependencies.

## Maintainer-authorized Unit 1a-contract apply

- **Authorized disposition:** the previously preserved mixed candidate is no longer present. The maintainer authorized this clean source/test disposition and a contract-only apply; the pre-apply working tree showed only the untracked `openspec/changes/aibar-visual-yasb/` artifact set.
- **Pre-edit forecast for tasks 1a-contract.1–.7:** 85–95 production lines plus 115–125 compact table-driven test lines, for 200–220 authored source/test lines total. This includes the closed schema/domain checks, forbidden-field sentinels, complete projection/failure/credential/window matrix, timestamp/percentage/reset boundaries, arbitrary direct-document fallback matrix, nullable-reset records/ports, projector, normalizer, and validating wire serialization.
- **Hard stop:** source/test implementation may proceed only while all mandatory cases remain covered at no more than 220 authored additions plus deletions; there is no size exception and no authority/privacy/platform scope may be borrowed.

## Unit 1a-contract attempt result

- **Outcome:** failed. The one authorized focused command reached the intended `aibar-analytics-v2` project but compilation failed at `QuotaExportTests.cs:27` with `CS0246` because `JsonReaderException` was not resolved; no selected test executed and the command was not rerun.
- **Candidate:** `src/AIBar.Application/QuotaExport.cs` has 115 additions and `tests/AIBar.Domain.Tests/QuotaExportTests.cs` has 93 additions, for **208 authored source/test lines**. The source/test scope contains exactly those two untracked paths, both pass no-index `git diff --check`, and the index remains empty.
- **Diagnostics:** a primary LSP diagnostics tool was not available in this worker runtime. The test/build output named only paths under the intended `aibar-analytics-v2` checkout.
- **Task state:** tasks `1a-contract.1` through `.7` remain unchecked because required passing evidence is absent; `1a-contract.8` and every later/authority/privacy task also remain unchecked.
- **Next action:** parent-owned disposition of this failed candidate and any separately authorized correction attempt; do not relabel the failed compile as behavior evidence.

## Current status

- **Maintainer planning decision:** `Dividir contract/authority`.
- **Implementation status:** acceptance incomplete; no task in the new ten-unit plan is complete.
- **Candidate state:** the current source/test candidate is preserved intact, uncommitted, and unstaged at **223 authored lines**: 25 additions and 5 deletions in `QuotaRefreshCoordinator.cs`, 67 lines in untracked `QuotaExport.cs`, and 126 lines in untracked `QuotaExportTests.cs`.
- **Target-scoped evidence:** the absolute target completed **35/35**. This is evidence only for the selected cases that ran; it does not cover omitted acceptance rows.
- **Independent acceptance validation:** **FAIL**. Passing target tests are insufficient because required contract and authority/concurrency cases are absent or contradicted by the candidate.
- **Native objective:** the latest native validation-only objective is complete. Native procedural completion does not convert the independent acceptance failure into implementation completion.
- **Planning progress:** all implementation/check/commit tasks for 1a-contract, 1a-authority, 1a-privacy, and 1b are unchecked; all later implementation tasks also remain not started. Only the parent-owned `feature-branch-chain` decision remains checked in `tasks.md`.
- **Candidate disposition:** unresolved. Preserve every source/test byte until the maintainer explicitly decides whether the candidate is discarded, retained for selective adaptation, or otherwise handled by a later authorized apply.
- **Next action:** `await explicit approval for candidate disposition and Unit 1a-contract apply`.

## Cumulative candidate, interruption, and evidence history

| Stage | Observed evidence or disposition | Result for the current plan |
|---|---|---|
| Earlier mixed Unit 1a candidate | 277 authored source/test lines with a recorded focused 40/40 selection | Independently insufficient. It omitted authority freshness, blocked-write cancellation, no-cache warning, and matrix coverage. It earns no completion credit. |
| Earlier disposition | Maintainer authorized discarding that mixed candidate and restoring the source/test tree before a narrower attempt | Historical candidate closed; no discarded line carries forward as completed work. |
| Narrow Application attempt | Added `QuotaExport.cs`, changed `QuotaRefreshCoordinator.cs`, and added `QuotaExportTests.cs`; 145 authored lines | Compilation failed before tests ran. `QuotaExportTests.cs` passed `QuotaRefreshState` where a helper expected `QuotaSnapshot?` (`CS1503`). No checkbox was completed. |
| Zero-edit procedural interruption | A continuation/preflight stopped for procedural authority before changing source/test bytes | Zero source/test edit and zero behavioral evidence. It is retained only as interruption history. |
| Exceptional correction | A separately authorized exceptional correction superseded the compile-failing shape and led to the current candidate lineage | The correction is historical authorization, not acceptance. Its resulting candidate remains subject to independent validation and disposition. |
| Wrong-checkout test incident | A reported test execution used the wrong checkout | Invalid evidence. It is excluded from all RED/GREEN, target, acceptance, and completion claims. |
| Current absolute target | The intended absolute target later completed 35/35 | Valid only for the selected target cases. It proves neither omitted contract matrices nor concurrent authority ordering. |
| Latest native validation-only objective | Completed with the target evidence recorded | Procedurally complete; no source/test disposition or acceptance completion follows from it. |
| Independent acceptance validation | Reviewed the current candidate against the approved proposal/specifications and mandatory omitted cases | **FAIL**. No task may be considered complete. |
| Maintainer decision | `Dividir contract/authority` | Replan into independent 1a-contract and 1a-authority units before privacy; preserve the 223-line candidate pending later disposition. |

The earlier 40/40 and current 35/35 runs are both bounded evidence. Neither may be relabeled as complete GREEN evidence for 1a-contract, 1a-authority, 1a-privacy, or 1b because the missing acceptance cases were not exercised.

## Independent acceptance findings assigned to the new units

### Unit 1a-contract — closed projection and fail-closed wire contract

The current candidate does not yet establish this unit because:

- `QuotaExportWindow.ResetAt` is non-nullable even though missing reset must be representable as null.
- The public `Serialize` path can receive arbitrary `QuotaExportDocument` values, force `schemaVersion` to 1 while preserving other inconsistent fields, and throw for unknown state/warning values instead of failing closed.
- Unknown state or warning input must normalize or reject into the canonical unavailable/null document, never throw or leak an arbitrary wire value.
- Missing and unusable credential mapping/evidence is incomplete.
- Mandatory cases are missing or incomplete: numeric `schemaVersion` assertion; forbidden root/db/source-model properties; weekly-only projection; exact retained source timestamp; an interior percentage; nullable/missing reset; and inconsistent direct-document serialization.
- The full state/failure/cache/credential/timestamp/percentage/partial-window/forbidden-field matrices must pass independently of coordinator behavior.

Contract owns export records/enums/input/projector/validating serialization boundary/ports. It owns no `QuotaAuthorityUpdate`, coordinator event stream, publisher, consent/privacy behavior, Desktop, filesystem, or ACL code.

### Unit 1a-authority — serialized causal authority stream

The current candidate does not yet establish this unit because:

- sequence allocation and event invocation are not one serialized delivery protocol; concurrent callers can allocate under a lock and still deliver callbacks out of order;
- transition state mutation, sequence allocation, retrieval-generation allocation, and paired event delivery need one accepted order;
- concurrent reevaluate/clear/refresh behavior is under-tested;
- retrieval racing clear or cancellation is not proven safe;
- same-reference and equal-content concurrent sequencing is not proven;
- cross-event `AuthorityUpdated`/`StateChanged` compatibility and non-interleaving are not proven;
- persistence-before-generation is positive evidence, but it is not enough without the complete failure/race matrix.

Authority owns `QuotaAuthorityUpdate` and `QuotaRefreshCoordinator`. It depends on contract but owns no publisher, consent/privacy lifecycle, Desktop, filesystem, or ACL behavior.

### Unit 1a-privacy — publisher and consent lifecycle

Privacy now depends on the independently complete authority unit. Its prior responsibilities remain:

- subscribe to every serialized authority update;
- use retrieval generation only for post-enable unlock;
- use event sequence and state for freshness and latest-state order;
- cancel blocked ordinary writes when disclosure epoch advances;
- make disable/revoke/clear/re-enable and persisted-consent restart fail closed;
- prove latest-state, same-reference, cancellation-resistant writer, privacy race, and disposal matrices.

No publisher or privacy work can begin merely because the current mixed candidate contains contract/authority symbols.

### Unit 1b — Windows writer and composition

Unit 1b remains the Windows path, same-directory atomic writer, pre-commit ACL, clear ownership, disabled recreation, and Desktop composition unit. It depends on 1a-privacy and transitively on independently accepted authority and contract units.

## Positive evidence retained without overstating it

The following findings remain useful constraints for a later apply:

- operational no-cache mapping in the current projector uses bounded unavailable behavior rather than stale `refresh-failed` disclosure;
- the current wire shape is bounded to the intended root/window fields for the exercised projected documents;
- accepted retrieval generation is allocated after persistence in the exercised path;
- no platform, filesystem, consent/privacy publisher, Desktop, or private-data access was added by the current candidate;
- the current authored source/test delta is exactly 223 lines;
- the intended absolute target completed 35/35.

These positives do not compensate for omitted mandatory cases or authority serialization defects.

## Replanned implementation progress

| Review unit | Progress | State |
|---|---:|---|
| 1a-contract — closed projection and fail-closed wire contract | 0 | Not started; candidate disposition and apply approval required. |
| 1a-authority — serialized causal authority stream | 0 | Not started; depends on independently complete 1a-contract. |
| 1a-privacy — publisher/freshness/cancellation/consent lifecycle | 0 | Not started; depends on independently complete 1a-authority. |
| 1b — Windows writer/ACL/composition | 0 | Not started; depends on 1a-privacy. |
| 2 — payload-free activation | 0 | Not started; depends on 1b in the chain. |
| 3 — automatic semantic themes | 0 | Not started; depends on 2. |
| 4a — popup hierarchy/accessibility/tray | 0 | Not started; depends on 3. |
| 4b — placement/DPI/DWM/rendered validation | 0 | Not started; depends on 4a. |
| 5 — stock YASB adapter | 0 | Not started; depends on 4b and earlier contracts. |
| 6 — ZIP/docs/rollback cleanup | 0 | Not started; depends on 5. |

## Candidate integrity and continuation boundary

- Preserve all current source/test bytes exactly until an explicit disposition response is supplied.
- Do not split, repair, format, stage, commit, or delete the candidate during planning.
- Do not reuse wrong-checkout output as evidence.
- Do not treat 35/35, 40/40, native objective completion, bounded shape, or persistence-before-generation as completion of omitted acceptance cases.
- Start 1a-contract first. Do not start authority until contract has an independent end state; do not start privacy until authority has an independent end state; do not start 1b until privacy is complete.
- `QuotaExport.cs` may span contract and later privacy only by symbol ownership. Authority symbols belong with the coordinator/authority unit.
- Keep `feature-branch-chain`; no child may reach 400 authored lines and no `size:exception` is authorized.
- This planning handoff runs no tests/builds/native lifecycle commands and changes no source/test byte.

**Next action:** `await explicit approval for candidate disposition and Unit 1a-contract apply`.

## Unit 2 — failed focused activation harness; no completion credit

- **Status consumed:** Native OpenSpec status was authoritative: `applyState: ready`, `nextRecommended: apply`, and repository-local action context with the repository as the only allowed edit root. The parent-owned runtime attempt was already active and was not acquired, reset, or settled here.
- **Scope and delta:** Only the Unit 2 source/test paths were changed: `App.xaml.cs` +5/-3, `HostPrimitives.cs` +8/-0, `HostRuntime.cs` +17/-3, `HostPrimitivesTests.cs` +34/-0, and `HostRuntimeTests.cs` +41/-2 — **113 authored source/test lines** total, below the 290-line hard cap.
- **Attempted behavior:** The candidate adds an exact, case-sensitive payload-free `--show` parser; queues/coalesces a show request until `TrayHostRuntime.Start`; routes secondary activation through the existing fixed auto-reset event; and calls the existing popover without creating another runtime or payload channel. App defers first-instance show until composition initialization succeeds.
- **Static evidence:** `git diff --check` and `dotnet format "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" whitespace --verify-no-changes --no-restore` both exited 0.
- **Focused harness (exactly once):** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --no-restore --filter "FullyQualifiedName~Show_activation_parser_accepts_only_the_exact_payload_free_token|FullyQualifiedName~Show_activation_preserves_the_fixed_auto_reset_single_instance_handoff|FullyQualifiedName~Show_activation_queues_one_request_until_host_start_then_activates_the_existing_popover|FullyQualifiedName~Show_activation_from_a_secondary_instance_uses_the_primary_popover_only"` exited **1**: pass 2, fail 2, skip 0, total 4. Both runtime tests failed only while disposing `SingleInstanceHost` with `System.ApplicationException: Object synchronization method was called from an unsynchronized block of code` at `HostPrimitives.cs:60`; the parser and fixed auto-reset handoff tests passed.
- **Post-failure discipline:** No source/test edit and no test rerun followed the failure. `testhost.exe` and `vstest.console.exe` are absent; the remaining `dotnet.exe` PID 4700 is the shared Roslyn `VBCSCompiler.dll` server, not a test process. The index is unstaged and no task checkbox was changed.
- **Remaining tasks:** `- [ ]` 2.1 through 2.6 remain unchecked. Parent must authorize a bounded correction to make the asynchronous runtime tests dispose the mutex on its owning thread (or otherwise preserve ownership-thread cleanup) before a new focused harness can run.
- **Workload / PR boundary:** Unit 2 only on `feat/aibar-visual-yasb-2`, bounded at 290 source/test lines; no size exception, staging, commit, review, push, or PR.

## Unit 3 — automatic semantic themes: failed focused harness; no completion credit

- **Status and scope consumed:** Parent-bound authoritative readiness was `proceed` for change `aibar-visual-yasb`, branch `feat/aibar-visual-yasb-3`, with feature-branch-chain Unit 3 only. Strict TDD is inactive. The worker did not acquire, reset, or settle a runtime attempt, and performed no staging, commit, review, push, or PR action.
- **Candidate scope:** Added automatic light/dark/high-contrast semantic dictionaries, a dispatcher-marshalled and coalescing theme controller/source with popup-open reevaluation and disposal, plus safe best-effort DWM dark-mode chrome. Modified only `src/AIBar.Desktop/App.xaml`, `App.xaml.cs`, `HostRuntime.cs`, `tests/AIBar.Domain.Tests/QuotaVisualDesignTests.cs`, and added `src/AIBar.Desktop/WindowsTheme.cs` plus `Themes/Semantic.Light.xaml`, `Semantic.Dark.xaml`, and `Semantic.HighContrast.xaml`. No popup layout, tray, YASB, network, credential, or quota/export behavior changed.
- **Static checks:** `git diff --check` passed. `dotnet format "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" whitespace --verify-no-changes --no-restore` passed.
- **Focused harness (exactly once):** `dotnet test "C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2/tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj" --no-restore --filter "FullyQualifiedName~Semantic_theme_dictionaries_have_exact_key_parity_and_system_high_contrast_colors|FullyQualifiedName~Theme_controller_is_high_contrast_first_dispatcher_safe_and_retains_resources_after_failure_or_disposal"` exited **1** at WPF XAML compilation before either selected test ran. Diagnostic: `src/AIBar.Desktop/App.xaml(7,10) MC3074`, `ResourceDictionary.MergedDictionaries` is invalid at that location.
- **Post-failure discipline:** No source/test edit and no test rerun followed the failed command. No `testhost.exe` or `vstest.console.exe` remained. The remaining `dotnet.exe` PID 17180 is the shared Roslyn `VBCSCompiler.dll` server.
- **Candidate size:** **245 authored source/test lines** versus `ff6dfd9`, below the Unit 3 380-line hard maximum: 83 additions/deletions across modified tracked paths and 162 lines in new source/resource files. The index remains unstaged.
- **Tasks and delivery:** Tasks `3.1` through `3.6` remain unchecked because focused runtime evidence is absent. This Unit 3 feature-branch-chain boundary remains below 380 lines; no size exception is authorized.
- **Next action:** Parent-owned disposition and, if authorized, a bounded correction for the XAML resource-dictionary structure before another focused harness.

## Unit 3 — completed automatic semantic Windows themes

- **Correction sequence:** The failed-harness history above remains intact. The bounded continuation repaired the `App.xaml` resource-dictionary wrapper and the invalid integer-pattern correction, completed the production-gate remediation, and applied a final test-only coverage correction before settlement.
- **Behavior outcome:** Production now tracks automatic semantic light, dark, and high-contrast state with high-contrast-first registry handling; exact dictionary-key parity and system-color mappings; dispatcher-safe distinct-state coalescing with latest-state retention; same-theme idempotency; popup-open missed-change recovery; prior-resource retention after load failure; clean unsubscription/disposal; and unchanged accessibility and window identity.
- **Scope boundary:** DWM work was removed and deferred to Unit 4b as required by the Unit 3 exclusion. No app theme selector or persistence, popup layout, tray, YASB, network, quota, or other later-unit scope was added.
- **Focused evidence:** The exact complete focused target ran once and passed **5/5**, with 0 failed, 0 skipped, and exit 0. Native settlement is complete at evidence revision `sha256:96ad66f1d8d09b9f11cd124708d66f0bb139ae88b4bf1f4c983dcd923b862823`.
- **Diagnostics and size:** The four changed C# paths were clean under direct primary LSP diagnostics; focused `pi-lens` structural runners reported no issues. The full LSP sweep was inconclusive, so the direct changed-path results remain authoritative. `git diff --check` was clean. The candidate is exactly **296 authored source/test lines** versus `ff6dfd9`, below the 380-line Unit 3 cap, with no residual `dotnet`, `testhost`, or `vstest` process.
- **Task and delivery state:** Tasks `3.1` through `3.6` are complete; Unit 4 and later tasks remain untouched. No review, push, or PR occurred.

## Unit 5 — stock YASB adapter attempt blocked by focused Pester diagnostics

- **Status consumed/produced:** Parent-bound OpenSpec change `aibar-visual-yasb`, authoritative repository `C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar-analytics-v2`, branch `feat/aibar-visual-yasb-5`, baseline `29001615f4a2cca6250bd9370bbb2774f01b2347`, repo-local edit root only. Manual status is `applyState: ready`, `dependencies.apply: ready`, `nextRecommended: apply`; proposal, design, tasks, sanitized-export, external-show-activation, and yasb-custom-widget specs were readable. Strict TDD is inactive. Feature-branch-chain Unit 5 is the PR boundary. Parent owns attempt token/settlement. No action-context warning.
- **Candidate scope:** Added only `yasb/read-aibar-quota.ps1`, `yasb/show-aibar.cmd`, `yasb/custom-widget.example.yaml`, `yasb/custom-widget.example.css`, compact synthetic fixtures, and `yasb/tests/read-aibar-quota.Tests.ps1`. The reader has no script parameter block, contains a dot-source-only fixture seam, uses the canonical LocalApplicationData `AIBar\yasb-quota.json` path, attempts a 16 KiB bounded read, and returns only label/tooltip/className. The launcher and sample assets contain only the fixed show intent and stock configuration surface. No production profile, app, YASB process, network, credential, refresh, auth, write/delete, staging, commit, review, push, or PR operation occurred.
- **Pre-harness checks:** Per-untracked-file `git diff --no-index --check /dev/null <absolute-path>` passed with CRLF conversion warnings only. PowerShell parser check reported no parse errors, but the focused harness subsequently exposed a runtime parse diagnostic. Pester 3.4.0 was the available isolated function-test seam. Candidate total is **204 logical authored asset/test lines** across 22 files (30,256 bytes), below the 380-line hard maximum.
- **Focused harness (exactly once):** `powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command 'Import-Module Pester -RequiredVersion 3.4.0; $result = Invoke-Pester -Script "yasb/tests/read-aibar-quota.Tests.ps1" -PassThru; if ($result.FailedCount -ne 0) { exit 1 }'` exited **1**: **1 passed, 3 failed**. The static fixture-seam assertion used an invalid Pester regular expression (`AIBar\yasb-quota\.json`, unrecognized `\y`). The reader could not parse at `yasb/read-aibar-quota.ps1:14`: interpolated `$Name:` must use `${Name}` before `:`. No fixture behavior assertions executed after that parse failure.
- **Post-failure discipline:** No source, test, fixture, or asset edit and no test rerun followed the failing harness. No Unit 5 task earned completion credit; persisted tasks `5.1` through `5.7` remain visibly unchecked. No cleanup process was started by the harness.
- **Remaining work:** Correct the Pester matcher and PowerShell interpolation under a new parent-authorized bounded attempt, then rerun one complete isolated Unit 5 harness. Unit 6 remains out of scope.

## Unit 5 — completed stock YASB adapter

- **Behavior outcome:** Added a read-only stock YASB CustomWidget adapter with an empty public parameter surface, a private fixture seam, the fixed LocalApplicationData export path, one trusted UTC clock read, a 16 KiB pre-parse limit, exact ordered schema-v1 validation, ordinal state/warning handling, safe three-field output, permanent 5h/7d slots, visible state/warning/reset/age text, and fail-closed placeholders.
- **Assets and activation:** The sample configuration uses official nested `options.exec_options.run_cmd/run_interval/return_format`, a 60000 ms interval, path-neutral `<AIBarRoot>` commands, and only the left-click callback. The relative launcher passes one literal `--show`; CSS exposes bounded state, warning, and partial classes.
- **Fixture evidence:** Synthetic coverage includes every valid state/warning, both/partial/no windows, nullable and due resets, malformed/missing/empty/oversized input, property order/casing, schema and numeric types, enum casing, non-finite/range failures, strict UTC, future time, source-after-generation, and clock rollback. No live AIBar/YASB process, network, authentication, refresh, private profile, or production data was used.
- **Focused evidence:** The final expanded isolated Pester 3.4 harness ran once and passed **5/5**, with 0 failed, 0 skipped, and exit 0. Native settlement is complete at evidence revision `sha256:611267db6e8c81d0aba74baff98338688c9e4d84a5051832501388878361d4a1`.
- **Gate and size:** Fresh phase-contract verification returned **PASS** for tasks 5.1–5.7. The candidate is **297 logical lines**, below the 380-line Unit 5 cap.
- **Task and delivery state:** Tasks 5.1 through 5.7 are complete. Unit 6 remains untouched. No review, push, or PR occurred.
