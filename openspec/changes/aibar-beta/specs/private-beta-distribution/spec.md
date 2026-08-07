# Private Beta Distribution Specification

## Purpose

Define the manually distributed private-beta artifact and tester guidance without resuming packaging authority. The artifact MUST be produced from exact committed parent `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`.

## Requirements

### Requirement: Distribution is an unsigned self-contained Windows x64 ZIP

The release candidate MUST be a manually distributed ZIP for Windows x64, MUST be self-contained so testers do not need a separately installed .NET runtime, MUST be explicitly labeled unsigned and private beta, and MUST be produced from exact committed parent `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`.

#### Scenario: Tester receives the artifact

- GIVEN the published beta ZIP and a Windows x64 tester machine
- WHEN the tester extracts it without .NET installed
- THEN the application can be launched from the extracted files
- AND the instructions warn that the artifact is unsigned and private beta

#### Scenario: Artifact is inspected

- GIVEN a tester examines the ZIP metadata and contents
- WHEN platform and runtime requirements are checked
- THEN the artifact identifies Windows x64 and contains the files required for self-contained launch
- AND it makes no signing or public-release claim

#### Scenario: Produced ZIP is smoke-tested

- GIVEN a Windows x64 self-contained beta ZIP has been produced from parent commit `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`
- WHEN the ZIP is extracted and launched on a Windows x64 machine without .NET installed
- THEN the application starts successfully from the extracted files
- AND the ZIP is eligible for manual distribution only after this smoke test passes

### Requirement: Checksums and versions are verifiable

The distribution MUST provide a checksum for the exact ZIP and a version identifier that matches the artifact and tester instructions. The checksum instructions MUST allow a tester to verify the downloaded bytes before launch.

#### Scenario: Checksum matches

- GIVEN a tester downloads the ZIP and the published checksum
- WHEN the tester computes the documented checksum
- THEN it matches the published value for the exact archive

#### Scenario: Version is checked

- GIVEN the tester follows the version instructions
- WHEN the artifact version is compared with the documented beta version
- THEN the values match and the tester can identify the build being exercised

### Requirement: Tester instructions cover safe manual use

Instructions MUST cover unsigned-warning handling, launch from the extracted directory, manual replacement of an older beta, version verification, checksum verification, and the private-beta testing context.

#### Scenario: Tester replaces a prior beta

- GIVEN an older beta installation exists
- WHEN the tester follows the replacement instructions
- THEN the older beta can be replaced manually without implying automatic update or uninstall support

## Explicit Deferrals

This capability MUST NOT authorize packaging authority, MSIX or installer work, code signing, automatic update or uninstall, `aibar-foundation` packaging completion, SBOM or compliance claims, public-release claims, cost/trend/ETA polish, forecasting, or store distribution.

## Acceptance Criteria

- A Windows x64 tester can launch the self-contained unsigned ZIP without .NET installed.
- Version and checksum instructions verify the exact downloaded archive.
- Manual launch and replacement guidance is present, with all deferred work excluded.
