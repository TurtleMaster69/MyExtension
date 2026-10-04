# Plan — Telescope results: text rows → columned list (per-finder columns + header column chooser)

> **Lane: feature (e2e ENABLED).** UPDATE 2026-10-04 (user): *"we are on machine that supports
> it and no longer need to defer it"* — the e2e-deferral mandate is LIFTED. The e2e scenarios
> specified here are CREATED + PROVEN RED by `e2e-test-builder` BEFORE the build and EXECUTED by
> `verification-agent` at VERIFY (the full feature-lane loop) — nothing is queued anymore. The
> harness boots the VS Experimental Instance (the no-VS `-SelfCheck`/`-List` gates remain as
> cheap pre-checks). New UI capability + one new `[Telescope]` diagnostic → **M-M7 trigger**
> (feature lane, full pipeline).
>
> **HANDOFF CONSTRAINT (user instruction 2026-10-04):** neovim_hub is implementing Gap 3
> RIGHT NOW — this plan is prepared IN ADVANCE and must NOT change any file he is using.
> All research is read-only; the plan lives in THIS session workspace; the handoff writes
> (`docs/implementation_plan.md`, `docs/progress.md`, the e2e queues) are DEFERRED until
> Gap 3 is GREEN in `docs/progress.md`. The plan touches `Telescope/Overlay/**` (not used by
> Gap 3) — no conflict with the Gap 3 file set. E2E execution also waits for Gap 3's GREEN
> (never boot the harness concurrently with the build loop's own VS usage).
>
> **Source:** user request 2026-10-04: *"start planning migration in telescope list of search
> hits from txt to actual list (right now its just list) … check if we have display file name,
> kind (write, read, …) beside it. if not add it (this is for references, for any other search
> result find other attributes we can display and i will decide if we add it. so 1 row has
> multiple columns)"* + the column decisions: *"implement all but these one that i marked are
> default on"* + *"we can also choose more options if we right click in column header"* +
> headers visible.
>
> **Ground truth:** repo state = post-Gap-1, Gap 3 IN FLIGHT (neovim_hub). Last known GREEN
> baselines: `tests/Telescope.Tests` **172**, `tests/NeoVisual.Tests` **177** (Gap 3 will move
> NeoVisual to 187 — this plan's Telescope.Tests arithmetic must be taken against the ACTUAL
> total at execution time, not a remembered number).
>
> **Research:** `feature-researcher` (Telescope.nvim row anatomy: `entry_maker` →
> `{value, ordinal, display}` with display/ordinal deliberately decoupled; `entry_display.create`
> fixed-width columns + one `remaining=true` flexible column; `…` truncation; `path_display`
> tail/truncate/filename_first modes; snacks.picker declarative formatters; VS Error List
> columns Severity/Code/Description/Project/File/Line; VS Find All References has a filterable
> **Kind column with Read/Write** — exactly the user's references ask). `trailmark-recon`
> (file:line verified): the results host is a read-only WPF **`TextBox` `_resultsBox`**
> (AcceptsReturn, Focusable=false, `Telescope/Overlay/TelescopeOverlay.cs:162-178`); pipeline =
> hits → `FinderEntry{Display, Payload}` (`FinderBase.cs:43`) → fzf filters the Display strings
> (`TelescopeOverlay.cs:393`) → `ResultMapper.MapBack` re-associates payloads (:406) →
> `RenderResults()` writes `ResultsFormatter.ToText(...)` into `_resultsBox.Text` (:452);
> **selection/preview/Enter read `Payload` by INDEX — display-independent** (:471-474, :766-782);
> **references rows ALREADY display file name + access** — `ReferencesFinder.cs:51-52`:
> `{Symbol} ({access}) {basename}:{line}:{col} — {text}` — but only as inline TEXT, not columns;
> the harness pins `results count=N selected=M` (~20 regexes) from a line that includes
> `boxText=…Length`; unit tests pin exact display strings; the overlay is WPF-only.

## Goal

Migrate the Telescope overlay's results list from a text block (a read-only TextBox with
`> `-marked rows) to a **real multi-column list** (WPF `ListView` + `GridView`, headers
visible): 1 row = multiple columns, payload keyed on the row object. **Every** cataloged
attribute becomes a column; the user-marked subset is **default-visible**; **right-clicking a
column header opens a chooser menu** to toggle any column (the VS Error List pattern).

## The column catalog (user-decided 2026-10-04 — implement ALL; marked = default ON)

| Finder | Column (source attribute) | Default |
|---|---|---|
| Files (`f,t`) | File name (basename) | **ON** |
| Files | Directory (path tail after the project root) | **ON** |
| Files | Full path | off |
| Issues (`f,d`) | Kind (Todo/Error/Warning/Info) | **ON** |
| Issues | File | **ON** |
| Issues | Message | **ON** |
| Issues | Line | off |
| References (`f,r`) | Access (read/write) | **ON** |
| References | File | **ON** |
| References | Symbol | off |
| References | Column | off |
| References | Line | off |
| References | Line text | off |
| Grep (`f,g`) | File | **ON** |
| Grep | Line | **ON** |
| Grep | Line text | **ON** |
| Fzf (`f,z`) | File | **ON** |
| Fzf | Line | **ON** |
| Fzf | Line text | **ON** |
| Implementation (`f,i`) | Kind (class/method/…) | **ON** |
| Implementation | File | **ON** |
| Implementation | Symbol | off |
| Implementation | Line | off |

Headers **visible**. (Note the user's marks: Issues/References/Implementation leave `Line`
off — the position stays available via the chooser; Grep/Fzf keep all three ON.)

### D2a — Abbreviated cell values (user instruction 2026-10-04: "shorten what you can … more space for preview")

The narrow columns render COMPACT values (the planner pins the complete table from the real
`Kind` values in `CodeIssue`/`ImplementationHit`):

- **Access**: `write` → `W`, `read` → `R` (user-specified).
- **Issues Kind**: `Error` → `err` (user-specified); by the same principle `Warning` → `warn`,
  `Todo` → `todo`, `Info` → `info` (planner pins).
- **Implementation Kind**: `Implementation` → `imp`, `Function` → `func`, `Interface` → `inf`
  (user-specified; NOTE: `inf` is the user's spelling — it collides with `info` only across
  finders, never within one column, so it is safe; the planner pins the remaining kinds —
  class/method/property/override — in the same compact style).

## Approach (Part 2) — Preview pane → REAL editor view (read-only, no insert mode)

### D9 — Feasibility (researcher-verified, HIGH confidence): BUILD

Replace the preview's RichTextBox + custom `SyntaxHighlighter` with a **real VS editor view**
hosted in the overlay:

- `IWpfTextView.VisualElement` is a `FrameworkElement` — it embeds in the overlay's WPF tree
  (the VS Peek/lightbulb-preview pattern; predefined roles `EmbeddedPeekTextView`/
  `PreviewTextView`/`ChangePreview` prove the hosting model).
- API path (all MEF exports via the repo's `VsServices.Mef<T>` `SComponentModel` pattern —
  VsServices.cs:25-29): `ITextDocumentFactoryService.CreateAndLoadTextDocument(path,
  contentType)` → `IContentTypeRegistryService.GetContentType("csharp")` →
  `ITextEditorFactoryService.CreateTextViewRoleSet(...)` + `CreateTextView(...)` →
  `CreateTextViewHost(view, false)` → host `HostControl`.
- **Roles: `Document + Interactive + Zoomable`, EXCLUDING `Editable`** — `Interactive` is
  required for caret/selection; excluding `Editable` makes the view non-editable AND means
  **VsVim never attaches** (VsVim's `HostFactory` is exported
  `[TextViewRole(PredefinedTextViewRoles.Editable)]`, so its `IWpfTextViewCreationListener`
  never fires for this view — plus programmatic views without shims never raise
  `VsTextViewCreated`). No insert mode, exactly as the user asked.
- **Syntax highlighting is FREE** — classifiers/taggers are MEF parts keyed by content type +
  view role; a "csharp"-typed buffer gets VS's own coloring (syntactic; semantic/Roslyn
  coloring may need the workspace — the planner notes the caveat).
