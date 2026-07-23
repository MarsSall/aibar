# AIBar foundation design

## Decision

Build AIBar as a **single-process, Windows-native .NET 8 desktop application using WPF**, with a thin tray/popover shell and framework-independent application/domain services. Use `HttpClient` for the private quota adapter, `System.Text.Json` streaming for Codex JSONL, and SQLite for AIBar-owned cache, checkpoints, daily aggregates, and settings.

### Corrective decision: defer diagnostic filesystem export

**Slice 8B.2 is retired from the current delivery chain. Diagnostic filesystem export remains explicitly unavailable and disabled.** The reviewed Slice 8B.1 behavior—gesture-bound category preview, bounded structured in-memory sinks, and an unavailable export result—is the approved diagnostics boundary for packaging.

Repeated review of the uncommitted Slice 8B.2 candidate identified an unresolved Windows filesystem security boundary. The candidate validates root and target path strings before calling path-based staging and replacement APIs. An attacker or concurrent actor can substitute a root or ancestor junction after validation and before a later path resolution. Rechecking attributes narrows but does not close this TOCTOU window because each check and mutation resolves the namespace again. The in-process semaphore is also keyed by normalized path string rather than stable filesystem identity, so aliases or a substituted directory identity are not the same serialization boundary.

Passing 208/208 tests demonstrates the candidate's tested serialization, cancellation, sharing, and replacement behavior; it does **not** prove namespace identity across the check/write/publish sequence. The review therefore blocks acceptance of 8B.2 without weakening privacy and without treating the tests as requirement approval. The native review record is additionally non-terminal and defective for this candidate: it remains `reviewing`, contains no lens result or finding, and classified a filesystem-security change as medium reliability work. It is evidence of no approval, not a receipt.

The foundation specification does not require diagnostic export. It requires local-first privacy, safe diagnostics, and explicit unsupported states. Packaging can therefore proceed from the last approved Slice 8B.1 while export stays unavailable. Slice 8C is rechained from the reviewed `feature/aibar-foundation-slice-8b1-diagnostic-command` base; it must not include, compile, advertise, enable, or test the retired exporter candidate.

Native Windows lifecycle and integration take precedence over pixel-perfect macOS imitation. The visual target is nevertheless a polished, modern, CodexBar-inspired experience: preserve the reference application's compact information hierarchy, clarity, spacing, status visualization, and perceived quality while expressing them through Windows typography, interaction, accessibility, and window behavior.

This is a Windows-only utility whose highest-risk work is credential access, private HTTP integration, incremental file processing, and lifecycle behavior—not cross-platform UI. WPF has the mature tray/window ecosystem and direct Windows integration identified by the feasibility investigation, supports Windows 10 and 11 without a Windows App SDK deployment dependency, and keeps the trusted implementation in one managed runtime.

### Stack comparison

| Criterion | Windows-native WPF/.NET 8 | Tauri 2 + Rust + web UI | Decision impact |
|---|---|---|---|
| Windows 10/11 tray and startup | Mature `NotifyIcon`/Win32 interoperability and direct registry/startup integration | Native tray exists, but popover focus, WebView2 lifecycle, IPC, and plugin behavior add layers | WPF is simpler for the MVP's primary surface |
| Security boundary | Credentials, HTTP, persistence, and UI remain in one typed process; no web IPC contract | Rust can strongly isolate secrets, but commands/events must cross a webview IPC boundary and frontend logging needs separate controls | WPF reduces credential and diagnostic exposure paths |
| Local analytics | .NET provides streaming JSON, async file I/O, cancellation, SQLite libraries, and testable pure services | Rust is excellent for streaming and correctness, but adds a second UI language/toolchain | No demonstrated workload requires Rust performance |
| Packaging footprint | Self-contained or framework-dependent Windows package; no WebView UI dependency | Requires WebView2 availability/bootstrap and Rust/web build toolchains | WPF has fewer runtime variables |
| UX | Older UI model, but sufficient for a compact tray popover; polish must be deliberate | Web UI enables rapid styling and reference architecture reuse | Styling advantage does not offset lifecycle/IPC complexity |
| Evidence from investigations | Exploration calls WPF mature and fast for Windows integration | Win-CodexBar proves Tauri/Rust feasibility and good provider/cache separation, but explicitly does not establish it as AIBar's default stack | Reuse the boundaries, not the stack coupling |

WinUI 3 was also considered as the modern Windows-native option. It is not selected because the MVP does not need Windows App SDK-only features, while its deployment and tray interop complexity provide no compensating product value. A later migration remains possible because presentation depends on application contracts rather than persistence or provider implementations.

## Architectural shape

Use one process and four dependency-directed layers:

```text
WPF presentation + tray host
        ↓ commands / immutable view state
Application coordinators
  QuotaRefreshCoordinator | AnalyticsScanCoordinator | Settings/ClearData
        ↓ ports
Domain models and policies
  QuotaSnapshot | Freshness | ScanCoverage | TokenDelta | DailyUsage
        ↑ adapters
Codex auth reader | private quota HTTP | JSONL scanner | SQLite | startup | clock
```

The UI never reads Codex files, sends HTTP requests, or executes SQL. Coordinators serialize work and publish immutable state snapshots through in-process events on the WPF dispatcher. Domain code has no WPF, filesystem, HTTP, or SQLite dependency.

### Visual direction

Use a custom WPF design system rather than default control styling. Define semantic design tokens for color, typography, spacing, radii, elevation, motion, and state before composing screens. Prefer Windows-native system fonts and accessibility behavior, with light/dark themes derived from semantic roles rather than hard-coded colors.

