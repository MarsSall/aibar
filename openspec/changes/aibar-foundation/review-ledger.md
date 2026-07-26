# Review Ledger

## Working-tree incident audit — 2026-07-25

Lens: reliability
Status: complete

No findings. `.gitignore` and `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` had metadata-only dirty entries; their working-tree bytes matched the index exactly. No executable implementation was retained.

## Judgment Day — Slice 8C1.1b1b — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-001 | judgment-day | `scripts/Publish-Deterministic.ps1:137-143` | CRITICAL | verified | Both blind judges verified that cancellation reaches tree termination without awaiting stream EOF and that the subsequent direct-process wait is bounded. |
| JD-002 | judgment-day | `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs:139-151,291-301` | CRITICAL | verified | Both blind judges verified bounded publisher completion and explicit child/grandchild exit assertions before cleanup, with bounds shorter than natural process timeouts. |
| JD-B-003 | judgment-day | `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs:139-147,243-251` | CRITICAL | refuted | The scoped re-judgment found its premises addressed by bounded `WaitAsync` and explicit descendant-exit assertions; both judges treated it as non-blocking. |
| JD-INFO-001 | judgment-day | `scripts/Publish-Deterministic.ps1:137-145` | WARNING | info | Both judges found redirected diagnostics are drained but discarded, reducing publish-failure diagnostics. This warning is informational and does not drive remediation. |

Round 1 verdict: two confirmed CRITICAL findings, one suspect CRITICAL finding, and one informational WARNING. `JUDGMENT: ESCALATED` pending maintainer approval for a fix round.

### Fix round 1 re-judgment

Both blind judges independently verified JD-001 and JD-002. JD-B-003 was refuted by the bounded test evidence; JD-INFO-001 remains informational.

Historical round verdict: `JUDGMENT: APPROVED` — it does not approve the current narrowed target.

Current target terminal state: `JUDGMENT: APPROVED`; `SDD: VERIFIED` for the narrowed Slice 8C1.1b1b contract.

## Judgment Day — Slice 8C1.1b2a — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-B2A-001 | judgment-day | `tools/AIBar.Packaging.Supervisor/Program.cs:9-11` | CRITICAL | verified | Both scoped re-judges verified stdin is now bounded during reading to 4097 bytes and over-limit input is rejected without requiring EOF or another read. |
| JD-B2A-002 | judgment-day | `tools/AIBar.Packaging.Supervisor/Protocol.cs:47,55` | CRITICAL | verified | Both scoped re-judges verified non-throwing integer conversion returns closed `INVALID_REQUEST` responses for overflow, underflow, and fractional protocol versions without stack traces or absolute paths. |

Round 1 verdict: two independently confirmed CRITICAL findings. No warnings or single-judge suspects were reported. Fixes require explicit maintainer approval before the first remediation round.

Fix round 1: JD-B2A-001 now bounds stdin during reads and rejects the 4097th byte before EOF; JD-B2A-002 uses non-throwing Int32 validation. Focused behavior tests passed; both findings await blind scoped re-judgment.

`JUDGMENT: ESCALATED`

## Judgment Day — Slice 8C1.1b2b Unit 1 — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-B2B-U1-001 | judgment-day | `tools/AIBar.Packaging.Supervisor/JobObjectInterop.cs:51-60`; `ProcessSupervisor.cs:50-63` | CRITICAL | verified | Both scoped re-judges verified exception-safe attribute-list construction, complete allocation/handle cleanup, and deterministic `PROCESS_START_FAILED` conversion through the production launch path. |
| JD-B2B-U1-002 | judgment-day | `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs:120-208`; `PackagingRecoveryTests.cs:113-134`; `apply-progress.md:26` | CRITICAL | verified | Both scoped re-judges verified the Windows test now traverses `ProcessSupervisor` and `WindowsProcessSupervisorInterop`, proves resume-gated execution and Job membership, and confirms helper termination when owned handles close. |
| JD-A-B2B-U1-002 | judgment-day | `tools/AIBar.Packaging.Supervisor/JobObjectInterop.cs:47`; `ProcessSupervisor.cs:53-56` | CRITICAL | info | Single-judge suspect: a failed `TerminateProcess` after assignment failure can leave an unassigned suspended process because the native result is discarded. Not independently confirmed; retained as non-blocking information. |

