# Design: AIBar Foundation

## Technical Approach

Build AIBar as a single-process Windows .NET 8 WPF tray application. Presentation depends on application coordinators and pure domain policies; adapters own Codex credentials/private HTTP, incremental JSONL scanning, SQLite, startup registration, clocks, and Windows integration. The proposal defines product intent and risk posture; `specs/aibar-foundation/spec.md` is authoritative for behavior; `tasks.md` and Git history trace delivery. This document records technical choices and the active Slice 8C2B1 correction without replacing those authorities.

**Size exception reason:** the full design retains exact credential/privacy boundaries, incremental persistence rules, completed-slice traceability, and literal B1 graph, legal, canonicalization, subprocess, promotion, rollback, and failure contracts. Compressing these below the ordinary tier would remove security or recovery semantics.

**Actual artifact word count:** 3,174 whitespace-delimited words, including headings, tables, and code contracts.

## Architecture Decisions

| Decision | Alternatives | Choice and rationale |
|---|---|---|
| Desktop stack | WinUI 3; Tauri/Rust/webview | WPF/.NET 8: mature Windows 10/11 tray/startup integration, one typed process, no web IPC or Windows App SDK deployment dependency. |
| Shape | UI-led services; multiple processes | Four dependency-directed layers: WPF host → application coordinators → domain → adapters. UI performs no filesystem, HTTP, or SQL work. |
| Persistence | Files; remote store | Per-user SQLite with WAL and transactions for quota cache, checkpoints, daily/model aggregates, settings, and scan provenance. No telemetry or machine-wide fallback. |
| Private quota access | Treat as supported API | Isolated `HttpClient` adapter for allowlisted `https://chatgpt.com/backend-api/wham/*`; redirects disabled, bounded timeout/retry, explicit unsupported/private disclosure and kill switch. |
| Packaging | Combined recovery/MSIX/signing/compliance | Reviewable chain `8B.1 → 8C1 → 8C2A → 8C2B1 → 8C2B2 → 8D → 8E`, each at most 400 authored changed lines. |
| Diagnostic export | Managed path-based exporter | Retired. Reviewed 8B.1 keeps bounded in-memory preview and explicit export-unavailable state; a future exporter requires native handle-relative design and separate security review. |

## Runtime Architecture

```text
WPF tray/popover
      ↓ commands / immutable state
QuotaRefreshCoordinator | AnalyticsScanCoordinator | Settings/ClearData
      ↓ ports
Quota/freshness/coverage/token/day/pricing domain policies
      ↑ adapters
Codex auth | private HTTP | JSONL scanner | SQLite | startup | clock
```

### Primary contracts

| Contract | Responsibility |
|---|---|
| `ICodexCredentialSource` | Resolve the one supported Codex root; return request-scoped access material without modifying or persisting it. |
| `IQuotaProvider` | Decode quota windows and classify auth, permission, network, service, and schema failures. |
| `IQuotaSnapshotStore` | Atomically retain only normalized last-success data and its original timestamp. |
| `IUsageScanner` | Discover supported session/archive layouts; stream append-only work; return deltas, checkpoints, and coverage. |
| `IAnalyticsStore` | Transactionally persist checkpoints, scan provenance, and daily/model aggregates. |
| `IPricingCatalog` | Supply immutable version/date/rates and explicit unsupported-model results. |
| `IStartupRegistration` | Read and change effective per-user startup state without elevation. |
| `IClock` / `ILocalTimePolicy` | Make freshness, countdowns, local-day/DST policy, and tests deterministic. |

### Lifecycle, credentials, and quota

A named mutex enforces one per-user process; a second launch activates the existing popover. Closing hides the popover; explicit Exit cancels work, commits only completed scan units, closes SQLite, and exits. Startup is cache-first and non-blocking. Tray states are current, stale, loading-with-prior-value, or unavailable/error; no percentage is fabricated.

Resolve non-empty `%CODEX_HOME%`, otherwise the current user’s `.codex`. Read only required auth fields, read-only, with replacement-tolerant sharing. Never deserialize or retain refresh tokens, prompt/response content, cookies, or passwords. Create authorization immediately before a request and release references afterward. Disable redirects; use bounded connection/request timeouts; never retry 401/403 or malformed data; permit at most one jittered retry for transient transport/408/429/5xx while honoring `Retry-After`. Optional quota-detail failure cannot invalidate valid primary windows. Errors expose safe codes, never bodies, headers, tokens, account IDs, or paths.

