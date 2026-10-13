# Partial-identity test teardown

## Change

The final cleanup of
`Partial_identity_cleanup_preserves_primary_failure_and_harness_root` now
reuses the existing `DescendantHarness.Dispose()` before closing its process
reference. It no longer bypasses identity/ownership admission with an
unconditional recursive directory deletion.

This preserves uncertain harness evidence and prevents a locked-file deletion
error from replacing the primary test exception. It adds no new process
protocol, diagnostic framework, timeout or production publishing behavior.

## Regression coverage

Two deterministic cases exercise the same final cleanup with and without a
delete-denying handle on `Harness.dll`. They assert the exact original
exception object/message and unchanged directory names and every file byte.
These cases launch no processes; their own final deletion occurs only after
unlocking the process-free root they created.

RED: both cases failed with the original cleanup (missing root without a lock;
IOException replacing InvalidOperationException with a lock). Restore/build
passed, actual test exit was one, and tracked source identities were unchanged
before/after except the fixture's intentional change.

GREEN passed 3/3: both deterministic cases and the existing process test.
Restore/build/test exited zero, with no failed, timed-out, aborted or
unexecuted case and completed output drains. Tracked input identities matched
before/after. Independent review admitted the narrow correction with no
blocking finding; the parent repeated the fixture SHA check and matched the
reviewed/tested source. This does not close full-suite or historical-CI work.

## Limits

This unit changes tests only. It does not identify the original DLL holder or
prove whether earlier assertions were hidden. The two packaging lifecycle
failures, the historical main-CI mutex failure and full-suite validation remain
separate pending work. No full suite or GitHub CI was repeated here.
