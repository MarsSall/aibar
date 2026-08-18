# Delta for Private Beta Distribution

## ADDED Requirements

### Requirement: Distribution inventory includes the stock YASB integration

The existing unsigned, self-contained Windows x64 ZIP inventory MUST include the bounded YASB reader, sample `yasb.custom.CustomWidget` configuration, CSS example, launcher/setup guidance, and any required fixtures or documentation designated for distribution. The inventory MUST identify these assets without embedding developer-checkout or user-specific absolute paths.

#### Scenario: ZIP contains integration assets

- GIVEN the existing ZIP is built for the change
- WHEN its contents are compared with the documented inventory
- THEN the reader, sample configuration, CSS example, and setup documentation are present
- AND the inventory matches the distributed bytes

#### Scenario: Examples are path-neutral

- GIVEN a user reads the supplied YASB configuration and launcher examples
- WHEN the examples are inspected
- THEN they contain no developer-specific or user-specific absolute path
- AND they direct the user to configure one stable extracted location or equivalent documented convention

### Requirement: Distribution remains a bounded manual ZIP capability

Adding YASB assets MUST NOT expand distribution into installer, signing, updater, package-manager, native YASB packaging, or public-release behavior. The existing unsigned, self-contained Windows x64 ZIP model and its provenance, checksum, version, and manual-use requirements remain authoritative.

#### Scenario: Tester receives the updated ZIP

- GIVEN a tester receives the updated private-beta ZIP
- WHEN the tester verifies its platform and distribution guidance
- THEN it remains explicitly unsigned, self-contained, Windows x64, and manually extracted
- AND the YASB assets are described as optional stock-widget integration assets
- AND no installer, signing, updater, or public-support claim is made

#### Scenario: Out-of-scope packaging request appears

- GIVEN a request asks the change to add an installer, code signing, automatic updates, package-manager delivery, or a public release
- WHEN scope is evaluated
- THEN that work is rejected as outside this change
- AND the existing ZIP distribution behavior remains unchanged apart from the bounded asset inventory

## Acceptance Criteria

- The existing unsigned self-contained ZIP inventory contains the documented YASB reader and examples.
- Examples are path-neutral and preserve the manual extraction model.
- No installer, signing, updater, package-manager, native YASB, or public-release behavior is added.