Round 1 verdict: two independently confirmed CRITICAL findings and one single-judge suspect signal. No Unit 2, Unit 3, or b2c behavior was reviewed.

`JUDGMENT: ESCALATED`

### Fix round 1 final scoped re-judgment

Both blind re-judges verified `JD-B2B-U1-001` and `JD-B2B-U1-002`. Focused tests passed 27/27, the solution built with zero warnings/errors, no helper process survived, and the final Unit 1 receipt is 329/400 review lines. The single-judge suspect remains informational and does not block this unit.

`JUDGMENT: APPROVED`

## Pre-commit reliability review — Slice 8C1.1b2b Unit 3

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| R3-B2B-U3-001 | reliability | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:90-117,157-161` | CRITICAL | verified | Scoped re-review verified deadline expiry is rechecked after the observation interval and before the decisive query; deterministic time-provider coverage proves it enters timeout containment instead of success. |
| R3-B2B-U3-002 | reliability | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:90-101,126-140` | CRITICAL | verified | Scoped re-review verified both pipe opens and drain startup are protected; failures contain/prove the Job, close partial ownership, dispose launch exactly once, and return truthful closed status. |

One general refuter evaluated the complete candidate list; both findings stand. Unit 3 must not be committed until they are fixed and scoped re-review verifies them.

### Fix Round 1

Only `R3-B2B-U3-001` and `R3-B2B-U3-002` were changed by this correction. Scoped reliability re-review verified both rows. Focused deterministic coverage passed 47/47, build passed with zero warnings/errors, `git diff --check` passed, no helper survived, and the final Unit 3 receipt is 383/400. The existing Unit 3 evidence and informational rows remain historical.

Pre-commit recommendation: APPROVED.

## Pre-commit reliability review — Slice 8C1.1b2b Unit 2

One exhaustive reliability sweep returned an empty findings ledger, including explicit triage of both single-judge informational signals. Unit 2 is approved for an isolated work-unit commit; Unit 3, b2c, and unrelated `.gitignore` remain excluded.

## Judgment Day — Slice 8C1.1b2b Unit 3 — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-B2B-U3-001 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:118-132` | CRITICAL | verified | Both scoped re-judges verified every failed post-containment repeated-zero proof becomes truthful `QUIESCENCE_UNPROVED` for cancellation and timeout paths. |
| JD-B2B-U3-002 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:158-161` | CRITICAL | verified | Both scoped re-judges verified streams are cancelled/disposed and the drain aggregate is unconditionally awaited before return, preventing post-return mutation or retained pipe ownership. |
| JD-B2B-U3-INFO-001 | judgment-day | `openspec/changes/aibar-foundation/apply-progress.md` | WARNING | info | Scoped re-judgment observed a one-line receipt discrepancy: persisted 310/400 versus current 309/400. Both remain safely below the review budget. |
| JD-A-B2B-U3-002 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:96-109` | CRITICAL | info | Single-judge suspect: zero confirmation can cross the deadline before its second query without rechecking timeout. Not independently confirmed; non-blocking. |
| JD-B-B2B-U3-002 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:89-125` | CRITICAL | info | Single-judge suspect: pipe/interop exceptions outside the protected region may bypass containment and disposal. Not independently confirmed; non-blocking. |
| JD-B-B2B-U3-004 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:125-132` | CRITICAL | info | Single-judge suspect: cancellation arriving after the final check but before terminal selection may still produce success. Not independently confirmed; non-blocking. |

Round 1 verdict: two independently confirmed CRITICAL findings and three single-judge suspect signals retained as non-blocking information. Unit 3 remains within its 187/400 review budget.

`JUDGMENT: ESCALATED`

### Fix Round 1 — maintainer-authorized correction

`JD-B2B-U3-001` now converts every failed post-containment authoritative repeated-zero proof to `QUIESCENCE_UNPROVED`, including cancellation and deadline-timeout paths. `JD-B2B-U3-002` now closes cancellation-resistant drains and awaits their completion before returning, so the result tail and owned pipes cannot outlive the observation. Focused tests passed 43/43, build passed with zero warnings/errors, `git diff --check` passed, and no helper survived. Both scoped re-judges verified the rows; the three single-judge suspects remain informational and unchanged.

`JUDGMENT: APPROVED`

## Pre-commit reliability review — Slice 8C1.1b2b Unit 1

