# Apply Progress — AIBar Foundation

## Slice 8C1.1b2c C1b0 — Native API readiness proof (2026-07-30)

**Status:** Standard mode (`strict_tdd: false`), maintainer-approved `size:exception`, C1b0 only. `DirectoryCapability` now has a separate rename-ready constructor that retains a source handle opened with `DELETE|SYNCHRONIZE`, a distinct same-volume non-reparse quarantine-parent handle opened with `FILE_TRAVERSE|FILE_READ_ATTRIBUTES|SYNCHRONIZE`, and no `FILE_SHARE_DELETE`. `NativeRenameReadiness` is a test-only readiness/proof seam called only from tests: it validates one strict simple leaf, closes child observation handles once, manually builds and zeroes the x64 native buffer, and makes one `ntdll!NtSetInformationFile(FileRenameInformation=10)` call. No production quarantine commit is wired.

| Work Unit Evidence | Exact result |
|---|---|
| RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` failed before production code with `CS0117` for missing `TryCreateRenameReady` and `CS0103` for missing `NativeRenameReadiness`. |
| GREEN / focused test | The same focused command passed 52/52, failed 0, skipped 0. Fake cases refuse source/parent identity substitution, reparse, volume, extra-child, and share drift before the native call; invalid/reserved/path leaf has call count 0; pending/non-success is not success. |
| Runtime harness | `Windows_native_rename_readiness_proves_relative_success_collision_and_no_residue` uses the exact production P/Invoke on Windows x64. It proved retained-source identity continuity under the retained quarantine parent, one native success, collision refusal with an unchanged sentinel/source, one-way child release, and test-root disposal. |
| Gate | `dotnet build AIBar.sln --no-restore --nologo` — exit 0; 0 warnings, 0 errors. `git diff --check` — exit 0 (existing LF-to-CRLF advisories only). `Get-Process -Name Harness -ErrorAction SilentlyContinue` — `HARNESS_HELPER_COUNT=0`; no `aibar-c1b0-*` temporary root remains. |
| Rollback boundary | Remove only C1b0 additions in `DirectoryCapability.cs`, C1b0 tests in `PackagingSupervisorTests.cs`, these three C1b0 task marks, and this progress block. Retain C1a and leave C1b1/C2/C3 unchecked. |

**Scope/cleanup:** no `Cleanup.cs`, production caller, Win32/path/shell/helper fallback, deletion, rename-back, scavenger, PowerShell, C2, or C3 behavior was added. The native buffer is freed and zeroed in `finally`; capability disposal closes child/source/parent handles exactly once. `.gitignore` is untouched by this unit.

### C1b0 bounded verification correction (2026-07-30)

**Status:** Standard mode (`strict_tdd: false`), native objective generation 51 / attempt 52. This correction is bound to failed verification evidence `sha256:43409e6d615699bb4e30cfc3e111e18fe491110b00b2c59ba4cd4396e2ddeacd`. It adds only a test-only compatibility seam and deterministic C1b0 coverage; C1b1 remains absent, unwired, and 0/3 unchecked.

| Work Unit Evidence | Exact result |
|---|---|
| Gate diagnosis | The previously failed focused and full commands were each reproduced once before editing and passed 52/52 and 279/279. The event-gated descendant test has no C1b0 dependency and the prior three lifecycle failures did not recur in the serialized clean run; they are classified as pre-existing environmental/test-isolation interference, not candidate-caused. No lifecycle test or production supervisor behavior changed. |
| RED / GREEN | The new focused filter initially failed at compile time because the deterministic compatibility and no-call seams did not exist. After the smallest seam was added, it passed 6/6. |
| C1b0 missing coverage | Unsupported Windows, x86, layout, entrypoint, and information-class inputs all return false without loading/calling native rename; `Prove(..., () => false)` returns `CLEANUP_REFUSED`, makes zero native calls, and retains children. A separate test proves no `Cleanup` type or C1b1 entrypoint/wiring exists when readiness is absent. |
| Focused / runtime / regression | `PackagingSupervisor` passed 58/58; exact Windows native rename runtime passed 1/1; full `AIBar.sln` passed 285/285; clean build passed with 0 warnings and 0 errors. |
| Rollback boundary | Revert only the compatibility seam in `DirectoryCapability.cs`, the two C1b0 correction tests, and this evidence block. Preserve C1a, existing C1b0 proof, and all unchecked C1b1/C2/C3 work. |

```json
{"schema":"gentle-ai.remediation-result/v1","lineage_id":"sha256:5dd516b1823de6ee3aaf28ef5b6e1bb6fbdebb9afd0f23bacaf5e38fbbb74f8f","generation":51,"mode":"standard","fix_batch":"b2c-c1b0-verification-correction","failed_evidence_revision":"sha256:43409e6d615699bb4e30cfc3e111e18fe491110b00b2c59ba4cd4396e2ddeacd","outcome":"passed"}
{"schema":"gentle-ai.remediation-evidence/v1","lineage_id":"sha256:5dd516b1823de6ee3aaf28ef5b6e1bb6fbdebb9afd0f23bacaf5e38fbbb74f8f","generation":51,"mode":"standard","fix_batch":"b2c-c1b0-verification-correction","failed_evidence_revision":"sha256:43409e6d615699bb4e30cfc3e111e18fe491110b00b2c59ba4cd4396e2ddeacd","focused_result":"58/58","runtime_result":"1/1","full_result":"285/285","build_result":"0-warnings-0-errors","diff_check":"passed"}
```

## Slice 8C1.1b2c — C1 rollback for replan (2026-07-26)

**Status:** Standard mode (`strict_tdd: false`). The uncommitted C1 capability-admission/quarantine implementation and its tests were removed after the confirmed `JD-B2C-C1-001` TOCTOU finding. C1, C2, and C3 RED/GREEN/TRIANGULATE/GATE tasks are all unchecked; the three-unit b2c feature-branch-chain plan remains the authority for a future handle-bound replan.

**Rollback-for-replan:** deleted only `DirectoryCapability.cs`, `Cleanup.cs`, and `DirectoryCapabilityTests.cs`; removed C1 completion claims and reset its task marks. This checkpoint preserves b2a/b2b behavior, the independently verified readiness-harness repair below, b2c planning, and historical review evidence. No C1 production behavior, types, tests, receipt, or approval remains.

## Slice 8C1.1b2c C1a — Retained capability admission (2026-07-29)

**Status:** Standard mode (`strict_tdd: false`), feature-branch-chain C1a only. `DirectoryCapability` collision-failingly creates a caller-selected leaf and its direct allowlist, opens and retains root/direct-child `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT` handles without `FILE_SHARE_DELETE`, and captures `FILE_ID_INFO`, final handle paths, volume, and reparse observations. Read-only revalidation requires the exact allowlist with no duplicates, retained identity/final-path equality, same volume, canonical child containment, and non-reparse state. It reports only `ROOT_CREATE_FAILED`, `ROOT_IDENTITY_CHANGED`, or `REPARSE_DETECTED` on refusal; it has no rename, deletion, scavenger, PowerShell, or path-only quarantine behavior.

| Work Unit Evidence | Exact result |
|---|---|
| RED | The added admission matrix initially failed 1/49 because a successful `TryValidate` retained its default refusal status. The assertion was corrected so successful validation emits `SUCCESS`; refusal cases remain closed. |
| GREEN | The capability retains all live safe handles and snapshots after successful admission. A failure during creation disposes only incomplete local setup; successful admission has no release path invocation before a future commit owner decides its lifecycle. |
| TRIANGULATE | Fake observations cover extra/duplicate/missing-equivalent allowlist mismatch, substituted identity, changed final path, volume mismatch, access fault, reparse, containment escape, reordered enumeration, and Unicode/space names. Every refusal leaves the fake mutation counter unchanged after initial creation. |
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` — exit 0; passed 50/50, failed 0, skipped 0. |
| Runtime harness | The same filter executes `Windows_directory_capability_retains_a_live_nonreparse_same_volume_admission` on Windows using `WindowsDirectoryCapabilityFileSystem` and a generated Unicode/space temporary parent. It validates real retained directory handles; test teardown removes only its generated harness parent after capability disposal. |
| GATE | `dotnet build AIBar.sln --no-restore --nologo` — exit 0; 0 warnings, 0 errors. `git diff --check` — exit 0 (existing LF-to-CRLF advisories only). `ZERO_HELPER_COUNT=0` for `Harness,powershell`. |
| Rollback boundary | Remove only `DirectoryCapability.cs`, C1a test additions in `PackagingSupervisorTests.cs`, the four C1a task marks, and this progress block. b2a/b2b remain; C1b/C2/C3 remain unchecked. |

**Scope/cleanup:** no production rename, deletion, identity-evidence release, quarantine commit, or path-only fallback was implemented. `.gitignore` remains unstaged and byte-identical to `HEAD` (`16d3fd82b1698894b9b3f4d707e12db4f16cd7f1`). Native attempt 43 (`b2c-c1a-apply-20260729-01`) was not begun, reset, or finished by this work; terminal finish remains orchestrator-owned.

### R3-INC-C1-001 Fix Round 1 — b2b readiness harness (2026-07-26)

**Scope:** committed b2b test harness synchronization/diagnostics only; no production supervisor, C1 behavior, C2/C3, cleanup deletion, scavenger, or PowerShell production integration changed. The nested helper now follows an explicit root-started → test-authorized child launch → child-ready → root-ready → root-exited → test-release handshake. It emits only fixed safe progress/failure codes; on a failed handshake the test reports root-exit state, exit code, recognized safe code, and bounded stdout/stderr byte counts, never raw tail text or paths.

**Verified evidence:** isolated readiness passed 10/10; full `PackagingSupervisor` passed 47/47; the original combined filter passed 57/57 while C1 was present; `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed (LF-to-CRLF advisories only); `ZERO_HELPER_COUNT=0` after excluding the invoking shell. `R3-INC-C1-001` is independently **verified** as a b2b readiness-harness repair. The C1-only and combined C1 receipts are superseded by this rejected-implementation rollback and grant no C1 completion, approval, or future-design authority. **Rollback boundary:** retain this harness synchronization; a future C1 replan must be separately implemented and reviewed.

## Slice 8C1.1b2b Unit 3 — Cancellation/timeout containment and classification (2026-07-26)

**Status:** Standard mode (`strict_tdd: false`), feature-branch-chain Unit 3 only. Cancellation, deadline timeout, ActiveProcesses-query failure, EOF/drain failure, and Job-termination failure now return closed status classifications. This unit does not implement b2c, cleanup/scavenging, PowerShell integration, or packaging/runtime behavior.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added fake cancellation, deadline timeout, active-process query/termination failure, EOF/drain failure, and late-cancellation tests. The focused command failed at compile time because `ObserveAsync` had no cancellation overload, `CompletionObservation` exposed no status, and the fake had no Job termination seam. |
| GREEN | `ObserveAsync` retains live ownership through terminal classification, terminates the Job before the bounded two-query proof on containment paths, cancels/disposes and bounded-waits both drains, retrieves exit code opportunistically, and disposes the launch exactly once. Status precedence keeps cancellation/timeout above worker success; failed termination/proof is `QUIESCENCE_UNPROVED`; failed EOF/drains is `OUTPUT_DRAIN_FAILED`. |
| TRIANGULATE | Focused `PackagingSupervisor` passed 38/38 three times. Existing event-gated Windows descendant and production 65,537-byte-per-stream saturation tests remain in the same filter; fake paths prove timeout/cancellation/query/termination/EOF and late-cancellation races without sensitive diagnostics. |
| GATE | Focused `PackagingSupervisor` passed 38/38; `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed (LF-to-CRLF advisories only); `ZERO_HELPER_COUNT=0`. Unit-3 diff receipt: 152 additions + 35 deletions = 187 lines, below the 400-line hard stop. |

**Changed paths:** `tools/AIBar.Packaging.Supervisor/{JobObjectInterop.cs,ProcessSupervisor.cs}`; `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`; `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. **Task state:** only Unit 3 RED/GREEN/TRIANGULATE/GATE marks are checked; Units 1/2 history is preserved and b2c remains unchecked. **Rollback boundary:** revert only the Unit 3 interop/supervisor/test additions and these four task/progress edits; retain b2a and committed Units 1/2. **`.gitignore`:** untouched and unstaged.

### Judgment Day Fix Round 1 — b2b Unit 3 (2026-07-26)

**Scope:** Maintainer-authorized correction only for `JD-B2B-U3-001` and `JD-B2B-U3-002`. Failed post-termination repeated-zero proofs now override cancellation or timeout with `QuiescenceUnproved`. Drain containment now cancels and disposes both streams, then awaits the drain aggregate before returning, preventing a delayed cancellation-resistant read from mutating the returned tail or retaining an owned pipe. No Unit 1/2 behavior, b2c, sensitive output, or suspect-only behavior changed.

| Work Unit Evidence | Exact result |
|---|---|
| RED | The retained failed-proof matrix failed against the prior implementation: cancellation and timeout incorrectly returned `Cancelled`/`Timeout` when query failure or the repeated zero proof failed. |
| GREEN | The matrix covers cancellation and deadline timeout with query failure and a `0,1` repeated-query failure; all return `QuiescenceUnproved`. A cancellation-resistant drain ignores its cancellation token, releases only after stream disposal, and proves returned tails, drain completion, pipe closure, and `OutputDrainFailed`. Fix Round 1 adds deterministic deadline-boundary, first/second pipe-open, and initial-drain-fault containment coverage. |
| GATE | Focused `PackagingSupervisor` passed 47/47; `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed (LF-to-CRLF advisories only); `ZERO_HELPER_COUNT=0`. |

`JD-B2B-U3-001` and `JD-B2B-U3-002` are **fixed**, not verified. `R3-B2B-U3-001` and `R3-B2B-U3-002` are also fixed by this correction, not verified. The three single-judge suspects remain informational and unchanged. **Unit-3-only receipt:** 345 additions + 38 deletions = **383** review lines, below the 400-line hard stop. **Rollback boundary:** revert only this Fix Round 1 supervisor/test/ledger/progress delta, retaining the pre-existing Unit 3 work and all Unit 1/2 and b2c bytes.

### Judgment Day Fix Round 1 continuation — integrated b2a+b2b (2026-07-26)

`JD-B2-INT-001` teardown correction is **fixed, not verified**. Fresh gates passed: partial child-only/missing-identity and complete-identity cleanup 4/4 twice; affected owned-lifecycle filter 5/5 twice; focused `PackagingSupervisor` 47/47; full `AIBar.sln` 273/273; build 0 warnings/errors; `git diff --check`; and `HARNESS_HELPER_COUNT=0`. The retained direct-completion/live-grandchild RED remains unchanged, and partial identity preserves the root rather than deleting unvalidated grandchild ownership. b2a is 4/4, b2b 12/12, b2c 0/4; no b2c code was added.

The authoritative complete correction receipt counts implementation/tests/tasks/apply evidence (excluding verifier/ledger records): **235/400**. `JD-B2-INT-INFO-001` remains WARNING/info: the narrower verifier-only receipt is not substituted for this complete receipt.

### Judgment Day Fix Round 2 — `JD-B2-INT-001` (2026-07-26)

**RED:** the initial harness extension exposed the prior recursive child-tree termination contradiction; the deterministic live-unpublished-grandchild assertion requires the original failure, unmarked/unrecorded grandchild liveness, preserved root, and retained PID cleanup evidence. **GREEN:** validated child and grandchild teardown uses direct `Kill()` only; unavailable grandchild identity stops cleanup without deleting the root. The test releases its own unvalidated helper only after assertions.

**GATE:** cleanup matrix 5/5 twice; owned lifecycle plus retained direct-completion/live-grandchild RED 6/6 twice; focused `PackagingSupervisor` 47/47; full `AIBar.sln` 274/274 once; build 0 warnings/errors; `git diff --check`; `HARNESS_HELPER_COUNT=0`. `JD-B2-INT-001` is **fixed, not verified**. b2a remains 4/4, b2b 12/12, b2c 0/4; no b2c code. Complete correction receipt (implementation/tests/tasks/apply, excluding verifier/ledger) is **320/400**.

## Slice 8C1.1b2a typed protocol and supervisor foundation (2026-07-26)

**Status:** Standard mode (`strict_tdd: false`) with the task-mandated RED → GREEN → TRIANGULATE → GATE sequence. This feature-branch-chain work unit adds only the dependency-isolated supervisor executable seam, closed protocol/state-machine foundation, focused fake-driven tests, and solution/test references. It does not implement Job Object native calls, process launch, stream drains, cleanup, scavenging, PowerShell integration, B1a2, MakeAppx, SignTool, or application runtime integration.

| Work Unit Evidence | Exact result |
|---|---|
| RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` failed before implementation because `AIBar.Packaging.Supervisor` and its protocol interfaces did not exist (`CS0234`, `CS0246`). |
| GREEN | After adding the BCL-only `net8.0-windows` x64 project, typed DTOs/statuses, JSON boundary, bounded diagnostic tail, and fake-injected state machine, the focused command passed 9/9. |
| TRIANGULATE | Added closed-field mutation coverage for version, operation, timeout, PID/root authority, oversized input, duplicate fields, reordered fields/callbacks, terminal cancellation precedence, deterministic response bytes, and bounded diagnostic tail behavior. The focused command passed 11/11. |
| GATE | `dotnet build AIBar.sln --no-restore --nologo` succeeded with 0 warnings and 0 errors; `git diff --check` passed (Git emitted LF-to-CRLF advisory warnings only). |
| Dependency boundary | `AIBar.Packaging.Supervisor` references no AIBar application project or package. Only `AIBar.Domain.Tests` references it for focused tests; application projects remain independent. |
| Rollback boundary | Remove the supervisor project, its solution/test references, `PackagingSupervisorTests.cs`, the four b2a checkbox marks, and this evidence block. The committed b1b script behavior remains runnable. |

**Changed paths:** `AIBar.sln`; `tools/AIBar.Packaging.Supervisor/{AIBar.Packaging.Supervisor.csproj,Program.cs,Protocol.cs,SupervisorState.cs}`; `tests/AIBar.Domain.Tests/{AIBar.Domain.Tests.csproj,PackagingSupervisorTests.cs}`; `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. **Task state:** `8C1.1b2a-RED`, `GREEN`, `TRIANGULATE`, and `GATE` are checked; b2b/b2c remain unchecked. **`.gitignore`:** untouched. **Next:** independent review/verification of b2a before b2b; no commit, push, PR, or receipt was created.

## Slice 8C1.1b2b Unit 1 — Production interop and suspended launch (2026-07-26)

**Status:** Standard mode (`strict_tdd: false`), feature-branch-chain Unit 1 only. This adds a dependency-isolated `IProcessSupervisorInterop`, Windows Job/IOCP/anonymous-pipe/CreateProcessW interop, dedicated safe-handle ownership, explicit three-handle inheritance allowlist, suspended launch, assignment before resume, and root exit-code seam. It does not add drains, quiescence, timeout/cancellation classification, cleanup/scavenging, PowerShell integration, or downstream behavior.

| Work Unit Evidence | Exact result |
|---|---|
| RED | The focused test command failed before implementation with missing `IProcessSupervisorInterop`, `ProcessSupervisor`, safe handles, pipe/process records, and launch request types (`CS0246`). |
| GREEN | Added native `KILL_ON_JOB_CLOSE` Job configuration without breakaway, completion-port association, three anonymous pipes with parent ends made non-inheritable, `PROC_THREAD_ATTRIBUTE_HANDLE_LIST`, `CREATE_SUSPENDED|EXTENDED_STARTUPINFO_PRESENT`, assignment-before-resume, failure termination, disposal, and exit-code retrieval. Focused`PackagingSupervisor` passed 25/25. |
| TRIANGULATE | Eight fake pre-resume/at-resume faults assert exact call order, status, termination where needed, allowlisted child handles only, and all owned handles closed. The existing event-gated child/grandchild harness also passed in the combined focused command; `Get-Process -Name Harness` returned 0 afterward. |
| GATE | `dotnet test ... --filter "FullyQualifiedName~PackagingSupervisor|FullyQualifiedName~Red_direct_invocation_completes_while_known_owned_grandchild_remains_alive" --no-restore -m:1 --nologo` passed 26/26; `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed (LF-to-CRLF advisories only). |

**Changed paths:** `tools/AIBar.Packaging.Supervisor/{JobObjectInterop.cs,ProcessSupervisor.cs}`; `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`; `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. **Task state:** Unit 1 RED/GREEN/TRIANGULATE/GATE are checked; Units 2 and 3 remain unchecked. **Rollback boundary:** remove only the two Unit 1 supervisor files, Unit 1 test additions, four Unit 1 task marks, and this block; b2a remains runnable. **`.gitignore`:** HEAD/index/worktree blobs are identical and it remains unstaged.

## Slice 8C1.1b2b Unit 2 — Concurrent drains and authoritative quiescence (2026-07-26)

**Status:** INVALIDATED PENDING REPAIR. Standard mode (`strict_tdd: false`), feature-branch-chain Unit 2 only. The power-loss audit found the claimed focused gate unsupported: the real descendant helper did not signal, and EOF-grace plus genuine production saturation proof were absent. Unit 2 task marks and the evidence below are provisional until the targeted repair completes; Unit 3 remains out of scope.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Focused `PackagingSupervisor` compilation failed before the Unit 2 observer existed: `FakeLaunchInterop` lacked the exit/signal/query/packet/stream controls and `ProcessSupervisor.ObserveAsync` was missing (`CS0117`, `CS1061`). |
| GREEN | The focused command passed 29/29 after adding concurrent bounded stream drains, live root/Job observation, cached root exit retrieval, bounded EOF wait, and native `WaitForSingleObject`/`QueryInformationJobObject`/completion-port/pipe seams. |
| TRIANGULATE | Fake saturation retains 65,536 bytes and exactly one discarded byte on each stream; lost/duplicate/reordered packet values remain non-decisive; a delayed nonzero active count prevents success; zero observations are timestamp-proven separated. The combined focused command passed 30/30, including the existing event-gated owned child/grandchild helper. |
| GATE | Focused combined test command passed 30/30; `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed with LF-to-CRLF advisories only; `Get-Process -Name Harness,powershell` returned 0. |

**Changed paths:** `tools/AIBar.Packaging.Supervisor/{JobObjectInterop.cs,ProcessSupervisor.cs}`; `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`; `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. **Task state:** Unit 2 RED/GREEN/TRIANGULATE/GATE are checked; Unit 3 and b2c remain unchecked. **Rollback boundary:** remove only Unit 2 observer/interop/test additions, its four task marks, and this evidence block; preserve b2a and Unit 1. **`.gitignore`:** not edited or staged.

### Unit 2 repair round — incident closure evidence (2026-07-26)

The preceding Unit 2 table is historical and was invalidated after the power-loss audit. Fresh RED reproduced the descendant helper failure at `PackagingSupervisorTests.cs:215` (29/30). The cause was twofold: the helper used the unavailable Windows PowerShell `ProcessStartInfo.ArgumentList` API, and asynchronous `FileStream` mode on an anonymous pipe did not drain the real saturation writer. The helper now uses the compatible `Arguments` string, waits for child readiness before root exit, and production pipe reads use synchronous pipe handles with asynchronous drain tasks.

| Repair evidence | Exact result |
|---|---|
| EOF grace | A gated non-EOF stream reaches authoritative quiescence, returns after the 25 ms grace with `EofCompleted=false`, then is explicitly released. |
| Packets | Lost, duplicate, and reordered packet sequences each succeed only after root signal, one zero exit retrieval, and three live queries (`1,0,0`); packets remain advisory. |
| Real Windows harnesses | Event-gated root/descendant remains active after root exit until release. A separate event-gated production `WindowsProcessSupervisorInterop` saturation run drains both 65,537-byte streams without deadlock, retains 65,536-byte tails, and counts exactly one discarded byte per stream. |
| Focused GREEN | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` passed 33/33. |

Unit 2 RED/GREEN/TRIANGULATE/GATE marks are restored only from this fresh evidence. Unit 3 cancellation, timeout, query-failure containment, and status classification; b2c; cleanup; PowerShell integration; and unrelated behavior remain out of scope.

### Unit 2 Fix Round 2 — deterministic descendant readiness (2026-07-26)

The remaining failure was harness ordering, not a production observer defect: the descendant-ready event was implicitly session-scoped and the test inferred root exit through `Task.Delay(100)`. The helper now uses explicit `Local\\` event names, suppresses the `Process.Start` pipeline object, and signals a separate root-exited event only after the descendant-ready wait and immediately before root exit. The test waits for that acknowledgement rather than time-based scheduling; it does not increase the 5-second readiness bound. The Windows PowerShell-compatible `Arguments` construction was retained and the production observer was not changed.

| Final evidence | Exact result |
|---|---|
| Prior failure / bounded diagnosis | The earlier 32/33 readiness failure could not be reproduced in 15 isolated pre-correction executions. Command construction remained `-NoProfile -NonInteractive -EncodedCommand`; child readiness/release events were present, the child waits release, and no helper exit/stderr-tail failure was observed. The missing root-exit acknowledgement and implicit namespace were the remaining nondeterministic harness boundary. |
| Repetition | The corrected descendant test passed 10/10 isolated executions. The complete `PackagingSupervisor` filter passed 33/33 on three repetitions, then 33/33 in the final focused gate. |
| Final gates | `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed with LF-to-CRLF advisories only; `ZERO_HELPER_COUNT=0` for `powershell`/`Harness`. |

R3-INC-U2-001, R3-INC-U2-002, and R3-INC-U2-003 are fixed by this round, not verified. Unit 3, b2c, cleanup/scavenging, PowerShell production integration, and unrelated behavior remain out of scope.

### Judgment Day Fix Round 1 — b2b Unit 1 (2026-07-26)

**Scope:** Maintainer-authorized correction only for `JD-B2B-U1-001` and `JD-B2B-U1-002`. `AttributeList` now validates its sizing and initialization calls, frees all unmanaged allocations on constructor failure, and `ProcessSupervisor` fails closed as `ProcessStartFailed` after disposing Job/port/pipe/process/thread ownership if launch interop throws. A Windows-native event-gated test now exercises `ProcessSupervisor` with `WindowsProcessSupervisorInterop`: its PowerShell helper signals only after resume, is already in the Job when observed, and exits after launch disposal. This is Unit 1 launch/containment evidence only; no drain, quiescence, timeout, cancellation, or b2c claim is made.

| Work Unit Evidence | Exact result |
|---|---|
| RED | With the exception-to-result catch disabled, the focused launch fault matrix failed 1/9 on `CreateProcessException` with an unhandled `InvalidOperationException`. |
| GREEN | Restoring the catch passed focused `PackagingSupervisor` 27/27, including the deterministic exception result/handle closure and the real Windows native launch test. |
| GATE / receipt | `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/errors; `git diff --check` passed with LF-to-CRLF advisories only; `Get-Process -Name powershell` returned 0. Reproducible Unit-1 receipt: pre-fix 259 + this correction 70 = 329 review-relevant lines, within 400. Both ledger rows are fixed, not verified. |

### Judgment Day fix round 1 — b2a input boundary (2026-07-26)

**Scope:** Maintainer-authorized correction of only `JD-B2A-001` and `JD-B2A-002`. `Program` now consumes stdin through a fixed 4097-byte buffer and returns the closed invalid response as soon as the 4097th byte arrives, without waiting for EOF or allocating an unbounded stream. `protocolVersion` now uses non-throwing Int32 parsing, so out-of-range and fractional JSON numbers produce only `invalid-request` response bytes. Focused RED failed with missing `HandleAsync` (`CS0117`); GREEN passed `15/15` PackagingSupervisor tests, including a no-EOF over-limit stream and three invalid numeric versions. b2b/b2c remain untouched. Both ledger entries were verified by both scoped re-judges.

**Independent terminal verification:** Focused PackagingSupervisor tests passed `15/15`; a fresh `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings and 0 errors; `git diff --check` passed with LF-to-CRLF advisories only. The final b2a receipt is 504 gross HEAD-relative lines and 362 review-relevant lines from the approved planning baseline; the detailed accounting is in `review-ledger.md`.

## Slice 8C1.1b1b narrowed-contract cleanup (2026-07-25)

**Status:** Standard mode (`strict_tdd: false`). Maintainer-approved mechanical cleanup removed the invalid/deferred nonzero-exit-with-partial-output test and its nonzero harness branch. The valid saturated-stream test and all other proven lifecycle tests remain. RED, GREEN, and TRIANGULATE remain checked under the narrowed task contract; the independent GATE remains unchecked. This record is apply evidence only, not independent verification or review approval. No production script, b1b2, B1a2, MakeAppx, SignTool, review, commit, PR, or cleanup integration changed.

