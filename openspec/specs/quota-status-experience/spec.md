# Quota Status Experience Specification

## Purpose

Define one truthful quota status for tray and popup views, including live refresh, cached fallback, reset timing, and safe failure states.

## Requirements

### Requirement: Live quota reports available windows

When consent and usable credentials permit access, the system MUST support a real refresh that independently reports quota for the five-hour window, weekly window, or both, including each available window's reset instant when supplied by the source. A successful live result MUST contain at least one recognized window and MUST be marked fresh. A card for an absent service window MUST be hidden and MUST NOT be fabricated.

#### Scenario: Live refresh succeeds

- GIVEN consent is enabled and credential lookup succeeds
- WHEN a quota refresh completes successfully
- THEN at least one recognized five-hour or weekly quota window is available as one fresh result
- AND tray and popup can render that same result

#### Scenario: Refresh is loading

- GIVEN a refresh has started and no new result is complete
- WHEN either surface is rendered
- THEN it reports a loading state and MUST NOT present the pending result as fresh

### Requirement: Cached quota remains truthful

The system MUST preserve the last safe quota snapshot for fallback, MUST show its age or freshness information, and MUST mark it cached and degraded when a live refresh fails. Cached data MUST NOT be presented as live.

#### Scenario: Offline refresh with cached data

- GIVEN a prior safe quota snapshot exists
- WHEN a refresh cannot reach the private endpoint
- THEN the last snapshot remains available with cached and degraded indicators
- AND the failure is distinguishable from a successful live refresh

#### Scenario: No snapshot exists

- GIVEN no safe quota snapshot exists
- WHEN a refresh fails or the credential is missing
- THEN the result is unavailable and no quota values are fabricated

#### Scenario: Missing credential with cached data

- GIVEN a safe quota snapshot exists but credential lookup reports missing
- WHEN refresh is requested
- THEN the snapshot remains available as cached and degraded
- AND the cause remains identifiable as missing credential without a private request

### Requirement: Failure states are distinct and safe

The system MUST distinguish loading, fresh live, cached degraded, unavailable, missing-credential, offline, and safe-error conditions whenever the cause is known. Errors MUST be redacted and MUST NOT expose credentials or private response contents.

#### Scenario: Missing credential differs from offline

- GIVEN the credential is missing, or alternatively the endpoint is unreachable with a usable credential
- WHEN status is classified
- THEN the result identifies missing-credential and offline as different conditions
- AND neither condition exposes secret material

#### Scenario: Safe error occurs

- GIVEN a refresh fails for an unclassified or unsafe-to-display reason
- WHEN the error is surfaced
- THEN the user sees a safe error state and any usable cached snapshot remains clearly degraded

### Requirement: Tray and popup share state and fresh countdowns

The tray and popup MUST render the same quota state and values from the same latest snapshot. Reset countdowns MUST be recalculated from reset instants and current time whenever displayed, including after a refresh or time advance; stale countdown text MUST NOT be treated as source data.

#### Scenario: Surfaces remain consistent

- GIVEN a quota state has changed
- WHEN tray and popup are opened or refreshed
- THEN both show the same state, five-hour values, weekly values, and freshness classification

#### Scenario: Reset countdown updates

- GIVEN a reset instant is in the latest result
- WHEN time advances or a newer result arrives
- THEN the displayed countdown reflects the current reset instant and current time

## Acceptance Criteria

- Live results contain at least one recognized five-hour or weekly window and are shared by tray and popup.
- Failed refreshes preserve only clearly cached, degraded snapshots.
- Missing credential, offline, unavailable, and safe-error states remain distinct.
