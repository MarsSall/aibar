# AIBar Foundation Specification

## Purpose

Define the technology-neutral MVP behavior for a local Windows tray application that presents Codex quota status and privacy-conscious local usage analytics. Private service quota, locally derived analytics, and estimated cost are separate data classes and MUST remain distinguishable.

## Requirements

### Requirement: Codex session authentication discovery

The system MUST discover supported Codex CLI authentication/session state from the configured initial Codex root and MUST read only the minimum fields needed for quota requests. Discovery MUST never modify Codex-owned files, request a password, extract browser cookies, or automate a browser session.

#### Scenario: Supported authentication is available

- GIVEN a readable supported Codex authentication file containing a usable access credential and optional account identifier
- WHEN quota refresh begins
- THEN the system uses the discovered values for that request without changing the source file

#### Scenario: Authentication is unavailable

- GIVEN the supported authentication file is missing, unreadable, malformed, or lacks a usable credential
- WHEN quota refresh begins
- THEN the system reports an explicit authentication or unavailable state and directs the user to restore the Codex CLI session

### Requirement: Private quota integration and schema isolation

The system MUST isolate the private quota integration behind a boundary and MUST treat its response as authoritative only for fields successfully returned by that response at that time. It MUST support the primary 5-hour window, weekly window, reset times, and optional detail fields when present, tolerate missing or changed fields, and identify the integration as private, undocumented, and unsupported rather than promising stability.

#### Scenario: Valid quota response

- GIVEN a request succeeds and contains valid primary and secondary window data
- WHEN the response is processed
- THEN the system exposes the returned percentages, reset times, duration metadata when available, and refresh timestamp as private service-reported quota data

#### Scenario: Schema drift or malformed response

- GIVEN a response is malformed or omits a required primary field
- WHEN it is processed
- THEN the system rejects or degrades only the affected quota result, preserves any previously valid snapshot according to stale rules, and presents an explicit error or unavailable state

#### Scenario: Optional detail fails

- GIVEN the primary quota response is valid but an optional detail request fails
- WHEN refresh completes
- THEN the valid primary snapshot remains available and the optional detail is marked unavailable

### Requirement: Quota presentation semantics

The tray MUST show percentage used for the configured primary quota window only when a current or explicitly stale percentage exists. The system MUST NOT fabricate a percentage or silently present an old value as current. The popover MUST distinguish service quota data from local analytics and estimated cost.

#### Scenario: Current percentage

- GIVEN a successful non-stale primary quota snapshot
- WHEN the tray and popover are displayed
- THEN the tray shows the reported percentage used and the popover identifies its source and timestamp

#### Scenario: Unavailable or stale percentage

- GIVEN no valid snapshot exists, or the snapshot exceeds the product's freshness threshold
- WHEN the tray and popover are displayed
- THEN they show a distinguishable unavailable or stale state and do not label the value as current

### Requirement: Quota windows, freshness, and manual refresh

The popover MUST show the 5-hour and weekly windows independently, their service-reported reset countdowns, the last successful refresh time, loading state, and explicit authentication, permission, malformed-response, network, and service errors. Countdown calculations MUST use service-reported reset times and an explicit time basis. Users MUST be able to request a manual refresh.

#### Scenario: Both windows are available

- GIVEN a successful response provides both windows and reset timestamps
- WHEN the popover is opened
- THEN it shows both percentages, both countdowns, the freshness timestamp, and no fabricated local reset time

#### Scenario: Manual refresh

- GIVEN the user activates manual refresh
- WHEN a refresh is initiated
- THEN the UI shows loading, bypasses ordinary freshness suppression, and ends with updated data or a visible error without erasing an otherwise valid snapshot

### Requirement: Cache-aware polling and overlap prevention

The system MUST render the last successful quota snapshot before network completion, retain its timestamp, poll conservatively, avoid unnecessary requests for fresh data, and prevent overlapping refresh operations. A failed refresh MUST preserve the prior snapshot as stale or failed rather than making it appear current.

#### Scenario: Fresh cached snapshot

- GIVEN a fresh cached snapshot exists
- WHEN ordinary polling is triggered
- THEN the system may render the snapshot without issuing an unnecessary request

#### Scenario: Concurrent triggers

- GIVEN a refresh is already in progress
- WHEN polling or manual refresh is triggered again
- THEN the system does not start an overlapping request and reports the existing operation's state

### Requirement: Incremental JSONL discovery and checkpointing

The system MUST discover only the supported initial Codex session root and its supported archive/layout forms. It MUST stream JSONL incrementally, maintain checkpoints sufficient to avoid recounting unchanged content, invalidate checkpoints when file identity or parser semantics require it, and support cancellation or bounded background work. The system MUST report scan coverage and last-scan status.

#### Scenario: Unchanged rescan

- GIVEN a supported history has been fully scanned and remains unchanged
- WHEN it is scanned again
- THEN daily totals do not increase and the scan reports reused or unchanged coverage

#### Scenario: Appended events

- GIVEN a previously checkpointed file receives supported appended events
- WHEN the incremental scan runs
- THEN only the new supported records contribute deltas

### Requirement: Tolerant parsing and non-negative token deltas

The system MUST tolerate malformed, truncated, unreadable, changing, and partially supported files without ingesting prompt or response bodies. For cumulative token snapshots it MUST compute each input, cached-input, and output delta independently as `max(0, current - previous)`; last-event values MUST be clamped to non-negative values. It MUST preserve partial-coverage warnings and MUST NOT claim completeness when records were skipped.

#### Scenario: Malformed or truncated input

- GIVEN a JSONL file contains malformed or incomplete lines
- WHEN it is scanned
- THEN valid records are processed, invalid portions are skipped or deferred, and the result includes a coverage warning

#### Scenario: Counter reset or decrease

- GIVEN a cumulative component is lower than its prior checkpoint
- WHEN its delta is computed
- THEN that component contributes zero rather than a negative or inflated value

### Requirement: Daily local analytics and model attribution

The system MUST aggregate local input, cached-input, and output token totals by explicit local calendar day and model. It MUST preserve timestamp and timezone provenance, use an explicit timezone policy including daylight-saving behavior, assign missing or untrusted model evidence to an explicit `Unknown` bucket, and never guess a named model. It MUST define most-used model as the model with the greatest total tokens under a documented formula; cached input MUST be explicitly stated as included or excluded from that formula. Local totals MUST be labeled locally derived and MUST NOT be treated as provider billing or quota source data.

#### Scenario: Missing model evidence

- GIVEN a valid token event has no reliable model identifier
- WHEN it is aggregated
- THEN its tokens appear under `Unknown` and are not assigned to a named model

#### Scenario: Model ranking

- GIVEN representative daily model totals
- WHEN the breakdown and most-used model are shown
- THEN the result matches the published token formula and does not rank by estimated cost

#### Scenario: Timezone boundary

- GIVEN an event timestamp crosses a local midnight or daylight-saving transition
- WHEN it is aggregated
- THEN the event is assigned according to the declared local timezone policy and the displayed data retains sufficient time-basis provenance