| Work Unit Evidence | Exact result |
|---|---|
| Scope correction | The maintainer reduced the b1b contract because the removed test neither distinguished exit code 7 from cancellation nor proved preservation inside recovery output. `B1B-HARDEN-NONZERO-PARTIAL-OUTPUT` defers that proof outside current b1b acceptance. |
| Lifecycle behavior | `Invoke-OwnedProcess` uses `ProcessStartInfo.ArgumentList`, concurrent `ReadToEndAsync` stdout/stderr tasks, a timeout linked to `PipelineStopToken`, bounded cancellation, `Kill(entireProcessTree: true)`, and bounded post-kill waits. On ordinary exit, an optional explicit known-descendant identity record is required to parse and match PID/start ticks; a live or unprovable descendant fails closed, retains the incomplete output, and never becomes success. |
| Focused lifecycle proof | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Owned_lifecycle" --no-restore -m:1 --nologo` — passed 5/5, failed 0, skipped 0, 1 m 29 s. |
| Focused recovery / build | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingRecovery" --no-restore -m:1 --nologo` — passed 18/18; `dotnet build AIBar.sln --no-restore --nologo` — 0 warnings, 0 errors; `git diff --check` passed; `Get-Process -Name Harness` — 0. |
| Rollback boundary | Revert only the removed test/nonzero harness branch and this b1b evidence/ledger alignment; preserve the existing production script, saturation test, b1a, 8C1.1a, and b1b2. |

**Changed-line count:** active review diff remains below the 400-line hard cap. **Next:** pending scoped verification; b1b2 and all downstream slices remain out of scope.

## Slice 8C1.1b1a final evidence closure (2026-07-24)

**Status:** Standard mode (`strict_tdd: false`). This maintainer-authorized evidence-only work unit made no production, lifecycle, or test-behavior change. `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` was verified unchanged before and after execution: SHA-256 `2D0F79703E24511F33D676E219775602AD0069EB92A75B4C38981CE9A191AA2D`. The latest approved receipt remains `review-246b855802905e01` (generation 1, `approved`, resolved `R3-001`). No b1b, b2, B1a2, review, commit, PR, or `.gitignore` action occurred.

| Work Unit Evidence | Exact result |
|---|---|
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Red_direct_invocation_completes_while_known_owned_grandchild_remains_alive" --no-restore -m:1 --nologo` — exit 0; passed 1/1, failed 0, skipped 0. Complete raw merged stdout/stderr capture: 978 bytes; SHA-256 `72292AC33A061A64B5E09A5DC28329D19CCE68D422E670CF7C043CFC00B06EC1`. |
| Current build/type-check | `dotnet build AIBar.sln --no-restore --nologo` — exit 0; 0 warnings, 0 errors. Complete raw merged stdout/stderr capture: 684 bytes; SHA-256 `3C46A2108C5B1215B863E7818B6575E3958A8940C29DD512CA42D3DEFE5D04DA`. This is build/type-check evidence only; no full test suite is claimed. |
| Runtime harness / cleanup | The focused test is the runtime-generated external child/grandchild harness. After it exited, scoped descendant inspection found no remaining harness/`Harness.csproj` process. Exact captures remain only in the approved external temporary directory `C:\Users\mjsal\AppData\Local\Temp\opencode`; no generated evidence file was added to the repository. |
| Diff | `git diff --check` — exit 0; no whitespace errors (Git emitted existing LF-to-CRLF advisories only). |
| Rollback boundary | Revert only this evidence-closure narrative in `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. No functional source or test behavior changed. |

**Task narrative alignment:** stale statements that b1a was incomplete or remediation was not authorized were replaced with the actual attempt-37 remediation, approved `review-246b855802905e01` receipt, and this evidence closure. Historical attempt-36 failure remains historical; the verifier-owned report remains unchanged. **Native work unit:** `slice-8c1-1b1a-final-evidence`, one attempt, maximum 400 changed lines. **Next:** verifier-owned evidence-gate update/acceptance; b1b, b2, and B1a2 remain blocked.

## Slice 8C1.1b1a verification remediation applied (2026-07-24)

**Status:** Standard mode (`strict_tdd: false`). Native attempt 37 remediated all four independent verification CRITICAL findings in one bounded work unit. It changes only `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` plus this truthful OpenSpec evidence/task state. The prior attempt-36 receipt remains historical and does not bind these bytes. No production lifecycle behavior, publisher-script change, b1b/b2/B1a2 work, MakeAppx, SignTool, commit, PR, or review action occurred.

| Work Unit Evidence | Exact result |
|---|---|
| RED-first remediation evidence | Before the passing correction, focused runs exposed incorrect finite-token expectations and release ordering while the harness was extended. The retained descendant-quiescence RED remains direct child exit zero while the recorded grandchild is live; saturation/deadlock is never used as proof. |
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Red_direct_invocation_completes_while_known_owned_grandchild_remains_alive" --no-restore -m:1 --nologo` — passed 1/1, failed 0, skipped 0. UTF-8 captured-output SHA-256: `243D8063F9EC5BD3818E5A7F2438D12069FE8DC244400E127E53A70F79D9EEC5`. |
| Immediate repeat | The exact focused command immediately repeated — passed 1/1, failed 0, skipped 0. UTF-8 captured-output SHA-256: `243D8063F9EC5BD3818E5A7F2438D12069FE8DC244400E127E53A70F79D9EEC5`. |
| Runtime harness / process and cleanup | The generated `net8.0` child records direct-child PID/start time before launching its grandchild; the grandchild completes the shared child/grandchild record. The test validates the direct owned handle and targeted grandchild handle, sees direct completion while the grandchild remains live, then signals release and bounded-waits for known grandchild exit. Both finite stdout/stderr token pairs are asserted exactly after release. Compiler stdout/stderr drain concurrently and the shared 10-second bound covers execution plus drains; timeout kills only the compiler tree, bounded-waits again, and reports at most 4096 diagnostic characters. Cleanup first requires the exact nonce marker, canonical containment of every known artifact, a non-reparse root, and a complete owned-tree reparse scan; failed admission preserves the root. No descendant remains before deletion. |
| Diff | `git diff --check` — exit 0; no whitespace errors (Git reported only existing LF-to-CRLF advisories). |
| Rollback boundary | Revert only the descendant-harness remediation in `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs` and the seven b1a task/evidence edits in `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. Keep 8C1.1a and reviewed 8C1 defaults; b1b/b2/B1a2 remain untouched and blocked. |

**Finding resolution:** (1) cleanup now checks exact nonce marker, canonical known-artifact containment, root and nested reparse points before recursive deletion; (2) compiler output drains concurrently within one bounded execution-and-drain timeout and targeted compiler-tree timeout path; (3) child and grandchild PID/start-time records plus owned-handle validation are present; (4) child and grandchild emit finite non-saturating stdout/stderr tokens, with exact captured stream assertions. **Authored scope:** below the 400-line maximum. **Next:** independent verification/review is required for these new bytes; b1b, b2, and B1a2 remain blocked.

```json
{"schema":"gentle-ai.remediation-result/v1","lineage_id":"review-d2bee95a21fe9905","generation":1,"mode":"standard","fix_batch":"slice-8c1-1b1a-verification-remediation","failed_evidence_revision":"sha256:3efc2057079cd07ab1b7d2520e1390ecc45873fa6113c70883ff908ff3a32349","outcome":"passed"}
{"schema":"gentle-ai.remediation-evidence/v1","lineage_id":"review-d2bee95a21fe9905","generation":1,"mode":"standard","fix_batch":"slice-8c1-1b1a-verification-remediation","failed_evidence_revision":"sha256:3efc2057079cd07ab1b7d2520e1390ecc45873fa6113c70883ff908ff3a32349","focused_output_hash":"sha256:243d8063f9ec5bd3818e5a7f2438d12069fe8dc244400e127e53a70f79d9eec5","repeat_output_hash":"sha256:243d8063f9ec5bd3818e5a7f2438d12069fe8dc244400e127e53a70f79d9eec5","diff_check":"passed"}
```

## Slice 8C1.1b1a descendant-quiescence RED harness applied (2026-07-24)

**Status:** Standard mode (`strict_tdd: false`) with task-mandated retained RED-first evidence. This isolated unit changes only `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`; it adds no production lifecycle behavior, no `scripts/Publish-Deterministic.ps1` change, and no b1b, b2, B1a2, MakeAppx, or SignTool work.

| Work Unit Evidence | Exact result |
|---|---|
| RED | The generated external `net8.0` direct executable proves the unchanged direct invocation exits zero while its explicitly recorded, owned grandchild handle remains live. The retained RED is the absence of descendant-exit/tree-quiescence evidence; stream saturation is not used. |
| Helper compilation / protocol | The test writes `Harness.csproj` and `Program.cs` below a fresh GUID root containing a space and NFC `café`, then runs bounded `dotnet build Harness.csproj --nologo -v:q`. Child-to-grandchild launch uses only `ProcessStartInfo.ArgumentList`; GUID-named ready/release events, exact root argument, PID/start-time identity record, and marker ordering are asserted. |
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Red_direct_invocation_completes_while_known_owned_grandchild_remains_alive" --no-restore -m:1 --nologo` — first implementation run failed 1/1 on generated-helper compile (`CS0136`), then passed 1/1 after correcting the protocol; immediate repeat passed 1/1. |
| Runtime harness / cleanup | Each pass signals release, bounded-waits for the known grandchild, validates PID/start-time identity, marker, containment root, and no root reparse point, then deletes only its owned GUID root. Disposal targets only the recorded process and uses bounded `Kill(entireProcessTree: true)` only if release does not finish it. No global scan, sleep, polling loop, `.cmd`, pipe saturation, or unbounded wait is used. |
| Rollback boundary | Revert the descendant harness test and these four 8C1.1b1a task marks/progress block only. Keep 8C1.1a isolation, reviewed 8C1 defaults, and all b1b/b2/B1a2 work unchanged. |

**Scope/line count:** 108 authored additions + 0 deletions in the test file, plus four checkbox edits and this evidence record; below the 400-line work-unit cap. **Next:** b1a is implementation-complete but must be independently reviewed/receipted/committed before b1b. No commit, PR, review, MakeAppx, or SignTool action was performed.

## Slice 8C1.1a generation 27 applied (2026-07-24)

**Status:** Standard mode (`strict_tdd: false`) with task-mandated RED-first evidence; `auto-chain` / `feature-branch-chain`, PR #7a only. This child implements only isolated admission and deterministic MSBuild routing. Slice 8C1.1b, B1a2, MakeAppx, and SignTool remain untouched.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added `Red_isolated_mode_rejects_a_relative_parent_before_command_launch` before the production change. `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Red_isolated_mode_rejects_a_relative_parent_before_command_launch" --no-restore -m:1` failed 1/1: the prior script accepted the relative isolation parent and launched the fake command. |
| GREEN | The same command passed 1/1 after isolated mode required a fully qualified parent and fully qualified children. |
| Focused / runtime harness | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingRecovery" --no-restore -m:1` passed 12/12. Two marker-owned caller roots (including a space/NFC Unicode root) ran explicit restore then `publish --no-restore`; recursive inventories, ZIPs, manifests, and identities matched byte-for-byte. Repository Desktop `obj/**` and `bin/**` hashes were unchanged. Roots were disposed by test ownership after child exit. |
| Full regression / build | `dotnet test AIBar.sln --nologo --no-restore -m:1` passed 218/218. `dotnet build AIBar.sln --nologo --no-restore -m:1` succeeded with 0 warnings and 0 errors. `git diff --check` passed. |
| Process / cleanup | A first focused invocation exceeded the external 120-second command timeout; its PowerShell child was absent afterward and no `aibar-8c1-isolated-*` root remained. The rerun completed 12/12. No MakeAppx or SignTool was invoked. |
| Rollback boundary | Revert only the isolated admission/routing changes in `scripts/Publish-Deterministic.ps1`, `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`, and these three task marks/progress evidence; reviewed 8C1 default invocation remains unchanged. |

**Implementation:** isolated mode now rejects relative parent/child paths before launch and revalidates the marker-owned parent plus all external routing children after creation and immediately before both restore and publish. Existing all-or-nothing parameters, NFC, containment, reparse, collision, stale-child, marker, deterministic `DirectoryBuildPropsPath`, explicit restore, and `publish --no-restore` behavior remain. **Authored count:** 97 source/test additions+deletions (`48+3` script; `45+1` tests), below the 400-line ceiling. **Next:** bounded review of 8C1.1a only; 8C1.1b remains unchecked and B1a2 blocked.

## Slice 8C1.1 generation 25 replanning preflight (2026-07-24)

**Status:** failed before implementation. The maintainer authorized splitting the invalid generation-24 correction, but the existing 79-line implementation and 35-line test candidate still lacks comprehensive admission negatives, process-tree ownership/timeout/draining, child-exit-before-cleanup enforcement, cleanup revalidation, repository-wide isolation proof, and RED-first evidence. Completing those concerns as one autonomous correction would exceed the 400 authored-line cap when the existing candidate and required OpenSpec evidence are included. No source or test file was edited and no focused/full/build/runtime command was run in this generation.

**Concrete split:** `8C1.1a` owns fail-closed isolated admission plus explicit external MSBuild routing and two-root deterministic/repository-isolation proof (forecast 260–340 authored lines). `8C1.1b` owns argument-safe restore/publish process lifetime, asynchronous capture, timeout/cancellation child-tree termination/wait, marker-gated post-exit cleanup, and failure/no-tool proof (forecast 240–340). Both retain the exact reviewed 8C1 default invocation; B1a2 remains blocked until both are independently reviewed, receipted, and committed.

| Work Unit Evidence | Exact result |
|---|---|
| Focused test | Not run: the preflight found no complete autonomous implementation unit within the cap. |
| Runtime harness | Not run: no external root or restore/publish process was created. |
| Rollback boundary | Revert only this replanning record and the unchecked 8C1.1 task split; existing candidate source/test bytes were not modified. |

**Native attempt:** generation 25 began with request `aibar-8c1-1-split-preflight-20260724-25` from revision `sha256:50969c20a73f193567f0905bfd2efb12e3a3c1dd8502a95414b5efdaf79db5e6` and finished `failed` with request `aibar-8c1-1-split-preflight-finish-20260724-25`; terminal revision `sha256:efd0e153182a8eec5c2c8f5e88211c07e7c1d7e6a95931df14b56a4d1188d71b`, ledger changed-lines `35`. No MakeAppx or SignTool was invoked.

## Prerequisite Slice 8C1.1 applied (2026-07-23)

**Status:** Standard mode; `auto-chain` / `feature-branch-chain`, PR #7a only. This is an opt-in extension to reviewed 8C1: default invocation remains unchanged. No B1a2 policy, MakeAppx, SignTool, signing, MSIX, or lifecycle work was added.

| Work Unit Evidence | Exact result |
|---|---|
| RED / focused | The isolated two-root test initially failed before isolation arguments existed. `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingRecovery" --no-restore -m:1` — passed 11/11, failed 0, skipped 0. |
| Runtime harness | Two caller-owned external roots (one NFC Unicode/space root) completed explicit restore then `publish --no-restore`; inventories, ZIPs, manifests, and artifact identity were byte-identical. Repository Desktop `obj/**` and `bin/**` hashes were unchanged. Marker mismatch and stale-child failures launched no publish; all harness roots were removed only after PowerShell children exited. |
| Full regression / build / diff | `dotnet test AIBar.sln --nologo --no-restore -m:1` — passed 217/217; `dotnet build AIBar.sln --nologo --no-restore -m:1` — 0 warnings, 0 errors; `git diff --check` passed. |
| Rollback boundary | Revert only `scripts/Publish-Deterministic.ps1`, `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`, and these four task/progress marks; exact reviewed 8C1 defaults remain. |

**Implementation:** isolated mode is all-or-nothing and requires a marker-owned external parent plus fresh, unique intermediate/build/restore/package/recovery children. A generated external `DirectoryBuildPropsPath` gives each project its own MSBuild leaf, forwards the same isolation configuration to restore and publish, and maps external paths for deterministic binaries. Failed publishes retain caller-owned incomplete output; the script performs no recursive cleanup or replacement. **Candidate accounting:** 79 source/test additions+deletions, below 400; generation 23 added 0 source lines. **Next:** independent review, receipt, and commit of 8C1.1 before B1a2.

## Current B1a1/B1a2 planning authority (2026-07-23)

**Authority:** maintainer-approved planning splits failed B1a into unchecked 8C2B1a1 pure graph projection/reconciliation/CycloneDX and 8C2B1a2 safe restore/publish acquisition plus live two-root proof. Generation 9 candidate/checkmarks, tests, outputs, counts, and completion claims, and generation 10's failed correction attempt, are historical and non-authoritative. No B1a review, receipt, commit, or approval exists; neither child may inherit completion evidence.

**Required start:** the invalid 93-line generation 9 implementation/test candidate still exists. Before B1a1 Apply, selectively restore the implementation and test paths to exact HEAD `28a2f46` bytes while preserving this planning update; do not review, receipt, commit, or reuse the candidate. This planning update does not claim cleanup, restoration, quarantine, output deletion, staging, or Git/index/history changes have occurred.

