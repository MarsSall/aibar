# Floating quota widget and YASB glyph font offer

Give users without YASB a quorb-style floating widget, and offer YASB users the glyph font the widget needs.
Branch: `feat/floating-widget-and-font`. Verification runs on Windows CI (`.github/workflows/ci.yml`) and on the user's PC.

## Specs

- S1 Font offer source: "Lo de la font lo resolvemos con el ofrecimiento desde la app"; answer → "IosevkaTerm Nerd Font en el ZIP". The private-beta ZIP bundles IosevkaTerm Nerd Font with its license; AIBar installs it per user (no admin, no network). It must cover U+F0766, U+F0A9E–U+F0AA5 (`yasb/read-aibar-quota.ps1:68`).
- S2 When to offer: answer → "Al iniciar, una vez, si hay YASB sin font (Recom.)": at startup, only when YASB is detected and the font is missing, AIBar shows the offer once with Install / Not now / Don't ask again; Settings also has an install button.
- S3 Floating widget: "con widget me refería a algo flotante, exacto, como ofrece quorb" (quorb: an always-on-top strip docked to a screen edge, one ring per provider, hover for details, drag to any edge, vertical on the sides).
- S4 Rings: answer → "Dos concéntricos: 5h afuera, semanal adentro (Recom.)": per provider (Codex, Claude), outer ring = 5h used %, inner ring = weekly used %; hover shows percentages, resets and plan.
- S5 Activation: answer → "Toggle en Settings + oferta si no hay YASB (Recom.)": Settings toggle, off by default; when YASB is not detected, AIBar asks once at startup whether to turn it on.
- S6 YASB tooltip like the dock card: "quedo muy bueno!! me gustaria que la info que da la tarjetita al estar parado sobre los widgest apapresca asi en la yasb var, me refieor a que la que ofrece ahora yasb es solo momentos de rest pero no el otro limite que no se muestra". The YASB tooltip shows, one per line like the dock card: provider name, `5h  <used>% · resets in <countdown>`, `Weekly  <used>% · resets in <countdown>`, then state (and distinct warning) with source age. Plan stays out (the YASB export carries no plan text by contract).

## Tasks

