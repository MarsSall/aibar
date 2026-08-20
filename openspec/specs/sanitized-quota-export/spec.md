# Sanitized Quota Export Specification

## Purpose

Define the bounded, current-user projection that AIBar publishes for external display without exposing quota authority, credentials, private responses, or internal data.

## Requirements

### Requirement: Export schema is versioned and closed

AIBar MUST publish a schema-v1 document whose complete allowlist is exactly `schemaVersion`, `generatedAt`, `state`, `warning`, `sourceRetrievedAt`, `fiveHour`, and `weekly`. `fiveHour` and `weekly` MUST always be present and MUST be either `null` or an object containing only `percentageUsed` and `resetAt`. No additional top-level or window fields are permitted.

`schemaVersion` MUST be `1`. `state` MUST be one of `current`, `refreshing`, `stale`, `unavailable`, or `disabled`. `warning` MUST be exactly `null` (no warning), `refresh-failed`, `authentication-failed`, `unavailable`, or `disabled`. These warning values are bounded generic presentation values and MUST NOT contain credentials, endpoint payloads, safe codes, paths, account information, or raw errors. Timestamps MUST be UTC timestamps in the accepted format. `percentageUsed` MUST be finite and within 0 through 100 inclusive. Any unsupported warning or other invalid, unsupported, or non-finite value MUST fail closed to an unavailable safe projection; the unsupported input MUST NOT be serialized, and the fallback MUST use only the bounded safe values.

#### Scenario: Valid schema-v1 projection

- GIVEN authoritative state contains valid five-hour and weekly values
- WHEN AIBar creates an external projection
- THEN the document contains only the allowlisted names
- AND the schema version is `1`
- AND each percentage is within the inclusive 0-to-100 range

#### Scenario: Forbidden field is present in source state

- GIVEN authoritative state or an internal error contains a token, account identifier, plan, endpoint, path, response, or diagnostic field
- WHEN AIBar creates the external projection
- THEN none of those fields appear in the document
- AND the projection remains valid against the closed allowlist

#### Scenario: Invalid value reaches the projection boundary

- GIVEN a percentage is non-finite or outside the permitted range, a timestamp is invalid, or a warning is outside the closed warning domain
- WHEN the projection is produced
- THEN the invalid or unsupported input is not exported
- AND the resulting document is an unavailable safe projection using only the bounded allowlist values rather than partially disclosing invalid data

### Requirement: Publication timestamps describe publication and source age separately

`generatedAt` MUST identify the publication time of the document. `sourceRetrievedAt` MUST identify the last successful retrieval time for the safe quota values and MUST remain unchanged when those values are retained during refresh or failure. A newer `generatedAt` MUST NOT make cached source data appear fresh.

#### Scenario: Cached values are republished

- GIVEN safe values were retrieved at an earlier time
- WHEN a refresh fails and AIBar republishes the retained values
- THEN `generatedAt` reflects the new publication
- AND `sourceRetrievedAt` remains the earlier retrieval time
- AND consumers can calculate the stale age from `sourceRetrievedAt`

#### Scenario: No safe source value exists

- GIVEN no successful safe quota value exists
- WHEN an unavailable document is published
- THEN both windows are `null`
- AND no source timestamp claims that unavailable data was retrieved

### Requirement: Export state and window nullability are truthful

The projection MUST use `current` only for a current safe result, `refreshing` when an active refresh is in progress, `stale` for retained values after an operational failure, `unavailable` when no safe values are available, and `disabled` after an explicit privacy action. Missing windows MUST be `null`, not omitted and not represented by fabricated percentages.

#### Scenario: Refreshing with a safe cache

- GIVEN a safe snapshot exists and a refresh is in progress
- WHEN the projection is published
- THEN retained windows remain available
- AND `state` is `refreshing`
- AND the original `sourceRetrievedAt` is retained

#### Scenario: Operational failure with a safe cache

- GIVEN a safe snapshot exists
- WHEN refresh or authentication/service access fails operationally
- THEN retained windows remain available
- AND `state` is `stale`
- AND `warning` contains only a presentation-safe value

#### Scenario: Explicitly disabled integration

- GIVEN the user disables, revokes, or clears the relevant private data
- WHEN the projection is next published
- THEN `state` is `disabled`
- AND both windows and their source timestamp are `null`, or the file is absent until a disabled projection is recreated
- AND a prior cached value is not retained for external display

### Requirement: Publication is atomic and failure-safe

