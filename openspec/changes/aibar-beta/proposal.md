# Proposal: AIBar Private Beta

## Intent

Deliver a private beta from exact committed parent `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`: testers can consent to Codex access, view quota and factual local usage, and run an unsigned portable build without AIBar storing secrets.

## User Outcomes

- Private integration is default-off, disclosed, enabled explicitly, and revocable; credential availability is visible without secret persistence.
- Real refresh shows 5-hour/weekly quota in tray and popup with loading, freshness, cached, degraded, unavailable, and safe error states.
- Local analytics show token/model totals, scan time, and complete, partial, or unavailable coverage.
- Testers receive an unsigned self-contained ZIP with warning, launch, replacement, version, and checksum instructions.

## Scope

### In Scope

- Persist only consent; compose credential lookup, refresh, cached fallback, and clean disable/exit.
- Unify tray/popup quota state; compose local scanning with factual aggregates and coverage warnings.
- Produce and smoke-test a manually distributed Windows x64 ZIP.

### Out of Scope

- Packaging authority, MSIX/installer, signing, automatic update/uninstall, or `aibar-foundation` packaging completion.
- SBOM/compliance, public-release claims, cost/trend/ETA polish, forecasting, and store distribution.

## Capabilities

### New Capabilities

- `private-codex-consent`: Default-off consent and secret-free credential availability.
- `quota-status-experience`: Live/cached refresh and consistent tray/popup state.
- `local-usage-analytics`: Factual aggregates with coverage state.
- `private-beta-distribution`: Unsigned ZIP and tester instructions.

### Modified Capabilities

None; no baseline OpenSpec capabilities exist.

## Approach

Compose existing services through the WPF host, with one tray/popup state and snapshots preserved on safe failures. Do not create a second shell or resume release engineering. Auto-chain within 1000 lines: consent/quota; presentation; analytics; ZIP.

## Affected Areas

| Area | Impact |
|---|---|
| `src/AIBar.Application/` | Consent, credentials, refresh, analytics |
| `src/AIBar.Desktop/` | Tray, popup, state |
| `scripts/Publish-Deterministic.ps1` | ZIP output |
| `tests/AIBar.Domain.Tests/` | Beta evidence |

## Dependencies

- Codex CLI files, private endpoint, and .NET 8 Windows x64 tooling.

## Risks

| Risk | Mitigation |
|---|---|
| Private API/auth changes | Safe classification, redaction, cached fallback |
| Partial scans appear complete | Explicit coverage and warnings |
| Unsigned ZIP warnings | Private-beta labeling and checksum |
| Packaging scope leak | Enforce stated exclusions |

## Rollback Plan

Disable consent to stop private access. Revert child slices to `cc8eca5`; no migration is required.

## Acceptance Outline

- [ ] Fresh launch is default-off and stores no credentials.
- [ ] Opt-in performs real refresh and updates tray/popup.
- [ ] Missing-credential, offline, stale/degraded, and partial-scan states are distinct; cached quota survives failure.
- [ ] Disable and exit are clean; ZIP instructions work without .NET installed.

## Proposal Question Round

Assume testers use Codex CLI, consent is per Windows user, and manual replacement is acceptable. Specs must flag contrary evidence.
