# Claude usage from another machine (optional)

Keep **This PC** for the normal Windows Claude Code login. This helper is only
for choosing the Linux Claude Code session instead. Windows AIBar must enable
Claude quota, accept its disclosure, and select **Claude source: Other machine**.
The helper does not change AIBar's default source or enable consent.

## What it reads and sends

Python 3.12 and OpenSSH clients are sufficient; there are no Python dependencies.
The helper opens `.credentials.json` read-only under `CLAUDE_CONFIG_DIR` (trimmed),
or `~/.claude` by default. Only `claudeAiOauth.accessToken` and `expiresAt`
(milliseconds since Unix epoch) are used. It never uses or sends refresh tokens,
refreshes a login, or writes credentials. Missing expiry is allowed, matching
AIBar's local provider; an invalid expiry is unavailable.

It calls `https://api.anthropic.com/api/oauth/usage` with the access token,
`anthropic-beta: oauth-2025-04-20` and `anthropic-version: 2023-06-01`.
Redirects are rejected; the request timeout is ten seconds. This unofficial
endpoint is unsupported and may change. A login expiring within 60 seconds is
not sent; expired/401/403 results become `authentication-required`.
Other failures become `unavailable`; no login refresh is attempted.

Only this UTF-8, schema-v1 contract (at most 4096 bytes) is pushed to Windows:

```json
{"schemaVersion":1,"generatedAt":"2026-10-18T02:00:00Z","state":"current","fiveHour":{"percentageUsed":32,"resetAt":"2026-10-18T06:27:00Z"},"weekly":null}
```

The property order is fixed. Windows receive path:
`%LOCALAPPDATA%\AIBar\claude-remote-usage.json`. Windows must run under the same
account/home as the configured SSH destination. Usage is 0–100; reset timestamps
are normalized to UTC `Z` and required: a window without a reset time is sent as
null. Non-current states have both windows null. Tokens, account/plan/email, paths, error details and HTTP bodies
are never output, logged, or pushed. Normal logs contain only state and UTC time.
The token is necessarily handled in Python process memory, not erased securely.

## Try once, then install (explicit opt-in)

First ensure key-based SSH to your Windows alias is already trusted and working,
with Windows OpenSSH/SFTP and Windows PowerShell available. Create the destination
`AppData/Local/AIBar` under that Windows account's home if it does not exist.
The helper neither creates the remote directory nor changes SSH trust settings.

Run these commands from this helper directory on Linux **only when ready**:

```sh
python3 claude_remote_usage.py --dry-run
install -d -m 700 ~/.config/aibar ~/.local/lib/aibar ~/.config/systemd/user
install -m 600 claude-remote-usage.conf.example ~/.config/aibar/claude-remote-usage.conf
# Edit host and remote_dir in that config before the first push.
install -m 700 claude_remote_usage.py ~/.local/lib/aibar/claude_remote_usage.py
install -m 644 aibar-claude-remote-usage.service aibar-claude-remote-usage.timer ~/.config/systemd/user/
python3 ~/.local/lib/aibar/claude_remote_usage.py --once
systemctl --user daemon-reload
systemctl --user enable --now aibar-claude-remote-usage.timer
```

`--once` is the default. `--dry-run` prints only the safe contract JSON and never
opens the push config or runs SSH. `CLAUDE_CONFIG_DIR` can be set in a user service
drop-in with `Environment=CLAUDE_CONFIG_DIR=/your/claude/config` if needed; do not
put tokens in service units or environment variables. The timer runs every five
minutes, catches missed calendar runs with `Persistent=true`, and runs only while
the user service manager is available (normally while logged in).

The real push config is `~/.config/aibar/claude-remote-usage.conf`. It contains
exactly `host` (a simple SSH alias/hostname) and `remote_dir` (home-relative forward
slash-separated directory segments using letters, digits, `_` and `-`). Spaces,
dots, absolute paths and shell syntax in directories are intentionally rejected.

The helper creates a mode-0600 local temporary file, uses `scp` to stage
`claude-remote-usage.json.tmp`, then `ssh` with PowerShell `Move-Item -Force` to
replace the final file in that same directory. Both use argument lists,
`BatchMode=yes`, `ConnectTimeout=10` and a 30-second process timeout. Failures exit
non-zero, preserve the last published file, and attempt to remove remote staging.
If Windows is unreachable, staging cleanup cannot be guaranteed; a subsequent
successful run overwrites it. Do not run concurrent helper instances against the
same destination. AIBar treats data older than 15 minutes as stale, not current.

## Troubleshoot or remove

- `authentication-required`: sign in again with Claude Code **on Linux**.
- `unavailable`: check the chosen config directory/login and endpoint availability.
- A non-zero push: check the SSH alias, existing destination directory, SFTP,
  PowerShell, permissions and trusted host key separately. No interactive prompts
  or raw SSH errors are exposed by the helper.
- Inspect scheduling with `systemctl --user status aibar-claude-remote-usage.timer`
  and state-only logs with `journalctl --user -u aibar-claude-remote-usage.service`.
- To uninstall, run `systemctl --user disable --now aibar-claude-remote-usage.timer`,
  stop any active service with `systemctl --user stop aibar-claude-remote-usage.service`,
  then remove the two installed unit files, installed helper, and
  `~/.config/aibar/claude-remote-usage.conf`; run `systemctl --user daemon-reload`.
  Select **This PC** or disable Claude quota in AIBar. Remove the remote JSON and
  `.tmp` file separately if desired; never remove Claude Code credentials.

## Tests (no live credentials, HTTP or SSH)

```sh
python3 -m unittest discover -s tools/claude-remote-usage/tests -v
```

Run from the repository root. Fixtures use synthetic tokens, fake HTTP/process
runners and self-cleaning temporary directories inside this helper directory.
