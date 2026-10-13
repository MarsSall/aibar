# Validation test observations

This test-only unit is separate from optional-window compatibility. Production behavior, cleanup bounds, cancellation ordering and the existing test harnesses remain unchanged.

## Cancellation observation

The revocation test awaits the existing `CancellationObserved` task with the original two-second bound, then asserts that the manual cancellation signal is set. The harness sets both signals at the same `analytics_cancelled` lifecycle event. Scan release and all shutdown, event-count and presentation assertions remain unchanged.

This removes the synchronous cancellation wait from this test; it does not establish the cause of the previously observed deadline miss or prove a production cancellation defect.

## Cleanup bounds diagnostics

Depth, entry-count and cancellation scenarios still run with their original arrangements. Each requires unsuccessful cleanup, zero completed deletes, retained quarantine and `CleanupPartial`. The success code remains `guarded-cleanup-bounds-verified`.

On failure, `guarded-cleanup-bounds-refused` includes a named `depth`, `entries` and `cancellation` snapshot: case `passed`, cleanup `succeeded`, completed `deletes`, quarantine `retained`, `status` and `gate`. This identifies failed case predicates without changing cleanup admission or the private helper. It does not identify every early setup stage: default tuples can still arise before cleanup.

## Verification boundary

The original job24 failure is retained: 616/618 full-suite results, with cancellation observation and aggregate cleanup bounds failures. No historical cause or flakiness is inferred from unchanged source or later passes.

Focused native Windows execution selects revocation, cooperative production exit, cleanup bounds and the existing cleanup fault matrix. Native Windows validation passed 4/4 selected cases. Restore, Debug build and focused test exited 0 without timeout, with completed output drains. The TRX contains no failures, errors, aborted or unexecuted cases; all 267 canonical input records matched before and after execution. Machine-local evidence is retained under `.atl/test-observations-evidence/` in the parent checkout.

No full-suite rerun, YASB execution, installation or publication belongs to this unit. The bounds case passed, so failure-message execution remains unproven; its coverage is structural only. These passes do not establish historical causality or full integrated WU1A acceptance.
