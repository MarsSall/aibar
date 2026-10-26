# Public release: free installer, new-version notice and GitHub Releases

Turn the private beta into a public Windows release the way small open-source apps do it, at no cost and without third-party code inside AIBar.
Branch: `feat/public-release` from `main` at `e0b3ff1`. Verification runs on Windows CI and on the user's PC.
Delivery: one PR with one commit per task, each under ~400 lines so it can be reviewed alone; T4 (Risk 5) in its own commit.

## Specs

- S1 Audience: "es para publicar". AIBar is published for other people, not only the user's PC.
- S2 Cost and approach: "deberiamos hacerlo como hacen los demas opensource, no vamos a pagar para tener un instalador en windows". No paid tool, service or certificate.
- S3 Third parties: "me preocupa usar cosa de terceros". Accepted proposal: "Instalador con Inno Setup ... Solo se usa en el CI para generar el `Setup.exe`: no queda nada de Inno Setup dentro de AIBar. Instala sin pedir permisos de administrador, crea accesos directos y agrega el desinstalador." No new runtime dependency (no Velopack, no Tauri).
- S4 Update notice, accepted: "AIBar consulta los Releases de GitHub y, si hay una versión más nueva, muestra un aviso con un botón que abre la página de descarga. Lo escribimos nosotros, son unas pocas líneas, y nunca descarga ni ejecuta nada por su cuenta."
- S5 Release, accepted: "Release en GitHub generado por el CI al crear un tag, con el `Setup.exe`, el ZIP portable y sus hashes."
- S6 Later, outside this feature: "winget, que acepta instaladores sin firmar, y pedir la firma gratis a SignPath cuando ya haya un release publicado."
- S7 Tauri: not now ("si en algún momento querés AIBar en macOS o Linux, ahí sí tendría sentido evaluar Tauri ... como un proyecto aparte"). Keep `%LOCALAPPDATA%\AIBar` data formats unchanged so a future rewrite can read them.
- S8 D1 answer → "Prendido, con opción para apagarlo (Recom.)": "Así los usuarios se enteran de las actualizaciones. Se apaga en Settings > General y el README lo explica." The check sends nothing but the anonymous request for the latest release.
- S9 D2 answer → "Preguntar al desinstalar": "El desinstalador muestra una casilla "Borrar también mis datos", destildada por defecto." Unchecked keeps `%LOCALAPPDATA%\AIBar`; checked deletes it.

## Tasks

- T1 [S1, S5] Publish script `-Release` mode: versioned portable ZIP with YASB assets and font, `SHA256SUMS.txt`, no private-beta baseline gates; packaging tests + docs — worker (parallel with T3), Windows trial `%TEMP%\aibar-trial-t1` — commit: 45f5d4a
- T2 [S2, S3, S9] Inno Setup script `packaging/installer/AIBar.iss`: per-user install in `%LOCALAPPDATA%\Programs\AIBar`, no admin, Start menu shortcut, uninstaller, closes a running AIBar before replacing files, D2 data rule, Start with Windows path stays valid; structural tests + docs — worker after T1 — commit: (this commit)
- T3 [S4, S8] New-version notice: bounded anonymous read of the latest GitHub release (pattern of `ProviderStatusClient`), version comparison, notice with a button that only opens `https://github.com/MarsSall/aibar/releases/...`, D1 default + Settings toggle; tests + docs (settings, README privacy) — worker (parallel with T1), Windows trial `%TEMP%\aibar-trial-t3` — commit: 0172d66
- T4 [S5] `.github/workflows/release.yml` on `v*` tags: build, test, T1 + T2, publish the GitHub Release with Setup.exe, ZIP and `SHA256SUMS.txt`; `contents: write` only in that job, pinned actions, pinned Inno Setup — independent verify (Risk 5) — commit: pending
- T5 [S1, S5] Version `0.1.0`, README/README.es install section with the SmartScreen steps, retire the private-beta doc, first tag after user acceptance on the PC (install with Setup.exe, update notice, uninstall) — inline — commit: pending

## Log

- L1 (user, original request): "es para publicar, enc uanto al instalador deberiamos hacerlo como hacen los demas opensource, no vamos a pagar para tener un instalador en windows"
- L2 (user): "me preocupa usar cosa de terceros, velopack de donde salio? es seguro? ademas no se si vale tanto la pena para esta app chica. Quorb como se maneja?" → evidence: Quorb (egdev6/quorb, Tauri) ships unsigned setup.exe + MSI with Tauri's signed updater; signing and winget are on its roadmap. Proposal changed to Inno Setup + notice-only update check.
- L3 (user): "y que pasa siq uisieramos implementar tauri?" / "y si despues quiesieramos cambviar a tauri? seria mas complejo todavia?" → rewrite of ~14k C# + 1.1k XAML lines; not now; this plan does not make it harder.
- L4 (user): "si dale" → build this plan and show it before touching code.
- L6 (user decisions): D1 "Prendido, con opción para apagarlo (Recom.)"; D2 "Preguntar al desinstalar".
- L5 (evidence): `scripts/Publish-Deterministic.ps1` (350 lines) has private-beta and recovery modes with hardcoded baseline/parent gates; covered by `PackagingDistributionTests`, `PrivateBetaDistributionTests`, `PackagingRecoveryTests`. CI (`ci.yml`) is `contents: read`, no secrets. Version `0.1.0-beta.4` in `AIBar.Desktop.csproj`. Start with Windows exists in Settings General.
- L7 (evidence): T3 full suite on the user's PC had 11-13 failures in `Until(...)`-waiting runtime tests; the same 7 classes fail on base e0b3ff1 (12 failures) in the same clone, so they are environmental on that PC (CI passes main), not T3 regressions. CI is the full-suite gate.
