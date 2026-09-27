# Implementation Plan — Item: Fix the `explorer-open-searchbox` search-box focus-exit gap

> **Lane: bugfix (no-seam).** Existing behaviour is broken (a known-RED e2e
> scenario) and the fix changes **NO** `[Telescope]`/`[NeoVisual]` diagnostic line
> and **NO** diagnostic-format contract — it is verified through the EXISTING
> contract (`[NeoVisual] toolwindow-exit-input` → `[NeoVisual] solution-explorer
> open` → `[NeoVisual] editor-view-opened file=...`). **M-M7 HARD TRIGGER: NOT
> triggered** — no diagnostic is added or changed; if the fix genuinely needs a
> new diagnostic, that is a RE-TRIAGE to the feature lane (do not silently add
> one).
>
> `bugfix (no-seam)` sub-lane: the defect is WPF/DTE keyboard-focus state with **no
> hermetic unit-test surface**, so RED is satisfied by re-confirming the named
> pre-existing known-RED scenario (`explorer-open-searchbox`) with **ONE VS boot
> BEFORE BUILD** (so the hub's RIGHT-REASON RED gate fires) plus the stated
> no-unit-test reason (see RED evidence). The hub overrode the bugfix lane's usual
> 2a skip and ran the initial-plan review gate explicitly (rounds recorded in the
> Execution Log). VERIFY runs the affected e2e scenarios + the affected unit
> project.

---

**Goal:** Make the registered known-RED scenario `explorer-open-searchbox` GREEN:
after `i` → type `GrepProbe` → `Esc`, the Solution Explorer **tree** (not the
search box) has keyboard focus, so the next `o` reaches
`SolutionExplorerController` and opens the filtered result (`GrepProbe.cs`).

---

## Root cause (verified by code reading 2026-09-27; the DEBUG step confirms live)

The search-box input path works (`i` → `FocusSearchBox()` →
`Window.SolutionExplorerSearch` + input mode; typing filters natively). The
**exit** path does not restore tree focus:

1. `MyExtension/ToolWindows/SolutionExplorerController.ExitInputMode()`
   (`SolutionExplorerController.cs:45-61`) sets `_isInputMode = false`, restyles
   the caret, and — **only if `TextMotionHelper.FindFocusedTextBox() != null`** —
   runs `ExecuteCommand("View.SolutionExplorer")`.
2. `View.SolutionExplorer` **activates/shows** the Solution Explorer window but
   does **not** move keyboard focus out of the search-box `TextBox` — the
   `TextBox` keeps WPF keyboard focus. (It is a window-activation command, not a
   focus-into-tree command.)
3. The physical **Escape is swallowed** by the hook
   (`InputHandler.ExitToolWindowInputMode()` returns `true` at `InputHandler.cs:361-374`
   after logging `toolwindow-exit-input`), so VS's **native** search-box Escape
   handler — which closes the box and returns focus to the tree — never runs.

Consequence: the search box still has focus, so the next `o` hits
`TryMove`'s early branch
(`SolutionExplorerController.cs:80-87`: `TryMoveFocusedTextBox` → not a motion →
`FindFocusedTextBox() != null` → `return false`), falls through to VS, and is typed
into the search box. No `solution-explorer open` line → the scenario fails at the
   `o fired solution-explorer open` assertion (`tools/test-e2e.ps1:999`).

---

## Approach

> **REVISED after live evidence** — the original approach (b) (capture `SelectedItems`
> → `View.SolutionExplorer` → re-select, with a contingent Escape) was falsified: a
> single Escape only clears the query and leaves the box focused, and VS's native
> filter never selects the matching tree item. See the Build Plan header + Execution
> Log REVISION. The approach below is the current one.

Make `ExitInputMode` (a) return real keyboard focus to the tree and (b) explicitly
select the **query-matched** tree item (VS does not), so the next `o` opens
`GrepProbe.cs`. Two mechanisms, both required:

1. **Capture the query, then drive focus OUT of the search box.** Read the typed query
   from the focused search box (`TextMotionHelper.FindFocusedTextBox()?.Text`) **before
   any focus action** (Escape clears it). Inject `VK_ESCAPE` (native Escape #1 clears
   the query); then a `DispatcherTimer` keeper observes the real focus state and injects
   another `VK_ESCAPE` while `FindFocusedTextBox() != null` (bounded) — the live run
   showed Escape #2 is what actually moves focus to the tree.
2. **Explicitly SELECT the query-matched node.** VS's search filter does **not** select
   the matching item (the tree selection stayed the project node `Probe`). Walk the
   `UIHierarchy` (reusing `FindFirstProjectNode` + `BuildForest`) and resolve the
   query→file path via the new pure `HierarchyResolver.FirstPathMatching` (BP-1), then
   `Select` that item and keep re-asserting it + `View.SolutionExplorer` on the keeper
   for ~1.5s (defeats VS's hover-preview focus steal). This replaces the falsified
   `SelectedItems` capture (which preserved the project node, not the filtered file).

Do **NOT** change the shared `InputHandler.ExitToolWindowInputMode` swallow for
all controllers (that would alter text-input-window behaviour); the fix is
Solution-Explorer-specific.

**Diagnostics:** UNCHANGED. `toolwindow-exit-input` is still logged by
`InputHandler`; `solution-explorer open` / `editor-view-opened` come from the
existing `o` path. No new line, no format change.

---

## Acceptance criteria

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | After `i`→type→`Esc`, the tree (not the search box) has focus, so `o` reaches the controller | PRECONDITION (already green in RED): `[NeoVisual] toolwindow-exit-input`; PASS signal: `[NeoVisual] solution-explorer open` after the next `o` | e2e `explorer-open-searchbox` (existing) |
| A2 | `o` opens the filtered result | `[NeoVisual] editor-view-opened file=.*[\\/]GrepProbe\.cs` | e2e `explorer-open-searchbox` |
| A3 | No Enter-storm / re-injection loop | ≤10 `solution-explorer open` lines post-baseline | `Assert-NoEnterStorm` (existing) |
| A4 | Neighbouring explorer scenarios non-regressed (arrow injection + `i`/`g`/`o` untouched) | existing `toolwindow-move`/`expand`/`collapse`/`select file=` lines unchanged | affected VERIFY set |
| A5 | Both unit suites stay green (+ the new pure seam) | — | NeoVisual 31, Telescope 56 |
| A6 | NO diagnostic added/changed (lane stays bugfix) | diff shows no new `[NeoVisual]`/`[Telescope]` literal | code review at VERIFY |

---

## Tests

### E2E (existing scenario — the regression pair)

- **`explorer-open-searchbox`** (`tools/test-e2e.ps1:971-1002`) — the
  already-registered RED scenario. Its assertions ARE the acceptance contract:
  `search-focus` → `toolwindow-enter-input` → (type) → `toolwindow-exit-input` →
  `solution-explorer open` → `editor-view-opened ... GrepProbe.cs`. No scenario
  edit is required; if the builder tightens anything it must NOT add a new
  diagnostic (M-M7).
- **Regression neighbours** (A4): `explorer-open-navigation`,
  `neovisual-explorer-open`, `neovisual-explorer-open-o`,
  `neovisual-explorer-collapse`, `neovisual-explorer-rename`,
  `neovisual-explorer-add`, `neovisual-explorer-move`, `neovisual-toolwindow`.

### Offline unit tests

A genuine hermetic seam **does** exist for the selection half of the fix: the
query→file-path resolution is pure (`HierarchyResolver`), so it is unit-tested in
`tests/NeoVisual.Tests` (`Run_HierarchyResolver_FirstMatch*`, BP-1). The
WPF/DTE focus-out half remains untestable offline (it is proven by the live
scenario, BP-4); do **not** invent a fake seam for it.

---

## RED evidence plan

This bugfix has **no unit-test surface** (WPF keyboard focus + DTE window
activation), and the scenario is already registered and **already red** (prior
runs; documented in `docs/progress.md`): it fails at the
`o fired solution-explorer open` assertion because `o` falls through into the
search box. Therefore:

1. The builder boots VS ONCE and runs
   `pwsh tools/test-e2e.ps1 -Tests explorer-open-searchbox`, then confirms the RED
   is for the RIGHT reason: after `[NeoVisual] toolwindow-exit-input` the next `o`
   produces **NO** `[NeoVisual] solution-explorer open` (and no
   `editor-view-opened ... GrepProbe.cs`) — i.e. the focus-exit gap, not a
   harness/seed failure. The scenario MUST reach `toolwindow-exit-input`; if it
   errors earlier, fix the run — do NOT treat that as RED.
2. The builder also confirms the scenario file still asserts the failing contract
   (`tools/test-e2e.ps1:971-1002`).
3. The genuine hermetic seam (`HierarchyResolver.FirstPathMatching`, BP-1) is proven
   at unit level: the RED test (`Run_HierarchyResolver_FirstMatch*`) fails to compile
   until the method exists, then passes — the pure half of the fix. The WPF/DTE
   focus-out half has no unit surface and is proven by the live run only.
4. The live RED→GREEN proof is the affected VERIFY run of
   `explorer-open-searchbox` (a second VS boot, at VERIFY).

---

## Known-RED allowlist (for VERIFY)

- `neovisual-editor-insert` — pre-existing flake, retry-once (not in the run set).
- **None of this item's own targets are allowlisted** — `explorer-open-searchbox`
  MUST become GREEN. The neighbour explorer scenarios + both unit suites must stay
  GREEN.
- (Note: as of this item, `explorer-open-searchbox` is the ONLY remaining known-RED
  scenario — once green, the full **34**-scenario suite becomes fully green (34/34)
  and the next feature item's final gate can be the full suite.)

---

## Build Plan

> **Lane: bugfix.** All edits are in `MyExtension/` (+ one pure unit test). **No**
> `[Telescope]`/`[NeoVisual]` diagnostic is added or changed (M-M7 NOT triggered).
> UI-thread only (`ThreadHelper.ThrowIfNotOnUIThread()` on the changed VS-API
> method); target `net472` (no `IReadOnlySet<T>`); SDK refs stay
> `ExcludeAssets="runtime"`. Executed top-to-bottom by the build-agent; the phase
> headers are hub spot-check points.
>
> **APPROACH CHANGED after live evidence (see Execution Log REVISION; plan gate 4a
> re-runs):** the RED run falsified two premises of the previous BP-1/BP-3 —
> (1) a single injected Escape does NOT close the search box (it only CLEARS the
> query; the `SearchTextBox` keeps WPF keyboard focus, so the next `o` is typed into
> it), and (2) VS's native search filter does NOT select the matching tree item (the
> tree selection stayed the **project node** `Probe`, not `GrepProbe.cs`). The fix
> now (a) captures the query and drives focus out with a focus-observing Escape loop
> and (b) explicitly SELECTs the tree node whose name matches the query (VS does not).

### Phase 1 — Pure query→node seam (unit-testable, `HierarchyResolver` pattern)

**BP-1 — Add `HierarchyResolver.FirstPathMatching`: resolve a file node by the captured search query.**
- **Files:** `MyExtension/ToolWindows/HierarchyResolver.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** add a pure static method to `HierarchyResolver` (sibling of `FirstSourceFilePath`; no
  VS/WPF dependency):
  ```csharp
  public static string? FirstPathMatching(
      System.Collections.Generic.IReadOnlyList<HierarchyNode> nodes, string query)
  {
      if (string.IsNullOrEmpty(query)) return null;
      foreach (var n in nodes)
      {
          if (n.Kind == PhysicalFileKind &&
              n.Name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0)
              return n.FilePath;                                  // name (with ext) contains the query
          if (n.Kind == PhysicalFolderKind && n.Children != null)
          {
              var hit = FirstPathMatching(n.Children, query);     // folders recurse, in order
              if (hit != null) return hit;
          }
      }
      return null;                                                 // no match / empty query -> null
  }
  ```
  Matching mirrors the VS search box (which filters by file name): a case-insensitive substring test
  on `n.Name` finds `GrepProbe.cs` for the query `GrepProbe`; first match in tree order wins
  (deterministic). Non-file/non-folder kinds are skipped (as in `FirstSourceFilePath`).
  Add tests `Run_HierarchyResolver_FirstMatch*` to `tests/NeoVisual.Tests/Program.cs`, mirroring the
  `Run_HierarchyResolver_FirstSourceFile` shape: (1) query `GrepProbe` finds `GrepProbe.cs`;
  (2) case-insensitive `grepprobe`; (3) a folder-wrapped hit recurses; (4) a non-matching query
  returns null; (5) empty query returns null.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- HierarchyResolver` → all pass;
  unit count 26 → 31 (5 new). Structural anchor: Trailmark
  `callers_of("proxy.unresolved:HierarchyResolver.FirstSourceFilePath")` →
  `["SelectFirstSourceFile", "Run_HierarchyResolver_FirstSourceFile"]` — the sibling seam is already
  exercised by the controller + the unit suite; `FirstPathMatching` deliberately mirrors that proven
  pure pattern.
- **Fails-if:** unit suite fails, or the count is unchanged (tests not discovered — names must start
  `Run_`); `net472` build error (modern BCL API such as `IReadOnlySet<T>`).

### Phase 2 — Controller rewiring (escape-driven focus-out + query-matched select)

**BP-2 — Rewrite `SolutionExplorerController.ExitInputMode` / `ReturnFocusToTree` to capture the
query, drive focus OUT of the search box, and SELECT the query-matched tree node.**
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:**
  1. `ExitInputMode()` (`SolutionExplorerController.cs:45-61`): capture the query BEFORE any focus
     action — `string query = TextMotionHelper.FindFocusedTextBox()?.Text ?? string.Empty;` — then
     (unchanged) `_isInputMode = false; TextMotionHelper.StyleFocusedTextBox(false);`. If
     `TextMotionHelper.FindFocusedTextBox() != null` call the new `ReturnFocusToTree(query)`; else
     keep the existing `ExecuteCommand("View.SolutionExplorer");`.
  2. Replace `ReturnFocusToTree()` with `private void ReturnFocusToTree(string query)`:
     `ThreadHelper.ThrowIfNotOnUIThread();` first (VS-API method, AGENTS.md), then inside a `try`:
      - **Resolve the query-matched tree item** (the evidence falsified relying on VS's native
        filtered selection): `var dte2 = _dteFactory() as EnvDTE80.DTE2;` then an early null guard
        mirroring `SelectFirstSourceFile` (`SolutionExplorerController.cs:258-263`):
        `if (dte2 == null) { ExecuteCommand("View.SolutionExplorer"); return; }` (no log) — otherwise
        an NRE is swallowed by the catch and the scenario fails at `:999` with a misleading
        "focus never left" symptom. Then
        `EnvDTE.UIHierarchy seh = dte2.ToolWindows.SolutionExplorer;` →
       `EnvDTE.UIHierarchyItem solutionNode = seh.UIHierarchyItems.Item(1);` →
       `EnvDTE.UIHierarchyItem? projectNode = FindFirstProjectNode(solutionNode);` — if non-null:
       `projectNode.UIHierarchyItems.Expanded = true;` (a collapsed node's children are empty) then
       reuse the existing `BuildForest(projectNode, forest, pathToItem)`; then
       `string? match = HierarchyResolver.FirstPathMatching(forest, query);` and
       `EnvDTE.UIHierarchyItem? target = (match != null && pathToItem.TryGetValue(match, out var t)) ? t : null;`
       (BP-1 supplies `FirstPathMatching`).
     - **Remove** the previous `seh.SelectedItems` capture entirely — the live DEBUG showed it
       captured the PROJECT node `Probe`, and the keeper then kept re-selecting that wrong node.
     - Inject the FIRST `KeyInjection.Press(KeyInjection.VK_ESCAPE);` (native Escape #1 clears the
       query; `Press` records the VK in `InjectedKeyGuard`, so the hook passes it through and
       `_isInputMode` is already `false` → no second `toolwindow-exit-input`).
     - Set the selection as early as possible: one immediate synchronous
       `target?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);` + `ExecuteCommand("View.SolutionExplorer");`.
     - Start a `System.Windows.Threading.DispatcherTimer` keeper (~100ms, ~1.5s; same shape as
       `SelectFirstSourceFile`, `SolutionExplorerController.cs:310-331`). On each tick:
       - while `TextMotionHelper.FindFocusedTextBox() != null` **and** an attempt counter `< 4`:
         inject another `KeyInjection.Press(KeyInjection.VK_ESCAPE);` and return (focus has NOT left
         yet — **Escape #2 is what actually moves focus to the tree**; the counter bounds it);
       - once `TextMotionHelper.FindFocusedTextBox() == null`: `target?.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);`
         + `ExecuteCommand("View.SolutionExplorer");` (re-assert the query-matched selection + tree
         focus);
       - stop at the ~1.5s deadline.
     - `catch (Exception ex)`: keep the existing `System.Diagnostics.Debug.WriteLine`
       `focus-tree failed:` aid (OUTSIDE the M-M7 log contract).
  3. Do NOT change `FocusSearchBox()`, `TryMove`, `EnterInputMode`, the `g`/`SelectFirstSourceFile`
     path, or the shared `InputHandler.ExitToolWindowInputMode` swallow (that would alter
     text-input-window behaviour).
- **Structural evidence (Trailmark):** `callers_of("ReturnFocusToTree")` → `["ExitInputMode"]`
  (private, single caller); `callers_of("proxy.unresolved:controller.ExitInputMode")` →
  `["ExitToolWindowInputMode"]` (sole reach path `HandleKey` → `ExitToolWindowInputMode` →
  `ExitInputMode`); `callees_of("SelectFirstSourceFile")` shows `FindFirstProjectNode` +
  `BuildForest` are the reused tree walk and `callers_of("SelectFirstSourceFile")` → `["TryMove"]`
  confirms the `g` path is untouched; `TextMotionHelper.FindFocusedTextBox` is the established
  focused-box probe (callers: `TextInputToolWindowController.TryMove`/`ApplyCaretStyle`,
  `SolutionExplorerController.TryMove`).
- **Verify-with:** `dotnet build MyExtension/MyExtension.csproj` succeeds; the behavioural proof is
  BP-4 (live scenario).
- **Fails-if:** build error; `o` still typed into the search box (no `solution-explorer open` →
  focus never left; the extra-Escape loop did not fire, or `query` was empty so `target` is null);
  `o` opens `Probe.csproj` (no `editor-view-opened … GrepProbe.cs` → `target` was null and the old
  project-node selection was used); a second `toolwindow-exit-input`.

### Phase 3 — Discipline + live verification

**BP-3 — Confirm no diagnostic / unit impact.**
- **Files:** none changed.
- **Change:** confirm the diff adds NO new `[NeoVisual]`/`[Telescope]` **log** literal and NO
  format change (M-M7). The only new test is the pure `Run_HierarchyResolver_FirstMatch*` unit test
  (no VS/WPF coupling — the `OverlayKeyHandler`/`HierarchyResolver` seam pattern). The
  `System.Diagnostics.Debug.WriteLine(
  $"{Telescope.DiagnosticLog.NeoVisual}focus-tree failed: {ex.Message}")` aid in the BP-2 catch is
  **retained, not new** (already present at `SolutionExplorerController.cs:142`). It
  is **outside** the M-M7 contract (not emitted via `NeoVisualLog`/`Log`; the harness never asserts
  it; M-M7 covers only the `[NeoVisual]`/`[Telescope]` **log** lines) and is EXPECTED in the diff
  (mirrors the sibling `SelectFirstSourceFile` catch, `SolutionExplorerController.cs:337`).
  Confirmed-sound reasoning to preserve: injection re-entrancy is safe (`InjectedKeyGuard` consumes
  at `GlobalKeyboardHook.cs` **before** `IsInteresting`); `_isInputMode=false` prevents a second
  `toolwindow-exit-input`; the `ThreadHelper.ThrowIfNotOnUIThread()` in `ExitInputMode`/
  `ReturnFocusToTree` does not break the offline unit suite because the tests never call it.
- **Verify-with:** `git diff -- MyExtension tests` shows only `HierarchyResolver.cs`
  (`FirstPathMatching`), `SolutionExplorerController.cs` (`ExitInputMode`/`ReturnFocusToTree`), and
  `tests/NeoVisual.Tests/Program.cs` (new tests); `dotnet run --project tests/NeoVisual.Tests` →
  31/31; `dotnet run --project tests/Telescope.Tests` → 56/56 (runs staggered — no simultaneous
  `obj/` lock).
- **Fails-if:** any new `[NeoVisual]`/`[Telescope]` **log** literal appears in the diff; a unit test
  source changes or fails.

**BP-4 — Live e2e verification (the RED→GREEN proof).**
- **Files:** `tools/test-e2e.ps1` (run only — no edits).
- **Change:** none.
- **Verify-with:**
  `pwsh tools/test-e2e.ps1 -Tests explorer-open-searchbox` → exit 0; log has
  `search-focus` → `toolwindow-enter-input` → `toolwindow-exit-input` → `solution-explorer open` →
  `editor-view-opened file=.*[\\/]GrepProbe\.cs`, and `Assert-NoEnterStorm` passes.
  Regression neighbours (A4): `pwsh tools/test-e2e.ps1 -Tests explorer-open-navigation,neovisual-explorer-open,neovisual-explorer-open-o,neovisual-explorer-collapse,neovisual-explorer-rename,neovisual-explorer-add,neovisual-explorer-move,neovisual-toolwindow` → exit 0.
- **Fails-if:** scenario still RED at `test-e2e.ps1:999`; or a neighbour regresses; or an Enter-storm trips.

## Verification Trace

| Failing test/scenario (RED) | Implicated steps | Expected diagnostic / pass signal |
|---|---|---|
| e2e `explorer-open-searchbox` — assertion `o fired solution-explorer open` (`test-e2e.ps1:999`) | BP-2 (query capture + escape-driven focus-out); BP-1 (`FirstPathMatching` supplies the target) | PRECONDITION (already green in RED): `[NeoVisual] toolwindow-exit-input`; PASS signal: `[NeoVisual] solution-explorer open` after the next `o` |
| e2e `explorer-open-searchbox` — assertion `o opened the filtered result (GrepProbe.cs)` (`test-e2e.ps1:1000`) | BP-1; BP-2 (explicit query-matched `Select` — VS does not select the filtered node) | `[NeoVisual] editor-view-opened file=.*[\\/]GrepProbe\.cs` |
| e2e `explorer-open-searchbox` — `Assert-NoEnterStorm` (`test-e2e.ps1:1001`) | BP-2 | ≤10 `solution-explorer open` lines after baseline; no repeated `toolwindow-exit-input` |
| e2e neighbours (A4): `explorer-open-navigation`, `neovisual-explorer-open`, `neovisual-explorer-open-o`, `neovisual-explorer-collapse`, `neovisual-explorer-rename`, `neovisual-explorer-add`, `neovisual-explorer-move`, `neovisual-toolwindow` | BP-2 | existing `toolwindow-move`/`expand`/`collapse`/`select file=`/`open` lines unchanged |
| unit suite `Run_HierarchyResolver_FirstMatch*` (BP-1) | BP-1 | `dotnet run --project tests/NeoVisual.Tests -- HierarchyResolver` passes; 26 → 31 |
| unit suites (A5) | BP-1, BP-3 | `NeoVisual.Tests` 31/31, `Telescope.Tests` 56/56 |
| acceptance A6 (no diagnostic added/changed) | BP-3 | `git diff -- MyExtension` shows no new `[NeoVisual]`/`[Telescope]` **log** literal and no format change (M-M7 not triggered) |

**Known-RED allowlist:** `neovisual-editor-insert` (pre-existing flake, retry-once; not in the run
set). `explorer-open-searchbox` is this item's target and MUST go GREEN — it is **not** allowlisted.
No other scenario is allowlisted for this item.

---

## Execution Log

### Attempt 1 — PLAN (superseded)
Initial plan (Escape-injection primary, DTE fallback contingent). `delegations: 0 | VS boots: 0 | iterations: 0`.

### REVISION — plan-review round 1 (verdict REVISE → superseded)
Flipped primary to non-destructive DTE path (approach b), demoted Escape to contingent BP-3, made BP-2 an executable gate. `delegations: 0 | VS boots: 0 | iterations: 0`.

### REVISION — plan-review round 2 (verdict REVISE → findings addressed)
Adjudicated the net472 `SelectedItems`/`Select` false positives; split the BP-2 gate into outcomes (A)/(B); synchronized the Escape ordering; kept the `Debug.WriteLine` aid outside M-M7. `delegations: 0 | VS boots: 0 | iterations: 0`.

### REVISION — plan-review round 3 (verdict REVISE → findings addressed)
`Lane: bugfix (no-seam)` + RED plan rewritten (boot VS once before BUILD); A6 row traced; count/ref fixes. `delegations: 1 | VS boots: 0 | iterations: 0`.

### REVISION — after live BUILD+DEBUG evidence (approach falsified → re-plan; plan gate 4a re-runs)

**Two prior premises FALSIFIED by the live run (`log/68-neovisual-exp.log`; DEBUG instrumentation now
removed):**
1. **A single injected Escape does NOT close the search box** — it only CLEARS the query
   (`GrepProbe` → empty); the `SearchTextBox` keeps WPF keyboard focus
   (`[DEBUG] probe+500ms tbFocused=True text='o' focusedElem=SearchTextBox`), so the following `o` is
   typed into the box. A **second** Escape is what finally moves focus to the tree.
2. **VS's native search filter does NOT select the matching tree item** — after the box is exited,
   `[DEBUG] captured names=[Probe]`: the tree selection is the **project node** `Probe`, not
   `GrepProbe.cs`. So the previous BP-1 `SelectedItems`-capture premise could not capture the filtered
   result, and its keeper then kept re-selecting the wrong (project) node; the subsequent `o` opened
   `Probe.csproj` (prior outcome (B)).

**Design consequences folded into the revised Build Plan:**
- **BP-1 (new):** add the pure `HierarchyResolver.FirstPathMatching(nodes, query)` seam (query→file
  path) + `Run_HierarchyResolver_FirstMatch*` unit tests — since VS will not select the filtered node,
  we must select it ourselves. Mirrors the proven `FirstSourceFilePath` pattern.
- **BP-2 (rewritten):** `ExitInputMode` captures the search-box query
  (`TextMotionHelper.FindFocusedTextBox()?.Text`) BEFORE any focus action; `ReturnFocusToTree(query)`
  resolves the query-matched node via `FindFirstProjectNode` + `BuildForest` +
  `HierarchyResolver.FirstPathMatching`, **removes** the falsified `SelectedItems` capture, injects
  Escape #1, then a `DispatcherTimer` keeper injects further Escapes **while
  `FindFocusedTextBox() != null`** (bounded `< 4`) — Escape #2 is what actually moves focus — and once
  unfocused re-`Select`s the query-matched node + `View.SolutionExplorer` for ~1.5s.
- **BP-3/BP-4 (renumbered; were BP-4/BP-5):** the discipline check (31/31 NeoVisual, 56/56 Telescope,
  no new log literal) and the live RED→GREEN proof (unchanged pass signals).
- The BP-2 executable outcome gate and the contingent BP-3 Escape supplement are **retired** — the
  live evidence already decided both branches (focus-out needs extra Escapes; selection needs an
  explicit query-matched `Select`), so the fix is now a single deterministic path.

Structural evidence (Trailmark, re-queried for the revised plan): `callers_of("ReturnFocusToTree")` →
`["ExitInputMode"]` (private, single caller);
`callers_of("proxy.unresolved:controller.ExitInputMode")` → `["ExitToolWindowInputMode"]` (sole reach
path `HandleKey` → `ExitToolWindowInputMode` → `ExitInputMode`);
`callers_of("proxy.unresolved:HierarchyResolver.FirstSourceFilePath")` →
`["SelectFirstSourceFile", "Run_HierarchyResolver_FirstSourceFile"]` (the sibling seam BP-1 mirrors);
`callers_of("SelectFirstSourceFile")` → `["TryMove"]` (the `g` path is untouched);
`callees_of("SelectFirstSourceFile")` confirms `FindFirstProjectNode` + `BuildForest` are the reused
tree walk. No `[NeoVisual]`/`[Telescope]` diagnostic added/changed (M-M7 not triggered).
`delegations: 0 | VS boots: 1 | iterations: 1`.

### REVISION — post-BUILD scope widening + DEVIATION adjudications (hub record)

Three changes landed OUTSIDE the stored BP-1..BP-4 while closing this item, each adjudicated:

- **DEVIATION D1 — `SelectFirstSourceFile` (`g`) emits `[NeoVisual] editor-view-opened file=<path>`
  directly (new emission SITE of the existing literal/format); `ItemOperations.OpenFile` →
  `Documents.Item(path).Activate()` when the document already exists. → ACCEPT.**
  Falsified premise: the plan/comment claimed "activating an open document raises `TextViewCreated`".
  It does NOT — so on the full-suite path (the `g` walk expands `Models/`, selecting the ALREADY-OPEN
  `IShape.cs`) `editor-view-opened` never fired and `explorer-open-navigation` fail-twice'd. Emitting
  the existing literal for the file `g` actually opened is truthful, keeps the format unchanged
  (M-M7 not triggered — no new/changed literal), and is order-deterministic. New BP-2a.
- **DEVIATION D2 — `explorer-open-navigation`'s assertion re-scoped (gate correction, NOT weakening).
  → ACCEPT.** The old line `editor-view-opened file=.*\.cs` labelled "`o` opened the selected source
  file" was in fact satisfied by the `g`-path emission (before `o` fired), so it did **not** evidence
  `o` — a pre-existing mislabelled assertion the fix exposed. BP-2a now asserts `editor-view-opened`
  right after `g` with its true meaning, and the `o` assertion is bounded by `Assert-NoEnterStorm`.
  No assertion was deleted to make a failure pass; the pass condition is unchanged in strength and
  now correctly attributed.
- **DEVIATION D3 — seed-leak guard rework + teardown save (user-requested). → ACCEPT.** W22's
  bootstrap-SHA snapshot + `$AllowLeak` list was replaced by an expected-result TREE
  (`log/seed-expected/`, byte-compared at end-of-run; no ignorelist; `obj/`+`bin/` excluded), and
  `neovascular-editor-insert` refreshes `Beta.cs`'s expected copy in a `finally` (atomic with the
  observed save) — so a genuine leak is never masked by a flaky scenario. Teardown now runs
  `File.SaveAll` (`Save-AllDocuments`) on EVERY kill path, so leak evidence survives and the next run
  avoids the "did not close properly" prompt. New BP-5.

`delegations: 4 | VS boots: 4 | iterations: 1`.