The popover should approach CodexBar's visual polish through compact quota cards, clear hierarchy, restrained gradients, progress visualization, aligned numeric data, subtle elevation, and short state transitions. It must not reproduce macOS traffic lights, menu-bar chrome, typography, blur behavior, or interactions that conflict with Windows conventions. Acrylic/Mica-like effects are progressive enhancement only: readability, contrast, performance, and Windows 10 fallback remain mandatory.

Keep presentation replaceable and testable through view models and immutable UI state. Custom visuals must preserve keyboard navigation, visible focus, screen-reader names, reduced-motion behavior, high-contrast compatibility, and DPI scaling. Visual acceptance should include reference screenshots at representative Windows 10/11 scale factors plus interaction checks; screenshot similarity alone cannot replace accessibility and lifecycle tests.

### Primary contracts

| Contract | Responsibility |
|---|---|
| `ICodexCredentialSource` | Resolve the one supported Codex root and return a short-lived access credential plus optional account identifier without modifying source files |
| `IQuotaProvider` | Fetch and decode required quota windows; classify auth, permission, network, service, and malformed-schema failures; isolate `/backend-api/wham/*` details |
| `IQuotaSnapshotStore` | Atomically load/save the last successful normalized snapshot and timestamp; never store credentials or raw response bodies |
| `IUsageScanner` | Discover supported session/archive layouts, stream appended JSONL, return deltas, checkpoint updates, and coverage warnings |
| `IAnalyticsStore` | Transactionally persist checkpoints, scan provenance, and daily/model aggregates |
| `IPricingCatalog` | Provide immutable version/date, model rates, and unsupported-model results |
| `IStartupRegistration` | Read and set effective per-user Windows startup registration |
| `IClock` / `ILocalTimePolicy` | Make freshness, countdowns, local-day normalization, DST behavior, and tests deterministic |

## Runtime and data flow

### Process, tray, and popover

AIBar is a single-instance, per-user process. A named mutex prevents duplicates; a second launch signals the existing instance to show the popover and exits. Closing the popover hides it rather than terminating AIBar. An explicit **Exit AIBar** tray command cancels background work, checkpoints completed scan work, closes SQLite, and exits.

The tray icon is always meaningful:

- current: primary 5-hour percentage is rendered;
- stale: the retained percentage is visually marked stale and its tooltip states the last success time;
- loading: an activity state does not erase the prior snapshot;
- unavailable/error: no percentage is fabricated when no valid snapshot exists.

Left-click toggles a borderless WPF popover positioned against the taskbar work area; it closes on deactivation unless an owned dialog is active. Right-click opens native commands: Open, Refresh, Start with Windows, Clear AIBar Data, and Exit. Explorer/taskbar recreation is handled by recreating the tray icon. Display/DPI/taskbar changes trigger repositioning, not process restart.

Startup sequence is cache-first and non-blocking: open/migrate the database, render cached state, create the tray, then schedule quota refresh and bounded analytics scanning. Network and scanning never run on the UI thread.

### Credential handling and private endpoint access

1. Resolve `%CODEX_HOME%` when non-empty; otherwise the current user's `.codex` root. Only the single supported root is used.
2. Open the supported auth file read-only with sharing that tolerates Codex replacement. Parse only the access credential and optional account identifier. Do not deserialize refresh tokens, prompt data, or unrelated fields into long-lived models.
3. Hold the credential in a request-scoped disposable object, create the authorization header immediately before sending, and release references after completion. AIBar does not cache, refresh, copy, or persist the credential.
4. Send HTTPS only to a compile-time allowlisted `https://chatgpt.com/backend-api` origin. Redirects are disabled so bearer credentials cannot cross origins. Apply bounded connect/request timeouts, cancellation, and conservative retry: no automatic retry for 401/403 or malformed payload; at most one jittered retry for transient connection/408/429/5xx failures while honoring `Retry-After`.
5. Fetch `/wham/usage` as the primary operation. Decode into a version-neutral DTO and validate percentages/reset timestamps before mapping to domain data. Fetch optional `/wham/rate-limit-reset-credits` separately; its failure cannot invalidate primary windows.
6. Map 401 to authentication restoration guidance, 403 to permission, 429/5xx to service, transport/timeouts to network, and decoding/required-field failures to malformed response. UI/log errors carry safe codes and summaries, never raw bodies, headers, tokens, or sensitive account IDs.

The private adapter is feature-gated by one application setting/build policy so distribution can disable it without affecting local analytics. Release remains blocked on current policy/legal review; the design does not interpret repository evidence as permission.

### Quota refresh, cache, and freshness

`QuotaRefreshCoordinator` owns a single asynchronous refresh task. Poll, popover-open, resume, and manual triggers join the in-flight task rather than overlap. Manual refresh bypasses freshness suppression but not the concurrency gate.

A successful primary response is normalized and atomically committed before state publication. The cache stores only percentages, reset instants/durations, optional non-secret details, source classification, schema adapter version, and retrieval time. It never stores raw payloads or credentials.

Freshness is a configurable product policy with a conservative initial value documented alongside implementation; the coordinator also schedules no earlier than a minimum poll interval and may schedule near the next reset. Sleep/resume and clock changes force freshness re-evaluation. Failures retain the previous snapshot with its original timestamp and an error overlay; once beyond threshold it is stale, never current. Clearing data removes the snapshot immediately and cancels active refresh publication.