- T1 [S3, S4] Dock strip component: `DockSettings` (dock.json: enabled, edge, offset, monitor), `DockWindow` with concentric rings per provider, hover card, click opens panel, drag-to-edge snap, placement math + tests — worker (background, parallel with T2) — commit: f2b15c5, 5b1d327
- T2 [S1] Bundled glyph font: Publish-Deterministic downloads pinned IosevkaTermNerdFont-Regular.ttf + LICENSE.md (v3.5.1, SHA-256 pinned) into `fonts/`; `GlyphFontInstaller` (detect, per-user install, WM_FONTCHANGE) + tests; YASB docs — worker (background, parallel with T1) — commit: f1c99a9 (packaging), 7a660a6 (installer). Risk: item 5 (writes the user's font registry/files).
- T3 [S2, S5] Wiring: `YasbPresence`, `OnboardingSettings` (onboarding.json, in clear-data list), startup offers (font when YASB + missing font; dock when no YASB), Settings toggle + install button, App composition — standalone pieces inline, wiring by worker (context backstop) — commit: cbe7ccc
- T4 [S1-S5] Docs, Windows CI, deploy to the user's PC, acceptance — inline — commit: pending

- T5 [S6] YASB reader tooltip mirrors the dock card (both windows with % and reset, multi-line) + Pester + docs — worker (context backstop) — commit: pending

## Log

- L1 (user, original request): "Buenas!! Quiero que chequeemos algo, me quedo en la cabeza que pasa si alguien instala AIBar y no tiene YASB, no tendrá iconos.." → finding: the desktop app has no font dependency; only the YASB progress glyphs need a Nerd Font.
- L2 (user): "Tenemos un instalador? No recuerdo.. si lo tenemos convendría que les pregunte si quieren instalar la font. Y si quisiera, podríamos implementar de activar un widget si no tiene YASB?" → no installer (ZIP only).
- L3 (user): "Lo de la font lo resolvemos con el ofrecimiento desde la app, y con widget me refería a algo flotante, exacto, como ofrece quorb"
- L5 (decision): font is not committed (14.4 MB); the publish script downloads `https://raw.githubusercontent.com/ryanoasis/nerd-fonts/v3.5.1/patched-fonts/IosevkaTerm/IosevkaTermNerdFont-Regular.ttf` (sha256 5c12e7d30e97ff997c10e9b0acb2ed7f8495134e0f33b1176136f9e6d36d8e00, 14442592 bytes) and `LICENSE.md` (sha256 7484961b97b41985d54c21700e53f7142a7007d72401acbd32c0b9c6a5a0b558, OFL-1.1). YASB detected = `%USERPROFILE%\.config\yasb` exists or a `yasb` process runs. Dock position lives in its own dock.json so drags do not re-run appearance changes. Offers are mutually exclusive by YASB presence.
- L4 (user, answers): font "IosevkaTerm Nerd Font en el ZIP"; offer "Al iniciar, una vez, si hay YASB sin font"; rings "Dos concéntricos: 5h afuera, semanal adentro"; activation "Toggle en Settings + oferta si no hay YASB".
- L6 (slice): branch is ~1.9k authored lines in 5 work-unit commits (dock placement, dock window, font packaging, font installer, onboarding wiring); one PR reviewed commit by commit, like PR #71. Nothing compiled locally (no dotnet on Linux); CI run 40110279069 on cbe7ccc.
- L7 (evidence): PR #84 CI 40110765452 on 5c95e6c green (.NET 1501/1501, Pester 29/29, Python).
- L8 (deploy): user authorized "Sí, instalalo y relanzalo". Built 5c95e6c on Mars (SDK 8.0.408, self-contained win-x64) in %TEMP%\aibar-pr84-build (reused for fix iterations; delete after acceptance); font + license downloaded with hash match. Installed changed/new files into Downloads\AIBar (fonts\ added; yasb\ only files already installed); AIBar.Desktop.dll/exe/Application.dll hash-equal to the build. The intended backup %TEMP%\aibar-pr84-backup ended empty, so rollback means rebuilding main (927615d). Relaunched PID 12352 session 2 via a one-shot interactive scheduled task (removed). Pending: user acceptance.
- L9 (user): "hay algo que esta fallando, aibar se abre y se cierra sola, en relidad se cierra y cuando toco en yasb se vuelve a abrir"
- L10 (evidence): Application log on Mars, 08:26:07 and 08:27:08: .NET Runtime 1026 unhandled System.NotSupportedException "la propiedad ToolTip no admite el elemento ToolTip con StaysOpen establecido en falso" at PopupControlService.ShowToolTip: the dock ring hover card (DockRing.cs:47) set StaysOpen = false, so hovering a ring killed the process; YASB's click relaunched it. Fix: drop StaysOpen = false; regression test in DockRingTests.
- L11 (evidence): RED on Mars: DockRingTests.Hover_card_is_a_tooltip_wpf_can_open_without_crashing failed with the old DockRing.cs; GREEN: 32/32 Dock tests after the fix (first GREEN attempt was a stale incremental build: the copied file kept an older mtime). Fix commit 7bd1c0c. Redeployed: only AIBar.Desktop.dll changed, previous copy in %TEMP%\aibar-pr84-build\backup-before-fix; relaunched PID 24920 session 2.
- L12 (user): "quedo muy bueno!! me gustaria que la info que da la tarjetita al estar parado sobre los widgest apapresca asi en la yasb var, me refieor a que la que ofrece ahora yasb es solo momentos de rest pero no el otro limite que no se muestra"
- L13 (evidence): CI 40189228047 on 12ec181 green (.NET 1502/1502 incl. the hover regression test, Pester 29/29, Python).
- L14 (evidence/deploy): CI 40189928888 on c224eaf green (Pester 29/29, .NET 1502/1502). Installed yasb\read-aibar-quota.ps1 + README.txt into Downloads\AIBar\yasb (the path the user's YASB config runs); previous copies in %TEMP%\aibar-pr84-build\backup-before-yasb-tooltip. Live output: Codex "Codex / Weekly  0% · resets in 6d 23h / current · age: 0m"; Claude "Claude / 5h  11% · resets in 4h 22m / Weekly  46% · resets in 1d 23h / current · age: 0m". Pending: user sees it in YASB.
