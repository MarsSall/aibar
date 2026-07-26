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

## Pre-commit reliability review — Slice 8C1.1b2a

One exhaustive reliability sweep returned an empty findings ledger. The final b2a work unit is approved for an isolated commit; unrelated `.gitignore`, pre-existing planning changes, and b2b/b2c remain excluded.

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