### Incremental analytics and persistence

Use one SQLite database under the per-user local application data directory, opened with foreign keys and WAL mode. Logical tables are:

- `schema_meta`: schema, parser, timezone-policy, and pricing versions;
- `file_checkpoint`: canonical-path keyed identity, size, last-write time, byte offset, parser state needed for cumulative deltas, and last successful scan;
- `daily_model_usage`: local day, timezone ID/offset provenance, model or `Unknown`, and non-negative input/cached-input/output totals;
- `scan_run`: start/end, files discovered/read/skipped/deferred, warnings, cancellation, and coverage status;
- `quota_snapshot`: the single normalized last-success cache;
- `settings`: non-secret settings only.

Paths are needed transiently to reopen source files but are sensitive. Persist a keyed, application-local path fingerprint plus the minimum reopen/checkpoint locator required by the scanner; never expose paths in UI or normal logs. Do not persist project/session names. Database files inherit current-user ACLs; no machine-wide storage or fallback is used.

Scanning proceeds in bounded batches:

1. Discover only the configured root's supported `sessions` and sibling `archived_sessions` layouts.
2. Compare file identity, length, and modification metadata with checkpoints. Unchanged files contribute nothing.
3. Stream from the validated byte offset. If identity changed, length shrank, parser semantics changed, or checkpoint state is inconsistent, invalidate and rebuild that file's contribution using a transactional replacement strategy rather than adding it twice.
4. Parse only timestamps, trustworthy model evidence, and token counters. Never bind or store prompt/response fields. Malformed complete lines are skipped with coverage warnings; an incomplete final line is deferred until a later append.
5. For cumulative records calculate each component as `max(0, current - previous)`; clamp last-event values independently to zero. Missing/untrusted model evidence maps to `Unknown`.
6. In one transaction, merge deltas and advance the checkpoint. Cancellation before commit leaves both unchanged.

Daily keys use the Windows timezone active for the event conversion. Persist event UTC-derived day provenance, Windows timezone ID, and observed offset. A later timezone-policy change increments the policy version and requires explicit rebuild rather than silently moving historical totals.

`total tokens = input + cached input + output`; this exact formula determines most-used model. Cached input is a separate component and is included once in the ranking formula. Pricing applies component-specific rates from a checked-in, versioned catalog. Unknown models have no fallback rate and produce an incomplete-estimate warning. Stored aggregates remain token facts; estimates are calculated with and display the selected catalog version/date so repricing cannot masquerade as billed history.

MVP trends use named daily windows only (initially a 7-complete-local-day average and day-over-day comparison). Linear quota exhaustion uses a documented observed quota-utilization rate within the current service window and the latest service-reported remaining percentage/reset; zero/negative rate, stale quota, invalid reset, or insufficient observations yields unavailable. It is labeled a simple linear estimate, never a prediction.

## Failure and degraded modes

| Failure | Retained capability | User-visible behavior |
|---|---|---|
| Auth missing/malformed/expired | Local analytics | Auth/unavailable state; direct user to restore Codex CLI session |
| 401 / 403 | Cached quota if present, local analytics | Authentication or permission error; cached value marked stale as required |
| Network, timeout, 429, or 5xx | Cached quota and all local features | Classified error, retry timing, original snapshot timestamp retained |
| Primary schema drift | Local analytics and prior snapshot | Malformed/unsupported integration state; never map guessed fields |
| Optional detail failure | Primary quota | Optional section unavailable only |
| SQLite open/migration failure | Tray and live quota request may operate in-memory for the session | Persistence/analytics unavailable; no destructive auto-recovery; clear/rebuild offered only with confirmation |
| Unreadable/changing/truncated JSONL | Other files and prior aggregates | Partial coverage with counts; incomplete tail deferred |
| Scan cancellation or shutdown | Last committed aggregates/checkpoints | Scan marked cancelled; resume later without recounting |
| Unsupported model/pricing | Token totals | `Unknown`/unsupported warning; no invented price |
| Windows startup registration denied | Core application | Toggle reverts to effective state with permission/unsupported message |

## Packaging, startup, and compatibility

Target x64 Windows 10 and 11 initially; add arm64 only as a separately verified package. Distribution packaging is split into two reviewable units: Slice 8C1 produces the deterministic unpackaged recovery artifact, and Slice 8C2 adds valid MSIX inputs, a recognized SBOM, provenance/notices, and signing behavior. A signed, installable MSIX remains unavailable until the required Windows SDK tools and credentials are present and the explicit runtime gate below passes.

Packaging is independent of diagnostic filesystem export. Both units start from the approved chain and must preserve the explicit export-unavailable state in packaged and unpackaged artifacts. Manifests, release notes, UI copy, support documentation, and smoke tests must not claim or imply export support. Deferring export does not relax redaction, bounded retention, clear-data isolation, telemetry-off defaults, or the prohibition on remote diagnostic sinks.

### Corrective Slice 8C split

The failed Slice 8C candidate combined recovery publishing, MSIX creation, signing, SBOM, provenance, notices, and evidence in one superficially small diff. Its 196 authored lines are not an acceptance argument: the archive omits nested publish files, artifact identity depends on output paths and mutable container metadata, the manifest/assets are not valid package evidence, `dotnet list package` JSON is not a recognized SBOM, notices are incomplete, and source-reading assertions do not execute the behavior they claim. The candidate and its recorded green results remain unapproved evidence.

The immutable parent chain is:

```text
8B.1 → 8C1 → 8C2 → 8D → 8E
```

