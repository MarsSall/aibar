# Codex analytics investigation

## Bottom line

Codex local JSONL logs can support an incremental, privacy-conscious analytics MVP: daily input/cached/output token totals, a token-defined most-used model, explicitly estimated cost, and simple quota pace/burn/exhaustion indicators. The private quota response should remain the authority for utilization and reset data. The logs do **not** provide authoritative billed cost, a separate reasoning-token total, or reconstructable hourly history after normalization to day-only rows.

This investigation is pinned to CodexBar commit [`9a6c74cfed418ddcd01f9559bb7a80368e5245dd`](https://github.com/steipete/CodexBar/tree/9a6c74cfed418ddcd01f9559bb7a80368e5245dd) and Win-CodexBar commit [`4e9ab06460a266ee5eea500e7ec5e212b42472a1`](https://github.com/Finesssee/Win-CodexBar/tree/4e9ab06460a266ee5eea500e7ec5e212b42472a1).

## Raw data sources and fields

### Local session JSONL

- CodexBar resolves the primary root as `$CODEX_HOME/sessions`, otherwise `~/.codex/sessions`, and also scans the sibling `archived_sessions` directory ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Vendored/CostUsage/CostUsageScanner.swift#L599-L624)).
- It recognizes date partitions (`YYYY/MM/DD/*.jsonl`), flat root-level JSONL files, and a legacy recursive layout ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Vendored/CostUsage/CostUsageScanner.swift#L916-L989)). Win-CodexBar independently resolves `$CODEX_HOME` or `~/.codex/sessions`, plus configured and WSL roots ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/codex_sessions.rs#L7-L35)); its current cost walk is date-partition-oriented ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/cost_scanner.rs#L303-L337)).
- The inspected CodexBar parser models session ID, fork parent/timestamp, project path, token-event timestamp, model, and turn ID ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Vendored/CostUsage/CostUsageScanner.swift#L1012-L1055)). Its session parser also maps `session_meta` ID, working directory, originator/source, and derives a project name from the directory ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/AgentSession.swift#L389-L443)). These fields are optional because historical schemas do not always emit them.
- Token events expose cumulative or last-event snapshots for input, cached input (including `cache_read_input_tokens`), and output. Win-CodexBar resolves model context and computes component-wise non-negative deltas from cumulative totals ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/jsonl_scanner.rs#L255-L364), [fields](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/jsonl_scanner.rs#L442-L479)). Timestamps are normalized to local calendar day keys ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/jsonl_scanner.rs#L422-L440)).
- No inspected Codex parser record or token-total structure contains a separate reasoning-token field; the enumerated fields are input, cached input, and output ([CodexBar fields](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Vendored/CostUsage/CostUsageScanner.swift#L1019-L1055), [Win fields](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/jsonl_scanner.rs#L442-L479)).

### Private quota response

The decoded response directly carries plan type, primary/secondary rate-limit windows, credits, and optional individual spend control ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthUsageFetcher.swift#L6-L36)). Each window supplies used percentage, reset epoch, and duration in seconds ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthUsageFetcher.swift#L162-L178)); credits expose availability/unlimited/balance, while spend control can expose limit, used amount, remaining percentage, and reset ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBarCore/Providers/Codex/CodexOAuth/CodexOAuthUsageFetcher.swift#L239-L317)).

## Direct, derived, and unsupported metrics

| Metric | Classification | Basis / caveat |
|---|---|---|
| Quota utilization, window duration/reset, plan, credits, optional spend limit | Direct | Returned by the private quota payload; treat it as authoritative for those fields. |
| Daily input/cached/output tokens by model | Derived from direct events | Non-negative snapshot deltas, then `day -> model -> token components`. Win-CodexBar accumulates totals and per-model token buckets ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/codex_costs.rs#L30-L45), [source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/codex_costs.rs#L88-L136)). |
| Most-used model | Derived | Rank models by total tokens under one documented definition, preferably `input + output` (state whether cached input is already included in input). Never rank by estimated dollars unless labeled differently. |
| Daily/period cost | Derived estimate | Win-CodexBar prices local token counts with model tables and fallback rates ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/codex_costs.rs#L158-L192)); it is not a billing receipt. |
| Trends, pace, burn rate, exhaustion ETA | Derived | Compute from daily series and/or quota snapshots; label the window, timezone, and estimator. |
| Authoritative billed cost | Unsupported by inspected JSONL | Local price-table multiplication cannot prove discounts, routing, contract prices, or later billing adjustments. |
| Separate reasoning-token total | Unsupported | No separate field in the inspected parser models. It may be folded into output by the producer, but that is not established here. |
| True hourly history from normalized rows | Unsupported | Once events are reduced to day keys, intra-day timestamps are lost; hourly values cannot be recovered without retaining/reprocessing timestamped events. |

## Algorithms

### Parsing and aggregation

1. Discover supported roots and layouts; fingerprint roots and file metadata for incremental scans.
2. Stream JSONL, tolerate malformed/truncated lines, and capture optional session/fork/turn/project metadata.
3. For cumulative token snapshots, compute `delta = max(0, current - previous)` independently for input, cached, and output. For last-event snapshots, clamp each value at zero.
4. Assign an explicit `Unknown` model when no model evidence exists. Do **not** copy Win-CodexBar's current fallback to `gpt-5` ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/jsonl_scanner.rs#L281-L314)), because that silently biases rankings and cost.
5. Aggregate `local day -> model -> input/cached/output`, preserving the pricing version and scan provenance. Compute most-used model from the declared token definition and cost from versioned price tables.

### Pace and prediction

- **Simple predictor (MVP):** Win-CodexBar computes expected utilization as elapsed/window duration, compares actual minus expected, derives current consumption rate, and estimates exhaustion as `remaining_percent / percent_per_second`; if that ETA is beyond reset, usage is marked as lasting to reset ([source](https://github.com/Finesssee/Win-CodexBar/blob/4e9ab06460a266ee5eea500e7ec5e212b42472a1/rust/src/core/usage_pace.rs#L89-L158)). Expose this as a simple linear estimate, not a forecast guarantee.
- **Historical predictor (later):** CodexBar requires at least three complete prior weeks, uses recency-weighted weekly curves blended with a sustainable linear baseline, and only emits run-out probability with at least five weeks ([source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBar/HistoricalUsagePace.swift#L761-L818), [source](https://github.com/steipete/CodexBar/blob/9a6c74cfed418ddcd01f9559bb7a80368e5245dd/Sources/CodexBar/HistoricalUsagePace.swift#L845-L896)).
- **Trend/burn:** Use clearly named windows (for example, 7-day average tokens/day and day-over-day change). Do not interpolate hourly curves from daily totals.

## MVP versus later

### MVP candidate

- Incremental daily input/cached/output token totals.
- Explicit `Unknown` model bucket.
- Most-used model defined by tokens, with the exact token formula visible.
- Estimated cost with “estimate” labeling, price-table version/date, unknown-model warning, and repricing warning.
- Quota utilization, duration/reset, plan, credits, and spend limit when supplied by the private response.
- Simple, labeled pace, burn rate, and linear exhaustion ETA.

### Later

- Hourly/event-level retention and charts.
- Per-project analytics after privacy review.
- Probabilistic historical prediction after sufficient complete weeks.
- Multiple roots/accounts, WSL discovery, and explicit account attribution.
- Immutable pricing snapshots and controlled repricing.
- Advanced fork/replay/cross-file deduplication.

## Privacy, performance, and data-quality risks

| Risk | Consequence | Mitigation |
|---|---|---|
| Missing, unreadable, or truncated logs | Under-counts and discontinuities | Surface coverage/last-scan status; parse line-by-line; never imply completeness. |
| Schema drift | Silent field loss or wrong defaults | Version tolerant decoders, fixture coverage, telemetry-free diagnostics, `Unknown` buckets. |
| Cache invalidation errors | Stale or double-counted totals | Key by canonical path plus size/mtime/content checkpoint; invalidate on parser/pricing version changes. |
| Forks, replay, cumulative resets | Double counts or spikes | Preserve session/fork/turn IDs; non-negative deltas are necessary but not sufficient; add lineage-aware dedupe later. |
| DST/timezone changes | Days shift or have 23/25 hours | Store event UTC plus the normalization timezone; recompute day views deliberately. |
| Repricing | Historical cost changes | Persist price snapshot/version and distinguish “cost at scan” from “repriced estimate.” |
| Account ambiguity | Usage assigned to the wrong account | Keep unattributed data explicit; do not infer account solely from a shared root. |
| Project/session metadata privacy | Repository names and paths may be sensitive | Default to aggregate metrics; hash/redact paths; require opt-in for project views; never export raw prompts. |
| Large histories and recursive layouts | Slow startup and high memory/I/O | Incremental checkpoints, bounded windows, streaming parse, cancellation, and background compaction. |

## Verified facts, inferences, and unknowns

### Verified facts

- The roots, archive sibling, and three layout forms above are implemented in the pinned CodexBar scanner.
- The inspected parsers extract timestamps/day, model and available session/fork/turn/project metadata, and input/cached/output usage; cumulative snapshots become non-negative deltas.
- Win-CodexBar aggregates token totals and locally estimates cost, and its missing-model fallback can label usage as `gpt-5`.
- The private response directly contains quota-window, plan, credit, and optional spend-control fields.
- The simple and historical predictors, including their three-/five-week thresholds, exist at the cited pinned lines.

### Inferences for Aibar

- Daily model rankings, cost estimates, trends, burn, and ETA are feasible if definitions and provenance are explicit.
- `Unknown` is safer than a guessed model because attribution errors propagate into rankings and pricing.
- Authoritative cost and true hourly history require sources or retained granularity not present in day-only normalized rows.

### Unknowns requiring product or runtime validation

- Whether all Codex versions/accounts emit equivalent token semantics, especially whether reasoning is included in output.
- Whether the private endpoint and fields are stable or permitted for Aibar's intended distribution model.
- Exact account ownership of local roots and archived/forked sessions.
- Real-world completeness, mutation patterns, and performance across very large histories, WSL, DST boundaries, and schema versions.
