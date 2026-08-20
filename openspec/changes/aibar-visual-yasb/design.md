# Design: Windows-native AIBar popup and bounded YASB bridge

## Decision summary

AIBar remains the only quota authority. The first product slice is delivered through five independently reviewable units, with contract, authority, Windows publication, and clear coordination separated so passing one boundary cannot hide an incomplete adjacent contract:

1. **Unit 1a-contract — closed projection and fail-closed wire contract** owns the Application export records, enums, projector, serializer/normalizer boundary, exact allowlists and wire domains, nullable reset semantics, and the complete projection/contract acceptance matrices.
2. **Unit 1a-authority — serialized causal authority stream** depends on 1a-contract and owns `QuotaAuthorityUpdate` plus `QuotaRefreshCoordinator` transition serialization, sequence allocation, ordered cross-event delivery, and retrieval-generation causality.
3. **Unit 1a-privacy — generation-gated publisher and consent lifecycle** depends on 1a-authority and owns publisher ordering, freshness propagation, disclosure epochs, cancellation, disable/revoke/clear, and persisted-consent gating.
4. **Unit 1b-writer — Windows canonical-path atomic writer** depends on 1a-privacy and owns minimum-rights protected ACLs and readback, reparse defenses, same-directory secure temporary files, durable validated UTF-8 writes, atomic move/replace, complete old/new reader views, and exact cleanup.
5. **Unit 1b-clear — clear transaction and Desktop composition** depends on the committed 1b-writer child and owns exact snapshot-path clearing, in-flight/queued coordination and stale suppression, disabled/null recreation or safe absence, writer failure isolation, composition, and explicit disposal order.

The 359-authored-line five-path source/test candidate is historical evidence only: at split-planning time it was recorded as uncommitted and unstaged, static acceptance found missing minimum-rights ACL behavior, reader atomicity proof, and clear coordination/lifecycle coverage, and no test was run. The maintainer selected `Dividir writer/clear`, and at that planning point no implementation task received completion credit. Separate writer (`3d137171a9c6c3a8fd4d079e8d9de6bf819a0043`) and clear (`bc8d79f719989a21f72b5c6cf843117c5001228d`) commits now exist, but no immutable stash object ID, `refs/stash`, named stash/ref, or stash reflog survives; stash/selective-restoration provenance is unavailable and MUST NOT be claimed.

## Architecture and dependency direction

```text
AIBar.Domain
    ^
AIBar.Application
    |
    | Unit 1a-contract
    |   QuotaExport records/enums/input/projector
    |   validating serializer/normalizer
    |   writer/privacy-facing ports
    |   no authority stream or coordinator
    |
    | Unit 1a-authority (depends on contract)
    |   QuotaAuthorityUpdate
    |   QuotaRefreshCoordinator
    |   one serialized transition queue/drain
    |   monotonic sequence + ordered AuthorityUpdated/StateChanged delivery
    |   retrieval generation after accepted + persisted real retrieval
    |
    | Unit 1a-privacy (depends on authority)
    |   QuotaExportPublisher
    |   generation unlock + event-fresh ordering
    |   disclosure epoch cancellation + consent lifecycle
    v
AIBar.Desktop
    |
    | Unit 1b-writer (depends on privacy)
    |   canonical path + ACL-safe atomic file adapter
    |
    | Unit 1b-clear (depends on committed writer)
    |   exact clear ownership + stale suppression + composition/disposal
    v
%LOCALAPPDATA%\AIBar\yasb-quota.json
    ^ read only; never a refresh or command channel
stock YASB assets -> AIBar.Desktop.exe --show
```

Neither Domain nor Application references WPF, Windows theme APIs, JSON filesystem details, ACL APIs, or YASB types. Contract contains no coordinator, authority event, publisher, consent, privacy epoch, Desktop, filesystem, or ACL behavior. Authority contains no publisher, consent/privacy lifecycle, Desktop, filesystem, or ACL behavior. Privacy contains no Desktop, filesystem, or ACL implementation. Unit 1b-writer is the first unit allowed to make external bytes visible; Unit 1b-clear then integrates that committed boundary into clear and Desktop lifecycle ownership.

`QuotaExport.cs` may appear in Unit 1a-contract and later Unit 1a-privacy diffs only by symbol ownership: contract owns records/enums/input/projector/normalizer/ports, while privacy may add publisher/lifecycle symbols. Unit 1a-authority does not add its symbols to the contract-owned region: `QuotaRefreshCoordinator` and `QuotaAuthorityUpdate` belong to authority and may use `QuotaRefreshCoordinator.cs` plus a repository-conventional authority-specific file.

