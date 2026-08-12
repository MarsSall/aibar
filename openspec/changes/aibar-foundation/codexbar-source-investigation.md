# CodexBar source investigation

**Pinned sources:** `Finesssee/Win-CodexBar@4e9ab06460a266ee5eea500e7ec5e212b42472a1` and `steipete/CodexBar@9a6c74cfed418ddcd01f9559bb7a80368e5245dd`  
**Investigation date:** 2026-07-12

## Bottom line

Win-CodexBar obtains **Codex consumer quota** by reusing credentials written by the Codex CLI and calling undocumented ChatGPT backend endpoints. That is not the same product or data source as **OpenAI API-platform usage**, which upstream CodexBar implements separately with an API key against documented `api.openai.com/v1/organization/...` endpoints. AIBar can safely adapt the provider/cache/event/UI separation, but it should not silently adopt private ChatGPT endpoints, browser-session extraction, or plaintext CLI-token trust; each requires an explicit product and security decision.

## Windows architecture and data flow

```text
CODEX_HOME/auth.json or ~/.codex/auth.json
  └─ OPENAI_API_KEY, else tokens.access_token + optional account_id
      └─ Rust CodexProvider / CodexApi
          ├─ GET chatgpt.com/backend-api/wham/usage
          └─ best-effort GET .../wham/rate-limit-reset-credits
              └─ AppState provider cache + refresh coordinator
                  ├─ provider-updated / refresh-started / refresh-complete events
                  ├─ native tray status/icon/tooltip update
                  └─ React useProviders cache-first state → tray/window surfaces
```

