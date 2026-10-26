# Release packaging

Build public Windows x64 artifacts locally with PowerShell 7, Git and the .NET 8 SDK on Windows. Start from a **clean, committed checkout**; the full source SHA must match `HEAD`. The version comes from `<Version>` in `src/AIBar.Desktop/AIBar.Desktop.csproj`.

```powershell
$source = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 `
  -Release -ExpectedSourceCommit $source `
  -OutputDirectory C:\artifacts\AIBar-release
```

The output must be a nonexistent directory outside the checkout, with an existing parent. Choose a new output directory for each invocation.

The self-contained Release build produces:

- `publish/`: the application, YASB integration assets, bundled glyph font and its license. Keep this tree for the installer build.
- `AIBar-<version>-win-x64.zip`: portable app, with ordinal entry ordering and fixed timestamps (override `-SourceDateEpoch` if needed).
- `release-manifest.json`: version, source commit, target, per-file inventory (paths, lengths and SHA-256), and ZIP SHA-256.
- `SHA256SUMS.txt`: lowercase SHA-256 checksums for the ZIP and manifest, in `sha256sum -c` format. For example, run `sha256sum -c SHA256SUMS.txt` from the output directory where that utility is available.

The script downloads the pinned IosevkaTerm Nerd Font and license. For offline packaging, pass `-GlyphFontDirectory` containing `IosevkaTermNerdFont-Regular.ttf` and `IosevkaTerm-LICENSE.md`; production length and hash checks still apply.

Release packaging does not launch the app, sign binaries, or build an installer. Test the application separately. Do not combine `-Release` with `-PrivateBeta`, `-CapabilityPlan`, `-GraphProjection`, or beta preparation/baseline options. No historical beta baseline or parent is required.

## Build the installer

After the release command succeeds, use the **same output directory** to build an unsigned installer with the free Inno Setup 6 compiler:

```powershell
pwsh -NoProfile -File .\scripts\Build-Installer.ps1 `
  -ReleaseDirectory C:\artifacts\AIBar-release
```

The script finds `ISCC.exe` on PATH or in the standard per-user/machine Inno Setup 6 locations. To select it explicitly, add `-IsccPath 'C:\Users\you\AppData\Local\Programs\Inno Setup 6\ISCC.exe'`.

The version is read from `release-manifest.json`, not hardcoded. The installer packages the retained `publish/` tree (including YASB assets and fonts), creates `AIBar-<version>-win-x64-setup.exe` beside the ZIP, and appends its SHA-256 line to `SHA256SUMS.txt`. Missing inputs/compiler or compilation failure produce a nonzero exit code, preserve checksums and remove any partial setup from that attempt. An existing setup is not overwritten; use a fresh release output to rebuild it.

### Installer behavior

- Installs for the current user in `%LOCALAPPDATA%\Programs\AIBar` without administrator privileges. English and Spanish are available.
- Creates a Start menu shortcut, offers an unchecked desktop-shortcut option, and offers to launch AIBar on completion.
- Requires a running AIBar to close before replacing files. Start with Windows continues to use the installed executable at the stable installation path.
- Uninstall removes installed files and shortcuts. It removes only AIBar's HKCU Run value, and only when it exactly matches the startup command for this installation; portable/other-install startup entries are preserved.
- Uninstall asks **Delete my AIBar data too** / **Borrar también mis datos**, unchecked by default. Checking it deletes only `%LOCALAPPDATA%\AIBar`. Leaving it unchecked preserves that folder. Silent uninstall always preserves data.

Inno Setup is a build tool, not an AIBar runtime dependency. Neither packaging command runs the generated installer or uninstaller. Actual install, upgrade while running, shortcut/startup behavior and both data-retention choices still require manual acceptance on Windows; do not test destructive uninstall options against data you need.

## Publishing a release

1. Bump `<Version>` in `src/AIBar.Desktop/AIBar.Desktop.csproj` to the intended version (for example, `0.1.0` or `0.1.0-beta.5`). Merge the reviewed change to `main` and check out the merged commit locally.
2. Tag that commit with the exact project version, prefixed by `v`, then push only that tag:

   ```bash
   git tag vX.Y.Z && git push origin vX.Y.Z
   ```

   Replace `X.Y.Z` with the full version, including any prerelease suffix. A mismatched tag fails before building.
3. Wait for the **Release** workflow. It runs only on pushed `v*` tags, builds and tests the solution on Windows, runs the two packaging scripts, and creates a **draft** titled `AIBar <version>`. Versions containing `-` are marked prerelease. Drafts and prereleases are excluded from the app's latest-release update notice.
4. Review the draft notes and all four assets: `AIBar-<version>-win-x64-setup.exe`, `AIBar-<version>-win-x64.zip`, `release-manifest.json`, and `SHA256SUMS.txt`. Confirm the source commit and hashes, and manually test installation/portable startup and uninstall behavior on Windows before publishing.
5. Publish the reviewed draft by hand in GitHub Releases. The workflow never publishes automatically. Before creating a draft, an authenticated guard lists every page of releases, including drafts, and fails if any release has the same tag. API or JSON parsing errors also stop creation (fail closed). A rerun against an existing draft therefore fails; inspect it rather than expecting assets to be replaced. The workflow never edits, deletes, or overwrites an existing release.

The workflow uses only the automatic `GITHUB_TOKEN`, with `contents: write` restricted to the draft-creating job and read-only permission at workflow level. The token is exposed only in the step-level environments of the existing-release guard and draft creation steps. Official checkout and .NET setup actions are pinned to full commit SHAs; checkout does not persist credentials. Inno Setup is build-time only: the runner must use exactly **6.7.3**, or download the official installer and verify pinned SHA-256 `9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732` before a silent per-user installation. That hash was checked against the winget-pkgs manifest for `JRSoftware.InnoSetup` 6.7.3. No paid signing service or new runtime dependency is introduced.

Release notes come from `packaging/release-notes.md`, with version and source commit filled in by CI. They include English/Spanish download guidance, unsigned-build SmartScreen instructions, and PowerShell checksum verification. Build outputs, compiler downloads and rendered notes stay in runner temporary storage, outside the clean source checkout required by release packaging.
