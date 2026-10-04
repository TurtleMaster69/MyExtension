# Plan — Bugfix: the preview's buffer source → the workspace buffer (FULL Roslyn highlighting for solution files)

> **Lane: bugfix (e2e ENABLED).** No new diagnostic literal (`preview file=`/`caret=`/`tokens=`
> unchanged; `tokens=` GROWS when semantic coloring lands — the harness's
> `preview tokens=[1-9]\d*` regex tolerates it). Follows the columns plan's preview work.
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
>   `CurrentSolution.GetDocumentIdsWithFilePath(path)` → `GetDocument(id).GetTextBuffer()` →
>   the view over THAT buffer → the semantic tagger finds the Document → FULL highlighting.
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
> **Ground truth:** the columns plan (FIRST in queue) builds `PreviewEditorHost`
> (`MyExtension/Package/Utils/PreviewEditorHost.cs`) with the standalone
> `CreateAndLoadTextDocument` buffer. THIS plan (queued after it) swaps the buffer source.

## Goal

The preview's editor view renders FULL Roslyn highlighting (syntactic + semantic) for solution
files by sourcing the buffer from the `VisualStudioWorkspace` (the Peek model); the standalone
content-type buffer remains the fallback for non-solution files; every content-type-classified
language (XML/JSON/…) highlights on either path.

## Approach

**D1 — The buffer-source resolution in `PreviewEditorHost`.** The host tries the WORKSPACE
first: resolve `VisualStudioWorkspace` via `VsServices.Mef<T>` (the established pattern) →
`CurrentSolution.GetDocumentIdsWithFilePath(path)` → the first id → `GetDocument(id).GetTextBuffer()`
→ the view over that buffer. On ANY miss (not in the solution / the workspace unavailable) →
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

1. **The workspace-buffer availability for CLOSED files (medium).** `GetDocumentIdsWithFilePath`
   finds project documents whether open or not; `GetTextBuffer()` on a closed document creates
  /returns its buffer — the planner verifies the behavior (a fallback covers the miss).
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
> `_package`); the sync `GetTextBuffer()` is pinned (a CS1061 → STOP and escalate); NO new
> diagnostic literal; the landed harness regex is presence-only `tokens=\d+` (the plan header's
> `[1-9]\d*` claim corrected).

- **BP-1** — RED: 3 `Run_PreviewBuffer_*` tests → CS0246.
- **BP-2** — GREEN: `PreviewBufferSource` (the pure Resolve + OwnsDocument).
- **BP-3** — the host swap: the lazy `VsServices.Mef<VisualStudioWorkspace>` →
  `GetDocumentIdsWithFilePath` → `GetDocument(id).GetTextBuffer()`; `Show` returns
  `_view.TextBuffer` (kills the `_document!` NRE); `CloseView` disposes only under
  `_decision.OwnsDocument`; the roles/fallback unchanged.
- **BP-4** — the build+unit gate.
- **BP-5..BP-7** — the docs (spec §2.5/§4/§7; AGENTS+SKILL; the doc lints).
- **BP-8** — the e2e preview subset + the tokens-VALUE read.
- **BP-9** — the manual visual pass (the semantic coloring).
- **BP-10** — the full gate + progress GREEN.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_PreviewBuffer_*` (3 new) | BP-1, BP-2 | RED CS0246 → GREEN: the workspace-first-then-fallback decision + the ownership |
| the host swap | BP-3 | the workspace buffer used for solution files; the fallback otherwise; the lifetime split |
| unit gate | BP-4, BP-10 | both suites 0-failed; the build 0 errors |
| e2e the preview subset (stays GREEN) | BP-8 | `preview file=/caret=/tokens=` unchanged (the tokens VALUE grows — presence-only regex) |
| the manual visual pass | BP-9 | the semantic coloring visible on a solution file |
| lints | BP-7 | 0 unresolved; PASS |

**Known-RED allowlist: NONE.** Expected RED = the 3 new unit tests before the change exists.
The verifier must NOT flag: the `neovisual-window-management` flake (**×2 cumulative — ONE more
flake = the 3rd-strike upgrade** per the W6 rule in progress.md), the stale harness comment,
or the tokens VALUE growth (presence-only regex).