`8C1` targets `feature/aibar-foundation-slice-8b1-diagnostic-command`. `8C2` targets the immutable reviewed 8C1 head; 8D targets the immutable reviewed 8C2 head; 8E targets the immutable reviewed 8D head. No unit may be rebased onto, merge from, or inherit completion claims from the failed combined 8C candidate. Export remains unavailable throughout the chain.

#### Slice 8C1 — deterministic self-contained recovery publish

**Responsibility.** Produce one `Release/win-x64` self-contained publish and a complete recovery ZIP without MSIX, signing, SBOM, provenance, or distribution claims. This is a recovery/development artifact, not an installer. The workflow is **fresh-output-only**: it accepts one caller-selected output leaf that does not exist and never cleans, deletes, replaces, or reuses caller-provided output.

**Owned paths.** Implementation is limited to `scripts/Publish-Deterministic.ps1` (or a narrowly extracted helper under `scripts/packaging/`), `tests/AIBar.Domain.Tests/PackagingRecoveryTests.cs`, and generated output below ignored `artifacts/8c1/`. Slice 8C1 must not add or modify `packaging/AppxManifest.xml`, `packaging/Assets/**`, SBOM/provenance/notices, signing inputs, application code, or project dependencies. The tasks phase must replace the failed candidate's `PackagingTests.cs`; it must not preserve source-string assertions as acceptance tests. The caller or test harness owns creation of a unique temporary parent and cleanup of the resulting output outside the script.

**Output admission contract.** All admission checks complete before the script creates the output leaf or invokes `dotnet publish`:

1. Normalize the supplied path for comparison and reject an empty path, a filesystem/volume root, and lexical equality, ancestry, or descendancy with the repository and its source inputs.
2. Require the immediate parent to exist as a directory. A missing parent, inaccessible parent, or parent resolved as a file fails closed; the script does not create parent directories.
3. Inspect the existing ancestor chain from the immediate parent to the filesystem root and reject any ancestor currently reported as a reparse point. Reject the output leaf if it exists in any form, including an ordinary file or directory, symbolic link, junction, mount point, dangling reparse entry, or an entry that cannot be classified safely.
4. Repeat the non-mutating leaf-existence and ancestor-reparse observations immediately before creation, then create exactly the previously nonexistent leaf with collision-failing semantics. Any race that makes the leaf exist causes failure; no existing entry is opened for reuse.

These checks are defense in depth, not a filesystem-identity proof. Lexical normalization and path-based attribute observations cannot establish stable identity across Windows namespace races. Slice 8C1 therefore makes no claim that it can defeat a hostile actor that replaces an ancestor or the new leaf between path resolutions. Instead, it removes the destructive consequence entirely: the script contains no recursive deletion, cleanup, replacement, move-over-existing, or rollback operation against caller paths. A stronger adversarial placement guarantee would require handle-relative creation and I/O in a separate native-handle slice; it is not silently approximated here.

**Artifact contract.** After admission, the script creates the fresh output leaf and writes only this contract beneath it:

```text
publish/**
AIBar-win-x64-recovery.zip
recovery-inventory.json
artifact-manifest.json
```

`publish/**` is enumerated recursively. ZIP entries use normalized relative `/` paths, ordinal ordering, fixed timestamps and attributes, deterministic compression settings, and no host/output-root names. The ZIP entry set and each uncompressed entry SHA-256 must exactly equal the recursive publish inventory; nested runtime, culture, configuration, and native files are mandatory. Empty directories are irrelevant, but no publish file may be omitted. `recovery-inventory.json` contains only normalized relative paths, lengths, and SHA-256 values. `artifact-manifest.json` identifies target/configuration and hashes the publish inventory, recovery ZIP, and inventory; it excludes absolute paths, wall-clock values, usernames, temporary roots, and its own hash. Stable artifact identity is the SHA-256 of canonical UTF-8 `artifact-manifest.json`, not a path-bearing object or directory traversal result.

**Failure and rollback behavior.** Admission failure leaves the caller path and parent unchanged and invokes no publish process. If failure occurs after the fresh leaf is created, the script reports a stable failure plus the incomplete caller-owned output path and exits nonzero. It does not recursively delete, partially clean, replace, or roll back that leaf. The caller decides whether and how to inspect or remove incomplete output after the process has exited. A retry always uses another nonexistent output leaf; stale or incomplete output is never an input to a later run.

**Strict TDD gates.** Tests execute the corrected behavior; they do not search script text as acceptance evidence and they never invoke the destructive legacy implementation.

