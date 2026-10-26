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

A later task will run packaging on version tags in CI and publish the installer, portable ZIP and hashes to GitHub Releases. This command currently produces local artifacts only.
