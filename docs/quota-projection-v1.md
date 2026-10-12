# Quota projection v1: isolated Domain foundation

WU1 adds deterministic quota contracts and `QuotaProjectionPolicy` without connecting them to the runtime. Existing quota presentation, persistence, credentials, polling, and YASB remain outside this unit.

## Review path

1. Read `src/AIBar.Domain/QuotaProjectionContracts.cs` for immutable input/result contracts.
2. Read `src/AIBar.Domain/QuotaProjectionPolicy.cs` for evidence selection and fitting.
3. Read `tests/AIBar.Domain.Tests/QuotaProjectionPolicyTests.cs` for public-policy examples.

## Evidence and projection rules

- Provider, window, reset instant, and nominal duration identify compatible cycle evidence.
- Input order is arrival order. Duplicate sample keys are canonicalized before temporal checks.
- A backward retrieval timestamp or a decrease in percentage used greater than 0.5 point starts a new segment. Only the latest arrival segment is fitted; old evidence is not sorted into it.
- At or after reset, projection is withheld as `Learning your pace` with no rate, ETA, confidence, or risk.
- Adjacent rates are filtered by median absolute deviation (MAD): reject deviations above three MADs, or above 0.5 percentage point/hour when MAD is zero.
- Both endpoints of rejected intervals are excluded from fitting, including when neighboring intervals retained them. More than 20% rejected intervals is inconsistent evidence.
- Projection requires six retained points, three positive-duration intervals, at least two points of observed depletion, and sufficient temporal coverage. No retained interval may dominate more than 60% of observed depletion.
- Minimum coverage is the greater of 45 minutes and 5% of nominal duration; without duration, it is two hours.
- Fit percentage used with decimal recency-weighted least squares. Half-life is the greater of two hours and 20% of nominal duration, or twelve hours without duration.
- Reject non-positive rate, residual RMS above three points, or fitted value at evaluation time differing from the authoritative current value by more than three points.
- Round displayed rate to 0.1 point/hour and exhaustion time to fifteen minutes.
- Medium confidence requires ten points, 10% coverage, residual at most two points, and at most 10% excluded intervals. The exclusion denominator is all pre-filter intervals, not kept intervals.
- High confidence requires sixteen points, 20% coverage, residual at most one point, no exclusions, and at least two non-zero depletion intervals.
- Reset-relative risk uses a margin of 5% of time until reset, bounded between thirty minutes and six hours.

The policy uses supplied facts and evaluation time only; it does not read a clock, files, credentials, HTTP, or local activity. Withheld projections expose no ETA or risk.

## Verification evidence

The final source was tested on Windows in a fresh isolated `net8.0` xUnit harness. It compiled current Domain sources with the projection, FoundationContracts, and DerivedMetrics test classes, excluding Application and Desktop.

| Stage | Passed | Failed | Exit |
|---|---:|---:|---:|
| Reset/filter/evaluation-time RED | 10 | 4 | 1 |
| First corrective GREEN (outlier classification regression) | 13 | 1 | 1 |
| Corrected projection GREEN | 14 | 0 | 0 |
| Temporal/confidence RED | 15 | 3 | 1 |
| Final projection GREEN | 18 | 0 | 0 |
| Final selected Domain suite, including projection | 38 | 0 | 0 |

No final cases were skipped. The outlier classification regression was corrected without weakening the test. Final TRX counters were read and remote source hashes matched local inputs.

Harness commands: `dotnet test Isolated.csproj --nologo --filter FullyQualifiedName~QuotaProjectionPolicyTests` for RED; add `--no-restore` for GREEN; remove the filter for the selected Domain suite. `Isolated.csproj` is an evidence harness, not a repository project.

Retained local evidence: `.atl/wu1-2b50390b11e248508ac564decc74bc47/` and `.atl/wu1-followup-02ba25ea37774eb493619bac4e480dbe/`. These ignored, machine-local directories are not distributed with this unit. The public tests remain in the existing repository test project.

The original WPF/full solution suite was not run for this candidate. These results do not establish installed UI behavior, full-feature acceptance, or delivery of later quota-first units.

## Rollback and next boundary

Revert only the WU1 contracts, policy, tests, and this document. Existing runtime behavior has no new consumer to disconnect. A later runtime integration must separately validate its adapters, history, privacy boundaries, UI, and full Windows regressions.