- **RED:** the already recorded deterministic review finding and immutable prior-candidate evidence are terminal RED proof that the legacy workflow deleted or replaced caller-owned output. Re-executing that candidate, deleting a sentinel, or reconstructing its unsafe conditions is prohibited. The RED gate is satisfied by preserving the evidence reference and demonstrating that the corrected contract rejects pre-existing output without mutation.
- **GREEN:** with a unique existing temporary parent and a nonexistent output leaf, invoke the real publish workflow and assert `win-x64`, self-contained output, recursive inventory/ZIP equality, normalized safe entry names, no duplicate/traversal entries, and no diagnostic export file/reference. Executable admission tests must safely cover pre-existing files and directories, filesystem root, repository overlap, parent-as-file as the deterministic unclassifiable-parent shape, missing parent, leaf reparse points and observed reparse ancestors where the environment supports their creation, plus process-not-launched markers for every pre-creation rejection. Assertions parse ZIP and JSON and hash bytes; caller-side test cleanup occurs only after the script exits.
- **TRIANGULATE:** perform two real publishes into two distinct nonexistent leaves under separately created absolute temporary parents. Compare publish relative-path sets and hashes, ZIP byte hash, inventory bytes, manifest bytes, and stable artifact identity. Exercise nested files, spaces/non-ASCII names, reordered source enumeration, leaf-creation collision, and a controlled post-creation publish failure; the latter must leave incomplete output reported and untouched by script cleanup. Reparse tests establish only observed rejection where supported, not stable filesystem identity under concurrent namespace substitution.
- **Runtime failure contract:** `IOException` or `UnauthorizedAccessException` while inspecting or creating the parent or leaf must fail closed with a stable nonzero result, must not launch `dotnet publish` when the failure precedes creation, and must not delete or clean caller-owned paths. Tests must not mutate ACLs or fabricate an inaccessible directory under the same Windows identity. Actual access-denied environment evidence is opportunistic follow-up evidence, not a completion blocker when the environment cannot provide it safely.
- **Gate:** 8C1 completes when both real fresh-output runs, deterministic safe admission cases, process-not-launched markers, post-creation failure preservation, focused/full tests, build, and authored-line cap pass for the exact candidate. Unsupported reparse creation may be recorded as an explicit environment limitation, and unavailable same-identity ACL evidence does not block completion. No stale-output cleanup or replacement criterion remains. The unit makes no signing, MSIX, SBOM, provenance, installability, lifecycle, release, or adversarial namespace-containment assertion.

**Acceptance evidence.** Completion evidence is content-bound to the corrected script and executable tests. It consists of the immutable legacy RED finding/evidence reference; executable fresh-output admission and reproducibility results; explicit process-not-launched markers; stable nonzero failure behavior; focused and full-suite results; a clean build; and an authored changed-line count at or below 400. The currently recorded corrected result—focused 9/9, full 211/211, clean build, and 320/400 authored lines (304 additions + 16 deletions)—meets this design gate when independent verification confirms it belongs to the unchanged candidate. It does not authorize staging, commit, publication, or lifecycle transitions.

**Evidence risks and limits.** The principal residual risk is Windows namespace substitution between path observations; 8C1 does not claim to close it and instead removes destructive operations. Environment-dependent ACL and reparse setup can be unavailable or unsafe under the test identity, so only safely obtainable observations are required. Any naturally occurring inspection/creation access exception still fails closed. Evidence must retain exact candidate identity so historical unsafe behavior, later code changes, or unrelated environment claims cannot be mistaken for current acceptance.

**Task handoff.** Downstream verification must not request destructive legacy replay, sentinel deletion, ACL mutation, or fabricated inaccessible-directory setup. It should verify the corrected fresh-output contract against the evidence above, preserve the `8C1 → 8C2` boundary and the 400-line cap, and treat access-denied environment execution as optional later hardening evidence. This corrective design phase does not edit tasks or apply progress; those records remain historical inputs and any stale checkbox wording cannot override this approved completion contract.

**Forecast.** 280–340 authored changed lines for the script/helper and executable recovery tests, leaving 60–120 lines below the 400-line ceiling for review correction. Generated publish output, ZIP bytes, and caller-created temporary fixtures are excluded from authored-line forecasting but remain in evidence identity and are never committed. If task-level forecasting exceeds 340 authored lines, reduce helper/test duplication before apply; no `size:exception` is authorized.

**Rollback.** Revert only the 8C1 script/helper and executable recovery tests. Any ignored or temporary 8C1 output remains caller-owned and is removed, if desired, by the caller after the script has exited—not by rollback logic inside the script. The reviewed 8B.1 application and explicit export-unavailable behavior remain unchanged.

#### Slice 8C2 — distribution metadata, MSIX, SBOM, notices, and signing gates

**Responsibility.** Starting from reviewed 8C1, add valid MSIX inputs and deterministic distribution metadata. Generate a recognized **CycloneDX 1.5 JSON** SBOM from the restored/published application graph, reconcile it to every shipped direct, transitive, runtime, and native component, record dependency/source provenance, and include every applicable third-party license/notice. Add explicit signing-available and signing-unavailable behavior. This unit does not perform install/upgrade/uninstall lifecycle work; that remains 8D.

**Owned paths.** `scripts/Publish-Deterministic.ps1` and/or helpers under `scripts/packaging/`; `packaging/AppxManifest.xml`; `packaging/Assets/**`; `packaging/PROVENANCE.md` or a machine-readable provenance companion; `packaging/THIRD-PARTY-NOTICES.md` plus required license texts; `tests/AIBar.Domain.Tests/PackagingDistributionTests.cs`; and a pinned repository-local SBOM tool manifest/configuration if needed. Generated output is restricted to ignored `artifacts/8c2/`. Application behavior and diagnostic export remain untouched.

**Manifest and asset contract.** The manifest must use the Windows 10 package schemas and namespaces required for a full-trust WPF desktop executable, x64 identity, per-user execution, `internetClient`, visual assets, and the existing packaged startup-task behavior. Identity publisher must be supplied by controlled build configuration and must match the signing certificate subject when signing is requested; development identity must not be represented as release identity. Every referenced asset must be a decodable PNG with required dimensions/scale variants, not a renamed or empty placeholder. XML parsing and deterministic structural checks are necessary fake-test evidence, but only successful `MakeAppx.exe pack` establishes Windows SDK schema/package acceptance.

