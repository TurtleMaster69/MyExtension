# Implementation Plan — Item: Fix the `explorer-open-searchbox` search-box focus-exit gap

> **Lane: bugfix.** Existing behaviour is broken (a known-RED e2e scenario) and the
> fix changes **NO** `[Telescope]`/`[NeoVisual]` diagnostic line and **NO**
> diagnostic-format contract — it is verified through the EXISTING contract
> (`[NeoVisual] toolwindow-exit-input` → `[NeoVisual] solution-explorer open` →
> `[NeoVisual] editor-view-opened file=...`). **M-M7 HARD TRIGGER: NOT
> triggered** — no diagnostic is added or changed; if the fix genuinely needs a
> new diagnostic, that is a RE-TRIAGE to the feature lane (do not silently add
> one).
>
> Lighter lane per the loop: no initial-plan REVIEW (2a); RED has no unit surface
> (see RED evidence); VERIFY runs the affected e2e scenarios + the affected unit
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
   (`SolutionExplorerController.cs:45-56`) sets `_isInputMode = false`, restyles
   the caret, and — **only if `TextMotionHelper.FindFocusedTextBox() != null`** —
   runs `ExecuteCommand("View.SolutionExplorer")`.
2. `View.SolutionExplorer` **activates/shows** the Solution Explorer window but
   does **not** move keyboard focus out of the search-box `TextBox` — the
   `TextBox` keeps WPF keyboard focus. (It is a window-activation command, not a
   focus-into-tree command.)
3. The physical **Escape is swallowed** by the hook
   (`InputHandler.ExitToolWindowInputMode()` returns `true` at `InputHandler.cs:361-370`
   after logging `toolwindow-exit-input`), so VS's **native** search-box Escape
   handler — which closes the box and returns focus to the tree — never runs.

Consequence: the search box still has focus, so the next `o` hits
`TryMove`'s early branch
(`SolutionExplorerController.cs:80-87`: `TryMoveFocusedTextBox` → not a motion →
`FindFocusedTextBox() != null` → `return false`), falls through to VS, and is typed
into the search box. No `solution-explorer open` line → the scenario fails at the
`o fired solution-explorer open` assertion (`tools/test-e2e.ps1:939`).

---

## Approach

Make `ExitInputMode` return real keyboard focus to the tree **without invoking
VS's search-clear**, so the pinned filtered result (`GrepProbe.cs`) survives.
Primary mechanism (approach **b** — non-destructive DTE focus; the native
`Escape` is demoted to a **contingent** supplement because VS search boxes
typically CLEAR their query on Escape, which would drop `GrepProbe.cs` and fail
A2 even if A1 passes):

1. **Capture → activate → re-select (DTE), filter-preserving.** When the
   search-box `TextBox` still has WPF keyboard focus, capture the tree's
   currently-selected `UIHierarchyItem`(s) (`seh.SelectedItems` — the
   natively-filtered result) **before any focus action**, then
   `ExecuteCommand("View.SolutionExplorer")` to activate/show the window, then
   re-`Select` each captured item. `Select` makes focus follow the selection, so
   the tree regains keyboard focus; re-selecting the SAME filtered item does
   **not** clear the active search filter, so `GrepProbe.cs` stays the selected
   result. This mirrors the `g` action (`SelectFirstSourceFile`), which already
   uses `Select` + `View.SolutionExplorer` to land tree focus — the passing
   `explorer-open-navigation` scenario is the live proof of that pattern.
2. **Only if BP-2's gated live check shows the non-destructive path does NOT move
   focus out of the search box** (empirically decided in the explicit BP-2 step —
   not deferred): add a **contingent** native-`Escape` supplement — inject
   `VK_ESCAPE` so VS's own search-box handler closes the box, then **re-`Select`
   the item captured before the injection** so `GrepProbe.cs` is the selected
   item again even if the native handler cleared the filter. Escape is *never* the
   primary (it risks clearing the filter); the capture-first / re-select-after
   ordering makes even the Escape path filter-preserving.

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
| A5 | Both unit suites stay green | — | NeoVisual 26, Telescope 56 |
| A6 | NO diagnostic added/changed (lane stays bugfix) | diff shows no new `[NeoVisual]`/`[Telescope]` literal | code review at VERIFY |

