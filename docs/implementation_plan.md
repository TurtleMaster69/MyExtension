# Implementation Plan — Item: Stop the Solution Explorer action keys leaking into the editor

> **Lane: bugfix (no-seam).** A registered live scenario
> (`neovisual-explorer-move`) passes, but the **user observed a real leak**: with the
> Solution Explorer **not** focused, pressing `m` (`o`/`r`/`a` likewise) is consumed by
> `SolutionExplorerController` and typed into the focused **editor** — in VsVim normal
> mode that re-enters the editor's own `m` motion/operator chain, producing a typed
> storm (observed `ljoljoljoljoljo` written into a seeded file; log evidence
> `log/79-neovisual-exp.log` 15:30:29.338 `[Hook] key=M` → `solution-explorer move`).
>
> The defect is a **focus misclassification** (VS window-frame state vs real WPF
> keyboard focus). The live focus *events* have no hermetic unit surface, but the fix
> extracts a pure `FocusGuard` decision helper that DOES. Per the `bugfix (no-seam)`
> sub-lane, RED is satisfied by re-confirming the named pre-existing RED/gap with **ONE
> VS boot BEFORE BUILD** plus the stated partial no-unit-test reason. **M-M7 HARD TRIGGER:
> NOT triggered** — no `[Telescope]`/`[NeoVisual]` log line or format change is added.

---

**Goal:** `SolutionExplorerController` (and the `InputHandler` routing that reaches it)
must **only** consume/handle Solution Explorer action keys (`o`, `Enter`, `r`, `m`, `a`,
`g`, hjkl arrow moves, and the search-box `i`/text motions) when the Solution Explorer
already holds focus. When an editor (or any other surface) holds keyboard focus, those
keys must **fall through to VS** and reach that surface — never be swallowed, never
injected into the tree.

---

## Root cause (verify live before BUILD)

`InputHandler.HandleKey` routes tool-window keys whenever
`_windowManager.IsToolWindow` is true (`InputHandler.cs:250`), and
`HasToolWindowActionKeys` (`:59`) keeps `m`/`o`/`r`/`a` "interesting" for the hook
pre-filter (`GlobalKeyboardHook.cs:169`). `IsToolWindow` is derived from VS's
**window-frame selection** event (`WindowManager.OnWindowFocusChanged`, `:88-115`),
which is **not** the same as WPF keyboard focus: after a document is activated the
frame state can still report the Solution Explorer tool window (or lag), so
`controller.TryMove(Keys.M)` runs `MoveSelected()` and `KeyInjection.Press` fires the
VS Move command / the key is consumed — while the editor owns the keyboard.

Symptom: with an editor focused, `m`/`o`/`r`/`a` do **not** reach the editor; instead a
tree action fires (and in the storm case the key text lands in the document).

## Approach

Gate every Solution Explorer action on **real keyboard focus**, sourced from state the
extension already tracks **event-driven** (so it cannot go stale like the cached
frame-selection flag):

1. **Editor-focus truth (`VimModeTracker.IsEditorFocused`).** `VimModeTracker` already
   subscribes to every code view's `GotAggregateFocus`/`LostAggregateFocus` (the same
   events that gate the typing/leader logic). Cache that as a volatile
   `IsEditorFocused` flag: if an editor text view currently holds keyboard focus, no
   tool window does — so `m`/`o`/`r`/`a`/hjkl must fall through, never route to the SE
   controller. This is why the guard cannot go stale: focus events arrive on the UI
   thread whenever focus moves, with no polling and no dependence on the frame-selection
   cache.
2. **Pure decision helper (`FocusGuard`).** Extract the routing truth table into a
   dependency-free class (`OverlayKeyHandler`/`HierarchyResolver` pattern) so it is
   unit-testable: `HasToolWindowActionKeys`, `ShouldRouteToolWindowKey`, `IsTyping`.