### Requirement: Estimated cost provenance and warnings

The system MAY calculate cost by applying versioned pricing assumptions to local token totals, but every result MUST be labeled an estimate, expose pricing version/date and calculation provenance, and warn for unknown or unsupported models and for repricing, discounts, routing, contracts, or other non-authoritative billing factors. It MUST never label local estimates as billed cost, invoice, credit balance, or authoritative provider spend.

#### Scenario: Supported model estimate

- GIVEN local totals and a matching versioned price table
- WHEN estimated cost is displayed
- THEN the value includes estimate labeling and pricing provenance

#### Scenario: Unsupported model estimate

- GIVEN totals include `Unknown` or a model without supported pricing
- WHEN cost is displayed
- THEN the system shows an explicit warning and does not silently apply a named-model rate

### Requirement: Trends, pace, burn, and exhaustion estimates

The system MAY show trends, pace, burn rate, and linear exhaustion ETA only with clearly named calculation windows, time basis, and estimate labeling. Linear exhaustion MUST be derived from observed usage and remaining service-reported quota under a documented rule, MUST not be called a prediction guarantee, and MUST not be presented as historical or probabilistic forecasting. The MVP MUST NOT reconstruct hourly history from daily aggregates or emit probabilistic forecasts before the later-history thresholds are met.

#### Scenario: Insufficient history

- GIVEN insufficient complete data exists for a requested trend or estimate
- WHEN the metric is displayed
- THEN it is marked unavailable or insufficient-data rather than inferred

#### Scenario: Linear ETA

- GIVEN a valid quota snapshot, reset window, and positive observed burn rate
- WHEN exhaustion ETA is calculated
- THEN the UI shows the window, rate basis, remaining quota basis, and labels the result as a simple linear estimate

### Requirement: Startup configuration

The system MUST provide a user-visible setting to enable or disable starting with Windows and MUST reflect the effective setting without requiring unsupported operating-system behavior.

#### Scenario: Startup toggle

- GIVEN the user changes the startup setting
- WHEN the setting is saved
- THEN the system applies or removes startup behavior and displays the resulting state

### Requirement: Local-first privacy and data clearing

The system MUST process and store initial data locally with telemetry disabled by default. It MUST never collect, store, transmit, index, or display prompt or response content. It MUST keep credentials in memory only as long as needed, redact bearer values, sensitive account identifiers, secrets, and headers from UI, logs, diagnostics, crash data, and errors, and MUST NOT persist access tokens in AIBar-owned plaintext storage. It MUST treat session metadata, paths, timestamps, caches, and diagnostics as sensitive. Users MUST be able to clear AIBar-owned caches and derived analytics without deleting or modifying Codex-owned source files.

#### Scenario: Persistence inspection

- GIVEN application-owned persistence, logs, and diagnostics are inspected after processing
- WHEN sensitive-data checks run
- THEN no prompt/response body or unredacted bearer credential is present

#### Scenario: Clear application data

- GIVEN AIBar-owned quota cache and derived analytics exist alongside Codex-owned files
- WHEN the user clears application data
- THEN AIBar-owned data is removed while Codex-owned source files remain unchanged

### Requirement: Windows compatibility policy

The system MUST support the tray, popover, refresh, analytics, and startup behavior on Windows 10 and Windows 11 where feasible without disproportionate complexity. If a concrete incompatibility requires Windows 11-specific behavior, the system MUST document the limitation and preserve an explicit state for unsupported behavior rather than silently failing.

#### Scenario: Supported Windows versions

- GIVEN a representative supported Windows 10 or Windows 11 environment
- WHEN the application is used
- THEN core tray and popover behavior works, or any exception is documented with a visible limitation

### Requirement: Slice 8C1.1b1a descendant-quiescence RED contract

The Slice 8C1.1b1a harness MUST prove the current publisher/direct invocation lacks descendant-tree quiescence evidence without implementing production lifecycle behavior. It MUST use a runtime-generated external `net8.0` direct executable, GUID-named readiness/release events, explicit owned process handles and records, exact discrete arguments, and external roots containing spaces and NFC Unicode. The child MUST launch a known grandchild, wait for readiness, then exit successfully while the known grandchild remains alive. Saturated stdout/stderr MUST NOT be the RED premise.

#### Scenario: Publisher completes while an owned grandchild remains alive

- GIVEN the direct child launches its known grandchild, signals readiness, and exits zero while the grandchild remains alive
- WHEN the current publisher/direct invocation completes
- THEN the harness proves completion occurred without descendant-exit or descendant-tree-quiescence evidence
- AND the result is retained as the b1a RED proof without production lifecycle changes

#### Scenario: Owned teardown removes only the harness root

- GIVEN the RED run has recorded child/grandchild identities and owned handles
- WHEN the release event is signaled and bounded teardown is performed
- THEN the harness uses targeted termination only if needed, performs a second bounded wait, verifies identity/marker/containment evidence, and deletes only its external root

#### Scenario: Stream saturation is not treated as deterministic proof

- GIVEN a child writes to stdout or stderr without relying on pipe saturation
- WHEN the direct invocation completes or fails
- THEN the harness does not classify stream saturation or a saturated-pipe deadlock as the b1a RED condition
- AND production timeout, concurrent draining, tree termination, and lifecycle integration remain deferred to Slice 8C1.1b

### Requirement: Non-goals and deferred capabilities

The MVP MUST NOT claim support for other providers, multiple accounts or roots, WSL attribution, per-project analytics, authoritative billing/invoices/credits, prompt or response ingestion, browser-cookie or password flows, complete fork/replay/cross-file deduplication, reconstructed hourly history, or historical/probabilistic forecasting. Later capabilities MAY be considered only as explicitly scoped follow-up work, including broader providers, multiple roots/accounts, project analytics after privacy review, event/hourly retention, advanced deduplication, and historical forecasting after at least three complete weeks (with run-out probability requiring at least five).

#### Scenario: Unsupported request

- GIVEN a user or configuration requests a deferred capability
- WHEN the system evaluates the request
- THEN it presents an explicit unsupported or later-capability state and does not silently approximate it as MVP behavior

## ADDED Requirements — Slice 8C1.1b2 packaging supervisor

### Requirement: Closed typed supervisor protocol

The packaging supervisor MUST accept only a length-bounded closed-schema operation selected from a fixed allowlist, with operation-specific typed arguments and caller-selected ownership authority. It MUST NOT accept generic shell text, arbitrary roots, caller-supplied identity/nonce/cleanup markers, arbitrary environment, or echo paths, secrets, command text, PIDs, or capability material.

#### Scenario: Invalid or sensitive request

- GIVEN a request contains shell text, an arbitrary root, an identity claim, or an unknown field
- WHEN the supervisor validates it
- THEN it rejects with `INVALID_REQUEST` and emits no sensitive value

### Requirement: Kernel-owned process membership

