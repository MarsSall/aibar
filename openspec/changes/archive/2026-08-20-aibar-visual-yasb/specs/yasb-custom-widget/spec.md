# YASB CustomWidget Specification

## Purpose

Define a stock YASB `yasb.custom.CustomWidget` integration that displays the sanitized AIBar projection without becoming a second quota authority or accessing private AIBar data.

## Requirements

### Requirement: Reader consumes only the known sanitized projection

The YASB adapter MUST read only the documented sanitized AIBar JSON file and MUST validate the supported schema before producing widget output. It MUST NOT read AIBar databases, credentials, sessions, logs, prompts, analytics, environment-derived roots, executable paths, or arbitrary files, and MUST NOT recursively discover paths or processes.

#### Scenario: Valid schema-v1 input

- GIVEN the documented file contains a valid schema-v1 projection
- WHEN the reader is polled
- THEN it emits only the safe fields needed for label, tooltip, and state display
- AND it performs no private data access

#### Scenario: Forbidden access boundary

- GIVEN private databases, credentials, logs, sessions, or analytics are present on the machine
- WHEN the reader is polled
- THEN it does not open, enumerate, or infer those sources
- AND its output remains based only on the known sanitized file

### Requirement: Both quota windows are permanent in the label

The widget label MUST include both the five-hour and weekly quota labels on every successful reader response. A missing or invalid window MUST remain visible as a `--` or equivalent safe placeholder rather than being omitted.

#### Scenario: Both windows are present

- GIVEN valid five-hour and weekly percentages exist
- WHEN the widget formats its primary label
- THEN the label contains both windows and their values

#### Scenario: One or both windows are missing

- GIVEN one or both quota windows are null or unavailable
- WHEN the widget formats its primary label
- THEN both quota labels remain present
- AND each missing value is shown as a placeholder

### Requirement: Widget text and tooltip expose truthful state and age

The widget MUST expose current, refreshing, stale, unavailable, and disabled meaning through text, an icon, or tooltip content and MUST NOT rely on CSS color alone. For a valid schema-v1 projection, `warning` MUST be `null` (no warning), `refresh-failed`, `authentication-failed`, `unavailable`, or `disabled`, matching the sanitized export contract. Any non-null exported warning MUST be represented in visible widget text and in tooltip content; neither representation may rely on CSS color alone. Tooltip content MUST include both windows' reset information when available, state, warning when non-null, and freshness based on `sourceRetrievedAt`; it MUST NOT expose raw parse errors or private details.

#### Scenario: Stale cached input

- GIVEN a valid projection has retained values with state `stale`
- WHEN the widget renders
- THEN values remain visible
- AND label or tooltip visibly identifies stale/degraded state
- AND the displayed age is derived from `sourceRetrievedAt`, not file modification time

#### Scenario: Non-null warning is visible and available in the tooltip

- GIVEN a valid schema-v1 projection contains any non-null warning from the closed domain `refresh-failed`, `authentication-failed`, `unavailable`, or `disabled`
- WHEN the widget renders
- THEN the warning is represented in visible widget text
- AND the same warning is represented in the tooltip
- AND neither representation depends on color alone or exposes raw error details

#### Scenario: Unavailable input

- GIVEN the projection has no safe values or is disabled
- WHEN the widget renders
- THEN both labels show placeholders
- AND the tooltip identifies unavailable or disabled state without raw error content

#### Scenario: Reset time has passed

- GIVEN a valid reset timestamp is due or in the past
- WHEN the widget formats reset information
- THEN it shows a due or zero countdown rather than a negative countdown
- AND it does not infer a new quota value

### Requirement: Reader failures fail closed with safe placeholders

Missing files, empty files, malformed JSON, unsupported or future schema versions, invalid timestamps, future-inconsistent timestamps, clock rollback, and unsupported values MUST produce both quota placeholders and a clear safe unavailable or stale tooltip. Reader failures MUST NOT erase or modify the last complete AIBar projection and MUST NOT disclose parser, path, exception, or credential details.