The provider delegates directly to its Rust API client and labels the result `oauth` ([`mod.rs` L51-L80](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/mod.rs#L51-L80)). The Tauri coordinator suppresses overlapping or fresh-cache refreshes, runs enabled providers concurrently, updates the cache, emits per-provider and lifecycle events, and then updates the native tray ([`providers.rs` L153-L215](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src-tauri/src/commands/providers.rs#L153-L215), [`providers.rs` L247-L289](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src-tauri/src/commands/providers.rs#L247-L289), [`providers.rs` L387-L410](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src-tauri/src/commands/providers.rs#L387-L410)). React first reads cached snapshots, merges `provider-updated` events, tracks refresh events, and requests a stale-aware initial refresh ([`useProviders.ts` L51-L92](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src/hooks/useProviders.ts#L51-L92), [`useProviders.ts` L119-L212](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src/hooks/useProviders.ts#L119-L212)).

## Authentication and endpoints

### Codex consumer quota

- The Windows client resolves `auth.json` from `CODEX_HOME` when non-empty, otherwise `~/.codex/auth.json` ([`api.rs` L232-L242](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L232-L242)).
- Within that file it prefers top-level `OPENAI_API_KEY`; otherwise it requires `tokens.access_token` and optionally reads `tokens.account_id` ([`api.rs` L157-L195](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L157-L195)). Despite the field name, this value is then used as the bearer credential for the ChatGPT consumer-quota endpoint—not the official organization-usage API.
- The default base is `https://chatgpt.com/backend-api`; usage is `GET /wham/usage` with `Authorization: Bearer ...` and optional `ChatGPT-Account-Id`. HTTP 401/403 becomes an authentication error ([`api.rs` L12-L15](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L12-L15), [`api.rs` L40-L78](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L40-L78)).
- It makes a best-effort second request to `/wham/rate-limit-reset-credits`; failure does not fail the main usage result, while a positive count becomes an extra rate window ([`api.rs` L86-L128](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L86-L128)).

These `/wham/...` routes are private ChatGPT backend surfaces in the inspected code; the repositories do not establish a public compatibility or stability contract for them.

### OpenAI API-platform usage

Upstream CodexBar treats API-platform usage as a separate provider: it accepts an API key and optional project ID, calls `https://api.openai.com/v1/organization/costs` and `/v1/organization/usage/completions`, adds `project_ids` when configured, and authenticates with the API key as a bearer token ([`OpenAIAPIUsageFetcher.swift` L35-L89](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/OpenAI/OpenAIAPIUsageFetcher.swift#L35-L89), [`OpenAIAPIUsageFetcher.swift` L210-L238](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/OpenAI/OpenAIAPIUsageFetcher.swift#L210-L238)). An OpenAI API key and organization cost/usage therefore do **not** prove access to ChatGPT/Codex consumer quota.

## Windows security, storage, fallback, and refresh

- The Codex provider reads the Codex CLI's existing `auth.json` directly. It keeps parsed credentials in memory for at most five seconds and invalidates that cache when path or modification time changes ([`api.rs` L131-L155](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L131-L155), [`api.rs` L198-L229](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L198-L229)). The inspected Windows parser retains only access token and account ID; it does not load or rotate a refresh token ([`api.rs` L157-L195](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/providers/codex/api.rs#L157-L195)). Re-login or an external Codex CLI rewrite is therefore the observed credential-renewal path.
- The application has a generic secure-file helper that DPAPI-protects local secret-bearing JSON, preferring user scope and falling back to machine scope; non-Windows writes are plaintext ([`secure_file.rs` L95-L138](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/secure_file.rs#L95-L138)). This helper does not protect the externally owned Codex CLI `auth.json` read by `CodexApi`.
- Its generic Windows browser importer decrypts Chromium keys with DPAPI, supports legacy AES-GCM cookie formats, explicitly rejects Chrome/Edge `v20` App-Bound Encryption, and suggests manual cookies or Firefox when ABE blocks import ([`cookies.rs` L44-L53](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/browser/cookies.rs#L44-L53), [`cookies.rs` L312-L365](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/browser/cookies.rs#L312-L365), [`cookies.rs` L383-L422](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/browser/cookies.rs#L383-L422)). This is application-wide browser support, not the Windows Codex OAuth provider's normal auth source.
- Errors are passed through a redactor that masks bearer values, cookie headers, secret query parameters, JSON secret fields, and common API-key forms before logs or frontend-visible errors ([`redactor.rs` L20-L74](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/redactor.rs#L20-L74)).
- Refresh behavior is cache-first and event-driven. Manual refresh bypasses freshness; mount refresh can reuse a fresh backend cache; timed React refresh occurs after the next known quota reset ([`providers.rs` L153-L215](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src-tauri/src/commands/providers.rs#L153-L215), [`useProviders.ts` L197-L212](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src/hooks/useProviders.ts#L197-L212), [`useProviders.ts` L252-L287](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/apps/desktop-tauri/src/hooks/useProviders.ts#L252-L287)).

## Differences from upstream macOS

- Upstream's Swift credential model requires and retains a refresh token for OAuth credentials, tracks `last_refresh`, and considers credentials stale after eight days; it still prefers top-level `OPENAI_API_KEY` when parsing the general credential file ([`CodexOAuthCredentials.swift` L10-L35](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthCredentials.swift#L10-L35), [`CodexOAuthCredentials.swift` L92-L153](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthCredentials.swift#L92-L153)).
- Upstream can POST the refresh token to `https://auth.openai.com/oauth/token`, classify expired/reused/revoked failures, and return rotated tokens ([`CodexTokenRefresher.swift` L6-L85](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexTokenRefresher.swift#L6-L85), [`CodexTokenRefresher.swift` L88-L109](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexTokenRefresher.swift#L88-L109)); the inspected Windows Codex client has no equivalent refresh-token path.
- Upstream's OAuth usage fetch uses the same default ChatGPT base, usage path, bearer header, and optional account header, and also supports the reset-credit route ([`CodexOAuthUsageFetcher.swift` L343-L378](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthUsageFetcher.swift#L343-L378), [`CodexOAuthUsageFetcher.swift` L409-L444](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthUsageFetcher.swift#L409-L444)).
- Upstream additionally has a macOS-only browser-cookie/WebKit dashboard route for `chatgpt.com` and `openai.com`, including cookie caching, manual-cookie input, and Keychain-sensitive browser import ([`OpenAIDashboardBrowserCookieImporter.swift` L104-L106](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/OpenAIWeb/OpenAIDashboardBrowserCookieImporter.swift#L104-L106), [`OpenAIDashboardBrowserCookieImporter.swift` L145-L193](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/OpenAIWeb/OpenAIDashboardBrowserCookieImporter.swift#L145-L193), [`OpenAIDashboardBrowserCookieImporter.swift` L1033-L1051](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/OpenAIWeb/OpenAIDashboardBrowserCookieImporter.swift#L1033-L1051)). That is distinct from the Windows Codex provider's CLI-file OAuth path.

## Adaptation and reuse boundary for AIBar

### Safe to adapt conceptually

- Provider interfaces separated from refresh orchestration and presentation.
- Cache-first rendering, stale-aware/background refresh, bounded concurrency, timeout handling, per-provider events, and tray updates.
- Secret redaction and explicit separation of consumer-quota data from API-platform cost/usage.

These are architecture patterns; AIBar should re-express them within its eventual stack rather than importing stack-specific Tauri/React/Rust or Swift machinery by default.

### MIT reuse requirements

Both repositories grant MIT rights to use, copy, modify, distribute, sublicense, and sell copies, provided the copyright and permission notice is included in copies or substantial portions ([Win-CodexBar `LICENSE` L1-L17](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/LICENSE#L1-L17), [CodexBar `LICENSE` L1-L17](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/LICENSE#L1-L17)). AIBar must preserve the applicable notices for copied or substantially derived code and track provenance; MIT permission does not make private service endpoints public or supported.

### Do not copy without an explicit decision

- Calls to undocumented `chatgpt.com/backend-api/wham/...` endpoints and assumptions about their schemas or longevity.
- Reading another application's plaintext token file, treating `OPENAI_API_KEY` as a ChatGPT backend bearer, or implementing token refresh with an embedded OAuth client ID.
- Browser-cookie extraction, DPAPI/ABE workarounds, manual Cookie headers, or persistent WebView session stores.
- Machine-scope DPAPI fallback for user secrets, because it changes who on the device may decrypt the data.

These choices affect terms-of-service exposure, credential ownership, account isolation, support burden, and breach impact; MIT licensing does not answer those product/security questions.

## Evidence classification

### Verified facts

- The two inspected trees were pinned to the SHAs stated above.
- The Windows Codex path, credentials, HTTP behavior, cache/event flow, generic DPAPI/browser support, secure-file helper, and redaction behavior are directly evidenced by the cited source ranges.
- Upstream's OAuth refresh, browser/WebKit route, separate OpenAI API provider, and both MIT licenses are directly evidenced by the cited source ranges.

### Inferences

- The `/wham/...` endpoints are operationally fragile for AIBar because the source calls them under `backend-api` and provides no public API contract; future stability cannot be inferred from current functionality.
- Architecture-level adaptation is lower risk than code copying because it avoids inheriting stack coupling and credential/session assumptions.
- The Windows provider relies on Codex CLI or user action to renew credentials because its parsed model omits the refresh token and no refresh call appears in the inspected provider files.

### Remaining uncertainties

- OpenAI's current policy, contractual permission, rate limits, and future compatibility for third-party use of the private ChatGPT endpoints were not established by source inspection.
- Runtime behavior against live accounts, enterprise/workspace variants, proxy/custom-base configurations, and all response-schema edge cases was not tested.
- This report does not decide AIBar's stack, MVP provider set, credential-storage policy, or whether consumer quota belongs in product scope.

## Recommended next step

Use this evidence to make two explicit planning decisions before implementation: (1) whether AIBar supports only documented OpenAI API-platform usage or also accepts the risk of private consumer-quota integration, and (2) whether AIBar will ever import credentials/sessions owned by other applications. Select the stack only after those boundaries are fixed.
