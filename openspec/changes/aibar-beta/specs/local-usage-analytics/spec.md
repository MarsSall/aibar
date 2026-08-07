# Local Usage Analytics Specification

## Purpose

Define factual, local-only usage summaries with explicit scan coverage so incomplete source data cannot appear complete.

## Requirements

### Requirement: Aggregates are factual and local

The system MUST scan the available local Codex CLI data and report only token totals and model totals supported by that data, together with the scan time. It MUST NOT infer cost, trends, ETA, forecasts, or other unsupported measurements.

#### Scenario: Complete local data is scanned

- GIVEN all supported local source data is readable
- WHEN an analytics scan completes
- THEN the result contains factual token totals, model totals, and scan time
- AND the result is identified as local data

#### Scenario: Source data contains no usable records

- GIVEN the local source contains no usable usage records
- WHEN a scan completes
- THEN the result does not invent totals and reports unavailable coverage

### Requirement: Coverage is explicit

Every analytics result MUST identify coverage as complete, partial, or unavailable and MUST provide a warning when coverage is partial or unavailable. Partial results MUST NOT be labeled complete.

#### Scenario: Partial scan

- GIVEN some supported local sources are readable and another source cannot be read
- WHEN the scan completes
- THEN factual aggregates from readable data are returned with partial coverage
- AND the result includes a coverage warning naming the incomplete condition without exposing secrets

#### Scenario: Unavailable scan

- GIVEN supported local data cannot be inspected
- WHEN the scan is requested
- THEN the result reports unavailable coverage, provides no fabricated totals, and surfaces a safe warning

### Requirement: Scan shutdown is exclusively owned, bounded, and safe

The system MUST expose scanning as loading until completion or failure and MUST retain the scan time attached to the returned result. It MUST stop or safely abandon local scanning when the related private-beta experience is disabled. One exclusive lifecycle owner MUST handle application shutdown, issue cancellation once, and await the scan once within a defined bound. A cooperative completion MUST dispose dependent analytics resources. If cancellation or the single await times out or fails, late publication MUST be suppressed; the owner MUST NOT re-await or dispose resources still potentially used by the active scan, MUST retain those process-owned resources until process exit, MUST continue disposing independent resources, and MUST surface a safe typed shutdown outcome without masking the first failure.

#### Scenario: Scan completes after loading

- GIVEN a local scan is in progress
- WHEN it completes
- THEN the UI leaves loading and shows the result's totals, coverage, warning state, and scan time

#### Scenario: Exit during scan

- GIVEN a local scan is in progress
- WHEN AIBar exits
- THEN the exclusive lifecycle owner performs one bounded cancellation-and-await attempt and no partial result is promoted to complete coverage

#### Scenario: Private-beta experience is disabled during scan

- GIVEN a local scan is in progress for the related private-beta experience
- WHEN that experience is disabled
- THEN the scan stops or is safely abandoned and no partial result is promoted to complete coverage

#### Scenario: Cooperative scan completes during shutdown

- GIVEN the production analytics composite has an active scan and dependent analytics resources
- WHEN the exclusive lifecycle owner requests application exit and the scan cooperatively completes within the bound
- THEN cancellation occurs once, the scan is awaited once, late publication is suppressed, and dependent resources are disposed after completion

#### Scenario: Non-cooperative scan exceeds the shutdown bound

- GIVEN the production analytics composite has an active scan that may still use process-owned resources and independent resources also exist
- WHEN cancellation is issued and the single await times out or fails
- THEN late publication is suppressed, the owner does not re-await or dispose potentially used resources, and those process-owned resources remain until process exit
- AND independent resources are disposed and a safe typed shutdown outcome surfaces without masking the first failure

### Requirement: Shutdown coverage uses the production path

Shutdown behavior MUST be covered through the real production analytics composite and the application's actual exit path. A test-only lifecycle surrogate MUST NOT satisfy this coverage requirement.

#### Scenario: Application-exit coverage exercises both outcomes

- GIVEN the application constructs its production analytics composite and its real exit path is invoked
- WHEN coverage exercises both cooperative completion and non-cooperative timeout or failure
- THEN the observed cancellation, await, publication, disposal, and typed-outcome behavior is testable against the shutdown requirements
- AND replacing the production composite or exit path with a test-only surrogate does not count as passing coverage

## Acceptance Criteria

- Results contain only factual token/model totals and scan time from local data.
- Complete, partial, and unavailable coverage are explicit and testable.
- Partial or unavailable scans never appear complete and always warn.
- Cooperative and non-cooperative shutdowns are covered through the production composite and application-exit path.
