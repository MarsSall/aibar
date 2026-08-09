# AIBar Private Beta Distribution

AIBar `0.1.0-beta.2` is an unsigned, self-contained Windows x64 ZIP with the consent-onboarding hotfix. It has no installer, signing, updater, uninstall flow, public release, or store distribution.

After committing the beta.2 hotfix on exact parent `5839721a566e5ad8dfc8381b9b9bd41270b7e3a6`, run:

```powershell
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "$env:TEMP\aibar-beta" -SourceDateEpoch "1767225600"
```

The command refuses a non-canonical repository, any staged or uncommitted source, a source whose exact parent is not `5839721a566e5ad8dfc8381b9b9bd41270b7e3a6`, or a source outside baseline ancestor `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`. It publishes self-contained `win-x64` output, writes a sorted path/length/SHA-256 inventory and ZIP SHA-256 in `private-beta-manifest.json`, creates tester instructions, and smoke-launches the extracted executable before retaining an artifact.

Testers must confirm version `0.1.0-beta.2` in `private-beta-instructions.txt`, verify the ZIP hash with `Get-FileHash`, extract the ZIP, acknowledge the unsigned Windows warning, and launch `AIBar.Desktop.exe` from the extracted directory. To replace beta.1 or another older beta, exit it and replace the old extracted directory manually.
