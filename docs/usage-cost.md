# Estimated cost and tokens

**Pace & details** shows two rows for each provider: today's tokens and estimated
list-price cost, and the last 30 local calendar days (today plus the preceding
29 days). These are local log estimates, not subscription charges or invoices.

```text
Today: $0.04 · 15K tokens
Last 30 days: $254.24 · 218M tokens
```

## Where the numbers come from

| Provider | Source | Day boundary |
|----------|--------|--------------|
| Codex | Daily model rows in AIBar's `analytics.db`, before all-time model totals are collapsed | Windows local calendar days assigned by the existing scanner |
| Claude | Linux helper's assistant `message.usage` aggregates from `~/.claude/projects/**/*.jsonl` | Linux local calendar days, including DST |

Codex numbers reflect the last local analytics scan. Existing all-time model
and token totals remain unchanged. Claude aggregates refresh on startup, panel
open and quota refresh, only while Claude consent is enabled. The Windows reader
does not open Claude logs or credentials. The Linux helper never sends prompts,
paths, message/request/session identifiers, or authentication tokens.

## Pricing and incomplete data

The checked-in catalog is dated **2026-10-19**, using standard-tier, short-context
USD prices per million tokens from [OpenAI](https://developers.openai.com/api/docs/pricing)
and [Anthropic](https://platform.claude.com/docs/en/about-claude/pricing).
Only catalog IDs are priced, with ordinal, case-sensitive matching. Claude IDs
may additionally end in exactly a hyphen and eight date digits (`-YYYYMMDD`).
No prefix, `latest`, OpenAI date alias, or other guessed model match is accepted.

Input, cached input, cache creation and output are separate components. Claude
cache creation uses the published five-minute write rate. Discounts, contracts,
routing, longer contexts and other billing adjustments are not inferred.

- Known models plus unpriced models: `$X.XX+`, all observed tokens, and
  **some models unpriced**. The dollar amount is the known subtotal only.
- All models unpriced: **Cost unavailable**, tokens, and **some models unpriced**;
  never a fabricated `$0.00`.
- Incomplete logs: a known subtotal ends in `+` and **incomplete data**.
- Missing, invalid, future-dated or older-than-two-hour Claude aggregates:
  **Unavailable**, not zero. Exactly two hours old is accepted.
- A complete, valid empty period can legitimately show `$0.00 · 0 tokens`.

## Separate Claude token contract

The helper publishes `%LOCALAPPDATA%\AIBar\claude-remote-tokens.json` atomically,
independently of the existing quota file. Schema 1 has exactly these root fields:

```json
{"schemaVersion":1,"generatedAt":"2026-10-19T12:00:00Z","localDay":"2026-10-19","state":"current","days":[{"day":"2026-10-19","model":"claude-opus-5-5","input":10,"cachedInput":40,"cacheWrite":30,"output":20}]}
```

`generatedAt` is UTC; `localDay` is the Linux collection day. State is `current`,
`partial`, or `unavailable`. Counts are nonnegative signed 64-bit integers;
components and period totals must not overflow. The limit is **1 MiB / 3000
unique day-model rows**. Duplicate or unexpected properties, missing fields,
invalid types, duplicate day-model pairs, invalid dates, rows outside the 30-day
range, and reparse-point files are rejected. Model labels are bounded lowercase
ASCII identifiers, never free-form log strings.

The helper deduplicates by message ID plus request ID across log files, ignores
`<synthetic>` messages, excludes future events and follows no log symlinks.
Malformed/unreadable records yield partial data rather than a complete zero.
Identifiers used for deduplication stay in Linux process memory only.

**Clear AIBar Data** cancels and drains the token reader, clears its presentation,
and deletes `claude-remote-tokens.json`. It leaves Claude source logs and the
existing remote quota file untouched. A running Linux helper can republish the
aggregate later; Claude consent is still required to read it.

See the [helper installation instructions](../tools/claude-remote-usage/README.md).
Codex export v1 and **Copy summary** are unchanged.