External readers MUST observe either the previous complete document or the new complete document, never a partial or mixed document. Before an atomic replace or rename, the temporary document MUST be created in the same directory as the destination. If publication fails before the new document is committed, the previous complete document MUST remain intact. A publication failure MUST NOT corrupt or replace the authoritative quota cache and MUST be surfaced only through safe application state.

#### Scenario: Successful replacement

- GIVEN a previous complete document exists
- WHEN a valid new document is published
- THEN a reader can parse either the complete previous document before replacement or the complete new document after replacement
- AND no intermediate partial document is observable

#### Scenario: Failure before replacement

- GIVEN a previous complete document exists
- WHEN writing or committing the new document fails
- THEN the previous document remains parseable and unchanged
- AND the authoritative quota cache remains usable

#### Scenario: Same-directory replacement exposes complete documents only

- GIVEN a previous complete document exists at the destination
- WHEN AIBar prepares a valid replacement
- THEN the temporary document is created in the destination's same directory before the atomic replace or rename
- AND readers observe either the previous complete document before replacement or the new complete document after replacement
- AND if temporary-file writing or replacement fails, the previous complete document remains parseable and unchanged

#### Scenario: First publication fails

- GIVEN no external document exists
- WHEN the first publication fails
- THEN no partial file is exposed
- AND a reader receives a missing or unavailable result rather than raw write details

### Requirement: Export is current-user scoped and non-elevated

The projection MUST be stored and published for the current Windows user without requiring elevation or machine-wide write authority. The external reader MUST receive read-only access to the known projection and MUST NOT be granted authority to write, delete, or alter AIBar state.

#### Scenario: Current-user publication

- GIVEN AIBar runs as a non-elevated Windows user
- WHEN it publishes the projection
- THEN publication uses the documented current-user location
- AND it does not require administrator approval or machine-wide storage

#### Scenario: External reader access

- GIVEN a stock external widget reads the documented projection
- WHEN it polls the file
- THEN it can read the sanitized output only
- AND it cannot use the projection contract to write or mutate AIBar state

### Requirement: Privacy actions take precedence over stale retention

Explicit disable, revoke, or clear-data actions MUST immediately take precedence over operational stale-value retention. After such an action, AIBar MUST remove or null the externally visible values and MUST NOT republish the prior values until privacy controls explicitly permit new values.

#### Scenario: Clear overrides an in-flight failure

- GIVEN stale values are retained and a refresh failure is pending
- WHEN the user clears the relevant AIBar data
- THEN the external projection becomes disabled or absent with null placeholders
- AND the pending failure cannot restore the cleared values

#### Scenario: Re-enable after revoke

- GIVEN revoke or disable has produced a null or absent projection
- WHEN the user later re-enables private access
- THEN no prior value is restored from the external projection
- AND new values appear only after a new safe retrieval

### Requirement: Rollback cleanup removes obsolete export

Rolling back or removing the sanitized export or YASB integration MUST delete the exported snapshot or replace it with a `disabled` projection whose `fiveHour`, `weekly`, and `sourceRetrievedAt` values are `null`. Rollback or removal MUST NOT be considered complete while an old readable snapshot still discloses quota values, and this cleanup MUST preserve the precedence of explicit disable, revoke, and clear-data actions over stale-value retention.

#### Scenario: Integration rollback removes disclosed quota values

- GIVEN the integration has an exported document containing safe quota values
- WHEN the sanitized export or YASB integration is rolled back or removed
- THEN the exported location is absent or contains a valid `disabled` projection with both windows and `sourceRetrievedAt` set to `null`
- AND obsolete quota values are no longer readable from the exported location
- AND subsequent operational failures or stale-cache publication cannot restore those values without explicit re-enablement followed by a new safe retrieval

### Requirement: Validation uses synthetic data only

Projection and publication validation MUST use synthetic quota states and isolated temporary filesystem locations. Validation MUST NOT call a private endpoint, use real credentials, access a real user dataset, inspect a live AIBar session, start a live YASB process, or read session, log, analytics, database, environment, or credential data.

#### Scenario: Synthetic validation run

- GIVEN tests have synthetic current, refreshing, stale, unavailable, disabled, partial, malformed, and invalid states
- WHEN export validation runs
- THEN all assertions use synthetic objects and isolated local files
- AND no network, credential, session, user-data, or live-process access occurs

## Acceptance Criteria

- Schema-v1 output contains only the explicit allowlist and bounded values.
- Readers never observe partial publication, and failed publication preserves the previous complete document.
- Operational failure retains safe values with truthful source age, while privacy actions null or remove them.
- Export and writer validation uses synthetic state and isolated local files only.
