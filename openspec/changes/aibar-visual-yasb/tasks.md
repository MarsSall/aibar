# Implementation Tasks: Reference-quality AIBar popup and YASB quota bridge

## Review Workload Forecast

| Field | Value |
|---|---|
| Estimated changed lines | **2,530–3,150 authored lines total** across ten bounded units |
| 400-line budget risk | High overall; every child forecast is below 400 and has an explicit hard split trigger |
| Chained PRs recommended | Yes |
| Suggested order | 1a-contract → 1a-authority → 1a-privacy → 1b → 2 → 3 → 4a → 4b → 5 → 6 |
| Delivery strategy | ask-on-risk → chained delivery selected |
| Chain strategy | feature-branch-chain |
| Size exception | None; `size:exception` is not authorized |

Decision needed before apply: explicit approval is required for disposition of the preserved current candidate and for Unit 1a-contract apply.

The current **223-authored-line source/test candidate** remains intact, uncommitted, and unstaged. Its absolute target-scoped evidence is **35/35**, and the latest native validation-only objective is complete, but independent acceptance validation failed. Passing tests omitted required contract and concurrency cases, so no task is complete and no current line earns completion credit in this plan.

This replanning task runs no tests or builds and changes no source/test byte.

## Delivery, dependency, and feature-branch-chain map

```text
1a-contract Closed projection + fail-closed wire contract
  -> 1a-authority Serialized causal authority stream
  -> 1a-privacy Publisher + consent/privacy lifecycle
  -> 1b Windows writer/ACL/filesystem privacy
  -> 2 --show activation
  -> 3 Automatic themes
  -> 4a Popup hierarchy/tray
  -> 4b Placement/DWM/rendered validation
  -> 5 YASB assets
  -> 6 ZIP/docs/rollback cleanup
```

The tracker remains draft/no-merge until all children are integrated. Child 1a-contract targets the tracker branch; each later child targets its immediate predecessor. Only the completed tracker targets `main`.

| Order | Logical branch | PR base | Review focus | Forecast | Margin to 400 |
|---:|---|---|---|---:|---:|
| Tracker | `feat/aibar-visual-yasb` | `main` | Draft integration only | n/a | n/a |
| 1a-contract | `feat/aibar-visual-yasb-1a-contract` | tracker | Closed projection, wire normalizer, full contract matrices | 160–220 | 180 |
| 1a-authority | `feat/aibar-visual-yasb-1a-authority` | 1a-contract | Serialized coordinator transitions, sequence/delivery/generation races | 120–180 | 220 |
| 1a-privacy | `feat/aibar-visual-yasb-1a-privacy` | 1a-authority | Publisher ordering, cancellation, consent lifecycle | 180–260 | 140 |
| 1b | `feat/aibar-visual-yasb-1b` | 1a-privacy | Windows atomic writer, ACL, clear, composition | 300–360 | 40 |
| 2 | `feat/aibar-visual-yasb-2` | 1b | Payload-free activation | 230–290 | 110 |
| 3 | `feat/aibar-visual-yasb-3` | 2 | Semantic Windows themes | 320–380 | 20 |
| 4a | `feat/aibar-visual-yasb-4a` | 3 | Popup hierarchy, accessibility, tray | 340–380 | 20 |
| 4b | `feat/aibar-visual-yasb-4b` | 4a | Placement, DPI, DWM, rendered checks | 300–370 | 30 |
| 5 | `feat/aibar-visual-yasb-5` | 4b | Stock YASB adapter | 320–380 | 20 |
| 6 | `feat/aibar-visual-yasb-6` | 5 | ZIP, docs, fixed rollback cleanup | 260–330 | 70 |

## Global work-unit rules

- Each unit is one behavior-complete commit boundary and one candidate review scope.
- Tests stay with the behavior they verify; documentation stays with the user-visible workflow.
- Preserve the current 223-line candidate exactly until an explicit disposition decision.
- A hard split trigger is a stop, not permission to omit cases or exceed 400 lines.
- No unit may use `size:exception`.
- All validation uses synthetic state and isolated local files only. No private endpoint, network, real credential, real profile, live AIBar/YASB process, or real user dataset is permitted.

## Unit 1a-contract — Closed projection and fail-closed wire contract

**Forecast:** 160–220 authored lines. **Margin:** 180 lines. **Risk:** High (public privacy contract and direct-document fail-closed behavior).

**Expected files:**
- `src/AIBar.Application/QuotaExport.cs`
- `tests/AIBar.Domain.Tests/QuotaExportTests.cs`

**Start state:** The 223-line mixed contract/authority candidate is preserved and has failed independent acceptance validation. Candidate disposition and this apply require explicit approval. No contract task is complete.

**End state:** Application owns schema-v1 records/enums/input/projector/normalizer/ports. `ResetAt` is nullable. Projected and directly supplied documents pass through one validating serialization boundary. Unknown or inconsistent values become the canonical unavailable/null fallback without throwing. The complete contract matrix passes independently of coordinator authority, publisher, consent, or platform code.

