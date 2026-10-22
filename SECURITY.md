# Security policy

AIBar reads existing Codex CLI and Claude Code login files on your machine. Issues that could expose those credentials or the data AIBar stores locally are taken seriously.

## Supported versions

Only the latest private-beta build on `main` receives fixes.

## Reporting a vulnerability

Please **do not open a public issue**. Report it privately through [GitHub security advisories](https://github.com/MarsSall/aibar/security/advisories/new).

Include:

- what an attacker could read, change or trigger;
- steps or a proof of concept to reproduce it;
- the AIBar version or commit you tested.

You will get an acknowledgement as soon as possible. A fix and a coordinated disclosure date will be agreed with you.

## In scope

- Reading, logging, exporting or sending credentials or tokens.
- Writing to or rotating official CLI login files.
- Leaking prompts, paths or account identifiers through the YASB snapshot, the clipboard summary, logs or the remote-usage helpers.
- Network requests to destinations other than the provider and status endpoints documented in the README.

## Out of scope

- Changes in the undocumented provider endpoints that AIBar depends on.
- Attacks that need prior administrator or same-user code execution on the machine.
