# Plan — Feature 6: Vim motions in the Solution Explorer search box (extend to `j`/`k`/`0`/`$` + confirm the block caret)

> **Lane: feature (e2e enabled).** This item adds a new capability but **REUSES the existing
> `[NeoVisual] text-motion` / `block-caret` diagnostics — NO M-M7 trigger, no new diagnostic
> format.** It is still the feature lane (e2e RED boots the VS Experimental Instance) because
> the behavior is only observable live.
>
> **Source:** `docs/progress.md` item 6 (`:416-420`) + `docs/plans/backlog-plans.md`
> "Feature 6" (`:247-256`, `:299-340`). User chose **BUILD** 2026-09-28
> (`docs/progress.md:63-70`). Every claim in the backlog sketch was re-verified against the
> current code — the sketch's line refs and type names are **stale** (see D1/D2 below).
>
> **Ground truth:** repo GREEN. Unit suites: `tests/Telescope.Tests` **172 passed**,
> `tests/NeoVisual.Tests` **168 passed** (`docs/spec.md:252,265`; `AGENTS.md:100,115`).
> Live E2E: `tools/harness/test-e2e.ps1` lists **36 scenarios, no known-RED** (all GREEN;
> `docs/spec.md:291,296`). This feature adds one new scenario, so the scenario count goes
> **36 → 37 at GREEN** (the GREEN doc-sync step updates the counts in `docs/spec.md`/`AGENTS.md`).
>
> **Research:** read `TextMotionDispatcher` (the single key→motion table — the M19 refactor
> moved `MapMotion`/`Apply` out of `TextMotionHelper`), `TextMotionHelper`
> (`MapMotion`/`TryMoveFocusedSurface`/`ApplyMotionToBox`/`ApplyCaretStyle`),
> `TextMotionNavigator` (`Down`/`Up`/`LineStart`/`LineEnd`), `SolutionExplorerController`
> (`TryMove` WPF-TextBox branch + `_actions`/`ActionKeys`), `ToolWindowControllerBase`
> (`AddTextMotionKeys`), `InputHandler` (`IsKeyOfInterest`, the tool-window routing block,
> the R10 shift gate), `FocusGuard` (`ShouldRouteToolWindowKey` shift overload),
> `WindowManager` (`IsTextInputType`/`TextInputSurfaceFocused`), `BlockCaretStyle`,
> the `explorer-open-searchbox` (`test-e2e.ps1:942-971`) and `neovisual-textinput-motions`
> (`:1095-1177`) scenarios, and the `Run_TextMotionEngine_*` / `Run_TextMotionHelper_*` /
> `Run_SolutionExplorer_ActionKeys` / `Run_FocusGuard_*` unit tests. No Trailmark graph query
> was needed — this is a small additive mapping/routing change; the only structural question
> (does the hook pre-filter see `0`/`$`?) was answered by reading `InputHandler.IsKeyOfInterest`.

## Goal

Make the Solution Explorer search box (a WPF `TextBox`) support the full normal-mode vim
motion set the text-input surfaces expose — **`j`/`k`/`0`/`$`** in addition to the existing
`h`/`l`/`w`/`b`/`e`/`a`/`A`/`I` — while the box is focused and the controller is in normal
mode. The motions are consumed (never typed into the box) and emit the existing
`[NeoVisual] text-motion key=... caret=...` diagnostic. The white **block caret** is already
applied to WPF TextBoxes in normal mode — **confirm, do not rebuild**.

## Approach

The motion math (`TextMotionNavigator.Down/Up/LineStart/LineEnd`) and the canonical
key→motion table (`TextMotionDispatcher.MapMotion(MotionKey, shift)` + `Apply`) are **already
complete**. The gap is the **WinForms `Keys` translation** used by the tool-window surface,
plus two routing gates that would otherwise swallow `0`/`$` before they reach the motion path.

### Resolved design decisions