**Dependencies:** Approved sanitized-export and quota-status specifications; existing `QuotaRefreshState`, `QuotaSnapshot`, `QuotaFailure`, and synthetic clock/state fixtures. This is the first child.

**Exclusions:** `QuotaAuthorityUpdate`; `QuotaRefreshCoordinator`; any event/sequence/generation work; publisher; consent/privacy epoch; cancellation; Desktop; filesystem; ACL; clear-data ownership; `--show`; themes; popup/tray; YASB; ZIP; endpoint, refresh-cadence, or credential-retrieval behavior changes.

### Behavior-first tests and contracts

- [x] **1a-contract.1 RED — exact schema and wire domains:** Assert numeric `schemaVersion == 1`; exact ordered root names `schemaVersion`, `generatedAt`, `state`, `warning`, `sourceRetrievedAt`, `fiveHour`, `weekly`; exact window names `percentageUsed`, `resetAt`; exact state and warning wire domains; and no extension data. <!-- sdd-owner: implementation -->
- [x] **1a-contract.2 RED — forbidden source boundary:** Build source models containing token, credential, account, plan, endpoint, path, response, exception, safe-code, diagnostic, session, analytics, prompt, log, environment, root, db/database, and extra source-model properties. Prove none can appear in output and projection does not reflect the source object graph. <!-- sdd-owner: implementation -->
- [x] **1a-contract.3 RED — complete state/failure/credential matrix:** Cover disabled, current, refreshing with/without cache, stale, unavailable, optional failure, both windows, five-hour-only, weekly-only, and no windows. Cover network/service/redirect/malformed, authentication, permission, missing credential, unusable credential, explicit unavailable, null, and unknown failure with and without cache. Require explicit credential-signal mapping and safe no-cache warnings. <!-- sdd-owner: implementation -->
- [x] **1a-contract.4 RED — timestamp/percentage/reset matrix:** Cover percentages `0`, `100`, and an interior decimal; below/above bounds; generated/source separation; exact retained source timestamp across loading/failure republish; future/inconsistent source; UTC normalization; reset before/equal/after now; nullable or missing reset; and partial windows. <!-- sdd-owner: implementation -->
- [x] **1a-contract.5 RED — arbitrary direct-document fail-closed matrix:** Call the public serialization boundary with null, schema mismatch, unknown state, unknown warning, disabled-with-values, unavailable-with-source, valued-window-without-source, invalid percentage, inconsistent warning/state, and invalid timestamp combinations. Require the whole unavailable/null fallback and no exception. <!-- sdd-owner: implementation -->

### Production implementation

- [x] **1a-contract.6 GREEN — closed records and nullable reset:** Implement/correct export records/enums/input/ports so `QuotaExportWindow.ResetAt` is nullable and no arbitrary string or extension-data field exists. Do not add authority, publisher, or platform behavior. <!-- sdd-owner: implementation -->
- [x] **1a-contract.7 GREEN — projector and normalizer:** Implement the complete projection tables and one validating serializer/normalizer shared by projector output and direct documents. Use explicit switch/allowlist logic; normalize UTC; preserve exact safe source age; map missing/unusable credentials safely; and return canonical fallback for every unsupported/inconsistent input. <!-- sdd-owner: implementation -->

### Focused check and commit boundary

- [x] **1a-contract.8 CHECK — verify contract independently:** Run only focused pure/synthetic contract tests. Inspect every mandatory matrix row, exact wire bytes/properties, nullable reset, weekly-only, interior percentage, source timestamp retention, forbidden root/db/source fields, credential mapping, and direct-document fallback. No coordinator or concurrency test is used as substitute evidence. <!-- sdd-owner: implementation -->
- [ ] **1a-contract.9 COMMIT BOUNDARY — contract only:** Freeze one Application contract/projector commit. If the current candidate is retained, first remove/rehome authority-owned symbols/tests so this diff has no coordinator stream. Rollback removes only the export contract and creates no external file. <!-- sdd-owner: implementation -->

**Hard split trigger:** Stop and recalculate at 220 authored lines. If the complete contract matrices cannot fit below 300, require a new explicit planning split before 1a-authority. Do not move authority/publisher work inward, omit cases, cross 400, or use an exception.

## Unit 1a-authority — Serialized causal authority stream

**Forecast:** 120–180 authored lines. **Margin:** 220 lines. **Risk:** High (concurrent authority ordering and causal generation).

**Expected files:**
- `src/AIBar.Application/QuotaRefreshCoordinator.cs`
- `src/AIBar.Application/QuotaAuthorityUpdate.cs` when a separate repository-conventional file is clearer
- `tests/AIBar.Domain.Tests/QuotaRefreshCoordinatorTests.cs`
- `tests/AIBar.Domain.Tests/QuotaExportTests.cs` only to remove/rehome mixed candidate authority tests; no new contract ownership