One exhaustive reliability sweep returned an empty findings ledger. Unit 1 is approved for an isolated work-unit commit; planning-only re-slicing, Units 2/3, b2c, and unrelated `.gitignore` remain outside its implementation diff.

## Power-loss incident audit — Slice 8C1.1b2b Unit 2

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| R3-INC-U2-001 | reliability | `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs:200-220` | BLOCKER | verified | Final scoped review verified deterministic `Local\\` events and an explicit root-exited acknowledgement after descendant readiness; the corrected test passed 10/10 without increasing the timeout. |
| R3-INC-U2-002 | reliability | `openspec/changes/aibar-foundation/tasks.md:434-437` | CRITICAL | verified | Final scoped review verified the marks are backed by EOF-grace, advisory-packet, descendant, and real production-saturation evidence; the complete suite passed 33/33 repeatedly. |
| R3-INC-U2-003 | reliability | `openspec/changes/aibar-foundation/apply-progress.md:38-49` | CRITICAL | verified | Final scoped review verified the progress record accurately invalidates prior evidence and records the deterministic correction, repeated focused gates, clean build/diff, and zero helpers. |

One general refuter evaluated the complete candidate list; all three findings stand. Targeted repair is required before Unit 2 implementation can resume.

### Repair round evidence

Fresh RED reproduced 29/30 at descendant readiness. Fresh GREEN passed 33/33 after repairing anonymous-pipe drain mode and the Windows PowerShell helper, adding bounded EOF grace, packet-advisory variants, and a production event-gated 65,537-byte-per-stream saturation proof. Build/diff/process gates remain required for the Unit 2 receipt. These rows are **fixed**, not verified.

### Fix Round 2 — final persisted state

The remaining descendant harness ambiguity used implicit session event names and inferred root exit with a 100 ms delay. It now uses explicit `Local\\` names and a root-exited acknowledgement after descendant readiness; no production supervisor behavior changed. The prior readiness failure did not recur in 15 isolated pre-correction attempts; corrected readiness passed 10/10, the complete focused suite passed 33/33 three times plus the final 33/33 gate, build passed with 0 warnings/errors, `git diff --check` passed, and `ZERO_HELPER_COUNT=0`. Final scoped reliability review verified `R3-INC-U2-001..003`; the incident is closed.

## Judgment Day — Slice 8C1.1b2b Unit 2 — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-A-B2B-U2-001 | judgment-day | `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs:210-218`; `apply-progress.md:52,59` | CRITICAL | info | Single-judge suspect: the helper acknowledges imminent root exit before executing `exit`, so the descendant-delay assertion may occur before authoritative root termination. Not independently confirmed; non-blocking. |
| JD-B-B2B-U2-001 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:102-107`; `PackagingSupervisorTests.cs:253,271` | CRITICAL | info | Single-judge suspect: a non-positive observation interval uses `Task.Yield()`, which may not prove temporal separation between zero-active queries. Not independently confirmed; non-blocking. |

The blind judges returned no overlapping BLOCKER/CRITICAL finding. Both single-judge signals are retained as informational suspects under the Judgment Day convergence contract; they do not drive a fix round. Unit 2 remains within its 288/400 review budget.

`JUDGMENT: APPROVED`

### Fix Round 1 — maintainer-authorized correction

`JD-B2B-U1-001` now checks both attribute-list initialization phases, releases initialized/uninitialized list memory and inherited-handle storage on every constructor failure, and converts launch exceptions to `ProcessStartFailed` after disposing all locally owned resources. `JD-B2B-U1-002` now has a Windows-native event-gated test through `ProcessSupervisor` and `WindowsProcessSupervisorInterop`: the helper signals after resume, is observed in its Job, and exits when the launch is disposed. Focused RED failed with the unhandled launch exception; focused GREEN and native coverage passed. Both rows are `fixed`, pending scoped re-judgment; no Unit 2/3 or b2c semantics are claimed.

## Pre-commit reliability review — Slice 8C1.1b2a

One exhaustive reliability sweep returned an empty findings ledger. The final b2a work unit is approved for an isolated commit; unrelated `.gitignore`, pre-existing planning changes, and b2b/b2c remain excluded.

## Judgment Day — Slice 8C1.1b2b — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-B2B-001 | judgment-day | `tools/AIBar.Packaging.Supervisor/JobObjectInterop.cs:13-41` | CRITICAL | wont-fix | Both blind judges confirmed no production implementation of `IProcessSupervisorInterop` exists. Native launch, explicit inherited-handle control, suspended assignment/resume ordering, completion-port association, concurrent pipe drains, exit retrieval, active-process queries, and owned handle lifetimes are declared or faked but not wired into an executable supervisor. The rejected implementation was rolled back for replanning, so the defective code no longer exists. |
| JD-B2B-002 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:24-38` | CRITICAL | wont-fix | Both blind judges confirmed cancellation and timeout terminate and return without the required bounded repeated post-termination `ActiveProcesses == 0` proof, so callers can receive a terminal result while descendants remain alive. The rejected implementation was rolled back for replanning, so the defective code no longer exists. |
| JD-B2B-003 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:35` | CRITICAL | wont-fix | Both blind judges confirmed an active-process query failure returns `QuiescenceUnproved` without terminating containment or establishing disposal/handle ownership, allowing descendants to outlive the supervision call. The rejected implementation was rolled back for replanning, so the defective code no longer exists. |
| JD-A-B2B-004 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:39-40` | CRITICAL | info | Single-judge suspect: post-quiescence EOF waiting ignores cancellation and can misclassify a late cancellation. Not independently confirmed; non-blocking until fix-touched behavior is reviewed. |
| JD-B-B2B-003 | judgment-day | `tools/AIBar.Packaging.Supervisor/ProcessSupervisor.cs:21-23,39-43` | CRITICAL | info | Single-judge suspect: abandoned or timed-out drain tasks can retain pipes and handles. Not independently confirmed; non-blocking until fix-touched behavior is reviewed. |

