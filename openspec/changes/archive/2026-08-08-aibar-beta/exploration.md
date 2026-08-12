## Exploration: AIBar Private Beta

### Current State

AIBar already contains most lower-level capabilities needed for a private beta, but they do not form a complete user-visible path. The clean `feature/aibar-beta` branch points exactly at committed baseline `c4e35c01d5ed9cff30b136802a39196a3b2ed91d`; there are no commits or working-tree changes between that baseline and the branch before this exploration artifact. The beta should be developed as a new, independent change on top of that commit. It must not depend on uncommitted files in the original worktree or resume the interrupted `aibar-foundation` packaging chain.

| Capability | Existing implementation | Disconnection or correction needed for beta |
|---|---|---|
| Private Codex integration | `PrivateIntegrationPolicy`, `CodexCredentialReader`, safe credential disposal, disclosure text, and a private-endpoint HTTP adapter exist. | Production composition constructs a default-disabled policy and injects a credential delegate that always returns `null`. The policy supports disable only; there is no explicit enable/consent action, persisted opt-in, or visible credential-availability state. |
| Quota retrieval | `QuotaHttpProvider` calls the real private `wham/usage` endpoint, parses 5-hour and weekly windows, rejects redirects, classifies safe failures, and treats reset-credit detail as optional degradation. | The provider cannot execute in the current application composition. Enabling must remain explicit, credentials must be read from the resolved Codex root, and initialization must lead to a real refresh rather than only loading cached data. |
| Refresh state | `QuotaRefreshCoordinator`, SQLite snapshot persistence, freshness policy, coalescing, manual bypass, lifecycle adapter, and stale/error preservation are implemented and tested. | Desktop composition does not connect lifecycle events or a polling/open-trigger path. The popup-open callback currently does nothing. Reset countdown text is recomputed only when state changes, so it can freeze between refreshes. |
| Tray | Single-instance hosting, `NotifyIcon`, taskbar recreation, popup toggling, refresh/settings menu commands, and orderly runtime cleanup exist. | The tray always uses a generic application icon and `AIBar` tooltip. It does not observe quota state or render percentage/loading/stale/error/unavailable status. |
| Popup | WPF cards already bind 5-hour and weekly percentages and reset countdowns, with freshness and manual refresh controls. | Error and optional-degradation data are mapped but not exposed/bound. There is no opt-in/credential guidance, no analytics area, and no continuously updated reset state. |
| Local analytics | Codex root resolution, safe session discovery, incremental JSONL scanning, checkpoints, SQLite daily/model aggregation, rebuild handling, coverage warnings, pricing/trend policies, and presentation mappers exist with focused tests. | None of the discovery, scan, aggregate, or presentation services are composed by `App`. No production scan is triggered and no local analytics are displayed. Basic beta analytics should be composed first; pricing, cost, trend, and exhaustion estimates are not required for the smallest beta. |
| Distribution | The Desktop project targets .NET 8 Windows x64. `Publish-Deterministic.ps1` can produce a self-contained publish directory and ZIP, while extensive packaging/signing/SBOM authority work also exists at the baseline. | The advanced authority chain is incomplete and must not be resumed for this change. The beta needs only a reproducible self-contained folder/ZIP, a short manual launch/update procedure, and prominent unsigned/private-beta limitations. |

The existing OpenSpec configuration is stale: it still describes an empty repository and has no detected test commands. A later proposal/design phase should use the actual .NET 8/WPF solution and `AIBar.Domain.Tests` evidence rather than that obsolete context, without broadening this exploration into SDD initialization work.

Session constraints for downstream planning are execution mode `auto`, artifact store `openspec`, delivery strategy `auto-chain`, and a `1000` authored-line review budget. The likely implementation exceeds one review unit, so beta work should be chained by user-visible dependency rather than delivered as one large branch.

### Affected Areas

- `src/AIBar.Desktop/App.xaml.cs` — replace disabled/null production composition with explicit opt-in state, real credential resolution, quota refresh triggers, analytics composition, and coordinated disposal.
- `src/AIBar.Application/CredentialBoundary.cs` — extend the one-way policy into an explicit, non-secret opt-in state while preserving read-only credential access and redaction boundaries.
- `src/AIBar.Application/StartupSettings.cs` — expose enable/disable consent operations and persist only the opt-in choice, never Codex credentials.
- `src/AIBar.Application/QuotaHttpProvider.cs` — retain the real adapter; verify live-response compatibility and surface credential-unavailable distinctly without exposing sensitive data.
- `src/AIBar.Application/QuotaRefreshCoordinator.cs` and `QuotaRefreshLifecycleAdapter.cs` — compose startup, popup-open, manual, and lifecycle refresh behavior; avoid overlapping work.
- `src/AIBar.Desktop/QuotaPresentation.cs` — expose loading, degraded, failure, credential, timestamp, and reset states needed by both tray and popup.
- `src/AIBar.Desktop/HostRuntime.cs` — connect popup-open refresh and update the tray percentage/state indicator and tooltip from presentation changes.
- `src/AIBar.Desktop/MainWindow.xaml` — add consent/credential guidance, visible degraded/error states, and a compact local-analytics section while retaining the existing quota cards.
- `src/AIBar.Application/SessionFileDiscovery.cs`, `SessionJsonlScanner.cs`, `AnalyticsScanCoordinator.cs`, and `SqliteDailyModelUsageStore.cs` — compose the existing local scan path and translate partial coverage/rebuild requirements into user-visible state.
- `src/AIBar.Desktop/DerivedMetricsPresentation.cs` — reuse source-label conventions where useful, but do not make cost/trend/ETA features beta blockers.
- `scripts/Publish-Deterministic.ps1` and `src/AIBar.Desktop/AIBar.Desktop.csproj` — establish the bounded self-contained manual beta artifact without activating MSIX/signing/release-authority work.
- `tests/AIBar.Domain.Tests/` — add composition and presentation coverage for the complete vertical path, including no-credential, disabled, success, stale, degraded, scan-partial, and offline-cache cases.