## Architecture decisions

| Area | Decision | Rationale and tradeoff |
|---|---|---|
| External contract | Unit 1a-contract owns a closed schema-v1 `QuotaExportDocument` and an explicit validating wire boundary | A direct caller cannot bypass projection invariants and serialize an inconsistent public record. |
| Reset semantics | `QuotaExportWindow.ResetAt` is nullable and serializes as `null` when the source omits reset information | The approved specification requires available percentages even when reset is absent; absence is not contract failure. |
| Projection | `QuotaExportProjector.Project(input, now)` maps authoritative state through exact state, warning, timestamp, percentage, partial-window, credential, and forbidden-field rules | Projection remains pure, synthetic, and independent of coordinator sequencing. |
| Direct-document serialization | Every public serialization entry validates or normalizes the whole candidate first; invalid schema, unknown enum values, inconsistent state/warning/window/source combinations, invalid percentages, or invalid timestamps become the canonical unavailable/null document instead of throwing or emitting arbitrary bytes | Public records are not trusted merely because they have the right CLR type. Unknown state/warning values fail closed. |
| Authority stream | Unit 1a-authority owns a serialized transition queue/drain that mutates authoritative state, assigns sequence/generation, and delivers the paired authority/legacy callbacks in accepted order | A lock around only sequence allocation is insufficient because concurrent callbacks can still be delivered out of order. |
| Legacy event compatibility | Each accepted transition produces one `QuotaAuthorityUpdate`, then one matching `StateChanged(update.State)` in the same non-interleaved drain turn; existing consumers keep one legacy callback per transition | Cross-event tests prove state identity/content compatibility and prevent a later transition from interleaving between the paired events. |
| Retrieval generation | Retrieval generation increments exactly once only after a valid non-null real retrieval is persisted and accepted for the current operation generation | Equal content still proves a causally new retrieval; cache load, loading, failure, replay, cancellation, clear, and persistence failure do not. |
| Publication | Unit 1a-privacy subscribes to every authority update and serializes publication through one latest-state gate | Retrieval generation is unlock evidence only; event sequence and state control freshness and final ordering. |
| Filesystem | Unit 1b-writer makes `WindowsAtomicQuotaExportWriter` secure and read back minimum-rights protected ACLs for the directory, existing destination, same-directory temporary file, and result before reporting success; it durably writes validated UTF-8 and uses only atomic move/replace | Concurrent readers see a complete old or new document, and ACL, reparse, durability, or cleanup failure cannot authorize newly visible valued bytes. |
| Activation | Parse exactly zero arguments or the one-token `--show`; preserve the fixed local single-instance event | No arbitrary payload or general IPC is introduced. |
| Theme/popup/YASB | Preserve the existing WPF host, add semantic Windows resources and quota-first layout, and use stock YASB assets over the sanitized file | The integration stays display-only and avoids a second quota authority. |

## 1. Unit 1a-contract: closed projection and fail-closed wire contract

### Owned symbols

The contract unit owns these concepts in `AIBar.Application`:

```csharp
public enum QuotaExportState { Current, Refreshing, Stale, Unavailable, Disabled }
public enum QuotaExportWarning { RefreshFailed, AuthenticationFailed, Unavailable, Disabled }
public sealed record QuotaExportWindow(decimal PercentageUsed, DateTimeOffset? ResetAt);
public sealed record QuotaExportDocument(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    QuotaExportState State,
    QuotaExportWarning? Warning,
    DateTimeOffset? SourceRetrievedAt,
    QuotaExportWindow? FiveHour,
    QuotaExportWindow? Weekly);
```

It also owns `QuotaExportInput`, `QuotaExportProjector`, the validating serializer/normalizer, `IQuotaExportWriter`, and the privacy-facing Application port. It does not own `QuotaAuthorityUpdate`, `QuotaRefreshCoordinator`, publisher state, consent state, or platform adapters.

### Exact schema-v1 contract

The top-level allowlist, in wire order, is exactly:

```text
schemaVersion, generatedAt, state, warning, sourceRetrievedAt, fiveHour, weekly
```

Each permanent window is present and is either `null` or an object with exactly:

```text
percentageUsed, resetAt
```

`schemaVersion` is exactly numeric `1`. State values are exactly `current`, `refreshing`, `stale`, `unavailable`, and `disabled`. Warning values are exactly `null`, `refresh-failed`, `authentication-failed`, `unavailable`, and `disabled`. `resetAt` may be `null`; omission by the source is represented as a JSON null and does not invalidate an otherwise valid window.

