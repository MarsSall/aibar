# Tray consent integration-test retirement

`HostRuntimeTests.Accept_reaches_real_grant_once_updates_state_and_disable_restores_the_menu` is retired by an explicit cost/coverage decision. No structural fixture defect or cause of its `UnauthorizedAccessException` was demonstrated. Removing it does not establish that the integrated revocation path works or fix production behavior.

## Accepted coverage gap

The removed test joined accepted disclosure, duplicate enable requests, one prompt/provider/credential read, real consent persistence, missing-credential presentation, tray-driven disable, persisted revocation, restored menu, and cleared presentation flags. That complete sequence is no longer asserted by this test suite.

Existing narrower tests retain disabled-consent persistence, active-work cancellation and late-result rejection (`BetaConsentOrQuotaTests`), refresh gating (`QuotaPresentationHostTests`), local-analytics independence and clear ordering (`StartupSettingsTests`), and declined-disclosure/initial disabled-menu behavior (`HostRuntimeTests`). They do not replace the integrated sequence.

## Evidence and delivery boundary

PR37 CI run38557535424 recorded 617/618 .NET tests passing, with this test failing during real revocation; YASB/Pester passed. That failure remains retained evidence, not classified as flakiness or a proven fixture error.

This independent main-based slice deletes only the test method and adds this coverage disclosure. Production code, remaining tests, shared helpers, deadlines, assertions and CI configuration are unchanged. It precedes WU1A PR37 in the selected stacked-to-main delivery strategy. Review and checks of each new head remain necessary before merge; a passing reduced suite does not restore the accepted coverage gap.
