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

### Requirement: Scan state is safe and time-bounded by the application lifecycle

The system MUST expose scanning as loading until completion or failure, MUST retain the scan time attached to the returned result, and MUST stop or safely abandon local scanning when the user disables the related private-beta experience or exits AIBar.

#### Scenario: Scan completes after loading

- GIVEN a local scan is in progress
- WHEN it completes
- THEN the UI leaves loading and shows the result's totals, coverage, warning state, and scan time

#### Scenario: Exit during scan

- GIVEN a local scan is in progress
- WHEN AIBar exits
- THEN the scan stops or is safely abandoned and no partial result is promoted to complete coverage

## Acceptance Criteria

- Results contain only factual token/model totals and scan time from local data.
- Complete, partial, and unavailable coverage are explicit and testable.
- Partial or unavailable scans never appear complete and always warn.