**D1 — The `TextMotion` enum and `ApplyMotionToBox` need NO change (the backlog sketch is
stale).** The sketch (`backlog-plans.md:320-326`) says to add `TextMotion.Down/Up/LineStartHome/
LineEnd` and `ApplyMotionToBox` cases. That was true before the M19 refactor; today:
- `TextMotion` (`TextMotionDispatcher.cs:11-27`) **already has** `Down`, `Up`, `LineStart`,
  `LineEnd` (there is no `LineStartHome` — the sketch's name is wrong).
- `TextMotionDispatcher.Apply` (`:148-169`) **already handles** `Down`/`Up`/`LineStart`/`LineEnd`.
- `TextMotionHelper.ApplyMotionToBox` (`:186-245`) delegates to `TextMotionDispatcher.Apply`
  (`:193`) — it has no per-motion switch to extend.
So the **only mapping edit** is the WinForms `MapKey(Keys, bool)` switch.

**D2 — The single mapping edit: `TextMotionDispatcher.MapKey(Keys, bool)` (`:67-82`).**
It currently maps only `H/L/W/B/E/A/I`. Add four cases (the canonical `MotionKey` enum and
`MapMotion` already carry `J/K/D0/D4`):
```csharp
case Keys.J:  canonical = MotionKey.J;  break;
case Keys.K:  canonical = MotionKey.K;  break;
case Keys.D0: canonical = MotionKey.D0; break;   // 0 -> LineStart
case Keys.D4: canonical = MotionKey.D4; break;   // $ (Shift+D4) -> LineEnd; bare 4 -> null
```
`MapMotion(MotionKey.D4, shift)` already returns `LineEnd` only when `shift` is true (the
`$` drift fix, `:128-130`), so a bare `4` stays unmapped. `TextMotionHelper.MapMotion`
(`:62-65`) delegates here, so the search box and the text-input surfaces share the change.

**D3 — `0`/`$` must be added to the controller's action keys or the hook pre-filter drops
them.** `InputHandler.IsKeyOfInterest` (`:448-456`) only marks a key interesting when it is
`DefaultControllerKeys` (`{H,J,K,L,I}`) or `controller.ActionKeys.Contains(key)`. `j`/`k` are
already in `SolutionExplorerController._actions` (tree moves, `:54-55`), so they pass. `0`/`$`
are **not** in `_actions` → the pre-filter returns false → the hook passes them through and
they are **typed into the box**. Fix: add `_actions[Keys.D0] = TextMotion(Keys.D0);` and
`_actions[Keys.D4] = TextMotion(Keys.D4);` in `SolutionExplorerController`'s ctor (next to
`AddTextMotionKeys`). When the tree (not the box) is focused, `TryMove` falls to `_actions`
and `TextMotion(Keys.D0/D4)` → `TryMoveFocusedSurface` → no box → `MapMotion` is not
Left/Right → returns false, so the key falls through (no tree action, no swallow). `j`/`k`
keep their existing `TreeMove` entries — the WPF-TextBox branch in `TryMove` (`:200-211`)
delegates to `TryMoveFocusedSurface` **before** `_actions`, so the search box gets the motion
and the tree keeps the arrow move.

**D4 — `$` (Shift+D4) is blocked by the R10 shift gate; relax it for a focused WPF TextBox
that belongs to the current tool window.** `InputHandler.HandleKey` (`:328-334`) wraps the
tool-window routing in `FocusGuard.ShouldRouteToolWindowKey(..., shiftHeld: shift)`, whose
shift overload (`FocusGuard.cs:50-52`) returns false for a **non-text-input** controller when
shift is held (`!(shiftHeld && !isTextInputSurface)`). The Solution Explorer is not a
text-input type (`GeneralToolWindowController.IsTextInputType` → false), so `$` never reaches
`TryMove` and is typed into the box. Fix: exempt a **focused WPF TextBox** from the shift gate
— add a `textBoxFocused` parameter to the shift overload, **defaulted to `false`**
(`bool textBoxFocused = false`) so the existing 6-arg call in
`Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` (`tests/NeoVisual.Tests/Program.cs:1383`)
still compiles, and change the gate to
`!(shiftHeld && !isTextInputSurface && !textBoxFocused)`. `InputHandler` computes
`textBoxFocused` **only when `shift` is held** (rare; not the editor hot path) via a new
`WindowManager.IsFocusedTextBoxInCurrentToolWindow()` that resolves
`TextMotionHelper.FindFocusedTextBox()` and walks its parent chain to the current tool
window's `VSFPROPID_DocView` content (the same walk `ComputeTextInputSurfaceFocused` uses,
`WindowManager.cs:100-141`) — so the exemption is scoped to the **current tool window's own
content**, not *any* focused WPF TextBox. This preserves R10: when the **tree** is focused
`FindFocusedTextBox()` is null, and when a stale SE frame coexists with a modal rename/move
dialog's TextBox (not a descendant of the SE frame's DocView) the exemption is false, so
`Shift+O/R/M/A/G` still do not fire tree actions and `Shift+A`/`Shift+I` do not enter input
mode. The existing `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` test (passes
`textInputSurfaceFocused: false`, no text box) stays GREEN; a new test pins the exemption.

**D5 — Single-line search-box `j`/`k` are no-ops (documented, not a bug).** The search box is
a single-line `TextBox` (no `\n`). `TextMotionNavigator.Down()` (`:61-76`) returns early when
there is no next `\n`; `Up()` (`:79-102`) returns early on the first line. So `j`/`k` leave the
caret unchanged **but are still consumed** and log `text-motion key=J/K caret=<same>`. This
matches the navigator's N66/BP-62 boundary behavior and the text-input surfaces. `0`/`$` move
to the single line's start/end (`LineStart`/`LineEnd` with no `\n` → `0` / `text.Length`).

**D6 — Block caret: confirm only (no code change).** `TextMotionHelper.ApplyMotionToBox`
(`:233-237`) calls `ApplyCaretStyle(focusedBox, false)` for every non-insert motion when
`styleCaret: true` (the WPF branch passes `styleCaret: true, focusedBox`, `:113-116`), which
delegates to `BlockCaretStyle.ApplyCaretStyle` (`:29-39`) → `box.CaretBrush = BlockCaretBrush`.
So the search box already draws the white block caret in normal mode. **There is no
`block-caret active=` log for a WPF TextBox** (that diagnostic is emitted only by
`BlockCaretAdornment` for editor views), so the block caret is confirmed by code inspection +
the fact that the `text-motion` path (which applies it) ran — **not** by a new diagnostic.

### Constraints honored

- **net472:** no new BCL APIs; `Keys`/`TextMotion` are existing types.
- **UI-thread affinity:** `FindFocusedTextBox`/`ApplyCaretStyle` already run on the UI thread
  (the hook runs on the UI thread); no new VS API method is added.
