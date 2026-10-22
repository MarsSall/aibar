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

## Tasks

- T1 [S3] Remove the DWM system border on the popup window — inline — commit: 281d648
- T2 [S4] Cost card uses a panel-matching surface — inline — commit: 281d648
- T3 [S1, S2] Stronger glow + "Provider tint" setting — inline — commit: 281d648 + 145a7b3
- T4 [S5, S6] DetailView moves into an owned side window with hover/click/keyboard — worker (context backstop) — commit: see `git log` (feat(panel): open Cost details in a side window)
- T5 [S1-S6] Docs, Windows CI, rendered check on the user's PC — inline — commit: pending

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
