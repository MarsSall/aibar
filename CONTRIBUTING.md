# Contributing to AIBar

Thanks for helping. AIBar handles quota data and reads local CLI logins, so changes are expected to stay small, tested and explicit about what they touch.

## Setup

- Windows x64 with the .NET SDK version pinned in [`global.json`](./global.json) (`rollForward` is disabled).
- Python 3.12 for the Linux remote-usage helpers.
- [Pester 5.7.1](https://pester.dev) for the YASB reader tests.

```powershell
dotnet restore AIBar.sln
dotnet build AIBar.sln
dotnet test AIBar.sln
python -m unittest discover -s tools/claude-remote-usage/tests -v
```

CI runs the same three suites on every pull request (see [`.github/workflows/ci.yml`](./.github/workflows/ci.yml)).

## Workflow

1. Open an issue first for anything beyond a small fix, so the scope is agreed before code.
2. Branch from `main` (`feat/...`, `fix/...`, `docs/...`, `test/...`).
3. Write the failing test first when the change is testable, then make it pass.
4. Keep pull requests reviewable, roughly 400 changed lines or fewer. Split larger work into stacked PRs.
5. Use [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`.
6. Update the matching page in [`docs/`](./docs) when you change user-visible behavior.

## Ground rules

- **Credentials are read-only.** Never refresh, rotate, write or log tokens. Changes to `CredentialBoundary` or any provider need tests that prove this.
- **Consent first.** A provider stays off until its own consent is accepted, and disabling it must stop requests and clear its cache.
- **No new network destinations** without discussion in an issue. AIBar has no backend and no telemetry.
- **Sanitized exports.** Anything written for YASB or the clipboard must not include credentials, paths, prompts or account identifiers.
- Technical artifacts (code, comments, commits, docs) are written in English.

## Repository layout

User documentation lives in [`docs/`](./docs). Developer and test-design notes live in [`docs/dev/`](./docs/dev/README.md). `openspec/` and `odd/` hold the specifications and task logs used to plan changes.

## Reporting security issues

Do not open a public issue. Follow [SECURITY.md](./SECURITY.md).