**Start state:** Unit 1a-contract is complete and independently reviewed. There is a closed projection contract but no accepted new authority stream. Existing `StateChanged` consumers remain authoritative.

**End state:** `QuotaRefreshCoordinator` accepts, mutates, sequences, and delivers all authoritative transitions through one serialized queue/drain. `QuotaAuthorityUpdate` is monotonic and paired with compatible `StateChanged` delivery. Retrieval generation increments only after an accepted/persisted real retrieval, including equal-content retrievals. Real concurrency and clear/cancellation races pass.

**Dependencies:** 1a-contract types/ports; existing provider/store/clock/freshness seams and coordinator lifecycle.

**Exclusions:** Projector/wire redesign; publisher; consent/privacy epochs; publication cancellation; Desktop; filesystem; ACL; clear-file ownership; `--show`; theme/popup/tray; YASB; ZIP; endpoint/cadence/credential behavior changes.

### Behavior-first tests and contracts

- [ ] **1a-authority.1 RED — serialized transition and cross-event order:** Observe initialization success/failure, loading, accepted success, null result, provider failure, persistence failure, optional failure, freshness-only reevaluation, and clear. Require one monotonic contiguous sequence per accepted transition and one contiguous `AuthorityUpdated(update)` plus matching `StateChanged(update.State)` pair with no cross-transition interleaving. <!-- sdd-owner: implementation -->
- [ ] **1a-authority.2 RED — retrieval-generation causality:** Prove cache load, loading, reevaluation, failure, null result, replay, cancellation, clear, optional failure, and persistence failure do not increment. Prove a valid non-null retrieval increments exactly once only after persistence and operation-generation acceptance, including equal-value/equal-timestamp/equal-reference content. <!-- sdd-owner: implementation -->
- [ ] **1a-authority.3 RED — real concurrent sequencing:** Race reevaluate with reevaluate, refresh completion with reevaluate, and multiple same-reference/equal-content transitions. Block a subscriber to expose callback races. Require allocation and delivery order to agree and the final `State` to match the last delivered update. <!-- sdd-owner: implementation -->
- [ ] **1a-authority.4 RED — retrieval versus clear/cancellation:** Race a provider/store completion against `CancelAndWaitAsync` and `ClearAsync`. Require cancelled/cleared retrievals never to become authoritative or increment generation, clear-to-unavailable to retain final order, and replay callers to observe the same active operation without duplicate authority. <!-- sdd-owner: implementation -->
- [ ] **1a-authority.5 RED — lifecycle compatibility:** Cover reentrant and throwing subscribers according to repository event policy, disposal during in-flight work, no transitions after disposal, and unchanged existing `StateChanged` consumer semantics. <!-- sdd-owner: implementation -->

### Production implementation

- [ ] **1a-authority.6 GREEN — authority-owned update and queue/drain:** Implement `QuotaAuthorityUpdate` outside the contract-owned `QuotaExport.cs` region and one coordinator-owned serialized transition queue/drain. Mutate state, allocate sequence/generation, enqueue immutable updates, and deliver callbacks outside the state lock but in accepted order. <!-- sdd-owner: implementation -->
- [ ] **1a-authority.7 GREEN — causal acceptance:** Integrate initialization, refresh, reevaluation, clear, cancellation, persistence, replay, and disposal through the same transition protocol. Reject stale operation generations and increment retrieval generation only at accepted/persisted commit. <!-- sdd-owner: implementation -->

### Focused check and commit boundary

- [ ] **1a-authority.8 CHECK — verify authority independently:** Run focused synthetic coordinator tests with actual concurrent tasks and blocking seams. Inspect sequence allocation versus callback order, same-reference/equal-content behavior, cross-event compatibility, clear/cancellation races, and persistence-before-generation. Contract serializer tests are not substitute evidence. <!-- sdd-owner: implementation -->
- [ ] **1a-authority.9 COMMIT BOUNDARY — authority only:** Freeze one coordinator/authority-stream commit based on 1a-contract. Rollback removes the new stream and restores prior coordinator event behavior without changing the closed export contract or creating external files. <!-- sdd-owner: implementation -->

**Hard split trigger:** Stop and recalculate at 180 authored lines. If the complete real-concurrency, generation, and cross-event matrix cannot fit below 280, require a new explicit split before 1a-privacy. Do not weaken serialization, drop race cases, cross 400, or use an exception.

## Unit 1a-privacy — Publisher, freshness propagation, cancellation, and consent lifecycle

**Forecast:** 180–260 authored lines. **Margin:** 140 lines. **Risk:** High (privacy epochs, restart causality, blocked writes).

**Expected files:**
- `src/AIBar.Application/QuotaExport.cs` (publisher/lifecycle symbols only after contract child)
- `src/AIBar.Application/BetaRuntime.cs`
- `src/AIBar.Application/StartupSettings.cs`
- `tests/AIBar.Domain.Tests/QuotaExportTests.cs` (publisher/privacy classes)
- `tests/AIBar.Domain.Tests/BetaConsentOrQuotaTests.cs`
- `tests/AIBar.Domain.Tests/StartupSettingsTests.cs` when conventional

