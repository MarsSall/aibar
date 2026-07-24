# Design: AIBar Foundation

## Technical Approach

Build AIBar as a single-process Windows .NET 8 WPF tray application. Presentation depends on application coordinators and pure domain policies; adapters own Codex credentials/private HTTP, incremental JSONL scanning, SQLite, startup registration, clocks, and Windows integration. The proposal defines product intent and risk posture; `specs/aibar-foundation/spec.md` is authoritative for behavior; `tasks.md` and Git history trace delivery. This document records technical choices and the active Slice 8C2B1 correction without replacing those authorities.

**Size exception reason:** the full design retains exact credential/privacy boundaries, incremental persistence rules, completed-slice traceability, and literal B1 graph, legal, canonicalization, subprocess, promotion, rollback, and failure contracts. Compressing these below the ordinary tier would remove security or recovery semantics.

**Actual artifact word count:** 4,700 whitespace-delimited words, including headings, tables, and code contracts.

## Architecture Decisions

| Decision | Alternatives | Choice and rationale |
|---|---|---|
| Desktop stack | WinUI 3; Tauri/Rust/webview | WPF/.NET 8: mature Windows 10/11 tray/startup integration, one typed process, no web IPC or Windows App SDK deployment dependency. |
| Shape | UI-led services; multiple processes | Four dependency-directed layers: WPF host → application coordinators → domain → adapters. UI performs no filesystem, HTTP, or SQL work. |
| Persistence | Files; remote store | Per-user SQLite with WAL and transactions for quota cache, checkpoints, daily/model aggregates, settings, and scan provenance. No telemetry or machine-wide fallback. |
| Private quota access | Treat as supported API | Isolated `HttpClient` adapter for allowlisted `https://chatgpt.com/backend-api/wham/*`; redirects disabled, bounded timeout/retry, explicit unsupported/private disclosure and kill switch. |
| Packaging | Combined recovery/MSIX/signing/compliance | Reviewable chain `8B.1 → 8C1 → 8C2A → B1a1 → 8C1.1 → B1a2 → B1b → B2 → 8D → 8E`, each at most 400 authored changed lines. `8C1.1` is an opt-in isolation extension, not a rewrite of 8C1 defaults. |
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
| Git through `e244995` | Reviewed implementation history. `5fe8b52` is deterministic 8C1 recovery packaging; `f0d15b1` is reviewed 8C2A manifest/assets and capability planning; `e244995` is B1a1 pure graph reconciliation with fixture-only policy. |

Completed earlier slices retain the behavior specified above and in the normative spec. The retired uncommitted 8B.2 path exporter did not prove stable Windows namespace identity; its tests/review state granted no authority. 8C1 admits only a fresh nonexistent output leaf, performs no caller-path cleanup/replacement, creates complete recursive self-contained publish inventory/recovery ZIP/canonical manifest, and reports incomplete caller-owned output without deleting it. Its lexical/reparse checks are defense in depth, not hostile namespace-containment proof. 8C2A adds reviewed manifest/assets and deterministic capability-state planning only; it executes neither MakeAppx nor SignTool.

Historical failed combined-8C code/results remain non-authoritative and are traceable in prior Git/OpenSpec history. They may not seed outputs, claims, or tests. Completed implementation behavior is not changed by this compression.

## Active Slice 8C2B1: Restore-to-Publish Compliance Evidence

### Authority and boundary

B1a1 is reviewed and committed at `e244995`; its checked `PackagingGraph` fixture policy proves only the pure projection contract. Generation 18 established the missing live-policy authority; generation 20 established that an in-repository `artifacts/8c2b1/**` root conflicts with reviewed 8C1; generation 21 established that default 8C1 still writes restore/MSBuild intermediates under repository `obj`. The selected stronger resolution is prerequisite Slice 8C1.1, followed by B1a2 from its reviewed commit. All failed generations retained zero implementation/runtime output. Fixture policy, prior failed B1 candidates, and IDs/names/roots inferred from live output may not seed or authorize B1a2.