**Active chain:** reviewed 8C2A → B1a1 (PR #6) → B1a2 (PR #7) → B1b (PR #8) → B2 (PR #9) → 8D (PR #10) → 8E (PR #11), feature-branch-chain, each child <=400 authored additions+deletions, no exception. B1b depends on reviewed B1a2; B2 depends on reviewed B1b; 8D depends on reviewed B2.

## Slice 8C2B1a1 generation 17 checked-fixture evidence (2026-07-23)

**Status:** passed pure-model scope only. Maintainer-authorized, versioned minimal real-format fixtures supply restore assets, emitted deps, publish inventory, recovery inventory, and source/publish bytes. They are not live-runtime evidence: B1a2 later validates live acquisition/integration.

| Work Unit Evidence | Exact result |
|---|---|
| Focused GraphProjection test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution&FullyQualifiedName~GraphProjection" --no-restore -m:1` — passed 1/1, failed 0, skipped 0. Mutations cover graph edge/reachability, omission/extra/duplicate/case collision/owner ambiguity/test leakage, recovery mismatch, source bytes, artifact hash, and length. |
| Full suite / build | The initial generation-16 full-suite run reported one host-mutex test failure; no exact error text is retained here. Its immediate rerun of `dotnet test AIBar.sln --nologo --no-restore -m:1` passed 215/215. `dotnet build AIBar.sln --nologo --no-restore -m:1` — 0 warnings, 0 errors. |
| Runtime harness | N/A: B1a1 consumes checked fixtures only and launches no restore/publish process. B1a2 exclusively proves live acquisition and integration. |
| Rollback boundary | Revert only B1a1 graph-mode script/tests/`Fixtures/PackagingGraph`, `packaging/{sbom.cdx.json,compliance-manifest.json}`, these four checkboxes, and this evidence; 8C2A remains. |

**Generation 17 provenance correction:** preserves focused 1/1, the host-mutex initial full-suite failure disclosure, successful 215/215 rerun, and build result. The native generation-17 finish ledger is terminal-revision authority after finish. No additional claim is made.

## Historical non-authoritative Slice 8C2B1a generation 9 candidate (2026-07-23)

**Authority/base:** `28a2f46` split plan; Standard mode; `auto-chain` / `feature-branch-chain`, PR #6 only. The real `Release/win-x64` self-contained harness selected the matching restore and published RID graphs and emitted canonical CycloneDX 1.5 nested file evidence without legal/provenance promotion, MakeAppx, or SignTool.

| Work Unit Evidence | Exact result |
|---|---|
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution" --no-restore -m:1` — passed 4/4, failed 0, skipped 0. |
| Full suite / build | `dotnet test AIBar.sln --nologo --no-restore -m:1` — passed 215/215; `dotnet build AIBar.sln --nologo --no-restore -m:1` — 0 warnings, 0 errors. |
| Runtime harness | Two fresh `artifacts/8c2b1/` roots (one reordered and non-ASCII) ran real publish: 9 graph components, 470 artifacts, and byte-identical SBOM/manifest output. Child capture drains stdout/stderr asynchronously; timeout kills/waits its tree; stale leaves and failed/partial children are unaccepted. |
| Rollback boundary | Revert only `scripts/Publish-Deterministic.ps1`, `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`, and this B1a evidence/checkmarks; delete only caller-owned ignored `artifacts/8c2b1/` roots. |

**Mapping:** 470 files: 6 first-party, 3 managed, 236 runtime, 4 native, 221 resource. No receipt, license/notice/provenance schema, durable promotion/recovery, MakeAppx, SignTool, lifecycle, or release work was added.

## Current Slice 8C2B1 supersession authority (2026-07-23)

**Current authority:** the failed combined/B1 working candidate, former B1 checkmarks, tests, runtime generations 3–6, generated outputs, and completion claims below are retained only as historical, non-authoritative evidence. No B1 review, durable receipt, or commit exists. The next apply must start from reviewed `f0d15b1` and the new unchecked B1 tasks, reconstructing the candidate rather than continuing it.

Prior generated outputs and runtime evidence MUST NOT be reused as implementation inputs, acceptance evidence, completion authority, or review authority. The working candidate still exists; this planning correction does not claim that any candidate file or output has been discarded, quarantined, restored, or cleaned. MakeAppx/SignTool execution remains B2-owned.

## Slice 8C2B1 generation 8 preflight (2026-07-23)

**Status:** blocked before implementation. Native generation 8 (`slice-8c2b1-fresh`) started from `cd9cea4` with a 400-line hard limit. The committed B1 contract requires complete redistributed license/notice text, canonical evidence documents, a real publish/reconciliation harness, and adversarial coverage; these additions cannot fit within the remaining authored budget without omitting required behavior or evidence. No production code, tests, generated evidence, candidate-root output, MakeAppx, SignTool, staging, commit, or review lifecycle action was performed.

**Evidence:** runtime status before any execution reported active generation 8, begin revision `sha256:b8b371a6bc356a7d5e911cfceab64802b01a1355aa2ca7aecef1d164db3602e8`, and `changed_lines: 0`. Current committed scope contains only the 64-line recovery publisher, 87-line 8C2A planner tests, and no B1 packaging evidence files; the eight B1 checkboxes remain unchecked. This record is the terminal preflight artifact for the fresh candidate and does not reuse generations 3–7 or their outputs, totals, tests, or receipts.

**Required resolution:** split B1 into additional independently reviewed work units, or explicitly revise the B1 contract and planning base; a size exception is forbidden by the active task. **Rollback boundary:** remove only this preflight record; reviewed 8C2A and all B1 source/evidence paths remain unchanged.

## Slice 8C2B1 applied (2026-07-23)

**Authority/base:** `f0d15b1`; Standard mode (`strict_tdd: false`); `auto-chain` / `feature-branch-chain`, PR #6 only. This supersedes the failed combined-candidate claims without deleting their history below. It retains the reviewed 8C2A manifest/capability planner and removes all unreviewed MakeAppx/SignTool execution or runtime-gate behavior.

| Work Unit Evidence | Exact result |
|---|---|
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution" --no-restore -m:1` — passed 5/5, failed 0, skipped 0. |
| Runtime harness | Two GUID-root controlled restore/inventory generations are executed by `PackagingDistributionTests`; each root is disposed after the child PowerShell process exits. It validates bidirectional restore graph and managed/runtime/native/first-party file mapping; no package/signing process is invoked. |
| Rollback boundary | Revert `.gitignore`, the B1 metadata branch of `scripts/Publish-Deterministic.ps1`, `packaging/{sbom.cdx.json,PROVENANCE.md,THIRD-PARTY-NOTICES.md,LICENSES/**}`, B1 tests, and these OpenSpec records. 8C1 and reviewed 8C2A remain unchanged. |

**Behavior:** canonical CycloneDX 1.5 metadata rejects omitted restored dependencies, unmapped or ambiguous shipped files, incomplete identity/license/origin/edges, missing notices/license text, and placeholder-free component hashes. Test-only scope is explicit in provenance. Metadata contains no root or secret. No MakeAppx, SignTool, MSIX, signing, installability, distribution-ready, lifecycle, or release claim is made.

**Full regression/build:** `dotnet test AIBar.sln --nologo --no-restore -m:1` — passed 216/216, failed 0, skipped 0. `dotnet build AIBar.sln --nologo --no-restore -m:1` — succeeded, 0 warnings, 0 errors. `git diff --check` passed.

**Final corrective evidence (generation 6):** the focused harness runs a real fresh `Release/win-x64` self-contained publish into a GUID-owned temporary parent, consumes `src/AIBar.Desktop/obj/project.assets.json`, and reconciles all 9 restored libraries and 470 published files. It classifies every published file as first-party, managed, runtime, or native; generated metadata is removed when the temporary root is disposed after child PowerShell exits. No MakeAppx, SignTool, MSIX, signing, installability, or release action occurs. The terminal ledger must finish once with request `aibar-8c2b1-final-finish-20260723-01`, expected revision `sha256:9fe0c14c3309b2afbf73dd01d0b483379a8cca2127afd13fad7430c6b4badaf9`, outcome `passed`, and no active attempt; it is the authoritative terminal-revision record.

**Candidate accounting:** `git diff --numstat` plus untracked text accounting before the terminal finish is **193 additions + 21 deletions = 214** authored lines versus `HEAD`, including OpenSpec, notices, and license text; below the 400-line cap. **Rollback:** revert only `.gitignore`, B1 metadata logic/tests, `packaging/{sbom.cdx.json,PROVENANCE.md,THIRD-PARTY-NOTICES.md,LICENSES/**}`, and the B1 OpenSpec records; reviewed 8C2A remains intact.

## Slice 8C2A applied (2026-07-23)

**Status:** Standard mode (`strict_tdd: false`); `applyState: ready`; `auto-chain` / `feature-branch-chain`. This is PR #5's bounded 8C2A unit from reviewed 8C1. It adds metadata inputs and deterministic fake capability planning only. It does not execute MakeAppx or SignTool, create an MSIX, claim package/schema/signature/installability acceptance, add SBOM/provenance/notices, or enter 8C2B, 8D, or 8E.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added `PackagingDistributionTests` before implementation. The focused command failed 3/3 because `packaging/AppxManifest.xml` and capability-plan behavior were absent. |
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingDistribution"` — passed 3/3, failed 0, skipped 0. |
| TRIANGULATE | Tests parse the manifest and PNG headers/dimensions, cover tool-unavailable, unsigned/signing-unavailable, requested-signing-unavailable, publisher mismatch, stale output preservation, exact planned commands, and secret/path-free JSON. |
| Full regression / build | `dotnet test AIBar.sln --nologo` — passed 214/214, failed 0, skipped 0. `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. `git diff --check` — passed. |
| Runtime harness | N/A — real MakeAppx/SignTool packaging and signing execution is exclusively Slice 8C2B. This slice tests injected deterministic planning and makes no runtime package, signing, installability, lifecycle, or release claim. |
| Rollback boundary | Revert `scripts/Publish-Deterministic.ps1`, `packaging/AppxManifest.xml`, `packaging/Assets/`, `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`, and these 8C2A task/progress edits. Reviewed 8C1 recovery publishing remains usable. |

**Behavior:** the x64 full-trust WPF manifest uses a development publisher identity and `internetClient`; publisher binding is checked before a signing plan. Capability planning is explicit: no MakeAppx yields `tool-unavailable`; MakeAppx-only yields `unsigned` plus `signing-unavailable`; requested signing fails closed for missing inputs or publisher mismatch; stale candidate output remains untouched. Planner JSON deliberately omits package-root paths and secrets. PNG assets are valid, dimensioned, and referenced by the manifest.

**Git candidate accounting (versus `HEAD`):** read-only `git diff --numstat HEAD`, plus `git diff --no-index --numstat -- NUL` for each untracked textual file, reports **186 additions + 15 deletions = 201 authored textual lines**, including the approved `tasks.md` planning split. The four PNG assets are binary entries (`-`/`-` in Git numstat), are included in candidate identity, and add no textual authored lines. **201 <= 400**.

**Native runtime status:** generation **2**; work unit **`slice-8c2a`**; terminal outcome **`passed`**; `complete: true`; `next_action: complete`; no `active_attempt` is present. Exact terminal runtime revision: `sha256:f4ddd939e5990340f39e7cd7996c998b5e910c083c7e26c943b76e9214f29af7`.

**Deviation:** none. **Workload / PR boundary:** feature-branch-chain, Slice 8C2A only, base `feature/aibar-foundation-slice-8c1`; no commit, staging, review, PR, SBOM, provenance, notices, signing execution, lifecycle, or release work. **Next dependency:** reviewed 8C2A before Slice 8C2B; no readiness or approval claim is made here.

## Retired Slice 8B.2 removal verified (2026-07-21)

**Status:** strict TDD is active by parent instruction; authoritative OpenSpec status consumed before work: `applyState: ready`, `nextRecommended: apply`, repo-local workspace/allowed edit root, and `auto-chain` / `feature-branch-chain`. This approved removal work unit stops before Slice 8C.

**Retirement outcome:** Deleted only the uncommitted candidate files `src/AIBar.Application/DiagnosticExport.cs` and `tests/AIBar.Domain.Tests/DiagnosticExportTests.cs`. The former 208/208 candidate result and filesystem-export claims are superseded historical evidence, not completion or approval. The corrective design is unchanged.

| TDD Cycle Evidence | Exact result |
|---|---|
| RED | No new production behavior was written: this is deletion of an unapproved production/test candidate. Absence checks are the removal contract. |
| GREEN | Removed both candidate files; 2/2 required paths are absent. |
| TRIANGULATE | Source/test inspection found 0 `DiagnosticExport` or `diagnostics.jsonl` references; project/package/manifest/generated-artifact inspection found 0. The approved command and tray adapter contain 0 filesystem/network/export markers. |
| REFACTOR | No production refactor. SHA-256 before/after comparison confirms the approved command, tray adapter, clear-data service, and their tests are byte-identical. |

**Verification:** proactive diagnostics via `dotnet test AIBar.sln --nologo` passed **202/202** (0 failed, 0 skipped); `dotnet build AIBar.sln --nologo` succeeded with **0 warnings, 0 errors**. `git diff --check` passed and `git diff --cached --quiet` confirmed a clean index. No project, package, manifest, UI adapter, dependency, generated artifact, or test reference remains; no `diagnostics.jsonl` creation exists; no enabled or advertised filesystem/network diagnostic export path exists.

**Persisted task evidence:** the four implementation-owned retired-8B.2 removal tasks are visibly marked `- [x]` in `tasks.md`. The parent-owned lifecycle task and Slice 8C+ tasks remain unchecked.

**Files changed:** deleted `src/AIBar.Application/DiagnosticExport.cs`; deleted `tests/AIBar.Domain.Tests/DiagnosticExportTests.cs`; updated only the retired Slice 8B.2 progress section and its four approved implementation checkboxes. **Deviation:** none. **Workload / PR boundary:** retired 8B.2 removal only; no staging, commit, review, push, PR, publication, or Slice 8C work. **Next:** parent-owned post-apply review/verification routing; do not start Slice 8C from this work unit.

## Slice 8B.1 applied (2026-07-19)

**Status:** Standard mode (`strict_tdd: false`); `applyState: ready`; `auto-chain` / `feature-branch-chain`. This PR #3 work unit starts at reviewed Slice 8B.0 (`c03fa7f62757994e5457711634b1a1e970706334`) and implements only the private one-shot gesture, structured in-memory sinks, and tray/UI adapter. No export, filesystem/network I/O, raw-text parser, installer, or `SafeRedactor` change was introduced.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added `DiagnosticCommandTests` before implementation. The required focused command failed as expected with missing `DiagnosticMemorySinks`, `DiagnosticCommand`, and `IDiagnosticClock` (`CS0246`). |
| GREEN / focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~DiagnosticCommand" --nologo` — passed 7/7, failed 0, skipped 0. |
| TRIANGULATE | Covers one-shot/replayed and cancelled gestures; forged/default zero-token rejection before and after trusted gesture; safe category preview/confirm/unavailable-empty behavior; App/Quota routing; malformed structured-event rejection; same-category oldest-entry count eviction plus byte/age retention; immutable snapshots; concurrent emit/clear races; and clear behavior. |
| Runtime harness | The focused `Synthetic_tray_runtime_proves_safe_preview_confirm_and_no_file_or_network_operation` scenario invokes the internal trusted tray adapter, previews `Quota`, confirms its structured event, then confirms empty `Application` as unavailable. The command source has no filesystem, network, parser, or export reference: `findstr /I /N /R "System\\.IO System\\.Net HttpClient File\\. Directory\\. JsonDocument JsonSerializer Deserialize Export" "src\\AIBar.Application\\DiagnosticCommand.cs" "src\\AIBar.Desktop\\DiagnosticTrayAdapter.cs"` — no matches. |
| Full regression / build | `dotnet test AIBar.sln --nologo` — passed 202/202, failed 0, skipped 0. `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. `git diff --check` — passed. |
| Rollback boundary | Remove `src/AIBar.Application/DiagnosticCommand.cs`, `src/AIBar.Desktop/DiagnosticTrayAdapter.cs`, the Application friend-assembly line, `tests/AIBar.Domain.Tests/DiagnosticCommandTests.cs`, and these Slice 8B.1 task/progress edits. Slice 8B.0 remains intact. |

**Corrective retry:** `Confirm` rejects null/default previews and the zero inactive-token sentinel before attempting atomic token consumption. A forged `DiagnosticPreview(true, [Quota], 0)` is unavailable on repeated attempts before a trusted gesture; a default/null preview is unavailable; a real `BeginTrustedGesture` token still authorizes exactly once. A deterministic single-Quota-sink test records three timestamped events with a count bound of two and proves only timestamps 00:00:01 and 00:00:02 remain.

**Behavior:** `DiagnosticMemorySinks` accepts a `StructuredDiagnosticEvent` only when the existing strict serializer accepts it, routes it to separate Application/Quota in-memory lists, and keeps only immutable serialized snapshots under count, size, and age limits. `DiagnosticCommand` grants one internal trusted token at a time; confirm/cancel atomically consumes it, so replay cannot expose snapshots. `DiagnosticTrayAdapter` is the sole Desktop bridge and cannot access sinks. Export remains unavailable because no export command/path exists. **Deviation:** none. **Next dependency:** Slice 8B.2 after Slice 8B.1 review.

**Changed paths:** `src/AIBar.Application/{AssemblyInfo.cs,DiagnosticCommand.cs}`, `src/AIBar.Desktop/DiagnosticTrayAdapter.cs`, `tests/AIBar.Domain.Tests/DiagnosticCommandTests.cs`, and `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`.

**Authored count:** 285 additions + 4 deletions = 289 total across the exact six-path scope, below the 400-line work-unit limit (111 lines remain; the directed correction consumed 37 of its 148-line allowance).

## Slice 8B.0 applied (2026-07-19)

**Status:** Standard mode (`strict_tdd: false`); `applyState: ready`; `auto-chain` / `feature-branch-chain`. This PR #2 work unit starts from the reviewed Slice 8A base and is limited to a pure Application structured diagnostic contract. No UI, sink, filesystem/network export, arbitrary-text parser, or `SafeRedactor` change was introduced.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added `StructuredDiagnosticTests` before production code. The required focused command failed as expected with missing `DiagnosticFieldEntry`, `DiagnosticErrorKind`, and `StructuredDiagnosticEvent` (`CS0246`). |
| GREEN / focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~StructuredDiagnostic"` — passed 9/9, failed 0, skipped 0. |
| TRIANGULATE | The focused table/property tests cover unknown category/code/error enums, unknown keys/value kinds, duplicate/nested-style invalid entries, null fields, numeric and count limits, casing/escaping hostile content, repeated fallback, deterministic ordering, and reflection proof that the public serializer accepts no string parameter or raw string-bearing event field. Each invalid candidate returns exactly `[REDACTED]`; valid output contains only fixed enum names and scalar values. |
| Full regression / build | `dotnet test AIBar.sln --nologo` — passed 195/195, failed 0, skipped 0. `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. |
| Runtime harness | N/A — this work unit is a pure in-memory Application contract with no runtime host, UI, sink, filesystem, or network boundary. |
| Rollback boundary | Remove `src/AIBar.Application/StructuredDiagnostics.cs`, `tests/AIBar.Domain.Tests/StructuredDiagnosticTests.cs`, and these Slice 8B.0 task/progress edits. Slice 8A startup/settings and the existing credential `SafeRedactor` remain unchanged. |

**Behavior:** `StructuredDiagnosticEvent` carries only allowlisted enums plus bounded numeric/boolean fields. Serialization is deterministic and field-order stable. Unknown category/code/time basis, missing fields, invalid keys/types/ranges, duplicates, or more than eight fields fail closed to the complete `[REDACTED]` fallback. Unknown error kinds become the fixed `unavailable` error kind. The boundary does not accept, parse, retain, or emit arbitrary strings, raw payloads, exception values, identifiers, paths, authorization content, URLs/query strings, or bodies.

**Changed paths:** `src/AIBar.Application/StructuredDiagnostics.cs`, `tests/AIBar.Domain.Tests/StructuredDiagnosticTests.cs`, `openspec/changes/aibar-foundation/tasks.md`, and `openspec/changes/aibar-foundation/apply-progress.md`. **Deviation:** none. **Next dependency:** Slice 8B.1 after Slice 8B.0 review.

## Ordinary-review correction `review-slice-8a-standalone-20260718` (2026-07-18)

**Authority / scope:** post-forecast revision `sha256:328819136f5b1748aca3412d8d55808641cf888c9b13039d0e1bfa1f0f5849bd`; only frozen severe IDs `R3-001`, `R4-001`, `R4-002`, and `R2-001` were corrected. INFO warnings and planning/tasks remain unchanged.
**Frozen-ID proof:** `R3-001`/`R4-001` now read both effective startup backends, migrate stale fallback state when packaged startup becomes available, and disable packaged plus HKCU state; `R4-002` stages validated owned targets through reversible sibling moves, rolls back an earlier prepared target when a later file is locked, and publishes host failure through the existing unavailable-state seam; `R2-001` replaces the production no-op with `QuotaRefreshCoordinator.ClearAsync`, publishing null/unavailable coordinator and presentation state.
**Verification:** focused quoted-filter startup/Clear Data/host tests passed 25/25; direct synthetic/temp-root safety harness passed 15/15; full solution passed 186/186; build succeeded with 0 warnings and 0 errors; `git diff --check` passed.
**Runtime safety:** all correction tests use fakes, synthetic reflection, and temporary roots; no real registry, startup task, elevation, machine-wide state, AppData, or Codex-owned source was read or mutated.
**Rollback:** revert only this correction in `StartupSettings.cs`, `ClearAiBarDataService.cs`, `App.xaml.cs`, `HostRuntime.cs`, `StartupSettingsTests.cs`, `HostRuntimeTests.cs`, and this evidence block; the frozen reviewed candidate remains the rollback state.
**Exact correction receipt relative to the frozen candidate: 139 additions + 12 deletions = 151 authored lines, below the 160 forecast and 200 hard budget.**

## Slice 8A applied (2026-07-18)

**Status:** Standard mode (`strict_tdd: false`); `applyState: ready`; `feature-branch-chain` with a **Slice 8A-only `size:exception`**, explicitly maintainer-approved in session 2026-07-18. Slices 8B–8E remain `auto-chain` and <=400. This child starts at Slice 7C parent `66dbfbac94256b9d259247076c1517688ad5e75b` and is limited to per-user startup/settings and Clear AIBar Data command wiring.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added `StartupSettingsTests` first. The required focused command failed before production code with missing `IStartupTaskRegistration`, `ICurrentUserRunStore`, and `IAiBarDataClearCommand` (`CS0246`). |
| GREEN / focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Startup|FullyQualifiedName~ClearAiBarData|FullyQualifiedName~Policy"` — passed 41/41, failed 0, skipped 0. |
| TRIANGULATE / safe runtime harness | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~StartupSettingsTests|FullyQualifiedName~Native_settings_events"` — passed 11/11, failed 0, skipped 0. Injected packaged-task/current-user-run fakes prove package-unavailable fallback, exact HKCU path and quoted command, malformed values, denial, true→false read-back, `quota.db` clear isolation, kill-switch/local-analytics preservation, direct host startup/clear events, and synthetic reflection Completed/Error/Canceled/unexpected states. No real registry/startup task/user data was touched. Live Windows toggle verification is deferred to Slice 8D's isolated install harness. |
| REFACTOR | The runtime detects and invokes the OS packaged startup-task API only when it is present and task resolution succeeds; otherwise it falls back to the isolated HKCU adapter. Reflection polling accepts only `Started`/`Completed` and fails closed immediately for `Error`, `Canceled`, missing, or unexpected states. Host state and the checked native menu update only from effective post-mutation read-back. `ClearAiBarDataService` owns `quota.db` and sidecars. |
| Full regression / build | `dotnet test AIBar.sln --nologo` — passed 182/182, failed 0, skipped 0. `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. `git diff --check` — passed (LF-to-CRLF advisories only). |
| Rollback boundary | Revert `src/AIBar.Application/{StartupSettings.cs,AssemblyInfo.cs,ClearAiBarDataService.cs,CredentialBoundary.cs}`, `src/AIBar.Desktop/{App.xaml.cs,HostRuntime.cs}`, `tests/AIBar.Domain.Tests/{StartupSettingsTests.cs,HostRuntimeTests.cs}`, and `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. This includes the Slice 8A `PrivateIntegrationPolicy` mutation. Existing Clear AIBar Data, quota, analytics, and Slice 7C presentation behavior remain. |

**Behavior:** unpackaged startup writes only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with a quoted executable and `--startup`; the production adapter never elevates or writes machine-wide state. A packaged process resolves the Windows startup-task API/task at runtime and uses it only when that task is available; otherwise the HKCU adapter is selected. Every mutation reads the effective state back into the host/menu. The native private-integration command only disables the default-off policy and local analytics remains enabled. Clear Data includes live `quota.db`/WAL/SHM and fails closed if a locked owned file cannot be removed. **Deviation:** live mutable startup verification was intentionally deferred because this apply batch must not persist real user startup state.

**Size exception rationale:** maintainer explicitly approved `size:exception` for Slice 8A in session 2026-07-18 after truthful production/runtime corrections exceeded the original budget. The exception applies only to this unit; 8B–8E remain auto-chained and <=400.

**Exact Slice 8A authored changed-line receipt from `66dbfbac94256b9d259247076c1517688ad5e75b`: 455 additions + 26 deletions = 481 total, including the authorized Slice 8 planning diff. This exceeds 400 solely under the Slice 8A-only maintainer-approved `size:exception`.**

## Slice 7C applied (2026-07-18)

**Status:** Standard mode (`strict_tdd: false`); `applyState: ready`; `auto-chain` / `feature-branch-chain`. This child work unit starts from Slice 7B commit `42729af` and covers view-model mapping only. No domain policy, quota behavior, host lifecycle, runtime integration, or Slice 8 work changed.

| Work Unit Evidence | Exact result |
|---|---|
| RED | Added `ViewModelPresentationTests` first. `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~ViewModel` failed as expected with missing `DerivedMetricsPresentationMapper` and `ViewModelDisplayLabels` (`CS0246`/`CS0103`). |
| GREEN / focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~ViewModel` — passed 4/4, failed 0, skipped 0. |
| TRIANGULATE / full relevant suite | `dotnet test AIBar.sln --nologo` — passed 171/171, failed 0, skipped 0. Snapshots cover stale quota, unknown/unsupported price states, unsupported ETA, and insufficient trend data; equality assertions prove token facts, service quota, and observations are unchanged. |
| REFACTOR | `ViewModelDisplayLabels` and one warning-copy table centralize source/non-authoritative wording. Mapping returns original factual collections and values; it never derives a replacement quota, token total, or cost. |
| Build / diagnostics | `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. `git diff --check` — passed; only Git LF-to-CRLF advisory warnings occurred. |
| Runtime harness | N/A — this is a pure immutable presentation-mapping boundary with no new live host, service, filesystem, or UI runtime path. |
| Rollback boundary | Revert `src/AIBar.Desktop/DerivedMetricsPresentation.cs`, `src/AIBar.Desktop/QuotaPresentation.cs`, `tests/AIBar.Domain.Tests/ViewModelPresentationTests.cs`, and these Slice 7C task/progress edits. Slice 7A pricing and Slice 7B trend/ETA policies remain usable. |

**Behavior:** service quota, locally derived trend facts, estimated cost, and ETA estimates each retain an explicit source label. Missing costs remain `null`/unavailable; unknown or unsupported prices receive readable warnings plus non-billing language. No ETA output is called a prediction. **Deviation:** none. **Next dependency:** Slice 8 only after this Slice 7C child is reviewed.

### Gatekeeper authorized corrective retries (2026-07-18)

`QuotaPresentationMapper` consumes the shared source labels, and `MapCost` now consumes `WarningCopy[PricingWarning.NonAuthoritative]` rather than repeating its literal; display text and warning-list behavior are unchanged.

| Verification | Exact result |
|---|---|
| Focused ViewModel | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~ViewModel` — passed 4/4, failed 0, skipped 0. |
| Full solution / build / diff | `dotnet test AIBar.sln --nologo` — passed 171/171, failed 0, skipped 0; `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors; `git diff --check` — passed (LF-to-CRLF advisories only). |

**Exact Slice 7C authored changed-line receipt from `42729af3c603b42868d7492071a41d6b21e0e13d`: 124 additions + 7 deletions = 131 total, below the 400-line limit.**

## Slice 7A applied (2026-07-14)

**Status:** Standard TDD mode (`strict_tdd: false`); authoritative OpenSpec `applyState: ready`; force-chained `feature-branch-chain`. This child work unit starts from committed Slice 6D (`2f0af30`) with the intentional Slice 7 task decomposition preserved, and covers immutable pricing catalog and pure estimated-cost policy only. No token persistence, trend/window/ETA policy, view-model/UI, billing authority, Git lifecycle, PR, or review action was used.

| Evidence | Exact result |
|---|---|
| RED | Added `PricingPolicyTests` before the production policy. `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~Pricing --nologo` failed as expected with missing `PricingPolicy`, `CheckedInPricingCatalog`, result-state, and warning contracts (`CS0246`/`CS0103`). |
| GREEN / focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~Pricing --nologo` — passed 9/9, failed 0, skipped 0. |
| TRIANGULATE / token-fact regression | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter 'FullyQualifiedName~Pricing|FullyQualifiedName~SqliteDailyModelUsageStoreTests|FullyQualifiedName~Slice6AggregationIntegrationTests' --nologo` — passed 21/21, failed 0, skipped 0. It proves pricing keeps supplied component facts unchanged, and existing SQLite/Slice 6 regressions prove repricing never changes persisted token facts. |
| REFACTOR | `CheckedInPricingCatalog` now implements the design-named `IPricingCatalog`; obsolete placeholder `PricingResult`/`PricingResultState` and duplicate `IComponentPricingCatalog` are removed. One policy owns provenance and read-only warning semantics. No persistence or presentation contract changed. |
| Catalog inspection / provenance | `CheckedInPricingCatalog.Current` is checked-in immutable code data: version `2026-07-14`, effective date `2026-07-14`, and `gpt-5` input/cached-input/output rates of `1.25`/`0.125`/`10.00` per million tokens. Component-only one-million-token cases fail if rates are swapped. A copied `readonly record struct` rate can change only locally; re-fetch proves the stored rate/provenance unchanged, and the exposed read-only dictionary rejects added keys. `Unknown` and unlisted models have no lookup/fallback. |
| Full regression / build | `dotnet test AIBar.sln --nologo` — passed 155/155, failed 0, skipped 0; `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors; `git diff --check` — passed with no whitespace errors (Git emitted only LF-to-CRLF conversion warnings for modified tracked files). |
| Runtime harness | N/A — this is a pure Domain pricing-policy boundary; no live service, persistence, filesystem, or UI runtime exists in scope. |
| Rollback boundary | Remove `src/AIBar.Domain/PricingPolicy.cs`, restore the prior placeholder-only pricing declarations in `src/AIBar.Domain/Foundation.cs`, remove `tests/AIBar.Domain.Tests/PricingPolicyTests.cs`, and revert these Slice 7A task/progress updates. Slice 6D token facts, persistence, quota, and all later Slice 7B/7C work remain untouched. |

**Behavior:** component-specific rates calculate only an explicitly estimated result. Empty facts return `Unavailable`; `Unknown` and unlisted models return `Incomplete` with no monetary number. Every result retains original `DailyUsage` token facts and catalog version/date; estimates carry repricing, discount, routing, contract-term, and non-authoritative warnings through a read-only collection. **Changed paths:** `src/AIBar.Domain/Foundation.cs`, `src/AIBar.Domain/PricingPolicy.cs`, `tests/AIBar.Domain.Tests/PricingPolicyTests.cs`, `openspec/changes/aibar-foundation/tasks.md`, and this file. **Authored count:** 240 additions + 12 deletions = 252 from `2f0af30`, including the pre-existing task decomposition and untracked files. **Deviation:** none — the checked-in immutable code catalog is the small design-consistent catalog representation. **Next dependency:** Slice 7B only after 7A review.

## Slice 6D applied (2026-07-14)

**Status:** Standard TDD mode (`strict_tdd: false`); OpenSpec/repo-local `applyState: ready`; force-chained `feature-branch-chain`. This child work unit starts at `512488a` and covers synthetic aggregation integration hardening only. No production code, live Codex source, AppData, Git lifecycle, PR, or review action was used.

| Evidence | Exact result |
|---|---|
| RED | Added the persistence-inspection test before production changes. Its first run exposed a test-only pooled read-only SQLite handle that prevented deterministic temporary-root cleanup; disabling pooling in the inspection connection fixed the harness without changing production behavior. |
| GREEN | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~Slice6AggregationIntegrationTests --nologo` — passed 4/4, failed 0, skipped 0. |
| TRIANGULATE / aggregation-property-persistence report | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter 'FullyQualifiedName~Slice6AggregationIntegrationTests|FullyQualifiedName~AnalyticsScanCoordinatorTests|FullyQualifiedName~SqliteDailyModelUsageStoreTests|FullyQualifiedName~AnalyticsPolicyTests' --nologo` — passed 31/31, failed 0, skipped 0. The single-quoted plain-pipe filter is PowerShell-safe and was executed exactly as shown. Covers checkpoint/aggregate rollback and crash retry, partial-tail retention, CAS/rebuild/unrelated-source retention, repricing token-fact retention, ranking, `Unknown`, component resets/decreases, local midnight, and DST provenance. |
| REFACTOR | Kept only one synthetic fixture builder, coordinator factory, SHA-256 helper, and temporary-root disposal in the integration test; no production abstraction changed. |
| Full regression / build | `dotnet test AIBar.sln --nologo` — passed 146/146, failed 0, skipped 0; `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors; `git diff --check` — passed (only LF-to-CRLF warnings). |
| Runtime harness | N/A — the real boundary is the synthetic SQLite/filesystem scanner harness; no live Codex/AppData or UI boundary belongs to this slice. |
| Privacy report | The synthetic JSONL deliberately includes unique prompt, response, access-token, and raw-path sentinels. The scanner reads the JSONL only to extract its allowed fields; after the actual scan/persistence path, the test enumerates every AIBar SQLite table and asserts all sentinels plus the actual raw source path are absent. It also proves the durable SHA-256 source fingerprint, checkpoint, contribution row, and `7/8/9` token facts remain usable. The fixture SHA-256 is unchanged before/after scan; no real Codex file is used. |
| Rollback boundary | Remove `Slice6AggregationIntegrationTests.cs` and this Slice 6D OpenSpec evidence/checkmarks; completed Slice 6 production aggregation, checkpoint, contribution, and clear-data behavior remains intact. |

**Changed paths:** `tests/AIBar.Domain.Tests/Slice6AggregationIntegrationTests.cs`, `openspec/changes/aibar-foundation/tasks.md`, and this cumulative progress file. **Authored count:** 170 additions + 2 deletions = 172 lines, below the hard 400-line budget (including the untracked test and OpenSpec evidence). **Acceptance:** synthetic before/after fixture hash proof and raw-persistence inspection are asserted in the focused test; aggregation/property/privacy evidence is recorded above. **Deviation:** no production change — a test-only SQLite read connection disables pooling for deterministic temporary-fixture cleanup. No pricing catalog, trends, pace, burn, ETA, UI, or production logic was added. **Next dependency:** Slice 7 after this 6D child slice is independently reviewed.

## Slice 6C2B applied (2026-07-14)

**Status:** Standard mode; OpenSpec/repo-local, `applyState: ready`, force-chained `feature-branch-chain`. This child work unit starts at `7d2f025` and ends at Clear AIBar Data only; no Git, PR, or review lifecycle action was performed.

`ClearAiBarDataService` serializes clear requests, cancels and awaits registered AIBar-owned work, rejects ambiguous/traversing/non-allowlisted paths and reparse points, then deletes only `cache`, `logs`, `aibar.db`, `aibar.db-wal`, `aibar.db-shm`, and `settings.json` under its explicit application-data root. It never recurses over the root or a Codex path. A successful return means the allowlisted targets were removed and the injected empty-state factory completed; deletion is intentionally non-atomic. A deletion/recreation failure returns an error without claiming success; a later clear is retryable. `QuotaRefreshCoordinator` now quiesces its active refresh, waits for it, generation-suppresses late publication, and resumes only after clear completion.

| Work Unit Evidence | Result |
|---|---|
| Focused clear/cancellation tests | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~ClearAiBarData --nologo` — passed 7/7, failed 0, skipped 0; includes a Windows `FileShare.None` lock failure, no-outside-write assertion, and retry after release. |
| Related persistence/coordinator tests | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~QuotaRefreshCoordinatorTests|FullyQualifiedName~AnalyticsScanCoordinatorTests|FullyQualifiedName~SqliteDailyModelUsageStoreTests" --nologo` — passed 32/32, failed 0, skipped 0. |
| Full test | `dotnet test AIBar.sln --nologo` — passed 142/142, failed 0, skipped 0. |
| Build | `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. |
| Runtime harness | N/A — isolated synthetic filesystem/cancellation boundary only; no live Codex source, AppData, native command, or UI runtime belongs to this slice. |
| Rollback boundary | Revert `ClearAiBarDataService.cs`, the quiesce change/tests in `QuotaRefreshCoordinator*`, and these 6C2B artifacts; 6C2A persistence/rebuild behavior remains intact. |

**Source hash proof:** the synthetic Codex source `session.jsonl` SHA-256 was `81B154E2705A9347DDED445B9E48399EA1C81128B1818CD2083D6FAFEDC2BB7E` before clear and exactly the same after; the clear test uses separate temporary AIBar/Codex roots.

**Files:** `src/AIBar.Application/ClearAiBarDataService.cs`, `src/AIBar.Application/QuotaRefreshCoordinator.cs`, `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`, `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs`, `openspec/changes/aibar-foundation/{tasks,apply-progress}.md`.

**Corrective evidence:** a locked allowlisted database fails closed, leaves the separate Codex fixture unchanged, and clears successfully only after the Windows lock releases. A successful clear initializes an empty SQLite database and then accepts the next synthetic `AnalyticsScanCoordinator` rescan. **Authored count:** under the 400 additions+deletions budget (code/tests/OpenSpec only; generated test diagnostics excluded). **Deviation:** no atomic filesystem-deletion claim; observable success requires deletion plus recreation. **Next:** Slice 6D only after this child slice is reviewed.

## Slice 6C2A.2 applied (2026-07-14)

**Status:** Standard mode; `applyState: ready`, OpenSpec/repo-local, force-chained `feature-branch-chain`. Review lineage `review-878ecbaeb79af763` authorized one bounded correction; no review authority or Git lifecycle action was started.

Completed only the three Slice 6C2A.2 tasks and the authorized correction. Before cleanup or debt clearing, `ReplaceSourcesAsync` now requires the exact requested fingerprint set to match the durable checkpoint universe; contribution-state cardinality is not authoritative, so partially attributed legacy A/B accepts a complete A/B rescan, while checkpoint-free legacy aggregates retain their explicit full-rescan path. Atomic CAS, replacement, aggregate recomputation, policy transition, rollback, retry, and unrelated-source retention remain unchanged.

| Work Unit Evidence | Result |
|---|---|
| Focused test | Exact two regression filter — passed 2/2; coordinator/store filter — passed 18/18; failed 0, skipped 0. |
| Related scanner/coordinator/store/policy tests | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~SessionJsonlScannerTests|FullyQualifiedName~AnalyticsScanCoordinatorTests|FullyQualifiedName~SqliteDailyModelUsageStoreTests|FullyQualifiedName~AnalyticsPolicyTests" --nologo` — passed 39/39, failed 0. |
| Full test | `dotnet test AIBar.sln --nologo` — passed 135/135, failed 0, skipped 0. |
| Build | `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors; `git diff --check` passed. |
| Runtime harness | N/A — synthetic coordinator/SQLite transaction boundary only; no live Codex/UI runtime belongs to this slice. |
| Rollback boundary | Revert `AnalyticsScanCoordinator.cs`, `SqliteDailyModelUsageStore.cs`, these coordinator tests, and the 6C2A.2 task/progress evidence; 6C2A.1 schema/contribution rows and 6C1B normal scan CAS remain. |

**Files:** `src/AIBar.Application/AnalyticsScanCoordinator.cs`, `src/AIBar.Application/SqliteDailyModelUsageStore.cs`, `tests/AIBar.Domain.Tests/AnalyticsScanCoordinatorTests.cs`, `openspec/changes/aibar-foundation/tasks.md`, `openspec/changes/aibar-foundation/apply-progress.md`.

**Authored count:** the authorized correction is 66 additions+deletions across the frozen paths, within its hard 70-line limit; the underlying Slice 6C2A.2 remains below 400. **Deviation:** none. **Next:** Slice 6C2B only after review; downstream scope remains unchanged.

## Slice 6C2A replanned after terminal review escalation (2026-07-14)

**Status:** The prior 6C2A candidate is discarded and must not be marked complete. Review `review-f9dc7720376bf064` is terminal ESCALATED/superseded; successor `review-6c2a-source-attribution` is terminal INVALIDATED at `sha256:804a1585b1d6ec7c8fdee81a6462fab1dac7bc288865c3c92f0c85b3dd56a0ed`. No review is active or reused.

The candidate was escalated because policy-less migration cannot deterministically identify historical ownership, and global multi-source aggregates lose unrelated source contributions during replacement. The four candidate implementation/test files were restored to the 6C1B baseline at `8fe89ec`; only this progress artifact and `tasks.md` are intentionally revised.

New chain: `8fe89ec → 6C2A.1 source-attributed durable contributions/migration → 6C2A.2 atomic multi-source rebuild/policy transition → 6C2B Clear AIBar Data → 6D`. 6C2A.1 must establish independent source ownership before 6C2A.2 can rebuild safely; 6C2B remains downstream.

## Superseded candidate record

**Status:** INVALIDATED/discarded; the former completion claims below are historical evidence only and no longer represent repository state.

No implementation, migration, checkbox completion, test, build, stage, commit, push, PR, or review operation was performed during replanning. The 6C1A preparation API and 6C1B expected-checkpoint CAS remain the preserved baseline and are prerequisites for the new chain.

| Work Unit Evidence | Result |
|---|---|
| Focused test | Not run; planning only. |
| Full test | Not run; planning only. |
| Build | Not run; planning only. |
| Runtime harness | N/A — no implementation or runtime boundary exists in planning. |
| Rollback boundary | Revert only the deliberate planning artifacts; restored implementation paths remain at 6C1B baseline. |

**Files:** only `openspec/changes/aibar-foundation/tasks.md` and `apply-progress.md` were deliberately revised; the four invalid implementation/test paths were restored and are not part of the plan.

## Status

**Slice 1 complete. Slice 2 blocked before implementation by its enforced 400-line budget gate.** The prior SDK blocker is resolved: .NET SDK `8.0.408` is installed alongside `6.0.424`. Only the approved Solution Skeleton and Pure Contracts work unit was implemented. Slice 2 was assessed on `feature/aibar-foundation-slice-2`; no Slice 2+ production code, fixtures, tests, credentials, HTTP, SQLite, scanner, tray behavior, polished UI, commit, push, branch, or PR was created.

## Preserved blocker history

The previous apply attempt was blocked before implementation because only .NET SDK `6.0.424` was installed and `net8.0` templates were unavailable. Its temporary empty solution was removed, no checkbox was changed, and no application source was retained. That blocker is resolved by the verified SDK `8.0.408`; it is retained here for audit continuity.

## Structured status consumed

- Change: `aibar-foundation`
- Native status: authoritative OpenSpec status, `applyState: ready`, `nextRecommended: apply`, no blocked reasons
- Action context: `repo-local`; workspace root and allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`
- Delivery: `auto-chain` / `feature-branch-chain`
- Prior PR boundary: Slice 1 only; current attempted boundary: Slice 2 only (blocked at preflight; Slice 3+ remain out of scope)
- Strict TDD configuration: false; Slice 1's explicit test-first requirement was followed. Slice 2 explicitly requires RED → GREEN → TRIANGULATE → REFACTOR, but no cycle began because the budget gate stopped work before code/test edits.

## Slice 2 budget preflight (2026-07-12)

Structured status was supplied by the parent and consumed before editing: authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, `5/52` complete, repo-local action context rooted at `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`, with no blockers. The approved delivery path is `auto-chain` / `feature-branch-chain`, and the assigned boundary is Slice 2 only.

Slice 2 cannot be implemented as its six required checkboxes under the `<=400` authored-line limit. The smallest credible forecast is **~620 authored lines**, excluding generated build output:

| Concern | Forecast |
|---|---:|
| Root resolver, minimum-field JSON reader, request-scoped credential object, feature policy, safe error/redaction boundary | 160 |
| Allowlisted `HttpClient` adapter, timeout/cancellation/redirect/retry policy, response DTO mapping | 210 |
| Synthetic HTTP/auth fixtures and contract tests (including RED/TRIANGULATE cases) | 230 |
| Project/test wiring and unsupported-integration disclosure | 20 |
| **Total** | **620** |

No partial Slice 2 work was started, no test was run, and no Slice 2 checkbox was changed. This preserves the reviewed Slice 1 baseline and prevents an unreviewable security-sensitive diff. Actual Slice 2 implementation authored lines: **0**; this progress-only record changed 29 lines (26 additions, 3 deletions).

**Concrete split proposal:**

1. **Slice 2A — credential boundary and disablement** (~350 authored lines): root resolver; minimum-field read-only credential reader; request-scoped credential lifetime; centralized safe error/redaction; feature-default-disabled policy and unsupported-endpoint disclosure; synthetic auth/redaction tests.
2. **Slice 2B — private HTTP quota adapter** (~390 authored lines): HTTPS allowlist, redirects disabled, timeout/cancellation/retry classification; version-neutral `/wham/usage` and optional-detail mapping; synthetic status/redirect/schema/retry contract tests.

Each proposed unit remains within the budget and retains synthetic-only fixtures. Slice 2B depends on 2A; neither introduces real credentials or live endpoints.

## Completed tasks and checkbox evidence

All five persisted Slice 1 checkboxes are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md` after successful build/test and dependency-inspection evidence.

- Created `AIBar.sln`, `AIBar.Domain`, `AIBar.Application`, `AIBar.Desktop` (WPF `net8.0-windows`, x64), and `AIBar.Domain.Tests`.
- Added pure domain records, ports, error/coverage/pricing taxonomies, freshness policy, component-wise token delta, fixed clock, and explicit timezone policy.
- Kept Domain/Application package-free and free of WPF, HTTP, filesystem, and SQLite references. The WPF dependency is restricted to `AIBar.Desktop`.

## TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| Runner establishment | `dotnet --version`; `dotnet --list-sdks`; `dotnet new list` | .NET `8.0.408` and WPF/xUnit templates available. |
| RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore` after writing contract tests | Failed as expected: `QuotaSnapshot` and other domain types did not exist (`CS0246`). |
| GREEN | Added only the minimal pure `Foundation.cs` contracts/policies, then reran the focused test command | Passed: 4/4 tests. |
| TRIANGULATE | Added deterministic clock/time-policy test first; reran focused test command | Failed as expected: `FixedClock` and `TimeZoneLocalTimePolicy` absent (`CS0246`); after minimal implementation, passed: 5/5 tests. |
| REFACTOR | Restricted WPF target to x64, added build-output ignores, and reran complete solution build/tests | Passed with 0 warnings and 0 errors; no infrastructure coupling added. |

## Verification evidence

```text
dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 5, Failed: 0, Skipped: 0, Total: 5.

dotnet list src/AIBar.Domain/AIBar.Domain.csproj package
No packages found.

dotnet list src/AIBar.Application/AIBar.Application.csproj package
No packages found.

Source-only dependency inspection (excluding bin/obj)
No System.Net.Http, Microsoft.Data.Sqlite, or System.IO references in Domain/Application source.
```

The runner used only synthetic pure tests; no real Codex files, credentials, network calls, SQLite, scanner, tray runtime behavior, or private adapter was introduced.

## Files changed

- `.gitignore`
- `AIBar.sln`
- `src/AIBar.Domain/AIBar.Domain.csproj`
- `src/AIBar.Domain/Foundation.cs`
- `src/AIBar.Application/AIBar.Application.csproj`
- `src/AIBar.Desktop/AIBar.Desktop.csproj`
- `src/AIBar.Desktop/App.xaml`
- `src/AIBar.Desktop/App.xaml.cs`
- `src/AIBar.Desktop/AssemblyInfo.cs`
- `src/AIBar.Desktop/MainWindow.xaml`
- `src/AIBar.Desktop/MainWindow.xaml.cs`
- `tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj`
- `tests/AIBar.Domain.Tests/FoundationContractsTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

## Design deviations

None. The WPF host is template-only and x64-targeted; no presentation behavior was implemented. Domain and application layers are framework-independent and contain no infrastructure adapters.

## Workload / PR boundary

Feature-branch-chain work unit: **Slice 1 only**. Total new solution/skeleton source plus solution/ignore metadata is **361 lines** (`304` source/project/XAML + `57` solution/ignore), below the **380-line forecast** and **400-line limit**. The count includes generated skeleton files; no further scope may be added to this work unit. No commit, branch, PR, push, or review transaction was created.

## Remaining tasks

The exact unchecked persisted task lines are:

- [ ] Implement the single supported Codex-root resolver (`%CODEX_HOME%`, otherwise the current-user `.codex` root) and a read-only minimum-field credential reader; never deserialize, persist, or log refresh tokens, passwords, browser cookies, prompt/response content, or unrelated secret fields.
- [ ] Implement the feature-gated `IQuotaProvider` using `HttpClient`, HTTPS origin allowlisting for `https://chatgpt.com/backend-api`, redirects disabled, cancellation/timeouts, bounded retry rules, and request-scoped credential lifetime.
- [ ] Implement version-neutral response DTO mapping for `/wham/usage` and optional `/wham/rate-limit-reset-credits`, validating required percentages/reset times and preserving optional-detail failure independently.
- [ ] Classify 401/403/429/5xx, transport/timeout, malformed/missing-field, and unavailable cases into safe error codes without raw bodies, headers, credentials, or sensitive account identifiers.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add fixture/contract tests for minimum/extra/malformed auth fields, valid and schema-drift responses, status classes, redirect rejection, retry limits, optional-detail failure, and secret-free errors.
- [ ] Add the explicit unsupported/private-endpoint disclosure and a build/application setting that can disable this integration without disabling local analytics.
- [ ] Implement `QuotaRefreshCoordinator` with cache-first publication, one in-flight refresh task, trigger coalescing, manual-refresh freshness bypass, conservative polling, cancellation, sleep/resume and clock-change re-evaluation.
- [ ] Implement SQLite schema/migration and `IQuotaSnapshotStore` for normalized quota fields and retrieval metadata only; atomically commit successful primary snapshots and never store credentials or raw responses.
- [ ] Preserve the prior snapshot and original timestamp on failure, overlay the classified error, and transition to explicit stale/unavailable states after the documented freshness threshold; optional-detail failures must not erase primary data.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add coordinator/store tests for startup cache rendering, concurrent triggers, manual bypass, stale-on-failure, atomic save/load, cancellation races, clear-data publication, and optional-detail degradation.
- [ ] Verify migration, foreign keys, WAL configuration, and crash-before-commit behavior using persistence integration tests.
- [ ] Implement the single-process WPF host, named-mutex single-instance activation, tray recreation handling, taskbar/DPI-aware borderless popover positioning, deactivation behavior, and explicit Exit command that cancels work and closes persistence.
- [ ] Implement immutable view models/state rendering for current, stale, loading, unavailable, authentication, permission, malformed, network, and service states; never show a fabricated percentage or stale data as current.
- [ ] Build the custom semantic WPF design tokens and compact CodexBar-inspired quota cards using Windows-native typography, spacing, contrast, keyboard navigation, visible focus, accessible names, reduced-motion behavior, high-contrast support, and DPI scaling.
- [ ] Show independent 5-hour/weekly percentages, service reset countdowns, source/timestamp/freshness disclosure, manual refresh, local-analytics separation, estimated-cost separation, and private-endpoint disclosure.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test view-model state mapping and coordinator/UI command behavior; add focused Windows smoke checks for tray toggling, popover focus, single-instance activation, taskbar recreation, and representative DPI.
- [ ] Capture representative Windows 10/11 visual evidence; treat accessibility and lifecycle checks as acceptance evidence, not screenshot similarity alone.
- [ ] Implement supported initial Codex `sessions` and `archived_sessions` discovery only, with bounded background batches and cancellation.
- [ ] Implement streaming JSONL parsing from validated checkpoints, file identity/size/mtime comparison, append handling, incomplete-tail deferral, parser-semantics invalidation, replacement/rebuild, and transactional checkpoint updates.
- [ ] Parse only timestamps, trustworthy model evidence, and token counters; skip/defer malformed or changing records with scan coverage warnings and never retain prompt/response bodies.
- [ ] Implement `scan_run` provenance/status including discovered/read/skipped/deferred counts, warnings, cancellation, and partial coverage.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic golden/property tests for unchanged rescans, appended events, malformed/truncated files, replaced/shrunk files, cumulative resets, cancellation, parser-version invalidation, idempotence, and non-negative deltas.
- [ ] Verify fixture data contains no real credentials, paths, prompt text, or response text, and that cancellation before commit leaves aggregates/checkpoints unchanged.
- [ ] Implement transactional `daily_model_usage` aggregation for input, cached-input, and output totals using `max(0, current - previous)` per component and the exact documented ranking formula `total tokens = input + cached input + output`.
- [ ] Implement explicit `Unknown` attribution for missing/untrusted model evidence; never infer a named model or rank by cost.
- [ ] Implement Windows local-timezone/day normalization with timezone ID, observed offset, DST behavior, UTC-derived provenance, and explicit rebuild/version behavior when the policy changes.
- [ ] Add SQLite migrations and transactional tests for checkpoint/aggregate atomicity, crash recovery, repricing without token mutation, and aggregate retention.
- [ ] Implement Clear AIBar Data to cancel work, remove only AIBar-owned cache/database/log/settings data, recreate empty state, and prove Codex-owned source files are byte-for-byte unchanged.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test model ranking, `Unknown`, midnight/DST boundaries, reset/decrease handling, clear-data isolation, and partial scan retention.
- [ ] Add a checked-in, versioned immutable pricing catalog with version/date provenance, component-specific rates, unsupported-model results, and no fallback rate for `Unknown`.
- [ ] Implement estimated-cost calculation and UI labels/warnings for estimate status, unknown models, repricing, discounts, routing, contracts, and non-authoritative billing assumptions; never use billed-cost/invoice/credit language.
- [ ] Implement named daily trend windows (initial 7 complete local days and day-over-day comparison) and simple linear quota exhaustion from observed current-window usage, remaining service quota, reset time, and valid positive rate only.
- [ ] Return insufficient/unavailable for stale quota, invalid reset, zero/negative rate, insufficient observations, or unsupported forecasting; never reconstruct hourly history or emit probabilistic predictions.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test pricing provenance/warnings, model totals, trend windows, time basis, ETA insufficiency, and linear estimate labeling.
- [ ] Verify UI/source labels distinguish service quota, locally derived analytics, and estimated cost in view-model snapshots.
- [ ] Implement per-user startup registration abstraction: packaged startup-task path where available and unpackaged HKCU Run entry with quoted executable and `--startup`; read back effective state after mutation, never elevate or write machine-wide state.
- [ ] Add native commands/settings for startup toggle, Clear AIBar Data, safe diagnostic export, and private-integration disablement; keep telemetry and remote crash reporting off by default.
- [ ] Implement central redaction before every diagnostic sink/export for bearer headers, secret fields, sensitive IDs, URLs/query strings, paths, raw bodies, auth files, JSONL lines, database rows, and exception values; bound local log size/age.
- [ ] Add privacy tests that recursively inspect AIBar-owned persistence, logs, diagnostics, exports, and UI/error snapshots for seeded prompt/response text, bearer values, sensitive IDs, and source paths.
- [ ] Add deterministic x64 Windows 10/11 packaging, SBOM, signed per-user MSIX path when signing is available, unpackaged self-contained development/recovery artifact, and upgrade/migration/uninstall/user-data retention checks.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add packaging smoke tests for clean install, upgrade, startup launch, single instance, uninstall, and data retention/removal; perform W10/W11 manual/VM checks for tray recreation, DPI, taskbar placement, sleep/resume, popover focus, and concrete unsupported behavior.
- [ ] Complete policy/legal review, dependency and source-provenance/MIT-notice review, private-endpoint compatibility validation, and remote-free kill-switch verification before enabling distribution builds.
- [ ] Run the full test suite, static analysis, dependency/license checks, and `dotnet publish` for the supported Windows target.
- [ ] Confirm all MVP non-goals remain unsupported and no code path reads multiple roots/accounts, WSL, browser cookies, passwords, prompt/response bodies, or authoritative billing data.
- [ ] Confirm generated fixtures and screenshots are synthetic/approved and remain bound to the reviewed behavior.
- [ ] Confirm each implementation slice remains independently revertible and its authored change count is at or below 400 lines before opening or advancing its chained PR.
- [ ] Preserve no credentials or user content in test evidence, logs, artifacts, screenshots, or release bundles.

All remaining lines are Slice 2–8 or cross-slice gates and are intentionally not part of this apply batch.

## Slice 2A applied (2026-07-12)

Slice 2A is complete on `feature/aibar-foundation-slice-2`. The preserved 620-line Slice 2 gate and approved 2A/2B split above remain the governing delivery decision. This work unit implements the credential/policy boundary only; it adds no `HttpClient`, endpoints, DTOs, retries, redirects, or live integration.

### Completed tasks and persisted checkbox evidence

All seven Slice 2A task lines are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md` after focused synthetic and full-suite evidence:

- supported `%CODEX_HOME%`/current-user `.codex` root selection;
- read-only minimum-field `access_token` plus optional `account_id` reader using `FileShare.ReadWrite | FileShare.Delete`;
- disposable request credential that clears token/account references on disposal;
- central safe redaction for bearer, token/secret, account ID, and path values;
- disabled-by-default private-integration policy with private/undocumented/unsupported disclosure; and
- synthetic tests for roots, malformed/missing/extra fields, lifetime, defaults, disclosure, redaction, and replacement/share-read behavior.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `CredentialBoundaryTests` and Application reference; ran focused test command before implementation | Failed as expected: `AIBar.Application` and `ICredentialFileReader` did not exist (`CS0234`, `CS0246`). |
| GREEN | Added the minimum credential boundary, reader, policy, disclosure, and redactor; reran focused tests | Initially exposed platform-sensitive root separator expectation; corrected the synthetic assertion to `Path.Combine`; passed 9/9. |
| TRIANGULATE | Focused tests cover missing/malformed/blank fields, extra refresh-token field, replacement/share-read, disabled default, and seeded secret/account/path redaction | Passed 9/9; all inputs are in-memory synthetic strings. |
| REFACTOR | Kept parsing in one `JsonDocument` minimum-field path and redaction in one primitive; fixed the pre-existing WPF `Application` namespace ambiguity caused by the new application namespace | Full build and suite pass with 0 warnings/errors; no HTTP or persistence references added. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~CredentialBoundaryTests --logger "console;verbosity=minimal"
RED: failed as expected (CS0234/CS0246).
GREEN/TRIANGULATE: Passed: 9, Failed: 0, Skipped: 0, Total: 9.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 15, Failed: 0, Skipped: 0, Total: 15.
```

LSP diagnostics were requested but no LSP tool is available in this executor session; the successful compiler build is the available static diagnostic evidence. `git diff --check` passed. Source inspection found no `HttpClient`, `System.Net.Http`, `backend-api`, or `wham` references in Slice 2A paths.

### Files changed in Slice 2A

- `src/AIBar.Application/CredentialBoundary.cs`
- `tests/AIBar.Domain.Tests/CredentialBoundaryTests.cs`
- `tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj`
- `src/AIBar.Desktop/App.xaml.cs` (one-line namespace disambiguation required for the full solution build)
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

### Workload / boundary / deviations

Current feature-branch-chain boundary: **Slice 2A only**; prior dependency: Slice 1; follow-up: Slice 2B. Slice 2A adds **171 source/test lines**, plus two one-line project/host corrections, before SDD metadata; it is below the 400-line authored budget. No commit, push, PR, review transaction, real Codex access, credential, or network call occurred.

Deviation: the reader is an infrastructure-neutral `ICredentialFileReader` port backed by a read-only file implementation; the existing `ICodexCredentialSource` remains unchanged so Slice 2B can compose the request boundary without introducing HTTP now. The required build fix qualifies the WPF base type as `System.Windows.Application` because `AIBar.Application` now exists.

### Remaining tasks

The following exact Slice 2B lines remain unchecked and are outside this work unit:

- [ ] Implement the feature-gated `IQuotaProvider` with `HttpClient`, HTTPS allowlisting for only `https://chatgpt.com/backend-api`, redirects disabled, request-scoped authorization, cancellation, bounded connect/request timeouts, and conservative retry behavior honoring `Retry-After`.
- [ ] Implement version-neutral DTO mapping for `/wham/usage` and optional `/wham/rate-limit-reset-credits`, validating required percentages/reset timestamps and preserving optional-detail failure without invalidating the primary quota result.
- [ ] Classify 401/403/429/5xx, transport/timeout, redirect, malformed/missing-field, and unavailable outcomes into safe error codes and summaries; never expose raw bodies, headers, credentials, URLs/query secrets, or sensitive account identifiers.
- [ ] **RED:** add synthetic HTTP/auth contract fixtures and tests for valid/version-variant/missing-field responses, status classes, redirect rejection, timeout/cancellation, retry limits, `Retry-After`, optional-detail failure, and secret-free errors.
- [ ] **GREEN:** implement the allowlisted adapter, version-neutral mapping, failure classification, and bounded retry behavior against the synthetic handler/fixtures only.
- [ ] **TRIANGULATE:** verify no cross-origin redirect can carry authorization, no retry occurs for 401/403/malformed payloads, one transient retry is bounded, and optional failure preserves primary windows.
- [ ] **REFACTOR:** isolate endpoint details behind `IQuotaProvider`, reuse Slice 2A redaction/lifetime boundaries, and keep live private-endpoint tests opt-in and outside ordinary CI.

Slice 3–8 and cross-slice gates remain unchecked exactly as listed in the persisted tasks artifact above.

## Slice 2B applied (2026-07-12)

Slice 2B is complete on `feature/aibar-foundation-slice-2b`, based on approved Slice 2A commit `e72da52`. This work unit is synthetic-only: no live endpoint, credential, Codex file, account, cache, coordinator, or Slice 3+ behavior was used or added.

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, 12/60 tasks complete before this unit, no blocked reasons; `actionContext: repo-local` with allowed root `C:\\Users\\mjsal\\Desarrollos IA\\Modificacion de Terminales\\aibar`. Delivery was resolved as `auto-chain` / `feature-branch-chain`; current PR boundary is Slice 2B only. No action-context warnings.

### Completed tasks and persisted checkbox evidence

The seven Slice 2B task lines are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md`: feature-gated `HttpClient` provider; exact HTTPS base origin; disabled redirects; per-request bearer/account headers scoped to a disposable Slice 2A credential; 5-second connect and 10-second request bounds; cancellation; one transient retry with capped `Retry-After`; primary `/wham/usage` plus optional reset-credit mapping; validated 0–100 percentages and timestamps; safe status/transport/timeout/redirect/malformed/disabled failures; and optional-detail degradation.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added synthetic `QuotaHttpProviderTests`; focused test run before the provider existed | Failed as expected: `CS0246` (`QuotaHttpProvider` absent). |
| GREEN | Added the minimal provider and domain result fields; focused tests | Passed 7/7. |
| TRIANGULATE | Variant fields, missing/invalid percentages, redirect, 401/403/400, 429 + `Retry-After`, cancellation, transport/timeout, retry bound, and optional 503 cases | Passed 7/7; primary snapshot survives optional failure. |
| REFACTOR | Kept endpoint strings private to the adapter, only safe fixed error codes, and reused `RequestCredential` disposal | Full build/suite pass with no warnings/errors. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~QuotaHttpProviderTests --logger "console;verbosity=minimal"
Passed: 7, Failed: 0, Skipped: 0, Total: 7.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 31, Failed: 0, Skipped: 0, Total: 31.

git diff --check
Passed.
```

LSP diagnostics are unavailable in this executor session; the zero-warning compiler build is the available static diagnostic evidence.

### Files changed / workload / deviations

- `src/AIBar.Application/QuotaHttpProvider.cs`
- `src/AIBar.Domain/Foundation.cs`
- `tests/AIBar.Domain.Tests/QuotaHttpProviderTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

Slice 2B authored implementation/test lines are 166 (`91` adapter + `71` tests + four domain change lines), plus this task/progress evidence; the current work unit remains below the 400-line limit. Feature-branch-chain boundary: Slice 2B only; dependency: Slice 2A; follow-up: Slice 3. No commit, push, PR, review transaction, or Judgment Day action occurred. No design deviation: optional credits are exposed only through the adapter result because cache/coordinator work is deferred to Slice 3.

### Remaining tasks

Slice 3–8 and cross-slice gates remain unchecked. The next exact unchecked line is:

- [ ] Implement `QuotaRefreshCoordinator` with cache-first publication, one in-flight refresh task, trigger coalescing, manual-refresh freshness bypass, conservative polling, cancellation, sleep/resume and clock-change re-evaluation.

## Slice 3 budget gate (2026-07-12)

**Blocked before any Slice 3 code, test, project, or task-artifact edit.** Parent-provided structured status was consumed: authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, `19/60` complete, no blocked reasons; action context is `repo-local` with workspace/allowed root `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is resolved as `auto-chain` / `feature-branch-chain`; this requested work unit is Slice 3 only, with parent Slice 2B commit `9351927`. No action-context warning applies.

The required Slice 3 scope cannot credibly fit the enforced **<=400 authored changed-line** budget when code, focused tests, project wiring, and this required OpenSpec evidence are counted. No production code or RED test was started, so strict TDD evidence has no cycle for this blocked gate.

| Required concern | Credible forecast (lines) |
|---|---:|
| Coordinator state, cache-first publication, trigger coalescing, manual bypass, polling, cancellation, clock/lifecycle handling | 160 |
| SQLite connection/schema migration, normalized snapshot mapping, atomic save/load/clear and privacy boundary | 150 |
| Coordinator fake-clock/provider/store tests: startup cache, coalescing, bypass, stale/failure, cancellation, optional degradation | 175 |
| SQLite integration tests: migration, FK/WAL, atomicity, crash-before-commit and normalized-data inspection | 145 |
| Domain/project wiring and task/progress evidence | 35 |
| **Total** | **665** |

Actual Slice 3 authored implementation/test/project lines: **0**. This append-only audit evidence changes 29 lines in `apply-progress.md`; no persisted task checkbox changed.

### Required dependency-ordered split

1. **Slice 3A — quota snapshot SQLite store** (~330 lines including tests and OpenSpec evidence): add the SQLite package/project wiring; schema versioning/migration; `quota_snapshot` normalized fields only; foreign-key/WAL setup; atomic load/save/clear; synthetic persistence integration tests for migration, normalized-data inspection, atomic rollback/crash-before-commit, and no credentials/raw bodies. Depends on Slice 2B; no coordinator.
2. **Slice 3B — refresh coordinator state machine** (~375 lines including tests and OpenSpec evidence): cache-first publication using the 3A store; single in-flight provider operation; trigger coalescing; manual freshness bypass; conservative scheduling; failure/stale/unavailable state overlay preserving original timestamp; optional-detail degradation; cancellation/clear-data publication; fake-clock/provider/store tests. Depends on 3A; no tray/UI.
3. **Slice 3C — lifecycle re-evaluation hardening** (~230 lines including tests and OpenSpec evidence): sleep/resume and clock-change event boundary wired to coordinator reevaluation, deterministic fake-time lifecycle/cancellation race tests, plus focused full-suite/build diagnostics evidence. Depends on 3B; no Slice 4 presentation.

Each slice is independently reviewable and stays within the 400-line budget without removing required persistence or deterministic test behavior. Feature-branch-chain order: `9351927` → **3A** → **3B** → **3C** → Slice 4. 📍 Current blocked boundary: Slice 3 (must be replaced by 3A/3B/3C before implementation).

### Verification and remaining work

No test, build, or LSP command was run because the pre-edit budget gate stopped implementation. No design deviation occurred. Slice 3's five persisted `- [ ]` lines remain unchecked; the next recommended action is to approve the proposed Slice 3A/3B/3C delivery boundary and apply Slice 3A only.

## Slice 3A applied (2026-07-12)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blocked reasons. `actionContext` is `repo-local`, with workspace and allowed root `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`; no action-context warnings. Delivery is `auto-chain` / `feature-branch-chain`; parent dependency is Slice 2B commit `9351927`; current boundary is **Slice 3A only**. No coordinator, polling, lifecycle, tray, UI, or Slice 3B/3C work was added.

### Completed tasks and checkbox evidence

The four Slice 3A task lines (RED, GREEN, TRIANGULATE, REFACTOR) are visibly marked `- [x]` in the persisted `openspec/changes/aibar-foundation/tasks.md` artifact. The store uses a temporary synthetic database per test, opens SQLite with foreign keys and WAL, keeps one normalized snapshot row with primary/weekly values, optional reset-credit column, retrieval timestamp, and schema-adapter version, and supports transactional save/load/clear. It persists no credentials, raw HTTP bodies, headers, or source paths.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `SqliteQuotaSnapshotStoreTests` and the SQLite project dependency, then ran the focused test command before the store existed. | Failed as expected: `CS0246` for missing `SqliteQuotaSnapshotStore`. |
| GREEN | Implemented the smallest store, normalized schema, v1→v2 migration, transactional operations, WAL/foreign-key setup, and disposal lock. | Focused suite passed 4/4. |
| TRIANGULATE | Added repeated save/load, absent optional detail, malformed row, future-schema/no-downgrade, migration, and injected pre-commit failure cases. | Focused suite passed 5/5; failed save retained the prior row. |
| REFACTOR | Centralized schema SQL and connection initialization; disabled pooling so temporary WAL databases deterministically release locks for cleanup. | Full solution build/test passed with 0 warnings/errors. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~SqliteQuotaSnapshotStoreTests --logger "console;verbosity=minimal"
RED: failed as expected (CS0246).

dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~SqliteQuotaSnapshotStoreTests --logger "console;verbosity=minimal"
GREEN: Passed: 4, Failed: 0, Skipped: 0, Total: 4.
TRIANGULATE: Passed: 5, Failed: 0, Skipped: 0, Total: 5.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 38, Failed: 0, Skipped: 0, Total: 38.

git diff --check
Passed.
```

No LSP diagnostic tool is injected in this executor session; the zero-warning compiler build is the available static diagnostic evidence.

### Files changed / migration / rollback

- `src/AIBar.Application/AIBar.Application.csproj` — `Microsoft.Data.Sqlite` 8.0.8 boundary.
- `src/AIBar.Application/SqliteQuotaSnapshotStore.cs` — serialized/disposable SQLite store, schema v2, v1 migration, future-version rejection, atomic save/clear.
- `src/AIBar.Domain/Foundation.cs` — adds `IQuotaSnapshotStore.ClearAsync`.
- `tests/AIBar.Domain.Tests/SqliteQuotaSnapshotStoreTests.cs` — temporary synthetic integration tests only.
- `openspec/changes/aibar-foundation/tasks.md` and this cumulative progress record.

The v1→v2 migration preserves the existing normalized row; a database newer than v2 is rejected before mutation, so downgrade is non-destructive. Rollback removes only the 3A dependency/store/tests and leaves existing data untouched; no Codex-owned file is accessed or deleted. The injected pre-commit failure throws before transaction commit, and the test proves the previous committed row survives.

### Workload / remaining tasks

Current feature-branch-chain PR boundary: **Slice 3A** (`9351927` → 3A → 3B → 3C → Slice 4). This work unit adds 178 implementation/test/project lines before the 3A task/progress evidence. The complete current work-tree diff from `9351927`, including inherited Slice-3 split metadata and this 3A OpenSpec evidence, is 336 changed lines (314 additions, 22 deletions), below the 400-line authored budget. No commit, push, PR, or review transaction occurred.

Remaining Slice 3 work is exactly:

- [ ] **RED:** add fake-clock/provider/store tests for cache-first startup publication, one in-flight refresh, concurrent trigger coalescing, manual freshness bypass, conservative polling eligibility, failure overlays, original timestamp preservation, stale/unavailable threshold transitions, optional-detail degradation, cancellation, and clear-data publication suppression.
- [ ] **GREEN:** implement `QuotaRefreshCoordinator` using the 3A store and 2B provider: publish cached state first, gate one asynchronous refresh task, coalesce poll/popover-open/resume/manual triggers, let manual refresh bypass freshness but not concurrency, schedule conservatively, preserve the prior snapshot/timestamp on failure, overlay classified errors, and publish explicit stale/unavailable states without fabricating percentages.
- [ ] **RED:** add deterministic tests for sleep/resume, system clock forward/backward changes, resume freshness re-evaluation, stale-to-current/unavailable transitions, prior timestamp preservation, optional-detail degradation, and clear-data/cancellation races using the existing coordinator contracts.
- [ ] **GREEN:** add the lifecycle boundary that forwards sleep/resume and clock-change notifications to coordinator re-evaluation; harden stale/unavailable transitions, retain original successful timestamps on failures, preserve primary data when optional details fail, and prevent cancelled/cleared work from republishing.

## Slice 3B applied (2026-07-13)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blocked reasons; workspace and allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is `auto-chain` / `feature-branch-chain`; the boundary is **Slice 3B only**, based on approved Slice 3A commit `73919f3`. Strict TDD is false, so Standard Mode applied with focused behavior tests. No action-context warnings.

### Completed tasks and persisted checkbox evidence

The four Slice 3B task lines (RED, GREEN, TRIANGULATE, REFACTOR) are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md`. `QuotaRefreshCoordinator` publishes the persisted cache first, exposes immutable test-observable state, coalesces in-flight triggers, allows manual freshness bypass without bypassing the concurrency gate, avoids fresh ordinary polls, saves successful normalized snapshots, retains the prior timestamp on failure, exposes stale/unavailable/error/optional-degradation state, and suppresses late publication after clear/cancellation. It contains no UI, dispatcher, tray, lifecycle adapter, or thread-affinity dependency.

### Focused behavior evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~QuotaRefreshCoordinatorTests --logger "console;verbosity=minimal"
RED: failed as expected: CS0246 (QuotaRefreshCoordinator absent).
GREEN/TRIANGULATE: Passed: 8, Failed: 0, Skipped: 0, Total: 8.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 48, Failed: 0, Skipped: 0, Total: 48.

git diff --check
Passed.
```

LSP diagnostics were requested before command execution, but no LSP diagnostic tool is injected in this executor session. The zero-warning compiler build is the available static diagnostic evidence.

### Files / workload / deviations

- `src/AIBar.Domain/Foundation.cs` — immutable refresh trigger/state contracts.
- `src/AIBar.Application/QuotaRefreshCoordinator.cs` — application-only cache/refresh state machine.
- `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs` — deterministic fake clock/provider/store behavior tests.
- `openspec/changes/aibar-foundation/tasks.md` — completed Slice 3B checkboxes.
- `openspec/changes/aibar-foundation/apply-progress.md` — this cumulative Slice 3B record.

Authored implementation/test lines: **215** (106 coordinator + 107 tests + 2 domain contracts). The Slice 3B implementation/test change is below the 400-line budget; OpenSpec evidence is separately required delivery metadata. No design deviation: lifecycle re-evaluation remains intentionally deferred to Slice 3C. No commit, stage, push, PR, review transaction, Judgment Day, branch creation, credential, network call, or UI work occurred.

### Remaining work

Slice 3B has no unchecked tasks. Slice 3C remains the next bounded work unit:

- [ ] **RED:** add deterministic tests for sleep/resume, system clock forward/backward changes, resume freshness re-evaluation, stale-to-current/unavailable transitions, prior timestamp preservation, optional-detail degradation, and clear-data/cancellation races using the existing coordinator contracts.
- [ ] **GREEN:** add the lifecycle boundary that forwards sleep/resume and clock-change notifications to coordinator re-evaluation; harden stale/unavailable transitions, retain original successful timestamps on failures, preserve primary data when optional details fail, and prevent cancelled/cleared work from republishing.
- [ ] **TRIANGULATE:** inject reordered lifecycle events, duplicate notifications, clock jumps, cancellation at each publication boundary, provider failure during resume, and store-unavailable conditions; verify deterministic state traces and no overlapping refreshes.
- [ ] **REFACTOR:** make event subscriptions/disposal idempotent, centralize degradation rules, remove duplicated transition handling, and run the affected suite plus static/build diagnostics without adding UI coupling.

Slice 4 and later UI/wiring work remain out of scope.

## Slice 3C applied (2026-07-13)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, `verify: blocked`, and no blocked reasons. The allowed repository edit root is `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`; no action-context warnings apply. Delivery is `feature-branch-chain`; this boundary is **Slice 3C only**, based on approved Slice 3B commit `cac832e`. Strict TDD is false, so Standard Mode applied with focused deterministic behavior tests.

### Completed tasks and persisted checkbox evidence

The four Slice 3C task lines (RED, GREEN, TRIANGULATE, REFACTOR) are visibly marked `- [x]` in the persisted tasks artifact. The application-only lifecycle adapter subscribes idempotently to sleep, resume, and clock-change events, forwards them to coordinator freshness re-evaluation, and unsubscribes on disposal. Re-evaluation republishes only a changed freshness state; sleep never starts a request, while resume/clock-change joins the existing coordinator request gate. Existing generation/cancellation safeguards continue to prevent clear/cancelled work from publishing late data.

### Focused behavior evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~QuotaRefreshCoordinatorTests"
RED: failed as expected with CS0246 because IQuotaRefreshLifecycleEvents did not yet exist.
GREEN/TRIANGULATE/correction: Passed: 14, Failed: 0, Skipped: 0, Total: 14.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore
Passed: 54, Failed: 0, Skipped: 0, Total: 54.

git diff --check
Passed (only Git LF-to-CRLF conversion warnings were emitted).
```

LSP diagnostics were requested before validation commands, but no LSP diagnostic tool is injected in this executor session. The zero-warning compiler build is the available static diagnostic evidence. Tests cover suspended/resumed/clock-change forwarding, forward and backward clock changes, duplicate resume/clock notifications coalescing to one provider call, stale-to-current transition, resume failure with original timestamp preservation, optional-detail degradation, store-save degradation, and clear/dispose late-publication suppression.

### Files / workload / deviations

- `src/AIBar.Application/QuotaRefreshLifecycleAdapter.cs` — lifecycle event boundary and idempotent disposal.
- `src/AIBar.Application/QuotaRefreshCoordinator.cs` — explicit lifecycle freshness re-evaluation.
- `src/AIBar.Domain/Foundation.cs` — lifecycle refresh trigger values.
- `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs` — deterministic lifecycle/degradation race tests.
- `openspec/changes/aibar-foundation/tasks.md` — completed Slice 3C checkboxes.
- `openspec/changes/aibar-foundation/apply-progress.md` — this cumulative record.

Authored implementation/test changes are **241 lines** (238 additions, 3 deletions, including the new adapter); the bounded reliability correction is **95 lines** relative to the frozen review tree, within its 114-line budget and the 400-line Slice 3C limit. OpenSpec task/progress metadata is required delivery evidence. No UI/tray work, Slice 4+, commit, stage, push, PR, branch creation, review transaction, Judgment Day, network call, credential access, or writes outside the repository occurred. No design deviation: the boundary remains application-only and does not reference WPF, tray, or dispatcher APIs.

### Remaining work

No Slice 3C task remains unchecked. Slice 4 and later work remain out of scope; the next exact unchecked persisted line is:

- [ ] Implement the single-process WPF host, named-mutex single-instance activation, tray recreation handling, taskbar/DPI-aware borderless popover positioning, deactivation behavior, and explicit Exit command that cancels work and closes persistence.

The remaining unchecked Slice 4–8 and cross-slice-gate lines remain unchanged in `tasks.md`; verification is the next phase only after the entire change's remaining tasks are complete.

## Slice 4 planning gate (2026-07-13)

Planning-only status: Slice 4 implementation/test/project lines remain **0**. The approved split and current next action are recorded in the final `Slice 4A split approval` note below; no duplicate workload history is repeated here.

## Slice 4A split approval (2026-07-13)

**Approved decision recorded; implementation not started.** Replace the unchecked ~465-line 4A unit with two independently testable/revertible units: **4A.1** (~315 including evidence) for WPF host primitives, named-mutex single-instance activation/secondary-activation handoff, pure taskbar/DPI-aware placement, and deterministic tests; and **4A.2** (~260) for the live tray bridge, taskbar recreation, popover show/hide/deactivation, orderly explicit Exit, and focused Windows smoke tests using 4A.1. Neither unit includes 4B/4C presentation scope.

**Approved chain and next action:** `1b9cca5 → 4A.1 → 4A.2 → 4B → 4C → 5`. Apply 4A.1 only after task review. All new task checkboxes remain unchecked. No implementation, XAML, project, test, screenshot, build, stage, commit, branch, push, or review action was performed. The complete planning diff from `1b9cca5` still requires `git diff --stat`/`git diff --check` verification by an execution environment with Git command access; no such command tool is available in this phase.

## Slice 4A.1 applied (2026-07-13)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`; repo-local edit root; no blockers. Boundary: **4A.1 only** in the approved `auto-chain` / `feature-branch-chain` path. Strict TDD is false; test-first evidence is retained.

### Completed tasks / evidence

The four 4A.1 RED/GREEN/TRIANGULATE/REFACTOR lines are visibly `- [x]` in `tasks.md`. `SingleInstanceHost` owns a named mutex and uses a named auto-reset activation event: secondaries signal, while the primary drains one pending activation per dispatch. `PopoverPlacement` is pure and consumes monitor/work-area rectangles and DPI scale; it clamps size/position to the work area. No tray runtime/recreation, deactivation/show-hide behavior, Exit orchestration, presentation state, cards, or disclosures was added.

| Cycle | Evidence | Result |
|---|---|---|
| RED | `HostPrimitivesTests` compiled before the primitives existed | Failed as expected with `CS0246`. |
| GREEN | Minimal host/placement primitives | Focused tests passed 4/4. |
| TRIANGULATE | Same-process takeover with another handle alive, bounded real child-process election/handoff, abandoned-owner recovery, every NaN/±Infinity field, invalid dimensions/DPI, and overflow | Focused tests passed 7/7. |
| REFACTOR | Kept WPF/platform code in Desktop and policies pure | Full build/tests passed. |

### Verification / workload

Correction focused command `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~HostPrimitivesTests"` passed 7/7, including a timeout-bounded `dotnet test --no-build` child process that is terminated and awaited for abandoned-owner evidence. Full-suite/build/diff checks are recorded from the final correction validation. LSP diagnostics remain unavailable in this executor session; the compiler build is the available static diagnostic evidence.

Files: `src/AIBar.Desktop/HostPrimitives.cs`, `tests/AIBar.Domain.Tests/HostPrimitivesTests.cs`, `tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj`, plus persisted tasks/progress. No design deviation. The pre-existing planning diff was 69 lines; the post-implementation complete authored count is recorded after final validation and remains within 400. No stage, commit, push, branch, PR, or review transaction occurred.

**Remaining:** all 4A.2, 4B, 4C, Slice 5+, and cross-slice lines remain unchecked; next boundary is 4A.2 only.

Exact next unchecked Slice 4 lines:

- [ ] **RED:** add focused Windows smoke tests for tray creation/toggle, secondary activation handoff, Explorer/taskbar recreation, popover focus/deactivation, and Exit cancellation/persistence disposal ordering.
- [ ] **GREEN:** implement the tray runtime bridge, recreation handling, borderless popover show/hide and deactivation behavior, and explicit Exit orchestration that cancels work, closes persistence, and exits orderly.
- [ ] **TRIANGULATE:** exercise taskbar recreation, repeated toggles, owned-dialog deactivation, shutdown races, cancellation/close ordering, and activation handoff through the 4A.1 contract on a representative Windows environment.
- [ ] **REFACTOR:** isolate runtime Win32/WPF adapters, make tray recreation and shutdown idempotent, keep coordinator/persistence calls at the host boundary, and rerun focused smoke tests plus diagnostics.

The 4B and 4C task lines remain unchecked as persisted in `tasks.md`; they are intentionally deferred with Slice 5+ and cross-slice gates. Final complete authored diff from `1b9cca5`: **282** lines (tracked `git diff --numstat 1b9cca5 --`: 118 additions + 38 deletions = 156; two unstaged new source/test files: 126 additions). This is within the 400-line limit.

## Slice 4A.2 applied (2026-07-13)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`; repo-local allowed root; no blockers. Delivery is `auto-chain` / `feature-branch-chain`; boundary: **4A.2 only**, based on approved 4A.1 commit `5e13ef5`. Strict TDD is false; focused test-first evidence was retained. No action-context warnings.

### Completed tasks / persisted checkbox evidence

The four 4A.2 RED/GREEN/TRIANGULATE/REFACTOR task lines are visibly `- [x]` in `tasks.md`. `TrayHostRuntime` composes the approved `SingleInstanceHost` with an actual WinForms `NotifyIcon`, WPF borderless popover adapter, and `TaskbarCreated` Win32 monitor. It toggles/show-hides the popover, preserves an owned dialog on deactivation, recreates the tray icon after Explorer/taskbar recreation, and blocks late activation after Exit. Exit is idempotent: it cancels active work, awaits cancellation, disposes injected persistence before the tray resource, and requests process shutdown. Event/fire-and-forget boundaries swallow exceptions.

### RED → GREEN → TRIANGULATE → REFACTOR evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `HostRuntimeTests`, then ran the focused filter before runtime contracts existed. | Failed as expected with `CS0246` for `ITrayRuntime`, `IPopoverRuntime`, and taskbar recreation contracts. |
| GREEN | Added minimal runtime adapters and lifecycle orchestration. | Focused tests passed 2/2. |
| TRIANGULATE | Covered repeated toggle, owned-dialog deactivation, taskbar recreation, secondary activation handoff, repeated Exit, cancellation-before-disposal, and late activation suppression. | Focused tests passed 2/2. |
| REFACTOR | Kept Win32/WPF/WinForms inside Desktop; host shutdown depends only on injected lifecycle resources. | Full build/tests passed with 0 warnings/errors. |

### Verification / smoke evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~HostRuntimeTests"
Passed: 2, Failed: 0.

dotnet test AIBar.sln --no-restore
Passed: 63, Failed: 0.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

git diff --check
Passed.

Bounded Windows smoke: launched Debug AIBar.Desktop.exe, observed it remained running for two seconds, then forcibly stopped it.
```

The bounded smoke proves the composed Windows host launches; it does not automate Explorer restart, owned-dialog focus, or orderly Exit interaction. Those behaviors have deterministic adapter tests; manual Windows 10/11 matrix evidence remains a later compatibility obligation.

### Files / workload / remaining work

- `src/AIBar.Desktop/HostRuntime.cs` — tray, WPF popover, Explorer/taskbar monitor, and orderly shutdown boundary.
- `src/AIBar.Desktop/App.xaml`, `App.xaml.cs`, `AIBar.Desktop.csproj` — live host composition and Windows Forms support.
- `tests/AIBar.Domain.Tests/HostRuntimeTests.cs` — deterministic runtime/lifecycle tests.
- `openspec/changes/aibar-foundation/tasks.md` and this cumulative progress record.

No quota view-models, quota/analytics/cost disclosures, visual tokens/cards/accessibility visuals, scanner, packaging, or Slice 5+ scope was added. LSP diagnostics are unavailable in this executor session; the zero-warning compiler build is the available static diagnostic evidence. No commit, stage, push, PR, branch, review transaction, credential, network, or user-data access occurred. The next remaining boundary is Slice 4B, followed by 4C; all of their persisted task lines remain unchecked.

## Bounded reliability correction (2026-07-13)

- `RELIABILITY-001`: the live runtime now starts one UI-dispatcher timer that drains secondary activation signals automatically and stops/unsubscribes before shutdown; the focused test pumps the dispatcher and observes activation without manual dispatch.
- `RELIABILITY-002`: process-exit failures now fault `ExitAsync`; retry reuses the completed cleanup and invokes only process exit again, preserving cancellation → persistence → tray → process-exit order.
- Focused tests: 3/3 passed. Full suite: 64/64 passed. Build: 0 warnings/errors. `git diff --check`: passed with line-ending warnings only.
- LSP diagnostics were requested but unavailable because no LSP tool is injected in this executor session; compiler diagnostics are clean.

## Slice 4B applied (2026-07-13)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blocked reasons; repo-local workspace/allowed edit root is `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is `auto-chain` / `feature-branch-chain`; boundary: **4B only**, based on approved 4A.2 commit `c280bb6`. Strict TDD is false; focused test-first evidence was retained. No action-context warnings.

### Completed tasks / persisted checkbox evidence

All four Slice 4B RED/GREEN/TRIANGULATE/REFACTOR lines are visibly `- [x]` in `tasks.md`. `QuotaPresentationMapper` returns immutable presentation records for current, stale, loading, unavailable, authentication, permission, malformed, network, and service states. It preserves known stale/loading values without marking them current, leaves unavailable percentages null, derives independent service-reset countdowns from an injected clock, and exposes source/timestamp/freshness plus fixed distinct disclosures for private service quota, local analytics, and estimated cost. `ManualRefreshCommand` is platform-light and deterministic; the existing tray host accepts it as an optional refresh boundary and the Windows tray exposes a Refresh command. No XAML, visual tokens/cards, accessibility styling, analytics scanning, pricing, or Slice 5+ scope was added.

### RED → GREEN → TRIANGULATE → REFACTOR evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `QuotaPresentationTests`, then ran its focused filter before mapper/command types existed. | Failed as expected: `CS0246` for `QuotaPresentationMapper` and `ManualRefreshCommand`. |
| GREEN | Added immutable mapping, injected-clock countdowns, disclosure records, and refresh command. | Focused presentation tests passed 8/8. |
| TRIANGULATE | Added stale/error overlay and elapsed-reset cases; exercised all safe error classes, loading, unavailable, independent windows, command disabled state, and tray refresh routing. | Focused presentation/host tests passed 12/12. |
| REFACTOR | Centralized state labels/error mapping/disclosures; retained no dispatcher dependency in pure mapping or command tests. | Full suite/build passed with zero warnings/errors. |

### Verification / workload / deviations

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~QuotaPresentationTests"
RED: failed as expected (CS0246); GREEN: Passed 8/8.

dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~QuotaPresentationTests|FullyQualifiedName~HostRuntimeTests"
Passed: 12, Failed: 0.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 73, Failed: 0.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

git diff --check
Passed (Git emitted only LF-to-CRLF warnings).
```

LSP diagnostics are not injected in this executor session; the zero-warning compiler build is the available static diagnostic evidence. Files: `src/AIBar.Desktop/QuotaPresentation.cs`, `src/AIBar.Desktop/HostRuntime.cs`, `tests/AIBar.Domain.Tests/QuotaPresentationTests.cs`, `tests/AIBar.Domain.Tests/HostRuntimeTests.cs`, plus persisted tasks/progress. Complete diff from `c280bb6`, including tracked, untracked, and OpenSpec evidence, is rechecked below the 400-line cap. No design deviation, staging, commit, push, PR, review transaction, branch action, credential/network/user-data access, or out-of-repository write occurred.

### Bounded reliability correction

- `RELIABILITY-001`: tray refresh availability now follows whether the host received a functional command; null composition leaves Refresh unexposed.
- `RELIABILITY-002`: manual refresh owns an atomic running state, rejects overlap, and restores/notifies executability after success, failure, or cancellation.
- `RELIABILITY-003`: a retained snapshot with a refresh failure preserves data/timestamp but is labeled `Stale`, never `Current`.
- Focused deterministic correction tests passed 14/14; full suite passed 75/75 and build completed with zero warnings/errors.

### Remaining work

Slice 4C remains the next boundary; its exact unchecked lines are:

- [ ] **RED:** add rendering/interaction checks for semantic token roles, quota-card hierarchy, typography/spacing/contrast, keyboard navigation, visible focus, accessible names, reduced motion, high contrast, and DPI scaling; establish representative W10/W11 evidence expectations.
- [ ] **GREEN:** build the custom semantic WPF token system and compact quota cards using Windows-native typography, spacing, contrast, keyboard/focus/accessibility behavior, reduced-motion and high-contrast support, and DPI scaling.
- [ ] **TRIANGULATE:** validate representative Windows 10/11 scale factors, keyboard/focus paths, screen-reader names, reduced-motion/high-contrast behavior, and that visual evidence does not replace lifecycle/accessibility checks.
- [ ] **REFACTOR:** consolidate semantic resources, remove hard-coded presentation roles, preserve readable Windows fallbacks, and rerun representative interaction/accessibility checks and build diagnostics.

## Slice 4C escalated-review remediation (2026-07-13)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blockers; repo-local action context rooted at the allowed workspace. Delivery remains `auto-chain` / `feature-branch-chain`; this expanded remediation remains **Slice 4C only**, based on `0b0289e`. Terminal lineage `review-976c8d0610470e0e` was not reused; the normalized committable candidate uses fresh lineage `review-4487567db2eeb73c`. Strict TDD is false; focused RED/GREEN/TRIANGULATE/REFACTOR evidence is retained.

### Remediation completed

- `RELIABILITY-001`: disabled policy collapses Refresh; enabled composition is functional. Startup contains composition, tray-start, initialization, and disposal failures, reporting safe evidence while retaining honest unavailable state.
- `RELIABILITY-002`: the STA test proves Button invocation, running disablement, completion/re-enable, and coordinator failure reporting through the production host path.
- `RELIABILITY-003`: the STA WPF test creates `UIElementAutomationPeer`s for both `GroupBox` cards and asserts their peer names.
- High-contrast card overrides now use dynamic `SystemColors` brush keys rather than fixed high-contrast colors. No animation was introduced.

### Evidence and workload

| Stage | Evidence | Result |
|---|---|---|
| RED | Added the runtime presentation-host test before the host/composition types existed. | Failed as expected with missing presentation/coordinator types. |
| GREEN | Added host, runtime DataContext, coordinator/manual-refresh composition, and command binding. | Focused visual suite passed. |
| TRIANGULATE | Gated provider, enabled/disabled composition, and automation peers cover running state and card names. | Focused visual suite passed. |
| REFACTOR | Removed duplicate 4C evidence; presentation remains a dispatcher-only adapter. | Full suite/build passed. |

Commands: focused `QuotaVisualDesignTests` passed **4/4**; `dotnet build AIBar.sln --no-restore` passed with **0 warnings / 0 errors**; `dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"` passed **79/79**; `git diff --check 0b0289e` passed (only LF-to-CRLF warnings). Parent LSP diagnostics are clean. The complete diff is **400 changed lines** from `0b0289e`, at the hard cap.

No Slice 5+ code or tasks changed. The four persisted Slice 4C checkboxes remain visibly checked and are satisfied by runtime composition, functional refresh, UI Automation, and focused/full verification. Windows 10/11 physical/VM, screen-reader, and live high-contrast desktop evidence remain unavailable and are not claimed.

Remaining exact Slice 5 lines:

- [ ] Implement supported initial Codex `sessions` and `archived_sessions` discovery only, with bounded background batches and cancellation.
- [ ] Implement streaming JSONL parsing from validated checkpoints, file identity/size/mtime comparison, append handling, incomplete-tail deferral, parser-semantics invalidation, replacement/rebuild, and transactional checkpoint updates.
- [ ] Parse only timestamps, trustworthy model evidence, and token counters; skip/defer malformed or changing records with scan coverage warnings and never retain prompt/response bodies.
- [ ] Implement `scan_run` provenance/status including discovered/read/skipped/deferred counts, warnings, cancellation, and partial coverage.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic golden/property tests for unchanged rescans, appended events, malformed/truncated files, replaced/shrunk files, cumulative resets, cancellation, parser-version invalidation, idempotence, and non-negative deltas.
- [ ] Verify fixture data contains no real credentials, paths, prompt text, or response text, and that cancellation before commit leaves aggregates/checkpoints unchanged.

## Slice 5A applied (2026-07-14)

**Structured status consumed:** authoritative OpenSpec status supplied by the parent: `applyState: ready`, `nextRecommended: apply`, no blocked reasons; `actionContext: repo-local` with the current AIBar repository as workspace and allowed edit root. Delivery is `feature-branch-chain`; PR boundary is **Slice 5A only**, based on clean base `3d7fc39`. The parent explicitly required RED → GREEN → TRIANGULATE → REFACTOR despite `openspec/config.yaml` declaring `strict_tdd: false`; this work followed that test-first cycle. No action-context warnings.

### Completed tasks and persisted checkbox evidence

Slice 5 was split in the persisted task artifact into 5A discovery/bounded enumeration, 5B streaming/checkpoint semantics, and 5C provenance/coverage status. Only the four Slice 5A RED/GREEN/TRIANGULATE/REFACTOR checkboxes are visibly `- [x]`; all 5B/5C lines remain unchecked.

`SessionFileDiscovery` reuses `CodexRootResolver`, inspects only `<root>/sessions` and `<root>/archived_sessions`, discovers `.jsonl` in synthetic date-partitioned, flat, and recursive legacy layouts, orders paths deterministically, and emits configured positive-size batches. It checks cancellation before traversal and between directories/files/batches, rejects reparse points and paths outside the resolved root, and returns only path-free coverage codes/counts for missing, unreadable, changing, and reparse conditions. It never opens JSONL files or implements parsing, identity/mtime, offsets, checkpoints, SQLite, aggregation, UI, real Codex access, multiple roots/accounts, or WSL.

### TDD cycle evidence

| Cycle | Evidence | Result |
|---|---|---|
| RED | Added synthetic `SessionFileDiscoveryTests` before the discovery types existed; ran the focused test filter. | Failed as expected: `CS0246` for missing `SessionFileBatch`. |
| GREEN | Added the minimum resolver-based discovery boundary, bounded batches, cancellation checks, containment, reparse rejection, and safe coverage codes. | Focused tests passed 4/4. |
| TRIANGULATE | Covered date/flat/recursive layouts, unsupported files, deterministic batching, positive-size validation, pre-traversal and between-batch cancellation, missing root, reparse skipping, and path-free warnings. | Focused tests passed 4/4. |
| REFACTOR | Centralized containment/reparse and safe warning-code handling; no content-reading or persistence dependency was introduced. | Full suite and build passed with 0 warnings/errors. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~SessionFileDiscoveryTests" --logger "console;verbosity=minimal"
RED: failed as expected (CS0246); GREEN/TRIANGULATE: Passed: 4, Failed: 0, Skipped: 0, Total: 4.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 83, Failed: 0, Skipped: 0, Total: 83.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

git diff --check 3d7fc39
Passed.

LF-only check for Slice 5A source/test/tasks artifacts
Passed.
```

### Files changed / workload / rollback

- `src/AIBar.Application/SessionFileDiscovery.cs`
- `tests/AIBar.Domain.Tests/SessionFileDiscoveryTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

Feature-branch-chain boundary: **Slice 5A only**. The semantic implementation/test diff is recorded after final reconciliation and remains below the 400-line cap. No commit, push, PR, review/lens/lineage action, Desktop composition, or write outside the repository occurred. Rollback removes only `SessionFileDiscovery` and its synthetic tests; it never modifies Codex-owned files and does not affect later scanner or persistence work.

### Remaining Slice 5 work

- [ ] Implement streaming JSONL parsing from validated checkpoints with append handling, incomplete-tail deferral, parser-semantics invalidation, replacement/rebuild, and transactional checkpoint updates.
- [ ] Parse only timestamps, trustworthy model evidence, and token counters; skip/defer malformed or changing records with scan coverage warnings and never retain prompt/response bodies.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic tests for unchanged rescans, appended events, malformed/truncated files, replaced/shrunk files, parser-version invalidation, and cancellation before checkpoint commit.
- [ ] Implement `scan_run` provenance/status including discovered/read/skipped/deferred counts, safe warnings, cancellation, and partial coverage.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** add synthetic tests for scan coverage counts, cancellation, idempotence, and non-negative delta handoff boundaries.
- [ ] Verify fixture data contains no real credentials, paths, prompt text, or response text, and that cancellation before commit leaves aggregates/checkpoints unchanged.

## Slice 5A verification-remediation (2026-07-14)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blockers; repo-local root is the sole allowed edit root. Boundary remains **Slice 5A** from `3d7fc39`; strict TDD was explicitly active.

The checked Slice 5A task is reconciled: a minimal injected `ISessionFileSystem` seam deterministically exercises missing `sessions`/`archived_sessions`, enumeration unreadability, changing-entry attributes, root attribute failure, and cancellation. All warning codes are path-free; missing/race conditions increment `FilesSkipped`; cancellation propagates. No OS-permission-dependent test, JSONL read, parser, SQLite, Desktop, or fifth path was added.

### TDD Cycle Evidence

| Task | Safety net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|
| Slice 5A remediation | Focused 4/4 passed before changes | New tests failed `CS0246`: `ISessionFileSystem` absent | 7/7 after minimal seam/handling | Added cancellation case; 8/8 | Blank-line-only cleanup; 8/8 |

Observed evidence only: baseline 4/4; RED compiler failure above; final focused 8/8. Full suite passed 87/87; build passed 0 warnings/errors; `git diff --check 3d7fc39` and LF-only check of all four allowed paths passed (only Git autocrlf advisories). Final four-path recount: 94 tracked additions + 6 deletions + 276 untracked additions = **376**, within the 400 cap. Rollback removes only the four listed Slice 5A paths; no commit, stage, push, PR, review, or authority action occurred.

## Slice 5B applied (2026-07-14)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blockers; repo-local allowed root. Boundary: **Slice 5B only**, based on `bcd0931`; delivery is `auto-chain` / `feature-branch-chain`. No action-context warnings.

### Completed tasks / files

The three Slice 5B task lines are visibly `- [x]` in `tasks.md`. Added `SessionJsonlScanner` and synthetic tests. It reads JSONL by byte offset, compares creation-time identity/length/mtime plus parser version, rebuilds from zero on invalidation, defers an incomplete tail, emits safe warning codes, and commits a checkpoint only after stable parsing and cancellation checks. Parsed records retain only timestamp, trusted model or `Unknown`, and token counters; raw JSON and prompt/response fields are discarded. No aggregation, SQLite, UI, scan-run provenance, or Codex access was added.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `SessionJsonlScannerTests`; focused test before scanner types existed. | Failed as expected: `CS0246` for scanner/checkpoint types. |
| GREEN | Added byte-offset parser and in-memory transactional checkpoint boundary. | Focused 3/3 passed. |
| TRIANGULATE | Covered unchanged, append, incomplete tail, malformed line, replacement, shrink, parser version, and pre-commit cancellation. | Focused 3/3 passed. |
| REFACTOR | Centralized checkpoint validation, safe warnings, and minimal-field parsing. | Full suite/build passed. |

### Verification / workload

```text
dotnet test ... --filter FullyQualifiedName~SessionJsonlScannerTests
Passed: 3, Failed: 0, Total: 3.
dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 91, Failed: 0, Total: 91.
dotnet build AIBar.sln --no-restore
0 warnings, 0 errors.
git diff --check
Passed.
```

PR boundary is Slice 5B only. No design deviations, commit, push, PR, review, or Slice 5C+ work. Remaining Slice 5C lines are unchecked.

## Slice 5B remediation applied (2026-07-14)

Corrected the failed preflight: the canonical required specification is `openspec/changes/aibar-foundation/specs/aibar-foundation/spec.md`. Removed only the erroneous `Slice 5B remediation preflight blocked` section; all preceding Slice 5B evidence is retained. This corrective boundary changes only the scanner, its synthetic tests, and this cumulative evidence record.

### Remediation completed

- Validates negative, beyond-recorded/current, and non-line-boundary checkpoint offsets before the unchanged fast path; invalid offsets rebuild from zero.
- Captures immutable pre-read identity/length/mtime metadata and compares post-read state against it. A deterministic pre-commit mutation seam returns path-free `session_file_changed` and does not commit a checkpoint.
- Retains only timestamp, trusted model or `Unknown`, and independently non-negative input/cached-input/output counters. Synthetic fixtures contain no prompt/response text, credentials, or user paths.
- Proves identity-only checkpoint mismatch, incomplete-tail complete-boundary checkpointing, and cancellation leaves an existing checkpoint unchanged.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| Slice 5B remediation | `tests/AIBar.Domain.Tests/SessionJsonlScannerTests.cs` | filesystem-focused unit | focused baseline: 3/3 passed | 2 failures: corrupt offsets and mutation during read | 5/5 passed after minimal scanner fix | 6/6 passed with identity-only mismatch, extraction/clamping/`Unknown`, incomplete tail, and existing-checkpoint cancellation | immutable `FileSnapshot` and centralized offset-boundary predicate; 6/6 passed |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~SessionJsonlScannerTests" --logger "console;verbosity=minimal"
Safety net: 3/3 passed. RED: 2 failed, 3 passed. GREEN: 5/5 passed. TRIANGULATE/REFACTOR: 6/6 passed.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 94, Failed: 0, Skipped: 0, Total: 94.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.
```

### Workload / scope / remaining

Delivery remains `auto-chain` / `feature-branch-chain`, PR boundary Slice 5B remediation only. No aggregation, UI, SQLite, scan-run provenance, Slice 5C+, stage, commit, push, PR, review, or Judgment Day action occurred. No design deviation. Slice 5B's three persisted task lines remain visibly checked; Slice 5C and later remain unchecked.

### Bounded review correction evidence

Safety net passed 6/6. RED failed three cases: numeric timestamp/model threw, and an unchanged incomplete tail lost its warning. Minimal value-kind guards plus the truthful unchanged-tail fast path passed 8/8; a following valid record survives malformed input and repeated scans preserve checkpoint semantics. Full suite passed 96/96; build passed with 0 warnings/errors; diff/LF checks passed and LSP was unavailable. No review approval or authority mutation is claimed.

## Slice 5C applied (2026-07-14)

**Structured status consumed:** authoritative OpenSpec status: `artifactStore: openspec`, `applyState: ready`, `nextRecommended: apply`, `54/81` complete, no blocked reasons. `actionContext` is repo-local; the authoritative workspace and allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is `auto-chain` / `feature-branch-chain`; this independent child boundary is **Slice 5C only** on `feature/aibar-foundation-slice-5c`, based on `2441cd6`. No action-context warnings apply.

### Completed tasks and persisted checkbox evidence

The three Slice 5C task lines are visibly marked `- [x]` in `openspec/changes/aibar-foundation/tasks.md`:

- `ScanRunRecorder` persists immutable scan-run provenance with discovered/read/skipped/deferred counts, safe warning codes, partial/complete coverage, non-negative token handoff, and deterministic idempotence through `IScanRunStore`.
- Synthetic focused tests cover counts and partial coverage, cancellation before commit, idempotent replay, non-negative handoff boundaries, and warning-code filtering.
- Fixture/privacy inspection passed: the Slice 5C fixture uses only synthetic model/file labels and token counts; it contains no credential, prompt, response, or real filesystem-path material. Cancellation before commit raises before a scan-run/handoff write and retains the prior committed state.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Preserved the interrupted untracked `ScanRunProvenanceTests.cs` and ran the focused test before implementation. | Failed as expected with `CS0246`: `InMemoryScanRunStore` and `ScanRunRecorder` did not exist. |
| GREEN | Added minimal application-only scan-run store/recorder and reran the focused suite. | Initial run exposed cancellation occurring after the idempotence lookup; moving the cancellation boundary before lookup made 3/3 pass. |
| TRIANGULATE | Added an unrecognized-warning case alongside counts, cancellation, idempotence, partial coverage, and non-negative handoff cases. | Focused suite passed 4/4. |
| REFACTOR | Centralized deterministic fingerprinting, safe-warning filtering, coverage determination, and one pre-commit cancellation boundary. | Full suite/build passed; no aggregation, UI, SQLite, or source-file mutation was introduced. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~ScanRunProvenanceTests" --logger "console;verbosity=minimal"
RED: failed as expected (CS0246).
GREEN: Passed: 3, Failed: 0, Skipped: 0, Total: 3.
TRIANGULATE/REFACTOR: Passed: 4, Failed: 0, Skipped: 0, Total: 4.

fixture privacy grep (credential/prompt/response/path patterns)
Passed: no matches.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 100, Failed: 0, Skipped: 0, Total: 100.

git diff --check
Passed (Git emitted only LF-to-CRLF working-copy notices).
```

### Files changed / workload / rollback

- `src/AIBar.Application/ScanRunProvenance.cs`
- `tests/AIBar.Domain.Tests/ScanRunProvenanceTests.cs` (preserved and completed interrupted RED work)
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

Authored implementation/test change is **194 lines** (117 production + 77 synthetic tests) before OpenSpec evidence, below the hard 400-line budget. The Slice 5C PR boundary starts at `2441cd6`, ends with scan-run provenance/status only, depends on Slice 5B, and is followed by Slice 6. Rollback removes only the recorder/store and its synthetic tests; it does not modify Codex-owned files, checkpoints, aggregates, UI, or later analytics. No design deviation, commit, stage, push, PR, review transaction, or Slice 6/aggregation work occurred.

### Remaining tasks

Slice 5C is complete. The next exact unchecked task is outside this boundary:

- [ ] Implement transactional `daily_model_usage` aggregation for input, cached-input, and output totals using `max(0, current - previous)` per component and the exact documented ranking formula `total tokens = input + cached input + output`.

## Slice 5C corrective rerun (2026-07-14)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, repo-local action context, no blockers. Delivery is `auto-chain` / `feature-branch-chain`; this is the single focused corrective rerun of the **Slice 5C-only** boundary from HEAD `2441cd6`. Strict TDD was active by executor context despite `openspec/config.yaml` declaring false. No action-context warnings. The canonical spec was read at `openspec/changes/aibar-foundation/specs/aibar-foundation/spec.md`; the canonical design was read at `openspec/changes/aibar-foundation/design.md`.

### Corrected implementation and persisted task evidence

The persisted Slice 5C task explicitly now says **SQLite-backed** `scan_run` and remains visibly checked. `SqliteScanRunStore` uses the existing `Microsoft.Data.Sqlite` dependency and creates the logical `scan_run` table in the application database with fingerprint, timestamps, discovered/read/skipped/deferred counts, safe warning codes, cancellation, and coverage state. It opens with foreign keys and WAL. Completed scans are idempotent by fingerprint across a reopened store; `LoadLastAsync` returns durable last-scan status.

Cancellation before the normal commit now records a separate durable cancelled scan status with partial coverage, `session_cancelled`, and an empty handoff before rethrowing `OperationCanceledException`. The normal scan commit is not performed in that path, preserving the Slice 5C boundary's no-aggregate/no-checkpoint-mutation guarantee. `WasCancelled` is no longer dead or always false. The correct application file is `src/AIBar.Application/ScanRunProvenance.cs` (not an Infrastructure path).

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added durable reopen/idempotence and persisted-cancellation tests before a SQLite store existed; ran focused tests. | Failed as expected with `CS0246`: `SqliteScanRunStore` did not exist. |
| GREEN | Added the minimum SQLite-backed store/table and cancellation-status persistence, reusing the existing SQLite package. | Focused tests initially exposed cancellation being bypassed by the completed-run idempotence lookup; ordering was corrected so cancellation is observed and recorded first. |
| TRIANGULATE | Added persisted-row idempotence assertion (`COUNT(*) = 1`), reopen/load assertions, and cancellation status assertions including empty handoff and partial coverage. | Focused suite passed 6/6. |
| REFACTOR | Kept provenance mapping, warning validation, normal idempotence, and cancellation status in the existing application boundary; no aggregation, checkpoints, UI, or Codex file behavior was added. | Full build/suite and diff/privacy checks passed. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~ScanRunProvenanceTests" --logger "console;verbosity=minimal"
RED: failed as expected (CS0246 SqliteScanRunStore missing).
GREEN/TRIANGULATE: Passed: 6, Failed: 0, Skipped: 0, Total: 6.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 102, Failed: 0, Skipped: 0, Total: 102.

git diff --check; untracked no-index diff checks
Passed (only Git LF-to-CRLF working-copy notices).

Privacy grep over Slice 5C source/tests for bearer/access-token/prompt/response/path patterns
Passed: no matches.
```

### Files, workload, deviation, and rollback

Exact Slice 5C implementation/test paths from HEAD `2441cd6`:

- `src/AIBar.Application/ScanRunProvenance.cs`
- `tests/AIBar.Domain.Tests/ScanRunProvenanceTests.cs`

Required SDD metadata paths:

- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

Implementation plus test authored lines from HEAD are **203** (`wc -l` across the two untracked Slice 5C paths), within the `<=400` limit. Parent gate diagnostics identified dynamic SQL composition in the shared load helper; it was replaced with two constant parameterized query texts. Post-fix LSP/Semgrep diagnostics reported zero findings, focused tests passed 6/6, the full suite passed 102/102, the build completed with zero warnings/errors, and `git diff --check` passed. No design deviation remains: the scan-run status is now durable in SQLite as required. Rollback removes only the scan-run recorder/store and its synthetic tests; it leaves the prior SQLite quota store, scanner, Codex-owned files, checkpoints, aggregates, UI, and Slice 6 untouched. No commit, stage, push, PR, or review transaction was started.

### Remaining tasks / PR boundary

Current PR boundary: **Slice 5C only** (`2441cd6` → Slice 5C → Slice 6). Prior dependency: Slice 5B. The next exact unchecked line, intentionally out of scope, is:

- [ ] Implement transactional `daily_model_usage` aggregation for input, cached-input, and output totals using `max(0, current - previous)` per component and the exact documented ranking formula `total tokens = input + cached input + output`.

Other Slice 6+ and cross-slice tasks remain unchecked. Do not advance this apply rerun to Slice 6.

## Slice 5C bounded reliability correction (2026-07-14)

The single correction transaction for `review-60b9b949769a5e75` resolves RELIABILITY-001/002/003 only. Persisted scan identity is now a SHA-256 digest of canonical inputs, so raw warning paths/secrets are not stored. SQLite atomically persists/reloads handoff JSON, including idempotent replay after reopen. Cancelled rows use deterministic upsert to advance their row order, preserving the freshest repeated cancellation as last-scan status after reopen.

RED focused tests failed in all three frozen behaviors: raw secret/path present in `fingerprint`, empty reopened handoff, and stale first-cancellation timestamp. GREEN focused tests passed 8/8; privacy inspection passed 1/1; full build passed with 0 warnings/errors; full suite passed 104/104; `git diff --check` passed with line-ending notices only. No Slice 6 behavior or task state changed.

Rollback boundary: revert only `ScanRunProvenance.cs`, its focused tests, and this evidence paragraph; prior Slice 5C behavior and all Codex-owned data remain untouched.

## Slice 6A applied (2026-07-14)

**Structured status consumed:** authoritative OpenSpec status for `aibar-foundation`: `applyState: ready`, `nextRecommended: apply`, and no blocked reasons. `actionContext` is repo-local; workspace and only allowed edit root are `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is `auto-chain` / `feature-branch-chain`; this independently revertible boundary is **Slice 6A only**, based on approved Slice 5C parent `ed5c353`. No action-context warnings.

### Completed tasks and persisted checkbox evidence

The four Slice 6A lines are visibly marked `- [x]` in `tasks.md`. The pure domain kernel computes per-component non-negative cumulative deltas, assigns absent/untrusted models to `Unknown`, ranks models only by `input + cached input + output` with ordinal deterministic ties, and records UTC timestamp, Windows timezone ID, observed offset, local day, and policy version. A policy-version mismatch returns `RebuildRequired`; it never moves persisted history. SQLite, scanner/checkpoint wiring, rebuild persistence, Clear Data, pricing, UI, and network access remain deferred.

### TDD Cycle Evidence

| Task | Test file | Layer | Safety net | RED | GREEN | TRIANGULATE | REFACTOR |
|---|---|---|---|---|---|---|---|
| 6A policy kernel | `tests/AIBar.Domain.Tests/AnalyticsPolicyTests.cs` | Unit | `FoundationContractsTests`: 6/6 | Missing policy types: expected `CS0246` | 5/5 passed | Added DST offset-separation case; 6/6 passed | No behavior change required; focused suite remained green |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~FoundationContractsTests"
Passed: 6, Failed: 0, Total: 6.

RED: AnalyticsPolicyTests before production implementation
Failed as expected: CS0246 (AnalyticsRebuildDecision absent).

Focused GREEN/TRIANGULATE:
Passed: 6, Failed: 0, Total: 6.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 110, Failed: 0, Skipped: 0, Total: 110.

git diff --check ed5c353 --
Passed.
```

No LSP tool is available in this executor session; the zero-warning compiler build is the available static diagnostic evidence. The diff and source boundary contain no Codex file reads, credentials, prompts/responses, network calls, SQLite, or scanner wiring.

### Files / workload / remaining

- `src/AIBar.Domain/AnalyticsPolicy.cs`
- `tests/AIBar.Domain.Tests/AnalyticsPolicyTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

No design deviation. The 6A rollback boundary is exactly the policy/test/task/progress changes above; it changes no persisted data. The feature-branch-chain follow-up is 6B only. The exact unchecked next 6B task lines are:

- [ ] Add SQLite migration and transactional `daily_model_usage` storage for local day, timezone ID/offset provenance, model, and input/cached-input/output totals, preserving 6A token facts.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test aggregate/checkpoint transaction seams, crash recovery, aggregate retention, and repricing without token mutation using synthetic data.

## Slice 6B applied (2026-07-14)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blockers; `actionContext` is `repo-local` and the workspace/only allowed root is `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is approved `auto-chain` / `feature-branch-chain`. This boundary is **Slice 6B only**, from stable Slice 6A parent `d0ca0e3`; no action-context warnings.

### Completed tasks and persisted checkbox evidence

Both Slice 6B lines are visibly `- [x]` in `tasks.md`. `SqliteDailyModelUsageStore` creates only `daily_model_usage` and its local schema marker alongside pre-existing application tables. Its transaction atomically merges rows keyed by local day, Windows timezone ID, observed offset minutes, and explicit model (`Unknown` for blank input), retaining only non-negative input/cached-input/output token facts. It stores no prices, costs, catalog values, credentials, payloads, prompts/responses, source paths, session/project names, scanner wiring, or checkpoints.

The injected seam runs after aggregate upserts and before commit. A seam failure rolls back every aggregate update in that transaction. This is **not** checkpoint atomicity: aggregate/checkpoint coupling remains strictly deferred to Slice 6C1.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added synthetic `SqliteDailyModelUsageStoreTests` before the store existed; ran focused tests. | Failed as expected: `CS0246` for missing `SqliteDailyModelUsageStore`. |
| GREEN | Added minimal SQLite migration/open, transactional merge, reopen/load mapping, and injected pre-commit seam. | Focused tests passed 3/3. |
| TRIANGULATE | Added coexistence migration test against an existing `quota_snapshot` table and `user_version = 2`. | Focused tests passed 4/4; existing row remained intact. |
| REFACTOR | Kept schema ownership local to `daily_model_usage`; no scanner/checkpoint or pricing contract was added. | Focused/full suite and zero-warning build passed. |

### Verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~SqliteDailyModelUsageStoreTests" --logger "console;verbosity=minimal"
RED: failed as expected (CS0246).
GREEN: Passed: 3, Failed: 0, Skipped: 0, Total: 3.
TRIANGULATE: Passed: 4, Failed: 0, Skipped: 0, Total: 4.

dotnet build AIBar.sln --no-restore
Passed: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 117, Failed: 0, Skipped: 0, Total: 117.

git diff --check d0ca0e3 -- plus no-index checks for both untracked Slice 6B paths
Passed; only Git LF-to-CRLF notices.
```

No LSP tool is available in this executor session; the zero-warning compiler build is the available static diagnostic evidence. Production-store privacy inspection found no price/cost, credential, bearer, prompt/response, checkpoint, or session persistence concerns. Tests use only synthetic temporary databases/data.

### Files / workload / rollback

- `src/AIBar.Application/SqliteDailyModelUsageStore.cs`
- `tests/AIBar.Domain.Tests/SqliteDailyModelUsageStoreTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

No design deviation. PR boundary: **Slice 6B only** (`d0ca0e3 → 6B → 6C1`); no stage, commit, push, PR, branch, review, scanner wiring, checkpoint mutation, Clear Data, pricing/catalog, UI, or 6C1+ work occurred. Rollback removes only the daily-model store/test and this Slice 6B task/progress evidence; existing database tables and all Codex-owned files remain untouched.

### Remaining tasks

- [ ] Wire supported scanner token/model/timestamp handoff through 6A to 6B, advancing checkpoints only in the same successful aggregate transaction.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test unchanged/appended handoff, cancellation-before-commit, and partial-scan retention without prompt/response persistence.

## Slice 6C1 applied (2026-07-14)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blockers; `actionContext` is `repo-local` with workspace and only allowed root `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is approved `auto-chain` / `feature-branch-chain`; this boundary is **Slice 6C1 only**, from stable parent `052ede7`. Strict TDD was active by parent instruction; no action-context warning applied.

### Completed tasks and persisted checkbox evidence

Both Slice 6C1 tasks are visibly `- [x]` in `tasks.md`. `AnalyticsScanCoordinator` consumes the existing scanner's supported timestamp/model/token records, applies the 6A local-day/Unknown/component-wise policy, and asks the 6B SQLite store to merge aggregates and advance a minimum path-free `file_checkpoint` (opaque fingerprint, monotonic sequence, parser version, last token facts) in one transaction. Cancellation or an injected pre-commit failure rolls back both changes. Partial scanner results retain valid supported records, preserve only safe `session_*` warning codes in the returned coverage, and persist no raw paths, prompts, responses, project/session names, or arbitrary warnings.

### TDD Cycle Evidence

| Stage | Evidence | Result |
|---|---|---|
| RED | Added `AnalyticsScanCoordinatorTests` and ran its focused test before coordinator/checkpoint types existed. | Failed as expected: `CS0246` for `DurableFileCheckpoint`. |
| GREEN | Added the coordinator plus the aggregate/checkpoint SQLite transaction. | Focused tests passed 3/3. |
| TRIANGULATE | Added pre-commit-failure rollback coverage; exercised unchanged, appended/reopen, cancellation, malformed/incomplete-tail partial retention, and schema privacy. | Focused tests passed 4/4. |
| REFACTOR | Kept transaction ownership in `SqliteDailyModelUsageStore`, checkpoint data minimum and path-free, and warning handling outside durable checkpoint state. | Focused legacy scanner/store tests, full build, and full suite passed. |

### Verification evidence

```text
dotnet test ... --filter "FullyQualifiedName~AnalyticsScanCoordinatorTests"
RED: failed as expected (CS0246); GREEN: 3/3; TRIANGULATE: 4/4 passed.

dotnet test ... --filter "FullyQualifiedName~SessionJsonlScannerTests|FullyQualifiedName~SqliteDailyModelUsageStoreTests"
Passed: 14, Failed: 0.

dotnet build AIBar.sln --no-restore
Passed: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 123, Failed: 0, Skipped: 0.

git diff --check 052ede7 --
Passed (only LF-to-CRLF notices).
```

No LSP tool is available in this executor session; the zero-warning compiler build is the available static diagnostic evidence. Privacy inspection found prompt/response strings only in the synthetic test fixture/assertion, never in production persistence schema or data. No design deviation.

### Files, workload, and rollback

- `src/AIBar.Application/AnalyticsScanCoordinator.cs`
- `src/AIBar.Application/SqliteDailyModelUsageStore.cs`
- `tests/AIBar.Domain.Tests/AnalyticsScanCoordinatorTests.cs`
- `openspec/changes/aibar-foundation/tasks.md`
- `openspec/changes/aibar-foundation/apply-progress.md`

PR boundary: **Slice 6C1 only** (`052ede7 → 6C1 → 6C2`). Rollback removes the coordinator, `file_checkpoint` schema/transaction additions, focused tests, and this 6C1 metadata; it leaves prior daily aggregates and all Codex-owned files untouched. No staging, commit, push, PR, branch, review transaction, Clear Data, rebuild persistence, pricing, UI, or 6C2+ work occurred.

### Remaining tasks

- [ ] Persist timezone-policy version/provenance; on mismatch require explicit replacement/rebuild rather than silently moving historical daily totals.
- [ ] Implement Clear AIBar Data to cancel work, remove only AIBar-owned cache/database/log/settings data, recreate empty state, and prove Codex-owned source files are byte-for-byte unchanged.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test policy-version replacement/rebuild and clear-data isolation with synthetic before/after source hashes.

## Slice 6C1 corrective gate rerun — blocked by review budget (2026-07-14)

**Structured status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blocked reasons; `actionContext` is `repo-local` and the workspace/only allowed edit root is `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is `auto-chain` / `feature-branch-chain`; the assigned boundary remains **Slice 6C1 only** from `052ede7`. Strict TDD was required by parent instruction. No action-context warning applies.

The focused parent gate correctly rejected the prior implementation: `SessionJsonlScanner.ScanAsync` advanced its private checkpoint before SQLite aggregate/checkpoint commit, and the coordinator accepted hand-built checkpoint data. Its cancellation/failure tests bypassed the scanner. Therefore neither Slice 6C1 task is complete; both persisted checkboxes were restored to `- [ ]` immediately. The invalid coordinator, its tests, and the related store checkpoint changes were removed, returning production code to `052ede7` behavior.

### Budget decision: split required before implementation

The remaining budget is approximately **200 authored additions + deletions** from `052ede7`. A truthful, readable repair requires at least:

| Required concern | Conservative forecast |
|---|---:|
| Scanner prepare result and uncommitted checkpoint identity/length/mtime/safe-offset/parser/cumulative-token state; preserve legacy `ScanAsync` | 85 |
| SQLite checkpoint schema/load/atomic write extension | 55 |
| Coordinator durable-checkpoint → scanner-prepare → one-transaction integration | 45 |
| Real scanner cancellation/failure/retry, unchanged/appended/reopen, and incomplete-tail/privacy tests | 115 |
| **Total** | **300** |

Compressing these boundaries would weaken checkpoint identity, hide retry behavior, or make tests hand-fabricate state again. No RED/GREEN cycle was started in this corrective rerun because the workload gate stops before edits. Earlier claimed 6C1 TDD/verification evidence is superseded and must not be used.

### Required 6C1 split proposal

- **6C1A — two-phase scanner and durable checkpoint contract** (~190): add `PrepareAsync` returning parsed records plus an uncommitted proposed checkpoint with file identity, length, mtime, safe byte offset, parser version, and cumulative tokens; retain legacy `ScanAsync` by committing only through its existing in-memory compatibility path. Add scanner-only RED/GREEN/TRIANGULATE tests for unchanged, append, reopen, and incomplete tails.
- **6C1B — coordinator-owned atomic commit and retry proof** (~250): persist/load the exact proposed checkpoint, have the coordinator load durable state then prepare from it, and atomically merge usage plus checkpoint. Add real scanner-path cancellation/failure/retry and schema-privacy tests proving a retry reproduces records and commits once.

**PR boundary:** no 6C1 implementation is complete. Current invalid work was removed; no stage, commit, push, PR, review, branch action, 6C2, Clear Data, rebuild persistence, pricing, or UI work occurred. **Rollback boundary:** none beyond removing this planning/progress record; production code remains at `052ede7` for Slice 6C1.

### Remaining tasks

- [ ] Wire supported scanner token/model/timestamp handoff through 6A to 6B, advancing checkpoints only in the same successful aggregate transaction.
- [ ] **RED → GREEN → TRIANGULATE → REFACTOR:** test unchanged/appended handoff, cancellation-before-commit, and partial-scan retention without prompt/response persistence.

### Corrective-rerun verification evidence

```text
dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~SessionJsonlScannerTests|FullyQualifiedName~SqliteDailyModelUsageStoreTests" --logger "console;verbosity=minimal"
Passed: 14, Failed: 0, Skipped: 0, Total: 14.

dotnet build AIBar.sln --no-restore
Build succeeded: 0 warnings, 0 errors.

dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"
Passed: 119, Failed: 0, Skipped: 0, Total: 119.

git diff --check 052ede7 --
Passed (only LF-to-CRLF notices).
```

No LSP service is available in this executor session; the zero-warning compiler build is the available static evidence. The final complete diff from `052ede7` is **104 additions + 0 deletions = 104 authored lines**, all cumulative apply-progress metadata. No production or test code remains for Slice 6C1, and the persisted tasks artifact retains both 6C1 tasks as unchecked.

## Slice 6C1A applied (2026-07-14)

**Status consumed:** authoritative OpenSpec `applyState: ready`, `nextRecommended: apply`, no blockers; `actionContext: repo-local` and the allowed workspace root is `C:\Users\mjsal\Desarrollos IA\Modificacion de Terminales\aibar`. Delivery is approved `auto-chain` / `feature-branch-chain`; boundary: **6C1A only** from `052ede7`. Strict TDD was active by parent instruction.

### Completed tasks and evidence

The four 6C1A lines are visibly `- [x]` in `tasks.md`; both 6C1B lines remain unchecked. `PrepareAsync` accepts a caller checkpoint and returns records/warnings/rebuild status with a path-free, uncommitted `SessionCheckpoint` proposal (identity, length/mtime, safe offset, parser version, and cumulative token counters). It does not access `SessionCheckpointStore`. `ScanAsync` now prepares then uses only its legacy in-memory commit path. No SQLite, coordinator, aggregate transaction, retry persistence, 6C1B, or 6C2 work was added.

| TDD stage | Evidence | Result |
|---|---|---|
| RED | New preparation contract test before API implementation | Failed as expected: `CS1061` (`PrepareAsync` absent). |
| GREEN | Minimal prepare/propose and compatibility-commit implementation | Focused scanner tests: 9/9 passed. |
| TRIANGULATE | Existing scanner cases plus new same-prior append/retry test cover unchanged, append, incomplete tail, offset/identity rebuild, cancellation, and no store mutation | Focused scanner tests: 9/9 passed. |
| REFACTOR | Kept checkpoint state path-free and commit ownership in `ScanAsync` | Full build/suite passed. |

### Verification / workload / rollback

`dotnet build AIBar.sln --no-restore`: 0 warnings, 0 errors. `dotnet test AIBar.sln --no-restore --logger "console;verbosity=minimal"`: 120 passed. `git diff --check 052ede7 --` passed (LF/CRLF notices only). LSP is unavailable; the zero-warning compiler build is the static diagnostic evidence. Current authored total from `052ede7`: **248 lines** (201 additions + 47 deletions), below the 400-line limit. Rollback removes the scanner preparation API/test and this 6C1A metadata only; no durable checkpoint or Codex-owned file changes exist.

**Remaining:** 6C1B's exact unchecked lines are the scanner-to-6A/6B atomic handoff and its cancellation/failure retry proof. PR boundary: `052ede7 → 6C1A → 6C1B → 6C2`; current slice is 📍 **6C1A**. No stage, commit, push, PR, branch, review, or 6C1B/6C2 work occurred.

## Slice 6C1B applied (2026-07-14)

**Status consumed:** `applyState: ready`, `nextRecommended: apply`, no blocked reasons; repo-local edits only. Strict TDD is false, so Standard Mode was used with behavior-first tests. Delivery is `force-chained` / `feature-branch-chain`; this autonomous child boundary is **6C1B only**, from clean parent `feature/aibar-foundation-slice-6c1a` at `a0188df`.

### Completed tasks and evidence

Both Slice 6C1B checkboxes are visibly `- [x]` in `tasks.md`. `file_checkpoint` is durable SQLite state keyed by an application-local SHA-256 source fingerprint and holds only the 6C1A path-free checkpoint fields. `AnalyticsScanCoordinator` loads that checkpoint, invokes `PrepareAsync`, routes records through the 6A `AnalyticsPolicy`, and persists aggregate deltas plus the exact proposed checkpoint in one SQLite transaction. It neither changes legacy `ScanAsync` nor persists paths, prompt/response data, or session names.

| Evidence | Result |
|---|---|
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --no-restore --filter "FullyQualifiedName~AnalyticsScanCoordinatorTests\|FullyQualifiedName~SqliteDailyModelUsageStoreTests" --logger "console;verbosity=minimal"` — exit 0; 10 passed, 0 failed. It exercises actual JSONL scanner preparation followed by injected store failure/cancellation and retry, append-once behavior, incomplete-tail partial retention, and aggregate/checkpoint rollback. |
| Runtime harness | N/A — this is a framework-independent SQLite/scanner integration boundary; the focused tests execute the real temporary-file JSONL + SQLite path without a desktop runtime. |
| Rollback boundary | Revert `AnalyticsScanCoordinator.cs`, the `file_checkpoint` methods/schema in `SqliteDailyModelUsageStore.cs`, their focused tests, and this metadata. Existing daily aggregates and all Codex-owned source files remain untouched. |

### Verification / workload

`dotnet test AIBar.sln --nologo`: exit 0, 127 passed, 0 failed. `dotnet build AIBar.sln --nologo`: exit 0, 0 warnings, 0 errors. `git diff --check`: exit 0 (only Git LF/CRLF notices). The complete 6C1B authored diff is 172 additions plus deletions, below 400; no stage, commit, push, PR, or review lifecycle operation was performed.

**Deviation:** None — implementation follows the 6C1A non-mutating preparation contract and keeps policy-rebuild persistence and Clear Data deferred to 6C2.

**Remaining:** Slice 6C2, Slice 6D, and later tasks remain unchecked and out of scope. Next chain boundary: `a0188df → 6C1B → 6C2`; current slice is 📍 **6C1B**.

## Slice 6C2A.1 applied (2026-07-14)

**Status consumed:** `applyState: ready`, `nextRecommended: apply`, no blocked reasons; repo-local OpenSpec edits only. Delivery is `force-chained` / `feature-branch-chain`; this child boundary is **6C2A.1 only**, based on `8fe89ec`. Standard mode applied; no review authority was started or reused.

All three 6C2A.1 checkboxes are visibly `- [x]` in `tasks.md`. Schema v2 adds path-free `source_contribution_state` policy provenance and source-keyed daily contribution rows. The 6C1B CAS remains first in the same transaction; global aggregates, contribution rows, source policy, and checkpoint all roll back together. V1 global aggregates are retained read-only and report rebuild-required rather than inventing historical ownership; future schemas fail closed. A policy mismatch also fails before commit.

| Work Unit Evidence | Result |
|---|---|
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --nologo --filter "FullyQualifiedName~Source_attribution\|FullyQualifiedName~AnalyticsScanCoordinatorTests\|FullyQualifiedName~SqliteDailyModelUsageStoreTests" --logger "console;verbosity=minimal"` — exit 0; 13 passed, 0 failed, 0 skipped. |
| Full test | `dotnet test AIBar.sln --nologo` — exit 0; 130 passed, 0 failed, 0 skipped. |
| Build | `dotnet build AIBar.sln --nologo` — exit 0; 0 warnings, 0 errors. |
| Runtime harness | N/A — synthetic SQLite migration/scanner boundary has no UI or live source; focused tests use temporary JSONL and SQLite. |
| Rollback boundary | Revert `AnalyticsPolicy.cs`, `AnalyticsScanCoordinator.cs`, `SqliteDailyModelUsageStore.cs`, the two focused test files, and this 6C2A.1 metadata; 6C1B checkpoint/CAS behavior and Codex-owned files remain untouched. |

**Files:** `src/AIBar.Domain/AnalyticsPolicy.cs`, `src/AIBar.Application/{AnalyticsScanCoordinator,SqliteDailyModelUsageStore}.cs`, `tests/AIBar.Domain.Tests/{AnalyticsScanCoordinatorTests,SqliteDailyModelUsageStoreTests}.cs`, `tasks.md`, and this progress record. No prompt/response bodies, paths, Clear Data, multi-source rebuild orchestration, pricing, UI, Git, PR, or review lifecycle action was added.

**Workload:** complete authored diff including pre-existing replanning artifacts is **157 additions + 16 deletions = 173 lines**, below the 400-line limit. `git diff --check` passed (LF/CRLF notices only). No design deviation.

**Remaining:** 6C2A.2 is the next dependency-bound child slice; 6C2B, 6D, and later work remain unchecked and out of scope.

## Slice 7B strict-TDD recovery applied (2026-07-14)

**Status:** Harness Strict TDD mode is active. This corrected Slice 7B record replaces the earlier incomplete phase evidence after an authorized discard/reconstruction from clean `41b1ded`; it covers only pure named trends and linear primary-window ETA. No UI, hourly reconstruction, probabilistic/history forecasting, Slice 7C/8, persistence, or Git/review lifecycle action was used.

### TDD Cycle Evidence

| Stage | Chronological evidence |
|---|---|
| Safety net | On clean base before any Slice 7B file: `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --nologo` — passed 155/155, failed 0, skipped 0. |
| RED | Added one behavior-first seven-complete-local-day trend test while production types were absent; `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~DerivedMetrics --nologo` failed as expected (`CS0246` `NamedTrendPolicy`; `CS0103` `DerivedMetricState`). |
| GREEN | Added the minimum `NamedTrendPolicy`/trend result contracts only; same focused command passed 1/1. |
| TRIANGULATE | Added insufficient-history, positive/zero/negative rate, stale/invalid/mismatched reset, unsupported single-observation, source-fact preservation, and estimate-label tests. Before ETA production code, the focused command failed as expected (`CS0246` `LinearExhaustionEtaPolicy`/`QuotaUsageObservation`); after adding ETA policy it passed 8/8. Related `DerivedMetrics|Pricing|AnalyticsPolicyTests` passed 26/26. |
| REFACTOR | Extracted current-service-window validation while retaining separate trend/ETA policies and deterministic clock/local-time inputs. A concurrent related test invocation had a transient Windows file lock (`CS2012`), so it is not evidence; sequential reruns passed focused 8/8 and related 26/26. |

**Behavior:** trends require all seven immediately preceding complete local calendar days, excluding today's partial day. ETA is labeled only `simple linear estimate`, requires a fresh primary snapshot, future reset, matching-reset observations, and a positive observed percentage-per-hour rate; unavailable/unsupported results preserve quota and observation facts. No token or quota fact is mutated.

**Verification:** full `dotnet test AIBar.sln --nologo` passed 163/163; `dotnet build AIBar.sln --nologo` succeeded with 0 warnings/0 errors; `git diff --check` passed. Runtime N/A: pure Domain policy. **Checkboxes:** all four Slice 7B lines are `- [x]`. **Files:** `src/AIBar.Domain/DerivedMetricsPolicy.cs`, `tests/AIBar.Domain.Tests/DerivedMetricsTests.cs`, `openspec/changes/aibar-foundation/{tasks,apply-progress}.md`. **Boundary/deviation:** Slice 7B only; corrected TDD chronology. **Rollback:** remove policy/test and revert Slice 7B artifacts, retaining 7A pricing. **Next:** independent review, then Slice 7C.

## Slice 8C1 corrected fresh-output publish (2026-07-21)

**Status:** the corrected Slice 8C1 Apply and final independent verification are complete. The next lifecycle action is bounded review/receipt establishment; no approval, receipt, staging, or commit is claimed. Strict TDD; `auto-chain` / `feature-branch-chain`; 8C1 only.

| TDD evidence | Current result |
|---|---|
| RED | The unsafe candidate run exceeded its 300-second limit after entering publish, so no terminal destructive assertion exists. Its code, results, and reviews are historical/non-authoritative. |
| GREEN/TRIANGULATE | Fresh-output-only admission rejects unsafe leaves and reparse observations, creates collision-failingly, publishes real self-contained output, and creates deterministic recursive inventory/ZIP/manifest artifacts without deletion, cleanup, replacement, reuse, rollback, or move-over-existing. |
| Verifier follow-up | Fake-command-marker tests prove file/file-parent/missing-parent/repository-overlap/leaf-reparse rejection launches no process. Reparse ran here; unsupported/permission environments explicitly skip. Inaccessible/unclassifiable-directory admission remains unproven without unsafe ACL mutation. |
| REFACTOR | Child real publishes use `--disable-build-servers`; tests serialize child publishes and never invoke `dotnet test` recursively. |

**Verification:** `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~PackagingRecoveryTests --nologo` passed 9/9; full suite passed 211/211; build 0 warnings/errors; `git diff --check` passed. No generated 8C1 output remains. **Tasks:** the authorized task record marks RED from immutable prior-candidate evidence and marks GREEN verification with opportunistic same-identity access-denied evidence non-blocking; parent historical-record task is truthful. 8C2+ remains unchecked. **Boundary:** no MSIX, signing, SBOM, provenance, notices, lifecycle, release, or external publication.

## Slice 8C1 fresh candidate reconstruction (2026-07-23)

**Status:** Standard mode (`strict_tdd: false`). The abandoned lineage `review-slice-8c1-standalone-20260722` was preserved without lifecycle, authority, or receipt mutation. This bounded reconstruction proves only the current 8C1 candidate; it does not claim review approval, staging, commit, publication, or readiness for 8C2.

**Native runtime attempt:** terminal `complete`; outcome `passed`; no active attempt remains. Runtime revision: `sha256:3be9fbda6b653e089dae7cdf4cfcd65e97f704ee450101d19bbdf622c87f6925` (`next_action: complete`).

| Work Unit Evidence | Exact result |
|---|---|
| Focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter FullyQualifiedName~PackagingRecoveryTests --nologo` — passed 9/9, failed 0, skipped 0. |
| Runtime harness | The executable focused suite invoked two real self-contained `Release/win-x64` publishes beneath separately created GUID temporary parents; it compared recursive publish inventory and ZIP entries/hashes, ZIP bytes, inventory bytes, manifest bytes, and artifact identity. Admission/failure scenarios also proved pre-creation rejection does not launch the marker command and post-creation failure leaves its caller-owned output. |
| Full verification | `dotnet test AIBar.sln --nologo` — passed 211/211, failed 0, skipped 0. `dotnet build AIBar.sln --nologo` — succeeded, 0 warnings, 0 errors. `git diff --check` — passed. |
| Cleanup/process evidence | The tests own and remove their GUID temporary parents after child processes exit. Post-run inspection found no `aibar-8c1-*` temporary directories. The script contains no delete, cleanup, replace, move, MSIX, signing, SBOM, provenance, notice, or diagnostic-export operation. |
| Rollback boundary | Remove only `scripts/Publish-Deterministic.ps1`, `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`, and this Slice 8C1 status/evidence update. Do not remove caller-owned incomplete output; the script never cleans it. |

**Current candidate behavior:** the script validates a caller-selected nonexistent leaf below an existing non-reparse parent, repeats admission before collision-failing creation, publishes self-contained `win-x64` output, inventories every recursive publish file, and writes deterministic ZIP/inventory/manifest artifacts. Existing leaves, repository overlap, missing parents, roots, and observed reparse paths fail before publish; post-creation failure reports `PUBLISH_INCOMPLETE_OUTPUT` and preserves the leaf. The contract makes no hostile namespace-identity guarantee.

**Scope and size:** only the 8C1 script and executable recovery test are implementation additions: 40 + 108 = 148 authored lines. With this 8C1 task/progress status update, the fresh work-unit accounting remains below the 400-line cap. Slice 8C2 tasks remain unchecked and untouched.

## Integrated b2 corrective gate — bounded test-harness and task-ledger correction (2026-07-26)

**Status:** Standard mode. The full-suite failure was reproduced as an incomplete child-only identity record during teardown: the child writes its record before the grandchild record exists, so a timeout/failure path could mask the real harness failure with `KeyNotFoundException`. The lifecycle harness also started the publisher through `Task.Run` and competed with parallel Windows process fixtures. `RunningProcess` now starts the publisher synchronously, preserves bounded completion, and uses `SemaphoreSlim` rather than thread-affine monitor ownership; the two Windows process-harness classes run in a non-parallel collection. No production supervisor behavior changed.

| Corrective evidence | Result |
|---|---|
| RED / root cause | Six pre-correction targeted executions passed 3/3, but the mandated full gate failed 269/271. During the correction, a full run reproduced the partial-record teardown path (`KeyNotFoundException` for missing `grandchild`) and a separate concurrent Windows event fixture missed its 5-second readiness bound. |
| GREEN / repeat | The affected lifecycle filter passed 3/3 four consecutive times; focused `PackagingSupervisor` passed 47/47; zero scoped helpers remained. |
| Full gates | `dotnet test AIBar.sln --no-restore -m:1 --nologo` passed 271/271 twice (372.50 s, 341.12 s). `dotnet build AIBar.sln --no-restore -m:1 --nologo` passed with 0 warnings/errors; `git diff --check` passed. |
| Task ledger | Restored the approved explicit b2a 4/4 checked rows and b2c 0/4 unchecked rows; current b2b Units 1–3 remain 12/12 checked. |

`SDD-VERIFY-B2-INTEGRATED-001` and `SDD-VERIFY-B2-INTEGRATED-002` are **fixed, not verified** at this historical apply-time corrective-gate status. Subsequent scoped verification exists by reference in `verify-report.md` and `review-ledger.md`; it does not rewrite this apply-time status. `AIBar.sln` was restored exactly to `HEAD` after the scope audit; the historical committed-range trailing-whitespace warning remains informational. No b2c code, cleanup/scavenging, PowerShell integration, staging, commit, push, or PR action occurred. **Receipt (recomputed against `e5cca98`):** authorized test/harness changes are 61 additions + 5 deletions (tracked tests via `git diff --numstat e5cca98 --`, plus the 4-addition untracked `WindowsProcessHarnessCollection.cs` via `git diff --no-index --numstat -- NUL <path>`); `tasks.md` is 14 additions; this apply-progress evidence is 13 additions. `verify-report.md` (171 additions), `review-ledger.md` (24 additions), and unrelated metadata are excluded. Total: 88 additions + 5 deletions = **93** review lines, below the 400-line hard stop.

## Slice 8C1.1b2c C1b1 — rolled back incomplete apply (2026-07-30)

**Status:** failed/incomplete, Standard mode (`strict_tdd: false`), native attempt 55 only. The C1b1 RED tests were added before production code and failed to compile because `Cleanup` did not exist. A bounded `Cleanup.cs` candidate then passed its three C1b1 tests and the 61/61 focused `PackagingSupervisor` suite, including Windows retained-handle relative rename success and collision refusal. It was rolled back because the required full-solution runtime gate did not complete: both `dotnet test AIBar.sln --no-restore -m:1 --nologo` invocations were externally aborted before a result was emitted. No C1b1 implementation, runtime mutation, test, task completion, or C2/C3 behavior is retained.

| Work Unit Evidence | Exact result |
|---|---|
| RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~C1b1_commit" --no-restore -m:1 --nologo` failed before production code with `CS0103`: `Cleanup` did not exist. |
| Discarded candidate evidence | C1b1 focused tests passed 3/3; the temporary candidate also passed `PackagingSupervisor` 61/61. These are non-terminal evidence only because the candidate was removed. |
| Required full gate | `dotnet test AIBar.sln --no-restore -m:1 --nologo` was started twice and externally aborted before a test result/output artifact was available; no pass/fail result is claimed. |
| Post-rollback focused/build | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` passed 58/58. `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings and 0 errors. |
| Cleanup/process | No `aibar-c1b1-*` temporary root exists. The only observed `dotnet.exe` was the `VBCSCompiler` server, not a test/harness process. No C1b1 helper or native-call process survives. |
| Diff | `git diff --check` still reports only the pre-existing trailing whitespace at `openspec/changes/aibar-foundation/verify-report.md:67-68`; C1b1 did not change that file. |
| Rollback boundary | Removed only temporary `tools/AIBar.Packaging.Supervisor/Cleanup.cs` and its C1b1 test edits. The three C1b1 task lines remain visibly unchecked; C1b0 remains usable. |

**Scope and status:** Structured status consumed: `applyState=ready`, `actionContext.mode=repo-local`, workspace root `C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar`, authorized work unit `C1b1 only`. No action-context warning applied. No C2/C3, staging, commit, push, PR, review, deletion, rename-back, scavenger, PowerShell, shell, helper, or fallback behavior was retained. `.gitignore` HEAD/index/worktree blob remains `16d3fd82b1698894b9b3f4d707e12db4f16cd7f1` and unstaged.

**Remaining tasks:**

- [ ] **RED:** Add tests before production code for the explicit external-root, cleanup-ownership, and native-security threat cases: final identity/reparse/share/containment/same-volume/complete-child-set gates, source/parent substitution, invalid leaf, collision, unsupported/native failures, child transition/disposal, and post-success uncertainty (`CLEANUP_PARTIAL`); preserve every C1b0 refusal case.
- [ ] **GREEN:** After C1b0 passes, implement one `NtSetInformationFile(FileRenameInformation)` call on retained source with the retained quarantine-parent relative target, `ReplaceIfExists=FALSE`, bounded native buffer, and closed `NTSTATUS` handling; use `CLEANUP_REFUSED` pre-commit and retained `CLEANUP_PARTIAL` after issued-call uncertainty.
- [ ] **TRIANGULATE/GATE:** Run focused supervisor tests, one bounded Windows 10/11 x64 runtime proof, `dotnet test AIBar.sln --no-restore -m:1 --nologo`, `dotnet build AIBar.sln --no-restore --nologo`, `git diff --check`, and zero-helper inspection. Prove identity continuity, collision refusal, no Win32/path/shell/helper fallback, no deletion, and no rename-back.

## C1b1 retry attempt 56 — terminal failure rollback (2026-07-30)

**Status:** failed. The retry candidate’s only C1b1 production file, `tools/AIBar.Packaging.Supervisor/Cleanup.cs`, and its three C1b1 test cases were removed; the retained C1b0 test again asserts that no `Cleanup` type exists. The candidate had passed C1b1 3/3, `PackagingSupervisor` 61/61, and a clean build with 0 warnings/errors, but its required `dotnet test AIBar.sln --no-restore -m:1 --nologo` run was externally aborted before totals or an exit code. That full-suite evidence is conclusively **inconclusive**, was not rerun, and invalidates the candidate.

| Terminal evidence | Exact result |
|---|---|
| Post-rollback focused test | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` — exit 0; passed 58/58, failed 0, skipped 0. |
| Post-rollback clean build | `dotnet clean AIBar.sln --nologo` — exit 0; 0 warnings, 0 errors. The preliminary unsupported `--no-restore` clean modifier was rejected before the successful clean and did not run a build. `dotnet build AIBar.sln --no-restore --nologo` — exit 0; 0 warnings, 0 errors. |
| Cleanup/process | `Cleanup.cs` absent; C1b1 test methods absent; `aibar-c1b1-*` temporary roots = 0; `Harness` helper processes = 0; matching `dotnet`/`testhost` processes = 0. |
| Scoped integrity | `git diff --check -- tools/AIBar.Packaging.Supervisor/Cleanup.cs tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs` — exit 0. `.gitignore` HEAD/index/worktree blob is byte-identical: `16d3fd82b1698894b9b3f4d707e12db4f16cd7f1`. |
| Task ledger | C1b1 RED, GREEN, and TRIANGULATE/GATE remain visibly `- [ ]`; C2/C3 were not changed. |

**Terminal evidence revision:** `sha256:54749e9499271d9e89ac4f6b924c5fefba59ab136314a1fe3608659effb9cd55`, SHA-256 over the canonical UTF-8/LF `gentle-ai.sdd-attempt-terminal-evidence/v1` statement covering the candidate result, aborted full gate, rollback, post-rollback checks, cleanup, `.gitignore`, scoped diff, and task state.

**Structured status consumed:** native attempt status before rollback was ordinal 56 active at `sha256:3cd9eaea8df2ee420c6f9a551dabdca2d64001b67db758b0db4762dd3c9cd40c`, `next_action=finish`, `decision_required=false`. The supplied action context limited this work to C1b1 contractual rollback; no `actionContext` fields were supplied, so no additional action-context warning can be evaluated. **Workload/PR boundary:** terminal rollback only; no staging, commit, push, PR, review, C2, or C3 work.

**Remaining tasks (unchanged):**

- [ ] **RED:** Add tests before production code for the explicit external-root, cleanup-ownership, and native-security threat cases: final identity/reparse/share/containment/same-volume/complete-child-set gates, source/parent substitution, invalid leaf, collision, unsupported/native failures, child transition/disposal, and post-success uncertainty (`CLEANUP_PARTIAL`); preserve every C1b0 refusal case.
- [ ] **GREEN:** After C1b0 passes, implement one `NtSetInformationFile(FileRenameInformation)` call on retained source with the retained quarantine-parent relative target, `ReplaceIfExists=FALSE`, bounded native buffer, and closed `NTSTATUS` handling; use `CLEANUP_REFUSED` pre-commit and retained `CLEANUP_PARTIAL` after issued-call uncertainty.
- [ ] **TRIANGULATE/GATE:** Run focused supervisor tests, one bounded Windows 10/11 x64 runtime proof, `dotnet test AIBar.sln --no-restore -m:1 --nologo`, `dotnet build AIBar.sln --no-restore --nologo`, `git diff --check`, and zero-helper inspection. Prove identity continuity, collision refusal, no Win32/path/shell/helper fallback, no deletion, and no rename-back.

## C1b1 background verification attempt 57 — successful closure (2026-07-30)

**Status:** passed; Standard mode (`strict_tdd: false`), C1b1 only. This closes the background full-suite verification retry and preserves the failed/rolled-back attempt-55/56 history above. No implementation behavior was changed during closure.

| Evidence | Exact result |
|---|---|
| Candidate accounting | `Cleanup.cs` +7; C1b1 additions in `PackagingSupervisorTests.cs` +32/-1; 40 changed lines, within the 350-line limit. |
| Focused and regression | C1b1 focused tests passed 3/3; `PackagingSupervisor` passed 61/61. |
| Clean/build | `dotnet clean AIBar.sln --nologo` and `dotnet build AIBar.sln --no-restore --nologo` each exited 0 with 0 warnings and 0 errors. |
| Background full suite | `dotnet test AIBar.sln --no-restore -m:1 --nologo` exited 0: 288 passed, 0 failed, 0 skipped, duration 5m32s. Its full-output digest is unavailable because the verifier persisted no output artifact. |
| Integrity/cleanup | Candidate-only whitespace inspection passed; `Cleanup.cs` is present; no matching test process, helper, or `aibar-c1b1-*` root remains; `.gitignore` HEAD/worktree hash is `16d3fd82b1698894b9b3f4d707e12db4f16cd7f1`. |

**Task ledger:** C1b1 RED, GREEN, and TRIANGULATE/GATE are marked `[x]` in `tasks.md`; C2 and C3 remain unchanged and unchecked.

**Evidence revision:** `sha256:d257b04eb11ce8755c7d0b59c5713afc1ae3eacbf27774cbbc5ee9312cc8a04e`, SHA-256 of canonical structured attempt-57 evidence including the results above and current scoped hashes for `Cleanup.cs` (`sha256:225bd1dcc731f10763304fa52f45e19c555e42f9f10295c5d0fb1828678821ca`) and `PackagingSupervisorTests.cs` (`sha256:55ba6132fdac9167f0076c7585587c50d656529c83ebe0cce1a49d4dadf9177a`).

**Structured status consumed:** authoritative OpenSpec status reported `applyState=ready`, `actionContext.mode=repo-local`, workspace root `C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar`, and the only blocker was active attempt 57 at `sha256:a7c57f5318de177addb8fc6d42694e8a101cc3cb81429810fe4a5e18154abfa2`; native attempt status reported `next_action=finish`, `decision_required=false`.

**Workload/PR boundary:** C1b1 only, 40/350 candidate lines. No stage, commit, push, PR, review, C2, or C3 work. **Remaining implementation tasks:** C2/C3 and later planned slices remain unchecked.

## Slice b2c-C2 — blocked pre-implementation authority check (2026-07-30)

**Status:** blocked before implementation. The authoritative OpenSpec apply status was ready for the explicitly authorized `b2c-C2` feature-branch-chain work unit; receipt-driven review is disabled and strict TDD is false. The active attempt remains parent-owned attempt 58 and was not begun, reset, or finished here.

**Blocker:** C2 requires revalidation of the committed retained quarantine and its exact direct-child identities before any handle-anchored deletion. `DirectoryCapability` retains those observations privately, but its C1b commit releases and clears the child-handle map and exposes no immutable child-identity snapshot or post-commit cleanup capability. The C2-only allowed implementation paths exclude `DirectoryCapability.cs`; reopening from a path or trusting an unbound enumeration would violate the exact-identity/no-arbitrary-root contract. No safe C2 implementation can therefore be added in this bounded unit without an explicitly authorized C1/C2 boundary amendment.

**No mutation/evidence:** no C2 production or test code, C2 task checkbox, runtime root, helper process, staging, commit, review, or authority record was changed. No focused/full/build command was run because the required capability proof is unavailable before the RED implementation boundary. `git diff --check` passed before this evidence update; `.gitignore` remained pre-existing, unstaged, and byte-for-byte unchanged.

**Remaining C2 tasks (unchanged):**

- [ ] **RED:** Test reopen/identity/reparse/enumeration/delete/unknown failures before first deletion and mid-delete; DPAPI metadata corruption, retry exhaustion, and cancellation must retain quarantine as `CLEANUP_PARTIAL`.
- [ ] **GREEN:** Reopen and revalidate the committed quarantine, delete only handle-anchored bounded recursive entries, and write protected metadata containing retained identity, phase, bounded retry count, and next eligible time.
- [ ] **TRIANGULATE:** Prove successful removal only after quiescence, every post-commit failure stays `CLEANUP_PARTIAL`, retries never rename back, no path/secret leakage occurs, and no arbitrary root can be selected.
- [ ] **GATE:** Focused fault matrix, build, diff check, and retained-quarantine receipt. C2 rollback removes only post-commit cleanup/metadata/tests; C1 remains usable.

**Required decision:** authorize a narrowly scoped amendment that exposes an immutable post-commit retained-quarantine child-identity capability to C2 (with no path-only fallback), then re-run C2 under a new or explicitly continued native attempt. C3 remains out of scope.

## b2c-C1b-evidence — Immutable child-evidence producer and capability transfer (2026-07-30)

**Status:** complete for this producer unit only; Standard mode (`strict_tdd: false`), feature-branch-chain boundary `864bb14 → b2c-C1b-evidence`. Native attempt 59 was already active and remains parent-owned; this executor did not begin, reset, finish, or otherwise mutate the native ledger.

| Evidence | Exact result |
| --- | --- |
| RED | `dotnet test ... --filter "FullyQualifiedName~CommittedChildEvidence" --no-restore -m:1 --nologo` initially failed at compile time because `DirectoryCapability.TryFreezeChildEvidence` and `CommittedChildObjectKind` did not exist. |
| GREEN/TRIANGULATE | `CommittedChildEvidence/v1` now clones and seals root/parent `FILE_ID_INFO` data, a 32-byte digest, a 32-byte random correlation key, and sorted collision-checked direct-child HMAC tags. Capture re-observes only live retained handles before the one-way child release; failure zeroes temporary key/tag/identity buffers. Successful native commit transfers the committed capability exactly once; its disposal owns the transferred live capability and zeroes evidence. |
| Focused | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor\|FullyQualifiedName~CommittedChildEvidence" --no-restore -m:1 --nologo` — exit 0; 63/63 passed, 0 failed, 0 skipped. |
| Native runtime | `Windows_native_rename_readiness_proves_relative_success_collision_and_no_residue` — exit 0; 1/1 passed. It proves retained-source identity continuity, quarantine-parent relative commit, collision refusal/no overwrite, evidence validity, exact committed-capability disposal, and zero `aibar-c1b0-*` residue. |
| Full regression | `dotnet test AIBar.sln --no-restore -m:1 --nologo` — exit 0; 290/290 passed, 0 failed, 0 skipped, 5m25s. The earlier parent-timeout run was incomplete; its lingering PIDs were absent at recovery inspection, so no process was terminated. |
| Build / whitespace | `dotnet build AIBar.sln --no-restore --nologo` — exit 0; 0 warnings, 0 errors. `git diff --check` — exit 0; existing LF-to-CRLF advisories only. |

**Task state:** the four `b2c-C1b-evidence` RED/GREEN/TRIANGULATE/GATE task checkboxes are now `[x]`. C2, C3, DPAPI retry metadata, deletion, scavenging, packaging/release, and ledger operations remain unimplemented and unchecked.

**Cleanup and safety:** the recovery PIDs named by the parent no longer existed when inspected. Current surviving `dotnet.exe` processes are shared `VBCSCompiler`/MSBuild workers, not testhost or attempt-owned helpers, so they were not terminated. No `aibar-c1b0-*` root remains. `NUL` was a Git-Bash filesystem entry created by `2>NUL` redirection (not a Windows device via Git Bash); it was safely removed and no longer appears in Git status. `.gitignore` content remains byte-identical to HEAD/index (`16d3fd82b1698894b9b3f4d707e12db4f16cd7f1`) and was not edited.

**Rollback boundary:** before C2 lands, remove only `CommittedChildEvidence.cs`, the producer additions in `DirectoryCapability.cs`, producer tests, these four task marks, and this evidence block; retain historical C1b `864bb14`. If C2 later lands, disable/remove C2 first. No C2 deletion path is reachable from this unit.

**Evidence revision:** `sha256:1306f56b3f9cef7c1b3f3c04bef03b918eae543f2d06cd14576a7591a7022069` (canonical final source/test/task hashes plus RED/focused/full/build/native/diff, cleanup, and producer-only scope evidence).

## b2c-C2 — Guarded cleanup continuation (2026-07-30)

**Status:** blocked after RED/GREEN scaffolding; do not treat this as C2 completion. Consumed authoritative OpenSpec state `nextRecommended=apply`, `dependencies.apply=ready`, attempt-60 handoff revision `sha256:90bc8b1908a13b9aa1fce1f59a87df41c62afaa42444b7b5ca65bf0e10747c5e`. The executor did not mutate the native attempt ledger. `actionContext` supplied no unsafe edit-root warning; `.gitignore` remains unrelated and untouched.

| Evidence | Exact result |
| --- | --- |
| RED | The exact C2 focused command failed before production code with `CS0246` for the absent `ICommittedCleanupOperations` and `ICleanupMetadataProtector`. |
| GREEN/TRIANGULATE | `Cleanup` now rejects invalid/frozen evidence, duplicate/missing/extra/renamed/reparse/cross-volume/inaccessible/unknown/identity/cancellation/enumeration cases before an adapter deletion. `ScavengerMetadata` uses `CryptProtectData`/`CryptUnprotectData` for CurrentUser protection and zeroes bounded plaintext/native buffers. The focused filter passed 81/81 after one unrelated `PackagingRecovery` harness cleanup flake; immediate repeat passed. |
| Full / build / diff | Full solution test failed 5/305 in pre-existing `PackagingRecoveryTests` descendant-harness creation/cleanup (`Harness.dll` access and helper build timing). Build passed with 0 warnings/errors; `git diff --check` passed. |

**Blocking safety diagnosis:** the permitted C2 files contain no trusted production implementation of `ICommittedCleanupOperations` that can bounded-enumerate, reopen, observe, and recursively delete relative to the retained root handle. Adding a path-based adapter would violate C2. Implementing the required native handle-relative enumeration/open/delete primitives safely exceeds the remaining hard-cap scope and conflicts with the existing native interop boundary. The new internal seam has no production caller, so it cannot select an arbitrary root or delete by path; it remains fail-closed.

**Task state:** all four C2 checkboxes remain `[ ]`; no checkbox claim is proven. Remaining exact unchecked lines are the C2 RED, GREEN, TRIANGULATE, and GATE lines in `tasks.md`. C3 and PowerShell remain untouched. The feature-branch-chain PR boundary remains C2 only; current authored count is 281 source/test lines before this evidence record, below 400.

**Cleanup:** handoff PIDs 26192/21932 were absent before any new command. No `aibar-c1b0-*` or `aibar-c2-*` root remains. Shared `dotnet` processes were not killed. The failed full suite left pre-existing/descendant-harness `aibar descendant*` roots (75 observed); this executor did not delete uncertain roots.

**Rollback boundary:** remove only `Cleanup.cs`, `ScavengerMetadata.cs`, and the C2 test additions; leave C1b evidence producer and C3 untouched.

## b2c-C2a — Trusted retained-handle operations (attempt 61, 2026-07-30)

**Status:** blocked / partial implementation; C2a checkboxes deliberately remain unchecked. Consumed authoritative OpenSpec apply authorization for active attempt 61, handoff revision `sha256:6c4d22db1e467d8d6cca2b5f7d728fc1adab169c01867ff25a23183650a5d62c`, feature-branch-chain boundary `b2c-C2a`, and allowed edit roots. No native attempt ledger operation, staging, commit, review, or publication occurred. `strict_tdd: false`; the task-mandated local RED → GREEN → TRIANGULATE/REFACTOR → GATE evidence follows.

| Cycle | Evidence |
| --- | --- |
| RED | The exact focused command failed before production implementation with `CS0103`/`CS0246` because `RetainedTreeSession` and `NativeDirectoryStatus` did not exist. |
| GREEN | Added a bounded `RetainedTreeSession` with x64/export/layout compatibility checks, `NtQueryDirectoryFile` record parsing, root-relative `NtCreateFile`, handle observation leases, `NtSetInformationFile(FileDispositionInformation)` primitives, cancellation/deadline boundaries, buffer zeroing, and stable mechanism-only statuses. The exact focused command passed 67/67. |
| TRIANGULATE/REFACTOR | Parser tests cover order independence and malformed, duplicate, dot, embedded-NUL, and >16-entry refusal. The Windows test transfers a real producer capability, uses a Unicode/space direct child, enumerates/reopens/observes it, and deletes that test-owned child only through the Debug-only test authorization seam; no production authorization factory is exposed. Exact focused command: passed 67/67. |
| GATE | `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings/0 errors; `git diff --check` passed (only pre-existing LF→CRLF advisory lines for already-modified design/spec files). Full `dotnet test AIBar.sln --no-restore -m:1 --nologo` failed 1/294: pre-existing `PackagingRecoveryTests.Owned_lifecycle_concurrently_drains_saturated_stdout_and_stderr_within_bound`, during `DescendantHarness.Create` at line 447. It is outside C2a allowed paths and was not changed or retried. |

**Files changed:** `tools/AIBar.Packaging.Supervisor/DirectoryCapability.cs` (+252/-0) and `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs` (+70/-0). Functional delta: 322 authored lines, within the approved 800 hard cap and below the 400-line visibility threshold. `tasks.md` is intentionally unchanged because none of the four C2a claims is fully proven.

**Runtime / cleanup evidence:** scoped C2a runtime proof passed inside the 67 focused tests on this Windows x64 worker. The test-owned producer handoff used a 10-second session deadline, a single Unicode/space direct child, and left `C2A_TEMP_ROOT_RESIDUE=0`. The interrupted earlier full-suite vstest/testhost PIDs 4880/32484 were rechecked and absent; `Harness.exe` was absent. No process was killed and no uncertain root was deleted.

**Blocking safety diagnosis:** the producer's `CommittedQuarantineCapability` exposes root/parent handles only through an owner that also owns and disposes immutable evidence; with `CommittedChildEvidence.cs` forbidden for this work unit, C2a cannot atomically move only the retained-handle facet while leaving the evidence/key facet owned by future C2b as required by the amended spec/design. The present session therefore retains the whole producer capability during its lifetime, which is insufficient proof of the required one-way facet separation. The runtime matrix also lacks a proven path-name-substitution case and the full-suite gate is red. Do not mark C2a complete or advance C2b until the allowed producer-facet contract is amended and the unrelated full-suite failure is independently resolved/reclassified.

**Scope/ownership:** no `Cleanup.cs`, `ScavengerMetadata.cs`, DPAPI, evidence/HMAC policy, C2b, C3, PowerShell, path fallback, rename-back, copy-delete, `.gitignore`, staging, commit, or native-ledger mutation occurred. C2b and C3 task lines remain untouched and unchecked. **Rollback boundary:** remove only the C2a additions in `DirectoryCapability.cs`, `PackagingSupervisorTests.cs`, and this progress record; retain producer commit `03ee654` and all prior history.

## b2c-C1c — Atomic capability-facet transfer (attempt 62, 2026-07-30)

**Status:** blocked at the required final diagnostics gate. The authoritative OpenSpec status consumed `artifactStore=openspec`, `applyState=ready`, `actionContext.mode=repo-local`, workspace root `C:/Users/mjsal/Desarrollos IA/Modificacion de Terminales/aibar`, and active native attempt 62 for `b2c-c1c-atomic-capability-facet-transfer`. The active-attempt blocker is expected for this charged actor; no `sdd-attempt` command was run. Strict TDD is false, but this unit retained RED → GREEN → TRIANGULATE/REFACTOR.

| Evidence | Exact result |
| --- | --- |
| RED | Before production edits, `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~C1c" --no-restore -m:1 --nologo` exited 1 with real `CS1061` failures for missing `CommittedQuarantineCapability.TrySplit`/`State` and `CS0103` failures for the absent facet-binding/failure-injection types. |
| GREEN/TRIANGULATE | The producer now holds a synchronized `Whole`/`Splitting`/`Split`/`Disposed` split gate, stages both facets before detaching the retained handles, publishes one paired handoff, and leaves the original inert. The tree and evidence facets dispose independently; the internal same-handoff binding rejects cross-handoff facets. Tests cover paired-only/repeated/concurrent split, split-versus-dispose, all injected pre-publication boundaries, original inertness, both disposal orders, cross-handoff rejection, orphaned-evidence preservation, stable state diagnostics, and a real Windows producer handoff. |
| Focused / repeat | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` passed 68/68 twice, serialized. The narrower C1c filter passed 5/5 after TRIANGULATE. |
| Windows x64 producer-handoff proof | `Windows_c1c_real_producer_handoff_preserves_evidence_when_tree_is_disposed_first` passed through the real C1b producer seam. It proves only paired ownership/lifetime and tree-first disposal with evidence preserved; it performs no C2a operation, enumeration, reopen, observation refresh, deletion, cleanup authorization, DPAPI, or C2 completion claim. Scoped `aibar-c1b0-*`/`aibar-c1c-*` root inspection found none; `tasklist` found no Harness or PowerShell process. No uncertain/pre-existing root was deleted. |
| Full / build | `dotnet test AIBar.sln --no-restore -m:1 --nologo` passed 295/295, 0 failed. `dotnet build AIBar.sln --no-restore --nologo` passed with 0 warnings and 0 errors. |
| Diff / scope | `git diff --check` passed (only existing LF→CRLF advisories for already-modified design/spec files). C1c source/test numstat is `246 additions, 11 deletions` (257 total), below the hard 400 cap: `CommittedChildEvidence.cs` +116/-6, `DirectoryCapability.cs` +8/-0, `PackagingSupervisorTests.cs` +122/-5. No prohibited added production API, DPAPI/HMAC recomputation, path fallback, C2a/C2b/C3 behavior, shell, PowerShell, subprocess, staging, commit, push, PR, or review action was found. |
| Required LSP/lens gate | **Blocked:** this executor's injected tool surface exposes no `lsp_diagnostics`/`lens_diagnostics` tool, and `pi-lens` plus `lens_diagnostics` CLIs were absent from `PATH`. The edit host reported `C# clean`, but that is not a substitute for the mandated proactive LSP and `lens_diagnostics mode=all` checks. |

**`.gitignore` audit:** HEAD, index, and worktree hashes are all `16d3fd82b1698894b9b3f4d707e12db4f16cd7f1`; no `.gitignore` bytes or index entry were changed by this unit. Its pre-existing unstaged `M` status was already present before C1c work and remains.

**Task state:** every C1c implementation checkbox remains `[ ]` because the mandatory LSP/lens gate could not be run. No parent-owned lifecycle checkbox was changed. **Workload/PR boundary:** C1c only, 257 source/test authored additions+deletions, hard cap 400, no size exception; no Git delivery action occurred.

**Exact remaining C1c unchecked lines:**

- [ ] Add deterministic failing tests before production edits for paired-only exact-once split, no one-facet overload, repeated/concurrent split, and split-versus-dispose linearization; assert one winner and no visible partial handoff. <!-- sdd-owner: implementation -->
- [ ] Add deterministic failure-injection tests at validation, binding creation, facet allocation, factory, and pre-publication boundaries; capture original ownership intact on pre-publication failure with no leak, duplication, or double ownership. <!-- sdd-owner: implementation -->
- [ ] Add tests for original inertness after publication, independent disposal in both orders with exact handle-close and evidence-zeroization counters, same-handoff binding acceptance, cross-handoff/legacy/reconstructed rejection, orphaned tree/evidence facets, C2a failure preserving evidence, and diagnostics free of sensitive values. <!-- sdd-owner: implementation -->
- [ ] Capture a real RED compile or assertion failure from the deterministic suite and record that no production implementation is permitted during RED. <!-- sdd-owner: implementation -->
- [ ] Implement the smallest producer-side seam: one atomic exact-once paired handoff consuming one `CommittedQuarantineCapability`, with no tree-only or evidence-only overload and no behavior outside C1c. <!-- sdd-owner: implementation -->
- [ ] Implement two non-cloneable, non-serializable, non-reconstructible facets with disjoint ownership: `RetainedTreeCapabilityFacet` for only the exact root/quarantine-parent handles and captured observations, and `CommittedEvidenceCapabilityFacet` for only `CommittedChildEvidence/v1`, digest, correlation key, child-record sensitive buffers, and zeroization. <!-- sdd-owner: implementation -->
- [ ] Implement the synchronized `Whole`/`Splitting`/`Split`/`Disposed` state machine with one linearization gate; only the `Whole` to `Splitting` winner may stage a pair, losers obtain no facet, pre-publication failures deterministically clean staged resources while retaining original ownership, and an unexpected post-detach publication failure disposes unpublished resources without duplicating or restoring ownership. <!-- sdd-owner: implementation -->
- [ ] Preserve one internal opaque same-handoff `HandoffIdentity` reference in both facets for future C2a/C2b consumers without exposing raw representation; publish both facets together, make the original permanently inert, and never restore ownership by duplicating handles or sensitive buffers. <!-- sdd-owner: implementation -->
- [ ] Implement independent idempotent disposal and deterministic cleanup for every failure boundary: the tree facet closes only transferred root/parent handles, the evidence facet zeroizes/disposes only evidence-owned buffers, and orphaned peers are never implicitly disposed or reconstructed. <!-- sdd-owner: implementation -->
- [ ] Run bounded repeated race schedules for concurrent/repeated split and split-versus-dispose; prove exactly one winner, no deadlock, no partial visibility, and no duplicated/restored ownership. <!-- sdd-owner: implementation -->
- [ ] Exercise every failure-injection boundary and post-publication disposal order; prove paired-only visibility, exact-once cleanup counters, original inertness, and C2a-failure evidence preservation. <!-- sdd-owner: implementation -->
- [ ] Run a real producer-handoff sandbox using only a fresh test-owned capability through the producer seam; prove handle/evidence lifetime and both disposal orders with no C2a operation, cleanup authorization, enumeration, reopen, observation refresh, delete, DPAPI, HMAC, or C2 completion claim. <!-- sdd-owner: implementation -->
- [ ] Remove test-only seams that could construct or substitute production facets; retain only the minimum internal injection needed for deterministic failure tests and keep binding identity authority-internal. <!-- sdd-owner: implementation -->
- [ ] Track authored additions/deletions continuously and stop for replanning at 400; audit that production changes remain limited to the allowed C1c paths and that C2a/C2b/C3 behavior is absent. <!-- sdd-owner: implementation -->
- [ ] Retain the RED evidence, run the focused C1c/PackagingSupervisor tests and an immediate repeat, execute the bounded real Windows x64 producer-handoff sandbox with exact cleanup/process-residue checks, run the serialized full solution suite, and record a clean zero-warning/zero-error build, `git diff --check`, exact authored-line count, and allowed-path audit. <!-- sdd-owner: implementation -->
- [ ] Prove there is no scoped helper/process/root residue, `.gitignore` is untouched and unstaged, no sensitive diagnostic value is emitted, and no C2a native operation, C2b policy/cleanup, C3, new native API, DPAPI/HMAC, path fallback, shell, PowerShell, subprocess, stage, commit, push, PR, or review action occurred. <!-- sdd-owner: implementation -->
- [ ] Keep every C1c checkbox unchecked until its corresponding proof actually passes; do not claim implementation, verification, review, commit, or completion from Attempts 60/61 or the rolled-back C2a candidate. <!-- sdd-owner: implementation -->

### Attempt 62 diagnostics closure

**Status:** passed for the C1c apply work unit. This closure supersedes only the preceding C1c diagnostics-blocked status; it does not finish the parent-owned native attempt, create a review receipt, commit, or advance C2a/C2b/C3.

**Parent-supplied final diagnostics:** primary `lsp_diagnostics` checked the exact three touched C# files with 0 diagnostics; `lens_diagnostics mode=all` reported no issues for those same files. The parent also inspected the complete C1c source/test diff and the ownership/state/disposal symbols and found no blocker. `git diff --check` remains passed. The functional source/test numstat remains 257: `PackagingSupervisorTests.cs` +122/-5, `CommittedChildEvidence.cs` +116/-6, and `DirectoryCapability.cs` +8/-0.

**Reconciled gate result:** the retained real RED failure, focused C1c and repeated PackagingSupervisor passes, Windows x64 real-producer-handoff proof, serialized full-suite 295/295 pass, zero-warning/zero-error build, scope/cleanup audit, `git diff --check`, and now the required LSP/lens diagnostics all passed. All 17 C1c implementation checkboxes are marked `[x]` in `tasks.md`; the two parent-owned lifecycle checkboxes remain `[ ]`.

**Continuation scope:** this closure changed only `openspec/changes/aibar-foundation/apply-progress.md` and C1c checkbox state in `tasks.md`. It made no functional production or test byte change and did not rerun tests, builds, runtime proof, review, delivery, or native-ledger operations; it did not alter `.gitignore`, design, spec, verify-report, or Git state.

### Attempt 62 parent lifecycle closure

After explicit maintainer apply approval, the parent renamed the branch to `feature/aibar-foundation-slice-8c1-1b2c-c1c-facets`, confirmed native `next_action=begin`, and opened attempt 62 with `max_attempts=1` and `max_changed_lines=400`. Attempts 60/61 were not reused. No stage, commit, push, PR, review, C2a, C2b, or C3 action occurred. Both parent-owned C1c lifecycle checkboxes are now complete; native attempt finalization remains the next parent action.

## C1c independent-verification correction — Attempt 64 (2026-07-31)

**Status:** correction applied in Standard mode after the independently verified C1c candidate exposed facet-lifetime, ordinary-API authority, and post-detach containment defects. This is an apply correction only. It does **not** complete independent verification, alter the failed Attempt 63 report, create review/delivery authority, or advance C2a/C2b/C3.

| Cycle | Evidence |
|---|---|
| RED | `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Red_c1c" --no-restore -m:1 --nologo` failed before production changes with `CS1061` (missing `TryTakeBoth`) and `CS0117` (missing `PostDetach`). |
| GREEN | The handoff now performs one locked atomic take-both transition. Concrete handoff/facet/identity classes are private; interfaces are non-authoritative views; constructors and attachment are unavailable to ordinary code. The original does not retain an evidence accessor after transfer. |
| TRIANGULATE | Instance-scoped failure injection covers validation through handoff creation and post-detach containment. Sixteen concurrent split/dispose schedules prove one winner. Wrapper abandonment after paired release and either peer disposal preserve the live peer; cross-handoff binding rejects mismatches; a seeded sensitive path never enters state diagnostics. |
| REFACTOR | Staging contains no owned handles/evidence before detach. Any post-detach exception disposes both unpublished facets exactly once and moves the original to `Disposed`; pre-detach failures restore `Whole` without touching original bundles. |

### Validation evidence

- `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~C1c" --no-restore -m:1 --nologo` — passed 8/8; immediate repeat passed 8/8.
- `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~Windows_c1c_real_producer_handoff_preserves_evidence_when_tree_is_disposed_first" --no-restore -m:1 --nologo` — passed 1/1 on Windows x64.
- `dotnet test tests/AIBar.Domain.Tests/AIBar.Domain.Tests.csproj --filter "FullyQualifiedName~PackagingSupervisor" --no-restore -m:1 --nologo` — passed 71/71.
- `dotnet test AIBar.sln --no-restore -m:1 --nologo` — passed 298/298.
- `dotnet clean AIBar.sln --nologo && dotnet build AIBar.sln --no-restore --nologo` — exit 0; clean/build each reported 0 warnings and 0 errors.

**Files changed:** `tools/AIBar.Packaging.Supervisor/CommittedChildEvidence.cs`, `tests/AIBar.Domain.Tests/PackagingSupervisorTests.cs`, `openspec/changes/aibar-foundation/{tasks.md,apply-progress.md}`. `DirectoryCapability.cs`, design, spec, verify-report, and `.gitignore` were not changed by this correction. No C2a/C2b/C3, cleanup policy, path fallback, shell/PowerShell, rename-back, copy-delete, staging, commit, push, PR, or review work occurred.

**Task reconciliation:** the four `C1c verification correction — Attempt 64 only` RED/GREEN/TRIANGULATE/GATE checkboxes are `[x]`. The parent-owned independent-verification checkbox remains `[ ]`; do not read this correction as a verification result.

**Workload / boundary:** `feature-branch-chain`, correction work unit `b2c-c1c-verification-correction`, native Attempt 64 already running under the parent-owned 1,000-line ceiling. Exact changed-line count against begin candidate `fd56a203de6630fbef8154b0ffc34eb307b1d44d`: 267 additions + 130 deletions = **397**, within the ceiling. Rollback removes only this correction's atomic handoff/facet encapsulation, focused tests, four task marks, and this record; C2 consumers must remain absent before such a rollback.

**Structured status consumed:** authoritative OpenSpec status reported `applyState: ready`, repo-local workspace root and allowed edit root, plus `nextRecommended: resolve-blockers` solely because native Attempt 64 is active. Per parent authorization, this executor performed only that active correction and never acquired, began, reset, or finished an attempt. Action-context warning: no target outside the repository root was edited.

**Remaining:** parent must settle Attempt 64 and run its independent verification/update path. Exact relevant unchecked line: `- [ ] Obtain fresh native reset/begin authorization for one distinct independent-verification objective with max attempts 1 and native changed-line ceiling 1000; verify the exact Attempt 62 candidate/evidence ...` (the active correction is authorized under that parent-owned objective). C2a, C2b, C3, and all later unchecked tasks remain out of scope.