`QuotaRefreshCoordinator` coalesces poll, popover, resume, and manual triggers. Manual refresh bypasses freshness suppression, not concurrency. Commit normalized success before publication. Failure retains the old timestamp and overlays error/staleness; clearing data cancels publication and removes only AIBar-owned state.

### Incremental analytics

SQLite logical data comprises schema/parser/timezone/pricing versions, file checkpoints, daily-model usage, scan runs, one quota snapshot, and non-secret settings. Source paths are sensitive: retain only the minimum reopen locator and keyed local fingerprint; never expose project/session names.

Scanning is bounded and transactional: discover only supported `sessions`/`archived_sessions`; skip unchanged identities; stream from validated offsets; rebuild a file contribution when identity, size, parser version, or checkpoint state invalidates it; parse only timestamp, model evidence, and token counters; defer incomplete tails; warn on malformed/unreadable/changing input. Each cumulative component contributes `max(0,current-previous)`. Missing model evidence is `Unknown`. Merge deltas and advance checkpoints in one transaction; cancellation before commit changes neither.

Daily keys use the declared Windows timezone/DST policy and retain UTC-derived day, timezone ID, and observed offset. Policy changes require versioned rebuild. Most-used model ranks `input + cached input + output`, with cached input included exactly once. Pricing is calculated from immutable versioned assumptions; unknown models have no fallback rate. Trends use named complete-day windows. Linear exhaustion is unavailable for stale quota, invalid reset, non-positive rate, or insufficient observations and is always labeled an estimate.

### Failure and privacy posture

Auth/private-service failure preserves local analytics; quota failures preserve any prior snapshot with truthful freshness; optional detail degrades alone; SQLite failure may retain in-memory live quota but disables persistence/analytics without destructive repair; scanner failures produce partial coverage; unsupported pricing preserves token facts. Startup denial reverts to effective state.

Telemetry and remote crash reporting are absent. Structured diagnostics contain safe event IDs, coarse durations/counts, versions, status class, and redacted codes. A central redactor precedes every bounded local sink. Raw auth/HTTP/JSONL/database content, prompts/responses, source paths, and exception objects containing them are forbidden. Clear AIBar Data never traverses or modifies Codex-owned files. Filesystem/network diagnostic export remains unavailable and unadvertised.

## Delivery Traceability and Completed Decisions

| Authority | Role |
|---|---|
| `proposal.md` | Product goals, unsupported private-endpoint disclosure, privacy posture, release/legal gates. |
| `specs/aibar-foundation/spec.md` | Normative behavior and acceptance scenarios. |
| `tasks.md` / `apply-progress.md` | Work decomposition and historical execution records; never override reviewed Git or this corrective authority statement. |
| Git through `f0d15b1` | Reviewed implementation history. `5fe8b52` is deterministic 8C1 recovery packaging; `f0d15b1` is reviewed 8C2A manifest/assets and capability planning. |

Completed earlier slices retain the behavior specified above and in the normative spec. The retired uncommitted 8B.2 path exporter did not prove stable Windows namespace identity; its tests/review state granted no authority. 8C1 admits only a fresh nonexistent output leaf, performs no caller-path cleanup/replacement, creates complete recursive self-contained publish inventory/recovery ZIP/canonical manifest, and reports incomplete caller-owned output without deleting it. Its lexical/reparse checks are defense in depth, not hostile namespace-containment proof. 8C2A adds reviewed manifest/assets and deterministic capability-state planning only; it executes neither MakeAppx nor SignTool.

Historical failed combined-8C code/results remain non-authoritative and are traceable in prior Git/OpenSpec history. They may not seed outputs, claims, or tests. Completed implementation behavior is not changed by this compression.

## Active Slice 8C2B1: Restore-to-Publish Compliance Evidence

### Authority and boundary

B1 starts exactly from reviewed commit `f0d15b1`. The current working candidate, former checked B1 child tasks, apply-progress completion text, test totals, runtime generations 3–6, outputs, and completion claims are superseded **non-authoritative history**. No B1 review, receipt, or commit exists. Fresh apply must reconstruct B1 from `f0d15b1`; prior generated outputs and runtime evidence may not seed or authorize it.

