# Codex quota from another machine (issue #85)

A Linux helper pushes Codex quota (percentages and resets only) to Windows; AIBar reads it for Codex when its Source is Automatic (file present) or Other machine.
Branch: `feat/codex-remote-quota`. Verification: Python tests locally, Windows CI, deploy on Linux + the user's PC.

## Specs

- S1 Issue #85 "Proposal": "Extend the Linux helper (`tools/claude-remote-usage/`) to also push Codex quota (5h and weekly percentages and resets only, never tokens or credentials) as `codex-remote-usage.json`, using the same strict contract as `claude-remote-usage.json`. AIBar reads it for Codex when the data source is Automatic (file present) or Other machine, with the same 15-minute staleness rule."
- S2 Issue #85 "Acceptance": "With Other machine, the Codex panel, dock, tray tooltip and YASB widget show the values from Linux." / "With Automatic, the remote file wins when it exists, like Claude." / "AIBar never reads the local Codex credential while the remote file is in use."
- S3 User: "dale nomas encaralo".

## Contract (shared by T1 and T2)

`%LOCALAPPDATA%\AIBar\codex-remote-usage.json`, identical to `claude-remote-usage.json`: UTF-8 JSON ≤ 4096 bytes, properties in order `schemaVersion` (1), `generatedAt` (UTC `Z`), `state` (`current` | `authentication-required` | `unavailable`), `fiveHour`, `weekly` (null or `{percentageUsed: 0–100, resetAt: UTC Z, required}`), optional trailing `plan`. Non-current → both windows null; current → at least one window. Nothing else (no token, account, email, path, error text).

## Tasks

- T1 [S1] Linux helper `codex_remote_usage.py`: reads the Gentle Shell/Pi `openai-codex` OAuth entry (`<agent-dir>/auth.json`: `access`, `expires` ms, `accountId`; agent dir from `GENTLE_SHELL_HOME`, `PI_CODING_AGENT_DIR`, else `~/.gentle-shell/agent`, then `~/.pi/agent`) read-only, never refreshes; GET `https://chatgpt.com/backend-api/wham/usage` (Bearer + `ChatGPT-Account-Id`); maps windows by `limit_window_seconds` (18000 → fiveHour, 604800 → weekly); writes the contract; pushes with the existing atomic push; service unit gains `ExecStartPre=-` step; unittest tests; README — worker (parallel) — commit: edaddbe
- T2 [S1, S2] AIBar: Codex remote provider reading the contract with the existing strict reader; Codex Source Automatic resolves to remote when the file exists, Other machine always remote; no local credential reads while remote; panel/dock/tray/YASB/alerts show remote values with the 15-minute staleness rule; tests; docs — worker (parallel) — commit: pending
- T3 [S1-S3] CI, independent verification (credential gating), deploy helper on Linux and AIBar on Mars (with consent for credential-reading runs), live check, close #85 — inline — commit: pending

## Log

- L1 (user): "hace le merge nomas" → PRs #84/#86 merged; then "dale nomas encaralo" (issue #85).
- L2 (evidence, explore): Pi stores `"openai-codex": {type:"oauth", access, refresh, expires, accountId}` in `<agent-dir>/auth.json` (pi-ai openai-codex.js:95–105,241–258); Gentle Shell defaults the agent dir to `~/.gentle-shell/agent` (gentle-shell-launcher.ts:205–223) and already calls `wham/usage` with that token (gentle-shell.ts:1712–1730). Windows parser: QuotaHttpProvider.cs:111–190. Claude reader: ClaudeRemoteUsage.cs:34–153; Claude source runtime: ClaudeQuotaRuntime.cs:115–147,324–473. Codex Other machine today: BetaRuntime.cs:44–161 masks with `quota_other_machine_pending`.
- L3 (decisions): valid remote Codex quota feeds alerts like Claude remote (queued local alerts stay invalidated across handoff); cost rules from #86 unchanged (Automatic local+remote tokens, This PC local, Other machine remote); Automatic with remote quota keeps local analytics. Risk: item 2 (helper reads the Codex OAuth token on Linux; AIBar must not read the local credential while remote) and item 5 (systemd unit + Windows deploy) → independent verification. The agent never opens credential files; live helper runs need the user's consent.
- L4 (routing): T1 (Python/Linux) and T2 (C#/Windows) are independent given the contract, each several files/100+ lines → two background workers, disjoint surfaces.
- L5 (evidence): T1 worker strict TDD — RED 12 new tests failed (module missing), 51 existing passed; GREEN 63/63; final 64/64 (parent re-ran locally: 64/64). Parent aligned the service line to /usr/bin/python3.
- L6 (process): parent mistake — 67ce12a (titled docs) was made with commit -a and also captured T2 STEP 1 test edits (CodexDataSourceTests.cs, SettingsWindowTests.cs) and the BetaRuntime constructor stub. History rewrite was blocked by the safety policy, so it stays; the remaining new test file is committed next. T2 RED = 67ce12a + the next commit. Use explicit paths, never commit -a, while workers edit the tree.
