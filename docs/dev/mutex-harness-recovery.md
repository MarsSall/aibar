# Minimal mutex test synchronization

## Scope

Only the existing mutex test fixture changes; production `SingleInstanceHost`
and CI workflows are unchanged. The rejected named-pipe identity protocol is
not part of this unit. Its failed evidence is preserved separately.

The correction keeps parent acquisition/disposal on one synchronous thread.
After tree termination is requested, root exit and public `IsPrimary`
observation share the existing five-second budget. Unsuccessful candidates
are disposed; the successful instance is retained for the recovery assertion.
A secondary public host keeps the named mutex object open across abandonment,
so recovery does not pass merely because a replacement object was created.
Original handoff, activation, root exit-zero and recovery assertions remain.
The child checks ownership before signaling ready.

## Regression

`Recovery_waits_for_actual_mutex_availability` uses a controlled owner thread
and existing public `SingleInstanceHost` operations. The first unavailable
attempt signals that owner to release and waits for its termination; a later
attempt must acquire. No sleeps, process-identity IPC or diagnostic tracing
control the case. The callback only coordinates this deterministic test.

RED against the one-shot acquisition assumption failed exactly the recovered
IsPrimary assertion (one executed case, actual exit one). Focused GREEN passed
12/12 cases, including the original cross-process case and the new regression;
restore/build/test exited zero with no aborted, timed-out or unexecuted cases.
The full suite finished with actual exit one: 608/611 passed. All 12 host cases
passed again, but three existing `PackagingRecoveryTests` cases failed (managed
cancellation deadline, descendant readiness and locked `Harness.dll` cleanup).
Their source was unchanged; no baseline comparison establishes pre-existence
or independence. Suite input identities matched focused GREEN exactly.
Those failed records remain preserved. Separate teardown and lifecycle test
corrections were then verified and committed as `f252d888` and `04ef63b3`.
The authorized combined candidate, based on the latter commit and retaining the
exact previously tested mutex fixture, passed the full Windows .NET assembly:
615/615, restore/build/test 0/0/0, no exceptional counters, completed drains.
Independent concurrency review admitted T2 and S1–S5 with no blockers; the
parent matched the reviewed/tested fixture hash. Joint build retained 27
warnings and zero errors. Streams are retained directly as
`{restore,build,test}.{stdout,stderr}`. The reviewer could not access the supplied
historical CI log, so that historical corroboration remains parent-reported.
No historical CI reproduction or GitHub CI closure is claimed.

## Evidence and limits

Windows checks use the already authorized isolated native Git checkout,
including repository-local CRLF checkout settings. Git-clean tracked inputs
were compared to `2ec1210bc59bb2a73826ac167f7dc7eacde01efc` for the original
checks. Joint validation compares all 265 canonical inputs to `04ef63b3`;
only the mutex fixture differs and before/after inputs match. Packaging source
is included unchanged from its separately verified commits.

The discarded identity-protocol candidate passed 11/13 focused cases and
failed two ready waits. Those are not evidence about production AIBar.
Historical main CI run `38324395259` remains failed (609/610); its precise
cause was not established. This change repairs test synchronization
assumptions, not a demonstrated production defect.

No installation, CI rerun, push, PR, merge or cleanup is authorized here.
WU1A and the original mixed WIP remain separate.
