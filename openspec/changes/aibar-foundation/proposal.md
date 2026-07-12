# AIBar foundation proposal

## Decision summary

AIBar will be a Windows tray application for people who use the OpenAI Codex CLI. The tray will provide an at-a-glance quota percentage, while a popover panel will explain the 5-hour and weekly quota windows, reset countdowns, data freshness, errors, local token usage, model mix, estimated costs, and simple usage pace.

The initial product explicitly accepts the compatibility and policy risk of reading the local Codex CLI authentication/session state and calling private ChatGPT `/backend-api/wham/*` quota endpoints. Those endpoints are undocumented, are not officially supported for third-party applications, and may change or disappear without notice. AIBar must communicate that limitation rather than presenting the integration as stable.

No application stack is selected by this proposal.

## Problem

Codex CLI users cannot quickly see how much of their short and weekly quota windows they have consumed, when those windows reset, or whether their current pace is likely to exhaust the remaining quota. Local Codex sessions also contain useful token activity, but turning those logs into understandable daily and model-level analytics currently requires manual inspection or a separate tool.

The absence of a compact status surface causes users to interrupt their work, consult first-party interfaces, or make decisions from incomplete information. Existing reference applications demonstrate feasibility but rely on private service behavior and analytics assumptions that AIBar must expose honestly.

## Target users

The MVP targets individual Windows developers who:

- use the OpenAI Codex CLI on the same Windows account as AIBar;
- want quota status available without keeping another application open;
- want local, aggregate token analytics without collecting prompt or response content; and
- understand that consumer quota access depends on private, unsupported endpoints.

The first release is OpenAI/Codex-only. Teams, administrators, multi-account users, WSL-heavy workflows, and users needing authoritative billing records are not the initial target.

## Goals

1. Make current Codex quota consumption visible at a glance from the Windows tray.
2. Explain both the 5-hour and weekly windows, including reset countdowns and freshness.
3. Fail visibly and safely when credentials, local data, or private endpoints are unavailable.
4. Produce privacy-conscious, incremental daily analytics from local Codex session logs.
5. Separate service-reported quota data, locally derived analytics, and estimated costs in both behavior and language.
6. Support optional startup with Windows.
7. Deliver a useful experience on Windows 10 and Windows 11 when that does not add disproportionate complexity; prefer Windows 11 behavior when a concrete incompatibility requires a choice.

## Non-goals

The MVP will not:

- support providers other than OpenAI/Codex;
- claim that private ChatGPT quota endpoints are public, stable, documented, or officially supported;
- provide authoritative billed cost, invoices, credits, or OpenAI API-platform organization usage;
- collect, store, transmit, index, or display prompt or response content;
- extract browser cookies, request passwords, or automate a browser session;
- support multiple Codex roots, accounts, or WSL installations;
- provide per-project analytics;
- promise complete deduplication across forks, replay, cumulative resets, or historical schema variants;
- reconstruct hourly history from daily aggregates;
- provide probabilistic or historical forecasting before enough complete history exists; or
- select the implementation language, UI framework, packaging system, or application architecture.

## Scope

### MVP

#### Tray and popover

- A Windows tray icon showing the current percentage used for the primary quota window.
- A popover panel showing:
  - 5-hour percentage used and reset countdown;
  - weekly percentage used and reset countdown;
  - last successful refresh and an explicit stale indicator;
  - loading, unavailable, authentication, permission, malformed-response, and network/service error states;
  - manual refresh; and
  - optional startup with Windows.

If a quota value cannot be obtained, the tray must use an unavailable/error presentation rather than showing a fabricated percentage or silently retaining an apparently current value.

#### Local analytics