B1 generates CycloneDX 1.5, provenance, notices, licenses, and a persistent receipt from a real `Release/win-x64 --self-contained` publish. It performs no MakeAppx/SignTool execution and makes no package validity, signature, installability, lifecycle, or release claim. B2 exclusively owns those runtime gates.

### Decisions and authoritative inputs

| Rejected approach | B1 decision |
|---|---|
| Template/glob or one fake package per file | Components come only from selected restore/publish graphs; files are evidence owned by real components. |
| `project.assets.json` alone | Combine selected `net8.0-windows7.0/win-x64` restore target with emitted `.NETCoreApp,Version=v8.0/win-x64` `AIBar.Desktop.deps.json`. |
| Online legal lookup | Use restored `.nuspec`, embedded license/notice bytes, and checked-in version/hash/source-pinned evidence. |
| Temporary outputs cleaned then claimed | Promote validated canonical evidence and retain content hashes bound to the candidate. |

Inputs are explicit paths to real `publish/**`, 8C1 `recovery-inventory.json`, desktop `project.assets.json`, published `AIBar.Desktop.deps.json`, the reviewed B1a2 live policy, `license-evidence.json`, an existing external caller-owned temporary parent, and caller-supplied source revision/tool facts. The parent and every acquisition/candidate/runtime child must be absolute, outside every repository/worktree, and accepted by the unchanged reviewed 8C1 output-root guard. Generated leaves are unique and nonexistent; collision or stale content fails before process launch. Input paths are read-only and never serialized. Build facts include target/configuration/RID/self-contained/deterministic/debug settings, SDK/PowerShell/generator/runtime versions or explicit `unavailable`; MakeAppx/SignTool are `not-invoked:b2-owned`.