**Start state:** Contract and authority units are complete and reviewed. Every authority transition arrives in serialized order with sequence and retrieval generation. No publisher is composed.

**End state:** Publisher subscribes to every update, uses generation only for post-enable unlock, uses sequence/state for freshness/latest-state order, cancels blocked ordinary writes on epoch advance, and makes disable/revoke/clear/restart/disposal fail closed.

**Dependencies:** 1a-authority and transitively 1a-contract; existing consent/runtime/settings seams; cancellation-aware in-memory writers.

**Exclusions:** Desktop composition, paths, JSON filesystem adapter, ACL/reparse/atomic writer, clear owned path, activation, themes, popup/tray, YASB, ZIP, endpoint/cadence/credential behavior changes.

### Behavior-first tests and contracts

- [ ] **1a-privacy.1 RED — persisted-consent generation gate:** Prove restart cache, loading, reevaluation, failure, replay, and same-reference events cannot unlock at the exclusive baseline; prove a newer accepted/persisted retrieval unlocks even with equal content; failed/null/persistence-failed retrieval does not. <!-- sdd-owner: implementation -->
- [ ] **1a-privacy.2 RED — freshness/latest-state bursts:** Cover current→stale reevaluation, loading→failure, same-reference updates, rapid and concurrent bursts, coalesced intermediates, and final-state retention. Require sequence/state ordering and no publisher-triggered refresh. <!-- sdd-owner: implementation -->
- [ ] **1a-privacy.3 RED — epoch cancellation:** Block ordinary publication, then disable, revoke, clear, re-enable, or dispose. Require token cancellation, stale queued/in-flight rejection, disabled/null or safe absence before privacy completion, and no stale resurrection. <!-- sdd-owner: implementation -->
- [ ] **1a-privacy.4 RED — failures and lifecycle races:** Cover cancellation-resistant writers, disabled/null write failure, repeated privacy commands, privacy versus ordinary races, routine publication failure isolation, unsubscription, and disposal. <!-- sdd-owner: implementation -->

### Production implementation

- [ ] **1a-privacy.5 GREEN — generation-gated sequence-fresh publisher:** Add `QuotaExportPublisher` over the authority stream with one drain/gate, final-state retention, generation-only unlock, and sequence/state freshness. No timer or refresh call. <!-- sdd-owner: implementation -->
- [ ] **1a-privacy.6 GREEN — epoch cancellation and operations:** Advance/cancel before disable/revoke/clear/enable/dispose, reject old completions, and complete privacy operations only after a safe current-epoch result. <!-- sdd-owner: implementation -->
- [ ] **1a-privacy.7 GREEN — consent lifecycle ordering:** Establish persisted/manual enable baselines before retrieval, suppress cache/freshness events, and preserve fail-closed disable/revoke/clear ordering. <!-- sdd-owner: implementation -->

### Focused check and commit boundary

- [ ] **1a-privacy.8 CHECK — verify privacy independently:** Run focused pure/in-memory publisher and lifecycle tests covering restart, equal-content unlock, freshness-only updates, latest-state bursts, blocked cancellation, failures, races, and disposal. No Desktop/filesystem/profile/live data. <!-- sdd-owner: implementation -->
- [ ] **1a-privacy.9 COMMIT BOUNDARY — privacy only:** Freeze one Application publisher/lifecycle commit based on authority. Shared `QuotaExport.cs` may contain contract and privacy symbols, but this child owns only the latter. Rollback leaves contract/authority intact and external disclosure absent. <!-- sdd-owner: implementation -->

**Hard split trigger:** Stop at 260 and recalculate. If the full restart/freshness/cancellation/concurrency matrix cannot fit below 320, replan before 1b. Do not move platform work inward, omit cancellation, cross 400, or use an exception.

## Unit 1b — Windows atomic writer, ACL, composition, and filesystem privacy

**Forecast:** 300–360 authored lines. **Margin:** 40 lines. **Risk:** High (data exposure, ACL ordering, atomic replacement, reparse/concurrency).

**Expected files:**
- `src/AIBar.Application/ClearAiBarDataService.cs`
- `src/AIBar.Desktop/QuotaExportWindows.cs`
- `src/AIBar.Desktop/App.xaml.cs`
- `tests/AIBar.Domain.Tests/QuotaExportWriterTests.cs`
- `tests/AIBar.Domain.Tests/ClearAiBarDataTests.cs`

**Start state:** Contract, authority, and privacy units are complete; no Windows writer is composed and no external snapshot is required.

**End state:** `%LOCALAPPDATA%\AIBar\yasb-quota.json` is published only through a same-directory atomic commit with directory/existing destination/temp/result ACL proof, reparse and sharing defenses, clear ownership, disabled recreation, and safe composition.

**Dependencies:** 1a-privacy transitively through authority/contract; Windows ACL and same-volume atomic APIs; existing clear transaction and Desktop composition.