The supervisor MUST create a fresh Job Object with `KILL_ON_JOB_CLOSE` and no breakaway, create the root worker suspended, assign it before resume, and retain safe process/thread handles through assignment, resume, and exit-code retrieval. Any start or assignment failure MUST terminate the retained process safely before returning failure.

#### Scenario: Assignment failure

- GIVEN a suspended root cannot be assigned to the fresh Job Object
- WHEN assignment fails
- THEN the supervisor terminates and closes the retained process safely and returns `JOB_ASSIGN_FAILED` without cleanup success

### Requirement: Bounded worker lifecycle and streams

The supervisor MUST drain stdout and stderr concurrently from launch into bounded diagnostic tails, discard excess bytes with counts, retrieve the root exit code, and enforce one overall deadline plus bounded post-quiescence EOF grace. Timeout and production cancellation MUST terminate the Job and use the same bounded lifecycle proof.

#### Scenario: Saturation, timeout, or cancellation

- GIVEN either stream saturates or the worker exceeds its deadline or is cancelled
- WHEN the supervisor handles the condition
- THEN drains remain bounded, the Job is terminated, and it returns `OUTPUT_DRAIN_FAILED`, `TIMEOUT`, or `CANCELLED` without hanging

### Requirement: Authoritative quiescence

Completion notifications MUST be advisory wake-ups only. Success MUST require a signaled root handle, zero root exit code, and two successful bounded `ActiveProcesses == 0` queries separated by the observation interval while live Job and root handles remain open. Unknown or nonzero query results MUST fail closed; closing the Job MUST NOT be completion evidence.

#### Scenario: Lost, duplicate, or reordered notifications

- GIVEN completion packets are lost, duplicated, delayed, or out of order
- WHEN the supervisor evaluates completion
- THEN packets cannot decide success or failure; only the bounded repeated live-handle queries can do so

### Requirement: Explicit external-process boundary

The supervisor MUST document that processes created through pre-existing build servers, brokers, or services are outside Job membership and MUST NOT claim to own them. Worker configuration MUST disable supported build-server delegation where possible; inability to disable it MUST produce a conservative non-success outcome.

#### Scenario: Active-process uncertainty

- GIVEN a query fails, remains uncertain, or a delegated process may be outside the Job
- WHEN quiescence is evaluated
- THEN the supervisor returns `QUIESCENCE_UNPROVED` and does not clean up

### Requirement: Capability and physical identity admission

For C1b, capability preparation MUST retain the admitted source capability and a distinct non-reparse quarantine-parent capability, including their identities, share state, and same-volume relationship. C1b0 MUST prove on supported Windows 10/11 x64 the approved user-mode `NtSetInformationFile(FileRenameInformation)` relative-rename contract: the retained source handle identifies the source, the retained quarantine-parent handle identifies the relative target root, the target is one validated simple leaf, replacement is disabled, and every non-success `NTSTATUS` fails closed. C1b1 MUST NOT start without that proof. Source-path reopen, destination-path resolution, Win32 rename projections, and unknown, substituted, extra, missing, or raced identity MUST fail closed. Malicious same-user or administrator interference is outside the threat-model guarantee.

#### Scenario: C1b0 is a prerequisite

- GIVEN C1a admission exists but C1b0 rename-ready capability preparation is not separately verified
- WHEN C1b1 commit is requested
- THEN no native call or namespace mutation is attempted and the source remains admitted but uncommitted

#### Scenario: C1b0 proves the approved native contract

- GIVEN supported Windows 10/11 x64 and retained source and same-volume quarantine-parent capabilities pass identity, reparse, share, child-set, and leaf checks
- WHEN the bounded C1b0 proof runs
- THEN it proves retained-handle relative rename success, collision refusal without overwrite, invalid-leaf refusal before the native call, fail-closed status handling, and exact resource disposal with no residue

#### Scenario: Unsupported native contract

- GIVEN Windows, process architecture, layout, information class, entry point, or source/parent relationship is unsupported or unprovable
- WHEN C1b0 evaluates compatibility
- THEN it refuses the proof, does not admit C1b1, and performs no fallback mutation

#### Scenario: Source-path substitution

- GIVEN the retained source or quarantine-parent handle remains bound while its path spelling is substituted
- WHEN C1b1 attempts the quarantine commit
- THEN the substituted path cannot redirect the mutation; the commit uses the admitted identities or fails closed without path resolution

#### Scenario: Admission identity drift

- GIVEN the source, parent, or complete observed child set changes, becomes reparse-tainted, loses required share compatibility, or becomes unprovable before commit
- WHEN the final admission gate runs
- THEN it returns `CLEANUP_REFUSED` before the native call and preserves the source namespace

### Requirement: C1b identity-bound quarantine commit

C1b1 MUST perform last-moment identity, reparse, share, containment, same-volume, and complete-child-set checks, make an explicit one-way child-observation transition, and then issue exactly one approved native relative rename using the retained admitted source and quarantine-parent capabilities. The target MUST be one validated simple leaf with replacement disabled. Every non-success `NTSTATUS` MUST fail closed. The checks and rename are not one atomic multi-object transaction: C1b1 MUST NOT claim atomic parent-ID or child-set validation with the rename. Pre-commit uncertainty MUST return `CLEANUP_REFUSED` with ownership and source-path state safely classified; success followed by uncertain observation MUST return `CLEANUP_PARTIAL` with quarantine retained and MUST NOT rename back. Win32 `FileRenameInfo`, absolute/path-based move or rename, shell, helper process, child deletion, copy-delete emulation, and target-changing retries are forbidden.

#### Scenario: Child-handle transition

- GIVEN the final gate validates the retained source, parent, and exact child set
- WHEN C1b1 crosses the commit boundary
- THEN child observation handles are disposed exactly once in an explicit one-way transition, while source and parent capabilities remain retained for the single native commit

#### Scenario: Successful identity-bound commit

- GIVEN the admitted source object has a different current name but its retained identity, parent capability, complete child set, and same-volume evidence remain valid
- WHEN the single relative native commit succeeds
- THEN the quarantine contains the admitted source identity under the retained parent and no source pathname was reopened

#### Scenario: Destination collision

- GIVEN a fresh simple target leaf is selected and another entry appears at that destination
- WHEN the single namespace commit is attempted
- THEN it fails without replacement, retry, or source mutation and returns `CLEANUP_REFUSED`

#### Scenario: Cross-volume refusal

- GIVEN the retained source and quarantine-parent capabilities are on different volumes
- WHEN C1b1 evaluates the commit
- THEN it returns `CLEANUP_REFUSED` before mutation and never emulates the operation with copy-delete

#### Scenario: Unsupported rename

- GIVEN the filesystem, filter, access/share state, or volume relationship cannot support the rename
- WHEN C1b1 evaluates or attempts the commit
- THEN it fails closed, preserves the source, and does not emulate the operation with copy-delete

#### Scenario: Invalid target leaf

- GIVEN the requested leaf is empty, rooted, a separator/device/stream form, reserved, or collides case-insensitively with a protected name
- WHEN C1b1 validates the target
- THEN it refuses before the native call and leaves ownership and source-path state unchanged

