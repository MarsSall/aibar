# Panel side details and surface polish

Move the in-panel Cost detail view into a CodexBar-style side flyout and clean up the panel surface.
Branch: `feat/panel-side-details`. Verification runs on Windows CI (`.github/workflows/ci.yml`, windows-2022) and on the user's PC.

## Specs

- S1 Provider tint stays, glow is stronger: "pensandolo mejor dejemos el borde, glow y demas, el efecto de glow a lo mejor estaria bueno que fuera un poco mas"
- S2 User choice for the tint: "ademas podriamos poner para que el ususario pueda elegir si sacarlo o ponerlo". Settings gets a "Provider tint" toggle (default on, persisted in `appearance.json`); off makes border, glow, header divider and selected tab neutral.
- S3 No second frame: "fijate en la otra flecha que te marco en la parte superiro hay como un segundo "borde" o marco gris que no me gusta" (the Windows 11 DWM system border drawn outside the panel's own rounded border).
- S4 Cost card matches the panel: "fijate que el "COSTO", esta en engro, no se condice con el pane" (opaque `CardBrush` black card on a translucent surface).
- S5 Side flyout: "me gusta como presenta los graficos codexbar, que se desplieguen al costa" / answer: "Todo el DetailView al costado" (chart, local usage and disclosures move to a side panel; the main panel no longer swaps views).
- S6 Trigger: "Hover + click (Recomendado)" — opens after a short hover on the Cost row, on click, and on Enter/Space/→ from the keyboard; Esc/← closes it and returns focus to Cost.
- S7 Charts: "los graficos no me gistan, preferiria de linea o columnas (configurable tambien)"; answers: data → "Ambos" (quota % of the selected provider, 5h and weekly, solid and readable; plus daily tokens/cost per day, CodexBar style); style switch → "Botón en el propio gráfico" (a Line | Columns selector next to 24h/7d/30d, remembered across restarts in appearance.json).
- S8 Unified details color: "el pane de de cada uno tiene el color del proveedor, cuando desplegas el pane del grafico tiene otro color y settings tiene otro color, tenemos que unificar, dejaria con el color de settings cuando se despliega el pane con el grafico"; answer → "Borde + glow + fondo (Recomendado)": the details window uses the Settings window's surface, border and blue glow; the main panel keeps the provider tint.
- S9 Remove sections: "/tmp/herdr-clipboard-images-1000/client-26-clipboard-1792676693844618882-0.png que no em interesa que este.."; answer → "Ambos": the details window drops "Local usage" (Local Codex data, OpenCode and Pi local usage) and "Quota access" (the consent dialogs keep the disclosure text).

## Tasks

- T1 [S3] Remove the DWM system border on the popup window — inline — commit: 281d648
- T2 [S4] Cost card uses a panel-matching surface — inline — commit: 281d648
- T3 [S1, S2] Stronger glow + "Provider tint" setting — inline — commit: 281d648 + 145a7b3
- T4 [S5, S6] DetailView moves into an owned side window with hover/click/keyboard — worker (context backstop) — commit: see `git log` (feat(panel): open Cost details in a side window)
- T5 [S1-S6] Docs, Windows CI, rendered check on the user's PC — inline — commit: 4b9d4cf (deployed, user feedback L11)
- T6 [S7, S8, S9] Chart redesign (quota + daily usage, Line/Columns toggle persisted), Settings-colored details window, remove Local usage and Quota access — worker — commit: pending
- T7 [S7-S9] Windows CI, redeploy to the user's PC, user acceptance — inline — commit: pending

## Log

- L1 (user, original request): "bueno, quiero que cambiemos un poco la estetica de aibar... me gusta como presenta los graficos codexbar, que se desplieguen al costa, y ademas... no me gusta que el pane se "tiña" con el color de cada proveedor"
- L2 (user, answers): tint → "pensandolo mejor dejemos el borde, glow y demas, el efecto de glow a lo mejor estaria bueno que fuera un poco mas, ademas podriamos poner para que el ususario pueda elegir si sacarlo o ponerlo, por otro lado fijate que el "COSTO", esta en engro, no se condice con el pane../tmp/herdr-clipboard-images-1000/client-26-clipboard-1792672852807960605-0.png ademas fijate en la otra flecha que te marco en la parte superiro hay como un segundo "borde" o marco gris que no me gusta"; flyout → "Todo el DetailView al costado"; trigger → "Hover + click (Recomendado)".
- L3 (decision, superseded by L4): side panel is a WPF `Popup` (no activation), because the host hides the panel on `Deactivated` (`HostRuntime.cs:247`); a second window would close it.
- L4 (decision): side panel is an owned borderless `Window` (Owner = MainWindow). The host already ignores panel deactivation while an owned window is visible (`IsOwnedDialogActive`, `HostRuntime.cs:556`), so the panel stays open; the pair handles outside clicks itself. A window gets the same acrylic/corners as the panel (a Popup cannot) and real keyboard focus.
- L5 (evidence): T1-T3 commit pushed; CI run 39781285393 (workflow_dispatch, CI only triggers on main/PRs).
- L6 (evidence): CI 39781285393 on 281d648 failed only on PanelSurfaceTests glow (cross-thread brush, test bug fixed in 145a7b3) and RefreshIntervalWindowTests(Light) (pre-existing flake; main run 39778832297 failed on a sibling Settings test).
- L7 (slice): T1-T3 and T4 are separate commits; the branch is ~900 authored lines, mostly T4 test migration. One PR, reviewed commit by commit.
- L8 (review fix): parent fixed worker output: hover-then-click pins instead of closing; Left stays with the chart range selector; details window background transparent (no double alpha). CI 39783679767.
- L9 (evidence): CI 39784767277 green on all jobs (.NET Windows, YASB Pester, Python) after the activation fix. Pending: rendered/interactive check on the user's PC (hover, focus, acrylic, outside click).
- L10 (deploy): built 4b9d4cf on Mars (SDK 8.0.408, self-contained win-x64); 7 AIBar files changed, installed into Downloads\AIBar with hash check; backup of previous 7 files at %TEMP%\aibar-pr71-backup (delete after acceptance); build clone removed; relaunched PID 1196 session 4.
- L11 (user, after testing 4b9d4cf): "bueno varias cosas, los graficos no me gistan, preferiria de linea o columnas (configurable tambien), el pane de de cada uno tiene el color del proveedor, cuando desplegas el pane del grafico tiene otro color y settings tiene otro color, tenemos que unificar, dejaria con el color de settings cuando se despliega el pane con el grafico.  Luego tenemos /tmp/herdr-clipboard-images-1000/client-26-clipboard-1792676693844618882-0.png que no em interesa que este.." Answers: data "Ambos"; style "Botón en el propio gráfico"; color "Borde + glow + fondo (Recomendado)"; remove "Ambos".
- L12 (slice): T6 commit pushed; branch now ~1.6k authored lines across 7 commits (one per work unit). PR #71 stays one PR reviewed commit by commit; PR pushes trigger CI automatically (run 39792862817).
- L13 (deploy): CI 39793696910 Test step success on 740fe2e (first T6 run failed on a stray semicolon, fixed in 740fe2e; full build verified on Mars). Installed 7 files into Downloads\AIBar, relaunched PID 1900 session 4; original backup still at %TEMP%\aibar-pr71-backup.
- L14 (user): "est abuenno, pero.. por que figuran dos graficos, el segundo es solo una linea y no tieen los ejes.." Fix e76aac8: daily chart axes + empty message for all-zero days; 30/30 chart tests passed on Mars; deployed.
- L15 (user): "me gusto, te pido que cambiemos algo nomas, el glow de costo que sea el del proveedor seleccionado / otra cosa, codex dice "PRO" pero claude no dice nada, adem´as codex no esta registrando costo de tokens". Answers: Codex cost → "Dejarlo para otro PR" (issue #72: Codex usage runs through Gentle Shell/Pi openai-codex on Linux, 0 Codex CLI sessions on Windows or Linux); Claude plan → "Sí, enviar el plan (Recomendado)". Done: b7f99da details glow = panel provider glow (39/39 focused tests on Mars); helper sends optional trailing "plan" (Python RED observed, 39/39 green), AIBar accepts it and labels the plan for the other-machine source. Deploy order: AIBar first (old builds reject the extra property), then the helper in ~/.local/lib/aibar.
- L16 (deploy): c7fbfae installed on Mars (PID 33224); Linux helper ~/.local/lib/aibar/claude_remote_usage.py updated from repo (cmp equal), service run success; Windows file now state=current plan=max with keys ending in plan.