Forbidden fields include any token, credential, account, plan, endpoint, path, response, exception, safe code, diagnostic, session, analytics, prompt, log, environment value, root, database/db field, or source-model property. Projection constructs a new closed model and never reflects or serializes the source object graph.

### Fail-closed serializer boundary

The serializer boundary treats every direct `QuotaExportDocument` as untrusted. Before writing bytes it validates the complete document against these invariants:

- schema version equals `1`;
- state and warning are known enum members and form an allowed pair;
- disabled means warning `disabled` and null source/windows;
- unavailable contains no valued windows/source;
- current/stale valued output has at least one valid window and an exact valid source timestamp;
- refreshing may retain a valid partial cache with its source timestamp or have null source/windows;
- `sourceRetrievedAt` is not after the trusted publication time;
- percentages are finite representable decimals in `[0,100]`;
- each window has only a percentage plus nullable reset; a valid past, equal, or future reset is normalized to UTC;
- no inconsistent direct document can preserve only its convenient fields.

A null document, unsupported/unknown state or warning, schema mismatch, invalid percentage/timestamp, or inconsistent direct document produces the whole canonical fallback and does not throw:

```json
{"schemaVersion":1,"generatedAt":"<trusted-now-utc>","state":"unavailable","warning":"unavailable","sourceRetrievedAt":null,"fiveHour":null,"weekly":null}
```

The public API must either accept trusted `now` for this fallback or serialize an already validated opaque result; it must not use a caller-controlled inconsistent document to decide arbitrary wire values. Projector output and direct-document serialization pass through the same normalizer so their behavior cannot drift.

### Projection rules

| Authoritative condition | Export state | Warning | Values/source time |
|---|---|---|---|
| Disclosure disabled/revoked/cleared | `disabled` | `disabled` | both windows and source null |
| Loading with validated cache | `refreshing` | `null` | retain independently available windows and exact original `RetrievedAt` |
| Loading without cache | `refreshing` | `null` | both windows and source null |
| Current valid snapshot, no failure | `current` | `null` | retain five-hour, weekly, or weekly-only/five-hour-only values and exact source time |
| Operational failure with safe cache | `stale` | `refresh-failed` | retain independent windows and exact source time |
| Authentication, permission, missing credential, or unusable credential with cache | `stale` | `authentication-failed` | retain independent windows and exact source time |
| Explicit unavailable/unknown failure with safe cache | `stale` | `unavailable` | retain independent windows and exact source time |
| Operational failure without cache | `unavailable` | `unavailable` | both windows/source null |
| Authentication, permission, missing credential, or unusable credential without cache | `unavailable` | `authentication-failed` | both windows/source null |
| No failure and no cache | `unavailable` | `null` | both windows/source null |

Missing and unusable credential signals must have an explicit bounded mapping to `authentication-failed`; tests must prove the repository signal used for each case. A free-form credential message or safe code is not evidence and never enters output.

### Mandatory contract matrix

The contract unit is not complete without all of these behavior cases:

- numeric `schemaVersion == 1` and exact top-level/window property order and allowlists;
- every state and warning wire value, plus unknown enum members that fail closed rather than throw;
- forbidden root, db/database, source-model, token, path, credential, and diagnostic fields absent from output;
- both windows, five-hour-only, weekly-only, and no-window projections;
- percentages exactly `0`, exactly `100`, and at least one interior decimal, plus below/above range failures;
- exact retained `sourceRetrievedAt` across loading and stale republish while `generatedAt` advances independently;
- reset before/equal/after now, nullable reset, and source reset omission represented as null;
- current, refreshing with/without cache, stale, unavailable, disabled, partial-window, and optional-failure states;
- operational, redirect, malformed, authentication, permission, missing/unusable credential, unavailable, null, and unknown failure families with and without cache;
- null document and inconsistent direct documents, including schema mismatch, disabled-with-values, unavailable-with-source, valued-window-without-source, and unsupported state/warning;
- UTC normalization without reinterpreting wall-clock text.

## 2. Unit 1a-authority: serialized causal authority stream

### Owned contracts and coordinator

Authority owns:

```csharp
public sealed record QuotaAuthorityUpdate(
    QuotaRefreshState State,
    long EventSequence,
    long RetrievalGeneration);
```