#### Scenario: Native failure classification

- GIVEN the final gate passed but the single native call returns a non-success `NTSTATUS`, including collision or unsupported completion
- WHEN C1b1 handles the result
- THEN it returns `CLEANUP_REFUSED`, performs no retry or fallback, and safely classifies the source as still owned at its original namespace

#### Scenario: Ownership and disposal

- GIVEN C1b1 has either refused before commit or committed successfully
- WHEN the operation releases resources
- THEN every temporary buffer and handle is disposed exactly once, with root and parent ownership transferred only after success

#### Scenario: Post-success observation failure

- GIVEN the identity-bound namespace commit returned success
- WHEN immediate observation of the retained source and parent is uncertain
- THEN the result is `CLEANUP_PARTIAL`, the quarantine is retained, and no rename-back is attempted

### Requirement: Safe status and observability taxonomy

Supervisor status and structured events MUST use only path/secret-free stable codes, including `INVALID_REQUEST`, `ROOT_CREATE_FAILED`, `JOB_CREATE_FAILED`, `JOB_CONFIG_FAILED`, `PROCESS_START_FAILED`, `JOB_ASSIGN_FAILED`, `PROCESS_RESUME_FAILED`, `TIMEOUT`, `CANCELLED`, `PROCESS_FAILED`, `OUTPUT_DRAIN_FAILED`, `QUIESCENCE_UNPROVED`, `CLEANUP_REFUSED`, `CLEANUP_PARTIAL`, and `INTERNAL_UNKNOWN`. Events MAY expose phase, coarse duration, active count, exit code, byte counts, truncation, and retry metadata, but MUST NOT expose paths, PIDs, commands, environment, nonce, handles, secrets, or exception text.

#### Scenario: Status emission

- GIVEN any success or fault transition
- WHEN status is serialized or logged
- THEN it contains only the closed response fields and permitted coarse observability values

### Requirement: C1b scope boundary

C1b MUST stop at the retained quarantine capability and its immediate observation result. It MUST NOT perform deletion, scavenging, rename-back, PowerShell integration, or C2/C3 behavior; those are separate future scopes.

#### Scenario: Commit-only boundary

- GIVEN C1b has committed a quarantine or returned a fail-closed refusal
- WHEN the C1b operation completes
- THEN it performs no deletion, scavenging, rename-back, PowerShell, C2, or C3 action

### Requirement: C1b immutable committed-child evidence production

C1b MUST produce and transfer a bounded, immutable `CommittedChildEvidence/v1` value with the retained quarantine capability. Until the final gate, the admission capability owns the live observation handles; after successful rename, the committed capability owns the evidence buffers, correlation key, and retained source/parent handles until C2 completes or transfers them to a retained partial-cleanup owner. The value MUST contain the admitted root and quarantine-parent `FILE_ID_INFO`, one 32-byte evidence digest covering the version, root/parent identities, count, and records, one 32-byte per-operation correlation key, and an ordinal-ignore-case collision-checked array of no more than 16 direct-child records. Each record MUST contain a 32-byte HMAC-SHA-256 leaf tag over the exact bounded UTF-16 leaf bytes, the child's volume serial and 128-bit file ID, object kind, and expected non-reparse state. Leaf components MUST be simple and no longer than 255 UTF-16 code units. Invalid counts, lengths, duplicate identities or tags, unknown kinds, overflow, or larger inputs MUST refuse before the native rename. Plaintext leaves, absolute paths, nonces, handle values, credentials, and command text MUST NOT be stored or emitted as evidence.

#### Scenario: Evidence is frozen before child-handle release and rename

- GIVEN the final C1b read-only gate has validated the retained source, quarantine parent, and complete direct-child allowlist while all admitted child handles remain live
- WHEN C1b captures handle-derived child identity, type, and reparse evidence
- THEN it computes and freezes `CommittedChildEvidence/v1` and its digest before releasing any child handle and before issuing the single native rename
- AND the successful rename transfers the frozen evidence, correlation key, and still-live source/parent handles exactly once into the committed capability
- AND no caller, path, or later enumeration can replace or amend the frozen evidence

#### Scenario: Evidence bounds or identity records are invalid

- GIVEN a child count, leaf length, record kind, identity, tag, digest input, or buffer size is missing, duplicated, unknown, overflowing, or outside the stated bounds
- WHEN C1b prepares the evidence
- THEN it returns `CLEANUP_REFUSED` before the native call, releases the uncommitted capability exactly once, and performs no namespace mutation

#### Scenario: Evidence capture and rename are not one transaction

- GIVEN evidence has been frozen while child handles are live
- WHEN C1b releases child handles and performs the single native rename
- THEN the specification treats the release, evidence snapshot, and rename as a residual non-atomic sequence
- AND the frozen evidence is correlation evidence rather than proof that the child set cannot change during that sequence
- AND any uncertainty before the rename remains `CLEANUP_REFUSED`, while any uncertainty after a successful rename is handled as `CLEANUP_PARTIAL` with the quarantine retained

### Requirement: b2c-C1c atomic capability-facet transfer prerequisite

The committed producer at `03ee654` MUST expose exactly one producer-side split operation that consumes exactly one `CommittedQuarantineCapability` exactly once and produces one indivisible paired handoff. The handoff MUST contain both facets together: a non-cloneable `RetainedTreeCapabilityFacet` owning only the exact transferred `DirectoryCapability` root and quarantine-parent handles and their already captured observations for C2a, and a non-cloneable immutable `CommittedEvidenceCapabilityFacet` owning only the exact `CommittedChildEvidence/v1`, its digest, correlation key, child-record sensitive buffers, and zeroization duty for C2b. C1c MUST provide no tree-only or evidence-only overload.

Neither facet, the paired handoff, or its shared binding MAY be constructed, duplicated, serialized, reconstructed, substituted, or mixed across handoffs from a path, final-path string, handle value, caller-created safe handle, observation, evidence record, digest, correlation key, nonce, token, or another facet. Each facet MUST be releasable at most once to its fixed consumer role and MUST NOT be reinterpreted as the other facet.

The original capability MUST expose exactly the states `Whole`, `Splitting`, `Split`, and `Disposed`. One synchronized linearization gate MUST select exactly one winner for split/dispose races. Only the `Whole` to `Splitting` winner may stage a pair; concurrent or repeated split/dispose losers MUST obtain no facet and MUST NOT touch staged or transferred resources. A losing dispose MUST wait for the winner's terminal publication or disposal decision as needed and then observe only the final state. Validation, immutable binding creation, facet allocation, and all potentially failing factory work MUST occur before ownership detaches. Publication MUST be one commit in which both facets become visible together and the original becomes `Split` and permanently inert. A pre-publication failure MUST dispose every staged resource and leave the original in `Whole` with both ownership bundles intact. If an unexpected failure occurs after ownership detaches but before paired publication, the unpublished facets and staged resources MUST be deterministically disposed and the original MUST become inert or `Disposed`; ownership MUST NOT be restored by duplicating a handle or sensitive buffer.

