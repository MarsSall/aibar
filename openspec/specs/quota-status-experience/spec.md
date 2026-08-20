# Quota Status Experience Specification

## Purpose

Define one truthful quota status for tray and popup views, including live refresh, cached fallback, reset timing, and safe failure states.

## Requirements

### Requirement: Live quota reports available windows

When consent and usable credentials permit access, the system MUST support a real refresh that independently reports the five-hour window, the weekly window, or both, including each available window's reset instant when supplied by the source. A successful live result MUST be marked fresh. Tray and popup presentation MUST retain permanent slots for both windows; an absent window MUST be represented as unavailable rather than hidden or fabricated.
(Previously: An absent service window was hidden and a successful result needed only one recognized window.)

#### Scenario: Live refresh provides both windows

- GIVEN consent is enabled and credential lookup succeeds
- WHEN a quota refresh completes successfully with five-hour and weekly data
- THEN both permanent quota slots contain their corresponding values and reset information
- AND tray and popup render the same fresh result

#### Scenario: Live refresh provides one window

- GIVEN a refresh completes with only a recognized five-hour window
- WHEN either quota surface is rendered
- THEN the five-hour slot shows the supplied value
- AND the weekly slot remains visible as an explicit unavailable placeholder
- AND no weekly value is fabricated

#### Scenario: Refresh is loading

- GIVEN a refresh has started and no new result is complete
- WHEN either surface is rendered
- THEN both permanent slots remain present
- AND pending values MUST NOT be presented as fresh
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


### Requirement: Quota status preserves truthful cached windows

The system MUST preserve each last safe quota window independently during operational refresh failure, MUST retain its original source age, and MUST distinguish cached or degraded state from fresh state. A missing window MUST remain unavailable and MUST NOT be inferred from the other window.

#### Scenario: Refresh fails with cached data

- GIVEN a prior safe five-hour and weekly snapshot exists
- WHEN a refresh fails
- THEN both cached values remain available
- AND the result is visibly marked stale or degraded
- AND the source age is based on the prior successful retrieval time

#### Scenario: Refresh fails with a partial cache

- GIVEN only the five-hour window has a prior safe value
- WHEN a refresh fails
- THEN the five-hour value remains cached
- AND the weekly slot remains an explicit unavailable placeholder

#### Scenario: No snapshot exists

- GIVEN no safe quota snapshot exists
- WHEN a refresh fails or credentials are unavailable
- THEN both permanent slots are unavailable placeholders
- AND no quota value is fabricated

## Acceptance Criteria

- Live results contain at least one recognized five-hour or weekly window and are shared by tray and popup.
- Failed refreshes preserve only clearly cached, degraded snapshots.
- Missing credential, offline, unavailable, and safe-error states remain distinct.