Round 1 verdict: three independently confirmed CRITICAL findings and two single-judge suspect signals retained as non-blocking information. The checked b2b tasks and apply evidence currently overstate completion. The rejected implementation was rolled back for replanning.

Historical Round 1 terminal state: `JUDGMENT: ESCALATED` — it does not approve any future b2b implementation.

### Fix round 1 scoped re-judgment

Both blind judges verified `JD-B2A-001` and `JD-B2A-002`; no defect remains open. The terminal judgment remains escalated because the judges disagreed on review-budget accounting and evidence sufficiency: Judge A calculated 381 changed lines and approved, while Judge B calculated 481 candidate lines and requested a clean b2a-only receipt plus explicit post-fix build evidence.

Independent receipt (2026-07-26): count tracked paths with `git diff --numstat`, untracked b2a files with `git diff --no-index --numstat NUL <path>`, solution semantics with `git diff --ignore-space-at-eol --numstat -- AIBar.sln`, and the four task completions against the approved planning baseline as four additions plus four deletions. The final b2a gross is 504 lines: 412 executable/project/test, 50 pre-existing task-planning delta, 21 apply evidence, and 21 b2a ledger lines. The review-relevant total is 362: 312 executable/project/test after excluding 100 line-ending-only solution lines, plus 8 task-state lines, 21 apply-evidence lines, and 21 ledger lines. Separate non-b2a workspace deltas are 61 design lines, 83 spec lines, and 28 unrelated design-ledger lines; `.gitignore` has 0 normalized content lines and its filtered hash matches `HEAD`. Focused tests passed 15/15, the fresh post-fix solution build passed with 0 warnings/errors, and `git diff --check` passed with advisory line-ending warnings only. The b2a work unit is therefore within the 400-line review budget; b2b/b2c remain pending.

`JUDGMENT: APPROVED`