It also owns every `QuotaRefreshCoordinator` change needed to generate and deliver that stream. `QuotaAuthorityUpdate` may live beside the coordinator or in an authority-specific file; it is not part of the contract-owned section of `QuotaExport.cs`.

### Serialized transition protocol

Every authoritative transition uses one coordinator-owned serialized queue/drain:

1. evaluate and accept the transition against disposal and operation generation;
2. mutate `State` inside the authority boundary;
3. allocate the next event sequence in the same boundary;
4. increment retrieval generation only for an accepted and persisted real retrieval;
5. enqueue one immutable `QuotaAuthorityUpdate`;
6. deliver queued updates in sequence order through one drain, without holding the state lock during subscriber code;
7. for each update, invoke `AuthorityUpdated(update)` and the compatible `StateChanged(update.State)` as one contiguous pair before the next update.

This protocol covers initialization success/failure, loading start, accepted retrieval, null result, provider failure, persistence failure, optional failure, freshness-only reevaluation, clear-to-unavailable, replay/coalesced refresh behavior, cancellation, and disposal rejection. Sequence allocation without ordered delivery is not sufficient.

### Retrieval-generation rules

Retrieval generation starts at zero and increments exactly once after all of these are true:

1. the provider returned a valid non-null safe snapshot;
2. persistence completed successfully;
3. the result still belongs to the accepted operation generation;
4. the accepted state is becoming authoritative.

Cache load, loading, freshness-only reevaluation, failed/null retrieval, persistence failure, cancellation, replay, clear, and optional failure do not increment it. Equal values, timestamps, object references, or record equality still increment after a genuinely new accepted/persisted retrieval.

### Mandatory authority/concurrency matrix

- monotonic contiguous sequences for initialization, loading, success, null/failure, optional failure, freshness-only reevaluation, and clear;
- real concurrent reevaluate/clear/refresh operations, not only sequential calls;
- retrieval completion racing clear and cancellation, proving cleared/cancelled values cannot become authoritative;
- two concurrent reevaluations and refresh completion racing reevaluation;
- same-reference and equal-content transitions preserving distinct accepted sequences;
- persistence failure retaining generation and the prior safe authoritative state;
- cancellation and replay retaining generation;
- ordered delivery despite a blocked or reentrant subscriber;
- no sequence inversion or duplicate allocation under concurrent callbacks;
- exact cross-event compatibility: one `AuthorityUpdated` and one matching `StateChanged` per accepted transition, contiguous pairing, same state reference/content, and no later state delivered between the pair;
- disposal prevents new transitions and drains only already accepted work according to the lifecycle contract.

## 3. Unit 1a-privacy: publisher and consent lifecycle

`QuotaExportPublisher` subscribes to every authority update. It owns one serialized publication drain, a monotonic disclosure epoch, an exclusive `minimumRetrievalGeneration` baseline, the latest queued authority update, and an epoch-owned cancellation source.

Each item carries `(state, eventSequence, retrievalGeneration, disclosureEpoch, enabled)`:

- retrieval generation answers only whether valued disclosure may unlock after enable/restart;
- event sequence and state answer which projection is newest and what freshness it carries;
- disclosure epoch answers whether work still has privacy authority.

Intermediate ordinary updates may be coalesced, but the final state cannot be dropped. Same-reference updates are distinct when sequence differs. The publisher has no timer and never calls refresh or reevaluation.

Disable, revoke, clear, enable, and disposal advance the disclosure epoch and cancel prior ordinary work. Older queued or completing writes cannot supersede the new privacy decision. Persisted consent starts pending, captures the current generation as an exclusive baseline, and remains null until a strictly newer accepted/persisted retrieval. Once unlocked, later event-sequenced freshness changes may republish without another retrieval increment.

Mandatory privacy cases remain restart/cache suppression, equal-content retrieval unlock, latest-state and same-reference bursts, blocked ordinary-write cancellation, cancellation-resistant writers, disable/revoke/clear/re-enable races, privacy-operation failure, routine writer failure isolation, and disposal/unsubscription.

## 4. Unit 1b-writer: Windows canonical-path writer

`WindowsQuotaExportPath.ForCurrentUser()` resolves exactly `%LOCALAPPDATA%\AIBar\yasb-quota.json`. The writer:

1. rejects reparse points at the directory, destination, temporary file, and every race-reopened path;
2. applies and reads back protected, minimum-rights ACLs for only the current user and SYSTEM, with no inherited, broad, deny, or `FullControl` shortcut;
3. creates a same-directory GUID temporary file with the compliant descriptor at creation;
4. serializes only a validated contract document, writes exact BOM-less UTF-8 bytes, flushes durably, closes, and revalidates bytes and descriptor;
5. atomically replaces/renames with no cross-directory or copy-over fallback so concurrent readers observe only a complete old or complete new document;
6. verifies the resulting descriptor and bytes before success;
7. deletes only its exact temporary file on every pre-commit failure and never removes an unrelated path.

At split-planning time, the historical writer portion was recorded as 308 authored lines. Correcting the permissive `FullControl` contract, its matching tests, and the missing reader-atomicity proof yielded a realistic **335–360 authored source/test line** forecast. Stop at **360**; do not omit security cases, cross 400, or use a size exception. Expected paths are primarily `src/AIBar.Desktop/QuotaExportWindows.cs` and `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`, plus only unavoidable project metadata.

Rollback first secures and removes the snapshot or atomically publishes a disabled/null snapshot, then removes the writer while leaving the complete 1a-privacy Application boundary intact.

## 5. Unit 1b-clear: clear transaction and Desktop composition

Unit 1b-clear starts only from a committed 1b-writer child. It adds the exact snapshot path to clear-data ownership, coordinates queued and in-flight publisher work before path mutation, suppresses stale completion, and finishes with a disabled/null recreation or proven safe absence. Desktop composition keeps writer failure isolated from authoritative cache and UI state and disposes publisher/writer-facing resources before the dependencies they can call.

At split-planning time, the historical clear/composition portion was recorded as 51 authored lines and covered only path inclusion and a narrow happy path. The required coordination, stale-suppression, failure, and disposal matrices yielded a realistic **90–140 authored source/test line** forecast with a **160-line hard stop**. Expected paths are `src/AIBar.Application/ClearAiBarDataService.cs`, `src/AIBar.Desktop/App.xaml.cs`, and `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`.

Rollback removes 1b-clear composition and exact-path ownership while retaining the committed writer and every 1a Application unit; before rollback completes, the snapshot must be disabled/null or safely absent. No real profile path is used in either child’s tests.

## Remaining product architecture

### Activation

`StartupIntentParser` accepts only zero arguments or exactly case-sensitive `--show`. Secondary startup sets the existing fixed event and exits. Primary startup completes host readiness, then requests one show. Unknown arguments are discarded and never become a payload.

### Themes and popup

Semantic Light, Dark, and HighContrast dictionaries have exact key parity. High contrast wins; theme changes marshal to the dispatcher and popup-open reevaluation recovers missed notifications. The popup has fixed header, permanent five-hour and weekly cards, a secondary-only scroll region, and fixed footer. Placement is work-area/DPI aware; DWM effects are optional over an opaque WPF fallback.

### Stock YASB adapter

The bounded PowerShell reader reads only the known sanitized file, validates schema v1, emits only `label`, `tooltip`, and `className`, and always includes 5h/7d placeholders. Polling never refreshes or starts AIBar. The relative launcher passes only `--show`. Distribution stays an unsigned, self-contained, manually extracted Windows x64 ZIP.

## File and symbol ownership map