**Exclusions:** Application redesign, activation/kernel ACLs, themes, popup/tray, YASB reader/launcher, ZIP/docs, real profile paths, endpoint/cadence/credential changes.

### Behavior-first tests and contracts

- [ ] **1b.1 RED — ACL matrix:** Cover new/existing directory/destination, broad inherited/explicit ACE removal, current-user+SYSTEM result, application/read-back failure at directory/destination/temp/result, and no valued visibility after pre-commit failure. <!-- sdd-owner: implementation -->
- [ ] **1b.2 RED — atomic matrix:** Cover first write, replace, same-directory GUID temp, secure-before-write, flush/close/schema validation, first-write race, failures before/after write and before commit, sharing, cancellation, previous-byte survival, complete old/new reader views, and exact temp cleanup. <!-- sdd-owner: implementation -->
- [ ] **1b.3 RED — reparse/concurrency matrix:** Reject parent, destination, temp, and race-substituted reparse points. Cover concurrent first/replace and latest-state flow through the real writer with a complete latest document and compliant ACL. <!-- sdd-owner: implementation -->
- [ ] **1b.4 RED — clear/composition matrix:** Cover exact owned path, staged rollback, queued/in-flight clear, disabled recreation or safe absence, stale callback suppression, sibling composition, disposal order, and writer failure isolation from cache/UI. <!-- sdd-owner: implementation -->

### Production implementation

- [ ] **1b.5 GREEN — secure atomic writer:** Implement exact path, pre-commit ACL/reparse checks, secure same-directory temp, validated write/flush/close, atomic rename/replace, result verification, and exact cleanup; no copy/cross-directory/elevation/permissive fallback. <!-- sdd-owner: implementation -->
- [ ] **1b.6 GREEN — clear and composition:** Add exact owned path, preserve staged rollback/reparse defenses, recreate only disabled/null after clear, compose publisher/writer beside presentation, and prevent shutdown recreation. <!-- sdd-owner: implementation -->

### Focused check and commit boundary

- [ ] **1b.7 CHECK — verify 1b:** Run isolated writer/clear/composition tests only; inspect ACLs, old/new atomicity, reparse/sharing/concurrency, previous bytes, owned path, disabled recreation, and no real profile/network/live process. <!-- sdd-owner: implementation -->
- [ ] **1b.8 COMMIT BOUNDARY — Windows publication:** Freeze one Windows filesystem/privacy commit based on 1a-privacy. Rollback first secures/removes or nulls the snapshot, then removes writer/composition while retaining all Application units. <!-- sdd-owner: implementation -->

**Hard split trigger:** Stop at 360 and recalculate. If the security/atomic/clear matrix cannot stay below 400, require a new planning split before Unit 2. Never omit security cases or use an exception.

## Unit 2 — Payload-free `--show` activation

**Forecast:** 230–290. **Margin:** 110. **Risk:** Medium/High.

**Expected files:** `src/AIBar.Desktop/HostPrimitives.cs`, `src/AIBar.Desktop/App.xaml.cs`, `src/AIBar.Desktop/HostRuntime.cs`, `tests/AIBar.Domain.Tests/HostPrimitivesTests.cs`, `tests/AIBar.Domain.Tests/HostRuntimeTests.cs`.

**Start state:** Units 1a-contract through 1b are complete; existing single-instance and tray startup are unchanged.

**End state:** Exactly `--show` requests one show with no payload; first/secondary/startup-race flows preserve one authority.

**Dependencies:** 1b in the chain and existing single-instance/host readiness seams.

**Exclusions:** theme/DWM/layout/YASB launcher, general IPC, path/process discovery, endpoint behavior.

- [ ] **2.1 RED — parser matrix:** Zero args, exact `--show`, case mismatch, assignment, abbreviation, path, URL, multiple and arbitrary args; only Ordinary/Show and no forwarded payload. <!-- sdd-owner: implementation -->
- [ ] **2.2 RED — host lifecycle matrix:** First show-after-ready, secondary signal/exit, visible popup, pending construction, repeated requests, failed start, disposal, abandoned mutex, access denied, and object-type mismatch. <!-- sdd-owner: implementation -->
- [ ] **2.3 RED — kernel safety:** Preserve fixed names, zero-time acquisition, AutoReset, abandoned behavior, and minimum current-user/SYSTEM rights with no fallback authority name. <!-- sdd-owner: implementation -->
- [ ] **2.4 GREEN — bounded activation:** Implement parser and ready/pending show sequencing over the existing fixed event; discard unknown args and preserve ordinary async initialization. <!-- sdd-owner: implementation -->
- [ ] **2.5 CHECK — verify Unit 2:** Run focused parser/host tests and prove no payload/discovery/duplicate authority. <!-- sdd-owner: implementation -->
- [ ] **2.6 COMMIT BOUNDARY — activation:** Freeze one activation commit; rollback restores prior startup without changing export authority. <!-- sdd-owner: implementation -->