## Judgment Day — Slice 8C1.1b2 Job Object design — Round 1

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-DESIGN-001 | judgment-day | `design.md:220,235` | CRITICAL | verified | Both re-judges verified completion messages are advisory and bounded repeated `ActiveProcesses == 0` queries with live handles are authoritative. |
| JD-DESIGN-002 | judgment-day | `design.md:227-233` | CRITICAL | verified | Both final re-judges verified the quarantine rename is irreversible and every post-commit failure becomes `CLEANUP_PARTIAL` with retained quarantine and bounded retry/scavenger metadata. |
| JD-A-DESIGN-002 | judgment-day | `design.md:223,264` | CRITICAL | info | Single-judge suspect: the generic argument array may need an operation-specific typed allowlist; it was not independently confirmed and is non-blocking. |
| JD-B-DESIGN-002 | judgment-day | `design.md:219,225` | CRITICAL | info | Single-judge suspect: assignment failure and root exit retrieval may need additional retained-handle interop; it was not independently confirmed and is non-blocking. |
| JD-B-DESIGN-003 | judgment-day | `design.md:220,223,229` | CRITICAL | info | Single-judge suspect: the production cancellation channel needs clarification; it was not independently confirmed and is non-blocking. |
| JD-DESIGN-INFO-001 | judgment-day | `design.md:214,219,220,231` | WARNING | info | Job membership excludes pre-existing build servers and broker/service-mediated process creation; the threat model and worker configuration must state this limitation. |
| JD-DESIGN-INFO-002 | judgment-day | `design.md:231-235` | WARNING | info | The proposed cleanup/scavenger child lacks a defensible under-400-line forecast and likely requires another split. |

Fix round 1: JD-DESIGN-001 and JD-DESIGN-002 are fixed pending scoped re-judgment. The three suspect CRITICAL findings and two informational warnings are unchanged.

### Fix round 1 re-judgment

JD-DESIGN-001 is verified. JD-DESIGN-002 remains confirmed open because one post-commit branch still uses pre-commit refusal semantics.

`JUDGMENT: ESCALATED`

`JUDGMENT: ESCALATED`

### Fix round 2 final re-judgment

Both blind judges verified JD-DESIGN-002. JD-DESIGN-001 remains verified; single-judge suspects remain recorded as non-blocking information.

`JUDGMENT: APPROVED`

## Judgment Day — narrowed-scope cleanup

Both blind judges returned empty findings ledgers. They independently confirmed that valid lifecycle and saturated-stream coverage remain, the deferred hardening item is truthful, and the prior suspect findings are correctly marked `wont-fix` following explicit maintainer-approved scope reduction.

`JUDGMENT: APPROVED`

## Pre-commit reliability review

One exhaustive reliability sweep returned an empty findings ledger. Commit may proceed with the six scoped b1b files; `.gitignore` remains excluded.

## Judgment Day — Slice 8C1.1b1b final tests

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-A-FINAL-001 | judgment-day | `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` (removed deferred test) | CRITICAL | wont-fix | Maintainer explicitly narrowed b1b: the sentinel was outside recovery output, so this invalid test was removed. The requirement is deferred as `B1B-HARDEN-NONZERO-PARTIAL-OUTPUT`; it is not a current b1b acceptance requirement. |
| JD-B-FINAL-001 | judgment-day | `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` (removed deferred test) | CRITICAL | wont-fix | Maintainer explicitly narrowed b1b: the test could not distinguish exit code 7 from cancellation, so this invalid test was removed. The requirement is deferred as `B1B-HARDEN-NONZERO-PARTIAL-OUTPUT`; it is not a current b1b acceptance requirement. |

The valid saturation test remains in scope. The maintainer explicitly reduced b1b scope and deferred nonzero-exit-with-partial-output preservation as `B1B-HARDEN-NONZERO-PARTIAL-OUTPUT`; no replacement proof is claimed in this slice.

`JUDGMENT: APPROVED`; `SDD: VERIFIED` for the narrowed Slice 8C1.1b1b contract. Deferred `B1B-HARDEN-NONZERO-PARTIAL-OUTPUT` remains excluded from this approval.

## Judgment Day — Slice 8C1.1b1b remediation — Round 2

| id | lens | location | severity | status | evidence |
|---|---|---|---|---|---|
| JD-R2-INFO-001 | judgment-day | `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs:153-164` | WARNING | info | Both judges confirmed saturated-stream coverage remains absent, truthfully disclosed, and unclaimed. |
| JD-R2-INFO-002 | judgment-day | `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs:153-164`; `openspec/changes/aibar-foundation/tasks.md:408-411` | WARNING | info | Both judges confirmed nonzero-with-partial-output coverage remains absent, truthfully disclosed, and unclaimed. |

No BLOCKER or CRITICAL defect was found in the remediation. PID/start-tick identity checks and fail-closed behavior were independently validated within their stated boundary.

Historical round verdict: `JUDGMENT: APPROVED` — it does not approve the current narrowed target.

Current target terminal state: `JUDGMENT: APPROVED`; `SDD: VERIFIED` for the narrowed Slice 8C1.1b1b contract.