| File | Unit and owned change |
|---|---|
| `src/AIBar.Application/QuotaExport.cs` | 1a-contract: records/enums/input/projector/validating serializer/ports. 1a-privacy may later add publisher/lifecycle symbols only. Authority symbols do not belong here. |
| `src/AIBar.Application/QuotaRefreshCoordinator.cs` | 1a-authority: serialized transition acceptance, state mutation, sequence/generation allocation, queue/drain, ordered `AuthorityUpdated`/`StateChanged` delivery. |
| `src/AIBar.Application/QuotaAuthorityUpdate.cs` (optional, repository-conventional) | 1a-authority: authority update record if not colocated with coordinator. |
| `src/AIBar.Application/BetaRuntime.cs` | 1a-privacy: persisted-consent pending enablement before retrieval. |
| `src/AIBar.Application/StartupSettings.cs` | 1a-privacy: fail-closed disable/revoke and enable-before-retrieval ordering. |
| `src/AIBar.Application/ClearAiBarDataService.cs` | 1b-clear: exact owned snapshot path and clear transaction integration. |
| `src/AIBar.Desktop/QuotaExportWindows.cs` | 1b-writer: Windows canonical path, JSON adapter/context, minimum-rights ACL-safe same-directory writer. |
| `src/AIBar.Desktop/App.xaml.cs` | 1b-clear composition and explicit disposal order; later activation/theme lifecycle integration. |
| `src/AIBar.Desktop/HostPrimitives.cs` | Unit 2 startup intent/kernel safety; Unit 4b placement primitives. |
| `src/AIBar.Desktop/HostRuntime.cs` | Units 2, 4a, 4b: show lifecycle, tray state flow, placement/surface integration. |
| `src/AIBar.Desktop/WindowsTheme.cs` | Units 3 and 4b: theme source/controller and progressive surface. |
| `src/AIBar.Desktop/Themes/Semantic.*.xaml` | Unit 3 matching semantic dictionaries. |
| `src/AIBar.Desktop/MainWindow.xaml(.cs)` | Units 4a/4b quota-first structure and bounded measurement hooks. |
| `tests/AIBar.Domain.Tests/QuotaExportTests.cs` | 1a-contract contract/projection/direct-document matrices; later 1a-privacy publisher/privacy matrices by test-class ownership. No authority-stream coverage is hidden here unless clearly separated. |
| `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs` | 1a-authority serialized transition, generation, concurrency, clear/cancellation, same-reference, and cross-event compatibility matrices. |
| `tests/AIBar.Domain.Tests/BetaConsentOrQuotaTests.cs` | 1a-privacy persisted-consent and qualifying-retrieval lifecycle. |
| `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs` | 1b-writer minimum-rights ACL/readback, reparse, durability, complete-reader, sharing, concurrency, and cleanup tests. |
| `tests/AIBar.Domain.Tests/HostPrimitivesTests.cs` | Units 2/4b parser, kernel, and placement matrices. |
| `tests/AIBar.Domain.Tests/HostRuntimeTests.cs` | Units 2/4a host activation and exact tray presentation flow. |
| `tests/AIBar.Domain.Tests/QuotaVisualDesignTests.cs` | Units 3/4a/4b theme parity, permanent popup/tray slots, accessibility. |
| `tests/AIBar.Domain.Tests/PopupRenderedTests.cs` | Unit 4b synthetic Windows layout/render checks. |
| `yasb/*`, `tests/AIBar.Domain.Tests/YasbAdapterTests.cs` | Unit 5 reader/launcher/config/CSS/fixtures and Unit 6 cleanup seam. |
| `scripts/Publish-Deterministic.ps1`, `docs/private-beta.md` | Unit 6 deterministic inventory, setup, rollback order. |

## Data and control flows

### Contract, authority, privacy, and publication

```text
QuotaRefreshCoordinator (1a-authority)
  -> accept transition + mutate State
  -> allocate eventSequence and optional retrievalGeneration atomically
  -> serialized delivery drain
       -> AuthorityUpdated(update)
       -> StateChanged(update.State)       # contiguous compatible pair
            |
            +--> existing presentation consumers
            |
            +--> QuotaExportPublisher (1a-privacy)
                    -> sequence/state chooses latest projection
                    -> retrieval generation unlocks disclosure only
                    -> disclosure epoch owns cancellation
                    -> QuotaExportProjector + normalizer (1a-contract)
                    -> IQuotaExportWriter
                              -> WindowsAtomicQuotaExportWriter (1b-writer)
                              -> secure same-directory atomic commit

    clear/composition (1b-clear)
      -> cancel/await queued and in-flight publication
      -> mutate only exact owned snapshot path
      -> disabled/null recreation or safe absence
      -> dispose writer-facing resources before their dependencies
                          -> yasb-quota.json
```

### Privacy precedence

```text
disable/revoke/clear
  -> advance epoch + cancel prior ordinary work
  -> reject older queued/completing items
  -> publish disabled/null or remove through current gate
  -> complete only after safe result is authoritative

enable/persisted consent
  -> capture current retrieval generation as exclusive baseline
  -> cache/loading/failure/reevaluation stay null
  -> accepted + persisted real retrieval emits newer generation
  -> values may unlock
  -> later sequence-ordered freshness updates may republish
```

### Activation and display

```text
YASB click -> show-aibar.cmd -> AIBar.Desktop.exe --show
  -> existing primary: fixed event -> RequestShowOnce
  -> new primary: normal startup -> ready -> RequestShowOnce
  -> theme + placement + optional DWM -> existing WPF popup
```

## Failure semantics