3. **Gate the routes in `InputHandler`:** the `HasToolWindowActionKeys` pre-filter
   (consumed by `GlobalKeyboardHook.IsInteresting`), the tool-window routing branch, the
   Escape/exit-input path, and `IsTyping()` all consult `FocusGuard`. When an editor is
   focused, `m` is not even "interesting", nothing reaches `controller.TryMove`, and the
   key falls through to VS — it cannot inject the Move command and cannot focus the SE
   search box.
4. **Deterministic fault-injection seam (`WindowManager`).** A test-only sentinel file
   (under `NEOVISUAL_LOG_DIR`, absent in normal runs) makes `WindowManager` report the
   SE frame as current even when it is not — the exact stale state the user hit — so the
   e2e regression pair can reproduce the leak on demand without timing.

**Diagnostics:** the change must NOT add/change a `[NeoVisual]`/`[Telescope]` log line
(M-M7). Existing `solution-explorer open/rename/move/add/select/…` lines stay as they
are; they just stop firing when an editor is focused.

---

## Acceptance criteria

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | With an **editor** focused (+ stale frame state injected), `m` (and `o`/`r`/`a`) do **not** fire any `solution-explorer` action and do **not** inject a tree command | ABSENCE of `[NeoVisual] solution-explorer move/open/rename/add` in the post-baseline window bounded by `[NeoVisual] leader-binding executed: W` | e2e `neovisual-explorer-move-editor-focus` (see tests) |
| A2 | The fall-through key does not get swallowed / does not steal focus | ABSENCE of `[NeoVisual] solution-explorer move` post-baseline **plus** presence of `leader-binding executed: W` (proves focus stayed in the editor and the next chord reached it) | e2e `neovisual-explorer-move-editor-focus` |
| A3 | With the **tree** focused, `m`/`o`/`r`/`a` still work | existing `solution-explorer move/open/rename/add` lines unchanged | `neovisual-explorer-move`/`-open`/`-rename`/`-add`/`-open-o` |
| A4 | Search-box `i`→type→Esc→`o` still works (the just-GREENed item is not regressed) | `search-focus`→`toolwindow-exit-input`→`solution-explorer open`→`editor-view-opened …GrepProbe.cs` | `explorer-open-searchbox` |
| A5 | `g` selection still works | `solution-explorer select file=…` + `editor-view-opened file=…` | `explorer-open-navigation` |
| A6 | Both unit suites stay green | — | NeoVisual 38, Telescope 56 |
| A7 | NO diagnostic added/changed (lane stays bugfix) | diff shows no new `[NeoVisual]`/`[Telescope]` literal | code review at VERIFY |

---

## Tests

### E2E (deterministic regression pair — no timeouts)

- **Positive half (unchanged):** `neovisual-explorer-move` — with the tree focused, `m`
  still fires `[NeoVisual] solution-explorer move`.
- **Negative half (NEW):** `neovisual-explorer-move-editor-focus` — self-contained:
  1. open `Program.cs` through the Telescope overlay (gated positive lines) so the editor
     holds focus, `Assert-VsFocused`;
  2. inject the stale-frame fault via the `stale-toolwindow` sentinel file (created in the
     scenario, removed in `finally`);
  3. send `m`; bound the window with the `Space+W` → `leader-binding executed: W` line
     (written only after `m` was handled, same UI thread);
  4. **absence scan** of the fixed post-baseline window for `solution-explorer move` —
     deterministic (the bound line is already on disk; `MoveSelected` logs synchronously),
     not a `-TimeoutMs` wait and not a retry loop.
- **Regression neighbours (A3/A4/A5):** `neovisual-explorer-open`,
  `neovisual-explorer-open-o`, `neovisual-explorer-rename`, `neovisual-explorer-add`,
  `explorer-open-navigation`, `explorer-open-searchbox`, `neovisual-toolwindow`,
  `neovisual-editor-insert`, **`neovisual-textinput-motions`** (the text-input
  tool-window motions — the surface the `editorFocused` precedence could break; see BP-2).

### Offline unit tests (the `FocusGuard` seam)