### Approaches

1. **Compose a beta path from existing modules** — add the missing consent/settings boundary, wire real credentials and refresh, connect analytics, project state into tray/popup, and publish an unsigned self-contained ZIP.
   - Pros: Maximizes reuse of tested domain/application code; directly closes the user-visible gaps; isolates beta scope from unfinished release engineering; supports reviewable dependency-ordered slices.
   - Cons: Requires careful desktop composition and state unification; live private-endpoint compatibility still needs manual validation; a small distribution procedure remains necessary.
   - Effort: Medium

2. **Resume `aibar-foundation` through production packaging before beta** — finish supervisor authority, MSIX, signing, install/update/uninstall, SBOM, and release gates before composing the product path.
   - Pros: Moves toward a stronger public-release posture and may eventually provide installer lifecycle guarantees.
   - Cons: Repeats the interrupted chain the request explicitly excludes; delays the actual user experience; introduces high-risk work unrelated to proving private-beta value; depends on unresolved packaging authority.
   - Effort: High

3. **Build a separate beta shell around selected services** — bypass the current desktop composition and create a second minimal executable/UI.
   - Pros: Could demonstrate quota data quickly with fewer modifications to the current host.
   - Cons: Duplicates tray, lifecycle, state, and UI code; creates a migration burden; weakens the tested architecture; makes later convergence harder.
   - Effort: Medium

### Recommendation

Use approach 1. Treat `cc8eca5` as an immutable committed parent and build `aibar-beta` as a clean child chain whose first slices restore the product path, not the packaging chain. A credible beta is complete only when a tester can:

1. Launch an unsigned self-contained build without installing a runtime.
2. See that private quota access is off by default, read the unsupported/private disclosure, and explicitly opt in.
3. See whether Codex credentials are available without AIBar copying, persisting, or revealing them.
4. Trigger a real quota refresh and receive 5-hour/weekly percentages, reset state, freshness, and safe loading/degraded/error feedback.
5. Read the current quota state from the tray indicator and open the popup for detail.
6. Scan local Codex session files and see basic token/model aggregates plus complete/partial/unavailable coverage.
7. Refresh again, operate from a cached snapshot during transient failure, disable the private integration, and exit cleanly.

Keep the beta analytics surface factual and small: recent local token totals, model grouping, scan time, and coverage/warnings are sufficient. Defer estimated cost, trend prediction, exhaustion ETA, historical forecasting, and polished dashboards unless they become nearly free after composition.

Plan the implementation as an `auto-chain` from `feature/aibar-beta`, likely in this dependency order: (1) consent/credential/composition and real quota refresh; (2) unified tray/popup state; (3) local analytics composition/display; (4) self-contained beta artifact and manual smoke evidence. Each child should stay within the `1000` authored-line review budget and remain independently reviewable. No child should target, merge, or mutate the interrupted `aibar-foundation` packaging worktree.

Explicitly defer MSIX, installer UX, automatic updates, upgrade/uninstall/data-retention matrices, production certificates, official signing, SmartScreen reputation, store distribution, release SBOM/compliance claims, and completion of packaging-supervisor authority. These are public-release concerns, not prerequisites for a manually distributed private beta. The ZIP must therefore be labeled private, unsigned, manually replaceable, and unsupported for automatic upgrade.

### Risks

- The private ChatGPT endpoint is undocumented and may change shape, permissions, or availability; live beta validation must fail safely and preserve cached data.
- Codex `auth.json` schemas may vary. Credential availability must be distinguishable from authentication/permission/network failures without logging secrets or paths.
- Opt-in persistence can accidentally become implicit consent. The stored value must be non-secret, default false, revocable, and paired with visible disclosure.
- Existing reset countdowns are event-driven snapshots; without a UI timer they become visibly inaccurate between refreshes.
- Analytics discovery and scanning can be partial because of file mutation, malformed records, reparse points, permissions, or rebuild requirements; the UI must not present partial totals as complete.
- Tray icon rendering and taskbar recreation can lose or lag state unless one presentation source owns icon/tooltip updates.
- The baseline includes substantial incomplete packaging machinery. Accidental reuse could reintroduce blocked authority assumptions and expand scope beyond the beta.
- An unsigned ZIP may trigger Windows warnings and has no automated update/uninstall path; tester instructions and checksum/version identification are required.
- `openspec/config.yaml` contains stale repository/testing context, which can mislead later phases unless they inspect the actual solution.

### Ready for Proposal

Yes. The proposal should define only the private-beta vertical path above, preserve default-off consent and credential secrecy, use `cc8eca5` as the exact parent, and state the advanced packaging deferrals explicitly. It should not continue or claim completion of `aibar-foundation`.