| Failure | Required behavior |
|---|---|
| Unknown state/warning or inconsistent direct export document | Contract normalizer returns unavailable/null fallback; serializer does not throw or emit arbitrary values. |
| Missing reset | Preserve valid window percentage and emit `resetAt: null`. |
| Missing/unusable credential | Map through an explicit bounded signal to authentication warning; never serialize credential details. |
| Concurrent authority transitions | Serialized acceptance/allocation/delivery preserves monotonic order and paired legacy compatibility. |
| Retrieval races clear/cancellation | Old operation cannot publish or increment generation after privacy/clear authority wins. |
| Persistence failure | No retrieval-generation increment; prior authoritative state is retained or a safe failure transition is delivered in order. |
| Blocked ordinary write during privacy action | Old epoch token is cancelled and late completion has no authority. |
| Writer/ACL/reparse/atomic failure | Retain prior complete file or safe absence; quota cache/UI continue. |
| Reader malformed/unsupported input | Both placeholders plus fixed unavailable text; no raw error or file mutation. |
| Theme/DWM/placement failure | Keep prior/light or opaque/clamped fallback; popup remains usable. |

## Testing strategy

1. **1a-contract:** pure synthetic tests for exact schema/version/property/domain allowlists; forbidden root/db/source-model and private fields; complete state/failure/credential/cache/partial-window matrices; 0/100/interior/out-of-range percentages; exact retained source time; nullable reset; UTC normalization; unknown enums; and inconsistent direct-document fail-closed serialization.
2. **1a-authority:** synthetic coordinator/store/provider tests with real task concurrency for transition serialization, sequence and delivery order, StateChanged pairing, accepted/persisted generation, equal-content retrieval, persistence failure, reevaluate/clear/refresh/cancellation races, replay, and disposal.
3. **1a-privacy:** cancellation-aware in-memory writer tests for restart generation gating, freshness propagation, latest-state/same-reference bursts, epoch cancellation, privacy-operation failures, and disposal.
4. **1b-writer / 1b-clear:** isolated Windows temporary-directory tests first prove minimum-rights ACL ordering/readback, first/replace, reparse/sharing/concurrent complete-reader behavior, durable UTF-8 bytes, previous-byte survival, and exact cleanup; the following child proves exact-path clear coordination, stale suppression, disabled/null recreation or safe absence, writer failure isolation, composition, and disposal order.
5. **2–4b:** focused parser/host, theme, popup/tray, placement, synthetic render, focus, automation, DPI, and DWM-fallback tests.
6. **5–6:** synthetic isolated reader/cleanup fixtures, exact stock asset inspection, deterministic inventory, and rollback ordering. No live AIBar/YASB, real profile, private endpoint, credential, or user dataset.

Passing a selected target count is insufficient when any mandatory matrix row is omitted. Contract and authority validate independently before privacy can begin.

## Specification-to-design traceability

| Specification capability | Design realization |
|---|---|
| Sanitized export: closed schema and safe fallback | 1a-contract exact schema/version/property/state/warning domains, validating direct-document serializer, nullable reset, forbidden-field construction boundary. |
| Sanitized export: truthful values and age | 1a-contract complete state/failure/credential/partial-window/timestamp/percentage matrices with exact retained source time. |
| Quota status and authority freshness | 1a-authority serialized every-transition stream, monotonic sequence, accepted/persisted retrieval generation, ordered legacy compatibility. |
| Privacy actions and restart | 1a-privacy generation-only unlock, event/state freshness, epoch cancellation, fail-closed disable/revoke/clear. |
| Atomic current-user publication | 1b-writer same-directory minimum-rights ACL-safe writer and complete old/new reader behavior; 1b-clear exact ownership, stale suppression, disabled/null recreation or safe absence, Desktop composition, and disposal order. |
| External show | Unit 2 payload-free parser and existing single-instance event. |
| Windows theme | Unit 3 matching semantic resources and dispatcher-safe high-contrast-first controller. |
| Popup presentation | Units 4a/4b fixed quota-first layout, accessibility, tray flow, DPI/work-area placement, rendered validation, opaque fallback. |
| YASB CustomWidget | Unit 5 fixed-path reader, permanent slots, exact stock config, fixed `--show` launcher. |
| Private beta distribution | Unit 6 path-neutral ZIP inventory and stop-clean-verify rollback. |

## Rollout, compatibility, and rollback

Rollout is one linear feature-branch chain:

```text
1a-contract -> 1a-authority -> 1a-privacy -> 1b-writer -> 1b-clear -> 2 -> 3 -> 4a -> 4b -> 5 -> 6
```