`FocusGuard` is a pure, dependency-free decision helper, so it gets focused tests in
`tests/NeoVisual.Tests/Program.cs` (`Run_FocusGuard_*`, mirroring the
`OverlayKeyHandler`/`HierarchyResolver` pattern): editor-focused blocks action keys and
routing; tree-focused allows them; input-mode/zero-action-keys/non-tool-window block;
and the full `IsTyping` truth table. The live WPF/VS focus *events* themselves have no
hermetic surface — those are covered by the e2e pair above.

---

## RED evidence plan (`bugfix (no-seam)`)

1. Builder boots VS **ONCE before BUILD** and runs the new
   `pwsh tools/test-e2e.ps1 -Tests neovisual-explorer-move-editor-focus` (and, if useful,
   `neovisual-explorer-open-o`) against the pre-fix build to capture the leak: with an
   **editor** focused and the stale-frame fault injected, `m` logs
   `[NeoVisual] solution-explorer move` instead of falling through — the presence of that
   line (or the missing `leader-binding executed: W` bound) is the RED. Re-confirm it is
   the focus misclassification, not a harness/seed problem.
2. State the partial no-unit reason: the live focus *events* have no hermetic surface; the
   extracted pure `FocusGuard` decision helper DOES get unit tests (BP-5).
3. Prove unit-level RED for `FocusGuard` only if the helper's behaviour is the thing that
   changed (it is new — the truth-table tests fail to compile/run before BP-2 exists).

---

## Known-RED allowlist (for VERIFY)

