# Plan — Bugfix: the preview's buffer source → the workspace buffer (FULL Roslyn highlighting for solution files)

> **Lane: bugfix (e2e ENABLED).** No new diagnostic literal (`preview file=`/`caret=`/`tokens=`
> unchanged; `tokens=` GROWS when semantic coloring lands — the landed harness regex is
> presence-only `preview tokens=\d+` and tolerates it). Follows the columns plan's preview work.
>
> **Source:** the user's 2026-10-04 ask: *"it should be able to highlight, show number of
> references, … everything [the] document editor in visual studio can show … what about c++,
> json, … i want it to work for all languages visual studio supports."*
>
> **Research verdict (feature-researcher, source-verified HIGH confidence):**
> - A STANDALONE buffer (`CreateAndLoadTextDocument`) gets **syntactic-only** C# highlighting:
>   Roslyn's syntactic tagger takes only the `ITextBuffer` (keywords/strings/comments color),
>   but the SEMANTIC tagger bails when the span has no `Document` (`AbstractSemanticOrEmbedded
>   ClassificationViewTaggerProvider.ProduceTagsAsync`) → no type/identifier coloring.
> - **The workspace-buffer swap fixes it** (the Peek model): `VisualStudioWorkspace` →
>   `CurrentSolution.GetDocumentIdsWithFilePath(path)` → `GetDocument(id)` → the LIVE buffer
>   (the RE-PLAN's verified 4.14 chain — the originally drafted `GetDocument(id).GetTextBuffer()`
>   is DEAD on Roslyn 4.14; see the RE-PLAN note) → the view over THAT buffer → the semantic
>   tagger finds the Document → FULL highlighting.
>   The buffer is the live shared buffer — safe for a read-only second view.
> - Non-C# content types (XML/JSON/PowerShell): the content-type-keyed classifiers attach to
>   standalone buffers (MEDIUM confidence) — they highlight on either path.
> - C++: not Roslyn (VC's own service); behavior on a programmatic buffer UNVERIFIED — the
>   fallback path applies; the live e2e/manual pass is the arbiter.
> - **CodeLens (the reference counts): SKIPPED** — it never attaches to a hosted programmatic
>   view (it is a document-well feature computed from the workspace); the References finder
>   already shows per-symbol reference counts.
> - **The document-window embedding: REJECTED** — reparenting an `IVsWindowFrame` into the
>   overlay's HWND is unsupported/fragile (the frame/RDT/command-routing lifecycle); the
>   hosted `IWpfTextViewHost` is the supported pattern (the Peek precedent:
>   `EmbeddedPeekTextView` = a view over the workspace buffer, minus `Editable`).
>
> **Ground truth:** the columns plan (GREEN 2026-10-04, commit `492c6c9`) built
> `PreviewEditorHost` (`MyExtension/Package/Utils/PreviewEditorHost.cs`) with the standalone
> `CreateAndLoadTextDocument` buffer. THIS plan (next in queue) swaps the buffer source.
>
> **HANDOFF (2026-10-04, neovim_hub):** gate-APPROVED in the planning-hub session
> (G5: APPROVE; the handoff USER APPROVED) — plan verbatim from
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-143017/plans/plan-preview-buffer.md`.
> Two handoff corrections (stale era data, the contract unchanged):
> 1. **Test-count baseline:** the plan was authored at Telescope.Tests 212; the CURRENT
>    baseline is **221** (the goto item added 9) → this item lands **221 → 224** (3 new tests).
> 2. **The stale flake note (Verification Trace footer):** the `neovisual-window-management`
>    3rd-strike regression was FIXED 2026-10-04 (the goto item's verify-time debug — step-0
>    editor-focus establishment) and runs 168 + 169 (full suites) were FLAKE-FREE. The
>    known-RED allowlist is **NONE**; per-scenario flaky counts start at **0** for this item.

## Goal

The preview's editor view renders FULL Roslyn highlighting (syntactic + semantic) for
**editor-OPEN solution files** by sourcing the buffer from the `VisualStudioWorkspace` (the
Peek model); CLOSED solution files and non-solution files fall back to the standalone
content-type buffer (syntactic/classifier highlighting — the RE-PLAN's resolved semantics);
every content-type-classified language (XML/JSON/…) highlights on either path.

## Approach

**D1 — The buffer-source resolution in `PreviewEditorHost`.** The host tries the WORKSPACE
first: resolve `VisualStudioWorkspace` via `VsServices.Mef<T>` (the established pattern) →
`CurrentSolution.GetDocumentIdsWithFilePath(path)` → the first id → `GetDocument(id)` →
`TryGetText` → `TryGetTextBuffer(container)` → the view over that LIVE buffer (the RE-PLAN's
verified 4.14 chain — the originally drafted `GetDocument(id).GetTextBuffer()` is DEAD on
Roslyn 4.14; see the RE-PLAN note in the Build Plan). On ANY miss (not in the solution / the
workspace unavailable / a CLOSED file whose container is not editor-backed) →
the EXISTING `CreateAndLoadTextDocument` fallback (the content type by extension).

**D2 — The discipline.** UI thread; any Roslyn async inside `ThreadHelper.JoinableTaskFactory.Run`
(never `.Result`); the buffer is the LIVE shared buffer — the view is read-only (the `Editable`
role excluded — VsVim never attaches), so sharing is safe (the Peek model); the lifetime
(`_host.Dispose()`/`_document.Dispose()`) adapts: a workspace buffer is NOT owned by the host —
only the standalone document is disposed (the planner pins the ownership split).

**D3 — Tests.** The buffer-source decision is VS-coupled (not hermetic) — the seam makes it
injectable: if the host's resolution is extractable pure (the try-workspace-then-fallback
decision), pin it with unit tests (RED-first); else code-inspection + e2e. The planner reads
the landed `PreviewEditorHost` and pins the testable surface.

**D4 — e2e (ENABLED).** The existing preview scenarios stay GREEN (the diagnostics unchanged;
`tokens=` grows — the regex tolerates). The semantic highlighting is NOT harness-assertable —
the manual visual pass is the verification (the planner notes it).

**D5 — Docs.** spec.md §2.5/§7 (the preview's highlighting note: full Roslyn for solution
files, the classifier fallback otherwise; CodeLens skipped — the References finder covers
counts), AGENTS.md/SKILL.md if they describe the preview, progress.md at GREEN.

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | A solution file's preview shows FULL Roslyn highlighting (semantic + syntactic) | (visual; `preview tokens=` grows) | unit (the decision, if extractable); manual visual |
| AC2 | A non-solution file falls back to the standalone buffer (the classifier highlighting) | (visual) | unit (the decision); manual |
| AC3 | The read-only/no-VsVim guarantees are unchanged | the existing lines | the existing tests + e2e stay GREEN |
| AC4 | The lifetime is correct (the workspace buffer not disposed; the standalone document disposed) | (behavioral) | unit (if extractable) + code-inspection |
| AC5 | The existing preview scenarios are unregressed | `preview file=/caret=/tokens=` | e2e at VERIFY |

## Files to be touched

- **Modified:** `MyExtension/Package/Utils/PreviewEditorHost.cs` (D1/D2 — the buffer
  resolution + the ownership split), the tests, the docs.
- **Not touched:** the overlay (the host's seam is unchanged), the finders, the diagnostics.

## Open risks

1. ~~**The workspace-buffer availability for CLOSED files (medium).**~~ **RESOLVED by the
   RE-PLAN (2026-10-04):** a CLOSED document's container is not editor-backed →
   `TryGetTextBuffer` returns null → the standalone fallback covers it (expected `tokens=0`
   there — the pre-fix behavior; NOT a failure).
2. **C++ (medium).** Unverified on programmatic buffers — the fallback path applies; the live
   pass is the arbiter.
3. **The queue position (process).** After the columns plan (its host is the dependency).

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/preview-buffer-section.md` — the authoritative full
> detail (the pure decision code, the host swap, the test code) lives there; the steps below
> are the contract. **e2e ENABLED.**
>
> **Pinned decisions:** EXTRACT the decision (the only automated AC1/AC2/AC4 evidence) — NEW
> pure `Telescope/Overlay/Utils/PreviewBufferSource.cs` (`Resolve` + `OwnsDocument`), hosted in
> Telescope for Telescope.Tests; the host's constructor is frozen (the lazy workspace via
> `_package`); the sync buffer-fetch is pinned in BP-3 (RE-PINNED 2026-10-04 — see the RE-PLAN
> note below; the original `GetTextBuffer()` pin CS1061'd on the Roslyn 4.14 closure → the pinned
> STOP fired and was escalated); NO new diagnostic literal; the landed harness regex is
> presence-only `tokens=\d+` (the plan header's `[1-9]\d*` claim corrected).
>
> **RE-PLAN (2026-10-04, implementation-planner — after the build STOP):** BP-3's pinned
> `Document.GetTextBuffer()` does NOT exist on the Roslyn 4.14 compile closure (CS1061 at
> `PreviewEditorHost.cs:214` — the plan's own pinned STOP case fired; the build-agent's metadata
> decode: no `GetTextBuffer*` on `Document` anywhere in the closure). Re-pinned below, verified
> against BOTH the local NuGet 4.14.0 reference DLLs (reflection probe) and the roslyn source tag
> `Visual-Studio-2022-Version-17.14.17`: `GetDocumentIdsWithFilePath(path)` → `GetDocument(id)` →
> `document.TryGetText(out text)` (public on `TextDocument`, inherited by `Document` — NOTE:
> `GetTextSynchronously` is INTERNAL on 4.14 (present in the metadata but not callable) —
> `TryGetText` is the public zero-I/O member) → `text.Container` (public on `SourceText`) →
> the public extension `Microsoft.CodeAnalysis.Text.Extensions.TryGetTextBuffer(container)`
> (`Microsoft.CodeAnalysis.EditorFeatures.Text` 4.14.0 — a declared transitive dep of
> `Microsoft.VisualStudio.LanguageServices` 4.14.0, nuspec line 23; nullable-return, NO out param
> on 4.14). **Resolved semantics (Open risk #1):** the workspace path engages ONLY for
> editor-OPEN solution documents — their text container is the editor-backed `TextBufferContainer`,
> so `TryGetTextBuffer` returns the LIVE editor buffer (source: `Workspace_Editor.cs`
> `OnDocumentOpened` stores `textContainer.CurrentText` with `PreservationMode.PreserveIdentity`;
> Roslyn's own `GetOpenDocumentText` asserts `TryGetText` succeeds for open docs). A CLOSED
> document's container is not editor-backed → `TryGetTextBuffer` returns null → the standalone
> fallback covers it (expected `tokens=0` there — the pre-fix behavior; NOT a failure). The
> decision shape is UNCHANGED — `documentFound` means "the document resolved AND its container
> yielded a live editor buffer" (the host computes it before `Resolve`); the 3 `Run_PreviewBuffer_*`
> tests stay byte-frozen. BP-8's tokens VALUE read is amended to these semantics; BP-9's manual
> pass must preview an editor-OPEN `.cs` (open one in the editor first) — a closed file exercises
> the fallback and cannot show semantic coloring.

- **BP-1** — RED: 3 `Run_PreviewBuffer_*` tests → CS0246. (LANDED, GREEN — 3/3 pass.)
- **BP-2** — GREEN: `PreviewBufferSource` (the pure Resolve + OwnsDocument). (LANDED, GREEN —
  the file + the tests are byte-frozen; do not touch.)
- **BP-3** — the host swap (RE-PINNED 2026-10-04 — the old `document?.GetTextBuffer()` is DEAD:
  CS1061 on Roslyn 4.14). In `MyExtension/Package/Utils/PreviewEditorHost.cs` keep the artifact's
  edits (a) the `_decision` field, (b) `Show` returns `_view!.TextBuffer...` (kills the
  `_document!` NRE), (d) the `CloseView` gate `_decision.OwnsDocument && _document != null` —
  the ownership split is UNCHANGED (`OwnsDocument` still drives `CloseView`) — and REPLACE the
  body of `TryGetWorkspaceBuffer` with the verified chain (also fix the `RebuildView` lead
  comment: delete the false "open or closed document — a closed document's buffer is created on
  demand" clause; closed documents FALL BACK; and fix the `:225`-area comment claiming "the
  harness's tokens>=1 assertion" — no such assertion exists, the landed regex is presence-only
  `tokens=\d+`. The byte-frozen `PreviewBufferSource.cs` doc comment still citing the dead
  `GetTextBuffer()` is ACCEPTED-STALE — the byte-freeze wins, do not touch it):
  `workspace.CurrentSolution.GetDocumentIdsWithFilePath(path).FirstOrDefault()` → `docId == null`
  → return null → `workspace.CurrentSolution.GetDocument(docId)` → `document == null ||
  !document.TryGetText(out var text)` → return null (a CLOSED file's text is not loaded in the
  solution snapshot — no disk I/O on the UI thread) → `return
  Microsoft.CodeAnalysis.Text.Extensions.TryGetTextBuffer(text.Container);` — the public static
  extension (`Microsoft.CodeAnalysis.EditorFeatures.Text` 4.14.0, namespace
  `Microsoft.CodeAnalysis.Text`, fully qualified to match the file's `VisualStudioWorkspace`
  style): returns the LIVE editor `ITextBuffer` for an editor-backed container (an OPEN
  document), null otherwise; NO out param, NO exception path (unlike `GetTextBuffer`, which
  throws ArgumentException for non-backed containers — `TryGetTextBuffer` avoids
  exception-driven flow; the surrounding try/catch stays as belt-and-braces). `Resolve` is still
  called with `documentFound: workspaceBuffer != null` — the decision shape and the 3 tests are
  byte-frozen. Everything else in the artifact's BP-3 stands (the roles block byte-identical,
  the silent guards, no new diagnostic literal, `ThrowIfNotOnUIThread` on all three methods,
  no `.Result`/`.GetAwaiter().GetResult()`).
  - **Verify-with:** `dotnet build` → 0 errors (proves `GetDocumentIdsWithFilePath`,
    `GetDocument`, `TryGetText`, `Container`, `TryGetTextBuffer` all bind on the 4.14 closure);
    the artifact's code-inspection checklist with the chain swapped; `dotnet run --project
    tests/Telescope.Tests -- PreviewBuffer` → 3 passed.
  - **Fails-if:** CS1061/CS1503 on any NEW chain member (`TryGetText`/`Container`/
    `TryGetTextBuffer`/`GetDocumentIdsWithFilePath`) — the reference set changed → STOP and
    escalate; `preview tokens=0` on an editor-OPEN solution `.cs` (the workspace path did not
    engage — inspect the silent guards); `preview load failed:` in the runtime log (an exception
    escaped — check `Show`'s catch); the fallback gone (a non-solution file previews empty
    instead of rendering); the workspace buffer disposed (the `_decision.OwnsDocument` gate
    missing or inverted). NOT a failure: `preview tokens=0` on a CLOSED `.cs` (the standalone
    fallback — the pre-fix behavior).
- **BP-4** — the build+unit gate.
- **BP-5..BP-7** — the docs (spec §2.5/§4/§7; AGENTS+SKILL; the doc lints).
- **BP-8** — the e2e preview subset + the tokens-VALUE read (RE-PINNED 2026-10-04: the VALUE is
  semantic evidence ONLY for an editor-OPEN `.cs` — non-zero, typically hundreds; a CLOSED `.cs`
  falls back to the standalone buffer and `tokens=0` is the EXPECTED pre-fix behavior, not a
  failure — the harness does not control the previewed file's open state, so read + record the
  VALUE, do not gate on it).
- **BP-9** — the manual visual pass (the semantic coloring; per the RE-PLAN note: preview an
  editor-OPEN `.cs`).
- **BP-10** — the full gate + progress GREEN.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_PreviewBuffer_*` (3 new) | BP-1, BP-2 | RED CS0246 → GREEN: the workspace-first-then-fallback decision + the ownership |
| the host swap | BP-3 | the LIVE workspace buffer for editor-OPEN solution files (`GetDocumentIdsWithFilePath` → `GetDocument` → `TryGetText` → `SourceText.Container` → `TryGetTextBuffer` — all verified public on the 4.14 closure); the standalone fallback otherwise (closed/non-solution files — `tokens=0` expected there); the lifetime split (`OwnsDocument` gates `CloseView`) |
| unit gate | BP-4, BP-10 | both suites 0-failed; the build 0 errors |
| e2e the preview subset (stays GREEN) | BP-8 | `preview file=/caret=/tokens=` unchanged (presence-only regex; the tokens VALUE > 0 only for an editor-OPEN `.cs` — a closed `.cs`'s 0 is the expected fallback, not a failure) |
| the manual visual pass | BP-9 | the semantic coloring visible on an editor-OPEN solution file (a closed file exercises the fallback — syntactic/classifier only) |
| lints | BP-7 | 0 unresolved; PASS |

**Known-RED allowlist: NONE.** Expected RED = the 3 new unit tests before the change exists.
(HANDOFF CORRECTION: the plan's original footer flagged the `neovisual-window-management`
flake ×2-cumulative — STALE: that 3rd-strike regression was FIXED 2026-10-04 and runs 168/169
were flake-free; flaky counts for this item start at 0.) The verifier must NOT flag: the
stale harness comment, or the tokens VALUE growth (presence-only regex).

## Execution Log

### Attempt 1 — BUILD STOPPED on the pinned STOP case (2026-10-04)

- **RED (e2e-test-builder): RED-CONFIRMED** — 3 `Run_PreviewBuffer_*` tests added to
  `tests/Telescope.Tests/Program.cs` (+38 lines; the existing 221 byte-untouched; no VS boot,
  no e2e run). The project failed to compile with 6 CS0103 errors, all mapping to the planned
  missing symbols (`PreviewBufferSource`/`PreviewBufferKind` — BP-2; the plan's "CS0246"
  realized as CS0103, the same missing-symbol class). Baseline 221 → 224 expected at GREEN.
- **BUILD (build-agent):** BP-2 done — `Telescope/Overlay/Utils/PreviewBufferSource.cs` created
  (the pure `Resolve` + `OwnsDocument` + `PreviewBufferKind`), 3/3 tests pass, 224/0. BP-3's
  faithful edits landed, then **the plan's pinned STOP case fired**: `dotnet build` 1 error —
  `PreviewEditorHost.cs(214,33): error CS1061: 'Document' does not contain a definition for
  'GetTextBuffer'`. The build-agent's metadata decode of the compile-time closure (NuGet
  `Microsoft.VisualStudio.LanguageServices 4.14.0` → `Microsoft.CodeAnalysis.Workspaces.Common`
  / `Microsoft.CodeAnalysis.EditorFeatures.Text`, both 4.14.0): NO `GetTextBuffer*` on
  `Document` anywhere; the only candidate is the `[Extension] GetTextBuffer(this
  SourceTextContainer)` in `Microsoft.CodeAnalysis.Text.Extensions`. The agent correctly did
  NOT substitute (the plan: "STOP and report to the hub — do NOT substitute").
- **Cost:** delegations: 2 | VS boots: 0 | iterations: 0

### Attempt 2 — GREEN (2026-10-04)

- **RE-PLAN (implementation-planner):** BP-3 re-pinned to the VERIFIED 4.14 chain —
  `GetDocumentIdsWithFilePath(path).FirstOrDefault()` → `GetDocument(docId)` →
  `document.TryGetText(out text)` (public on `TextDocument`; `GetTextSynchronously` is
  INTERNAL on 4.14 — not callable) → `text.Container` → the public extension
  `Microsoft.CodeAnalysis.Text.Extensions.TryGetTextBuffer(container)` (NULLABLE-return, NO
  out param; `GetTextBuffer(container)` throws for non-backed containers — TryGetTextBuffer
  pinned). Resolved semantics: the workspace path engages ONLY for editor-OPEN solution
  documents (the editor-backed `TextBufferContainer` → the LIVE buffer); a CLOSED document →
  null → the standalone fallback (`tokens=0` expected there — NOT a failure). The decision
  shape PRESERVED (`documentFound` = "resolved AND its container yielded a live editor
  buffer"); the 3 tests byte-frozen. Evidence: a reflection probe of the local NuGet 4.14.0
  reference DLLs + the roslyn source tag `Visual-Studio-2022-Version-17.14.17`.
- **PLAN REVIEW (4a, re-run — the approach changed beyond the trace table): APPROVE** (2
  minors + 2 nits; the hub applied the reviewer's one-line fixes: the internal-not-absent
  `GetTextSynchronously` reword, the stale D1/Goal/risk-#1/header annotations, the third
  comment fix + the accepted-stale frozen-comment note).
- **BUILD (re-dispatch): BP-3..BP-7 all done.** `dotnet build` 0 errors (the CS1061 gone —
  the chain binds on 4.14); Telescope.Tests **224/0**; both lints PASS. Docs: spec.md §2.5/§4/§7,
  AGENTS.md, SKILL.md updated to the resolved state. `DEVIATION: BP-3-third-comment ->
  ACCEPT (TryGetWorkspaceBuffer's own doc comment also described the dead chain; comment-only,
  same edited method, no behavior/diagnostic impact)`.
- **VERIFY (verification-agent) — the FINAL GATE: PASS.** Harness-health first: `-SelfCheck`
  PASS, `-List` 41, both lints PASS. Units staggered: Telescope **224/0**, NeoVisual **190/0**.
  Full e2e FRESH boot (`-TimeoutSec 2400`, run 170): **41/41 GREEN**, first-time, zero flakes.
  Deployment integrity: the deployed DLL byte-identical (SHA-256) to the post-fix build output,
  both new chain members in its metadata. BP-9's visual pass via the automated proxy + code
  inspection: AC1-AC5 verified.
- **FINDING adjudicated (hub):** the `preview tokens=` read is TIMING-BOUND —
  `CountClassificationSpans` is read synchronously at view creation, BEFORE async
  classification lands → it reads **0 for BOTH buffer sources** (runs 168/169/170 all-0) and
  cannot discriminate workspace-path engagement. `DEVIATION: tokens-expectation -> ACCEPT
  (docs-expectation vs diagnostic-reality mismatch; the mechanism is proven by code inspection
  + the deployment metadata + the Peek precedent; the docs' "non-zero (typically hundreds)"
  expectation corrected in AGENTS.md + spec.md §4; the semantic coloring is verified by the
  manual visual pass; a diagnostic-improvement candidate filed in progress.md's queue)`.
- **Failure-log sweep:** 10 entries read, 0 fixed, 0 queued, 0 annotated (all entries already
  carry FIXED resolutions).
- **Cost:** delegations: 5 | VS boots: 1 | iterations: 0