- **Log-line-as-contract:** no new/changed diagnostic literal — the existing
  `[NeoVisual] text-motion key={key} caret={newCaret} len={fullLength} text='{sample}'` line is
  reused byte-for-byte.
- **Pure seam:** the mapping stays in the pure `TextMotionDispatcher` (unit-testable); the
  routing gate stays in the pure `FocusGuard`.

## Acceptance criteria (each mapped to a diagnostic + a test)

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `j` in the focused search box is consumed as a motion (not typed); single-line → caret unchanged | `[NeoVisual] text-motion key=J caret={same}` | e2e `explorer-searchbox-motions`; unit `Run_TextMotionEngine_MapMotion_DownUp` |
| AC2 | `k` in the focused search box is consumed as a motion; single-line → caret unchanged | `[NeoVisual] text-motion key=K caret={same}` | e2e `explorer-searchbox-motions`; unit `Run_TextMotionEngine_MapMotion_DownUp` |
| AC3 | `0` moves the search-box caret to the line start | `[NeoVisual] text-motion key=D0 caret=0` | e2e `explorer-searchbox-motions`; unit `Run_TextMotionEngine_MapMotion_LineStartEnd` |
| AC4 | `$` (Shift+D4) moves the search-box caret to the line end | `[NeoVisual] text-motion key=D4 caret={len}` | e2e `explorer-searchbox-motions`; unit `Run_TextMotionEngine_MapMotion_LineStartEnd` |
| AC5 | `0`/`$` reach the motion path (not dropped by the hook pre-filter) | (unit-only — `ActionKeys` contains `D0`/`D4`) | unit `Run_SolutionExplorer_ActionKeys` (extend) |
| AC6 | `$` reaches the motion path despite the R10 shift gate, without regressing `Shift+O/R/M/A/G` in the tree | (unit-only — pure `FocusGuard` truth table) | unit `Run_FocusGuard_ShiftAllowsSearchBoxTextMotion` (new) + `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` stays GREEN |
| AC7 | The block caret is applied to the search box in normal mode | (no diagnostic — code-inspection confirmation; the `text-motion` line proves the path ran) | e2e `explorer-searchbox-motions` (the `text-motion` assertions) |
| AC8 | The existing search-box flow (`i` focus → type → Esc → `o` open) is unregressed | existing `search-focus` / `toolwindow-enter-input` / `toolwindow-exit-input` / `solution-explorer open` | e2e `explorer-open-searchbox` stays GREEN |
| AC9 | The text-input surfaces' existing motions are unregressed | existing `text-motion` / `textinput-enter-input` lines | e2e `neovisual-textinput-motions` stays GREEN |

> **Pure-logic criteria (AC5, AC6) and the block-caret confirmation (AC7) have NO new
> diagnostic by design** — AC5/AC6 assert the pure `ActionKeys`/`FocusGuard` state directly;
> AC7 is a code-inspection confirmation (the WPF-TextBox block caret has no log line — the
> `text-motion` line proves the path that applies it ran). The verification-agent must NOT
> treat the missing log line as a coverage gap. Pinning `BlockCaretStyle.ApplyCaretStyle`
> directly is likely **not hermetic** (it needs an STA `TextBox`), so AC7 stays a
> code-inspection + `text-motion`-line confirmation rather than a new unit test.

## E2E test plan

**New scenario: `explorer-searchbox-motions`** (added to `tools/harness/test-e2e.ps1` via
`Register-Scenario`, modeled on `explorer-open-searchbox` at `:942-971` +
`neovisual-textinput-motions` at `:1095-1177`). It depends only on the existing
`text-motion` diagnostic; the seed is needed only for a non-empty Solution Explorer tree
(the scenario types `xyz` itself — it does not depend on any specific seeded file).

The scenario must establish **"search box focused + controller in normal mode"**. The
extension's `i` action focuses the box **and enters input mode** (motions do not run in input
mode), and `Esc` refocuses the tree (`ExitInputMode` → `ReturnFocusToTree`). The deterministic
trigger is the **native `Window.SolutionExplorerSearch` command executed via the DTE helper
`Focus-SolutionExplorerSearchBox $vs.Id`** (the committed scenario's trigger; independent of the
`Ctrl+;` keybinding), which focuses the box **without** entering input mode. Unmapped
chars (`x`/`y`/`z`) then fall through the hook and type into the box (they are not in
`ActionKeys`), giving the box text to move over.

```powershell
Register-Scenario 'explorer-searchbox-motions' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Enter-NormalContext $vs
    Assert-VsFocused $vs 'explorer search-box motions'
    Ensure-SolutionExplorerOpen $vs $logPath

    # Focus the search box natively (Window.SolutionExplorerSearch) WITHOUT entering input mode,
    # so the controller stays in normal mode and routes motions. Executed via the DTE helper
    # (deterministic, independent of the Ctrl+; keybinding) — matches the committed scenario.
    Focus-SolutionExplorerSearchBox $vs.Id
    Start-Sleep -Milliseconds 500

    # Type unmapped chars (x/y/z are not action keys) -> they fall through and land in the box.
    Send-Text 'xyz'; Start-Sleep -Milliseconds 300

    # 0 -> line start (caret 0).
    Send-Tap 0x30; Start-Sleep -Milliseconds 200
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=D0 caret=0" '0 moved the search-box caret to the line start'
    # $ (Shift+4) -> line end (caret 3).
    Send-Shift 0x34; Start-Sleep -Milliseconds 200
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=D4 caret=3" '$ moved the search-box caret to the line end'
    # j -> single-line no-op (caret stays 3), consumed not typed.
    Send-Tap $script:VkJ; Start-Sleep -Milliseconds 200
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=J caret=3" 'j was consumed as a motion (single-line no-op)'
    # k -> single-line no-op (caret stays 3), consumed not typed.
    Send-Tap $script:VkK; Start-Sleep -Milliseconds 200
    Assert-NewLogLine $logPath "$($script:PfxNeo)text-motion key=K caret=3" 'k was consumed as a motion (single-line no-op)'

    Write-Pass 'search-box vim motions j/k/0/$ moved/consumed the caret'
}
```