```text
restore RID graph + published deps + inventories + reviewed live policy
                    ↓ exact join/own/reconcile
NuGet/runtime-pack/project components + exact dependency graph + file evidence
                    ↓ legal and schema validation
fresh external transient staged output
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

The caller supplies an existing isolated external temporary parent; no B1 runtime leaf may be under the repository/worktree. B1a2 generated child leaves are nonexistent and collision-failing. Validate graph, bytes, legal obligations, schemas, canonical bytes, secret/path absence, and staged hashes before touching tracked evidence. Promotion has an exact allowlist: `packaging/{license-evidence.json,sbom.cdx.json,provenance.json,THIRD-PARTY-NOTICES.md,compliance-manifest.json,receipts/8c2b1.json,LICENSES/**}`; `LICENSES/**` destinations must come from validated registry entries. Reparse targets/ancestors, unexpected existing paths, and allowlist escape fail.

Windows has no atomic multi-file replace. The promoter therefore acquires an exclusive repository-local B1 lock; creates same-volume sibling temporary files; records/fsyncs a rollback journal containing each target’s prior absent/present state and byte hash; and keeps byte-identical backups in the ignored leaf. Existing files are replaced individually with same-volume `File.Replace`; new files use collision-failing same-volume rename. License removals are journaled. `compliance-manifest.json` is promoted last as the acceptance marker. After all moves, re-read every allowlisted byte and validate the final manifest hash before deleting journal/backups.

Any handled failure reverses the journal, restoring prior bytes atomically per file and deleting only newly created allowlisted files, then verifies every prior hash before returning failure. If rollback verification fails, return `B1_ROLLBACK_INCOMPLETE`, retain journal/backups, and block all further promotion. A killed/crashed process cannot claim success because the last manifest is absent/stale; the next invocation must detect the journal and complete rollback/verification before generation. Therefore a reported failure leaves tracked evidence unchanged; an interrupted promotion is rejected and recoverable, never accepted as mixed evidence.

### Stable failure codes

Only these safe codes are emitted, optionally with non-sensitive enum context: `B1_INPUT_INVALID`, `B1_EXTERNAL_ROOT_INVALID`, `B1_TARGET_MISSING`, `B1_TARGET_AMBIGUOUS`, `B1_RID_MISMATCH`, `B1_GRAPH_UNREACHABLE`, `B1_TEST_ASSET_INCLUDED`, `B1_COMPONENT_IDENTITY_MISSING`, `B1_OWNER_MISSING`, `B1_OWNER_AMBIGUOUS`, `B1_SOURCE_BYTES_MISMATCH`, `B1_SHIPPED_FILE_MISSING`, `B1_SHIPPED_FILE_EXTRA`, `B1_FIRST_PARTY_UNDECLARED`, `B1_POLICY_REVIEW_REQUIRED`, `B1_POLICY_DRIFT`, `B1_LICENSE_METADATA_MISSING`, `B1_LICENSE_TEXT_MISSING`, `B1_LICENSE_INCOMPATIBLE`, `B1_NOTICE_MISSING`, `B1_CYCLONEDX_INVALID`, `B1_CANONICALIZATION_FAILED`, `B1_SECRET_OR_PATH_DETECTED`, `B1_STALE_OUTPUT`, `B1_SUBPROCESS_TIMEOUT`, `B1_SUBPROCESS_FAILED`, `B1_CLEANUP_FAILED`, `B1_PROMOTION_FAILED`, `B1_ROLLBACK_INCOMPLETE`. No exception, command line, absolute path, or secret is printed.

### Testing strategy

| Layer | Required proof |
|---|---|
| Controlled RED | Missing/extra graph node/file/edge; test leakage; duplicate owner; same filename/different bytes; RID mismatch; runtime/native/resource omission; fake component; wrong hash semantics; unknown/missing/incompatible legal evidence; path/secret injection; closed-schema/additional-property/cardinality/ref/order violations. |
| Real integration | Two existing-8C1 real publishes in distinct external caller-owned Unicode/space roots with nonexistent generated leaves; actual restore/deps/inventory; byte-identical path-free canonical metadata and cleanup evidence, complete bidirectional restore↔publish ownership, real package/runtime/project identities, exact file-component byte hashes and lengths, no synthetic package components or persistent runtime leaves. |
| Promotion/recovery | Stale target/leaf, collision at every promotion step, injected replace failure, bidirectional prior-present/prior-absent restore and publish-omission tests, rollback-journal recovery after simulated termination, manifest-last acceptance, receipt/manifest self-exclusion, unchanged prior hashes after reported failure. |
| Schema/legal | CycloneDX 1.5 validator; closed JSON contracts; purl/bom-ref/edge closure; every component-to-text/source link; exact redistributed bytes. |

### B1a2 live policy authority

#### Prerequisite Slice 8C1.1 — opt-in isolated restore and MSBuild roots

8C1.1 is a small independent child after reviewed B1a1 and before B1a2. It modifies only `scripts/Publish-Deterministic.ps1` and `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`. Its default invocation remains the reviewed 8C1 behavior: when no isolation arguments are supplied, command shape, output-root guard, publish layout, recursive inventory, deterministic ZIP, artifact manifest, failure reporting, and caller-owned cleanup boundary remain unchanged.

Isolated mode is explicit and all-or-nothing. The caller supplies one marker-owned external parent plus absolute distinct children for MSBuild intermediates, build outputs, restore metadata, package cache, and the existing fresh 8C1 recovery output leaf. Supplying only a subset fails before process launch. Every path must be beneath the marked parent, outside repository/worktrees in both containment directions, NFC-valid, non-root, and free of observed reparse ancestors. Existing child file/directory, collision, marker mismatch, inaccessible classification, overlap between children, or changed observation fails closed without mutation. The script repeats canonical containment/reparse checks immediately before restore and publish.

Isolated mode performs explicit `dotnet restore`, then `dotnet publish --no-restore`, forwarding the same external properties to both: project-separated `BaseIntermediateOutputPath`, `MSBuildProjectExtensionsPath`, and `RestoreOutputPath`; external `BaseOutputPath`; and `RestorePackagesPath`. Trailing separators and per-project leaves are deterministic MSBuild storage details, never component identity. After success, the selected `project.assets.json`, emitted deps file, publish tree, inventory, ZIP, and artifact manifest all originate from the external owned tree; repository `obj/**` and `bin/**` must remain byte/state unchanged.

Proof covers default-mode compatibility against reviewed 8C1 evidence and isolated-mode runs in two external parents containing spaces and NFC Unicode. It verifies identical inventory paths/hashes, ZIP bytes, manifest bytes, and artifact identity; no root spelling enters evidence. Stale/colliding children and injected restore/publish failure preserve pre-existing bytes and report incomplete caller-owned output. 8C1.1 itself performs no recursive cleanup or replacement: the owning harness waits for restore/publish process-tree exit, disposes handles, validates marker/containment/reparse state, then removes only its external parent and records path-free cleanup proof. MakeAppx and SignTool are never invoked.

8C1.1 adds no policy generation, graph ownership, SBOM, legal/provenance promotion, B1 receipt, MSIX, signing, lifecycle, or release claim. Rollback reverts only its optional parameters/forwarding and focused tests, restoring exact reviewed 8C1 default behavior. Forecast: 180–260 authored additions/deletions, with a hard 400-line ceiling. It must receive its own review, receipt, and commit before any B1a2 policy-candidate generation or runtime acquisition resumes.

#### Source of truth, generation, and ownership

The sole live authority is the maintainer-reviewed, checked-in `packaging/policies/win-x64.publish-policy.v1.json`, validated by the checked-in closed schema `packaging/policies/live-publish-policy-1.schema.json`. Repository maintainers own both files; the B1a2 PR review is the approval boundary. A tool may regenerate a candidate, but it may never overwrite the checked-in policy or approve its own output. Schema or semantic changes require a new schema identifier and policy filename/version; v1 bytes remain historical review evidence.

Acquisition first writes external-root `policy-candidate.json` containing only observed exact restore keys, deps keys/assets, and publish/recovery paths. Every authority field is `null`; generation exits `B1_POLICY_REVIEW_REQUIRED` and cannot invoke B1a1. A maintainer may authorize a path-free canonical copy at `packaging/policies/review/win-x64.publish-policy-candidate.v1.json`, explicitly supplies and reviews all mappings, then checks in canonical v1. The repository stores only this authorized review artifact, the schema, and the approved policy—never restore intermediates, publish leaves, recovery ZIPs, or runtime inventories. The candidate is inventory assistance, not policy derivation. B1a1 `Fixtures/PackagingGraph/publish-inventory.json` remains fixture-only and is never read by live mode.

#### Closed contract and join

The policy requires exactly `schemaVersion`, `policyVersion`, `target`, `expected`, `rootBomRef`, `components`, and `files`; unknown properties fail. `target` fixes project path, `Release`, `net8.0-windows7.0`, `win-x64`, self-contained, deterministic, and no-debug settings. `expected` stores SHA-256 fingerprints of canonical, path-free restore graph, deps graph, publish inventory, and 8C1 recovery inventory.

Each component entry explicitly records `restoreKey`, `depsKey`, `bomRef`, `componentType` (`project|package|runtimepack`), `classification` (`first-party|managed|runtime`), and `scope` (`production|test`). Package/runtime entries additionally record exact NuGet `id`, normalized version, purl, assets-library key, and package-relative root. These fields must agree with the exact selected NuGet target/library records; no casing repair, prefix, basename, assembly-name, directory, or root inference is permitted. The single `rootBomRef` must resolve to an explicit production project. Any test-scoped component in the selected graph or shipped set fails `B1_TEST_ASSET_INCLUDED`.

Each file entry explicitly records normalized `publishPath`, `ownerBomRef`, `classification` (`first-party|managed|runtime|native|resource`), and one exact source locator:

- `deps-asset`: exact deps library key, `runtime|native|resources` section, asset path, assets-library key, and package-relative source path;
- `project-output`: exact project key, role (`apphost|assembly|deps|runtimeconfig|resource`), and source-relative path.

The join is bijective. Every selected restore node and deps library has exactly one policy component; every policy component exists in both required graphs; every deps asset, publish-inventory item, and recovery-inventory item has exactly one policy file; every policy file exists in all applicable inputs and resolves to one source byte sequence and one owner. Missing, extra, duplicate, case-colliding, ambiguous, hash/length-mismatched, unreachable, or unowned entries fail with the existing stable B1 codes before SBOM generation. Standard NuGet identity semantics are therefore validated, not used to invent policy.

#### Drift and deterministic semantics

Inputs must be strict UTF-8; authority strings and paths must already be Unicode NFC. Schema tokens, exact keys, refs, and sorting use `StringComparer.Ordinal`; publish-path collision checks additionally use `OrdinalIgnoreCase` for Windows. No culture-sensitive comparison or normalization-on-output is allowed. Canonical policy uses UTF-8 without BOM, LF with one terminal LF, ordinal object keys, and arrays sorted by `restoreKey` or `publishPath` then `ownerBomRef`. Hashes are lowercase.

The runner recomputes the four canonical fingerprints and the canonical policy SHA-256. Any drift blocks with `B1_POLICY_DRIFT`; it never amends policy. Two fresh roots must produce identical fingerprints, policy hash, SBOM, and compliance manifest. A candidate-local acquisition record binds those hashes for B1b; B1b later places the reviewed policy hash/version into provenance and owns legal evidence, promotion, rollback journal, manifest-last acceptance, and receipt.

#### Process, external-root ownership, cleanup, and rollback

B1a2 resolves a fixed `dotnet` executable and the reviewed script entry point, uses `ProcessStartInfo.ArgumentList` without shell composition, and invokes only reviewed 8C1.1 isolated mode. The caller creates one absolute external parent beneath a canonical system temporary root, using a cryptographically unique leaf and collision-failing creation, then writes an invocation nonce marker before B1a2 receives it. Admission canonicalizes the temp, repository, worktree, parent, and proposed child paths; rejects relative/root paths, repository/worktree equality or containment in either direction, pre-existing child entries, inaccessible/unclassifiable entries, and any observed reparse ancestor; and repeats containment/reparse observations immediately before each creation and launch. These are observed Windows defenses, not hostile namespace-identity proof.

Restore intermediates, policy candidate, 8C1 recovery output, publish output, and acquisition record occupy distinct unique children beneath that parent. Existing files/directories or a mismatched/missing marker are stale/unowned: fail `B1_STALE_OUTPUT` or `B1_EXTERNAL_ROOT_INVALID` before launch, preserve bytes, and perform no cleanup. Spaces and NFC non-ASCII characters are accepted in absolute paths and passed only as discrete arguments. Runtime paths, nonce, username, and root spelling never enter canonical evidence.

The runner drains bounded stdout/stderr asynchronously, enforces timeout/cancellation in an owned process tree, terminates and waits for all descendants, disposes streams/handles, and accepts only exit zero plus the complete policy join. The owning caller may clean only after those conditions. Cleanup revalidates the exact marker, canonical containment, and absence of reparses across the owned tree, then removes only that external parent. It records path-free counts, expected inventory hashes, child-exit confirmation, and `cleanupStatus:"removed"` in the authorized canonical review artifact. A cleanup refusal/failure yields `B1_CLEANUP_FAILED`, no success evidence, and no automatic traversal; stale unowned bytes remain caller-owned. Successful B1a2 leaves no persistent runtime output.

Two runs use distinct external Unicode/space parents and fresh children. Their canonical restore/deps/publish/recovery fingerprints, policy hash, SBOM, manifest, and path-free cleanup evidence must be byte-identical. Repetition never reuses or cleans a prior leaf before acquisition. Rollback reverts only the B1a2 runner/tests, live schema/policy/review artifact, and removes still-owned external roots only after child exit; committed B1a1, reviewed 8C1 defaults, and reviewed 8C1.1 isolated mode remain usable.

B1a2 depends on the reviewed and committed 8C1.1 child; it cannot absorb, bypass, or reimplement that isolation boundary. B1a2 adds no legal text/provenance promotion (B1b), MakeAppx/SignTool/MSIX behavior (B2), install/upgrade/startup/uninstall behavior (8D), or release/policy sign-off (8E). Reuse the existing B1a1 parser/canonicalizer and one focused runner test class; generated canonical policy data is reviewed and hash-bound but excluded from authored-line accounting. Handwritten schema, runner, tests, and minimal design/task follow-up must total at most 400 changed lines; forecast 370–395, otherwise stop and replan rather than compress checks.

### Threat matrix

Tests launch PowerShell and 8C1/8C1.1 `dotnet restore`/`publish` with `ProcessStartInfo.ArgumentList`, fixed entry points, no shell interpolation, fresh roots, sanitized capture, and explicit process ownership. Policy-candidate generation cannot execute discovered paths. Applicable cases below are B1 requirements and must be copied unchanged into reset tasks/RED tests.

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
| Live policy acquisition | Applicable | Candidate generation emits unresolved data and `B1_POLICY_REVIEW_REQUIRED`; RED missing/extra/ambiguous/NFC-invalid/drifted policy → fail before B1a1 or acceptance. |
| External temporary root | Applicable | Absolute caller-owned root must be outside repository/worktrees and pass unchanged 8C1 guard; RED overlap/relative/root/reparse/stale/marker mismatch → no launch or cleanup. |
| Cleanup ownership | Applicable | Cleanup begins only after full child-tree exit and marker/containment/reparse revalidation; RED live child or tampered tree → `B1_CLEANUP_FAILED`, no acceptance or traversal. |
| Isolated MSBuild state | Applicable | 8C1.1 requires all external roots together and forwards identical restore/publish properties; RED partial/overlapping/stale roots or repository `obj/bin` mutation → fail with no accepted inventory. |

### Rollback and forecast

Before B1a2 apply, preserve `e244995`; generations 18–21 have no source/runtime delta to recover. Apply, review, receipt, and commit 8C1.1 first. Do not alter reviewed 8C1 default behavior, 8C2A, B1a1 fixtures/outputs, or historical Git/runtime/review records. Fresh B1a2 may retain no generated probe as authority and must use new external caller-owned leaves through reviewed 8C1.1 isolated mode.

Rollback reverts only the B1a2 runner/tests and authorized live policy schema/policy/review artifacts; runtime roots are external and removable only with matching ownership evidence after child exit. Committed B1a1 and reviewed 8C1.1 remain. Forecast 370–395 authored additions/deletions. Generated canonical policy/review data is excluded from authored forecasting but included in candidate hashes and review. Four hundred changed lines cannot be exceeded.

## Compatibility, Verification, and Release

Packaged startup uses supported MSIX startup tasks; unpackaged startup uses quoted per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with `--startup`; never elevation, services, scheduled tasks, or machine-wide registration. Read back effective state. Windows 10/11 checks cover tray recreation, DPI/taskbar, sleep/resume, popover focus, and explicit unsupported states.

General tests remain boundary-focused: domain policy; auth/private HTTP fixtures and redaction; scanner golden/property cases; SQLite atomicity/migration/clear isolation; coordinator concurrency/cancellation; WPF view state and Windows VM smoke; recursive privacy inspection. Live private-endpoint tests are opt-in, disposable, redacted, and never sole acceptance proof.

Release proceeds from developer fixtures/private adapter disabled, to opt-in internal validation, to signed limited release only after policy/legal, dependency/SBOM, Windows packaging, privacy, kill-switch, and export-absence gates. B2 establishes truthful MakeAppx/SignTool states; 8D owns install/upgrade/startup/uninstall lifecycle; 8E owns final release hardening. No B1 evidence implies those approvals.

## Open Questions

None blocking this design. The tasks and progress artifacts now carry the explicit B1 authority reset required before apply.