B1 generates CycloneDX 1.5, provenance, notices, licenses, and a persistent receipt from a real `Release/win-x64 --self-contained` publish. It performs no MakeAppx/SignTool execution and makes no package validity, signature, installability, lifecycle, or release claim. B2 exclusively owns those runtime gates.

### Decisions and authoritative inputs

| Rejected approach | B1 decision |
|---|---|
| Template/glob or one fake package per file | Components come only from selected restore/publish graphs; files are evidence owned by real components. |
| `project.assets.json` alone | Combine selected `net8.0-windows7.0/win-x64` restore target with emitted `.NETCoreApp,Version=v8.0/win-x64` `AIBar.Desktop.deps.json`. |
| Online legal lookup | Use restored `.nuspec`, embedded license/notice bytes, and checked-in version/hash/source-pinned evidence. |
| Temporary outputs cleaned then claimed | Promote validated canonical evidence and retain content hashes bound to the candidate. |

Inputs are explicit paths to real `publish/**`, 8C1 `recovery-inventory.json`, desktop `project.assets.json`, published `AIBar.Desktop.deps.json`, `license-evidence.json`, an existing caller-created isolated candidate root, and caller-supplied source revision/tool facts. The generator creates only named `staging` and `publish` child leaves; each must be nonexistent and creation must fail on collision. Input paths are read-only and never serialized. Build facts include target/configuration/RID/self-contained/deterministic/debug settings, SDK/PowerShell/generator/runtime versions or explicit `unavailable`; MakeAppx/SignTool are `not-invoked:b2-owned`.

```text
restore RID graph + published deps target + real inventory/source bytes
                    ↓ resolve/own/reconcile
NuGet/runtime-pack/project components + exact dependency graph + file evidence
                    ↓ legal and schema validation
fresh ignored staged output
                    ↓ allowlisted promotion with rollback journal
tracked canonical evidence + manifest byte hash for review binding
```

### Mapping algorithm and invariants

1. Select exactly the matching restore and published RID targets. Resolve id, exact version, `package|runtimepack|project`, dependencies, package path/hash evidence, and directness. A third-party node first declared by a first-party project is direct; reachable descendants are transitive. Only the production desktop graph is admitted. Test project/target/library appearance in shipped output fails.
2. Create package/runtime/project components, never filename-derived package identities. NuGet/runtime-pack purl and bom-ref are `pkg:nuget/{id}@{version}`; first-party refs are stable `pkg:generic/aibar/{project}@{version}`. Every node and edge comes from the selected target and must be reachable from AIBar Desktop. A reachable node with no distinct shipped asset, currently framework-satisfied `System.Memory/4.5.3`, remains represented with `aibar:distribution-state=resolved-no-distinct-artifact` and no artifact hash.
3. Project each library’s `runtime`, `native`, and `resources` entries through `.deps.json` publish semantics. Resolve source bytes from `packageFolders + libraries[path] + asset path`; resolve runtime-pack bytes by exact id/version/class. Published and authoritative source bytes must match. Basename, prefix, glob, assembly name, or extension alone is never ownership evidence. Zero/multiple owners fail.
4. First-party ownership is limited to selected project runtime entries and exact root apphost, `.deps.json`, and `.runtimeconfig.json` bound to root output identity; no `AIBar.*` wildcard. Every inventory file has exactly one owner and every projected shipped asset exists. Every component with projected assets owns at least one file. Extra/missing/duplicate/case-colliding/hash-mismatched/ambiguous paths fail in both directions.
5. Dependency refs resolve once. Every restored component expected by the selected production graph is represented; no test-only component is represented as shipped. Synthetic `pkg:generic/file-*` identities are forbidden.

### CycloneDX 1.5 representation and hash semantics

Top-level/nested non-file components represent restored packages, runtime packs, and first-party projects. Each shipped artifact is a CycloneDX 1.5 **nested component with `type:"file"` under its single owning component**: `name` is normalized publish-relative path; `bom-ref` is `urn:aibar:file:sha256-path:{sha256(UTF8(path))}`; `hashes:[{"alg":"SHA-256","content":artifactByteHash}]`; property `aibar:classification` is `first-party|managed|runtime|native|resource`. File evidence has no purl, package version, or license and is not counted as a restored component. This is standard component nesting, not a synthetic package identity.