- Incremental scanning of the initial supported local Codex session root and archives.
- Daily input, cached-input, and output token totals.
- Per-model breakdown with an explicit `Unknown` bucket when model evidence is absent.
- A documented most-used-model definition based on total tokens. The product must state the exact formula and whether cached input is included in input rather than ranking by estimated cost.
- Estimated cost from versioned pricing data, with visible provenance date/version, unsupported-model warnings, and a warning that repricing or provider billing rules can change the result.
- Clearly named trends, such as a documented daily or rolling token window.
- Simple, labeled linear pace, burn rate, and exhaustion ETA. The estimate must state its window and must not be represented as a prediction guarantee.
- Scan coverage, last-scan status, and partial-data warnings where unreadable, malformed, truncated, or changing logs can cause under-counting.

### Later

- Historical or probabilistic prediction only after at least 3 complete weeks of history; run-out probability or similarly stronger claims require at least 5 complete weeks.
- Hourly or event-level retention and charts.
- Per-project analytics after an explicit privacy review.
- Multiple roots, accounts, and WSL discovery with explicit attribution.
- Advanced fork, replay, cross-file, and lineage-aware deduplication.
- Broader provider support.

## Product behavior and data boundaries

| Data class | Source and authority | Product treatment |
|---|---|---|
| Private quota data | Current response from private ChatGPT `/backend-api/wham/*` endpoints, using local Codex CLI auth state | Authoritative only for the quota fields returned by that response at that moment. Show percentage, resets, timestamp, and stale/error state. Never imply endpoint stability, public support, or future availability. |
| Local derived analytics | Incremental calculations from local Codex session JSONL events | Label as locally derived. Preserve coverage and scan provenance. Use non-negative token deltas, tolerate partial records, and assign missing model evidence to `Unknown` rather than guessing. Do not treat local totals as provider billing records. |
| Estimated costs | Local token totals multiplied by versioned pricing assumptions | Always label as estimates. Show pricing provenance and warnings for unknown models, changing prices, discounts, routing, contracts, and billing adjustments. Never label the result as billed cost or an invoice. |

Private quota data and local analytics may be shown together for convenience, but they must remain conceptually and visually distinguishable. Local token consumption must not be presented as the source of the service-reported quota percentage.

Refresh behavior should be conservative and cache-aware. The product should retain the last successful snapshot with its timestamp, mark it stale when appropriate, prevent overlapping refreshes, and avoid a polling pattern that creates unnecessary service load. A failed optional quota detail request must not erase an otherwise valid primary quota snapshot.

Countdowns and analytics must state the relevant time basis. Daily analytics use an explicit local timezone policy; quota resets follow the service-reported reset time.

## Privacy and security posture

AIBar is local-first. Initial processing and storage occur on the user's device, and telemetry is off by default.

- Never collect or process prompt or response bodies for analytics.
- Read only the minimum fields required from Codex-owned authentication and session files.
- Do not modify Codex CLI auth or session files.
- Do not persist access tokens in AIBar-owned plaintext storage or expose them in UI, logs, diagnostics, crash data, or error messages.
- Keep credentials in memory only as long as needed for requests and redact bearer values, account identifiers where sensitive, secret fields, and headers from diagnostics.
- Do not copy refresh-token behavior, embedded OAuth client assumptions, browser-cookie extraction, manual cookie headers, or machine-wide secret protection without a separate reviewed decision.
- Treat file access, local caches, and diagnostics as sensitive because session metadata may reveal model activity, timestamps, paths, or project names even without prompt content.
- Default analytics to aggregate values. Project paths and names are outside MVP and require opt-in privacy controls in a later phase.
- Clearly disclose that AIBar relies on private endpoint behavior and credentials owned by the Codex CLI. Authentication failures should direct users to restore the Codex CLI session rather than asking AIBar for their password.
- Provide a way to clear AIBar-owned caches and derived analytics without deleting Codex-owned source files.

Before distribution, implementation planning must confirm current policy and legal constraints for use of the private endpoints and third-party reuse of Codex CLI credentials. Source-code reuse from investigated MIT repositories requires preserved notices and provenance; architectural inspiration alone does not make private endpoints supported.

## Risks and mitigations

