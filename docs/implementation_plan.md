# Implementation Plan — Item: 4 planned E2E scenarios (open-file-searchbox, open-file-navigation, explorer-open-navigation, explorer-open-searchbox)

> **Lane: trivial** — scenario-only additions to `tools/test-e2e.ps1` covering
> EXISTING behaviors with new coverage angles. ALL 4 scenarios assert on
> EXISTING `[Telescope]`/`[NeoVisual]` diagnostics — NO new diagnostic line, NO
> diagnostic-format contract change (M-M7 NOT triggered). Expected: no extension
> code change (any scenario that FAILS against the current extension exposes a
> REAL behavior gap and escalates that sub-part to a bugfix/feature item).
> Trivial lane: no docs-reviewer gates; one VS boot for the builder's run; one
> VERIFY pass.

---

**Goal:** Add the 4 user-requested planned E2E scenarios from `docs/progress.md`
(`## New planned E2E scenarios`) — they extend the suite's coverage of
file-opening paths (Telescope search-box + navigation; Solution Explorer tree
navigation + search box). Scenario count 29 → 33.

---

## The 4 scenarios (from docs/progress.md, refined)

1. **`telescope-open-file-searchbox`** — Telescope: type a query in INSERT mode,
   WAIT for the filter to settle (`results count=N selected=0`), Enter → the
   matched file opens. Distinct from `telescope-open-file` (which asserts the
   generic path) by explicitly waiting for the filtered single result before
   Enter and asserting the opened file matches the filtered candidate.
   Diagnostics: `[Telescope] promptChanged query=...`, `results count=1 selected=0`,
   `[Telescope] opened file: ...`.
2. **`telescope-open-file-navigation`** — Telescope: type a query, Esc to NORMAL
   mode, **j/k to MOVE the selection off index 0** (e.g. down to index 1),
   Enter → the SELECTED result opens (proving Enter opens the moved-to row, not
   just the first). Distinct from `telescope-open-file-normal` (which opens
   index 0 without moving).
   Diagnostics: `[Telescope] results count=\d+ selected=1`, `[Telescope] opened file: <the index-1 file>`.
3. **`explorer-open-navigation`** — Solution Explorer: l expands, **j/k to
   navigate the tree to a specific FILE node**, `o` opens it.
   Distinct from `neovisual-explorer-open`/-`open-o` by navigating with j/k to a
   target node first (not just expanding until an editor view happens to open).
   Diagnostics: `[NeoVisual] toolwindow-move key=J/K`, `solution-explorer open`,
   `[NeoVisual] editor-view-opened file=...`.
4. **`explorer-open-searchbox`** — Solution Explorer: `i` focuses the search box
   (`solution-explorer search-focus` + `toolwindow-enter-input`), type a query
   (native tree filtering), then `o` opens the filtered result. Watch the
   search-box focus path + `TextMotionHelper` handling (per the backlog note).
   Diagnostics: `[NeoVisual] solution-explorer search-focus`, `toolwindow-enter-input`,
   `solution-explorer open`, `[NeoVisual] editor-view-opened file=...`.

---

## Acceptance criteria

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `telescope-open-file-searchbox` passes (filtered insert-mode open) | `[Telescope] results count=1 selected=0` + `[Telescope] opened file: ...` | e2e |
| A2 | `telescope-open-file-navigation` passes (j/k-moved selection opens) | `[Telescope] results count=\d+ selected=1` + `[Telescope] opened file: <index-1 file>` | e2e |
| A3 | `explorer-open-navigation` passes (j/k to a file node, o opens) | `[NeoVisual] toolwindow-move key=J` + `solution-explorer open` + `editor-view-opened file=...` | e2e |
| A4 | `explorer-open-searchbox` passes (search-box filter + o opens) | `[NeoVisual] solution-explorer search-focus` + `toolwindow-enter-input` + `solution-explorer open` + `editor-view-opened file=...` | e2e |
| A5 | Existing suite unaffected (scenario count 29→33; no code change) | — | full-suite final gate |

---

## Tests

- **No offline unit tests** (no new pure logic — all 4 cover existing paths with
  existing diagnostics).
- **4 new e2e scenarios** registered via `Register-Scenario` (exact names above).
  All assert EXISTING diagnostics; harness conventions apply (`$script:PfxTel`/
  `$script:PfxNeo`, `Reset-LogBaseline`, `Wait-NewLogLine`, no advancing cursor,
  `Assert-VsFocused`/`Assert-OverlayFocused` before key sequences).

---

## RED evidence plan (e2e-test-builder, trivial lane)

The builder registers the 4 scenarios and runs them against the CURRENT
extension (one VS boot):
- **Expected: all 4 PASS** — pure coverage additions (the behaviors already
  exist). This is the "RED" for this item: the scenarios did not exist before;
  their green run against unchanged code proves they are coverage, not
  feature-fixes. No build-agent work needed.
- **If ANY scenario FAILS against the current extension:** that scenario exposes
  a REAL behavior gap (e.g. the search-box open path is broken, or j/k-moved
  selection opens the wrong file). The builder reports the exact failing
  assertion + diagnostic; the hub then re-plans that sub-scenario as its own
  bugfix/feature item (it will likely need a new diagnostic or a code fix —
  M-M7 applies to THAT item, not this one).

The builder must NOT modify any extension code and must NOT modify the OTHER
existing scenarios.

---

## Known-RED allowlist (for VERIFY)

- **None.** All 4 new scenarios must be GREEN (they cover existing behavior).
  The pre-existing `neovisual-editor-insert` flake remains on record (retry-once
  policy). Loop-time VERIFY runs the 4 new scenarios (+ affected neighbors:
  `telescope-open-file`, `telescope-open-file-normal`, `neovisual-explorer-open`,
  `neovisual-explorer-open-o`); the final gate is the full 33-scenario suite +
  both unit projects.

---

## Execution Log

_To be appended by the hub on each attempt: attempt #, per-scenario status,
verdict, capped evidence, and the cost line_
`delegations: N | VS boots: M | iterations: K`.