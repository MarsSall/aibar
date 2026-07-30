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

---

## Exploration: b2c C1b identity-bound same-volume quarantine rename

**Date:** 2026-07-29
**Scope:** feasibility and safest design only; no implementation or runtime execution

### Verdict

**Primitive feasibility: GO. Current C1b apply: NO-GO until the design, specification, and task plan are amended.** Windows provides a source-handle-targeted rename through `SetFileInformationByHandle` with `FileRenameInfo` and a variable-length `FILE_RENAME_INFO`. The source mutation is bound to the retained directory handle rather than a source pathname. `FILE_RENAME_INFO.RootDirectory` can bind relative target-name resolution to a retained destination-parent handle, and `ReplaceIfExists = FALSE` makes a destination collision fail instead of replacing data.

The current C1a capability cannot invoke that contract safely: its root handle lacks `DELETE` access, it retains no destination-parent handle, and its live direct-child handles can prevent renaming the root directory. No source-path fallback, absolute-target-path fallback, `Directory.Move`, `File.Move`, `MoveFileEx`, or reopen-by-source-path is acceptable.

### Current State

- `DirectoryCapability` retains root and direct-child handles opened with `FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT`, `FILE_SHARE_READ | FILE_SHARE_WRITE`, and desired access `0x00120089`. It validates `FILE_ID_INFO`, final handle paths, reparse state, same volume, containment, and the exact direct-child allowlist.
- The retained root handle does **not** include `DELETE` (`0x00010000`), which rename requires. Access cannot be added later by duplicating the handle.
- C1a retains no handle or identity evidence for the source/destination parent. Resolving an absolute quarantine path after admission would reintroduce a destination-parent namespace race.
- C1a retains direct-child handles without `FILE_SHARE_DELETE`. Microsoft documents that open handles in a directory subtree can block a directory rename; therefore “keep every C1a handle live through rename” is not a viable call contract.
- C1a is independently verified for admission only. C1b, cleanup, deletion, and scavenging remain unimplemented and unverified.

### Exact Primitive and Safe Call Contract

Use the documented Win32 call:

```text
SetFileInformationByHandle(
    admittedRootHandle,
    FileRenameInfo,                 // FILE_INFO_BY_HANDLE_CLASS value 3
    FILE_RENAME_INFO {
        ReplaceIfExists = FALSE,
        RootDirectory = admittedParentHandle,
        FileNameLength = UTF-16 byte length of quarantineLeaf,
        FileName = quarantineLeaf   // one validated simple leaf, no separator
    },
    exact variable buffer size)
```

Required contract:

1. **Admission-time source handle:** open the root once with at least the existing observation rights plus `DELETE`, `FILE_FLAG_BACKUP_SEMANTICS`, and `FILE_FLAG_OPEN_REPARSE_POINT`. Continue withholding `FILE_SHARE_DELETE` on the root. Failure to obtain this access refuses admission; never reopen the source by path.
2. **Admission-time parent handle:** before commit, retain a non-reparse directory handle for the actual parent, record its `FILE_ID_INFO`, final path, and volume, and require parent/root volume equality. Open with directory/traverse/read-attribute rights and sharing compatible with the owned tree. The target parent must not be resolved from a pathname at commit.
3. **Target leaf:** generate and validate one supervisor-owned simple leaf. Reject empty, `.`, `..`, separators, rooted syntax, streams, and case-insensitive collision with the source leaf or reserved names. Pass only that leaf relative to `RootDirectory`.
4. **Final read-only gate:** while root, parent, and child handles are live, revalidate parent/root/children, identities, final paths, non-reparse state, exact allowlist, containment, and same volume. Any uncertainty refuses mutation.
5. **Child-handle transition:** after the final gate, dispose the direct-child handles because descendant opens may block the directory rename. Keep the source-root and destination-parent handles live. This transition must be explicit and one-way; C1b must not claim that child identity is atomically locked with the root rename.
6. **Single commit call:** immediately call `SetFileInformationByHandle` on the retained root handle. `ReplaceIfExists` must remain false; do not use `FILE_RENAME_REPLACE_IF_EXISTS`, POSIX replacement semantics, or any retry that changes the target. A target race therefore fails atomically as a collision. Any false return is pre-commit `CLEANUP_REFUSED`; preserve the source capability and emit only a stable safe code.
7. **Same-volume behavior:** Windows rename supports a file or directory only within one volume. The retained parent/root volume check makes this deterministic before the call; an unsupported filesystem, cross-volume condition, read-only volume, ACL denial, sharing violation, filter-driver refusal, or unavailable information class fails closed. Never emulate a move with copy/delete.
8. **Post-success boundary:** the successful call is the irreversible transition. Re-observe the same root handle and retained parent immediately. Identity must remain the admitted identity; the final handle path must be the expected quarantine child of the retained parent; parent identity/reparse/volume must remain valid. Any failure after the call is `CLEANUP_PARTIAL`, never `CLEANUP_REFUSED` and never rename-back.
9. **Ownership and disposal:** transfer the still-live root and parent handles into a committed-quarantine capability for C2. Dispose child handles exactly once before the call; dispose root/parent only when ownership is transferred or on a verified pre-commit refusal. Unmanaged `FILE_RENAME_INFO` storage must be bounded, zeroed if practical, and freed in `finally`.
10. **Compatibility:** `SetFileInformationByHandle`/`FileRenameInfo` is documented from Windows Vista/Server 2008 and covers the project's Windows 10/11 boundary. `FileRenameInfoEx` is unnecessary because replacement is forbidden. Filesystem and filter support still varies, so false/unsupported outcomes are normal fail-closed results. The API gives an atomic namespace rename, not a portable cross-volume transaction or an unconditional power-loss durability guarantee.

