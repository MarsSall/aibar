# AIBar feasibility exploration

**Date:** 2026-07-12  
**Repository state:** newly initialized, empty Windows-app repository; no stack or test runner selected.

## Executive conclusion

A useful, supportable MVP is feasible for **OpenAI API platform accounts**: show configured-project model availability, recent API usage/cost data where the credential is authorized, and request-derived rate-limit signals. It is **not** feasible to promise a unified view of ChatGPT consumer subscription limits or Codex consumer quota through documented public APIs. Those concerns must be separate product surfaces, with explicit “not available” states rather than scraping or reusing browser credentials.

## Evidence map (official OpenAI platform)

| Need | Official surface | Feasibility / boundary |
|---|---|---|
| Available models | `GET https://api.openai.com/v1/models` ([API reference](https://platform.openai.com/docs/api-reference/models/list)) | Available to the authenticated API organization/project; not a promise of entitlement, capacity, or ChatGPT plan access. |
| Request usage | Organization Usage API, under `/v1/organization/usage/...` ([usage docs](https://platform.openai.com/docs/api-reference/usage)) | Available to organization/admin-authorized credentials; aggregate and time-bucketed data, not necessarily instant per-request accounting. |
| Costs | `GET /v1/organization/costs` ([costs docs](https://platform.openai.com/docs/api-reference/usage/costs)) | Organization billing usage; authorization and retention/detail limits apply. Do not treat it as an invoice or consumer subscription balance. |
| Rate limits | Response headers such as `x-ratelimit-limit-*`, `x-ratelimit-remaining-*`, and `x-ratelimit-reset-*`; documented in [rate-limit guidance](https://platform.openai.com/docs/guides/rate-limits) | Best source for the current request’s model/org limits. A tray app cannot infer every hidden quota or guarantee future availability. |
| Projects, keys, admin controls | Admin/organization APIs ([API reference](https://platform.openai.com/docs/api-reference/organization)) | Requires elevated organization permissions. Never require an admin key for ordinary model calls. |
| Billing/account status | No general documented endpoint that exposes a user-friendly API billing balance, invoice status, or account subscription state to an arbitrary local app | Link users to the platform billing page; report only data returned by authorized APIs. |

**Important:** endpoint paths and authorization requirements change. Validate against the current API reference during implementation; avoid hard-coding undocumented endpoints.

## API platform vs ChatGPT/Codex consumer access

- **API platform:** pay-as-you-go organization/project, API keys, model catalog, usage/cost reporting, and request rate-limit headers.
- **ChatGPT consumer plans:** plan-specific message/model/Codex limits belong to the ChatGPT product. They are not equivalent to API credits and are not documented as a general public API for third-party tray applications.
- **Codex consumer quota:** do not assume the API Usage API exposes ChatGPT/Codex plan quota. Unless OpenAI publishes a supported endpoint and authorization flow, AIBar must show “unsupported/not connected” and link to the relevant first-party UI.
- AIBar must not scrape ChatGPT pages, automate private endpoints, extract session cookies, or ask users for passwords. Those approaches are brittle, high-risk, and may violate product terms.

## Authentication options

| Approach | Security posture | MVP assessment |
|---|---|---|
| User supplies an OpenAI API key | Key is a bearer secret; local storage, logs, crash dumps, screenshots, and malware are risks. | Support only with secure Windows Credential Manager/DPAPI storage, masked entry, no telemetry of secrets, and clear revoke instructions. Prefer project-scoped/restricted keys. |
| User opens a first-party API-key page | Keeps account login outside AIBar; user pastes only the intended key. | Practical initial flow, though clipboard exposure remains possible. |
| OAuth/device flow for OpenAI API | Only use if OpenAI documents and permits it for this client. | Do not invent an OAuth flow or use ChatGPT cookies. |
| Admin key for usage/cost endpoints | High blast radius. | Optional advanced configuration, separate from request key, with explicit warning; do not require for basic status. |

## Windows tray architecture options

| Option | Strengths | Tradeoffs |
|---|---|---|
| Native WinUI 3 / Windows App SDK | First-party Windows UX, modern notifications/settings, strong OS integration. | More setup and deployment complexity; Windows-only and heavier toolchain. |
| WPF/.NET | Mature tray/window ecosystem, fast development, excellent Windows integration. | Older UI model; modern packaging/visual polish needs deliberate work. |
| WinUI/WPF shell + background worker | Separates polling/security/networking from UI; testable boundaries. | More lifecycle and IPC complexity than a single process. |
| Tauri/Electron | Familiar web UI and cross-platform potential. | Runtime footprint, tray edge cases, and larger credential-exposure surface; likely excessive for an initially Windows-only utility. |

Evidence does not force a final stack. The architecture should isolate provider adapters, credential storage, polling/backoff, and presentation so the stack can be chosen during design.

## Plausible MVP boundary

**In scope**

1. Windows tray icon with explicit connection state.
2. One OpenAI API configuration (project-scoped key; optional organization/project identifiers).
3. Manual refresh plus conservative periodic polling.
4. Model list from the authenticated API endpoint.
5. Recent API usage/cost summary only when the credential is authorized; otherwise a clear unavailable state.
6. Rate-limit headers captured from AIBar requests, with timestamp and stale-data indication.
7. Secure secret storage and “remove key” action.
8. Links to OpenAI API keys, usage, and billing pages.

**Out of scope for MVP**

- ChatGPT consumer plan/quota display.
- Codex CLI/browser session introspection.
- Scraping, cookie/session import, password collection, or private endpoint calls.
- Automatic key creation, billing changes, or organization administration.
- Exact real-time spend, invoice reconciliation, or prediction of hidden quota.
- Multi-provider support and cross-platform packaging.

## Risks and unknowns

- OpenAI may change endpoint schemas, permissions, retention, or rate-limit headers.
- Usage/cost data may be delayed and may require admin scopes unavailable to normal keys.
- A key-bearing desktop app is a local secret-management product; malware/endpoint compromise cannot be eliminated.
- Tray polling can itself consume quota or create misleading “health” signals.
- Organization/project selection is ambiguous for users with multiple workspaces.
- Product/legal review is needed for naming and any Codex/ChatGPT integration claims.
- Confirm whether current OpenAI terms and API policies permit the intended local-client distribution and telemetry (default should be zero secret/content telemetry).

## Questions before proposal

1. Is AIBar explicitly API-platform-only for MVP, or is consumer ChatGPT/Codex visibility a launch requirement?
2. Will the target user own an API key, or must the product support a documented first-party login flow?
3. Are organization-level usage/cost metrics required, or is model/rate-limit status sufficient without admin credentials?
4. What Windows versions, packaging/signing, and enterprise deployment constraints apply?
5. Is any telemetry desired? Recommended default: none for secrets, prompts, responses, or account identifiers.

## CodexBar feasibility note

CodexBar is useful as an implementation-feasibility reference, not an authority for supported OpenAI integration. Its repository describes a macOS menu-bar product and provider-specific adapters: [CodexBar repository](https://github.com/steipete/CodexBar). It has historically obtained some provider status through local CLI/account state and provider web/session mechanisms rather than a single universal public API. That demonstrates UI/polling feasibility but **must not be copied as a security or compatibility assumption**. Before relying on any exact adapter behavior, pin a reviewed commit and inspect the relevant OpenAI/Codex provider source; this exploration intentionally avoids claiming undocumented consumer endpoints as supported.

## Recommended next phase

Run proposal clarification focused on the five questions above. The proposal should choose the first-slice promise: **“OpenAI API project status”**, not “all OpenAI/ChatGPT/Codex limits.”