**SBOM, provenance, and notice contract.** `sbom.cdx.json` must validate as CycloneDX 1.5 JSON and contain stable package URLs or other canonical identifiers, exact resolved versions, hashes where available, license expressions/evidence, and dependency edges. Coverage is reconciled against restore assets and the actual self-contained publish inventory: each shipped managed package, .NET runtime/framework component family, and native library maps to an SBOM component or an explicit first-party build-output classification. Every direct and transitive dependency must be reachable in the SBOM dependency graph; test-only packages must be excluded from the shipped-artifact graph or clearly scoped outside it. Missing identity, version, dependency edge, license metadata, source origin, or shipped-file mapping is a hard failure, not `Unknown` success.

`PROVENANCE.md`/machine-readable provenance records repository source revision input, SDK/runtime and packaging-tool versions, restore lock/assets hashes, build parameters, and the boundary between AIBar-authored source and third-party material without embedding local paths or credentials. `THIRD-PARTY-NOTICES.md` and accompanying license texts are generated or checked from the complete SBOM component set. Every component whose license requires attribution, notice, or license-text redistribution must be represented; unresolved or incompatible licensing blocks 8C2. An assertion that one known package name and the word `MIT` occur is explicitly insufficient.

**Tool and signing state contract.** Capability detection reports structured, mutually exclusive states and never fabricates an artifact:

| Environment | Required result |
|---|---|
| `MakeAppx.exe` absent | Recovery artifact, SBOM, provenance, and notices may be produced; MSIX status is `tool-unavailable`; no `.msix` exists and no package-valid/installable claim is emitted. |
| MakeAppx present, SignTool or credentials absent | MakeAppx must successfully create an inspectable **unsigned** MSIX; signing status is `signing-unavailable`; no signed/installable claim is emitted. |
| Signing requested but any tool/credential/identity binding is absent | Fail closed with a stable safe error; do not downgrade silently to unsigned success. |
| MakeAppx, SignTool, certificate, and secret provider present | Pack, inspect, sign, and verify the signature. Credentials are caller-owned, never logged, hashed, copied, persisted, added to process arguments when a safer supported channel exists, or included in evidence. |

Deterministic fake tests use injected capability discovery, command planning, process results, certificate metadata, and sanitized logging to cover every state and failure transition. They must assert exact commands/inputs, no secret disclosure, no stale `.msix` survival, and correct status, but they cannot establish manifest validity, signature validity, installability, or Windows compatibility.

**Strict TDD gates.** Tests parse outputs and execute deterministic seams; no source-string test satisfies a gate.

- **RED:** schema/parser tests reject the current manifest/assets; CycloneDX validation and graph reconciliation reject `sbom.packages.json`; notice reconciliation reports all missing applicable components/texts; capability-state tests expose false or ambiguous signing/MSIX claims.
- **GREEN:** produce schema-valid deterministic inputs, a validating CycloneDX 1.5 SBOM with complete graph/file reconciliation, complete provenance/notices, and fail-closed capability/signing state behavior. Fake command tests cover unavailable and planned available paths without claiming external execution.
- **TRIANGULATE:** vary missing MakeAppx, missing SignTool, missing certificate, missing secret, publisher mismatch, tool failure, stale prior outputs, dependency graph changes, native/runtime components, license classes, and output roots. Two metadata runs from separate roots must yield identical canonical SBOM/provenance/notice bytes after approved volatile fields are normalized or omitted.
- **Actual Windows SDK gate:** on a controlled Windows environment, run the real MakeAppx path and inspect the package. When approved credentials are available, run SignTool, verify the signature and publisher binding, and retain sanitized hashes/tool versions/logs. When tools or credentials are absent, this gate is explicitly **not run** and 8C2 may establish only metadata and unavailable-state behavior; it must remain blocked from any `signed`, `installable`, distribution-ready, or release-ready claim. Install/launch evidence remains 8D even after a valid signature.

**Forecast.** 330–360 authored changed lines, including executable tests, helpers, manifest/configuration, provenance/notices, and license material, leaving 40–70 lines of correction headroom under 400. Generated SBOMs, generated valid asset renditions, and golden evidence are excluded from authored-line forecasting but remain in review identity. If task decomposition forecasts more than 360 authored lines before apply, split 8C2 into `8C2A` (manifest/assets and capability state) then `8C2B` (SBOM/provenance/notices/signing runtime gate), and update the immutable chain before implementation; no `size:exception` is authorized.

**Rollback.** Revert only 8C2 helpers/tests/metadata/assets/tool configuration and delete ignored `artifacts/8c2/`. Reviewed 8C1 recovery publishing remains usable and makes no distribution claim.

Use a per-user startup registration abstraction. Packaged builds use the supported MSIX startup-task mechanism where available; unpackaged builds use a per-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry with quoted executable path and a `--startup` argument. Never require elevation, scheduled tasks, services, or machine-wide registration. The UI always reads back effective state after mutation.

Automated packaging smoke tests cover clean install, upgrade with database migration, startup toggle, launch-at-login argument, single-instance activation, uninstall, and user-data retention/removal expectations. Windows 10 and 11 manual/VM checks cover tray recreation, DPI, taskbar placement, sleep/resume, and popover focus. Concrete unsupported behavior is displayed and documented rather than hidden.

## Observability and privacy

Telemetry is off and no remote crash reporter is included in MVP. Local structured diagnostics use event IDs, coarse durations/counts, adapter/parser versions, HTTP status class, and safe error codes. A central redactor runs before every sink and is tested against bearer headers, JSON secret fields, account identifiers, URLs/query strings, and source paths. Raw HTTP bodies, auth files, JSONL lines, prompts/responses, database rows, and exception objects containing those values are never logged.