**Existing scenarios that must stay GREEN:** all 36, in particular
`explorer-open-searchbox` (the `i`→type→Esc→`o` flow — the new `_actions` entries and the
shift-gate change must not break it) and `neovisual-textinput-motions` (the shared
`TextMotionDispatcher` mapping change must not regress the Command Window motions), plus
`neovisual-explorer-*` (tree `j`/`k` arrow moves must still work) and `seed-leak`.

## Offline unit tests to extend

**Project: `tests/NeoVisual.Tests`** (the tool-window mapping/routing logic; `Telescope.Tests`
is untouched — the `TextMotionNavigator`/`TextMotionDispatcher` WPF path is unchanged).

- `Run_TextMotionEngine_MapMotion_DownUp` (**new**) — `TextMotionDispatcher.MapKey(Keys.J,
  false) == TextMotion.Down`; `MapKey(Keys.K, false) == TextMotion.Up` (AC1/AC2).
- `Run_TextMotionEngine_MapMotion_LineStartEnd` (**new**) — `MapKey(Keys.D0, false) ==
  TextMotion.LineStart`; `MapKey(Keys.D4, true) == TextMotion.LineEnd`; `MapKey(Keys.D4,
  false) == null` (bare `4` is not a motion) (AC3/AC4).
- `Run_TextMotionHelper_MapMotionDelegatesToDispatcher` (**extend**) — add the `J`/`K`/`D0`/
  `D4` assertions so the tool-window delegation seam is pinned.
- `Run_TextMotionHelper_ApplyMotionMovesNavigator` (**extend**) — add `Down`/`Up`/`LineStart`/
  `LineEnd` navigator assertions (single-line no-op for `Down`/`Up`; start/end for `0`/`$`).
- `Run_SolutionExplorer_ActionKeys` (**extend**) — assert `controller.ActionKeys` contains
  `Keys.D0` and `Keys.D4` (AC5).
- `Run_FocusGuard_ShiftAllowsSearchBoxTextMotion` (**new**) — with `isTextInputSurface: false`
  and `textBoxFocused: true`, `ShouldRouteToolWindowKey(..., shiftHeld: true)` returns true;
  with `textBoxFocused: false` it returns false (AC6). `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput`
  stays GREEN.

## Known-RED allowlist

**None.** All 36 e2e scenarios are GREEN and both unit suites pass (172 / 168). The
verification-agent must NOT flag any pre-existing scenario as a regression. The only expected
RED is the new `explorer-searchbox-motions` scenario + the new/extended `Run_TextMotionEngine_*`
/ `Run_TextMotionHelper_*` / `Run_SolutionExplorer_ActionKeys` / `Run_FocusGuard_*` tests
before the feature exists (the RED evidence for the Build Plan).

## Files to be touched (initial estimate)

- **Modified:** `Telescope/Overlay/Utils/TextMotionDispatcher.cs` (add `Keys.J/K/D0/D4` to the
  WinForms `MapKey` switch — D2), `MyExtension/ToolWindows/SolutionExplorerController.cs` (add
  `_actions[Keys.D0]`/`_actions[Keys.D4]` — D3), `MyExtension/ToolWindows/Utils/FocusGuard.cs`
  (add the `textBoxFocused` exemption to the shift overload — D4),
  `MyExtension/ToolWindows/WindowManager.cs` (new `IsFocusedTextBoxInCurrentToolWindow()`
  helper — D4), `MyExtension/Input/InputHandler.cs` (compute `textBoxFocused` when shift is
  held and pass it to the gate — D4), `tools/harness/test-e2e.ps1` (new
  `explorer-searchbox-motions` scenario), `tests/NeoVisual.Tests/Program.cs` (new/extended
  tests), `docs/spec.md` (search-box motion set + counts + scenario list), `AGENTS.md` (search-box
  motion set + counts + scenario list), `docs/plans/whichkey-research.md` (stale "Ground truth"
  counts), `docs/progress.md` (item 6 → DONE), `.opencode/skills/vs-extension-dev/SKILL.md`
  (test/scenario counts).
- **Not touched:** `TextMotionNavigator.cs` (already supports `Down`/`Up`/`LineStart`/
  `LineEnd`), `TextMotionHelper.cs` (`MapMotion`/`ApplyMotionToBox`/`ApplyCaretStyle` already
  delegate correctly), `BlockCaretStyle.cs` (block caret already applied), `TextInputToolWindowController.cs`
  (out of scope — the task targets the search box; the shared mapping change reaches it for
  free, but its `_actions` are not extended here).

## Open risks / uncertainty