The split MUST create one immutable, non-exported, opaque `HandoffIdentity` reference held by both facets. Its representation MUST be authority-internal, non-loggable, non-serializable, and not caller-comparable by supplied bytes. C2a MUST preserve that exact identity when it consumes the tree facet into a `RetainedTreeSession`. C2b MUST accept the evidence facet only when an internal binding check proves that it and the C2a session hold the identical identity from the same C1c handoff. An absent, disposed, legacy, reconstructed, or cross-handoff pair MUST fail before enumeration, metadata work, authorization, or deletion.

After successful publication, the original capability MUST be inert and MUST NOT dispose transferred resources. The tree and evidence facets MUST have independent idempotent exact-once disposal. The tree facet MUST close only the retained root and quarantine-parent handles; the evidence facet MUST zero and dispose only evidence, identity, tag, digest, correlation-key, and other child-sensitive buffers. Disposal in either order MUST NOT affect the peer facet. The coordinator or retained-partial owner MUST preserve the evidence facet before transferring the tree facet to C2a; if C2a construction, runtime gating, session creation, cancellation, timeout, or execution fails, the evidence MUST remain owned and preserved for fail-closed retry or retention. An orphaned facet MUST fail closed and MUST NOT cause the surviving peer to be implicitly disposed or reconstructed.

C1c MUST exclude directory enumeration, relative reopen, observation refresh, deletion, exact-bijection proof, HMAC recomputation, DPAPI protection, cleanup or retry policy, native API additions, C2a or C2b mechanism or policy behavior, C3 discovery or scavenging, PowerShell, shell or subprocess execution, path fallback, and path-based substitution proof. It MUST NOT change the C1b rename or evidence contract.

C1c unit proof MUST cover every state transition, repeated and concurrent split/dispose races, injected failure before each allocation/factory/publication boundary, paired-only visibility, same-handoff binding, cross-handoff rejection, original inertness, independent disposal in both orders, exact handle-close and evidence-zeroization counts, and absence of leaked or double-owned resources. Runtime proof MAY use only a bounded fresh test-owned sandbox capability transferred through the real producer seam; it MUST prove ownership and lifetime mechanics only, not C2a native operations, C2b policy, cleanup authorization, or cleanup completion. C1c implementation MUST retain its 220–320 authored-line forecast, High risk, historical hard cap of 400 authored lines, and no exception; completed apply Attempt 62 remains historical at 347/400 with evidence `sha256:d7d8bd1cae69d55c820c2c06dd1c2e137fee47e8fd9a3a7930d806b778dddec8`. Independent C1c verification MUST be a distinct native objective with max attempts 1 and native changed-line ceiling 1000, MUST verify that exact Attempt 62 candidate/evidence, MUST make no functional code edits by default, and MUST write only verification evidence/report unless a separate correction decision authorizes otherwise. The verification objective requires fresh native reset/begin authorization and MUST NOT reset or reuse Attempt 62. Its 1000 ceiling is evidence/correction headroom only and MUST NOT expand behavior, allowed paths, dependencies, absorb C2a/C2b, merge units, authorize C3, or authorize stage/commit/push/PR/review. Rollback MUST occur in the order C2b → C2a → C1c → producer, and MUST remove or disable consumers before the split seam. Attempts 60 and 61 are failed historical evidence only and MUST NOT be used as completion, implementation, runtime, review, or verification authority. C3 remains excluded.

#### Scenario: Successful paired split

- GIVEN one valid `Whole` `CommittedQuarantineCapability` from committed producer `03ee654` owns the retained root/parent handles and immutable child evidence
- WHEN the single split operation wins its ownership gate and publishes
- THEN exactly one indivisible handoff becomes visible containing exactly one tree facet and exactly one evidence facet with the same internal binding, and the original enters `Split`

#### Scenario: Repeated or concurrent split

- GIVEN multiple split calls race or a split is requested again after a winner has published
- WHEN the calls compete for the capability's ownership gate
- THEN exactly one call may win, every other call obtains no facet, and no handle, evidence buffer, or binding is duplicated or exposed

#### Scenario: Split versus dispose

- GIVEN split and dispose are invoked concurrently on one `Whole` capability
- WHEN the synchronized linearization gate selects a winner
- THEN exactly one transition wins, the loser obtains no facet and does not touch staged resources, and the result is either a published pair with an inert original or a disposed original with no published facet

#### Scenario: Injected pre-publication failure

- GIVEN validation, binding creation, facet allocation, or factory work fails before paired publication
- WHEN the split operation handles the failure
- THEN all staged resources are disposed exactly once and the original remains `Whole` with both original ownership bundles intact

#### Scenario: Paired-only visibility

- GIVEN one facet allocation succeeds but the other facet or publication step has not completed
- WHEN any consumer observes the split result
- THEN neither facet is visible, and no tree-only or evidence-only handoff can be returned

#### Scenario: Original inertness

- GIVEN a paired split has published successfully
- WHEN the original wrapper is disposed, split again, or otherwise used as an owner
- THEN it performs no transferred-resource disposal and yields no capability, while the two published facets remain independently owned

#### Scenario: Disposal in both orders

- GIVEN both facets have been published and remain live
- WHEN the tree facet is disposed before the evidence facet, or the evidence facet is disposed before the tree facet
- THEN each facet disposes exactly once and only its own handles or sensitive buffers, and the peer remains valid until its own terminal disposal

#### Scenario: Same-handoff binding acceptance

- GIVEN C2a holds a session created from the tree facet and C2b holds the evidence facet from that same C1c handoff
- WHEN C2b checks their internal binding
- THEN the pair is accepted for the existing C2b gates, without exposing or comparing caller-supplied binding bytes

#### Scenario: Cross-handoff rejection

- GIVEN a C2a session and evidence facet originate from different C1c handoffs, or either binding is absent, legacy, disposed, or reconstructed
- WHEN C2b checks the pair
- THEN it rejects the pair before enumeration, metadata, authorization, or deletion and retains the quarantine without reconstructing authority

#### Scenario: Tree-facet orphan

- GIVEN the evidence facet is unavailable or terminally disposed while the tree facet remains live
- WHEN the tree facet is orphaned or its consumer cannot proceed
- THEN the system fails closed, closes only the tree facet's handles at its explicit disposal boundary, and never synthesizes evidence or performs cleanup

#### Scenario: Evidence-facet orphan

- GIVEN the tree facet or C2a session is unavailable while the evidence facet remains live
- WHEN the evidence facet is orphaned
- THEN the evidence remains preserved for retention or fail-closed retry until explicit terminal disposal, and no root is selected or cleanup is authorized

#### Scenario: C2a failure preserves evidence

- GIVEN the tree facet is transferred to C2a but construction, runtime gating, session creation, cancellation, timeout, or execution fails
- WHEN C2a reports the failure
- THEN the evidence facet remains owned by the coordinator or retained-partial owner with its buffers preserved, while no C2a or C2b cleanup authority is created