Logs are local, size/age bounded, and disabled or minimal by default. Diagnostic category preview may use the bounded structured in-memory sinks, but filesystem export is unavailable and disabled; no local export file or remote diagnostic sink is created. Clear AIBar Data cancels work, closes persistence, deletes AIBar cache/database/logs/settings as selected, recreates empty state, and never traverses or deletes the Codex root.

Reintroducing export requires a separate native-handle slice and a new security review. That slice must keep a trusted Windows directory handle open across validation, staging, flush, publication, and cleanup; reject reparse traversal while opening the root and children; create and rename children relative to the verified root handle rather than re-resolving absolute path strings; bind synchronization to stable volume/file identity; verify the destination identity and replacement semantics through handles; and fail closed when the filesystem cannot provide the required guarantees. A managed path-only `DirectoryInfo`/`File.GetAttributes`/`File.Move` sequence is not an acceptable substitute. Required Windows tests include root and ancestor junction substitution at every publication gate, alias paths to the same directory identity, destination substitution, cancellation, cleanup, sharing contention, and proof that no write can escape the held root. This work is a later capability, not part of Slice 8C or the current MVP critical path.

## Testing strategy

Tests follow boundaries rather than UI screenshots alone:

- **Domain unit tests:** freshness transitions, countdown clocks, component-wise deltas, `Unknown`, model ranking formula, pricing warnings, DST/local-day policy, trend windows, and linear ETA insufficiency rules.
- **Adapter contract tests:** auth fixtures with minimum/extra/malformed fields; private endpoint fixtures for valid, missing, changed, 401/403/429/5xx, redirect, timeout, and optional-detail failure; verify no secret reaches error output.
- **Scanner golden/property tests:** supported layouts, unchanged/appended/truncated/replaced files, malformed lines, cumulative resets, cancellation, parser-version invalidation, and randomized non-negative/idempotence properties. Fixtures contain synthetic metadata and no real content or credentials.
- **Persistence integration tests:** transactional checkpoint/aggregate atomicity, migration, crash-before-commit recovery, clear-data isolation, and repricing without token mutation.
- **Coordinator tests with fakes:** cache-first startup, trigger coalescing, manual bypass, stale-on-failure, sleep/resume, and cancellation races.
- **WPF/Windows tests:** view-model state rendering plus focused Windows VM smoke tests for tray, popover, DPI, Explorer restart, startup, and single instance on Windows 10/11.
- **Privacy tests:** recursively inspect AIBar-owned database, logs, and UI/error snapshots for seeded prompt text, bearer values, sensitive IDs, and source paths; assert that export remains unavailable and creates no diagnostic file or network path.

Live private-endpoint tests are opt-in, never run in ordinary CI, require an explicitly provisioned disposable account/session, redact evidence, and cannot become the sole acceptance proof because the endpoint is unsupported.

## Reviewable implementation slices

Each slice is independently testable and MUST be forecast below the 400 authored changed-line review budget; split contracts/fixtures from implementation when the forecast exceeds it.

1. **Solution skeleton and pure contracts** — .NET/WPF host, domain records, clocks, error taxonomy, and test projects; no credential or network behavior.
2. **Credential and private quota adapter** — minimum-field auth reader, allowlisted HTTP client, schema mapping, redaction, and failure contract. Security-focused risk review.
3. **Refresh state and quota cache** — coalescing, freshness, stale preservation, optional-detail degradation, SQLite snapshot persistence. Reliability review.
4. **Tray and quota popover** — single instance, tray states, window lifecycle, both quota windows, countdowns, manual refresh, and disclosure copy.
5. **Scanner discovery and checkpoints** — supported roots/layouts, streaming offsets, invalidation, cancellation, and coverage, without analytics UI. Reliability review.
6. **Daily aggregation and model attribution** — component deltas, timezone policy, transactional daily/model rows, `Unknown`, ranking formula, and clear-data protection.
7. **Pricing and derived metrics** — versioned catalog, incomplete estimates, trends, pace/burn/ETA, and strict source labels.
8. **Startup, packaging, privacy, and compatibility hardening** — effective startup toggle, structured in-memory diagnostics with export unavailable, installers, migration/upgrade checks, and the Windows 10/11 matrix. Resilience review.

The active Slice 8 delivery chain is `8B.1 → 8C1 → 8C2 → 8D → 8E`; 8B.2 is not on its critical path. 8C1 and 8C2 are autonomous review units and neither may exceed 400 authored changed lines. A future diagnostic export is a separately proposed native-handle slice after the current chain, with its own requirements, threat model, strict Windows runtime evidence, and security-focused review. It must not inherit the retired candidate's completion claims or review lineage.

No slice may copy investigated source without a provenance and MIT-notice decision. Generated fixture volume is kept separate from authored-logic forecasts while remaining part of behavioral review identity.

## Rollout and release gates

1. Developer-only builds with synthetic fixtures and the private adapter disabled by default.
2. Opt-in internal build after credential/redaction review and live compatibility validation; show unsupported-endpoint disclosure.
3. Signed limited release only after policy/legal approval, dependency/SBOM review, Windows 10/11 packaging evidence, privacy inspection, a tested remote-free kill switch/build policy for disabling private quota access, and evidence that diagnostic export is unavailable and absent from release artifacts.
4. Broader release only after observing schema/failure behavior without collecting user content or secrets. Diagnostic export remains a disclosed non-goal until the separate native-handle slice is approved.

