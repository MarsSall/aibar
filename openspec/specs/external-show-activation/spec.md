# External Show Activation Specification

## Purpose

Define the fixed, payload-free activation intent used by YASB and other local launchers to start AIBar or show the existing popup without weakening single-instance safety.

## Requirements

### Requirement: `--show` is the only external display intent

AIBar MUST recognize the fixed command-line intent `--show` without accepting a payload. The activation channel MUST NOT accept or forward arbitrary paths, URLs, commands, serialized state, or other arguments.

#### Scenario: Valid show intent

- GIVEN a local launcher invokes the AIBar executable with exactly `--show`
- WHEN AIBar processes the invocation
- THEN it requests one popup display
- AND no caller-supplied payload is parsed or forwarded

#### Scenario: Unknown arguments

- GIVEN an invocation contains an unknown argument or an arbitrary value
- WHEN AIBar processes the invocation
- THEN it fails safely or follows ordinary tray startup
- AND the argument is not interpreted as a file, URL, command, process target, or activation payload

### Requirement: Existing-instance show preserves single-instance safety

When AIBar is already running, a secondary `--show` invocation MUST use the existing single-instance activation mechanism, MUST signal the primary instance to show the popup, and MUST exit without creating a second popup authority or replacing the primary instance.

#### Scenario: Secondary instance requests show

- GIVEN a primary AIBar instance owns the single-instance authority
- WHEN a second AIBar process receives `--show`
- THEN the secondary process signals the primary through the existing activation path
- AND the secondary process exits
- AND the primary shows or activates its existing popup once

#### Scenario: Existing popup is visible

- GIVEN the primary popup is already visible
- WHEN a secondary `--show` invocation arrives
- THEN the primary remains the sole popup authority
- AND it brings the existing popup to the intended active state without creating a duplicate window

### Requirement: First-instance show waits for complete startup

When no AIBar instance is running, a `--show` invocation MUST complete the normal primary startup needed for tray, popup, and activation ownership before showing the popup. It MUST show the popup exactly once after startup is ready.

#### Scenario: First instance requests show

- GIVEN no AIBar instance is running
- WHEN AIBar starts with `--show`
- THEN normal primary startup completes
- AND the popup is shown once after the presentation host is ready
- AND single-instance ownership remains established

#### Scenario: Startup is not yet ready

- GIVEN a `--show` request is received while primary startup is still constructing the popup
- WHEN startup completes
- THEN the pending request is handled once
- AND it does not create duplicate windows or duplicate show operations

### Requirement: Activation remains local and bounded

The external show behavior MUST remain a local fixed-intent integration. It MUST NOT add a general IPC, HTTP, socket, named-pipe, URL, process-discovery, or command-execution interface, and it MUST NOT change quota endpoint behavior, refresh cadence, credentials, or session access.

#### Scenario: Launcher path is unavailable

- GIVEN a user-configured launcher cannot locate the documented AIBar executable
- WHEN the launcher is invoked
- THEN no process or path discovery is attempted
- AND the integration reports a safe unavailable result

#### Scenario: Safety regression check

- GIVEN tests exercise first-instance, secondary-instance, repeated, unknown-argument, and startup-race cases
- WHEN activation validation completes
- THEN the primary instance remains unique in every case
- AND no arbitrary activation payload reaches the primary instance

## Acceptance Criteria

- Exactly `--show` is supported as a payload-free display intent.
- First and secondary invocations show the existing popup once without creating a second authority.
- Unknown arguments remain safe and no general IPC or endpoint behavior is introduced.