**Hard split trigger:** Replan at 360 if kernel ACL compatibility needs a separate adapter. Do not mix theme work or cross 400.

## Unit 3 — Automatic semantic Windows themes

**Forecast:** 320–380. **Margin:** 20. **Risk:** Medium.

**Expected files:** `src/AIBar.Desktop/WindowsTheme.cs`, `App.xaml`, `App.xaml.cs`, `Themes/Semantic.Light.xaml`, `Semantic.Dark.xaml`, `Semantic.HighContrast.xaml`, and `tests/AIBar.Domain.Tests/QuotaVisualDesignTests.cs`.

**Start state:** Existing resources/popup work without an automatic theme controller.

**End state:** Exact dictionary parity, high-contrast precedence, dispatcher-safe/coalesced changes, popup-open recovery, and prior-resource retention on failure.

**Dependencies:** Unit 2 host lifecycle and WPF resource/dispatcher seams.

**Exclusions:** layout, placement, DWM, tray formatter, YASB, theme selector.

- [ ] **3.1 RED — dictionary parity:** Assert exact semantic and migrated style key sets across light/dark/high contrast. <!-- sdd-owner: implementation -->
- [ ] **3.2 RED — source/recovery:** Light, dark, high contrast, invalid/missing registry, runtime/off-dispatcher/missed changes, coalescing, disposal, load failure. <!-- sdd-owner: implementation -->
- [ ] **3.3 RED — accessibility invariants:** Preserve status text, focus, automation, percentage/reset readability, and window identity across transitions. <!-- sdd-owner: implementation -->
- [ ] **3.4 GREEN — resources/source/controller:** Add matching dictionaries, injectable source, stable merged-dictionary replacement, dispatcher marshalling, popup-open reevaluation, and high-contrast-first behavior. <!-- sdd-owner: implementation -->
- [ ] **3.5 CHECK — verify Unit 3:** Run focused fake-source/resource/dispatcher tests and inspect parity, precedence, recovery, identity. <!-- sdd-owner: implementation -->
- [ ] **3.6 COMMIT BOUNDARY — themes:** Freeze one theme commit; rollback restores prior resource merge. <!-- sdd-owner: implementation -->

**Hard split trigger:** At 380 keep non-palette shared styles in `App.xaml`; do not add popup/DWM work or cross 400.

## Unit 4a — Popup hierarchy, accessibility, permanent slots, and tray flow

**Forecast:** 340–380. **Margin:** 20. **Risk:** High.

**Expected files:** `src/AIBar.Desktop/MainWindow.xaml`, `MainWindow.xaml.cs`, `HostRuntime.cs`, `tests/AIBar.Domain.Tests/QuotaVisualDesignTests.cs`, `HostRuntimeTests.cs`.

**Start state:** Unit 3 provides semantic resources; popup/tray behavior is otherwise intact.

**End state:** Fixed header/quota/footer, secondary-only scrolling, exact accessible semantics, and permanent 5h/7d tray status through existing presentation flow.

**Dependencies:** Unit 3 plus truthful state/source semantics from contract/authority.

**Exclusions:** placement/DPI/DWM/render harness, YASB, analytics redesign, WinUI.

- [ ] **4a.1 RED — structure/permanent slots:** Both cards always exist in order with value or `--`, nullable reset text, state/age; fixed regions outside the only secondary ScrollViewer. <!-- sdd-owner: implementation -->
- [ ] **4a.2 RED — accessibility:** Exact window/summary/card names, descriptions, read/tab order, visible focus, non-focusable progress, scroll-into-view, non-color status. <!-- sdd-owner: implementation -->
- [ ] **4a.3 RED — tray formatter:** Both/partial/no-cache/loading/stale/unavailable/disabled/future-source/length cases; permanent `5h`/`7d`, literal state, truthful age, no private/detail fields. <!-- sdd-owner: implementation -->
- [ ] **4a.4 RED — production tray flow:** Constructor initial state and filtered later `PropertyChanged` updates pass `BetaPresentationState` through `ITrayRuntime.SetPresentation` to the unchanged tray item. <!-- sdd-owner: implementation -->
- [ ] **4a.5 GREEN — quota-first layout and tray:** Implement fixed regions, secondary analytics viewport, accessibility, formatter, and exact host wiring without authority logic or icon behavior changes. <!-- sdd-owner: implementation -->
- [ ] **4a.6 CHECK — verify Unit 4a:** Run focused structural/host/tray tests with synthetic state; inspect tree, order, placeholders, age, and exact wiring. <!-- sdd-owner: implementation -->
- [ ] **4a.7 COMMIT BOUNDARY — popup/tray:** Freeze one hierarchy/accessibility/tray commit; rollback preserves prior units. <!-- sdd-owner: implementation -->

**Hard split trigger:** At 380 defer placement/render breadth to 4b; never add DWM/YASB or cross 400.

## Unit 4b — Placement, DPI, DWM fallback, and synthetic rendered validation

**Forecast:** 300–370. **Margin:** 30. **Risk:** High.

