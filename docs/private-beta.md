# AIBar Private Beta Distribution

AIBar `0.1.0-beta.3` is an unsigned, self-contained Windows x64 ZIP with current Codex CLI auth storage compatibility. It has no installer, signing, updater, uninstall flow, public release, or store distribution.

After committing the final beta.3 compatibility fix on exact parent `50f283645bb0c97962ed7a4dbf881761538cda1e`, run:

```powershell
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "$env:TEMP\aibar-beta" -SourceDateEpoch "1767225600"
```

The command refuses a non-canonical repository, any staged or uncommitted source, a source whose exact parent is not `50f283645bb0c97962ed7a4dbf881761538cda1e`, or a source outside baseline ancestor `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`. It publishes self-contained `win-x64` output, binds the baseline, exact parent, source, and version in the manifest and tester instructions, writes a sorted path/length/SHA-256 inventory and ZIP SHA-256 in `private-beta-manifest.json`, and smoke-launches the extracted executable before retaining an artifact.

Testers must confirm version `0.1.0-beta.3` and parent `50f283645bb0c97962ed7a4dbf881761538cda1e` in `private-beta-instructions.txt`, verify the ZIP hash with `Get-FileHash`, extract the ZIP, acknowledge the unsigned Windows warning, and launch `AIBar.Desktop.exe` from the extracted directory. To replace an older beta, exit it and replace the old extracted directory manually.