Package/runtime/project component `hashes` is omitted, including for single/multiple outputs, so mapping/global/source hashes cannot masquerade as component artifact hashes. NuGet archive/source evidence is explicitly labeled property `aibar:package-evidence-sha512`; restore/document hashes live only in provenance/receipt. `dependencies` contains only real graph component refs, sorted and closed; file ownership is expressed by nesting, not false dependency edges.

CycloneDX omits volatile `serialNumber` and `metadata.timestamp`; `version` is `1`. Tool entries contain deterministic name/version or `unavailable`, never execution time or host path.

### Legal evidence

Parse restored `.nuspec` license expression/file and repository URL/commit. Current evidence must preserve MIT for Microsoft.Data.Sqlite/Core and .NET runtime packs; Apache-2.0 for SQLitePCLRaw packages; System.Memory’s MIT text and redistributed notices; runtime-pack embedded license/notices; and SQLite public-domain dedication/source attribution for bundled `e_sqlite3`. No license default exists. Unknown SPDX, absent complete text/notice/copyright, mutable or unattributed source, component omission, hash mismatch, or incompatible metadata blocks B1.

`packaging/license-evidence.json` is canonical JSON satisfying this exact closed JSON Schema-style contract:

```json
{"type":"object","additionalProperties":false,"required":["schemaVersion","entries"],"properties":{"schemaVersion":{"const":"aibar-license-evidence-1"},"entries":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["componentPurl","version","licenseExpression","redistributionStatus","upstream","texts","assetPaths"],"properties":{"componentPurl":{"type":"string","pattern":"^pkg:(nuget|generic)/[^\\s]+@[^\\s]+$"},"version":{"type":"string","minLength":1},"licenseExpression":{"type":"string","minLength":1},"redistributionStatus":{"enum":["compatible","incompatible"]},"upstream":{"type":"object","additionalProperties":false,"required":["url","revision"],"properties":{"url":{"type":"string","format":"uri","pattern":"^https://"},"revision":{"type":"string","minLength":1}}},"texts":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["kind","source","sha256","destination"],"properties":{"kind":{"enum":["license","notice","public-domain"]},"source":{"type":"string","minLength":1,"pattern":"^(https://|(?!.*/(?:\\.|\\.\\.)(?:/|$))(?!/)[^\\\\]+)$"},"sha256":{"type":"string","pattern":"^[0-9a-f]{64}$"},"destination":{"type":"string","pattern":"^LICENSES/(?!.*(?:^|/)\\.\\.?(?:/|$))[^\\\\/][^\\\\]*$"}}}},"assetPaths":{"type":"array","uniqueItems":true,"items":{"type":"string","pattern":"^(?!/)(?!.*(?:^|/)\\.\\.?(?:/|$))[^\\\\]+$"}}}}}}}
```

Purl/version values must equal resolved graph nodes; destinations and asset paths are unique under ordinal-ignore-case comparison in addition to JSON `uniqueItems`. `compatible` is accepted only from maintainer-reviewed pinned evidence; `incompatible` fails.

### Provenance and receipt schemas

`packaging/provenance.json` exact closed contract:

```json
{"type":"object","additionalProperties":false,"required":["schemaVersion","sourceRevision","tools","runtimePacks","restore","build","boundary","outputIdentity"],"properties":{"schemaVersion":{"const":"aibar-provenance-1"},"sourceRevision":{"type":"string","pattern":"^[0-9a-f]{40,64}$"},"tools":{"type":"object","additionalProperties":false,"required":["dotnetSdk","powershell","runtime","generator","makeAppx","signTool"],"properties":{"dotnetSdk":{"type":"string","pattern":"^(unavailable|[0-9]+(?:\\.[0-9A-Za-z-]+)+)$"},"powershell":{"type":"string","pattern":"^(unavailable|[0-9]+(?:\\.[0-9A-Za-z-]+)+)$"},"runtime":{"type":"string","pattern":"^(unavailable|[0-9]+(?:\\.[0-9A-Za-z-]+)+)$"},"generator":{"type":"string","minLength":1},"makeAppx":{"const":"not-invoked:b2-owned"},"signTool":{"const":"not-invoked:b2-owned"}}},"runtimePacks":{"type":"array","uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["purl","version"],"properties":{"purl":{"type":"string","pattern":"^pkg:nuget/[^\\s]+@[^\\s]+$"},"version":{"type":"string","minLength":1}}}},"restore":{"type":"object","additionalProperties":false,"required":["canonicalSha256","rawSha256"],"properties":{"canonicalSha256":{"type":"string","pattern":"^[0-9a-f]{64}$"},"rawSha256":{"type":"string","pattern":"^[0-9a-f]{64}$"}}},"build":{"type":"object","additionalProperties":false,"required":["configuration","targetFramework","rid","selfContained","deterministic","debugType"],"properties":{"configuration":{"const":"Release"},"targetFramework":{"const":"net8.0-windows7.0"},"rid":{"const":"win-x64"},"selfContained":{"const":true},"deterministic":{"const":true},"debugType":{"const":"None"}}},"boundary":{"type":"object","additionalProperties":false,"required":["firstParty","thirdParty","testOnly"],"properties":{"firstParty":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","minLength":1}},"thirdParty":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"string","minLength":1}},"testOnly":{"const":"excluded"}}},"outputIdentity":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}
```

