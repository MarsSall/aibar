# Private Codex Consent Specification

## Purpose

Define explicit, per-Windows-user consent for private Codex access while keeping credential material outside AIBar persistence and presentation.

## Requirements

### Requirement: Consent is default-off and revocable

The system MUST start private Codex access disabled, MUST disclose that enabling it permits private access, and MUST persist only the user's consent decision. The user MUST be able to revoke consent without migration.

#### Scenario: Fresh user starts disabled

- GIVEN no consent decision exists for the Windows user
- WHEN AIBar starts
- THEN private access is disabled and no private refresh is attempted
- AND the UI identifies the integration as disabled until explicit consent

#### Scenario: User enables access

- GIVEN private access is disabled
- WHEN the user explicitly accepts the disclosed consent
- THEN consent is persisted for that Windows user and private access may begin
- AND no credential value is persisted

#### Scenario: User revokes consent

- GIVEN private access is enabled
- WHEN the user disables it
- THEN consent is persisted as disabled, future private access stops, and no new refresh is started

### Requirement: Credential status is secret-free

The system MUST report whether the required credential material is available, missing, or unusable without storing, displaying, logging, or returning secret values. Credential lookup MUST occur only when consent is enabled.

#### Scenario: Credential is available

- GIVEN consent is enabled and the credential source can be used
- WHEN credential status is requested
- THEN the result reports availability without containing credential material

#### Scenario: Credential is missing or unusable

- GIVEN consent is enabled and lookup finds no usable credential
- WHEN credential status is requested
- THEN the result reports the safe non-secret condition and private refresh is not attempted

#### Scenario: Consent is disabled

- GIVEN consent is disabled
- WHEN status is requested
- THEN the result reports private access as disabled without inspecting or persisting credential material

### Requirement: Disable and exit are clean

The system MUST stop private refresh work when consent is revoked and MUST stop private access work when AIBar exits. It MUST NOT leave an active private-access operation that can continue after disable or exit.

#### Scenario: Revocation interrupts pending access

- GIVEN a private operation is pending
- WHEN the user revokes consent
- THEN the operation is stopped or safely ignored and no private result is committed as enabled access

#### Scenario: Application exits

- GIVEN private access or a local operation is active
- WHEN AIBar exits
- THEN active work is stopped or safely abandoned and no background private access remains

## Acceptance Criteria

- Fresh users are disabled by default and only consent is persisted.
- Credential status never exposes or stores secret material.
- Revocation and exit leave no continuing private-access operation.