Rollback is an application downgrade plus schema-compatible database handling; destructive database downgrade is not automatic. If private access becomes prohibited or incompatible, ship/configure it disabled while preserving local analytics and explicit quota-unavailable behavior.

### Failed combined 8C candidate disposition

This corrective design phase modifies only this design artifact. It does not modify or delete code, tests, assets, tasks, progress, generated artifacts, lifecycle state, or review authority. Existing task and progress records remain audit history; where they still demand destructive legacy replay or deterministic same-identity ACL evidence, this approved 8C1 completion contract supersedes those unsafe or environment-dependent demands for downstream verification.

The later approved apply phase must use this disposition:

- **Remove/replace:** `tests/AIBar.Domain.Tests/PackagingTests.cs` is vacuous source inspection and must be replaced by executable `PackagingRecoveryTests.cs` and `PackagingDistributionTests.cs` in their owning units.
- **Rewrite for 8C1:** `scripts/Publish-Deterministic.ps1` may retain harmless parameter names, deterministic archive/manifest logic that is independently re-proven, and the basic `dotnet publish` invocation. Its `Remove-Item -Recurse` cleanup, reuse semantics, path-safety claims, and evidence are unsafe and non-authoritative. The correction removes every script-owned delete/clean/replace/rollback path and admits only a nonexistent output leaf after the complete non-mutating validation sequence. MSIX, signing, SBOM, and notice behavior remains outside 8C1 and may be introduced only by 8C2.
- **Replace unsafe 8C1 tests:** the former `Publish_replaces_stale_output_before_writing_the_recovery_contract` claim is contrary to the approved contract. Its deterministic review finding and immutable prior-candidate evidence are retained as terminal RED proof; the dangerous implementation must never be rerun to delete a sentinel. Current executable tests begin from the corrected fresh-output contract and prove rejection with sentinel preservation, process-not-launched markers, deterministic safe parent-shape failures, and supported reparse observations. Root and lexical source-overlap checks remain defense in depth only; they do not prove filesystem identity.
- **Quarantine until 8C2:** `packaging/AppxManifest.xml`, `packaging/Assets/**`, `packaging/PROVENANCE.md`, and `packaging/THIRD-PARTY-NOTICES.md` provide no accepted evidence. They are absent from the 8C1 diff/result and may be regenerated or replaced in 8C2 only after the corresponding RED gates. Binary placeholder assets are not reusable merely because a file exists.
- **Discard generated outputs externally:** any `artifacts/8c*` output from the failed candidate is stale and must never seed comparison runs, SBOM coverage, package status, or review evidence. The later caller/apply harness may remove its own ignored output before invocation, but the publish script must not delete or clean it; each accepted run receives a different nonexistent leaf.
- **Preserve:** approved application code and 8B.1 diagnostic command/preview/in-memory sinks remain unchanged. Diagnostic filesystem and network export remain unavailable, absent, and unadvertised in scripts, manifests, archives, packages, tests, and notices.
- **Evidence attribution:** failed combined-8C results—2/2 packaging tests, 204/204 suite, signing-unavailable behavior, 196-line count, and its review transaction—remain historical candidate evidence only. The immutable destructive-behavior finding may satisfy only the corrected 8C1 RED gate. Current 8C1 focused/full/build/reproducibility and line-count evidence may satisfy the corrected 8C1 GREEN/TRIANGULATE/completion gates when bound to the unchanged safe candidate; it grants no Windows SDK, signing, installability, lifecycle, release, or 8C2 claim.

### Retired 8B.2 candidate removal boundary

The corrective tasks/apply history returned the worktree to the reviewed 8B.1 behavior before the failed combined 8C attempt:

- delete only `src/AIBar.Application/DiagnosticExport.cs` and `tests/AIBar.Domain.Tests/DiagnosticExportTests.cs` from the uncommitted candidate;
- revert only the Slice 8B.2 deltas in `openspec/changes/aibar-foundation/tasks.md` and `openspec/changes/aibar-foundation/apply-progress.md`, replacing completion/advance claims with truthful retirement evidence while preserving all 8B.1 and earlier history;
- preserve the 8B.0 structured event boundary and 8B.1 command, preview, in-memory sinks, bounded retention, clear-data behavior, and explicit unavailable state unchanged;
- verify no project, package, manifest, UI adapter, dependency, generated artifact, or test references `DiagnosticExport`, creates `diagnostics.jsonl`, or exposes a filesystem/network export path;
- treat the prior 208/208 result as historical candidate evidence only and rerun the approved 8B.1 baseline suite after removal; and
- do not manually delete or rewrite `.git/gentle-ai` authority records. The non-terminal/defective review binding is audit evidence and requires native lifecycle handling outside SDD design/apply scope; it grants no approval and must not be reused to authorize 8C.

This removal boundary deletes no user data, Codex-owned data, or approved diagnostic sink behavior. No code removal is performed by this design phase.

## Design constraints carried forward

- Private service quota, local analytics, and estimated cost remain distinct types and UI sections.
- AIBar never modifies Codex-owned files and never implements browser cookies, passwords, token refresh, multiple roots/accounts, WSL, project analytics, authoritative billing, advanced deduplication, hourly reconstruction, or probabilistic forecasting in MVP.
- Any future move to WinUI or Tauri must preserve the application contracts, credential lifetime, persistence schema semantics, failure taxonomy, and privacy tests defined here.