`packaging/compliance-manifest.json` exact closed contract:

```json
{"type":"object","additionalProperties":false,"required":["schemaVersion","inputs","artifacts","documents","outputIdentity"],"properties":{"schemaVersion":{"const":"aibar-compliance-manifest-1"},"inputs":{"type":"array","minItems":4,"maxItems":4,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["kind","sha256"],"properties":{"kind":{"enum":["restore-assets","published-deps","recovery-inventory","license-evidence"]},"sha256":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}},"artifacts":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["path","length","sha256","ownerBomRef","classification"],"properties":{"path":{"type":"string","pattern":"^(?!/)(?!.*(?:^|/)\\.\\.?(?:/|$))[^\\\\]+$"},"length":{"type":"integer","minimum":0},"sha256":{"type":"string","pattern":"^[0-9a-f]{64}$"},"ownerBomRef":{"type":"string","minLength":1},"classification":{"enum":["first-party","managed","runtime","native","resource"]}}}},"documents":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["path","sha256"],"properties":{"path":{"type":"string","pattern":"^packaging/(?!.*(?:^|/)\\.\\.?(?:/|$))[^\\\\]+$"},"sha256":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}},"outputIdentity":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}
```

Arrays are ordinal-sorted and unique; input kinds occur exactly once, artifact paths and document paths are unique ordinal-ignore-case, owner refs resolve exactly once, and each `length` is the actual file-component byte length as a non-negative integer. Define `outputIdentity` first as SHA-256 of canonical `{schemaVersion,inputs,artifacts,documents}` where `documents` contains final SBOM, notices, license texts, and `license-evidence.json`, but excludes provenance, the durable receipt, and the manifest. Insert that identity into final provenance, then hash final provenance. The final manifest’s `documents` contains those same documents plus final provenance and explicitly excludes `packaging/compliance-manifest.json` itself and `packaging/receipts/8c2b1.json`; its `outputIdentity` retains the evidence-set identity. This is the manifest self-exclusion rule.

The durable receipt is exactly `packaging/receipts/8c2b1.json` and satisfies:

```json
{"type":"object","additionalProperties":false,"required":["schemaVersion","baseRevision","outputIdentity","complianceManifestSha256","documents"],"properties":{"schemaVersion":{"const":"aibar-b1-receipt-1"},"baseRevision":{"const":"f0d15b1"},"outputIdentity":{"type":"string","pattern":"^[0-9a-f]{64}$"},"complianceManifestSha256":{"type":"string","pattern":"^[0-9a-f]{64}$"},"documents":{"type":"array","minItems":1,"uniqueItems":true,"items":{"type":"object","additionalProperties":false,"required":["path","sha256"],"properties":{"path":{"type":"string","pattern":"^packaging/(?!receipts/8c2b1\\.json$)(?!.*(?:^|/)\\.\\.?(?:/|$))[^\\\\]+$"},"sha256":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}}}}
```

Receipt documents are ordinal-sorted, unique ordinal-ignore-case, and exactly match the final manifest document set. `complianceManifestSha256` hashes exact final manifest bytes; the receipt excludes itself and is excluded from the manifest, so neither record is self-referential.

### Canonicalization