| Risk | Impact | Required response |
|---|---|---|
| Private endpoint or schema changes | Quota display may stop working without warning | Isolate the integration boundary, tolerate missing fields, show explicit unsupported/error states, and avoid compatibility promises. |
| Policy or contractual restrictions | Distribution or endpoint use may become unacceptable | Complete policy/legal review before release and retain the ability to disable the private integration. |
| Credential exposure | Account/session compromise | Minimize reads and lifetime, redact diagnostics, avoid copying secrets, and never use browser cookies or passwords. |
| Stale or unavailable quota data | Users make decisions from old information | Timestamp every snapshot and make stale/unavailable states prominent in tray and panel. |
| Log schema drift or partial files | Analytics under-count or misattribute usage | Use tolerant streaming parsing, explicit coverage status, `Unknown` attribution, and versioned scan provenance. |
| Forks, replay, or cumulative resets | Double counts or spikes | Apply safe component deltas in MVP, disclose limitations, and reserve advanced deduplication for later. |
| Incorrect cost assumptions | Users mistake estimates for bills | Label every cost as estimated and expose pricing provenance and warnings. |
| Timezone or daylight-saving changes | Daily buckets or countdown interpretation shifts | Store sufficient timestamp/timezone provenance and define normalization behavior. |
| Large histories | Slow startup or excessive I/O | Scan incrementally in the background with checkpoints, cancellation, and bounded work. |
| Windows version differences | Inconsistent tray, startup, or popover behavior | Prefer low-complexity Windows 10/11 compatibility; choose Windows 11 behavior when incompatibility is concrete and document the limitation. |

## Measurable acceptance outcomes

The foundation is successful when all of the following are demonstrably true:

1. On a supported authenticated Codex CLI setup, the tray displays the current primary quota percentage and the panel displays both 5-hour and weekly percentages with reset countdowns and a refresh timestamp.
2. When quota data is stale, missing, malformed, unauthorized, or unreachable, the tray and panel show a distinguishable stale/unavailable/error state and do not present old data as current.
3. The user can manually refresh and can enable or disable startup with Windows.
4. On representative Windows 10 and Windows 11 environments, the tray and popover work unless a documented concrete incompatibility justifies Windows 11-only behavior for that feature.
5. Incremental rescanning of unchanged fixture histories does not increase daily totals; appended supported events add only their non-negative input, cached-input, and output deltas.
6. Missing model evidence is reported under `Unknown`, never silently assigned to a named model.
7. The model breakdown and most-used model match the documented token formula for representative fixtures.
8. Every cost value is labeled estimated and exposes pricing provenance plus warnings for unknown models and non-authoritative billing assumptions.
9. Trends and linear pace/burn/exhaustion ETA identify their calculation window and are not labeled as historical or probabilistic predictions.
10. Truncated, malformed, unreadable, or partially supported logs produce coverage warnings without exposing prompt/response content.
11. Inspection of application-owned persistence, logs, and diagnostics finds no prompt/response bodies or unredacted Codex bearer credentials.
12. Clearing AIBar-owned data removes its cached quota and derived analytics without changing Codex-owned files.

## Delivery and review workload

The 400-changed-line review budget applies to each reviewable implementation unit. The foundation should be planned as independently reviewable slices rather than one cross-cutting delivery:

1. quota/auth adapter contract and private-endpoint failure model;
2. refresh cache and stale-state behavior;
3. tray and popover quota presentation;
4. incremental local log scanner and daily aggregation;
5. model breakdown, cost estimation, and provenance;
6. trends and simple linear pace/burn/ETA;
7. startup setting, privacy controls, diagnostics, and Windows compatibility hardening.

Task planning must forecast authored changed lines per slice and split any unit expected to exceed 400 lines before implementation. Security-sensitive credential handling and private endpoint integration require focused risk review. Refresh/cache behavior and incremental analytics require reliability review. Windows startup and OS integration require resilience review. Generated fixtures or goldens may be reviewed separately from authored logic while remaining bound to the same behavior contract.

This proposal does not authorize implementation. The next phase should define requirements and acceptance scenarios without selecting a stack; design can then evaluate technology and architecture tradeoffs against those requirements.
