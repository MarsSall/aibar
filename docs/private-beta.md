# AIBar private beta distribution

AIBar `0.1.0-beta.4` is an **unsigned**, **self-contained**, **Windows x64** ZIP for manual private-beta testing. It has no installer, signing, updater, uninstall flow, package-manager delivery, store distribution, or public release.

## Package contents

The ZIP includes the optional stock YASB CustomWidget assets under `yasb/`: the bounded reader, fixed rollback cleanup script, `--show` launcher, sample configuration, CSS example, and `yasb/README.txt`. The examples use `<AIBarRoot>`; configure that placeholder once to the stable directory where you manually extract the ZIP.

The quota service is an unsupported private integration. The widget reads only AIBar's sanitized snapshot. It does not refresh quotas, authenticate, access credentials, databases, logs, or analytics, and it does not make a public support or availability claim.

## Build and install

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