1. **`Ctrl+;` focus trigger (main risk).** The scenario assumes `Window.SolutionExplorerSearch`
   is bound to `Ctrl+;` in the experimental instance (it is the VS default, and the extension's
   `i` action executes the same command). If the binding differs, the box is not focused and the
   `text-motion` lines never appear. **Primary fallback:** add a harness helper that executes
   `Window.SolutionExplorerSearch` via DTE (`dte.ExecuteCommand("Window.SolutionExplorerSearch")`)
   — deterministic and independent of the keybinding. (The extension's `i` action is **not** a
   viable fallback: it calls `EnterInputMode()`, and motions do not run in input mode.) The RED
   run will confirm which trigger works.
2. **Shift-gate change (D4) regression surface.** Relaxing the shift gate for a focused WPF
   TextBox must not let `Shift+O/R/M/A/G` fire tree actions. The exemption is keyed on the
   focused TextBox being a descendant of the **current tool window's** `VSFPROPID_DocView`
   content (D4), so it is null/false when the tree is focused and when a stale SE frame
   coexists with a modal dialog's TextBox; R10 is preserved. The new `Run_FocusGuard_*` test +
   `neovisual-explorer-*` e2e pin it.
3. **`TextInputSurfaceFocused` is stale for the search box.** `WindowManager` recomputes it only
   on `SEID_WindowFrame` events, not on intra-window focus changes, so it cannot be used as the
   shift-gate signal — hence the `FindFocusedTextBox()`-based exemption (D4). If a future change
   makes `TextInputSurfaceFocused` reliable for the search box, the gate could use it instead.
4. **`0`/`$` on the tree.** Adding `Keys.D0`/`Keys.D4` to `_actions` makes them "interesting"
   while the tree is focused too; `TextMotion(Keys.D0/D4)` → `TryMoveFocusedSurface` → no box →
   `MapMotion` is not Left/Right → returns false, so the key falls through (no tree action). The
   `neovisual-explorer-*` scenarios confirm no regression.

## Build Plan

> **Lane: feature (e2e enabled).** RED evidence exists (see the Verification Trace). The e2e
> scenario `explorer-searchbox-motions` and the unit tests already exist (written by
> e2e-test-builder) — the steps below make them pass; do NOT re-add them. The initial plan's
> D1–D6 decisions are binding. No new diagnostic literal is introduced (the existing
> `[NeoVisual] text-motion key=... caret=...` line is reused byte-for-byte). No type/method/file
> is renamed or removed, so no doc-ref rename-propagation step is needed.

### Phase 1 — Source changes (BP-1 … BP-5)

**BP-1 — WinForms `MapKey`: map `J`/`K`/`D0`/`D4`.**
- **Files:** `Telescope/Overlay/Utils/TextMotionDispatcher.cs`
- **Change:** In the WinForms `MapKey(Keys key, bool shift)` switch (currently lines 70–80, cases
  `H/L/W/B/E/A/I`), add four cases before `default:`:
  ```csharp
  case Keys.J: canonical = MotionKey.J; break;
  case Keys.K: canonical = MotionKey.K; break;
  case Keys.D0: canonical = MotionKey.D0; break;
  case Keys.D4: canonical = MotionKey.D4; break;
  ```
  `MapMotion(MotionKey.J/K/D0/D4, shift)` already exists (lines 122–130): J→Down, K→Up,
  D0→LineStart, D4→LineEnd only when `shift` (bare `4`→null). No other change.
- **Verify-with:** unit `Run_TextMotionEngine_MapMotion_DownUp` (J→Down, K→Up);
  `Run_TextMotionEngine_MapMotion_LineStartEnd` (D0→LineStart, D4+shift→LineEnd, D4 no-shift→null);
  `Run_TextMotionHelper_MapMotionDelegatesToDispatcher` (delegation). e2e `explorer-searchbox-motions`
  asserts `[NeoVisual] text-motion key=J caret=3`, `key=K caret=3`, `key=D0 caret=0`, `key=D4 caret=3`.
- **Fails-if:** `MapKey(Keys.J,false)` returns null → `Run_TextMotionEngine_MapMotion_DownUp`
  "Expected [Down] but got []"; `MapKey(Keys.D0,false)` null → "Expected [LineStart] but got []";
  e2e never logs `text-motion key=D0`.

**BP-2 — `SolutionExplorerController._actions`: add `D0`/`D4`.**
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** In the ctor, immediately after `AddTextMotionKeys(_actions);` (line 56), add:
  ```csharp
  _actions[Keys.D0] = TextMotion(Keys.D0);
  _actions[Keys.D4] = TextMotion(Keys.D4);
  ```
  `TextMotion(Keys)` (base, `ToolWindowControllerBase.cs:52-60`) routes through
  `TextMotionHelper.TryMoveFocusedSurface`; when the tree (not the box) is focused it returns false
  (LineStart/LineEnd are not the Left/Right arrow fallback), so the key falls through — no tree
  action. `j`/`k` keep their existing `TreeMove` entries; the WPF-TextBox branch in `TryMove`
  (lines 200–211) delegates to `TryMoveFocusedSurface` before `_actions`, so the box gets the motion.
- **Verify-with:** unit `Run_SolutionExplorer_ActionKeys` (contains `Keys.D0`/`Keys.D4`);
  `Run_ActionTable_SolutionExplorer_ActionKeysMatchTable` (exact count 16 — `ActionKeys.Count == 16`).
  e2e `explorer-searchbox-motions` (D0/D4 reach the motion path).