**Expected files:** `src/AIBar.Desktop/WindowsTheme.cs`, `HostRuntime.cs`, measurement-only `MainWindow.xaml`, `tests/AIBar.Domain.Tests/PopupRenderedTests.cs`, `HostPrimitivesTests.cs`, `QuotaVisualDesignTests.cs`.

**Start state:** 4a fixed regions and tray semantics are complete.

**End state:** Deterministic clamped tray/work-area/DPI placement, progressive DWM, opaque fallback, and synthetic render proof.

**Dependencies:** Units 2–4a and pure placement/WPF lifecycle seams.

**Exclusions:** YASB/live app, network, WinUI, analytics redesign.

- [ ] **4b.1 RED — placement matrix:** Every taskbar edge, negative monitor, narrow/invalid geometry, cursor/primary fallback, 100/125/150/200% DPI, adjacency, clamp, fixed-region minimum, no discovery. <!-- sdd-owner: implementation -->
- [ ] **4b.2 RED — rendered/focus matrix:** Synthetic measure/arrange/render proves fixed regions visible, no clipping, secondary-only scroll, focus/automation/tab order, and theme preservation. <!-- sdd-owner: implementation -->
- [ ] **4b.3 RED — DWM capability matrix:** High contrast, supported/partial/rejected/remote cases with independent failures and complete opaque fallback. <!-- sdd-owner: implementation -->
- [ ] **4b.4 GREEN — placement/surface:** Add work-area/DPI/anchor seam, recompute each show, safe fallback, capability-checked hints, and opaque base. <!-- sdd-owner: implementation -->
- [ ] **4b.5 CHECK — verify Unit 4b:** Run pure placement and bounded synthetic Windows render tests; inspect clipping, order, DPI, and fallback. <!-- sdd-owner: implementation -->
- [ ] **4b.6 COMMIT BOUNDARY — placement/render:** Freeze one placement/DWM/render-validation commit; rollback retains 4a structure. <!-- sdd-owner: implementation -->

**Hard split trigger:** At 370 table-drive required breadth or replan before 400; do not move 4a/YASB scope.

## Unit 5 — Stock YASB reader, launcher, config, CSS, and fixtures

**Forecast:** 320–380. **Margin:** 20. **Risk:** High.

**Expected files:** `yasb/read-aibar-quota.ps1`, `show-aibar.cmd`, `custom-widget.example.yaml`, `custom-widget.example.css`, synthetic `yasb/fixtures/*`, `tests/AIBar.Domain.Tests/YasbAdapterTests.cs`.

**Start state:** Export, privacy, path, activation, and presentation contracts are complete; no adapter assets exist.

**End state:** Fixed-path bounded reader emits only label/tooltip/className with permanent slots and safe failures; left-click passes only `--show`; stock config/CSS is exact and path-neutral.

**Dependencies:** 1a-contract/authority/privacy, 1b, Unit 2, and 4b in the chain.

**Exclusions:** native/forked YASB, refresh/auth/network, process/path discovery, arbitrary parameters, cleanup/ZIP/docs.

- [ ] **5.1 RED — fixed private seam:** Private fixture function only; empty production params; fixed LocalApplicationData path; one UTC now; argument rejection before access; no exported helper. <!-- sdd-owner: implementation -->
- [ ] **5.2 RED — fixture matrix:** Current/refreshing/stale/unavailable/disabled, both/partial windows, nullable reset, malformed/missing/empty/oversized, schema/field/enum/value/time failures, due reset, clock rollback, every warning. <!-- sdd-owner: implementation -->
- [ ] **5.3 RED — output/poll boundary:** Exact three fields, permanent slots, warning/state/age/reset text, no CSS-only meaning, no write/delete/refresh/auth/start/network. <!-- sdd-owner: implementation -->
- [ ] **5.4 RED — stock config/launcher:** Exact CustomWidget keys, 60000 interval, `<AIBarRoot>`, no right/middle callback, relative executable, literal single `--show`, no `%*` or discovery. <!-- sdd-owner: implementation -->
- [ ] **5.5 GREEN — reader/assets:** Implement 16 KiB bounded closed-schema reader, safe formatter, relative launcher, exact YAML and customizable CSS classes. <!-- sdd-owner: implementation -->
- [ ] **5.6 CHECK — verify Unit 5:** Run isolated Pester/function and static asset checks only; never start AIBar/YASB. <!-- sdd-owner: implementation -->
- [ ] **5.7 COMMIT BOUNDARY — YASB assets:** Freeze one adapter commit; installed rollback later requires Unit 6 cleanup. <!-- sdd-owner: implementation -->

**Hard split trigger:** At 380 table-drive fixtures or replan; retain all security/path cases, expose no production path parameter, never cross 400.

## Unit 6 — ZIP inventory, documentation, fixed rollback cleanup, and distribution verification

**Forecast:** 260–330. **Margin:** 70. **Risk:** Medium/High.