### Reparse and Race Boundaries

- `FILE_FLAG_OPEN_REPARSE_POINT` ensures the retained handle denotes the reparse object itself if one is encountered; final non-reparse checks remain mandatory.
- Source-path substitution after admission cannot redirect the mutation because the API receives the retained source handle.
- Parent-path substitution cannot redirect the destination because relative resolution uses the retained `RootDirectory` handle.
- A competing destination creation is handled by `ReplaceIfExists = FALSE`; it must cause refusal, never replacement.
- Windows does not offer one transaction that atomically revalidates every child identity and renames the root. Closing child handles introduces a narrow child-content race. This does not permit quarantining a different root object, and C1b performs no deletion. C2 must revalidate the exact admitted identities after commit and retain quarantine on any mismatch. Malicious same-user/administrator interference remains outside the stated threat model.

### Rejected Approaches

1. **Path APIs (`Directory.Move`, `MoveFileEx`, absolute target path)** — rejected because source and/or destination-parent lookup is path-based after admission.
2. **Open a second source handle by path at commit** — rejected because it recreates the confirmed TOCTOU. `DuplicateHandle` cannot grant the missing `DELETE` access.
3. **`RootDirectory = NULL` with a simple name** — not selected. Kernel documentation describes same-directory rename, but the Win32 structure documentation also describes process-current-directory-relative names. An explicit retained parent handle is less ambiguous and binds destination identity.
4. **Replacement or POSIX flags** — rejected because destination replacement creates avoidable data-loss and open-target complexity.
5. **Undocumented user-mode `NtSetInformationFile` use** — unnecessary; the documented Kernel32 API exposes the required source handle and parent-relative target contract.

### Gaps Against Existing Artifacts

- `proposal.md`: no change is required; it does not define packaging cleanup mechanics.
- `specs/aibar-foundation/spec.md`: the same-volume quarantine requirement is directionally correct but does not explicitly require a source handle, retained destination-parent handle, collision-failing no-replacement target, or the post-success `CLEANUP_PARTIAL` rule when final observation fails. It also does not acknowledge that child validation and root rename are not one atomic multi-object transaction.
- `design.md`: amendment is required. The interop list mentions `SetFileInformationByHandle` only for deletion, while C1b needs `FileRenameInfo`. The current root access omits `DELETE`, parent capability is absent, and retaining all child handles through rename conflicts with documented directory-rename constraints.
- `tasks.md`: amendment is required before apply. C1b's allowed production path excludes `DirectoryCapability.cs`, yet that file must change to acquire `DELETE` and retain parent capability. The C1a statement that all handles survive through the commit boundary conflicts with the required child-handle transition. The C1b demand to prove every post-validation child race is refused is stronger than Windows can atomically guarantee.
- `apply-progress.md` and `verify-report.md`: remain truthful for C1a admission only. A capability-contract amendment must be separately implemented and reverified; existing C1a evidence cannot prove rename access, parent binding, or rename compatibility.

### Required Amendment Before Apply

Amend **design, delta spec, and tasks** before C1b implementation. Do not silently reinterpret the existing checked C1a work. The amendment should name `SetFileInformationByHandle(FileRenameInfo)`, specify the exact access/share/flag and `RootDirectory` contract above, forbid replacement and all path fallbacks, define the child-handle transition, and classify every post-success uncertainty as retained-quarantine `CLEANUP_PARTIAL`.

### Reviewable Delivery Shape

The full correction cannot safely fit the current single C1b scope and <=350-line forecast because it must revise an independently verified C1a capability plus add the rename adapter, race model, and Windows proof. Split it:

1. **C1b0 — rename-ready capability amendment (~120–190 authored lines):** update `DirectoryCapability.cs` and focused tests to retain parent identity/handle, acquire root `DELETE` access from admission, model deterministic child-handle release, and preserve C1a read-only behavior. No rename.
2. **C1b1 — identity-bound quarantine commit (~220–320 authored lines):** add `Cleanup.cs`, fake race/collision/disposal tests, and one bounded Windows integration proving handle-targeted same-parent directory rename, identity continuity, no replacement, source-path substitution immunity, and retained quarantine. No deletion, rename-back, scavenger, or PowerShell.