- **Fails-if:** `Run_SolutionExplorer_ActionKeys` "0 is an action key (search-box line start)"
  fails; `Run_ActionTable_...` "Expected [16] but got [14]"; e2e `0`/`$` are typed into the box
  (no `text-motion` line).

**BP-3 — `FocusGuard` shift overload: add `textBoxFocused` exemption.**
- **Files:** `MyExtension/ToolWindows/Utils/FocusGuard.cs`
- **Change:** Replace the 6-arg shift overload (lines 50–52) with:
  ```csharp
  public static bool ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused, bool isInputMode, bool isTextInputSurface, bool textInputSurfaceFocused, bool shiftHeld, bool textBoxFocused = false)
      => ShouldRouteToolWindowKey(isToolWindow, editorFocused, isInputMode, isTextInputSurface, textInputSurfaceFocused)
         && !(shiftHeld && !isTextInputSurface && !textBoxFocused);
  ```
  The default `textBoxFocused = false` keeps the existing 6-arg call in
  `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` compiling and GREEN.
- **Verify-with:** unit `Run_FocusGuard_ShiftAllowsSearchBoxTextMotion` (`textBoxFocused: true` →
  true; `textBoxFocused: false` → false); `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput`
  stays GREEN.
- **Fails-if:** CS1739 "does not have a parameter named 'textBoxFocused'"; the new test's first
  assert fails; `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` regresses.

**BP-4 — `WindowManager.IsFocusedTextBoxInCurrentToolWindow()` (scoped exemption, with a COM-DocView fallback).**
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`
- **Change:** Add a public method (near `ComputeTextInputSurfaceFocused`, lines 100–141). It has TWO
  paths because the SE frame's `VSFPROPID_DocView` is **not guaranteed** to be a WPF
  `FrameworkElement` — the codebase documents that some tool-window DocViews are COM objects
  (`WindowManager.cs:112-118`), and the search box may live in the frame chrome rather than the
  DocView content. The primary path walks the focused box's ancestry to the DocView content; the
  fallback (DocView is COM / not a `FrameworkElement`) scopes by the focused box's own top-level
  window so a modal dialog's TextBox is still excluded:
  ```csharp
  /// <summary>
  /// True when the focused WPF TextBox belongs to the CURRENT tool window. Primary path: the box is
  /// a descendant of the current tool window's VSFPROPID_DocView content (the same walk
  /// ComputeTextInputSurfaceFocused uses). Fallback when the DocView is a COM object (not a WPF
  /// FrameworkElement — see ComputeTextInputSurfaceFocused:110-119): scope by the focused box's own
  /// top-level window — the box must be hosted in the VS main window (Owner == null), NOT a separate
  /// modal dialog (whose Window has an Owner). Scopes the D4 shift-gate exemption to the current
  /// tool window's own search box, preserving R10. UI thread only.
  /// </summary>
  public bool IsFocusedTextBoxInCurrentToolWindow()
  {
      ThreadHelper.ThrowIfNotOnUIThread();
      if (!IsToolWindow || CurrentWindow == null)
      {
          return false;
      }
      var box = TextMotionHelper.FindFocusedTextBox();
      if (box == null)
      {
          return false;
      }
      try
      {
          CurrentWindow.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out object docViewObj);
          if (docViewObj is System.Windows.FrameworkElement frameContent)
          {
              // Primary: the focused box is a descendant of the current tool window's DocView content.
              for (var current = (System.Windows.DependencyObject)box; current != null; current = TextMotionHelper.GetParent(current))
              {
                  if (ReferenceEquals(current, frameContent))
                  {
                      return true;
                  }
              }
              return false;
          }

          // Fallback: the DocView is a COM object (not a WPF FrameworkElement), so the descendant
          // walk can never match. Scope by the focused box's own top-level window: the VS main
          // window has Owner == null; a modal dialog's Window has an Owner (the main window), so a
          // modal rename/move dialog's TextBox is NOT exempted (R10 preserved).
          var top = FindTopLevelWindow(box);
          return top != null && top.Owner == null;
      }
      catch
      {
          return false;
      }
  }

  private static System.Windows.Window? FindTopLevelWindow(System.Windows.DependencyObject child)
  {
      for (var current = child; current != null; current = TextMotionHelper.GetParent(current))
      {
          if (current is System.Windows.Window window)
          {
              return window;
          }
      }
      return null;
  }
  ```
- **Verify-with:** e2e `explorer-searchbox-motions` (`$` reaches the motion path →
  `[NeoVisual] text-motion key=D4 caret=3`); `neovisual-explorer-move-editor-focus` stays GREEN
  (stale frame + editor focus → `FindFocusedTextBox()` null → false). No unit test (needs VS COM +
  a live WPF tree).
- **Fails-if:** `$` never logs `text-motion key=D4`; **the DocView is COM / not a `FrameworkElement`
  and the fallback still returns false** (e.g. the focused box's top-level `Window` is not reachable
  via `TextMotionHelper.GetParent`, or its `Owner` is non-null) — in that case the build-agent must
  relax the fallback to the finding's literal `TextMotionHelper.FindFocusedTextBox() != null &&
  IsToolWindow` and re-run `explorer-searchbox-motions`; `Shift+O/R/M/A/G` fire tree actions in
  `neovisual-explorer-*`.
- **R10 caveat on the relaxation (do NOT skip):** the literal relaxation
  (`FindFocusedTextBox() != null && IsToolWindow`) exempts **any** focused WPF TextBox while the SE
  frame is current — including a modal rename/move dialog's TextBox — so `Shift+A`/`Shift+I` could
  enter input mode. `Shift+O/R/M/A/G` tree actions stay blocked regardless, because
  `SolutionExplorerController.TryMove`'s WPF-TextBox branch (`:200-211`) intercepts **before**
  `_actions` and delegates to `TryMoveFocusedSurface` (which returns false for O/R/M/A/G), so those
  keys fall through. If the relaxation is used, the build-agent MUST explicitly re-verify the
  `Shift+A`/`Shift+I` input-mode behavior (they must NOT enter input mode while a modal dialog's
  TextBox is focused) and record the result; prefer the scoped primary/fallback path above.

**BP-5 — `InputHandler`: compute `textBoxFocused` only when shift is held.**
- **Files:** `MyExtension/Input/InputHandler.cs`
- **Change:** In `HandleKey`, the tool-window gate call (lines 328–334), add the 7th argument:
  ```csharp
  if (!ctrl && !alt && FocusGuard.ShouldRouteToolWindowKey(
      _windowManager.IsToolWindow,
      _vsVim.IsEditorFocused,
      _windowManager.CurrentController?.IsInputMode == true,
      _windowManager.IsTextInputType,
      _windowManager.TextInputSurfaceFocused,
      shift,
      shift && _windowManager.IsFocusedTextBoxInCurrentToolWindow()))
  ```
  `shift && …` short-circuits the COM/visual-tree walk when shift is not held (the common path).
  `IsKeyOfInterest` needs no change: `D0`/`D4` are now in `ActionKeys` (BP-2), so the pre-filter
  marks them interesting.
- **Verify-with:** e2e `explorer-searchbox-motions` (`$` routed); `neovisual-explorer-move-editor-focus`
  GREEN.
- **Fails-if:** `$` is typed into the box instead of routed; `Shift+O/R/M/A/G` leak into tree actions.

### Phase 2 — Doc sync (BP-6 … BP-9)

**BP-6 — `docs/spec.md` + `docs/plans/whichkey-research.md`.**
- **Files:** `docs/spec.md`, `docs/plans/whichkey-research.md`
- **Change:** (a) `docs/spec.md` line 392–393 search-box bullet → add the motion set: "…WPF TextBox
  motions via shared `TextMotionHelper` (h/l/w/b/e/a/A/I + j/k/0/$; j/k are single-line no-ops, 0/$
  move to line start/end)."; (b) line 291 `# all 36 scenarios` → `# all 37 scenarios`; (c) line 296
  `The **36 scenarios**` → `The **37 scenarios**`; (d) line 420 `(36 scenarios;` → `(37 scenarios;`;
  (e) line 265 `**168 tests**` → `**171 tests**`; (f) line 419 `(168)` → `(171)`.
  (g) `docs/plans/whichkey-research.md:12` stale counts → `tests/NeoVisual.Tests` 168 → 171 and
  `36 scenarios` → 37 (the research doc's "Ground truth" line; keep it consistent with the GREEN
  counts). (h) **add `explorer-searchbox-motions` to the scenario NAME list at lines 297–310** (the
  list currently ends `…, seed-leak, explorer-open-navigation, explorer-open-searchbox.` — append
  `, explorer-searchbox-motions` before the period) so the list matches the new `37 scenarios` count.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS; `rg -n "36 scenarios|168 tests|\(168\)" docs/spec.md`
  returns no stale count; `rg -n "168|36 scenarios" docs/plans/whichkey-research.md` returns no stale count;
  `rg -n "explorer-searchbox-motions" docs/spec.md AGENTS.md` returns a hit in **both** files (the new
  scenario name is present in each list).
