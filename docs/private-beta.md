# AIBar Private Beta Distribution

The private beta is an unsigned, self-contained Windows x64 ZIP. It has no installer, signing, updater, uninstall flow, public release, or store distribution.

After committing the complete Unit 4 work on top of `6e2d8a46d455819c6f30a07a34bf63c9594d5c4c`, run:

```powershell
pwsh -NoProfile -File .\scripts\Publish-Deterministic.ps1 -PrivateBeta -OutputDirectory "$env:TEMP\aibar-beta" -SourceDateEpoch "1767225600"
```

The command refuses a non-canonical repository, any staged or uncommitted source, a source whose parent is not Unit 3B, or a source outside baseline ancestor `cc8eca54b8c49ffae3f88aa35f328cbf85a9ab97`. It publishes self-contained `win-x64` output, writes a sorted path/length/SHA-256 inventory and ZIP SHA-256 in `private-beta-manifest.json`, creates tester instructions, and smoke-launches the extracted executable before retaining an artifact.

Testers must read `private-beta-instructions.txt`, verify the ZIP hash with `Get-FileHash`, extract the ZIP, acknowledge the unsigned Windows warning, and launch `AIBar.Desktop.exe` from the extracted directory. To replace an older beta, exit it and replace the old extracted directory manually.
