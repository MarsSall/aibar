# AIBar Private Beta Distribution

AIBar `0.1.0-beta.4` is an unsigned, self-contained Windows x64 ZIP that supports the current Codex Unix-reset quota schema, independently optional windows, and weekly-only accounts while hiding absent cards. It has no installer, signing, updater, uninstall flow, public release, or store distribution.

After committing the final beta.4 provenance update on exact parent `816c28b6747a6e9aa62c5c23c216cd4feb7a55fd`, run:

```powershell
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "$env:TEMP\aibar-beta" -SourceDateEpoch "1767225600"
```

The command refuses a non-canonical repository, any staged or uncommitted source, a source whose exact parent is not `816c28b6747a6e9aa62c5c23c216cd4feb7a55fd`, or a source outside baseline ancestor `c4e35c01d5ed9cff30b136802a39196a3b2ed91d`. It publishes self-contained `win-x64` output, binds the baseline, exact parent, source, and version in the manifest and tester instructions, writes a sorted path/length/SHA-256 inventory and ZIP SHA-256 in `private-beta-manifest.json`, and smoke-launches the extracted executable before retaining an artifact.

Testers must confirm version `0.1.0-beta.4` and parent `816c28b6747a6e9aa62c5c23c216cd4feb7a55fd` in `private-beta-instructions.txt`, verify the ZIP hash with `Get-FileHash`, extract the ZIP, acknowledge the unsigned Windows warning, and launch `AIBar.Desktop.exe` from the extracted directory. To replace an older beta, exit it and replace the old extracted directory manually.
