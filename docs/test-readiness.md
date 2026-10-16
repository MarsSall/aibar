# Readiness signals in concurrency tests

The analytics clear and Pi fan-in concurrency tests wait for explicit readiness
signals instead of imposing short performance requirements on fixture startup.
This is a test-only correction; production code and shutdown policy are unchanged.

## Bounds have different purposes

| Bound | Purpose |
| --- | --- |
| 2-second analytics shutdown | Existing production behavior, unchanged |
| Shared 30-second coordination watchdog | Detect missing test readiness/progress, not measure product speed |
| Independent 30-second cleanup join | Observe retained work after releasing the fixture, even if the coordination watchdog expired |

The watchdog replaces the clear test's 2-second startup observation and Pi's
5-second blocking entry wait. It is not a claim that startup now meets a different
service-level objective. Existing intentional analytics timeout/failure tests
retain their limits and blocking behavior.

## Analytics clear

The preliminary disable has already completed the harness's first-cancellation
signal. `ArmClearCancellation()` creates a new notification for clear; arming must
not complete it or release the scanner. On that subsequent cancellation, the
opt-in observer releases the deliberately blocked scanner before completing the
notification. Thus the production shutdown interval does not depend on scheduling
the test continuation merely to release its own fixture.

Blocked-scan entry is awaited asynchronously. Cleanup releases the gate and, for
an armed clear with a blocked scan, joins that scan before disposing the host.
The regression test rejects reuse of the preliminary disable's signal.

## Pi fan-in

The owner runs on a separate task because its synchronous callback can block
before `RunAsync` returns. Entry is an asynchronous signal. The queued caller must
remain pending until cancelled; cancelling it must leave the held owner pending.
The owner must then complete with one callback and one committed event.

Cleanup always releases the gate and joins the owner before disposing the ledger.
If the cleanup join also fails, both failures are reported. If the owner remains
live, its ledger and fixture directory are retained rather than deleted underneath
it. The watchdog is outside the assertion expecting queued-caller cancellation,
so watchdog cancellation cannot make that assertion pass.

## Evidence boundary

Earlier controlled delay demonstrated late successful scan completion after an
observation timeout. It did not identify the historical CI delay's cause. These
changes address observable fixture synchronization hazards, not a proven product
scan defect. No production timeout is increased and no assertion about deletion,
source preservation, owner completion, or committed-event count is removed.