#### Scenario: Unsupported, legacy, or direct-producer input

- GIVEN C2a receives the combined producer capability directly, a pre-C1c legacy capability, a tree-only/evidence-only object, a reconstructed facet, or any path, handle value, caller safe handle, or other substituted input
- WHEN C2a attempts to create a session
- THEN it rejects the input before enumeration or mutation, remains blocked until C1c is independently verified and committed, and emits no sensitive value

#### Scenario: No sensitive values in diagnostics

- GIVEN any C1c success, refusal, race, failure, orphan, or disposal transition occurs
- WHEN status or structured diagnostics are emitted
- THEN they contain only stable non-sensitive phase/error information and MUST NOT contain paths, final-path strings, handle values, evidence, digests, keys, nonces, tokens, leaf names, credentials, buffers, exception text, or binding representation

### Requirement: Unit B2 signed Core relocation and bounded test seam

After the validated Unit S signing/key gate, Unit B2 MUST sign the relocated Core and grant exactly two public-key-qualified signed friendships from Core: the production `AIBar.Packaging.Supervisor` assembly and the existing `AIBar.Domain.Tests` assembly. The test friend MUST be non-production and limited to preserving existing C1c/C2a internal-seam coverage; it MUST never be published, shipped, packaged, or included in production SBOM, dependency, or runtime assets. This exception MUST NOT make Core internals public, add a public test hook, widen production APIs, or change any other B2 byte-identical relocation, signing, custody, authority-ownership, scope, or rollback constraint.

#### Scenario: Signed production and test friendships are admitted

- GIVEN Unit S has validated the checked public signing identity and B2 has the five materialized relocation files
- WHEN Core friendship metadata and the affected project/test boundaries are evaluated
- THEN exactly the signed Supervisor and signed `AIBar.Domain.Tests` identities are admitted, Supervisor retains its production internal seam, and the test assembly retains the existing C1c/C2a internal-seam coverage without a public API widening

#### Scenario: Unsigned, wrong-identity, or extra friend is refused

- GIVEN the proposed test friend is unsigned, uses a public identity different from the checked identity, or Core declares any additional friend
- WHEN B2 friendship activation is evaluated
- THEN activation is refused before the B2 candidate is accepted, and no public test hook, broad friendship, or substitute production API is created

#### Scenario: Test friend is not publishable

- GIVEN production publish, packaging, shipping, SBOM, dependency, and runtime-asset outputs are inspected
- WHEN the B2 exclusion boundary is checked
- THEN `AIBar.Domain.Tests` and its test-only dependencies are absent from every production artifact, while the signed test friendship remains usable only in the test scope

### Requirement: Strong physical C1 and C2 authority boundary

Production authority MUST be split across physical assemblies. `AIBar.Packaging.Supervisor.Core` MUST own C1 capabilities, committed evidence, atomic facets, and read-only enumeration/reopen/observation interop; it MUST own no issuer, token, session, sandbox, or delete primitive. `AIBar.Packaging.Supervisor.C2Authority` MUST reference Core and exclusively own C2a sessions/leases, token-verification state, the exactly-once issuer, its durable production owner, and private `NtSetInformationFile(FileDispositionInformation)` delete interop; future C2b policy and its guarded facade MUST live there. `AIBar.Packaging.Supervisor` MUST remain the host/orchestrator, reference Core and C2Authority, and MUST receive only the future high-level guarded facade, never a raw issuer, token, session, sandbox, or delete primitive.

No issuer, token, session, sandbox, verification state, or delete primitive MAY have a public constructor, public factory, public export, serialization form, or handle-based reconstruction route. The production reference graph MUST be exactly `C2Authority -> Core` and `Supervisor -> Core + C2Authority`; Core MUST NOT reference C2Authority, and no production project MAY reference testing support.

#### Scenario: Ordinary production reference cannot construct authority

- GIVEN any production source has only its declared compile-time project references
- WHEN it attempts to construct, obtain, or invoke an issuer, token, session, sandbox, or raw delete primitive
- THEN compilation or API-surface inspection proves no such public route exists, and the host can receive only the guarded facade

#### Scenario: Graph is acyclic

- GIVEN the project and solution references are inspected
- WHEN their topological order is evaluated as `Core -> C2Authority -> Supervisor` with test support after C2Authority and Domain.Tests after test support
- THEN no Core-to-Authority, production-to-Testing, Domain.Tests-to-C2Authority, or other reverse edge exists

### Requirement: Core read-only retained-capability operations

Core MUST accept the independently verified C1c handoff and own only C1 capability/evidence/facet state plus bounded read-only direct-child enumeration, relative reopen, and handle observation. `NtQueryDirectoryFile` MUST accept up to 16 distinct valid simple leaves, ignore at most one exact `.` and one exact `..` structural record without counting them, and reject duplicates, other dot-like names, separators, embedded NUL, malformed/truncated offsets or lengths, record-traversal faults, ambiguity, bounds, truncation, and uncertain nonterminal status. `NtCreateFile` reopen and observation MUST remain retained-root-relative, preserve `FileStandardInfo = 1`, and normalize successful enumeration and successful reopen plus `FileIdInfo`/`FileAttributeTagInfo`/`FileStandardInfo` observation to `RetainedTreeMechanismStatus.Success`. Core MUST contain no `FileDispositionInformation`, delete API, issuer, token, session, or sandbox.

#### Scenario: Valid read-only enumeration and observation

- GIVEN zero or one exact dot record of each kind and up to 16 valid simple leaves, including reordered `publish` and `restore`
- WHEN Core enumerates, reopens, and observes through retained handles
- THEN structural records are ignored, all valid leaves are returned without order authority, `FileStandardInfo = 1` is used, and successful statuses normalize to `Success`

#### Scenario: Core cannot delete

- GIVEN a Core consumer has a valid capability, evidence, facet, or observation result
- WHEN it seeks a raw or guarded deletion operation
- THEN no Core API or native import permits deletion and no reference to C2Authority exists

### Requirement: C2Authority owner lifetime and private delete containment

C2Authority MUST consume exactly one retained-tree facet into one session, preserve the C1c handoff identity, and transfer exactly one issuer to one durable production owner whose lifetime spans the session and future C2b guarded operation. C2Authority/session MUST retain only verification state and MUST NOT retain, reacquire, clone, reconstruct, or expose the issuer. The durable owner MUST neither release nor invoke the issuer except through the future C2b guarded facade after exact-bijection and durable-metadata gates pass.

Every delete MUST route through private `FileDispositionInformation` interop on a same-session lease and MUST require successful session/lease/token verification immediately before mutation. The token MUST be session-bound, one-use, non-cloneable, non-reconstructible, and invalidated by cancellation, timeout, disposal, lease mismatch, observation fault, wrong session, or reuse. No arbitrary-handle delete method, delete API, native delete import, or compatibility fallback MAY remain in Core or be callable from the host.

