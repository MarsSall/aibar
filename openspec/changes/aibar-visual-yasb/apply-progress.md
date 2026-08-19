# Apply Progress — aibar-visual-yasb

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

## Unit 1b — failed candidate preserved and split into writer then clear

- **Maintainer decision:** `Dividir writer/clear`. The active chain is now `1a-privacy -> 1b-writer -> 1b-clear -> 2`; 1b-clear may start only from a committed 1b-writer child.
- **Failed candidate identity:** The preserved candidate is exactly **359 authored source/test lines** across five paths and remains uncommitted and unstaged: 164 lines in untracked `src/AIBar.Desktop/QuotaExportWindows.cs`, 144 lines in untracked `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`, 2 changed lines in `src/AIBar.Application/ClearAiBarDataService.cs`, 12 changed lines in `src/AIBar.Desktop/App.xaml.cs`, and 37 changed lines in `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`. Evidence: `sha256:6c7c9a9be9234e8b006d18f16b66c057122d6443d5ba0c45750c9b09fd548381`.
- **Observed outcome:** Static acceptance failed and **no test was run**. The candidate grants current user and SYSTEM `FullControl` and tests that same permissive descriptor instead of minimum rights; it does not prove concurrent readers observe only complete old/new documents. The clear/composition portion does not yet prove queued/in-flight coordination, stale-completion suppression, bounded disabled/null recreation or safe absence, writer-failure isolation in the clear child, or explicit disposal order. No 1b implementation, check, or commit task is complete.
- **Native state:** The failed Unit 1b native attempt was reset. The reset is procedural state only; it does not erase the failed evidence, authorize another attempt, or create completion credit.
- **1b-writer forecast:** **335–360 authored source/test lines** (approximately 175–190 production and 160–170 tests) in `src/AIBar.Desktop/QuotaExportWindows.cs` and `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`, plus only unavoidable project metadata. This includes replacing `FullControl` with protected minimum rights and adding real complete-reader atomicity coverage. Hard stop: **360**, never 400 and no `size:exception`.
- **1b-clear forecast:** **90–140 authored source/test lines** (approximately 20–40 production and 70–100 tests) in `src/AIBar.Application/ClearAiBarDataService.cs`, `src/AIBar.Desktop/App.xaml.cs`, and `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`. It owns exact-path clear coordination, stale suppression, disabled/null recreation or safe absence, composition, writer-failure isolation, and explicit disposal order. Hard stop: **160**.
- **Rollback boundaries:** Roll back 1b-clear only after proving the snapshot disabled/null or safely absent; remove its clear/composition wiring while retaining committed 1b-writer and all Application privacy behavior. Roll back 1b-writer by first securing/removing or atomically nulling the snapshot, then removing the writer while leaving 1a-privacy intact.

### Candidate-preservation recommendation — parent authorization required; do not execute during planning

Use one **named Git stash including untracked files**, scoped to the exact five candidate paths, only after explicit parent authorization. Do not create a WIP commit: it would pollute the feature-branch chain with a non-deliverable mixed candidate and could be mistaken for reviewed history.

1. From `feat/aibar-visual-yasb-1b`, create a stash named `aibar-visual-yasb-1b-preserved-359-6c7c9a9b` with `--include-untracked` and exact pathspecs for:
   - `src/AIBar.Desktop/QuotaExportWindows.cs`
   - `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`
   - `src/AIBar.Application/ClearAiBarDataService.cs`
   - `src/AIBar.Desktop/App.xaml.cs`
   - `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`
2. Immediately record the immutable stash object ID rather than relying on the movable `stash@{0}` label. Because the writer files were untracked, restore only those two paths from `<stash-oid>^3` into the worktree with `git restore --source=<stash-oid>^3 --worktree -- <writer-paths>`. Do not use `stash pop` or restore the clear trio on the writer child.
3. Implement, validate, and commit 1b-writer as the first child. Create 1b-clear from that committed child, then restore only the three tracked clear/composition paths from `<stash-oid>` with `git restore --source=<stash-oid> --worktree -- <clear-paths>`.
4. Keep the named stash and recorded object ID until both children are committed and their exact path scopes are verified. Any later stash removal is a separate parent-owned action.

**Tradeoff:** this keeps both child diffs honest and avoids a WIP commit, but a stash is local-only and easy to misreference if its ordinal moves. The recorded object ID, exact pathspecs, separate untracked-parent restore, and prohibition on `pop` are therefore mandatory. The stash operation temporarily removes the five candidate paths from the worktree, so it must not occur without explicit parent authorization.

**Next action:** `await parent authorization for the exact named-stash preservation and selective 1b-writer restoration plan`.

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
