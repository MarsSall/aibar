# Codex remote tokens (issue #72)

Codex cost and Daily usage are empty because the user runs Codex through Gentle Shell/Pi on Linux (`provider: openai-codex`), not Codex CLI on Windows.
Branch: `feat/codex-remote-tokens`. Mirrors the Claude remote tokens path (`tools/claude-remote-usage/claude_remote_tokens.py`, `ClaudeRemoteTokens.cs`).

## Specs

- S1 Linux collector: issue #72 "The Linux helper sums per-day, per-model token counters from Gentle Shell/Pi session files (`provider: openai-codex`, counters only, no content) and pushes them to Windows". New `codex_remote_tokens.py` reads `*.jsonl` under `~/.gentle-shell/agent/sessions` and `~/.pi/agent/sessions` (overridable), keeps only `type: message` entries whose `message.role` is `assistant` and `message.provider` is `openai-codex`, maps `usage.input/cacheRead/cacheWrite/output` to `input/cachedInput/cacheWrite/output`, dedups by entry `id`, 30 local days, and emits the same schema-1 contract as Claude tokens to `codex-remote-tokens.json` (same bounds, partial/unavailable states, atomic push).
- S2 AIBar uses it: "AIBar uses them for Codex cost and the Daily usage chart". Windows reads `%LOCALAPPDATA%\AIBar\codex-remote-tokens.json` with the existing strict tokens parser and combines it with local Codex CLI usage (sum per day and model; local-only users see no change). The Codex Cost card and the Daily usage chart show the combined values.
- S3 Deploy: the systemd service runs the new collector as another independent `ExecStartPre=-` step; README and docs/usage-cost.md describe it.

## Tasks

- T1 [S1, S3] Python collector + tests + service unit + README — inline — commit: cbe74bb
- T2 [S2] AIBar reader wiring, combine, docs/usage-cost.md — worker — commit: c9f0d29
- T3 [S1-S3] CI, deploy helper on Linux and AIBar on Mars, user check — inline — commit: pending

## Log

- L1 (user): "bueno, resolvamos los pendientes" (pending: merge PR #71, issue #72 Codex cost). PR #71 merged as b6b136a.
- L2 (evidence): ~29k openai-codex assistant entries in 30 days (gpt-6.1-sol, gpt-6-astra, gpt-6-sol, gpt-6-luna; all priced in CheckedInPricingCatalog), none with cacheWrite > 0, 99 duplicate entry ids (forked sessions).
- L3 (evidence): Python RED observed (module missing), then 51/51 helper tests green; dry run on real sessions: current, 46 day-model rows, 4 models, ~4.0B tokens, 1.24 s, 26 MB, 5.8 KB.
- L4 (decision): oversized lines (22, none assistant) mark partial only when they are assistant messages; files untouched 31 days are skipped; only lines containing "openai-codex" are parsed.
- L5 (decision): ClearAiBarDataService allowlist extended with codex-remote-tokens.json (worker request, approved by parent to match Claude clear semantics). Combined Codex cost follows the existing Codex consent gate, like local analytics (BetaRuntime owns analytics).
- L6 (slice): branch ~930 authored lines in 2 commits (Linux helper, AIBar); one PR reviewed commit by commit.
