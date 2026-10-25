# AIBar private beta distribution

AIBar `0.1.0-beta.4` is an **unsigned**, **self-contained**, **Windows x64** ZIP for manual private-beta testing. It has no installer, signing, updater, uninstall flow, package-manager delivery, store distribution, or public release.

## Package contents

The ZIP includes the optional stock YASB CustomWidget assets under `yasb/`: the bounded reader, fixed rollback cleanup script, `--show` launcher, sample configuration, CSS example, and `yasb/README.txt`. The examples use `<AIBarRoot>`; configure that placeholder once to the stable directory where you manually extract the ZIP.

The ZIP also includes `fonts/IosevkaTermNerdFont-Regular.ttf` and `fonts/IosevkaTerm-LICENSE.md` (SIL OFL 1.1). AIBar offers an optional per-user font install, without admin rights or runtime downloads; YASB must use `font-family: "IosevkaTerm Nerd Font"` and may need a restart. Installing the font does not change the YASB configuration.

Packaging downloads from the pinned Nerd Fonts `v3.5.1` source directory `https://raw.githubusercontent.com/ryanoasis/nerd-fonts/v3.5.1/patched-fonts/IosevkaTerm/`:

| Source file | Bundled file | Verification |
| --- | --- | --- |
| `IosevkaTermNerdFont-Regular.ttf` | `fonts/IosevkaTermNerdFont-Regular.ttf` | 14,442,592 bytes; SHA-256 `5c12e7d30e97ff997c10e9b0acb2ed7f8495134e0f33b1176136f9e6d36d8e00` |
| `LICENSE.md` | `fonts/IosevkaTerm-LICENSE.md` | SHA-256 `7484961b97b41985d54c21700e53f7142a7007d72401acbd32c0b9c6a5a0b558` |

For offline publishing, add `-GlyphFontDirectory "<pre-downloaded-font-directory>"` with the two files using their **bundled file names** above. The same production hash/length checks apply; missing or mismatched inputs fail closed. This option requires `-PrivateBeta` and cannot be combined with capability-plan or graph-projection modes. No font binaries are committed or cached in the repository. The `TestGlyph*` parameters are synthetic-test-only overrides guarded by `AIBAR_PRIVATE_BETA_TEST_MODE=1` and local inputs; never use them for delivery.

The quota service is an unsupported private integration. The widget reads only AIBar's sanitized snapshot. It does not refresh quotas, authenticate, access credentials, databases, logs, or analytics, and it does not make a public support or availability claim.

## Prepare a reviewed current-source package without starting AIBar

Use a clean committed checkout, inspect its source commit, and pass that full lowercase commit ID explicitly:

```powershell
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -ExpectedSourceCommit "<reviewed-full-commit-id>" -PrepareOnly -OutputDirectory "<fresh-output-directory>"
```

`-ExpectedSourceCommit` must exactly equal the observed `HEAD`; abbreviations, ref names and mismatches are rejected before publishing. This selects explicit-source provenance instead of the historical exact-parent gate, but preserves canonical repository, clean-source and baseline-ancestry checks. The manifest records the actual source and parent. `BetaBaselineCommit` and `BetaParentCommit` remain test-only overrides: do not enable test mode to prepare a delivery.

Both new options require `-PrivateBeta` and reject combinations with `-CapabilityPlan` or `-GraphProjection` before producing output (`BETA_OPTIONS_REQUIRE_PRIVATE_BETA`). `-PrepareOnly` requires the explicit source pin. It packages all eight YASB assets (including two provider marks) and both font files without extracting or launching the application for smoke testing. The manifest reports `startupSmoke: "not-run"`, and instructions explicitly say **Startup smoke not run**. This is not runtime or YASB integration proof. The intermediate `publish` directory is removed after the ZIP and inventory are generated; retain the ZIP, manifest and instructions, not another extracted application copy.

To keep build outputs separate from an existing checkout, private-beta publishing also accepts the existing isolation parameters together: `IsolationParent`, `IsolationMarker`, `IntermediateDirectory`, `BuildOutputDirectory`, `RestoreMetadataDirectory`, and `PackageCacheDirectory`. The owned parent must already exist with an exact `.aibar-isolation-marker`; its child paths and output must be fresh, non-overlapping, and outside the repository. Only remove owned build/cache temporaries after retaining and verifying the package; existing source outputs are not cleanup targets.

Preparation does not install, start, stop or reconfigure AIBar/YASB. Later, extract to one chosen stable `<AIBarRoot>`; do not accumulate versioned installation folders. Follow the stop-before-replacement instructions below. A failed build is not a usable package; preserve its failure evidence and reconcile any interrupted process before cleanup or another attempt.

## Legacy beta.4 build and install

Without the new options, the original provenance gate and application-startup smoke behavior are unchanged; successful manifests report `startupSmoke: "passed"` (process survival only, not full runtime validation). The smoke launch uses the current user's application data and is not an isolated live-state test.

After committing the final beta.4 provenance update on exact parent `816c28b6747a6e9aa62c5c23c216cd4feb7a55fd`, run:

```powershell
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "$env:TEMP\aibar-beta" -SourceDateEpoch "1767225600"
```

The command refuses a non-canonical repository, staged or uncommitted source, a source whose exact parent is not `816c28b6747a6e9aa62c5c23c216cd4feb7a55fd`, or a source outside baseline ancestor `c4e35c01d5ed9cff30b136802a39196a3b2ed91d`. It publishes self-contained `win-x64` output and records a sorted path/length/SHA-256 inventory plus ZIP SHA-256 in `private-beta-manifest.json`.

Testers verify the ZIP hash with `Get-FileHash`, manually extract it to their chosen `<AIBarRoot>`, acknowledge the unsigned Windows warning, and launch `AIBar.Desktop.exe` from that directory. Stop AIBar before manually replacing an older extracted directory. If AIBar cannot be stopped, stop the upgrade and leave the current directory in place.

## Rollback YASB assets

1. Stop AIBar.
2. From `<AIBarRoot>`, run `yasb\remove-aibar-quota.ps1` with no arguments.
3. Continue only when the cleanup command exits 0.
4. Remove or replace the YASB assets.
5. Verify the fixed snapshot is absent or is schema-v1 `disabled` with `sourceRetrievedAt`, `fiveHour`, and `weekly` all `null`.

If cleanup fails, stop removal. Do not remove or replace assets. Cleanup changes no AIBar consent, cache, credentials, or database.