- **Fails-if:** a stale `36`/`168` remains in either file; the `37 scenarios` count is updated but the
  name list still has 36 entries (no `explorer-searchbox-motions` hit); doc-ref lint fails.

**BP-7 — `AGENTS.md`.**
- **Files:** `AGENTS.md`
- **Change:** (a) the "Solution Explorer search box" bullet → add "h/l/w/b/e/a/A/I + j/k/0/$ move
  the caret (shared `TextMotionHelper`; j/k are single-line no-ops, 0/$ move to line start/end)";
  (b) line 115 `Currently **168 tests, all passing**` → `Currently **171 tests, all passing**`;
  (c) line 131 `# all 36 scenarios` → `# all 37 scenarios`; (d) line 137 `Scenarios (36 total;` →
  `Scenarios (37 total;`. (e) **add an `explorer-searchbox-motions` bullet to the scenario list at
  lines 140–175** (e.g. after the `explorer-open-searchbox` bullet: "`explorer-searchbox-motions` —
  search box focused in normal mode: j/k/0/$ move/consume the caret via the shared `TextMotionHelper`
  (j/k single-line no-ops)") so the list matches the new `37 total` count.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS; `rg -n "36 scenarios|36 total|168 tests" AGENTS.md`
  returns no stale count; `rg -n "explorer-searchbox-motions" docs/spec.md AGENTS.md` returns a hit in
  **both** files.
- **Fails-if:** stale count remains; the `37 total` count is updated but the bullet list still has 36
  entries (no `explorer-searchbox-motions` hit).

**BP-8 — `.opencode/skills/vs-extension-dev/SKILL.md`.**
- **Files:** `.opencode/skills/vs-extension-dev/SKILL.md`
- **Change:** (a) line 14 `the 36 live E2E scenarios` → `the 37 live E2E scenarios`; (b) line 67
  search-box description → add "j/k/0/$"; (c) line 247 `(168)` → `(171)`; (d) line 249
  `(36 scenarios` → `(37 scenarios`.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS; `rg -n "36 scenarios|168" .opencode/skills/vs-extension-dev/SKILL.md`
  returns no stale count (mirrors BP-6/BP-7 — `check-doc-refs.ps1` alone does not verify the
  `168→171` / `36→37` count edits).
- **Fails-if:** stale count remains; `rg` still finds `36 scenarios` or `168` in `SKILL.md`.

**BP-9 — `docs/progress.md`.**
- **Files:** `docs/progress.md`
- **Change:** (a) mark item 6 (lines 416–420) DONE with the GREEN commit hash + the motion set;
  (b) Baseline (lines 90–95): `**168 passed**` → `**171 passed**`, `**36 scenarios**` →
  `**37 scenarios**`, add `explorer-searchbox-motions` to the scenario list; (c) update the top
  status line (line 10) to the new last item.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS; item 6 no longer in the pending queue.
- **Fails-if:** item 6 still listed as pending; stale baseline counts.

### Phase 3 — Verification gate (BP-10)

**BP-10 — Build + unit + e2e gate.**
- **Files:** none (verification only).
- **Change:** `dotnet build MyExtension.slnx`; `dotnet run --project tests/NeoVisual.Tests`
  (expect 171 passed); `dotnet run --project tests/Telescope.Tests` (expect 172 passed, unchanged);
  `& tools/harness/test-e2e.ps1 -Tests explorer-searchbox-motions,explorer-open-searchbox,neovisual-textinput-motions,neovisual-explorer-move-editor-focus`
  (call operator — the bare `pwsh … -Tests a,b,c` form is known-bad) then the full 37-scenario suite
  as the final gate.
- **Verify-with:** all unit tests pass; the four targeted e2e scenarios pass; the full suite is
  GREEN (37/37).
- **Fails-if:** any unit test fails; `explorer-searchbox-motions` fails; any of the 36 pre-existing
  scenarios regress.

## Verification Trace

| failing test/scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_TextMotionEngine_MapMotion_DownUp` | BP-1 | unit: `MapKey(Keys.J,false)==Down`, `MapKey(Keys.K,false)==Up` |
| `Run_TextMotionEngine_MapMotion_LineStartEnd` | BP-1 | unit: `MapKey(Keys.D0,false)==LineStart`, `MapKey(Keys.D4,true)==LineEnd`, `MapKey(Keys.D4,false)==null` |
| `Run_TextMotionHelper_MapMotionDelegatesToDispatcher` | BP-1 | unit: delegation returns Down/Up/LineStart/LineEnd |
| `Run_TextMotionHelper_ApplyMotionMovesNavigator` | none (GREEN pin) | unit: navigator Down/Up no-op; LineStart/LineEnd move |
| `Run_SolutionExplorer_ActionKeys` | BP-2 | unit: `ActionKeys` contains `Keys.D0`/`Keys.D4` |
| `Run_ActionTable_SolutionExplorer_ActionKeysMatchTable` | BP-2 | unit: exact count 16 |
| `Run_FocusGuard_ShiftAllowsSearchBoxTextMotion` | BP-3 | unit: `textBoxFocused:true`→true; `false`→false |
| `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` | BP-3 (stay GREEN) | unit: 6-arg call → false |
| e2e `explorer-searchbox-motions` | BP-1, BP-2, BP-3, BP-4, BP-5 | `[NeoVisual] text-motion key=D0 caret=0`; `key=D4 caret=3`; `key=J caret=3`; `key=K caret=3` |
| e2e `explorer-searchbox-motions` (COM-DocView fallback path) | BP-4 | `[NeoVisual] text-motion key=D4 caret=3` (fallback fires when `VSFPROPID_DocView` is not a `FrameworkElement`) |
| e2e `explorer-open-searchbox` (regression guard) | BP-2, BP-3, BP-5 | `solution-explorer search-focus`; `toolwindow-enter-input`; `toolwindow-exit-input`; `solution-explorer open` |
| e2e `neovisual-textinput-motions` (regression guard) | BP-1 | existing `text-motion` / `textinput-enter-input` lines |
| e2e `neovisual-explorer-move-editor-focus` (regression guard) | BP-3, BP-4, BP-5 | `stale-toolwindow sentinel active`; no tree action fires |

**Known-RED allowlist:** none. All 36 pre-existing e2e scenarios are GREEN and both unit suites
pass (172 / 168). The verification-agent must NOT flag the pre-fix RED state (the new
`explorer-searchbox-motions` scenario + the new/extended `Run_TextMotionEngine_*` /
`Run_TextMotionHelper_*` / `Run_SolutionExplorer_ActionKeys` / `Run_ActionTable_*` /
`Run_FocusGuard_*` tests) as a regression — that is the expected RED the Build Plan fixes.

## Execution Log

### Attempt 1 — GREEN (2026-10-04)
- lane feature; 16 delegations, 2 VS boots, 0 iterations; BUILD pass (BP-1…BP-9); VERIFY PASS (full 37-scenario e2e + NeoVisual 171 + Telescope 172); no deviations.
- failure-log sweep: 11 entries read, 0 fixed, 0 queued, 1 annotated.