- `neovisual-editor-insert` — pre-existing flake (IntelliSense autocomplete), retry-once.
- `telescope-implementation` — tracked intermittent Enter-delivery issue (separate queue
  item #5.5); not part of this run set.
- `neovisual-explorer-open-o` — recorded flaky (x5). **This item targets the same
  focus/`o` path, so treat a repeat failure here carefully: it may be THIS item's
  regression, not a flake** — flag it explicitly if it fails.
- No other scenario allowlisted.

---

## Build Plan

> **Lane: bugfix (no-seam).** Changes are in `MyExtension/` plus the two test surfaces
> (`tests/NeoVisual.Tests/Program.cs`, `tools/test-e2e.ps1`) and the doc-sync set.
> **No** `[Telescope]`/`[NeoVisual]` **log** literal is added or changed (M-M7 NOT
> triggered). UI-thread only; `net472` (no `IReadOnlySet<T>`); SDK refs stay
> `ExcludeAssets="runtime"`. The fix is a **focus-truth guard**, not a live COM probe:
> the editor's keyboard focus is already tracked event-driven by `VimModeTracker`, so no
> per-key polling or new P/Invoke is added.

### Phase 1 — Core fix (production)

**BP-1 — Track editor keyboard focus in `VimModeTracker` (event-driven; cannot go stale).**
- **Files:** `MyExtension/VimModeTracker.cs`.
- **Change:** add `private ITextView? _focusedView;` + `private volatile bool _editorFocused;`
  and `public bool IsEditorFocused => _editorFocused;`. Set `_focusedView = view;`
  `_editorFocused = true;` as the FIRST statements of `OnViewGotFocus` (BEFORE buffer
  resolution, so it works with or without VsVim). In `OnViewLostFocus` set
  `_editorFocused = false` only when `ReferenceEquals(_focusedView, view)`; in
  `OnViewClosed` clear `_focusedView`/`_editorFocused` when the closed view is
  `_focusedView`. Rationale: `GotAggregateFocus`/`LostAggregateFocus` already fire on the
  UI thread for every `IWpfTextView` (content type `text`, `TextViewCreated` `:114`), the
  same path the typing gate already trusts — focus is known without polling and cannot
  go stale like `WindowManager`'s frame-selection cache.
- **Verify-with:** no hermetic unit surface (needs a live view); proven live by BP-7.
  Structural grounding: Trailmark `callees_of("OnViewGotFocus")` → `SubscribeBuffer`/
  `GetBufferForView` (`VimModeTracker.cs:139-157`); `TextViewCreated` is the MEF
  `IWpfTextViewCreationListener` entry (`:114`).
- **Fails-if:** build error; or BP-7 still sees `solution-explorer move` (means
  `IsEditorFocused` did not become true when the Telescope-opened editor got focus).

**BP-2 — Pure decision helper `FocusGuard` (predicate = `isToolWindow && !editorFocused`).**
- **Files:** `MyExtension/ToolWindows/FocusGuard.cs` (new; SDK-style project auto-includes it;
  namespace **`MyExtension`** — matching every sibling under `MyExtension/ToolWindows/` and the
  `InputHandler` call site, verified: `TextMotionHelper.cs`/`SolutionExplorerController.cs`/
  `HierarchyResolver.cs` all use `namespace MyExtension`).
- **REVIEW FIX (round 2 — critical+major resolved).** The previous draft added an `isInputMode`
  term to the routing predicate to protect text-input surfaces, but that BOTH contradicted its own
  unit expectation AND rested on an unsound premise: `VimModeTracker` is a MEF `IWpfTextViewCreationListener`
  and is **never created for a Command Window view** (verified: opening the Command Window logs
  neither `editor-view-opened` nor `vim-mode=` — `log/87-neovisual-exp.log` 16:12:03–16:12:11), so
  `IsEditorFocused` is simply `false` there and no special case is needed. Adopt the SIMPLE predicate.
- **Change:** `internal static class FocusGuard` (namespace `MyExtension`), dependency-free
  predicates:
  - `HasToolWindowActionKeys(bool isToolWindow, bool isInputMode, int actionKeyCount, bool editorFocused)`
    → `isToolWindow && !isInputMode && actionKeyCount > 0 && !editorFocused`;
  - `ShouldRouteToolWindowKey(bool isToolWindow, bool editorFocused)`
    → `isToolWindow && !editorFocused`  ← **two args only**; leaks `(true, editorFocused:true)` →
    **false**, text-input/Command Window `(true, editorFocused:false)` → **true**;
  - `IsTyping(bool isToolWindow, bool isInputMode, bool editorFocused, bool editorInTypingMode)`
    → `isInputMode ? true : (editorFocused ? editorInTypingMode : (isToolWindow ? isInputMode : editorInTypingMode))`
    (`isInputMode ? true` keeps the Space-typing gate correct for a tool window in input mode, which
    is orthogonal to routing).
- **Verify-with:** BP-5 unit tests `Run_FocusGuard_*`, which MUST include:
  - `ShouldRouteToolWindowKey(true, editorFocused:true)` → **false** (the leak is fixed);
  - `ShouldRouteToolWindowKey(true, editorFocused:false)` → **true** (tree/tool-window routing kept);
  - `IsTyping(isToolWindow:true, isInputMode:true, editorFocused:false, editorInTypingMode:false)` → **true**.
- **Fails-if:** any predicate returns the wrong truth-table value; or the routing predicate keeps the
  `isInputMode` term (it would be dead/wrong for Command-Window normal mode).

**BP-3 — Wire the guard into `InputHandler`, gating BEFORE any action.**
- **Files:** `MyExtension/InputHandler.cs`.
- **Change:**
  > **DEVIATION D4 (ACCEPTED — plan defect corrected by verify-time debug).** The first build passed
  > `_vsVim.IsEditorFocused` DIRECTLY as `FocusGuard`'s `editorFocused` argument. That is WRONG: the
  > flag goes stale `true` for shell-routed non-code text tool windows — `VimModeTracker` is never
  > created for the Command Window and the code editor's `LostAggregateFocus` does not fire on that
  > transition — so gating `ExitToolWindowInputMode`/the routing branch on it stranded Escape and broke
  > `neovisual-textinput-motions` (fail-twice regression). BP-2's round-2 premise ("IsEditorFocused is
  > simply false there") was empirically false. **Corrected contract:** the boolean passed as
  > `editorFocused` is `EditorFocusedVeto` (`InputHandler.cs:82`) =
  > `_vsVim.IsEditorFocused && _windowManager.CurrentController?.IsInputMode != true && !GeneralToolWindowController.IsTextInputType(_windowManager.Type)`.
  > A genuine text-input surface or an input-mode controller OWNS the keyboard and is never vetoed;
  > SE-style navigation windows still honour the editor flag. `FocusGuard` itself is unchanged (pure),
  > so its unit tests are unaffected.
  1. `HasToolWindowActionKeys` (`:59-67`): `FocusGuard.HasToolWindowActionKeys(_windowManager.IsToolWindow,
     c?.IsInputMode == true, c?.ActionKeys.Count ?? 0, EditorFocusedVeto)`. `GlobalKeyboardHook.IsInteresting`
     (`:169`) consumes this, so an editor-focused `m` stops being "interesting" and is never
     inspected/consumed.
  2. Tool-window branch (`:276`): `if (FocusGuard.ShouldRouteToolWindowKey(_windowManager.IsToolWindow, EditorFocusedVeto))`.
     With a focused editor (and no trusted text-input/input-mode surface) NO key reaches
     `controller.TryMove`/`EnterInputMode`, so `m` cannot open the Move command and cannot focus the
     SE search box. (Two-arg predicate — see BP-2.)
  3. `ExitToolWindowInputMode()` (`:393`): same `EditorFocusedVeto` guard — **never** the raw flag
     (the D4 defect). This is what keeps Escape reaching the Command Window.
  4. `IsTyping()` (`:413-419`): `FocusGuard.IsTyping(_windowManager.IsToolWindow,
     _windowManager.CurrentController?.IsInputMode == true, EditorFocusedVeto, _vsVim.IsInTypingMode)`.
- **Structural evidence (Trailmark):** `callers_of("proxy.unresolved:controller.TryMove")` →
  `['InputHandler.HandleKey', 'Run_SolutionExplorer_ActionKeys', 'Run_ToolWindowMode_HjklMoves']`
  — the two `Run_*` entries are offline unit-test call sites; the only PRODUCTION routing site is
  `InputHandler.HandleKey` (`InputHandler.cs:274` and `:287`), so gating it here is complete;
  `callees_of("HandleKey")` lists `IsTyping`/`ExitToolWindowInputMode`/`controller.TryMove` —
  exactly the branches this step edits. No unrelated controller is touched.
- **Verify-with:** `dotnet build MyExtension/MyExtension.csproj`; behavioural proof BP-6/BP-7.
- **Fails-if:** the tree-focused scenarios stop logging their actions (guard inverted); build error.

**BP-4 — Deterministic stale-focus fault-injection seam in `WindowManager`.**
- **Files:** `MyExtension/WindowManager.cs`.
- **Change:** rename backing fields to `_isToolWindow`/`_type` (set only in
  `OnWindowFocusChanged`) and make the public members consult a test-only sentinel:
  - `private static readonly string? TestStaleSentinelPath = BuildTestSentinelPath();` where the
    path is `Path.Combine(Environment.GetEnvironmentVariable("NEOVISUAL_LOG_DIR"), "stale-toolwindow")`
    (`null` when the env var is unset → zero cost in normal user runs);
  - `private static bool IsTestStaleInjected() => TestStaleSentinelPath != null &&
    System.IO.File.Exists(TestStaleSentinelPath);`
  - `public bool IsToolWindow => _isToolWindow || IsTestStaleInjected();`
  - `public ToolWindowType Type => IsTestStaleInjected() ? ToolWindowType.SolutionExplorer : _type;`
  - `CurrentController`/`RegisterController`/`Dispose` unchanged.
  Rationale: a file sentinel is **runtime-togglable at an exact scenario boundary** and is read by
  presence/absence only, reproducing the *stale-frame + editor-focused* state deterministically —
  no elapsed time, no retry. (A whole-run env-var override was REJECTED: it would force every
  tool-window branch on for all 34 scenarios and break editor-typing/tool-window tests.)
- **Verify-with:** BP-7's negative scenario is RED without BP-1/BP-3 (it reproduces the leak) and
  GREEN with them; in normal runs (no sentinel) `WindowManager` behaviour is unchanged.
- **REVIEW FIX (minor) — documented cost:** in a harness run `NEOVISUAL_LOG_DIR` IS set, so
  `IsToolWindow` performs one `File.Exists` per access on the UI thread while the hook considers a
  candidate key. This is a single cached-path stat (microseconds) and only inside the harness; the
  plan accepts it **explicitly** (and notes it here) rather than leaving it undocumented. If the
  builder prefers, cache the sentinel's existence and re-stat only when `_isToolWindow` itself
  changes — but the simple form is acceptable for a test-only seam.
- **Fails-if:** the sentinel has no effect (negative scenario passes on the OLD code too → not a
  regression pair).

### Phase 2 — Tests + harness

**BP-5 — Offline unit tests for `FocusGuard` (`tests/NeoVisual.Tests/Program.cs`).**
- **Change:** add `Run_FocusGuard_*` public static tests (auto-discovered via the `Run_`
  convention): editor-focused blocks action keys + routing; tree-focused allows both; input-mode
  blocks; zero action keys blocks; non-tool-window blocks; `IsTyping` truth table (editor
  insert/replace → true, editor normal → false, tool-window input → true). ~7 tests →
  **NeoVisual 31 → 38**.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests` → 38/38 (and `-- FocusGuard`).
- **Fails-if:** any truth-table assertion fails.

**BP-6 — Keep the POSITIVE half of the regression pair.**
- **Files:** none (`neovisual-explorer-move`, `tools/test-e2e.ps1:962-979`, unchanged).
- **Change:** none — it already ensures SE is focused (`Space+E`) then asserts `m` →
  `solution-explorer move`. This is the "tree case still passes" positive; it must stay GREEN.
- **Verify-with:** `neovisual-explorer-move` → `[NeoVisual] solution-explorer move`.
- **Fails-if:** it regresses (guard over-blocking, e.g. `IsEditorFocused` stuck true).

**BP-7 — NEW deterministic NEGATIVE scenario `neovisual-explorer-move-editor-focus` (`tools/test-e2e.ps1`).**
- **Files:** `tools/test-e2e.ps1` (register immediately after `neovisual-explorer-move`).
- **Change (all primitives explicit; NO timeout-based absence, NO probe scenario):**
  1. `Reset-LogBaseline $logPath`; open `Program.cs` via the existing gated overlay chain
     (`Open-Telescope` → `Send-Text 'Program'` → assert `results count=1 selected=0` +
     `preview file=.*Program\.cs` → Enter → assert `opened file: .*Program\.cs` +
     `editor-view-opened file=.*Program\.cs` → `Close-Telescope`). The editor now holds focus
     (same mechanism `neovisual-editor-insert` relies on).
  2. `Enter-NormalContext $vs`; `Assert-VsFocused $vs 'editor-focused m'`.
  3. Create the fault sentinel `$sentinel = Join-Path (Split-Path $logPath) 'stale-toolwindow'`
     (`New-Item -Force`), inside `try { … } finally { Remove-Item -Force $sentinel
     -ErrorAction SilentlyContinue }`. This makes `WindowManager` report the SE frame as current
     — the exact stale state the user hit.
  4. `Send-Tap 0x4D` (`m`); `Send-Tap Escape` (dismisses any dialog on the OLD path only); then the
     **positive bound**: `Send-Tap Space` + `Send-Tap 0x57` (`W`) and
     `Assert-NewLogLine "$($script:PfxNeo)leader-binding executed: W" 'editor kept focus; m was
     not a tree action'`. On the NEW code no Move dialog opens, so Space+W saves → the line fires;
     it is written only AFTER `m` was handled (same UI thread), deterministically bounding the window.
  5. **Absence assertion (deterministic read, not a wait):** re-read the log and throw if any line
     in `[$script:LogBaseline .. end]` matches `$($script:PfxNeo)solution-explorer move`. Because
     the bound line is already on disk and `MoveSelected` logs synchronously before any later key,
     a `move` line from `m` would already be present — its absence is a fact, not a race.
- **Why deterministic:** the fault is a file presence toggle (no timing); the bound is a real log
  line produced after the key under test; the absence is a post-hoc scan of a fixed window. No
  `-TimeoutMs` absence wait, no retry loop, no temporary scenario.
- **Verify-with:** RED against the pre-fix build (finds `solution-explorer move`); GREEN after
  BP-1/BP-3. Companion of the pair with BP-6.
- **Fails-if:** the absence scan passes on pre-fix code (seam ineffective) or the bound line never
  appears on post-fix code (guard over-blocking).

### Phase 3 — Docs + verification

**BP-8 — Doc sync + reference propagation.**
- **Files:** `docs/spec.md`, `AGENTS.md`, `.opencode/skills/vs-extension-dev/SKILL.md`,
  `docs/progress.md`.
- **Change:** scenario count **34 → 35** and add `neovisual-explorer-move-editor-focus` to the
  scenario list (spec/AGENTS/SKILL); NeoVisual unit count **31 → 38** (spec/AGENTS/SKILL); document
  the new `FocusGuard` helper, `VimModeTracker.IsEditorFocused`, and the test-only
  `stale-toolwindow` sentinel in the spec architecture/diagnostics section; queue the item in
  progress.md. No type/method referenced by these docs is renamed, so no reference breaks.
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` → `[PASS]`.
- **Fails-if:** the lint reports an unresolved symbol; a stale count remains.

**BP-9 — Discipline + full verification.**
- **Change:** confirm no `[Telescope]`/`[NeoVisual]` **log** literal was added/changed (M-M7);
  confirm no new MEF/DI registration is required (the SE controller is already registered in
  `MyExtensionPackage.cs:85`; `FocusGuard` is static; `IsEditorFocused` is a property read off the
  existing MEF-resolved `VimModeTracker`).
- **Verify-with:** `dotnet build MyExtension/MyExtension.csproj`;
  `dotnet run --project tests/NeoVisual.Tests` → 38/38;
  `dotnet run --project tests/Telescope.Tests` → 56/56 (staggered, never simultaneous);
  e2e subset `neovisual-explorer-move,neovisual-explorer-move-editor-focus,neovisual-explorer-open,
  neovisual-explorer-open-o,neovisual-explorer-rename,neovisual-explorer-add,explorer-open-navigation,
  explorer-open-searchbox,neovisual-toolwindow,neovisual-editor-insert,neovisual-textinput-motions` → exit 0; then the full suite.
- **Fails-if:** any neighbour regresses; a `solution-explorer move/open/rename/add` line appears
  while an editor is focused; a new log literal is introduced.

## Verification Trace

| failing/leaking behaviour | implicated steps | expected diagnostic / pass signal |
|---|---|---|
| editor-focused `m` fires `[NeoVisual] solution-explorer move` (the user leak) | BP-1, BP-2, BP-3, BP-4, BP-7 | ABSENCE of `[NeoVisual] solution-explorer move` in the post-baseline window bounded by `[NeoVisual] leader-binding executed: W` |
| editor-focused `o`/`r`/`a`/Enter likewise consumed as tree actions | BP-1, BP-2, BP-3 | same absence guard (no `solution-explorer open/rename/add`); key falls through |
| `m` must not focus the SE search box or inject the Move command | BP-3 | no `solution-explorer search-focus`, no `solution-explorer move`; Space+W still saves |
| tree-focused `m`/`o`/`r`/`a` must still work | BP-2, BP-3, BP-6 | existing `[NeoVisual] solution-explorer move/open/rename/add` unchanged |
| search-box chain (A4) | BP-3 | `solution-explorer search-focus` → `toolwindow-exit-input` → `solution-explorer open` → `editor-view-opened …GrepProbe.cs` |
| `g` selection (A5) | BP-3 | `solution-explorer select file=…` + `editor-view-opened file=…` |
| guard truth table (pure) | BP-2, BP-5 | `Run_FocusGuard_*` all pass (NeoVisual 38/38) |
| unit suites (A6) | BP-5, BP-9 | NeoVisual 38/38, Telescope 56/56 |
| no log literal added/changed (A7) | BP-9 | `git diff -- MyExtension` adds no `[NeoVisual]`/`[Telescope]` log literal |

**Known-RED allowlist (do NOT report as this item's regression):**
- `neovisual-editor-insert` — pre-existing flake (IntelliSense autocomplete); retry-once.
- `telescope-implementation` — tracked intermittent Enter-delivery issue (separate queue item #5.5).
- **`neovisual-explorer-open-o`** — recorded flaky (x5), but it exercises the **same focus/`o`
  path** as this item: a repeat failure here is a **REGRESSION CANDIDATE for this item, not a
  flake** — flag it explicitly.

---

## Execution Log

### Attempt 1 — GREEN (final gate PASS, independently re-verified)

Lane: `bugfix (no-seam)`. RED: the leak was that `WindowManager.IsToolWindow` is driven by VS's
`SEID_WindowFrame` selection event (not WPF keyboard focus), so `InputHandler` kept routing
`o`/`r`/`m`/`a` to `SolutionExplorerController` while an editor held focus — the key was consumed as
a tree action (and, in the user's `ljoljoljoljoljo` case, keys landed in the editor). Fix: a pure
`FocusGuard` (`MyExtension/ToolWindows/FocusGuard.cs`, namespace `MyExtension`) consuming an
event-driven `VimModeTracker.IsEditorFocused` flag, wired into `InputHandler`'s
`HasToolWindowActionKeys` / tool-window branch / `ExitToolWindowInputMode` / `IsTyping`, plus a
test-only stale-frame sentinel in `WindowManager` and a deterministic new negative scenario
`neovisual-explorer-move-editor-focus` (Gamma.cs + sentinel; absence scan bounded by the Space+W
leader line — **no timeouts, no temporary probes**).

`delegations: 9 | VS boots: 6 | iterations: 1`.

### DEVIATION ADJUDICATIONS (hub)

- **D4 → ACCEPT (recorded in BP-3).** The first build passed the raw `_vsVim.IsEditorFocused` as the
  `editorFocused` argument; that flag goes stale `true` for shell-routed non-code text tool windows
  (Command Window), stranding Escape and breaking `neovisual-textinput-motions` (fail-twice). Fix:
  `EditorFocusedVeto` = `IsEditorFocused && CurrentController?.IsInputMode != true &&
  !GeneralToolWindowController.IsTextInputType(...)`. `FocusGuard` stays pure (unit tests unaffected).
- **D-A → ACCEPT.** `neovisual-explorer-open` / `-open-o` success gate reworked from the
  new-view-only `editor-view-opened` to `solution-explorer open` AND a NEW line after a pre-key
  line-index snapshot (`Wait-NewLogLineAfter`), with the tree refocused each iteration. Independently
  verified as **strictly STRONGER, not weakened**: `solution-explorer open` is emitted only when
  `o`/Enter is actually routed (a swallowed key still fails the scenario), and the new-line filter is
  genuinely post-key (stale preview lines cannot satisfy it). Fixes the already-open-tab false negative.
- **D-B → ACCEPT.** `neovisual-explorer-move-editor-focus` opens **Gamma.cs** (not Beta.cs, which
  `neovisual-editor-insert` needs as a fresh view; not Program.cs, the startup file). Verified Gamma.cs
  is opened by no other scenario; the sentinel is created+removed; the absence scan is non-vacuous.
- **D-C → ACCEPT.** `tools/dte-command.ps1` gained a read-only `GetActiveDocument` query branch (a
  DTE property read, `ExecuteCommand`-free) and the harness gained `Focus-SolutionExplorer`
  (`View.SolutionExplorer`) + bounded POSITIVE waits. No product code, no diagnostic, no absence-timer.

No `[NeoVisual]`/`[Telescope]` log literal added or changed (M-M7 not triggered).
Final gate: full suite 33/35 (the 2 failures are the allowlisted `neovisual-editor-insert` flake and
the separately-queued `telescope-implementation` #5.5); NeoVisual 38/38; Telescope 56/56.