Each slice fits below the 400-line review cap and is independently rollback-safe. Combining both is a high budget and review-risk outcome and is not recommended under `ask-on-risk`.

### Authoritative References

- Microsoft Learn, [`SetFileInformationByHandle`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)
- Microsoft Learn, [`FILE_RENAME_INFO`](https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_rename_info)
- Microsoft Learn, [`FILE_INFO_BY_HANDLE_CLASS`](https://learn.microsoft.com/windows/win32/api/minwinbase/ne-minwinbase-file_info_by_handle_class)
- Microsoft Learn, [`FILE_RENAME_INFORMATION`](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information)
- Microsoft Learn, [`CreateFileW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew)

### Risks

- Filesystem/filter-driver behavior and open descendants can refuse rename; refusal must remain a supported outcome.
- The child-handle release window cannot provide atomic child-set immutability; safety depends on root-handle identity binding, no deletion in C1b, and C2 post-commit revalidation.
- “Atomic rename” must not be expanded into an undocumented power-loss durability claim.
- Incorrect variable-buffer layout or access/share constants in P/Invoke could turn a safe design into fail-open behavior; native layout and error-path tests are mandatory.

### Ready for Proposal

**No new proposal is needed.** The change is technically feasible, but C1b apply is blocked until the existing design/spec/tasks are explicitly amended and reviewed.

---

## Exploration: C1b error 87 contract correction

**Date:** 2026-07-30
**Scope:** contractual diagnosis only; no implementation or additional runtime experiment
**Evidence revision:** `sha256:9cca7e8c36d69b567cc3b208ce175f2bf1dd96be564bebff6514450580e7bf00`

### Executive Conclusion

The prior GO verdict is **superseded for the attempted same-parent call shape**. `SetFileInformationByHandle(FileRenameInfo)` does support a non-NULL `FILE_RENAME_INFO.RootDirectory` with a relative target name; the Win32 structure documentation says so explicitly. However, the more specific rename contract requires `RootDirectory == NULL` when the source is not being moved to a different directory. A simple name plus NULL means “rename within the same directory”; a non-NULL target-directory handle is the form for moving to a different directory. C1b attempted a sibling rename in the source's existing parent while also supplying that parent as non-NULL `RootDirectory`. That is the documented contract mismatch that best explains `ERROR_INVALID_PARAMETER` 87.

Native attempt 48 rules out the previous buffer-size theory: x64 size 24, offsets 0/8/16/20, `FileNameLength = 22`, and a 46-byte buffer still returned `FALSE`/87 with `FILE_TRAVERSE | FILE_READ_ATTRIBUTES` on the parent. The source remained present, the destination absent, and the retained source identity unchanged. No further runtime control is required to choose the contract-correct design.

### Current State and Contradiction

- C1a currently retains the root and direct-child handles for identity/reparse/allowlist admission. Its root desired access is `0x00120089`, which does not include `DELETE`; C1b0 still needs rename-ready admission.
- The existing C1b design/spec/tasks require a same-volume **sibling** quarantine target and mandate `SetFileInformationByHandle(FileRenameInfo)` with the existing parent handle in `RootDirectory`.
- The earlier exploration claimed that parent-handle form was the unambiguous and necessary binding for this sibling rename. That claim is not contract-correct: the documented same-directory form is a simple new name with `RootDirectory = NULL`.
- The retained parent remains useful for admission evidence and post-success observation, but it must not be passed as `RootDirectory` for the same-parent operation.

### Authoritative Findings

#### Documented facts

1. `SetFileInformationByHandle` accepts `FileRenameInfo` with `FILE_RENAME_INFO`; the source is selected by `hFile`, and the source handle needs appropriate access. Microsoft documents `FileRenameInfo` as a valid setter class from Windows Vista/Server 2008.
2. The Win32 `FILE_RENAME_INFO` page, which states that the structure is used only with `SetFileInformationByHandle`, documents that a relative name may use a directory handle in `RootDirectory`. Therefore `RootDirectory` is **not generally ignored or required to be NULL** by this API.
3. The detailed Microsoft `FILE_RENAME_INFORMATION` contract narrows the forms:
   - same-directory simple rename: `RootDirectory = NULL`, `FileName` is a simple name;
   - move/rename using a target directory: `RootDirectory` is that target-directory handle and `FileName` is a simple name;
   - full path: `RootDirectory = NULL`.
   It also states directly that `RootDirectory` is NULL when the file is not being moved to a different directory.
4. Rename requires `DELETE` on the source and suitable rights to create the target entry. A target-directory handle used for the cross-directory relative form may be opened with traverse/read-attribute access while the I/O manager performs its separate relative target open.
5. Files and directories can be renamed only within one volume; a volume root cannot be renamed; read-only volumes can refuse rename; no-replacement fails when the destination exists. Open handles in a directory subtree can block a directory rename. C1b must therefore release direct-child handles before commit and treat filesystem/filter/ACL/share refusal as normal fail-closed outcomes.
6. `FileRenameInfoEx` uses the same `FILE_RENAME_INFO` layout and changes the first union member from `ReplaceIfExists` to flags. Its documented value is additional replacement/POSIX/storage semantics. It does not create a stronger parent-identity binding and does not change the same-directory naming form.
7. Microsoft documents `NtSetInformationFile` and the same `FILE_RENAME_INFORMATION` forms, including a user-mode note to use the `Nt` name. Calling it with the same non-NULL/same-parent shape would not cure the contract violation. It is also a Native System Services boundary rather than the preferred Win32 surface for this project.

#### Strong inference

- Because a NULL-root full-name control succeeded in the earlier bounded diagnosis while aligned non-NULL same-parent attempts 47 and 48 returned 87, and because attempt 48 eliminated parent traverse access and buffer size, the same-parent/non-NULL form is the strongest contract-grounded cause of error 87. Microsoft does not publish the internal parameter-validation branch that maps this exact violation to 87, so that final implementation-detail mapping is an inference, not a quoted guarantee.
- A NULL-root simple-name call remains source-handle-targeted and performs no source-path reopen. Under the documented semantics it renames the retained source object within its current parent. With the admitted root opened for `DELETE` while withholding `FILE_SHARE_DELETE`, competing opens that could obtain delete/rename authority are excluded by share compatibility. This avoids the rejected source-path TOCTOU without requiring a destination pathname.

#### Unknowns and limits

- Microsoft does not promise identical behavior across every filesystem and filter stack; a contract-correct call can still fail.
- Windows exposes no documented primitive here that atomically compares a retained parent file ID, validates the full child set, and renames in one transaction. The existing final-gate/release window remains.
- The documentation does not prove that `ERROR_INVALID_PARAMETER` can arise only from this mismatch. It establishes the violated contract and, together with the controls, makes it the decisive design correction.

### Approaches and Security Tradeoffs

1. **Use `SetFileInformationByHandle(FileRenameInfo)` with `RootDirectory = NULL` and one simple sibling leaf**
   - Pros: documented same-directory form; source remains selected by the retained handle; no source or destination-parent path lookup; no replacement; smallest change; keeps the supported Win32 surface.
   - Cons: parent identity is implicit in the source object's current directory rather than supplied as a second commit parameter; final parent/child validation is still not atomic with rename.
   - Effort: Low to medium.

2. **Move to a genuinely different retained destination directory with non-NULL `RootDirectory`**
   - Pros: documented handle-relative cross-directory form; explicit target-directory handle.
   - Cons: changes the quarantine topology and ownership boundary; requires a separately created/admitted destination directory, same-volume proof, ACL/collision/recovery redesign, and corresponding spec/design/task amendments. It is unnecessary for a sibling quarantine.
   - Effort: High.

3. **Call user-mode `NtSetInformationFile` directly**
   - Pros: exposes `NTSTATUS` and uses the documented native structure.
   - Cons: does not permit the invalid same-parent/non-NULL shape; adds Native API interop and status mapping with no security gain over the correct Win32 form. A retained parent is useful only when the destination is actually a different directory.
   - Effort: Medium with no present benefit.

4. **Fail closed and omit cleanup mutation**
   - Pros: strongest conservative boundary if the product requires atomic proof of both original parent identity and child-set immutability beyond Windows' documented rename guarantees.
   - Cons: external isolated roots remain caller-owned/incomplete and B1a2 cannot claim supervisor cleanup.
   - Effort: Low implementation effort, high product impact.

Path-based `Directory.Move`, `MoveFileEx`, full-path `RootDirectory = NULL`, reopen-by-source-path, and copy/delete remain rejected because they reintroduce namespace lookup or non-atomic mutation that the C1 correction was created to avoid.

### Recommendation

Amend C1b to use exactly one `SetFileInformationByHandle(admittedRootHandle, FileRenameInfo, ...)` call with `RootDirectory = NULL`, `ReplaceIfExists = FALSE`, and a validated simple sibling leaf. Keep the parent handle only as retained admission/post-success evidence. C1b0 must still acquire `DELETE` on the source from the original open, retain and validate the parent, and provide the one-way direct-child handle release. C1b1 must fail closed on any false result and classify uncertainty after true as retained-quarantine `CLEANUP_PARTIAL`.

This recommendation does not require another runtime experiment before artifact correction. The eventual C1b1 implementation gate should still contain its already-planned bounded Windows acceptance proof, but it is implementation verification, not further exploration.

### Required Artifact Amendments

- `proposal.md`: no amendment required; it does not define packaging cleanup mechanics.
- `design.md`: **amend required**. Replace the non-NULL retained-parent commit claim for sibling rename with the NULL-root simple-name contract; preserve the retained parent as evidence, not `RootDirectory` input.
- `specs/aibar-foundation/spec.md`: **amend required**. Remove/replace the requirement that commit be bound by passing the parent handle; specify source-handle-targeted same-directory rename and the implicit-current-parent limitation.
- `tasks.md`: **amend required before apply**. Change C1b1 GREEN/GATE wording from retained parent as `RootDirectory` to NULL-root simple sibling name, and adjust tests to prove no path fallback plus retained-parent pre/post evidence.
- `apply-progress.md` and `verify-report.md`: no amendment needed for C1a; they remain C1a-only evidence and grant no C1b feasibility.

### Authoritative References

- Microsoft Learn, [`SetFileInformationByHandle`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)
- Microsoft Learn, [`FILE_RENAME_INFO`](https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_rename_info)
- Microsoft Learn, [`FILE_RENAME_INFORMATION`](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information)
- Microsoft Learn, [`NtSetInformationFile`](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntsetinformationfile)
- Microsoft Learn, [`CreateFileW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew)
- Microsoft Learn, [`File Access Rights Constants`](https://learn.microsoft.com/windows/win32/fileio/file-access-rights-constants)
- Microsoft Windows SDK header mirror, [`minwinbase.h`](https://github.com/microsoft/win32metadata/blob/master/generation/WinSDK/RecompiledIdlHeaders/um/minwinbase.h) (`FileRenameInfo`/`FileRenameInfoEx` values)

### Risks

- Existing design/spec/tasks currently require the disproven same-parent/non-NULL call and are unsafe to apply unchanged.
- Withholding `FILE_SHARE_DELETE` protects against later incompatible opens but does not retroactively neutralize unknown pre-existing handles; admission must fail when share compatibility cannot be established.
- Open descendants, ACLs, filter drivers, unsupported filesystems, read-only state, or target collisions may still refuse a contract-correct rename.
- If reviewers require the retained parent handle itself to be an atomic commit input even for a sibling rename, the correct result is fail-closed architecture or a redesigned different-directory quarantine—not preservation of the invalid call shape.

### Ready for Apply

**No.** Exploration is sufficient, but design/spec/tasks must be amended explicitly before C1b0/C1b1 implementation resumes.

---

## Final Exploration: C1b identity-bound quarantine commit

**Date:** 2026-07-30
**Scope:** final bounded architectural review; documentation and existing evidence only
**Authoritative runtime evidence:** native attempt 49, revision `sha256:c8c9d03ded82d1c3bc5aa100fd043dc5fbd15b644fc63383a410c7f41256bd92`

### Final Verdict

**CONDITIONAL GO.** The current `SetFileInformationByHandle(FileRenameInfo)` NULL-root/simple-leaf design is invalid and MUST NOT be implemented. Attempt 49 proved that this Win32 call resolves the leaf against the process current working directory, not the retained source directory: the one call returned `TRUE`, moved the admitted directory temporarily to repository-CWD `aibar\renamed-dir`, and preserved source-handle identity. This directly supersedes the earlier claim in this artifact that NULL means a same-parent sibling rename for `SetFileInformationByHandle`.

The current no-path-TOCTOU contract remains feasible only if the product explicitly accepts the documented Windows Native System Services dependency and the C1b artifacts are amended to use user-mode `NtSetInformationFile(FileRenameInformation)` with the retained source handle, `RootDirectory = NULL`, and one validated simple leaf. The WDK contract defines that native form as a rename within the source object's current directory. It does not resolve the leaf against process CWD. The retained parent is still not an atomic rename parameter; its identity binding is derived from the source's documented current-directory semantics plus successful no-`FILE_SHARE_DELETE` admission and final retained-parent/source relationship validation. If reviewers require the parent handle itself to be passed as an atomic commit argument for the existing sibling topology, the result is **NO-GO**.

### Correction That Supersedes Prior C1b Conclusions

The following earlier statements are superseded:

- A non-NULL `RootDirectory` naming the existing source parent is not the documented same-parent form for either `FILE_RENAME_INFORMATION` or its Win32 projection. Microsoft says `RootDirectory` is NULL when the object is not moved to a different directory.
- A NULL-root simple leaf is not a same-parent contract for `SetFileInformationByHandle(FileRenameInfo)`. The current Win32 `FILE_RENAME_INFO` page documents a relative path as process-current-directory-relative, and attempt 49 confirms that behavior.
- `SetFileInformationByHandle` and `NtSetInformationFile` cannot be treated as semantically interchangeable for this name form. Their Microsoft pages describe different NULL-root/simple-name resolution contracts. The Native contract, not the Win32 wrapper contract, is required for a path-free sibling rename.
- A retained parent used only for pre/post observation does not by itself atomically bind an absolute Win32 destination. No artifact may claim otherwise.

### Evidence Classification

#### Documented facts

1. `NtSetInformationFile` changes information on the file object selected by `FileHandle`; `FileRenameInformation` requires `DELETE` access on that source handle. Microsoft expressly says that user-mode callers use the `NtSetInformationFile` name.
2. User-mode applications reach Native System Services through `ntdll.dll`. Microsoft says applications may call an `Nt` routine directly when Win32 does not support the required operation. `Zw` entry points are not the user-mode contract and may disappear; C1b must import `NtSetInformationFile`, not `ZwSetInformationFile`.
3. Native `FILE_RENAME_INFORMATION` defines three name forms:
   - `RootDirectory = NULL` plus a simple name: rename within the same directory;
   - `RootDirectory = NULL` plus a fully qualified name: path-based move/rename;
   - non-NULL `RootDirectory` plus a simple relative name: target-directory-handle-relative move to a different directory.
4. A non-NULL `RootDirectory` is a handle used by `IopOpenLinkOrRenameTarget` to open the target directory. Microsoft documents `FILE_TRAVERSE | FILE_READ_ATTRIBUTES` as sufficient for the caller's retained target-directory open while the I/O manager performs its separate relative target open requesting `FILE_WRITE_DATA | SYNCHRONIZE`.
5. `ReplaceIfExists = FALSE` causes an existing target to fail the operation. Rename is same-volume only; a volume root and objects on a read-only volume cannot be renamed. A directory rename can fail because of open descendants, ACLs, sharing, the filesystem, or filters.
6. `CreateFileW` documents that omitting `FILE_SHARE_DELETE` prevents subsequent opens that request delete access and that admission fails if an existing open with delete access conflicts. It also states that delete access permits rename. Share modes remain effective until handle close.
7. Win32 `FILE_RENAME_INFO` documents `FileName` as absolute, process-current-directory-relative, or an NTFS stream name. Its `RootDirectory` can anchor a relative name, but the native rename contract restricts the non-NULL form to a target directory when moving to a different directory.
8. `FileRenameInfoEx`/`FileRenameInformationEx` reuse the rename structure and add replacement, POSIX, pin-state, read-only, and storage-reserve flags. None adds a target-parent identity handle or changes name-resolution topology.
9. The documented Win32 file-management alternatives (`MoveFileEx`, `MoveFileWithProgress`, `ReplaceFile`, and managed move APIs) accept source/destination names, not a retained source handle plus retained same-parent handle.

#### Strong inference

- A retained source opened with `DELETE` while withholding `FILE_SHARE_DELETE`, after conflicting pre-existing opens have caused admission to fail, cannot ordinarily be moved through a newly acquired competing rename-capable handle. Therefore its native "current directory" remains the admitted parent across the final gate and the single `NtSetInformationFile` call under the stated threat model.
- If the retained parent is also opened without `FILE_SHARE_DELETE`, is verified as the source's actual parent immediately before commit, and both handles remain live, native same-directory semantics bind the new directory entry to that admitted parent without resolving a pathname. This is a security argument composed from documented rename and share contracts; Microsoft does not expose one API that atomically compares the retained parent file ID during rename.
- Attempt 49 is decisive against the Win32 NULL-root design but does not contradict the separately documented native NULL-root/simple-name contract. It exercised `SetFileInformationByHandle`, not `NtSetInformationFile`.

#### Unsupported assumptions that C1b must not make

- `SetFileInformationByHandle(FileRenameInfo)` with NULL `RootDirectory` and a simple leaf means "same parent." It demonstrably does not for attempt 49.
- Passing the existing source parent as non-NULL `RootDirectory` is a supported way to rename a sibling. Microsoft documents non-NULL for a move to a different directory.
- Directly calling `NtSetInformationFile` with the same invalid non-NULL/same-parent shape changes that contract.
- Retaining and checking a parent handle before an absolute-path Win32 rename atomically binds the destination lookup to that handle.
- POSIX or `FileRenameInfoEx` flags add parent-handle binding.
- A retained handle alone prevents all namespace interference regardless of requested access/share compatibility, pre-existing handles, filesystem behavior, privileges, or the declared threat model.

### Primitive Evaluation

#### 1. `NtSetInformationFile` / `ZwSetInformationFile`

| Concern | Finding |
|---|---|
| User-mode availability | Microsoft documents user-mode Native System Services through `ntdll.dll` and explicitly directs user-mode calls to the `NtSetInformationFile` name. The API page lists Windows 2000 as the minimum client and identifies `ntdll.dll` as an API location, but its build requirements are WDK-oriented rather than the ordinary Kernel32 Win32 application surface. |
| `Zw` status | Do not use `ZwSetInformationFile` from user mode. Microsoft warns that user-mode `Zw` exports may disappear and provides no Windows SDK header definition for them. |
| Source binding | `FileHandle` identifies the source file object. No source-path reopen is needed. |
| Target-parent binding | A non-NULL `RootDirectory` directly anchors a simple relative target in a **different** directory. It is not the documented existing-parent/sibling form. For the existing sibling topology, NULL plus a simple name uses the source object's current directory; retained-parent identity is established by admission/share invariants, not passed for atomic comparison. |
| Directory rename | Supported within one volume, excluding a volume root and read-only volumes. Open descendants and filesystem/filter behavior may refuse it. |
| Access/share | Source requires `DELETE`; the target parent requires rights to create the new entry. The documented retained-root open for the non-NULL form may request traverse/read-attribute while the I/O manager requests write-data/synchronize for target creation. C1b admission must withhold `FILE_SHARE_DELETE` on source and retained parent and fail on incompatible pre-existing handles. |
| Collision | `ReplaceIfExists = FALSE`; collision fails. No retry with a different target and no replacement/POSIX flag are permitted. |
| Compatibility risk | Higher than Kernel32: direct Native API interop, `NTSTATUS`, `IO_STATUS_BLOCK`, exact native layouts, and `ntdll` entry-point resolution become product-owned compatibility surface. The semantics are documented, but the API is not the preferred ordinary application layer. |

**Finding:** non-NULL `RootDirectory` is a valid, strongly bound cross-directory primitive, but cannot implement the current sibling topology. The only documented path-free sibling form is Native `NtSetInformationFile` with NULL plus a simple leaf.

#### 2. `SetFileInformationByHandle(FileRenameInfo)` with an absolute destination

The retained source remains identity-bound through `hFile`. The destination does not: `FileName` is resolved as an absolute namespace path, and the retained parent handle is not an input to that lookup. Pre-call parent identity/final-path checks and post-call source/parent observation can detect some substitutions, but cannot make the lookup and identity check atomic. Ancestor replacement, mount/reparse namespace changes, or another admissible race can redirect the path between check and use.

**Finding:** this leaves exactly the target-parent TOCTOU prohibited by the current specification. It is not acceptable without an explicit security-contract amendment. Post-success detection cannot undo a mutation safely and does not convert the original lookup into an identity-bound commit.

#### 3. `FileRenameInfoEx` / POSIX flags

No flag adds a retained target-parent handle or atomic parent-file-ID comparison. Replacement and POSIX replacement semantics weaken the collision/no-replace policy; storage and pin flags address unrelated behavior.

**Finding:** no security gain for C1b; reject.

#### 4. Documented Win32 handle-relative alternatives

No documented Win32 file-management API provides a retained source handle and a retained existing-parent handle for a same-parent sibling rename. `SetFileInformationByHandle` is the only relevant Win32 source-handle mutation surface, and attempt 49 plus its current structure contract disqualify its NULL-root/simple-leaf form. Path-based move/replace APIs remain disallowed.

**Finding:** none meets the current contract.

### Security-Contract Decision

The architecture MUST NOT silently accept "verified parent, then absolute destination." The delta specification expressly forbids destination-parent path resolution at commit and requires parent-path substitution either to remain bound to the retained identity or to refuse before source mutation. An absolute destination cannot prove that property. Accepting it would be a security requirement change, not an implementation detail, and would require explicit specification, design, threat-model, and review amendments. This exploration does not recommend that weakening.

The Native sibling form can preserve the existing no-path contract if the artifacts state its real guarantee precisely:

1. Retain one source directory handle opened at admission with observation rights plus `DELETE`, `FILE_FLAG_BACKUP_SEMANTICS`, and `FILE_FLAG_OPEN_REPARSE_POINT`, with no `FILE_SHARE_DELETE`.
2. Retain and identify the actual non-reparse parent on the same volume, also with no `FILE_SHARE_DELETE`; fail admission on incompatible pre-existing opens.
3. While source, parent, and child handles remain live, perform the last read-only identity/reparse/allowlist/containment/volume gate.
4. Release child handles once because descendants may refuse a directory rename; keep source and parent handles live.
5. Validate one collision-safe simple leaf: no empty/`.`/`..`, separator, root, stream, reserved-name, or source-name collision form.
6. Call user-mode `NtSetInformationFile(sourceHandle, ..., FileRenameInformation)` exactly once with native `FILE_RENAME_INFORMATION { ReplaceIfExists = FALSE, RootDirectory = NULL, FileName = simpleLeaf }`.
7. Treat any non-success `NTSTATUS` as pre-commit `CLEANUP_REFUSED`; do not retry, change target, use a path fallback, replace, copy/delete, or rename back.
8. Treat `STATUS_SUCCESS` as the irreversible boundary; re-observe the retained source and parent. Any uncertainty is retained-quarantine `CLEANUP_PARTIAL`.

This contract binds the source directly and the sibling parent through documented native current-directory semantics plus admitted share-mode immobility. It does **not** claim an atomic parent-file-ID comparison or child-set transaction.

### Product Compatibility and Security Obligations

Using `NtSetInformationFile` is acceptable for this Windows-only product only by explicit architecture decision because the required behavior is unavailable through the documented Win32 wrapper and the security benefit is material. Acceptance creates these obligations:

- import `NtSetInformationFile` from `ntdll.dll` by its `Nt` name; never depend on `ZwSetInformationFile`;
- pin the supported OS boundary to Windows 10/11 desktop and fail closed if the entry point or information class is unavailable;
- own x86/x64/ARM64 `FILE_RENAME_INFORMATION` and `IO_STATUS_BLOCK` layouts and bounded variable-buffer arithmetic;
- use `NTSTATUS` success semantics and a closed internal status mapping; if DOS conversion is needed, use documented `RtlNtStatusToDosError` and never log raw paths, handles, or exception text;
- retain the source/parent SafeHandle ownership and no-share-delete invariants through the commit boundary;
- treat filesystem, filter, ACL, sharing, read-only, open-descendant, and unsupported results as normal fail-closed outcomes;
- prohibit all Win32/path fallbacks because fallback would erase the security property that justified Native API use;
- document that malicious same-user/administrator privilege bypass remains outside the existing threat model and that the operation is not an atomic child-set transaction or power-loss durability guarantee.

If this Native API dependency is not acceptable to product maintainers, C1b is **NO-GO** under the current sibling and no-path-TOCTOU contract. The only handle-parent alternative is a topology amendment that moves the source into a genuinely different retained quarantine directory using non-NULL `RootDirectory`; that can use the documented Win32 or Native relative-target form, but it changes quarantine ownership, recovery, ACL, collision, and C2 assumptions and is not a silent substitution.

### Required Prerequisite Amendments

Before C1b apply, reviewers must explicitly approve the Native API dependency and amend design/spec/tasks to:

- replace `SetFileInformationByHandle(FileRenameInfo)` NULL-root wording with user-mode `NtSetInformationFile(FileRenameInformation)`;
- preserve the sibling simple-leaf, no-replacement, source/parent no-share-delete, and fail-closed contract;
- state that parent binding is native current-directory semantics plus retained share/identity invariants, not a parent handle passed to the call and not an atomic parent-ID comparison;
- add the Native entry-point/layout/`NTSTATUS` compatibility obligations and forbid `Zw`, absolute destinations, and every path fallback;
- supersede attempt-49's invalid Win32 primitive while preserving its identity-continuity and zero-residue evidence.

No further architectural experiment is required: Microsoft documentation and attempt 49 discriminate the Win32 and Native contracts. Normal implementation verification remains downstream work, not additional exploration.

### Authoritative References

- Microsoft Learn, [`NtSetInformationFile`](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/nf-ntifs-ntsetinformationfile)
- Microsoft Learn, [`FILE_RENAME_INFORMATION`](https://learn.microsoft.com/windows-hardware/drivers/ddi/ntifs/ns-ntifs-_file_rename_information)
- Microsoft Learn, [Using Nt and Zw Versions of Native System Services](https://learn.microsoft.com/windows-hardware/drivers/kernel/using-nt-and-zw-versions-of-the-native-system-services-routines)
- Microsoft Learn, [Libraries and Headers](https://learn.microsoft.com/windows-hardware/drivers/kernel/libraries-and-headers)
- Microsoft Learn, [`SetFileInformationByHandle`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-setfileinformationbyhandle)
- Microsoft Learn, [`FILE_RENAME_INFO`](https://learn.microsoft.com/windows/win32/api/winbase/ns-winbase-file_rename_info)
- Microsoft Learn, [`FILE_INFO_BY_HANDLE_CLASS`](https://learn.microsoft.com/windows/win32/api/minwinbase/ne-minwinbase-file_info_by_handle_class)
- Microsoft Learn, [`CreateFileW`](https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-createfilew)
- Microsoft Learn, [`MoveFileExW`](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-movefileexw)
- Microsoft Learn, [`RtlNtStatusToDosError`](https://learn.microsoft.com/windows/win32/api/winternl/nf-winternl-rtlntstatustodoserror)

### Final Recommendation

**CONDITIONAL GO: approve user-mode `NtSetInformationFile` only after an explicit correction—use `NtSetInformationFile(FileRenameInformation)`, not `SetFileInformationByHandle`; retain source and actual parent with no `FILE_SHARE_DELETE`; pass NULL `RootDirectory`, `ReplaceIfExists = FALSE`, and one validated simple leaf; preserve all fail-closed and post-success-retention rules; and accept the enumerated Native API compatibility obligations. Without that approval and artifact amendment, C1b is NO-GO.**