#### Scenario: Issuer and durable owner transfer exactly once

- GIVEN C2Authority creates one retained-tree session
- WHEN production ownership is established
- THEN one issuer transfers once to one durable production owner, repeated transfer fails closed, and neither the host nor the session can obtain the issuer or a token

#### Scenario: Raw arbitrary-handle deletion is unreachable

- GIVEN a caller has an arbitrary handle, Core observation, mismatched lease, missing/reused token, or wrong session
- WHEN it attempts deletion
- THEN the private route rejects before `NtSetInformationFile`, performs no mutation, and exposes no callable raw-delete surface

#### Scenario: Verified private deletion route

- GIVEN one live C2Authority session, matching lease, valid one-use token, supported Windows 10/11 x64 layout, and successful immediate re-observation
- WHEN the private delete route runs
- THEN it calls only private `NtSetInformationFile(FileDispositionInformation)` for that lease, consumes the token once, and maps every uncertain result to a stable path/secret-free mechanism status

### Requirement: Signed bounded test-support facade and production exclusion

`AIBar.Packaging.Supervisor.Authority.Testing` MUST be a non-production, checked-public-key-signed assembly with that exact simple name and the sole public-key-qualified signed friendship of C2Authority. It MUST own the concrete bounded sandbox and the only bounded bridge for migrated C1c/C2a internal-seam coverage; it MAY expose test outcomes and bounded verification operations only. It MUST NOT export an issuer, token, raw session, arbitrary-handle delete, sandbox constructor, or native delete route. `AIBar.Domain.Tests` MUST reference Authority.Testing, MUST lose its direct Core friendship, and MUST consume migrated or bridged C1c/C2a seams through Authority.Testing rather than compiling against Core internals.

When Unit C removes the temporary B2 Core friendships, Core MUST retain only the exact checked-public-key-qualified `AIBar.Packaging.Supervisor.C2Authority` and `AIBar.Packaging.Supervisor.Authority.Testing` friends required by the final boundary; Supervisor and Domain.Tests MUST no longer be Core friends. Authority.Testing's Core friendship MUST exist solely to preserve or migrate the existing C1c/C2a internal-seam coverage. This transition MUST NOT widen Core's public API, create a production authority edge, or authorize native deletion. Authority.Testing MUST be absent from every production project reference, publish or ship output, production SBOM/package component and asset set, `.deps.json`, runtime assets, and distribution metadata. Strong names establish assembly identity for narrow friendship; they MUST NOT be described as a hostile-code sandbox. Unit S's checked identity and custody policy remain prerequisites and are not redefined here. Completed B2 relocation and its evidence remain intact and MUST NOT be reopened, rewritten, or treated as Unit C authority.

#### Scenario: Bounded test support without authority export

- GIVEN Domain.Tests invokes Authority.Testing for a fresh test-owned sandbox within entry/depth/volume/deadline bounds
- WHEN the facade exercises bounded authority-verification behavior
- THEN the sandbox remains owned by Authority.Testing, no issuer/token/session/raw handle or native-delete capability is exposed, and the result grants no production or C2b evidence

#### Scenario: Exact final signed friend identity

- GIVEN Unit S has validated the checked full public key and Unit C has signed Core, C2Authority, Authority.Testing, and Domain.Tests
- WHEN final Core and C2Authority friendship metadata is evaluated
- THEN Core admits exactly the signed C2Authority and exact signed Authority.Testing identities, each with its required simple name and Unit S's checked full public key, C2Authority admits exactly signed Authority.Testing, and Supervisor and Domain.Tests are absent from Core friendship

#### Scenario: Existing internal seams survive through the test route

- GIVEN the existing C1c/C2a seam tests require internal Core behavior
- WHEN those tests are migrated or bridged through Authority.Testing
- THEN the focused seam coverage remains executable, Domain.Tests has no direct Core-internal access or friendship, and no Core public API or production reference is widened

#### Scenario: Wrong, unsigned, or extra friend is refused

- GIVEN Authority.Testing or any proposed Core/C2Authority friend is unsigned, token-only, unqualified, signed with the wrong public key, duplicated, renamed, or additional
- WHEN Unit C activates the final friendship boundary
- THEN activation fails closed before acceptance, no B2 temporary friend is retained as a fallback, and no public hook, broad friendship, authority edge, or native delete route is added

#### Scenario: Friend and publish boundary

- GIVEN assembly metadata, project references, publish output, production SBOM/package metadata, `.deps.json`, and runtime assets are inspected
- WHEN the authority boundary contract tests run
- THEN only the exact checked-public-key-qualified Authority.Testing identity is a C2Authority friend, Domain.Tests is not a Core friend or direct Core-internal consumer, and Authority.Testing, Domain.Tests, and test-only dependencies appear in none of those production artifacts

#### Scenario: Public surface and native boundary remain closed

- GIVEN a Unit C candidate exposes a new Core public type/hook, adds a production reference to Authority.Testing, or activates native delete
- WHEN public API, reference-graph, and native-import checks run
- THEN the candidate is refused before acceptance, Core's public surface remains unchanged, no production authority edge exists, and no native deletion is authorized

### Requirement: C2b exact producer-evidence and C2a-session consumption

C2b MUST consume exactly one independently committed `CommittedEvidenceCapabilityFacet` containing `CommittedChildEvidence/v1` and exactly one C2a session created from the matching `RetainedTreeCapabilityFacet` of the same C1c handoff. It MUST first prove the shared immutable authority-internal C1c binding by reference/identity, not by caller-supplied bytes; it MUST reject direct producer evidence, replacement, reconstruction, path selection, fresh-root selection, mixed producer versions, cross-handoff facets, and any evidence/session pair whose root or correlation binding differs. Before the first deletion, while the complete direct-child lease set remains live, C2b MUST verify the evidence version and digest, root and parent identity, and an exact bijection of count, HMAC tag, volume serial, 128-bit file ID, object kind, expected non-reparse state, and evidence digest. Enumeration order, leaf spelling, a tag-only match, or a fresh path-based enumeration MUST never authorize deletion.

#### Scenario: Exact bijection before first deletion

- GIVEN one C2a session returns one bounded direct-child lease set and one matching `CommittedChildEvidence/v1`
- WHEN C2b derives each tag in memory and compares both sets
- THEN every observed lease matches exactly one frozen record, every frozen record is consumed exactly once, all count/tag/identity/kind/volume/non-reparse/digest checks pass, leases remain live, and no deletion begins until the metadata gate also passes

#### Scenario: Every pre-first-deletion uncertainty fails closed

- GIVEN a missing, extra, renamed, substituted, duplicate, reparse, cross-volume, inaccessible, unknown-kind, tag-mismatched, identity-mismatched, digest-mismatched, cancelled, timed-out, faulted, or otherwise unknown enumeration/reopen/observation result occurs
- WHEN C2b evaluates the correlation gate
- THEN it performs zero deletions, retains the quarantine, reports truthful `CLEANUP_PARTIAL`, and never repairs the mismatch from a path, fresh enumeration, or synthesized evidence