---

## Tests

### E2E (existing scenario — the regression pair)

- **`explorer-open-searchbox`** (`tools/test-e2e.ps1:911-942`) — the
  already-registered RED scenario. Its assertions ARE the acceptance contract:
  `search-focus` → `toolwindow-enter-input` → (type) → `toolwindow-exit-input` →
  `solution-explorer open` → `editor-view-opened ... GrepProbe.cs`. No scenario
  edit is required; if the builder tightens anything it must NOT add a new
  diagnostic (M-M7).
- **Regression neighbours** (A4): `explorer-open-navigation`,
  `neovisual-explorer-open`, `neovisual-explorer-open-o`,
  `neovisual-explorer-collapse`, `neovisual-explorer-rename`,
  `neovisual-explorer-add`, `neovisual-explorer-move`, `neovisual-toolwindow`.

### Offline unit tests (only if a genuine hermetic seam exists)

The defect is WPF/DTE focus state — there may be **no** meaningful unit seam. The
builder must NOT invent a trivial test. If a real pure seam emerges from the fix
(e.g. a decision helper over "search box still focused on exit"), add one test to
`tests/NeoVisual.Tests`; otherwise report "no unit surface — RED is the
pre-existing known-RED scenario".

---

## RED evidence plan

This bugfix has **no unit-test surface** (WPF keyboard focus + DTE window
activation), and the scenario is already registered and **already red** (prior
runs; documented in `docs/progress.md`): it fails at the
`o fired solution-explorer open` assertion because `o` falls through into the
search box. Therefore:

1. The builder does **NOT** boot VS for RED (bugfix lane).
2. The builder confirms the scenario file still asserts the failing contract
   (`tools/test-e2e.ps1:911-942`) and that the failure is for the RIGHT reason
   (missing `solution-explorer open` after `toolwindow-exit-input` — a focus-exit
   gap, not a harness/seed problem).
3. If (and only if) a genuine hermetic seam exists, the builder adds a unit test
   and proves unit-level RED (build/run fails for the missing symbol/behaviour).
4. The live RED→GREEN proof is the affected VERIFY run of
   `explorer-open-searchbox` (one VS boot, at VERIFY).

---

## Known-RED allowlist (for VERIFY)

- `neovisual-editor-insert` — pre-existing flake, retry-once (not in the run set).
- **None of this item's own targets are allowlisted** — `explorer-open-searchbox`
  MUST become GREEN. The neighbour explorer scenarios + both unit suites must stay
  GREEN.
