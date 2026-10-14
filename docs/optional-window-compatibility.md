# Optional quota windows: popup and YASB compatibility

WU1A changes presentation of the existing nullable `QuotaSnapshot` and schema-v1 export. It does not connect the new projection policy, change authentication/provider parsing, or add history or polling behavior.

## Visible behavior

- The popup displays the weekly card first when supplied.
- Each absent quota card is `Collapsed`: no allocated height or card node in the UI Automation control tree.
- The reader renders only supplied quota windows, preserving `5h` then `7d` order when both exist.
- A weekly-only snapshot remains current, for example `7d: 35% | current`; absence does not create `partial` or an empty quota fragment.
- A supplied window with an unknown reset retains its reset placeholder. An absent window contributes no reset fragment.
- Stale, failed, unavailable, and disabled states retain their existing authority and warning semantics.

## Compatibility boundary

The export remains schema version 1 with ordered keys `schemaVersion`, `generatedAt`, `state`, `warning`, `sourceRetrievedAt`, `fiveHour`, and `weekly`. An absent sibling remains JSON null, not an omitted property. A malformed-present sibling or zero usable windows still fails closed.

The reader retains exactly three ordered fields: `label`, `tooltip`, and `className`. Tooltip `state:` and warning `warning:` copy are unchanged. There is no compact text, glyph/color output, human-warning mapping, warning coalescing, or UTF-8 execution change in this unit.

| Input | Label | Class |
|---|---|---|
| Weekly only, current | `7d: 35% | current` | `aibar-quota state-current` |
| Five-hour only, current | `5h: 12.5% | current` | `aibar-quota state-current` |
| Both, current | `5h: 12.5% | 7d: 35% | current` | `aibar-quota state-current` |
| Weekly only, refresh failed | `7d: 35% | stale | warning: refresh-failed` | `aibar-quota state-stale warning-refresh-failed` |
| Unavailable, warning unavailable | `unavailable | warning: unavailable` | `aibar-quota state-unavailable warning-unavailable` |
| Disabled, warning disabled | `disabled | warning: disabled` | `aibar-quota state-disabled warning-disabled` |

Parser validation, input bounds, public parameters, argument rejection, fixed paths, and cleanup behavior remain unchanged. YAML, CSS, launcher, and CI remain at the parent baseline. Nullable-window cases are folded into the existing Pester rendering block, preserving eleven test blocks without removing their assertions.

## Verification scope

### Historical original WU1A verification

The RED/GREEN and 613/613 results below apply to the original WU1A snapshot, not the current integrated PR candidate. They are retained provenance, not a claim that the current full suite passes.

Tests cover all four popup availability combinations, weekly-first order, collapsed layout, UI Automation card omission, schema-v1 wire shape, omission versus malformed data, reader exact labels/tooltips/classes, and existing failures/cleanup.

Before production edits, isolated Windows RED recorded Pester 9 passed/2 failed out of 11 and focused .NET 75 passed/6 failed out of 81, both exit 1 without timeout. Failures matched the old permanent cards/order and count-derived reader placeholders/partial behavior. An earlier supervisor lost the child exit code; its results are retained but are not accepted RED evidence.

The focused .NET filter selects PopupRenderedTests, QuotaVisualDesignTests, QuotaHttpProviderTests, QuotaExportTests, QuotaPresentationTests, and PrivateBetaDistributionTests. Windows Pester is pinned to 5.7.1. Final GREEN passed 81/81 .NET and 11/11 Pester tests, with exit 0, no skipped cases, and no timeout. All 264 uploaded candidate inputs matched local/remote hashes; the 37 original WIP files remained intact.

An independent read-only review found no code or scope blockers. Its scoped evidence supports QFR-1A.1 through QFR-1A.5. The user explicitly deferred QFR-1A.6 installed/live desktop behavior and YASB polling to the installation stage; those checks remain unexecuted, not passed.

Full solution validation passed **613/613 tests** in a separately authorized native Windows Git checkout (SDK 8.0.408). Restore, Debug build, and test exited 0 without timeout; the final TRX records no failures, errors, aborted runs, or unexecuted cases. The test command used `dotnet test AIBar.sln --no-build --no-restore --configuration Debug` after separate restore/build steps. Git clean-filter identities matched all 264 candidate inputs before and after execution; original WIP hashes remained unchanged. Only this evidence document was subsequently updated for delivery.

The earlier byte-preserving snapshot run exited 124 without a final TRX, recording failures in HostPrimitivesTests, PackagingSupervisorAuthorityTests, DesktopLocalUsageCompositionTests, and PackagingRecoveryTests. A separately authorized serial comparison executed five cases per version: both clean base and candidate passed four and failed the same B2 source-byte check. The expected B2 hash corresponds to CRLF, while the snapshot preserved LF. All four methods, including both Recovery cases, passed in the native checkout; this does not establish the cause of the other three earlier failures. No product or test correction was made.

The native checkout used repository-local `core.autocrlf=true` and `core.eol=crlf`, not global Git changes. An initial verifier compared raw local blobs against Git clean-filter hashes and stopped before restore/build/tests. A read-only diagnosis confirmed identical solution-file bytes; the comparator was corrected to apply the same filters on both sides before the single authorized suite execution.

Machine-local evidence remains in the original checkout's ignored `.atl/wu1a-evidence/` directory: corrected RED, GREEN, the failed snapshot suite, bounded base/candidate comparison, initial verifier failure, and successful native-checkout logs, exit metadata, XML/TRX, and input manifests. Earlier failures are retained rather than replaced. Historical isolated full-suite acceptance was established for that original snapshot; current integrated and live-installation acceptance are not established.

No installed AIBar/YASB configuration, live private endpoint, or live credential access is part of this validation. Synthetic fixtures and temporary test windows do not establish a live installation upgrade.

### Integrated draft candidate verification

The frozen clean-main control passed 613/615 .NET tests and 11/11 Pester tests. Its two failures reached `File.Replace` in the Clear composition and local-usage coordinator fixtures.

The integrated candidate passed 616/618 .NET tests and 11/11 Pester tests. Restore/build exited 0, the native suite exited 1 without a suite-supervisor timeout, and all 268 candidate input identities matched the frozen expected manifest before and after execution. Its two failures were the cleanup-bounds entry-count case and isolated packaging's internal restore deadline; that restore reported 8.35 hours. The cause is not established. The baseline's failing `File.Replace` cases passed in the candidate, which does not establish historical causality, flakiness, or independence.

The initial SSH supervisor timed out; native results were recovered later without rerunning tests. Full integrated acceptance remains pending, and the PR stays draft while CI is evaluated. The repository workflow runs independent Windows .NET and pinned Pester jobs. No new test fixes, production writer changes, installation, or live polling checks were added for this delivery.

These integrated results cover the source/test snapshot. This delivery subsequently contextualizes only this passive verification documentation; it does not claim that documentation was present in the earlier tested manifest.

## Rollback

Revert only WU1A presentation/tests and this document. Keep the unchanged parser, schema-v1 export, original WIP, and later YASB refinements separate. After a separately authorized merge, verify the worktree has no unpublished work before removing it and its branch; retained test evidence is reviewed separately before deletion.
