## AIBar {{VERSION}} — Windows x64

### What to download

- **AIBar-{{VERSION}}-win-x64-setup.exe** (recommended): installs for your user, adds a Start menu shortcut and an uninstaller. No administrator access required.
- **AIBar-{{VERSION}}-win-x64.zip** (portable): extract to a folder and launch `AIBar.Desktop.exe`. Keep the extracted files together.

These builds are **unsigned**. Windows SmartScreen may show a warning: choose **More info → Run anyway** only after verifying that you downloaded from this repository's release page and checking the hashes below. Do not disable Windows security protections.

### Verify the download

Download `SHA256SUMS.txt` into the same folder as your chosen artifact. In PowerShell, compute its SHA-256 (replace the filename for the portable ZIP):

```powershell
Get-FileHash -LiteralPath .\AIBar-{{VERSION}}-win-x64-setup.exe -Algorithm SHA256
Get-Content -LiteralPath .\SHA256SUMS.txt
```

Compare the entire hash with the line for that exact filename (case does not matter). Do not run the file if they differ. `release-manifest.json` records the source and portable file inventory; its checksum is also listed. Hashes detect corruption but are not a code signature or independent proof of publisher identity.

Source commit: `{{SOURCE_COMMIT}}`.

### Español

Descarga **setup.exe** para instalar sin permisos de administrador, o el **ZIP** para uso portable (extrae todo y abre `AIBar.Desktop.exe`). La compilación no está firmada: en SmartScreen, **Más información → Ejecutar de todas formas** solo si confías en esta página y verificaste el SHA-256 con `Get-FileHash` y `SHA256SUMS.txt`. No desactives la protección de Windows. El commit de origen figura arriba.