### Requirement: C2b DPAPI retry metadata binding and zeroization

Before the first deletion, C2b MUST serialize only bounded retry metadata containing the evidence schema version, evidence digest, correlation binding, retained-root identity, phase, retry count, and next-eligible time. It MUST protect the record with DPAPI `CurrentUser`, immediately unprotect it, and verify the binding in constant time before durably recording only the protected bytes. Plaintext serialization, DPAPI plaintext, correlation key, leaf tags, digests, and temporary lease/enumeration records MUST be bounded, zeroized, and disposed exactly once. Missing, legacy, corrupt, wrong-version, wrong-user, unprotectable, wrong-correlation, wrong-root, unbound, non-durable, or retry-exhausted metadata MUST fail closed with zero deletion. Protected metadata MUST never discover or select a root.

#### Scenario: Valid current-user metadata

- GIVEN the evidence version, digest, correlation binding, retained-root identity, phase, retry count, and next-eligible time are current and within bounds
- WHEN C2b protects, immediately unprotects, and constant-time verifies the record for the current user
- THEN only the protected bytes are retained, the metadata gate succeeds, and no path, secret, nonce, handle value, or plaintext record is exposed

#### Scenario: Missing, legacy, corrupt, unbound, or exhausted metadata

- GIVEN metadata is missing, legacy, corrupt, bound to another user/evidence/key/root, not durably recorded, unverifiable, or retry-exhausted
- WHEN C2b evaluates the retry gate
- THEN it performs zero deletions, retains the quarantine, reports `CLEANUP_PARTIAL`, and does not synthesize or select replacement authority

#### Scenario: Crash in the metadata window

- GIVEN the process crashes before protected metadata is durably recorded, after recording it but before deletion, or after deletion has started
- WHEN the retained capability is later disposed or observed
- THEN missing metadata is non-actionable, durable verified metadata remains bound only to the same live capability, and every uncertain post-commit state retains the quarantine as `CLEANUP_PARTIAL` without a success claim

### Requirement: C2b one-use authorization and guarded post-order cleanup

Only after both the exact-bijection and DPAPI metadata gates pass may future C2b invoke the durable production owner's guarded facade exactly once to obtain and consume one session-bound authorization inside C2Authority. C2b MUST use only that facade for bounded post-order, handle-anchored deletion; it MUST immediately re-observe every live direct-child and descendant lease before each mutation and MUST stop at the next bounded cancellation/deadline point. The facade MUST NOT return the issuer, token, session, lease handle, sandbox, or raw delete operation. Any reparse, identity, kind, volume, extra-descendant, reopen, enumeration, deletion, metadata, disposal, or unknown fault after the producer commit MUST retain the quarantine and report `CLEANUP_PARTIAL`, whether or not the first deletion has started.

#### Scenario: One-use authorization and immediate revalidation

- GIVEN the two gates passed and the complete direct-child leases remain live
- WHEN C2b invokes the guarded facade and begins post-order cleanup
- THEN the token remains authority-internal, is accepted only by its originating session and consumed once, and each live lease is re-observed immediately before its private delete operation

#### Scenario: Fault before the first deletion

- GIVEN a post-commit metadata, lease, revalidation, descendant enumeration, reopen, identity, reparse, cancellation, timeout, or authorization fault occurs before the first delete
- WHEN C2b reaches the fault boundary
- THEN it performs zero deletions, transfers or retains ownership safely, retains the quarantine, and reports `CLEANUP_PARTIAL`

#### Scenario: Fault after deletion starts

- GIVEN one or more bounded post-order deletions have completed and a later deletion, revalidation, disposal, cancellation, timeout, or unknown operation fails
- WHEN C2b handles the fault
- THEN it stops further mutation at the next bounded point, retains the remaining quarantine, reports `CLEANUP_PARTIAL`, and never renames back or retries from an arbitrary root

#### Scenario: Idempotent disposal and no leakage

- GIVEN C2b completes, fails, or is disposed more than once
- WHEN ownership and sensitive buffers are released
- THEN disposal is idempotent, handles close exactly once, plaintext and transient records are zeroed, and status/diagnostics contain no path, leaf, secret, credential, nonce, raw native status, exception text, or handle value

### Requirement: C2 dependency, rollback, threat model, and excluded capabilities

The assembly DAG MUST be `Core -> C2Authority -> Supervisor` for production and `C2Authority -> Authority.Testing -> Domain.Tests` for test access, where arrows denote referenced-before-dependent topological order; the actual project-reference edges are `C2Authority -> Core`, `Supervisor -> Core + C2Authority`, `Authority.Testing -> C2Authority`, and `Domain.Tests -> Authority.Testing`. Unit C MUST remove both temporary B2 Core friendships and establish only the exact final signed Core/C2Authority friend identities defined above; no Domain.Tests direct Core-internal route may remain. Source migration MUST follow read-only, non-authority Core extraction, the validated signing/key gate, Authority/Testing introduction, seam migration/bridging, and friendship transition. Native deletion remains outside Unit C and requires a later separately authorized unit. Rollback MUST reverse that order without leaving a production-to-Testing edge, a broad friend, or a partial friendship transition. No consumer may remain active without its predecessor.

This boundary MUST defend against ordinary compile-time production references and accidental authority exposure. Privileged same-process reflection, runtime patching, debugger access, and `unsafe` memory attacks are explicitly out of scope; process isolation would be required for that hostile-code model. Strong names MUST be treated as assembly identity for public-key-qualified friendship, not as a security sandbox. Historical Attempt 84 MUST remain failed and insufficient because same-assembly internals and raw arbitrary-handle deletion did not establish this physical boundary; its history MUST NOT be rewritten or used as completion evidence. C2a, future C2b, and C3 remain unchecked until their independent gates pass.

#### Scenario: Forward and rollback ordering

- GIVEN Core, C2Authority, Authority.Testing, the Supervisor host, Domain.Tests, and future C2b are being advanced or rolled back
- WHEN a unit boundary is crossed
- THEN references follow the acyclic graph, and migrated/bridged seam verification, complete final signed Core/C2Authority friend-set installation, and removal of both temporary B2 Core friends occur atomically in the same accepted Unit C transition; no intermediate graph is accepted, rollback is exactly reversed, no production-to-Testing or Core-to-Authority project edge appears, and native deletion remains deferred

#### Scenario: Mixed-version or historical evidence

- GIVEN C2b receives a legacy capability, mismatched authority input, or historical evidence from Attempts 60, 61, or 84
- WHEN it evaluates the input
- THEN it rejects it without deletion, evidence synthesis, or completion claim, and every failed candidate remains historical only

#### Scenario: C3 and discovery are excluded

- GIVEN a retry process, scavenger, PowerShell caller, metadata reference, or future component has no directly handed-off live capability
- WHEN it attempts to discover, select, schedule, or clean a retained root
- THEN it performs no cleanup under this contract and reports only a stable non-success state; C3 requires a separately approved specification
