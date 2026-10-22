# Developer notes

These pages record design decisions, test contracts and accepted coverage gaps. They are for contributors, not for AIBar users. User documentation lives one level up in [`docs/`](../).

| Note | Topic |
| --- | --- |
| [quota-projection-v1.md](./quota-projection-v1.md) | Deterministic quota-projection policy and its review path |
| [optional-window-compatibility.md](./optional-window-compatibility.md) | Optional 5-hour and weekly windows in the popup and YASB export |
| [packaging-lifecycle-contracts.md](./packaging-lifecycle-contracts.md) | Packaging supervisor deadline and descendant-evidence contracts |
| [packaging-partial-teardown.md](./packaging-partial-teardown.md) | Partial-identity test teardown |
| [mutex-harness-recovery.md](./mutex-harness-recovery.md) | Single-instance mutex test synchronization |
| [test-readiness.md](./test-readiness.md) | Readiness signals in concurrency tests |
| [validation-test-observations.md](./validation-test-observations.md) | Cancellation observation and cleanup-bound diagnostics |
| [tray-consent-test-retirement.md](./tray-consent-test-retirement.md) | Retired tray consent integration test and its accepted gap |

Change specifications live in [`openspec/`](../../openspec), and implementation task logs in [`odd/tasks/`](../../odd/tasks).
