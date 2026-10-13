# Packaging lifecycle test contracts

This test-only unit separates deadline classification from descendant evidence.
It does not change the publisher, its operation deadlines, cancellation inputs,
or the production termination grace.

## Completion budget

Asynchronous completion now uses the same optional bound as synchronous
completion: absent an override, the existing operation budget plus ten seconds
of wrapper grace. That grace covers startup, failure serialization, wrapper
shutdown and outer pipe EOF. It is not a new public publisher guarantee.
Explicit short overrides continue to exercise managed-deadline cleanup.

## Readiness and termination are different evidence

The one-second deadline case uses the permanent `diagnostic-timeout` mode. It
never signals Ready or records descendants; the test asserts timeout kind,
stage, declared deadline/grace, both diagnostic streams, nonzero wrapper exit
and preserved incomplete output. It makes no descendant-termination claim.

The cancellation case retains the 30-second operation limit and 5000ms
cancellation input. It requires Ready, opens PID/start-time-validated handles,
and asserts both descendants exited when completion returned. It also checks
cancelled classification and preserved incomplete output.

The normal grandchild now waits for the existing explicit Release event or
process termination, rather than naturally exiting after ten seconds. Its
regression crosses that former boundary, verifies it remains alive, then
releases it and checks exit code and exact stdout/stderr. Existing fixture
cleanup owns Release and identity-gated process termination. No new IPC is added.

## Verification boundary

Three deterministic RED cases failed as expected: no-ready prerequisite,
eight-second wrapper override, and natural grandchild expiry. Restore/build
exited zero; test exited one; all drains completed with no supervisor deadline.
The first GREEN executed all 17 relevant cases: 16 passed, one failed reading
ExitCode after termination from a PID-opened process whose handle was not
retained. The failed run and its inputs remain preserved. The regression now
retains the validated process handle before waiting. Corrected verification
passed all 17 relevant cases: restore/build/test exits 0/0/0, no failed, error,
timeout, aborted or unexecuted cases, and completed drains. All 264 canonical
inputs matched before/after; only the two authorized source files changed.
Independent review admitted S1/S2 with no blocking findings; the parent matched
both reviewed/tested source hashes. Builds retained 27 warnings. The download-only
job7 command/exit are harness-reported: its empty log is not independent receipt
proof. This does not close historical full-suite or GitHub CI failures and does
not authorize publication or resource cleanup.