- (Note: as of this item, `explorer-open-searchbox` is the ONLY remaining known-RED
  scenario — once green, the full 33-scenario suite becomes fully green and the
  next feature item's final gate can be the full suite.)

---

## Build Plan

> **Lane: bugfix.** All edits are in `MyExtension/`. **No** `[Telescope]`/`[NeoVisual]`
> diagnostic is added or changed (M-M7 NOT triggered). UI-thread only
> (`ThreadHelper.ThrowIfNotOnUIThread()` on the changed VS-API method); target `net472`
> (no `IReadOnlySet<T>`); SDK refs stay `ExcludeAssets="runtime"`.
> Executed top-to-bottom by the build-agent; the phase headers are hub spot-check points.

### Phase 1 — Primary non-destructive DTE tree focus (approach b)

**BP-1 — Rewrite `SolutionExplorerController.ExitInputMode` to return tree focus via
capture → activate → re-select (NO Escape, filter-preserving).**
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** rewrite `ExitInputMode()` (`SolutionExplorerController.cs:45-56`):
  1. `ThreadHelper.ThrowIfNotOnUIThread();` at method start (VS-API method per AGENTS.md).
  2. `_isInputMode = false; TextMotionHelper.StyleFocusedTextBox(false);` (unchanged).
  3. If `TextMotionHelper.FindFocusedTextBox() != null` (the search-box `TextBox` still has WPF
     keyboard focus): call a new private `ReturnFocusToTree()` (below). Else (box already
     unfocused): `ExecuteCommand("View.SolutionExplorer");` (existing behavior).
  4. Add `private void ReturnFocusToTree()` — start with `ThreadHelper.ThrowIfNotOnUIThread();`,
     wrap the body in `try { … } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(
     $"{Telescope.DiagnosticLog.NeoVisual}focus-tree failed: {ex.Message}"); }`, and:
     ```
     var dte = _dteFactory();
     var dte2 = dte as EnvDTE80.DTE2;
     // Capture the currently-selected tree item(s) — the natively-filtered result — BEFORE any
     // focus action, so a filter-preserving re-select can restore it.
     var captured = new System.Collections.Generic.List<EnvDTE.UIHierarchyItem>();
     if (dte2 != null)
     {
         EnvDTE.UIHierarchy seh = dte2.ToolWindows.SolutionExplorer;
         foreach (EnvDTE.UIHierarchyItem item in seh.SelectedItems) captured.Add(item);
     }
     // Activate/show the tool window (window-activation only — does NOT clear the search filter).
     ExecuteCommand("View.SolutionExplorer");
     // Focus follows the selection: re-selecting the SAME filtered item restores tree keyboard
     // focus WITHOUT clearing the active search filter, so GrepProbe.cs stays the result.
     foreach (EnvDTE.UIHierarchyItem item in captured)
     {
         item.Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);
     }
     ```
- **Do NOT touch** `FocusSearchBox()` or `TryMove`; do **not** change the shared swallow for other
  controllers. This is the same `Select` + `View.SolutionExplorer` mechanism the passing
  `g`/`explorer-open-navigation` path already proves.
- **Verify-with:** `dotnet build MyExtension/MyExtension.csproj` succeeds; the behavioural decision
  is BP-2 (which runs the live scenario).
- **Fails-if:** build error; or (from BP-2) `solution-explorer open` still absent (box still
  focused → `o` typed into it); or the filter was cleared (no `GrepProbe.cs` result); or a
  repeated `toolwindow-exit-input`.

### Phase 2 — Gated empirical decision + contingent Escape supplement

**BP-2 — [EXECUTABLE GATE — the hub spot-check] Run the live scenario and branch BP-2-only vs BP-3.**
- **Files:** none changed (`tools/test-e2e.ps1` run only).
- **Change:** run `pwsh tools/test-e2e.ps1 -Tests explorer-open-searchbox` and inspect the log
  POST-baseline. Look at the sequence after `[NeoVisual] toolwindow-exit-input`:
  - **Signal PRESENT** — the next `o` yields `[NeoVisual] solution-explorer open` AND
    `[NeoVisual] editor-view-opened file=.*[\\/]GrepProbe\.cs` → the non-destructive path works:
    do **NOT** implement BP-3; go to BP-4 / BP-5.
  - **Signal ABSENT** — no `solution-explorer open` after the exit, or no matching
    `editor-view-opened … GrepProbe.cs`, or a cleared filter → implement **BP-3**, then re-run
    this exact check (BP-3 must make the signal present before proceeding).
- **Verify-with:** the exact post-exit log chain
  `[NeoVisual] toolwindow-exit-input` → `[NeoVisual] solution-explorer open` →
  `[NeoVisual] editor-view-opened file=.*[\\/]GrepProbe\.cs`.
- **Fails-if:** the check is INCONCLUSIVE (scenario errors/throws before `toolwindow-exit-input`, or
  no baseline log) — that is a harness/seed problem, not a BP-3 trigger; do NOT implement BP-3 on
  an inconclusive signal — fix the run and re-check. This step is the hub's mid-plan spot-check.

**BP-3 — [CONTINGENT — implement ONLY if BP-2 reports the signal ABSENT] Add the native-Escape
supplement, filter-preserving via capture-first / re-select-after.**
- **Files:** `MyExtension/KeyInjection.cs`, `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:**
  1. `KeyInjection.cs`: add `public const int VK_ESCAPE = 0x1B;` in the "actions the Solution
     Explorer controller injects" group (next to `VK_RETURN = 0x0D`, `VK_F2 = 0x71`). `Press(vk)`
     already `InjectedKeyGuard.Instance.Record(vk)`s the VK before `keybd_event`, so the injected
     Escape re-enters `GlobalKeyboardHook.HookCallback` and is passed through untouched by the
     guard at `GlobalKeyboardHook.cs:107` (before `IsInteresting`) — because `_isInputMode` is
     already `false` at that point, no second `[NeoVisual] toolwindow-exit-input` can fire.
  2. `ReturnFocusToTree()` (BP-1.4): **after** the `captured` list is built (step 1 of the body)
     and **before** the re-select loop, add `KeyInjection.Press(KeyInjection.VK_ESCAPE);` so VS's
     own search-box handler closes the box and returns focus. Keep the
     `ExecuteCommand("View.SolutionExplorer")` + the re-`Select` loop AFTER the injection so
     `GrepProbe.cs` is re-selected whether or not the native handler cleared the filter (if it
     did, the re-select restores the item; the scenario only pins the opened file).
  3. If BP-2's run instead showed live hover-preview focus-steal (tree focus lost inside the
     harness's 400ms window), wrap the re-select in a `DispatcherTimer` (~100ms tick, stop after
     ~1500ms) exactly like `SelectFirstSourceFile` (`SolutionExplorerController.cs:218-243`).
- **Verify-with:** re-run `pwsh tools/test-e2e.ps1 -Tests explorer-open-searchbox` — the post-exit
  chain is `toolwindow-exit-input` → `solution-explorer open` →
  `editor-view-opened file=.*[\\/]GrepProbe\.cs`, and `Assert-NoEnterStorm` passes.
- **Fails-if:** filter cleared and the re-select fails to restore the result (no
  `editor-view-opened … GrepProbe\.cs`); wrong tree item selected; a second
  `toolwindow-exit-input`; or an Enter-storm.

### Phase 3 — Discipline + verification

**BP-4 — Confirm no diagnostic / unit impact.**
- **Files:** none changed.
- **Change:** confirm the diff adds NO new `[NeoVisual]`/`[Telescope]` literal and NO format
  change (M-M7); `tests/NeoVisual.Tests` and `tests/Telescope.Tests` sources unchanged (no
  dependency-free algorithm exists here — RED is the pre-existing known-RED scenario).
  Confirmed-sound reasoning to preserve: injection re-entrancy is safe (`InjectedKeyGuard`
  consumes at `GlobalKeyboardHook.cs:107` **before** `IsInteresting`); `_isInputMode=false`
  prevents a second `toolwindow-exit-input`; the `ThreadHelper.ThrowIfNotOnUIThread()` in
  `ExitInputMode` does not break the offline unit suite because the test never calls
  `ExitInputMode`.
- **Verify-with:** `git diff -- MyExtension tests` shows only the `KeyInjection.cs` const
  (BP-3 only) and the `SolutionExplorerController.cs` `ExitInputMode` / `ReturnFocusToTree`
  change; `dotnet run --project tests/NeoVisual.Tests` → 26/26;
  `dotnet run --project tests/Telescope.Tests` → 56/56.
- **Fails-if:** any new log literal appears in the diff; a unit test source changes or fails.

**BP-5 — Live e2e verification (the RED→GREEN proof).**
- **Files:** `tools/test-e2e.ps1` (run only — no edits).
- **Change:** none.
- **Verify-with:**
  `pwsh tools/test-e2e.ps1 -Tests explorer-open-searchbox` → exit 0; log has
  `search-focus` → `toolwindow-enter-input` → `toolwindow-exit-input` → `solution-explorer open` →
  `editor-view-opened file=.*[\\/]GrepProbe\.cs`, and `Assert-NoEnterStorm` passes.
  Regression neighbours (A4): `pwsh tools/test-e2e.ps1 -Tests explorer-open-navigation,neovisual-explorer-open,neovisual-explorer-open-o,neovisual-explorer-collapse,neovisual-explorer-rename,neovisual-explorer-add,neovisual-explorer-move,neovisual-toolwindow` → exit 0.
- **Fails-if:** scenario still RED at `test-e2e.ps1:939`; or a neighbour regresses; or an Enter-storm trips.

## Verification Trace

| Failing test/scenario (RED) | Implicated steps | Expected diagnostic / pass signal |
|---|---|---|
| e2e `explorer-open-searchbox` — assertion `o fired solution-explorer open` (`test-e2e.ps1:939`) | BP-1, BP-2 gate (BP-3 only if BP-2 says absent) | PRECONDITION (already green in RED): `[NeoVisual] toolwindow-exit-input`; PASS signal: `[NeoVisual] solution-explorer open` after the next `o` |
| e2e `explorer-open-searchbox` — assertion `o opened the filtered result (GrepProbe.cs)` (`test-e2e.ps1:940`) | BP-1, BP-2 gate (BP-3 only if BP-2 says absent) | `[NeoVisual] editor-view-opened file=.*[\\/]GrepProbe\.cs` |
| e2e `explorer-open-searchbox` — `Assert-NoEnterStorm` (`test-e2e.ps1:941`) | BP-1, BP-3 | ≤10 `solution-explorer open` lines after baseline; no repeated `toolwindow-exit-input` |
| e2e neighbours (A4): `explorer-open-navigation`, `neovisual-explorer-open`, `neovisual-explorer-open-o`, `neovisual-explorer-collapse`, `neovisual-explorer-rename`, `neovisual-explorer-add`, `neovisual-explorer-move`, `neovisual-toolwindow` | BP-1, BP-3 | existing `toolwindow-move`/`expand`/`collapse`/`select file=`/`open` lines unchanged |
| unit suites (A5) | BP-4 | `NeoVisual.Tests` 26/26, `Telescope.Tests` 56/56 |

**Known-RED allowlist:** `neovisual-editor-insert` (pre-existing flake, retry-once; not in the run
set). `explorer-open-searchbox` is this item's target and MUST go GREEN — it is **not** allowlisted.
No other scenario is allowlisted for this item.

---

## Execution Log

### Attempt 1 — PLAN (superseded)
Initial plan (Escape-injection primary, DTE fallback contingent). `delegations: 0 | VS boots: 0 | iterations: 0`.

### REVISION — plan-review round 1 (verdict REVISE → findings addressed)

Addressed all five review findings. **This revision CHANGES THE APPROACH beyond the trace table**
(Escape-injection demoted from primary to contingent; a non-destructive DTE path made primary), so
the hub must **re-run the build-plan REVIEW gate** before dispatch.

- **F1 (major, strategy):** primary strategy flipped to approach **(b)** — `ExitInputMode` now
  captures `seh.SelectedItems` → `View.SolutionExplorer` → re-`Select` (focus follows selection),
  which does **not** clear the search filter, so `GrepProbe.cs` survives. Native `Escape`
  injection (+ `VK_ESCAPE`) is demoted to the **contingent** BP-3, with capture-first /
  re-select-after ordering so it is filter-preserving even if the native handler clears.
- **F2 (major, missing executable gate):** BP-2 is now an explicit **executable gated step**
  (hub spot-check) that runs `explorer-open-searchbox`, inspects the post-`toolwindow-exit-input`
  sequence for `solution-explorer open` + `editor-view-opened … GrepProbe.cs`, and branches
  (present → skip BP-3; absent → implement BP-3 and re-run) — given a concrete Verify-with and
  Fails-if (inconclusive ≠ BP-3 trigger).
- **F3 (minor, false signal):** removed BP-3's bogus "no `select none`" claim (the new method adds
  no logging and cannot emit it); replaced with the meaningful `solution-explorer open` +
  `editor-view-opened … GrepProbe.cs` + `Assert-NoEnterStorm` signals.
- **F4 (minor, weak pattern):** trace table + A2 aligned to the scenario's actual regex
  `file=.*[\\/]GrepProbe\.cs` (separator included).
- **F5 (nit):** `toolwindow-exit-input` relabelled a PRECONDITION (already green in RED); the
  actual pass signal is `solution-explorer open` after the next `o`.

Preserved: the confirmed-sound injection re-entrancy reasoning (`InjectedKeyGuard` consumes at
`GlobalKeyboardHook.cs:107` before `IsInteresting`; `_isInputMode=false` prevents a second
`toolwindow-exit-input`; `ThrowIfNotOnUIThread` in `ExitInputMode` does not break the unit suite).
No `[NeoVisual]`/`[Telescope]` diagnostic added/changed (M-M7 not triggered).
`delegations: 0 | VS boots: 0 | iterations: 0`.
