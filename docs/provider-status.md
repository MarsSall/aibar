# Provider status and official usage

Each provider header includes **Usage ↗**, which opens that provider's official usage page in your default browser. When an enabled provider reports an incident, its header also shows a clickable badge with the service's status description. The badge opens the official status page. This is public service health, not an assessment of your account or quota.

| Provider | Official usage | Status page | Poll endpoint |
| --- | --- | --- | --- |
| Codex / OpenAI | https://chatgpt.com/codex/settings/usage | https://status.openai.com | https://status.openai.com/api/v2/status.json |
| Claude / Anthropic | https://claude.ai/settings/usage | https://status.claude.com | https://status.claude.com/api/v2/status.json |

## Network and display boundaries

- Only enabled providers are polled, immediately on enable and every five minutes while AIBar is running. Claude's source selection does not change its public status endpoint.
- Requests are anonymous GETs. They use no credentials, cookies, proxy credentials or redirects. There are no additional status network destinations.
- Each request, including reading its body, has a ten-second deadline. Bodies larger than 65,536 bytes are rejected, including bodies without a declared length.
- The parser requires exactly one `status` object with unique string `indicator` and `description` fields. It accepts only `none`, `minor`, `major` and `critical`; blank or control-character descriptions and extra status fields are rejected. Root metadata such as `page` is ignored.
- `none` displays no badge. Unreachable endpoints, timeouts, non-success responses and invalid payloads also display nothing; a failed poll clears any previous incident. Status failure does not create a quota error or use cached incident data.
- Disabling a provider cancels its in-flight status request, clears its badge and suppresses late responses. Shutdown stops polling and drains the active request before releasing its HTTP client. No status files are persisted, so Clear AIBar data needs no additional file deletion.
- Browser links are fixed allowlisted HTTPS destinations, launched with shell execute and no arguments. Neither a status payload nor a UI binding can supply a URL. A browser-launch failure does not crash the panel.

## Verification

`ProviderStatusTests.cs` covers strict parsing, indicators, byte boundaries, fixed anonymous requests, production transport policy, HTTP failure/redirect rejection, cancellation, consent gating, five-minute boundaries and late-result suppression. `ProviderLinksTests.cs` covers exact destinations, shell execution, invalid providers and launch failures. Windows CI runs the xUnit project; this feature was authored on Linux without .NET or PowerShell. Tests and implementation are separate files so CI can first verify the tests fail without the implementation.
