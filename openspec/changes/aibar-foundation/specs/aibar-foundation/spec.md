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

The supervisor MUST create the isolation root, retain a cryptographic capability, hold an exclusive capability handle, and validate physical identity, final path, canonical containment, and reparse state on every admission boundary. Unknown, substituted, extra, missing, or raced identity MUST fail closed. Malicious same-user or administrator interference is outside the threat-model guarantee.

#### Scenario: Reparse or identity race
- GIVEN a root or child changes identity or becomes a reparse point between validation steps
- WHEN admission or revalidation runs
- THEN it returns `ROOT_IDENTITY_CHANGED` or `REPARSE_DETECTED` and preserves bytes before commit

### Requirement: Irreversible cleanup state machine

Cleanup MUST perform read-only admission, then an atomic same-volume quarantine rename as the sole irreversible commit. Before commit, any failure MUST preserve the original namespace and return `CLEANUP_REFUSED`. After commit, every reopen, identity, reparse, enumeration, rename, deletion, or unknown failure MUST retain quarantine and return `CLEANUP_PARTIAL`; it MUST never claim pristine preservation or rollback. Protected metadata MUST contain only retained identity, phase, bounded retry count, and next eligibility.

#### Scenario: Rename or post-quarantine deletion failure
- GIVEN quarantine rename fails, or deletion fails after quarantine commits
- WHEN cleanup reports the result
- THEN rename failure preserves the original tree as `CLEANUP_REFUSED`; post-commit failure retains quarantine as `CLEANUP_PARTIAL` without claiming original-path preservation

### Requirement: Safe status and observability taxonomy

Supervisor status and structured events MUST use only path/secret-free stable codes, including `INVALID_REQUEST`, `ROOT_CREATE_FAILED`, `JOB_CREATE_FAILED`, `JOB_CONFIG_FAILED`, `PROCESS_START_FAILED`, `JOB_ASSIGN_FAILED`, `PROCESS_RESUME_FAILED`, `TIMEOUT`, `CANCELLED`, `PROCESS_FAILED`, `OUTPUT_DRAIN_FAILED`, `QUIESCENCE_UNPROVED`, `CLEANUP_REFUSED`, `CLEANUP_PARTIAL`, and `INTERNAL_UNKNOWN`. Events MAY expose phase, coarse duration, active count, exit code, byte counts, truncation, and retry metadata, but MUST NOT expose paths, PIDs, commands, environment, nonce, handles, secrets, or exception text.

#### Scenario: Status emission
- GIVEN any success or fault transition
- WHEN status is serialized or logged
- THEN it contains only the closed response fields and permitted coarse observability values

### Requirement: Conservative recovery and degradation

The supervisor MAY run an opt-in scavenger only for aged supervisor-owned quarantines after current-user ownership/ACL, protected capability metadata, physical identity, reparse, and lock admission. Any refusal MUST retain quarantine and perform no deletion. On rollback or unsupported capability, it MUST preserve bytes and disable cleanup rather than guess.

#### Scenario: Scavenger refusal
- GIVEN scavenger metadata is missing/invalid, identity is uncertain, or an extra entry exists
- WHEN scavenger admission runs
- THEN it refuses mutation, retains quarantine, records bounded retry metadata, and returns `CLEANUP_PARTIAL`