**Expected files:** `yasb/remove-aibar-quota.ps1`, `yasb/README.txt`, `scripts/Publish-Deterministic.ps1`, `docs/private-beta.md`, `tests/AIBar.Domain.Tests/PrivateBetaDistributionTests.cs`, cleanup additions in `YasbAdapterTests.cs`.

**Start state:** Unit 5 assets are complete; unsigned self-contained Windows x64 manual ZIP model remains unchanged.

**End state:** Inventory contains bounded assets/docs, examples are path-neutral, and rollback stops unless the fixed snapshot is absent or disabled/null after read-back.

**Dependencies:** Unit 5 assets, 1a-contract schema, 1b path/atomic boundary, existing deterministic publish flow.

**Exclusions:** installer/signing/updater/package manager/public release/native YASB/live smoke.

- [ ] **6.1 RED — cleanup seam:** Valued deletion, denied-delete disabled replacement, nullable reset-safe schema, reparse/replacement/read-back failure, absent no-op, temp cleanup, fixed path, empty params, argument rejection. <!-- sdd-owner: implementation -->
- [ ] **6.2 RED — inventory/docs:** Exact ZIP assets and bytes, path-neutral examples, unsigned/self-contained/Windows x64/manual/private-beta wording, no unsupported delivery claims. <!-- sdd-owner: implementation -->
- [ ] **6.3 RED — rollback order:** Stop AIBar; run fixed no-argument cleanup; continue only on exit 0; remove/replace assets; verify absent-or-disabled/null; failure stops removal and changes no consent/cache/credential/db. <!-- sdd-owner: implementation -->
- [ ] **6.4 GREEN — cleanup/package/docs:** Implement fixed cleanup and read-back, deterministic inventory inclusion, `<AIBarRoot>` setup, unsupported-service caveat, upgrade stop rule, and exact rollback order. <!-- sdd-owner: implementation -->
- [ ] **6.5 CHECK — verify Unit 6:** Run isolated cleanup and deterministic inventory tests; inspect ZIP/manifest/docs; no release/sign/install/live process/network. <!-- sdd-owner: implementation -->
- [ ] **6.6 COMMIT BOUNDARY — distribution:** Freeze one docs/package/cleanup commit; rollback requires successful still-installed cleanup first. <!-- sdd-owner: implementation -->

**Hard split trigger:** At 330 keep cleanup, absent-or-disabled proof, and rollback ordering together; replan packaging breadth before 400.

## Cross-unit traceability

| Required behavior | Owning tasks |
|---|---|
| Numeric schemaVersion and exact root/window allowlists | 1a-contract.1, .6–.8 |
| Forbidden root/db/source-model/private fields | 1a-contract.2, .7–.8 |
| State/failure/missing-unusable credential/cache/weekly-only matrices | 1a-contract.3, .7–.8 |
| Exact source timestamp, interior percentage, nullable reset | 1a-contract.4, .6–.8 |
| Inconsistent direct document and unknown enum fail closed, no throw | 1a-contract.5, .7–.8 |
| Serialized sequence allocation and delivery | 1a-authority.1, .3, .6–.8 |
| Retrieval generation after accepted/persisted real retrieval | 1a-authority.2, .7–.8 |
| Retrieval racing clear/cancellation | 1a-authority.4, .7–.8 |
| Same-reference/concurrent sequencing and StateChanged compatibility | 1a-authority.1, .3, .5–.8 |
| Freshness/latest-state publisher and privacy cancellation | 1a-privacy.1–.9 |
| Atomic/ACL/reparse/current-user/clear/composition | 1b.1–.8 |
| Exact payload-free `--show` | 2.1–2.6, 5.4–5.5 |
| Automatic light/dark/high-contrast | 3.1–3.6 |
| Permanent accessible popup/tray slots | 4a.1–4a.7 |
| DPI/work-area/DWM/render fallback | 4b.1–4b.6 |
| Fixed-path stock YASB safe reader | 5.1–5.7 |
| ZIP inventory and safe rollback cleanup | 6.1–6.6 |

## Parent-owned lifecycle actions

- [x] Maintainer selected `feature-branch-chain`; no `size:exception` is authorized. <!-- sdd-owner: parent -->
- [ ] Maintainer decides disposition of the preserved 223-line candidate and explicitly approves Unit 1a-contract apply. <!-- sdd-owner: parent -->
- [ ] After each applied unit, use the repository-owned bounded validation/review policy for that exact below-400 unit. <!-- sdd-owner: parent -->
- [ ] Before archive, confirm all ten units and final cleanup/inventory evidence without fabricating completion from prior target counts. <!-- sdd-owner: parent -->

## Global exclusions

Cost, spend, trend, forecasting, ETA, native/forked YASB, duplicate analytics, WinUI, installer, signing, updater, package manager, public release, new credential/session/database/log/analytics/environment access, endpoint behavior, refresh cadence, general IPC, HTTP, sockets, named pipes, URL protocols, process discovery, arbitrary command execution, and real-data/live-process validation remain outside every unit.
