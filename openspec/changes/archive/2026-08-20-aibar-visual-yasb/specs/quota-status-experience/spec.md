# Delta for Quota Status Experience

## MODIFIED Requirements

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

## ADDED Requirements

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

- Both five-hour and weekly slots are always represented on the quota surfaces.
- Cached values retain source age and never appear fresh solely because a surface was rendered.
- Missing windows remain explicit and truthful placeholders.