#### Scenario: Malformed or partial input

- GIVEN the file is missing, malformed, empty, or incomplete
- WHEN the reader is polled
- THEN the widget shows both placeholders and a safe unavailable indication
- AND the reader does not rewrite or delete the file

#### Scenario: Unsupported schema

- GIVEN the file declares a schema version the reader does not support
- WHEN the reader is polled
- THEN the widget shows both placeholders and an unsupported/unavailable indication
- AND no unknown fields are interpreted as quota data

#### Scenario: Future time or clock rollback

- GIVEN a timestamp is future-dated beyond the reader's accepted clock policy or produces a negative age
- WHEN the reader evaluates freshness
- THEN it avoids presenting the source as fresh
- AND it shows safe placeholders or a stale/invalid indication

### Requirement: Polling is display-only

The widget polling interval MUST read local sanitized state only and MUST NOT trigger quota refreshes, authentication, endpoint calls, or AIBar startup. The interval MUST be configurable within the stock CustomWidget contract.

#### Scenario: Repeated polling

- GIVEN the widget polls more than once while AIBar is running or stopped
- WHEN each interval callback executes
- THEN it performs only a read and format operation
- AND it does not request a quota refresh or authentication

### Requirement: Left click uses the fixed show launcher

The widget's left-click callback MUST invoke the documented stable local AIBar launcher with only the `--show` intent. It MUST NOT pass arbitrary user or widget payloads, discover executable paths or processes, or expose a second popup implementation.

#### Scenario: AIBar is stopped

- GIVEN the documented launcher resolves the extracted AIBar executable and AIBar is not running
- WHEN the user left-clicks the widget
- THEN the launcher passes only `--show`
- AND AIBar starts and shows its popup according to the activation contract

#### Scenario: AIBar is already running

- GIVEN AIBar is already running
- WHEN the user left-clicks the widget
- THEN the launcher passes only `--show`
- AND the existing primary instance shows its popup without a second instance

#### Scenario: Launcher cannot be resolved

- GIVEN the user has not configured the documented launcher location or the executable was moved
- WHEN the user left-clicks the widget
- THEN the callback reports a safe unavailable result
- AND it does not scan for a replacement process or path

### Requirement: Stock assets remain user-configurable

The integration MUST use stock `yasb.custom.CustomWidget` capabilities for command output, formatting, tooltip, CSS class, polling interval, and mouse callbacks. It MUST supply a sample configuration and CSS example that users can modify, and AIBar MUST NOT overwrite the user's YASB stylesheet or require native or forked YASB code.

#### Scenario: User customizes the sample

- GIVEN a user changes the sample label formatting, interval, or CSS class
- WHEN YASB loads the configuration
- THEN the widget remains compatible with the stock CustomWidget contract
- AND AIBar does not replace the user's styling

### Requirement: YASB validation is synthetic and isolated

Reader and formatting validation MUST use synthetic JSON fixtures and isolated local files. It MUST NOT start a live YASB process, a live AIBar session, or a private endpoint request, and MUST NOT use real credentials, sessions, user datasets, or installed private databases.

#### Scenario: Fixture matrix

- GIVEN fixtures cover current, refreshing, stale, unavailable, disabled, one-window, malformed, missing, unsupported-version, future-time, and clock-rollback inputs
- WHEN adapter validation runs
- THEN it verifies labels, tooltips, placeholders, and safe errors using only synthetic fixtures
- AND no live application or private data is accessed

## Explicit Scope Boundaries

This capability MUST NOT add a native or forked YASB widget, duplicate AIBar analytics in YASB, change private quota endpoint behavior, add credential or session access, or imply installer, signing, updater, package-manager, or public-release support.

## Acceptance Criteria

- Stock CustomWidget output always includes both quota labels, safe placeholders, truthful state, and source age.
- Polling reads only the sanitized file and never triggers refresh, authentication, startup, or private access.
- Left click passes only `--show`, and malformed or unsupported fixtures fail closed without raw error disclosure.