JSON is UTF-8 without BOM, LF, one terminal LF, invariant numbers, lowercase hashes, escaped JSON strings, no insignificant whitespace, and object keys in ordinal order. Arrays sort by: components/bom-ref recursively; dependencies/ref then unique dependsOn; file evidence/name; hashes/alg+content; licenses/expression; properties/name+value; schema entries/purl; texts/destination; inputs/kind; artifacts/path; documents/path. Markdown uses UTF-8/LF, fixed headings, components sorted by purl, texts by destination, and no timestamp. Relative paths use `/`, reject absolute/rooted/`.`/`..`, and compare ordinal-ignore-case for collisions. Absolute roots, usernames, environment values, timestamps, GUIDs, credentials, and secrets are forbidden. Separate-root real runs must produce byte-identical canonical outputs when authoritative inputs match.

### Staging, promotion, and Windows rollback

The caller supplies an existing isolated candidate root `artifacts/8c2b1/<candidate>/`; the generator neither creates nor cleans that root. Generated child leaves `staging/` and `publish/` must be nonexistent and collision-failing. Validate graph, bytes, legal obligations, schemas, canonical bytes, secret/path absence, and staged hashes before touching tracked evidence. Promotion has an exact allowlist: `packaging/{license-evidence.json,sbom.cdx.json,provenance.json,THIRD-PARTY-NOTICES.md,compliance-manifest.json,receipts/8c2b1.json,LICENSES/**}`; `LICENSES/**` destinations must come from validated registry entries. Reparse targets/ancestors, unexpected existing paths, and allowlist escape fail.

Windows has no atomic multi-file replace. The promoter therefore acquires an exclusive repository-local B1 lock; creates same-volume sibling temporary files; records/fsyncs a rollback journal containing each target’s prior absent/present state and byte hash; and keeps byte-identical backups in the ignored leaf. Existing files are replaced individually with same-volume `File.Replace`; new files use collision-failing same-volume rename. License removals are journaled. `compliance-manifest.json` is promoted last as the acceptance marker. After all moves, re-read every allowlisted byte and validate the final manifest hash before deleting journal/backups.

Any handled failure reverses the journal, restoring prior bytes atomically per file and deleting only newly created allowlisted files, then verifies every prior hash before returning failure. If rollback verification fails, return `B1_ROLLBACK_INCOMPLETE`, retain journal/backups, and block all further promotion. A killed/crashed process cannot claim success because the last manifest is absent/stale; the next invocation must detect the journal and complete rollback/verification before generation. Therefore a reported failure leaves tracked evidence unchanged; an interrupted promotion is rejected and recoverable, never accepted as mixed evidence.

### Stable failure codes

Only these safe codes are emitted, optionally with non-sensitive enum context: `B1_INPUT_INVALID`, `B1_TARGET_MISSING`, `B1_TARGET_AMBIGUOUS`, `B1_RID_MISMATCH`, `B1_GRAPH_UNREACHABLE`, `B1_TEST_ASSET_INCLUDED`, `B1_COMPONENT_IDENTITY_MISSING`, `B1_OWNER_MISSING`, `B1_OWNER_AMBIGUOUS`, `B1_SOURCE_BYTES_MISMATCH`, `B1_SHIPPED_FILE_MISSING`, `B1_SHIPPED_FILE_EXTRA`, `B1_FIRST_PARTY_UNDECLARED`, `B1_LICENSE_METADATA_MISSING`, `B1_LICENSE_TEXT_MISSING`, `B1_LICENSE_INCOMPATIBLE`, `B1_NOTICE_MISSING`, `B1_CYCLONEDX_INVALID`, `B1_CANONICALIZATION_FAILED`, `B1_SECRET_OR_PATH_DETECTED`, `B1_STALE_OUTPUT`, `B1_SUBPROCESS_TIMEOUT`, `B1_SUBPROCESS_FAILED`, `B1_PROMOTION_FAILED`, `B1_ROLLBACK_INCOMPLETE`. No exception, command line, absolute path, or secret is printed.

### Testing strategy

| Layer | Required proof |
|---|---|
| Controlled RED | Missing/extra graph node/file/edge; test leakage; duplicate owner; same filename/different bytes; RID mismatch; runtime/native/resource omission; fake component; wrong hash semantics; unknown/missing/incompatible legal evidence; path/secret injection; closed-schema/additional-property/cardinality/ref/order violations. |
| Real integration | Two existing-8C1 real publishes in distinct caller-created candidate roots with nonexistent generated leaves; actual restore/deps/inventory; byte-identical canonical metadata, complete bidirectional restore↔publish ownership, real package/runtime/project identities, exact file-component byte hashes and lengths, no synthetic package components. |
| Promotion/recovery | Stale target/leaf, collision at every promotion step, injected replace failure, bidirectional prior-present/prior-absent restore and publish-omission tests, rollback-journal recovery after simulated termination, manifest-last acceptance, receipt/manifest self-exclusion, unchanged prior hashes after reported failure. |
| Schema/legal | CycloneDX 1.5 validator; closed JSON contracts; purl/bom-ref/edge closure; every component-to-text/source link; exact redistributed bytes. |