1a-contract can land as a pure closed contract with no coordinator or external file. 1a-authority then adds the internal causal stream while preserving `StateChanged` consumers. 1a-privacy adds in-memory publication policy only after authority order is proven. 1b-writer is the first externally visible publication unit; 1b-clear then binds it to clear and Desktop lifecycle ownership. Later presentation and asset units consume those boundaries without redefining them.

There is no migration of `quota.db`, credentials, sessions, endpoint behavior, refresh cadence, or analytics. Event sequence, retrieval generation, and disclosure epoch are process-local and are neither persisted nor exported. Schema-v1 readers reject future schemas safely.

Rollback follows reverse ownership. Revert 1b-clear only after the snapshot is disabled/null or safely absent, leaving the committed writer and Application privacy intact. Revert 1b-writer only after securing/removing or nulling its snapshot, leaving 1a-privacy intact; then remove privacy before authority and authority before contract. Reverting authority restores the prior coordinator while preserving existing quota refresh/cache/presentation behavior. Reverting contract removes only projection types after dependents are gone. Theme/popup changes can revert independently. Installed YASB rollback must stop AIBar and successfully remove or replace the snapshot with disabled/null before assets are removed.

At planning time, the historical 359-line five-path candidate was not eligible to land or earn completion credit. The later writer (`3d137171a9c6c3a8fd4d079e8d9de6bf819a0043`) and clear (`bc8d79f719989a21f72b5c6cf843117c5001228d`) commits exist as separate child boundaries, but no immutable stash object ID or surviving stash ref/reflog proves the proposed selective restoration; that provenance is unavailable and is not claimed.

## Eleven review units and forecasts

Forecasts count authored additions plus deletions and keep tests with behavior. They are planning ranges, not completion claims.

| Unit | Dependency and boundary | Forecast | Margin to 400 | Hard split trigger |
|---|---|---:|---:|---|
| 1a-contract | First child; closed records/enums/projector/normalizer/ports and full contract matrices; no coordinator/publisher/platform | 160–220 | 180 | Stop at 220; if complete matrices cannot fit below 300, replan before authority. |
| 1a-authority | Depends on contract; update record, coordinator serialization, sequence/delivery/generation/races; no publisher/platform | 120–180 | 220 | Stop at 180; if concurrency and cross-event proof cannot fit below 280, replan before privacy. |
| 1a-privacy | Depends on authority; publisher, event freshness, epochs/cancellation, consent lifecycle | 180–260 | 140 | Stop at 260; if complete privacy matrix cannot fit below 320, replan before 1b-writer. |
| 1b-writer | Depends on privacy; canonical path, minimum-rights ACL/readback, reparse-safe durable atomic writer, complete old/new readers | 335–360 | 40 | Stop at 360; never omit minimum-rights or reader atomicity coverage, cross 400, or use an exception. |
| 1b-clear | Depends on committed 1b-writer; exact clear transaction, stale suppression, disabled/null or absent result, composition/disposal | 90–140 | 260 | Stop at 160; do not borrow writer implementation or Unit 2 scope. |
| 2 | Depends on 1b-clear; payload-free activation | 230–290 | 110 | Replan at 360 if kernel compatibility needs a separate adapter. |
| 3 | Depends on 2; automatic semantic themes | 320–380 | 20 | At 380 retain only required semantic migration; never cross 400. |
| 4a | Depends on 3; popup hierarchy/accessibility/tray | 340–380 | 20 | At 380 defer placement/render breadth to 4b; never cross 400. |
| 4b | Depends on 4a; placement/DPI/DWM/rendered validation | 300–370 | 30 | At 370 table-drive required cases or replan before 400. |
| 5 | Depends on 4b and export/activation contracts; stock YASB assets | 320–380 | 20 | At 380 table-drive fixtures or replan; no production path parameter. |
| 6 | Depends on 5, the 1b-writer path contract, and committed 1b-clear; ZIP/docs/cleanup | 260–330 | 70 | At 330 keep cleanup proof together and replan packaging breadth. |

The Unit 1b replacement forecasts **425–500 authored source/test lines across two sequential children**. All unrelated accepted-unit forecasts remain unchanged. Every child remains below 400, the selected strategy is `feature-branch-chain`, and no `size:exception` is authorized.

## Explicit exclusions

No unit adds cost, spend, trend, ETA, forecasting, a native/forked YASB widget, duplicate YASB analytics, WinUI, installer, signing, updater, package manager, public release, new credential/session/database/log/analytics/environment data access, endpoint behavior, refresh cadence, general IPC, HTTP, sockets, named pipes, URL protocols, process discovery, arbitrary command execution, real-data validation, or live-process validation.
