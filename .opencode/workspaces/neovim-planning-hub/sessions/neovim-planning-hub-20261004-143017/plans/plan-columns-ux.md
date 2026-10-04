# Plan — Bugfix + UX: the Telescope results columns (no h-scroll, all columns visible, min/max widths, logical shortening, column-relative window width, the selection contrast)

> **Lane: bugfix + UX (e2e ENABLED).** Fixes + refines the just-landed columns feature per the
> user's 2026-10-04 reports/directives. No new diagnostic literal (the existing
> `[Telescope] results columns=` + `results count=` lines are the contract, unchanged). JUMPS
> THE QUEUE (the first pending item — the user is looking at the UI now).
>
> **User requirements (2026-10-04, verbatim intent):**
> 1. *"the results of telescope have scrollbars remove them we dont need them. it can be
>    scrolled in height but it shouldnt be scrollable in width. all columns should be visible
>    right now they are hidden... its not user friendly"*
> 2. *"also the text under highlited item is not visible since its same color"*
> 3. *"the columns should have min and max width if max width is reached they should shorten
>    the content but shorten it logicaly(it should only lose information that is not extremly
>    important)"*
> 4. *"the width of results/whole telescope window should change relative to number of
>    column"*
> 5. *"the location should be shortend by removing front part not last part since last part is
>    most important(we want to at least see end folder cuz only seeing start of path gives no
>    meningfull information"*
>
> **Ground truth:** the columns plan is GREEN (the user is using it). The planner reads the
> LANDED code (`Telescope/Overlay/TelescopeOverlay.cs` — the ListView/GridView construction,
> the width logic, the item style; `Telescope/Overlay/Utils/FinderColumns.cs` + the column
> model) — the real code is the truth.

## Goal

The results list NEVER scrolls horizontally and ALWAYS shows every column: each column has a
**min and max width**; the content shortens LOGICALLY at its width cap (path-like columns
truncate from the FRONT keeping the tail — the end folder + file name are the important part;
text columns truncate at the END); the overlay window's WIDTH scales with the number of
visible columns (more columns → a wider window, capped by the work area); the selected row's
text contrasts with its highlight.

## Approach

**D1 — Per-column min/max widths (the model change).** `ResultColumn` gains `MinWidth` +
`MaxWidth` (in chars; the planner pins the values per column: the semantic columns
kind/access/line get tight caps — e.g. access min 2/max 4, kind min 3/max 8, line min 2/max 5;
the path-like columns file/dir/path get wider caps — e.g. file min 6/max 30, dir min 6/max 40,
path min 10/max 60; the text column min 10, NO max — it absorbs). The planner reads the landed
`FinderColumns` + pins the table per column per finder.

**D2 — The pure width-fit algorithm.** NEW pure `ColumnWidths.Compute(availableWidth, columns)`
→ the per-column pixel widths:
- start at each column's MIN; distribute the surplus toward each column's MAX in priority
  order (the narrow semantic columns reach their max first; the text column absorbs whatever
  remains);
- never below MIN, never above MAX; the total = the available width exactly (the invariant);
- the degenerate case (the available < sum(MINs)): the MINs win (the caller then widens the
  window per D3 — the algorithm reports the needed width).
RED-first (the function doesn't exist).

**D3 — The window width scales with the columns (R4).** The overlay's width = 
`max(defaultWidth, sum(visibleMinWidths) + previewWidth + chrome)` — recomputed when the
visible column set changes (the chooser toggle) and at open; capped at the work area width
(`SystemParameters.WorkArea.Width`). The planner pins the formula + the preview's share (the
user's motivation: more columns must NOT eat the preview — the window GROWS instead).

**D4 — The logical shortening (R3+R5).** NEW pure helper(s):
- `TailTruncate(text, maxWidth)` — for PATH-LIKE columns (file/dir/path/location): remove the
  FRONT, keep the TAIL, prefix `…` (e.g. `C:\Very\Long\Path\Models\Services\Order.cs` →
  `…\Services\Order.cs`) — the end folder + file name survive (the user's explicit example);
  Telescope.nvim's `path_display="truncate"` is the reference (it truncates the path start).
- `EndTruncate(text, maxWidth)` — for TEXT columns (message/line text): remove the END
  (the standard `…`) — the start of a line is the important part there.
- The column definition carries the truncation KIND (Tail for the path-like, End for the
  text); the cell rendering applies it at the computed width. RED-first.

**D5 — The horizontal scrollbar OFF.** `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`
on the ListView — combined with D2/D3 (the widths fit + the window grows), nothing is clipped
or hidden. The vertical scrolling stays.

**D6 — The selection contrast.** The `ItemContainerStyle` pins the selected-row colors
explicitly (the highlight background + a CONTRASTING foreground, both the active and the
inactive-selection states) — consistent with the overlay's palette. The planner reads the
landed styles first.

**D7 — Tests.** `tests/Telescope.Tests`: `Run_ColumnWidths_*` (the fit/the min-max/the
priority distribution/the exact-total invariant/the degenerate case), `Run_TailTruncate_*` /
`Run_EndTruncate_*` (the logical shortening: the tail keeps the end folder + the file name;
the short-path no-op; the exact-width edge), the window-width formula test. RED-first. The
existing column tests stay GREEN (the catalog is unchanged; the model gains members).

**D8 — e2e (ENABLED).** The existing `telescope-results-columns` scenario stays GREEN (the
diagnostics byte-stable). The visual defects are NOT harness-assertable — the unit-pinned
algorithms + a MANUAL visual pass (the planner notes it in the verification checklist).

**D9 — Docs.** Minimal: the column-model description (the min/max + the truncation kinds) in
SKILL.md/spec.md if documented there; progress.md at GREEN.

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | NO horizontal scrollbar, ever | (visual — the unit-pinned invariant) | unit `Run_ColumnWidths_*` |
| AC2 | ALL columns visible at any width (the min/max fit + the window growth) | (visual) | unit `Run_ColumnWidths_*` + the window formula test |
| AC3 | Each column respects its min AND max | (visual) | unit (the min/max table pinned) |
| AC4 | Path-like columns truncate from the FRONT (the tail survives: the end folder + file name) | (visual) | unit `Run_TailTruncate_*` |
| AC5 | Text columns truncate at the END | (visual) | unit `Run_EndTruncate_*` |
| AC6 | The overlay width scales with the visible column count (capped by the work area) | (visual) | unit (the formula) |
| AC7 | The selected row's text contrasts with the highlight (active + inactive) | (visual) | code-inspection + manual |
| AC8 | The existing diagnostics/behavior are unregressed | `results columns=` / `results count=` byte-stable | the existing tests + e2e stay GREEN |

## Files to be touched

- **Modified:** `Telescope/Overlay/TelescopeOverlay.cs` (D5/D6 + D2/D3's call sites), the
  column model (`ResultColumn` +`MinWidth`/`MaxWidth`/the truncation kind — D1/D4),
  `FinderColumns` (the per-column values), the tests.
- **Created:** `Telescope/Overlay/Utils/ColumnWidths.cs` + the truncate helpers (the exact
  split the planner pins).
- **Not touched:** the finders, ResultMapper, the diagnostics, the harness.

## Open risks

1. **The min-width floor vs the tiny overlay (low).** The window-growth formula (D3) makes the
   floor unreachable in practice; the degenerate branch is pinned anyway.
2. **The theme consistency (low).** The selection colors must match the overlay's palette.
3. **The tail-truncate readability (low).** `…` + the tail must still identify the file — the
   tests pin the minimum tail (the file name + at least one folder).

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/columns-ux-section.md` — the authoritative full
> detail (the code, the 23-site table, the test code) lives there; the steps below are the
> contract. **e2e ENABLED** (the existing scenario stays green; the visual ACs are the
> verify-agent's manual pass).
>
> **Pinned tables:** the min/max (chars): file 6/30 Tail · dir 6/40 Tail · path 10/60 Tail ·
> kind 3/8 End · message 10/∞ End · line 2/5 End · access 2/4 End · symbol(Refs) 6/24 End ·
> symbol(Impl) 6/∞ End · column 2/8 End · text 10/∞ End. TRUNCATION: Tail = file/dir/path
> (the front removed; the end folder + the file name survive); End = all text/semantic columns.
> WINDOW FORMULA: `Width = min(max(760, NeededWidth+18+480+22), WorkArea.Width)`; the results
> column = Pixel(`Width−502`); Compute's available = that −18; recomputed at open + every
> chooser toggle.

- **BP-1** — `ResultColumn` +`MinWidth`/`MaxWidth`/`ResultColumnTruncation` (Tail/End).
- **BP-2** — pin the per-column table in `FinderColumns` (all 23 sites).
- **BP-3** — NEW pure `ColumnWidths` (Compute/NeededWidth/WindowWidth).
- **BP-4** — NEW pure `ColumnTruncation` (Tail/End).
- **BP-5** — the truncating `ResultRowCells` overload.
- **BP-6** — the h-scrollbar Disabled + the selection contrast (the brush keys + the IsSelected
  trigger; the per-cell Foreground removed).
- **BP-7** — the Pixel results column + `ApplyWindowWidth` + the Compute-owned widths.
- **BP-8** — the truncation in `RebuildRows` + the recompute at open/toggle.
- **BP-9** — 13 unit tests (the RED classification per test).
- **BP-10** — the minimal docs.
- **BP-11** — the final gate (the build + Telescope 212 + NeoVisual 190 + the e2e + the manual
  visual pass).

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_ColumnWidths_*` (the fit/min-max/priority/exact-total/degenerate set) | BP-3, BP-9 | RED CS0246 → GREEN: the exact-total invariant; never below min / above max |
| `Run_ColumnTruncation_*` (Tail/End) | BP-4, BP-9 | RED → GREEN: the tail keeps the end folder + the file name; the end-truncate for text |
| the truncating cells | BP-5, BP-8 | the cells shorten at the computed width |
| the window formula | BP-3, BP-7 | the width scales with the visible columns; capped by the work area |
| the selection contrast | BP-6 | the IsSelected trigger sets a contrasting foreground (active + inactive) |
| the h-scrollbar | BP-6 | Disabled; the vertical unchanged |
| unit gate | BP-9, BP-11 | Telescope 212 / NeoVisual 190 (staggered) |
| e2e `telescope-results-columns` (stays GREEN) | BP-11 | the diagnostics byte-stable |
| the manual visual pass | BP-11 | the verify-agent's checklist: no h-scroll; all columns visible; the contrast readable |

**Known-RED allowlist: NONE.** Expected RED = the new unit tests before the change exists.
The visual ACs (AC1-AC3, AC6-AC7) are unit-pinned + manually verified — the harness cannot
see pixels; the verifier must not demand a pixel assertion.