### Threat matrix

Tests launch PowerShell and existing 8C1 `dotnet publish` with `ProcessStartInfo.ArgumentList`, fixed entry points, no shell interpolation, fresh roots, sanitized capture, and explicit process ownership. Applicable cases below are B1 requirements and must be copied unchanged into reset tasks/RED tests.

| Boundary | Applicability | Safe/failure behavior and planned RED test |
|---|---|---|
| Documentation-like executable paths | N/A — JSON/package/artifact inputs are parsed as data and cannot select executables. | No task. |
| Git repository selection | N/A — source revision is input data and B1 issues no `git -C` or repository selector. | No task. |
| Commit state | N/A — B1 does not stage or commit. | No task. |
| Push state | N/A — B1 does not push or resolve remotes/refspecs. | No task. |
| PR commands | N/A — B1 issues no PR command. | No task. |
| Process timeout | Applicable | Fixed timeout cancels acceptance; RED child exceeds timeout → `B1_SUBPROCESS_TIMEOUT`, no promotion. |
| Child-tree termination | Applicable | Launch in owned job/process tree; timeout/cancel terminates descendants and waits; RED child spawns grandchild holding output, then verify both exit and no acceptance. |
| Stdout/stderr drain | Applicable | Drain both asynchronously before/while waiting to avoid deadlock; RED fills both streams and must complete or timeout deterministically with sanitized output. |
| Exit/failure propagation | Applicable | Nonzero/start failure maps to `B1_SUBPROCESS_FAILED`; RED nonzero child cannot generate success receipt. |
| Partial output | Applicable | Publish success requires exit zero plus complete inventory/deps reconciliation; RED child writes partial files then fails; staging remains unaccepted. |
| Stale output | Applicable | Publish/staging leaves must be nonexistent and collision-failing; RED pre-existing file/directory/journal → `B1_STALE_OUTPUT`, bytes unchanged. |

### Rollback and forecast

Before apply, preserve `f0d15b1`; reset task/progress authority as stated; then discard only unreviewed B1 deltas and caller-owned ignored B1 outputs. Do not alter reviewed manifest/assets/capability planning, historical Git/runtime/review records, or completed behavior. Fresh B1 may retain no synthetic template, prior completion claim, or candidate output.

Rollback reverts only B1 helper/mode, tests, legal registry/texts, canonical metadata, and receipt; reviewed 8C2A remains. Forecast 320–380 authored additions/deletions. Generated legal/canonical outputs are excluded from authored forecasting but included in candidate hashes/review. Exceeding 380 requires simplification or a newly authorized split; 400 cannot be exceeded.

## Compatibility, Verification, and Release

Packaged startup uses supported MSIX startup tasks; unpackaged startup uses quoted per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with `--startup`; never elevation, services, scheduled tasks, or machine-wide registration. Read back effective state. Windows 10/11 checks cover tray recreation, DPI/taskbar, sleep/resume, popover focus, and explicit unsupported states.

General tests remain boundary-focused: domain policy; auth/private HTTP fixtures and redaction; scanner golden/property cases; SQLite atomicity/migration/clear isolation; coordinator concurrency/cancellation; WPF view state and Windows VM smoke; recursive privacy inspection. Live private-endpoint tests are opt-in, disposable, redacted, and never sole acceptance proof.

Release proceeds from developer fixtures/private adapter disabled, to opt-in internal validation, to signed limited release only after policy/legal, dependency/SBOM, Windows packaging, privacy, kill-switch, and export-absence gates. B2 establishes truthful MakeAppx/SignTool states; 8D owns install/upgrade/startup/uninstall lifecycle; 8E owns final release hardening. No B1 evidence implies those approvals.

## Open Questions

None blocking this design. The tasks and progress artifacts now carry the explicit B1 authority reset required before apply.