- **Lifetime:** create on the UI thread; `view.Close()` + `textDocument.Dispose()` on overlay
  close (`Deactivated` → `CloseOverlay`); the buffer is an in-memory snapshot (no RDT lock) —
  it does NOT live-update with the main editor (acceptable for a preview; the mtime cache
  logic decides when to rebuild the view/document).
- **The user's leak report is REFUTED by the recon:** `SyntaxHighlighter` is a pure static
  scanner; its brushes are PreviewRenderer-private frozen brushes applied only inside the
  overlay's own FlowDocument (zero WPF/VS/editor coupling; no classifier/format provider
  exists in the repo). The custom colors CANNOT reach the regular editor — what the user saw
  there is VS's own highlighting. The migration still proceeds (real highlighting, real editor
  features, the tokenizer dies).

### D10 — Preview motions + diagnostics under the real editor view

- **Motions:** `TextMotionNavigator` keeps computing the target text position (it is pure);
  the editor view's caret is then moved to that position (`ITextView.Caret.MoveTo` /
  `SnapshotPoint`) + `ScrollToLine`/`ViewScroller` for visibility. a/A/I insert placements stay
  no-ops (the view is not Editable — the same read-only semantics as today, where
  `PromptMotionRouter` already discards them).
- **Diagnostics:** `preview file=` stays; `preview caret=… line=…` stays (computed from the
  view's caret); `preview tokens=…` — the token count was the custom tokenizer's output; under
  the real editor the planner pins the replacement (drop the literal + update the harness, or
  emit the classifier's span count if cheaply available — decide + pin; the harness update is
  deferred-handoff work either way).
- **What dies:** `SyntaxHighlighter` (the tokenizer), the FlowDocument/Run rendering, the
  token cache — retired with the RichTextBox (the planner decides delete-vs-keep-as-fallback;
  prefer DELETE — the user wants the custom highlighting gone).

## Approach (Part 3) — `gd` / `gI` / `gr`: vim goto keys with single-hit direct jump

### D11 — Scope (user instruction 2026-10-04)

Remap go-to-definition (`gd`), go-to-references (`gr`), go-to-implementation (`gI`) so that:
**1 hit → jump directly** (open at line, no overlay); **multiple hits → forward to the
Telescope overlay** and display there (the references/implementation finders already gather
from the caret symbol; definitions need a new gatherer — the Gap 6 backlog item's core).
The full wiring design (how `g`-sequences reach the extension while VsVim owns normal-mode
keys — hook-based g-prefix matcher vs VsVim mapping integration) is RESEARCHED SEPARATELY and
planned as the companion plan `plans/plan-goto.md` in this session (the queue's Gap 6 item);
the columns+preview plan above is unaffected.

## Approach

### D1 — Control swap (the overlay's results host)

`_resultsBox` (read-only `TextBox`) → a **`ListView` + `GridView`** with visible column
headers. `Focusable=false` (the overlay's key handler keeps owning j/k/Ctrl+H/L — selection is
set programmatically: `SelectedIndex = i` + `ScrollIntoView`). The `> ` row marker dies (the
ListView's selection highlight replaces it). Same dock position/size as the old box.

### D2 — Pure column model (the `OverlayKeyHandler` pattern — unit-testable)

New pure types (placement `Telescope/Overlay/Utils/`):
- **`ResultColumn`**: `Id`, `Header`, width kind (fixed-chars vs flexible-remaining), a cell
  getter (`Func<object hit, string>` — or typed per finder), `DefaultVisible`.
- **Per-finder static column sets** implementing the catalog above (one definition list per
  finder; the getter reads the hit model's property — e.g. References' Access getter =
  `hit.IsWrite ? "write" : "read"`, mirroring `ReferencesFinder.cs:51-52`).
- **`ColumnVisibilityModel`**: the ordered column ids + the visible set + `Toggle(id)` +
  `VisibleIds` — order stability (a toggled-off-then-on column returns to its catalog
  position), the all-off edge (the chooser may not leave zero columns — pin the rule: the LAST
  visible column cannot be hidden, or all-off renders an empty header row; the planner picks
  and pins it).

### D3 — Row model + the fzf/ResultMapper pipeline (unchanged semantics)

Rows bind the existing `FinderEntry{Display, Payload}`: the ListView's row cells are computed
from the entry's payload via the column getters; **`Display` stays** — it is fzf's subprocess
input and the filter ordinal. `ResultMapper.MapBack` (display-keyed, duplicate-safe) is
UNTOUCHED — it still re-associates fzf's string output to payloads; selection/preview/Enter
keep reading `Payload` by index. The migration is presentation-only at the data layer.

### D4 — Header column chooser (right-click)

Right-click a `GridViewColumnHeader` → a `ContextMenu` listing ALL the finder's columns with
checkmarks → toggle → the visible `GridViewColumns` are rebuilt from
`ColumnVisibilityModel.VisibleIds` (catalog order). Each toggle logs the NEW diagnostic (D5).
The menu is built per finder from the same column set (no hard-coded menu).

### D5 — Diagnostics

- **Keep `[Telescope] results count=N selected=M` byte-stable** (the harness pins it ~20×).
  The current line includes `boxText=…Length`; post-migration the planner keeps the format by
  computing the rendered row-text length from the visible cells (cheap, deterministic) — OR
  simplifies the line and updates the harness regexes (the Gap-1 precedent allows harness
  updates). **Stated preference: byte-stable** — decide and pin at plan time.
- **NEW literal (M-M7):** `[Telescope] results columns=<comma-separated visible column ids>` —
  logged on render and on every chooser toggle (the e2e gate asserts it; the unit tests pin
  the id list format).

### D6 — Unit tests (`tests/Telescope.Tests` — the overlay is a Telescope type)

RED-first (new types → CS0246): per-finder column-set tests (ALL catalog columns present in
catalog order; the default-visible set EXACTLY the user's marks; getters return the right cell
text from real hit models — incl. References' read/write), `ColumnVisibilityModel` tests
(toggle on/off, order stability, the pinned all-off rule), the row-cell computation, and the
`results columns=` id-list format. Suite arithmetic against the ACTUAL Telescope total at
execution time (172 today; Gap 3 does not touch Telescope).

### D7 — Harness (e2e ENABLED)

The existing `results count=/selected=` assertions must stay GREEN (the byte-stable diagnostic
makes that free). A new scenario asserting the columned render: the default
`results columns=` line per finder + the existing results assertions. The chooser toggle is
NOT keyboard-injectable (the harness injects keys, not mouse) — unit-pinned + manually
verified; the e2e gate covers the default render. The scenario is CREATED + PROVEN RED by
`e2e-test-builder` before the build and EXECUTED at VERIFY.

### D8 — Docs sync (deferred handoff)

`docs/spec.md` (§2.5 overlay description + §4 the new literal + §5 counts), `AGENTS.md`
(feature bullet + counts), `SKILL.md` (overlay description + counts), `docs/progress.md`
(new item → DONE at GREEN).

## Acceptance criteria (each mapped to a diagnostic + a test)

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | The results render as a columned list (headers visible), 1 row = multiple columns | `[Telescope] results columns=<ids>` | unit column-set + visibility tests; e2e `telescope-results-columns` (executed at VERIFY) |
| AC2 | Every catalog column EXISTS per finder (implemented, toggleable) | (unit-only — the column-set definition) | unit `Run_ResultsColumns_*` per finder |
| AC3 | The default-visible set is EXACTLY the user's marks | the default `results columns=<ids>` line | unit default-visibility tests |
| AC4 | Right-click a header → chooser menu toggles any column; order stable | `results columns=<new ids>` after each toggle | unit `Run_ColumnVisibility_*` (the toggle is not keyboard-injectable — unit-pinned + manual) |
| AC5 | References rows show Access (read/write) + File as columns | the default `results columns=` line includes `access,file` | unit References column tests |
| AC6 | Selection/preview/Enter are unregressed (payload by index; fzf + ResultMapper untouched) | existing `results count=/selected=`, `preview file=`, `opened …` lines byte-stable | unit full-suite gate; e2e full re-run at VERIFY |
| AC7 | j/k navigation + the `> `-marker replacement (selection highlight) behave as before | `results count=N selected=M` unchanged | unit OverlayKeyHandler tests stay GREEN; e2e telescope-navigate at VERIFY |

## Unit test plan

**Project: `tests/Telescope.Tests`** (the overlay + column model are Telescope types;
NeoVisual untouched). The planner expands with exact assertions; expected new:
`Run_ResultsColumns_<Finder>_*` (per-finder catalog + defaults + getters),
`Run_ColumnVisibility_Toggle/OrderStability/AllOffRule`, `Run_ResultsColumnsIdFormat`,
+ the existing `Run_ResultsFormatter_*`/`Run_ResultMapper_*` tests must stay GREEN (Display
still produced) or be consciously updated if the formatter's role changes.

## Diagnostics

**New literal (M-M7):** `[Telescope] results columns={ids}` — the plan must list it in the
log-literal diff gate. `results count=/selected=` stays byte-stable (preferred) or is consciously
updated + harness-synced. All other lines unchanged.

## Known-RED allowlist

**None.** Expected RED = the new unit tests before the types exist + the new e2e scenario
before the source lands — created + proven RED by `e2e-test-builder` before the build,
executed at VERIFY (nothing is queued).

## E2E test plan (e2e ENABLED — executed at VERIFY, nothing queued)

| ID | Scenarios | What each asserts | Diagnostics |
|----|-----------|-------------------|-------------|
| E2E-RC-1 | `telescope-results-columns` (**new** — created + proven RED by `e2e-test-builder` before the build) | the columned render; `results columns=file,dir` (the Files default) + the per-finder default columns lines; the byte-stable `results count=/selected=`; j→`selected=1` | `results columns=`, the existing results lines |
| E2E-RC-2 | full-suite re-run at VERIFY (40 registered) | no regression across all telescope-* scenarios (36/37 preview sites byte-stable; the 1 `preview tokens=` site updated per BP-D10) | all existing `[Telescope]` lines |

The chooser toggle is NOT keyboard-injectable (the harness injects keys, not mouse) —
unit-pinned + manually verified; E2E-RC-1 covers the default render.

## Files to be touched (initial estimate)

- **Modified:** `Telescope/Overlay/TelescopeOverlay.cs` (D1/D4/D5), possibly
  `Telescope/Overlay/Utils/ResultsFormatter.cs` (its role may shrink to the diagnostic's
  text-length computation — the planner decides), `tools/harness/test-e2e.ps1` (D7),
  `tests/Telescope.Tests/Program.cs` (D6), `docs/spec.md`, `AGENTS.md`,
  `.opencode/skills/vs-extension-dev/SKILL.md`, `docs/progress.md` (D8).
- **Created:** `Telescope/Overlay/Utils/ResultColumn.cs` (+ the per-finder column sets /
  `ColumnVisibilityModel` — exact file split the planner decides).
- **Not touched:** the finders (`Telescope/Finders/**` — the hit models already carry every
  cataloged attribute), `ResultMapper.cs` (display-keyed fzf path unchanged), `FzfFilter.cs`,
  `OverlayKeyHandler.cs`, `TextMotionNavigator.cs`, anything Gap 3 touches.

## Open risks / uncertainty

1. **GridView star-sizing (medium).** WPF `GridView` columns don't star-size natively — the
   flexible "remaining" text column needs a `SizeChanged` handler (last column width =
   listView width − fixed widths) or accepts a fixed wide width. The planner pins the approach.
2. **`boxText=` in the results diagnostic (medium).** The harness pins the line format; the
   byte-stable path (compute the rendered length) is preferred; a format change must update
   ~20 harness regexes (deferred-handoff work).
3. **Right-click injectability (low).** The harness injects keys, not mouse — the chooser
   toggle may be unit-only + manually verified; the e2e gate then asserts the default render
   only. Decided at plan time.
4. **Virtualization (low).** Results are capped (200 hits for query finders) — a ListView
   handles that without virtualization concerns.
5. **Handoff timing (process).** The plan is complete but the handoff writes wait for Gap 3
   GREEN (the user's no-clobber instruction).

## Build Plan

> **Aggregated (Stage 3) from the round-1 + REV-1 section artifacts** — the authoritative full
> detail (complete code, assertion tables, enumerations) lives in
> `.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-124602/artifacts/`:
> `section-a.md` + `section-a-rev1.md` (the pure column model + abbreviations), `section-b.md`
> (the results swap), `section-p.md` (the preview editor), `section-c.md` + `section-c-rev1.md`
> (the tests), `section-d.md` + `section-d-rev1.md` (the harness), `section-e.md` +
> `section-e-rev1.md` (the docs). Where a REV-1 artifact supersedes round 1, REV-1 wins. The
> steps below are the executable contract; **e2e is ENABLED** (the scenarios are created + proven
> RED before the build and executed at VERIFY).
>
> **Pinned cross-section contracts:** the column-id vocabulary = 10 lowercase ids
> `file,dir,path,kind,message,line,access,symbol,column,text` (the `results columns=` payload =
> `string.Join(",", VisibleIds)`, no spaces, catalog order — e.g. References' default
> `access,file`); the all-off rule = the LAST visible column cannot be hidden; the abbreviated
> cell values = `W`/`R` (access), `todo`/`err`/`warn`/`info` (issues), `cls`/`inf`/`str`/`enm`/
> `func`/`prop`/`evt`/`imp`/… (implementation — the full 30-value union table in
> `section-a-rev1.md`); the diagnostics = `[Telescope] results columns={ids}` (NEW) +
> byte-stable `[Telescope] results count={n} selected={m} boxText={len}` (the length = the
> rendered row text from the visible cells) + `preview file=`/`preview caret=` byte-identical
> (navigator-computed) + `preview tokens=` KEPT (source = the classifier span count) +
> `[Telescope] preview editor unavailable: {reason}` (NEW, once).

### Phase 1 — Pure column model (BP-A1 … BP-A6, rev 1)

- **BP-A1** — `Telescope/Overlay/Utils/ResultColumn.cs` (NEW): the column definition (Id,
  Header, width kind fixed-chars vs flexible-remaining, `Func<object?, string>` getter,
  DefaultVisible). Verify: the Section C tests; Fails: CS0246.
- **BP-A2** — `Telescope/Overlay/Utils/ColumnVisibilityModel.cs` (NEW): ordered ids + the
  visible set + `Toggle(id)` (returns false on the last-visible column — the all-off rule) +
  `VisibleIdsJoined`. Verify: `Run_ColumnVisibility_*`; Fails: a wrap/order drift.
- **BP-A3** — `Telescope/Overlay/Utils/FinderColumns.cs` (NEW): the six per-finder catalogs
  (ALL 23 catalog columns; the user's marks DefaultVisible) + `ForFinder(name, root)`.
  Verify: `Run_ResultsColumns_<Finder>_*`; Fails: a missing/extra/misordered column.
- **BP-A3b** — `Telescope/Overlay/Utils/KindAbbreviations.cs` (NEW): the complete
  abbreviation table (the 30-value union; `CodeIssueKind`'s 4 values; the Implementation
  kinds from `RoslynGatherers.cs:141-144` — `TypeKind/SymbolKind.ToString()`; fallback =
  lowercase ≤4 chars). Verify: the abbreviation unit tests; Fails: an unmapped value.
- **BP-A4** — `Telescope/Overlay/Utils/ResultRowCells.cs` (NEW): the row-cell computation
  (the getters over a hit; `cells[0]` = `W`/`R` for References). Verify: `Run_ResultRowCells_*`.
- **BP-A5/A6** — the build gate + the 23-row catalog audit (read-only).

### Phase 2 — Results host swap (BP-B1 … BP-B6)

- **BP-B1** — `ResultsFormatter` SHRINKS (not dies): delete `ToText`; add the pure
  `RenderedTextLength(rows)` + `ColumnsIdList(ids)` (the byte-stable diagnostic's seams). The
  4 legacy `Run_ResultsFormatter_*` tests are expected-RED until Section C replaces them.
- **BP-B2** — the ctor swap: `_resultsBox` (TextBox) → `ListView`+`GridView` (visible themed
  headers, `Focusable=false` everywhere, ONE `MouseRightButtonUp` AddHandler, a `SizeChanged`
  fill handler), same Grid slot.
- **BP-B3** — the machinery: a nested `ResultRow` (indexer-bound `Binding("[i]")` cells),
  `SyncFinderColumns`/`RebuildColumns`/`RebuildRows`/`ApplySelection`/`ApplyFlexibleColumnWidth`
  (star-sizing: the last flexible column = `max(120, ActualWidth − fixedSum − 18)`; fixed =
  `max(24, FixedChars*8)`), a static per-finder visibility store (fresh overlay per open —
  TelescopeController.cs:72).
- **BP-B4** — `RenderResults` rewrite: the columns rebuild only on toggle/finder change; logs
  `columns=` then the byte-stable `count=`.
- **BP-B5** — the chooser: a visual-tree header hit-test, the full-catalog checkmark
  `ContextMenu`, toggle → rebuild → log; a `_chooserMenuOpen` Deactivated guard + `Closed`
  refocus/restore.
- **BP-B6** — the compile gate + the cross-section contract check.

### Phase 3 — Preview pane → real editor view (BP-P1 … BP-P6)

- **BP-P1** — the Telescope seam `IPreviewEditor`/`PreviewEditorResult` (public, WPF-only
  types — the layering note does NOT accept VS-coupled growth in Telescope, so the VS-coupled
  host lives in MyExtension behind this seam).
- **BP-P2** — the injection chain (`Func<IPreviewEditor>?` through TelescopeController →
  TelescopeOverlay; dispose in `CloseOverlay`).
- **BP-P2b** — the pure diagnostic/caret seams (gate round-1 finding 2): NEW
  `Telescope/Overlay/Utils/PreviewCaretMap.cs` (the navigator target → the editor caret offset,
  clamped; `Line` via the surviving `LineIndex.LineOf`, 1-based) + NEW
  `Telescope/Overlay/Utils/PreviewDiagnostics.cs` (`Caret`/`File` byte-exact format helpers) —
  wired into BP-P3/BP-P4's emission sites (full code in `section-p.md`).
- **BP-P3** — `MyExtension/Package/Utils/PreviewEditorHost.cs` (NEW): MEF via
  `VsServices.Mef<T>`; `CreateAndLoadTextDocument` + the content type BY EXTENSION (not
  hardcoded "csharp") + `CreateTextViewRoleSet(Document+Interactive+Zoomable)` EXCLUDING
  `Editable` (VsVim never attaches) + `CreateTextViewHost`; create-or-reuse by
  path+`LastWriteTimeUtc`; `_host.Dispose()`+`_document.Dispose()` on close; UI thread.
- **BP-P4** — the overlay hosting swap: the RichTextBox slot → a `ContentControl` +
  `HostControl`; the motions (the navigator computes the target; the editor caret moves);
  focus/clear paths.
- **BP-P5** — the RETIREMENT: DELETE `SyntaxHighlighter.cs`, `PreviewRenderer.cs`,
  `PreviewTokenCache.cs`, the overlay's RichTextBox path, `PromptBlockCaretBrush` — no
  fallback (the user wants the custom highlighting gone). 12 tokenizer tests die (Section C).
- **BP-P6** — the compile/layering gate.

### Phase 4 — Unit tests (BP-C1 … BP-C14 + BP-C1b/BP-C6b, rev 1) — `tests/Telescope.Tests`

Pinned arithmetic (gate-reconciled): **172 − 12 (tokenizer) − 4 (legacy formatter) + 25
(columns) + 5 (KindAbbrev) + 8 (formatter seams) + 5 (preview) = 199** (mid-point 206 after
the columns tests; 194 after the deletions). Steps: BP-C1 the region+helpers (RED CS0246);
BP-C2..C7 the per-finder column tests (Files 3, Issues 2, References 2, Grep+Fzf 4,
Implementation 2, Unknown+WidthKinds 2 — the abbreviated values asserted);
**BP-C1b** — DELETE the 4 legacy `Run_ResultsFormatter_*` tests + ADD 8 seam tests
(`RenderedTextLength`/`ColumnsIdList` — option (b): `ToText` has zero production callers
post-BP-B4); **BP-C6b** — ADD 5 `Run_KindAbbrev_*` tests (the 30-value union + the fallback);
BP-C8 ColumnVisibility (6, incl. IdFormat); BP-C9 ResultRowCells (4); BP-C10 the mid-point
gate; BP-C11 DELETE the 12 tokenizer tests (lines 1885-2049 contiguous — conscious retirement,
ordering pinned vs BP-P5); BP-C12 PreviewCaretMap (3, string-based — created by BP-P2b);
BP-C13 PreviewDiagnostics (2, byte-exact — created by BP-P2b); BP-C14 the guards + the final
gate `199 passed, 0 failed`.

### Phase 5 — Harness (BP-D1 … BP-D12, rev 1) — `tools/harness/test-e2e.ps1`

Round 1: BP-D1 the pre-flight; BP-D2 the NEW scenario `telescope-results-columns` (asserts
`results columns=file,dir` + the byte-stable `results count=` + j→`selected=1`);
BP-D3 the six one-line extensions asserting each finder's default columns line;
BP-D4 the byte-stable verification (ZERO edits to the 22 `results count=` sites — none pins
`boxText`, none is `$`-anchored); BP-D5 the conditional fallback (only if Section B changes
the format); BP-D6 the no-VS gates; BP-D7 the handoff notes. REV 1: BP-D8 the 37-site preview
enumeration (14 `preview file=` + 22 `preview caret=` + 1 `preview tokens=`); BP-D9 the
byte-stability pass (36 of 37 sites need ZERO edits); BP-D10 the CONDITIONAL `preview tokens=`
update (1 site + 2 comments — per BP-P's pinned decision); BP-D11 the scenario-survival
verification (telescope-preview 4+1; telescope-preview-motions 19 byte-stable); BP-D12 the
no-VS gates re-run.

### Phase 6 — Docs + e2e queue (BP-E1R … BP-E12, rev 1)

BP-E1R spec §2.5 (the columned list + the real-editor preview + the abbreviated values);
BP-E2R spec §4 (the `results columns={ids}` literal + the conditional `preview tokens=`
update); BP-E3R spec §5/§8 counts (the tokenizer clause removed); BP-E4R spec §7 (the
preview-bullet rewrite + the new results-columns bullet); BP-E5R/BP-E6R/BP-E7R AGENTS.md
(counts/coverage, the e2e counts + scenario bullets, the preview-bullet rewrite + the §7
mirror); BP-E8R SKILL.md (the pointer note, the overlay description, the counts, the key-files
row); BP-E9R progress.md (the new queue item, the DONE flip ONLY at GREEN); BP-E10R the
e2e-queue rows E2E-RC-1..2; BP-E11 the retirement rows (spec §1/§2.2, SKILL.md) + the
DOC-67-2 reconciliation (swap `PreviewRenderer` in check-doc-content.ps1's seam array —
hub-sanctioned, recorded as a DEVIATION); BP-E12 the final gate (the lints + the count +
retirement sweeps). Retired symbols stay UNBACKTICKED in docs until they resolve.

### Phase 7 — e2e RED + VERIFY (the e2e-enabled lane)

- **BP-E2E-1 (RED)** — `e2e-test-builder` creates the `telescope-results-columns` scenario
  (BP-D2/D3's spec) + any needed harness helpers, runs it against the VS Experimental
  Instance, and proves it FAILS before the source changes exist (the RED evidence). The no-VS
  `-SelfCheck`/`-List` gates run first (cheap).
- **BP-E2E-2 (VERIFY)** — `verification-agent` runs the new scenario + the FULL suite
  (40 registered scenarios after this plan's registration — 39 + 1) + both unit suites
  (Telescope 199 / NeoVisual per the post-Gap-3 actual), staggered; the verdict maps failures
  through the Verification Trace.
- **Fails-if:** the new scenario passes before the source lands (a false RED — the test is
  testing nothing); any pre-existing scenario regresses; the unit totals drift.
- **BP-G1 (final gate — defined here; cited by the Verification Trace's unit-gate and
  lints rows)** — the final build + unit + lint gate: `dotnet build` 0 errors; both unit
  suites GREEN (Telescope 199 / NeoVisual per the post-Gap-3 actual); the doc lints
  (`check-doc-refs`/`check-doc-content`) PASS; the retirement/count sweeps 0 remnants.

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_ResultsColumns_<Finder>_*` (15 new) | BP-A1/A3/A3b, BP-C2..C7 | RED CS0246 → GREEN: the full catalog per finder, the default-visible set = the user's marks, the abbreviated cell values |
| `Run_KindAbbrev_*` (5 new) | BP-A3b, BP-C6b | RED CS0246 → GREEN: the 30-value union + the ≤4-char fallback |
| `Run_ColumnVisibility_*` (6 new) | BP-A2, BP-C8 | RED CS0246 → GREEN: toggle/order-stability/last-visible-cannot-hide/IdFormat |
| `Run_ResultRowCells_*` (4 new) | BP-A4, BP-C9 | RED CS0246 → GREEN: `cells[0]=="W"` for a write reference, etc. |
| the 8 formatter-seam tests (BP-C1b) | BP-B1, BP-C1b | the 4 legacy `Run_ResultsFormatter_*` DELETED + 8 `RenderedTextLength`/`ColumnsIdList` tests (RED CS0246 → GREEN) |
| `Run_PreviewCaretMap_*` (3 new) | BP-P2b, BP-C12 | RED CS0246 → GREEN: the navigator target → the editor caret position |
| `Run_PreviewDiagnostics_*` (2 new) | BP-P2b, BP-C13 | RED → GREEN: byte-exact `preview caret=/file=` |
| the 12 DELETED tokenizer tests | BP-P5, BP-C11 | conscious retirement (not RED) — `Run_Syntax_*`/`Run_PreviewTokenCache_*`/`Run_PreviewDocumentCache_*` gone |
| `Run_ResultMapper_*` + the 5 display pins | BP-B4 (must stay GREEN) | Display still produced; the fzf path untouched |
| unit gate | BP-C14, BP-G1 | `199 passed, 0 failed` (Telescope, staggered); NeoVisual per the post-Gap-3 actual |
| e2e `telescope-results-columns` (RED then GREEN) | BP-E2E-1/E2E-2 | RED before the source lands; GREEN after: `results columns=file,dir` (the Files default) + the per-finder default lines + the byte-stable `results count=` |
| e2e full suite (40 registered) | BP-E2E-2 | all pre-existing scenarios GREEN (36/37 preview sites byte-stable; the 1 `preview tokens=` site updated per BP-D10) |
| no-VS harness gates | BP-D6/D12, BP-E2E-1 | `-SelfCheck` PASS; `-List` = 40 (seed-leak last) |
| lints + sweeps | BP-E11/E12, BP-G1 | check-doc-refs 0 unresolved; check-doc-content PASS (the DOC-67-2 seam swap recorded as a DEVIATION); the retirement/count sweeps 0 remnants |

**Known-RED allowlist: NONE.** Expected RED = the new unit tests (CS0246) + the new e2e
scenario before the source lands — that IS the RED evidence the Build Plan fixes. The
TRANSIENT states the verifier must NOT flag: the 4 legacy formatter tests between BP-B1 and
BP-C1b; the 12 tokenizer tests between BP-P5 and BP-C11; the chooser toggle's non-injectability
(unit-pinned + manual); the NeoVisual total moving under Gap 3 (taken as the actual).

## Hub handoff steps (DEFERRED until Gap 3 GREEN — the no-clobber constraint)

1. Verify Gap 3 is GREEN in `docs/progress.md` (the handoff precondition).
2. Write the assembled plan to `docs/implementation_plan.md`.
3. `docs/progress.md`: the new plan as the FIRST pending item (the e2e-ENABLED lane — no
   deferral instructions anymore); the Done entry at GREEN; the Baseline/Decisions updates.
4. The e2e queues: E2E-RC-1..2 appended (they execute at this plan's VERIFY — the queue drains
   inline now); the READY E2E-GAP1-1..5 + Gap 3's gates drain at their VERIFY points.
