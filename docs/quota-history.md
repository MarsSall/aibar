# Quota sample history

AIBar records successful Codex and Claude quota retrievals in
`%LOCALAPPDATA%\AIBar\quota-history.db`. This is independent of the single-snapshot
quota caches and the Codex export v1 format. **Pace & details** displays this history and uses it for observed pace; see [panel details](panel-details.md).

## What is recorded

Each available window produces one row: provider (`codex` or `claude`), window
(`five-hour` or `weekly`), percentage used (0–100), reset instant, retrieval instant,
and source (`codex-local`, `claude-local`, or `claude-remote`). Missing windows do
not produce synthetic rows. No credentials, account identifiers, paths, prompts,
or token payloads are stored.

An observer on `QuotaRefreshCoordinator.AuthorityUpdated` queues a snapshot only
when `RetrievalGeneration` increases. Loading, cache initialization, failed
retrievals, countdown/freshness changes, and other presentation notifications do
not create samples. Successful remote Claude retrievals may contain an older
retrieval timestamp: they are recorded with that original timestamp, not the
poll time. SQLite keeps the first row for a provider/window/retrieval instant,
including across restarts and Claude source changes. Equivalent UTC instants
with different offsets deduplicate.

Codex has one recorder. Claude creates a recorder for **each** new coordinator,
including consent grants, explicit source changes, and automatic source handoffs.
Each recorder captures its source identity before retrieval. The recorder's
callback only queues work: SQLite runs on a background worker and no outward
runtime/UI notifications are raised under Claude's consent gate. Accepted
retrievals are queued without coalescing. History write failures are isolated
from quota authority; they are not retried until a later retrieval and are not
logged with private exception details.

## Storage and retention

The database has its own `PRAGMA user_version = 1`, WAL journaling, and a serialized
read/write lifecycle. Connections are unpooled and closed after each operation.
Future schema versions are rejected rather than overwritten.

A snapshot batch is appended atomically. On every append, rows across **all**
providers strictly older than the supplied clock time minus 30 days are pruned
in the same transaction. A row exactly at the cutoff is retained. Old incoming
rows are pruned too. There is no synthetic interpolation or background retention
job; without an append, old rows can remain on disk until the next append or clear.
UTC ticks retain full timestamp precision and avoid lexical offset comparisons;
percentages retain decimal precision as invariant text.

## API for history and observed pace

`AIBar.Domain.IQuotaHistoryStore` exposes:

- `AppendAsync(samples, now, cancellationToken)` for normalized atomic batches.
- `QueryAsync(provider, window, fromInclusive, toInclusive, cancellationToken)`
  for a provider/window UTC-instant range, ordered by retrieval ascending.
  Both endpoints are inclusive. An empty range result is an empty list.
  Reversed endpoints, invalid enum values, invalid percentages, and a source
  inconsistent with its provider are rejected. Disposed stores reject access.

Desktop composition exposes the shared store as `StartupComposition.QuotaHistory`.
`PanelPaceHistory` uses that same store for chart and pace reads, off the UI thread.
The chart requests each provider/window separately over inclusive 24-hour, 7-day,
or 30-day ranges ending at the read clock time. It keeps all four series on a
fixed 0–100% scale and breaks segments when the reset instant changes or usage
decreases. Missing history is not interpolated.

`ObservedPacePolicy` selects samples matching provider, window and the current
`ResetAt`, inside the nominal cycle start through evaluation time (inclusive).
It sorts and deduplicates retrieval instants. With at least two distinct samples,
strictly more than 5% elapsed, and no decreasing interval, the observed rate is
`(last percentage - first percentage) / (last retrieval - first retrieval)`.
The pace ratio is this rate divided by `100% / nominal duration`; projected
exhaustion is evaluation time plus `(100% - current usage) / observed rate`.
Zero observed consumption has ratio zero and no exhaustion instant. Projections
beyond the date type's maximum have no representable exhaustion instant.

Insufficient or inconsistent history retains `QuotaPacePolicy`'s snapshot result,
including its exact 5% boundary, limit-reached and expired-reset behavior. Only
current, fresh, successful quota states use observed pace; stale data remains
**Not current** without a warning. History errors never replace quota authority.
Pace rows retain their wording and expose **based on observed use** in accessible
help when applicable. Presentation updates use revision checks to discard results
from superseded states, consent/source changes, or clear operations.

Queries during the privacy-clear pause fail with `InvalidOperationException`.
The desktop reader joins clear before the store: it synchronously pauses reads
and advances its publication revision, then cancels the retired epoch without
waiting for cancellation callbacks, a query, or the WPF dispatcher. UI invalidation
runs immediately on the owner thread or is posted without awaiting it. Consumers
check the reader revision again before publishing, so even an already-completed
read queued for UI delivery cannot restore pre-clear data after resume. The store
itself owns draining database access later in the clear sequence.

Epoch replacement, pause/resume and disposal are serialized; disposal is idempotent,
and resume after disposal cannot recreate a reader. Each retired cancellation
source has one owner and is disposed only after its asynchronous cancellation
callbacks finish. Closing the disclosure also cancels chart reads and drops chart
data. Unloading the chart stops its timer and removes its invalidation subscription.
The next visible refresh queries again; there is no persistent presentation history
cache.

## Clear and shutdown

Clear AIBar Data stops provider work, cancels/discards pending history writes,
and drains already-entered writes (even a cancellation-ignoring writer) before
deleting the history database and its `-wal`/`-shm` sidecars. The shared store also
joins the clear lifecycle to drain reads and reject access until resume. The
recorder clears its pending in-memory snapshots and resumes from the current
retrieval generation; it cannot replay a pre-clear sample. There is no in-memory
history cache. The database is recreated lazily by the next operation. Claude
remains disabled after clear until a new explicit consent grant.

Normal shutdown, source handoff, and consent revocation drain accepted history
before disposing the recorder; existing history remains subject to retention
and Clear AIBar Data. Shutdown disposes provider owners and both recorders before
the shared database store.

## Validation

Tests are separate from implementation so the parent can commit tests alone
and observe RED in Windows CI, then commit implementation and observe GREEN:

- `tests/AIBar.Domain.Tests/SqliteQuotaHistoryStoreTests.cs`
- `tests/AIBar.Domain.Tests/QuotaHistoryRecorderTests.cs`
- History allowlist assertions in `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`
- `tests/AIBar.Domain.Tests/ObservedPacePolicyTests.cs`
- `tests/AIBar.Domain.Tests/ObservedPacePresentationTests.cs`
- `tests/AIBar.Domain.Tests/QuotaHistoryChartTests.cs`
- `tests/AIBar.Domain.Tests/QuotaHistoryChartLifecycleTests.cs`

The Windows workflow restores/builds `AIBar.sln` and runs `dotnet test AIBar.sln`
with its existing no-build/no-restore, Debug, logger and results-directory options.
The Linux implementation environment has neither dotnet nor PowerShell; no local
RED/GREEN execution is claimed.
