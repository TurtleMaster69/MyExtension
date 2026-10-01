# Backlog plans — F5, F8, F9, F13, F14, F43 (planned 2026-09-28, NOT executed)

> These are the remaining Architecture review backlog items, **planned + researched**
> without running e2e tests (this machine cannot boot the VS Experimental Instance).
> Each plan documents the lane, the research (with file:line evidence), the approach,
> the tests, and the e2e gates. **Execute on a VS-capable machine** — the e2e gates
> boot the Experimental Instance. The unit-only parts (where a hermetic seam exists)
> can be executed on any machine.

---

## F5 — VimModeTracker stale buffer + latched failure

**Lane: bugfix (no-seam).** VS-coupled (ITextView / IVimBuffer / MEF) — no hermetic
unit-test surface. RED is satisfied by a pre-existing known-RED scenario re-confirmed
with ONE VS boot BEFORE BUILD + a stated no-test reason.

### Research (MyExtension/Vim/VimModeTracker.cs)

1. **Focus loss never unsubscribes** — `OnViewLostFocus` (:178-190) clears
   `_editorFocused` + `_cachedTyping` but does NOT call `UnsubscribeBuffer` or null
   `_currentBuffer`/`_currentTextBuffer`. So after focus leaves a code editor, the old
   buffer's `SwitchedMode` subscription remains live.
2. **Stale buffer's SwitchedMode trusted** — `OnSwitchedMode` (:338-352) only checks
   `ReferenceEquals(sender, _currentTextBuffer)` — NOT whether the view is focused. A
   non-focused buffer's mode change overwrites `_cachedTyping` (e.g. sets it true →
   the leader key types a literal space instead of starting a sequence).
3. **`_resolved` latches on first failure** — `GetVim()` (:481-512) sets
   `_resolved = true` BEFORE the resolution attempt. If VsVim isn't loaded yet, the
   resolution fails and `_vim` stays null forever — the tracker never re-resolves when
   VsVim loads later.
4. **Reflection-failure routing** — already done (the consolidation's X1 converted
   `Debug.WriteLine` → `NeoVisualLog.Debug` at :255, :265, :285, :332, :392, :421,
   :441, :462, :503, :507). No work needed here.

### Approach

1. `OnViewLostFocus`: after clearing `_editorFocused`/`_cachedTyping`, call
   `UnsubscribeBuffer(_currentTextBuffer)` and null `_currentBuffer`/`_currentTextBuffer`
   (mirror `OnBufferClosed`'s cleanup).
2. `OnSwitchedMode`: additionally require the view to be focused — only trust the
   buffer when `_editorFocused` is true AND `ReferenceEquals(sender, _currentTextBuffer)`.
3. `GetVim()`: don't latch on failure — only set `_resolved = true` on success; on
   failure leave `_resolved = false` so a later call re-attempts resolution.

### Tests / e2e gates

- **Unit:** none (no hermetic surface — the tracker is MEF/ITextView/IVimBuffer-bound).
- **e2e (existing scenarios, re-confirm GREEN):** `neovisual-editor-insert` (insert-mode
  typing must reach the editor — the leader key must NOT fire in insert mode),
  `neovisual-leader` (Space+W/Space+E must fire leader bindings in normal mode). The
  stale-buffer bug manifests as: focus an insert-mode editor → move to a tool window →
  Space types a literal space instead of starting a leader. The e2e gate is these two
  scenarios staying green after the fix.
- **Blocker:** needs ONE VS boot to re-confirm the regression case before BUILD.

---

## F8 — fzf spawned per keystroke with sync UI-thread pipe write

**Lane: bugfix (perf).** FzfFilter is pure (hermetic unit surface exists); the debounce
can be extracted into a pure debouncer.

### Research

1. `FzfFilter.FilterAsync` (Telescope/Filter/FzfFilter.cs:77-138) spawns a short-lived fzf
   process per query. The **sync** `p.StandardInput.BaseStream.Write(...)` (:113) blocks
   the calling thread while the candidate bytes are written to the pipe.
2. `TelescopeOverlay.FilterAndUpdateAsync` (:374) calls `_fzf.FilterAsync(...)` from the
   UI thread (via `RefreshResults` ← `OnPromptTextChanged`, :326-350) — so the sync pipe
   write blocks the UI thread on every keystroke.
3. `RefreshResults` (:337-350) fires on every prompt change with NO debounce — fzf is
   spawned per keystroke.

### Approach

1. `FzfFilter.FilterAsync`: background the process spawn + pipe write — wrap the
   process I/O in `Task.Run(...)` so the sync pipe write never runs on the caller's
   thread. The method stays `async Task<IReadOnlyList<string>>` (same signature).
2. `TelescopeOverlay.RefreshResults`: debounce — wait ~200ms after the last prompt
   change before calling `FilterAndUpdateAsync` (cancel the pending debounce on each
   keystroke). Extract the debounce into a pure `Debouncer` (a class that delays an
   action until the input settles, with a `Cancel()`/`Trigger()` API) so it is
   unit-testable.

### Tests / e2e gates

- **Unit (Telescope.Tests):** `Run_Debouncer_*` — a pure debouncer test (trigger →
  action fires after the delay; re-trigger resets the delay; cancel suppresses the
  action). RED: `Debouncer` doesn't exist → compile error.
- **e2e (existing scenarios, re-confirm GREEN):** `telescope-search` (typing filters
  candidates — the debounce must not break the filtered-result assertions),
  `telescope-grep` (query-driven). The debounce changes the timing of result updates,
  so these scenarios must stay green.
- **Blocker:** the debounce timing change needs e2e confirmation (VS boot).

---

## F9 — fzf missing/crash is silent; `IsAvailable()` unused

**Lane: feature (M-M7 HARD TRIGGER).** The fix ADDS a `[Telescope]` diagnostic
(`fzf unavailable — filtering disabled`), so it is the feature lane — full pipeline
(initial-plan REVIEW + e2e RED booting VS).

### Research

1. `FzfFilter.IsAvailable()` (Telescope/Filter/FzfFilter.cs:51-70) exists but is ONLY used by
   the unit test (`Run_FzfFilter_FilterMatchesPrefix`). It is NOT called in production.
2. `TelescopeController` (:25-28) constructs `new FzfFilter()` but never checks
   `IsAvailable()`. When fzf is missing, `FilterAsync`'s catch (:133-137) silently
   returns the full candidate list — no user-visible signal.

### Approach

1. `TelescopeController` ctor: call `_fzf.IsAvailable()` once; if false, log
   `[Telescope] fzf unavailable — filtering disabled` (via `TelescopeLog.Log`).
2. The overlay continues to work (unfiltered list) — the diagnostic is the only change.

### Tests / e2e gates

- **Unit:** none new (the diagnostic is emitted from the VS-coupled controller ctor).
- **e2e (NEW scenario):** a scenario that asserts the `[Telescope] fzf unavailable —
  filtering disabled` line when fzf is absent (or the absence of the line when fzf is
  present). This is a NEW diagnostic contract → feature lane → e2e RED booting VS.
- **Blocker:** needs VS boot for the e2e RED + the new scenario.

---

## F13 — FileFinder re-implements the shared DTE walker

**Lane: bugfix (no-seam dedup).** Behavior-preserving (the walkers are near-verbatim
identical) — no observable behavior change, no hermetic RED test. RED is satisfied by a
pre-existing known-RED scenario re-confirmed with ONE VS boot + a stated no-test reason.

### Research

1. `FileFinder.CollectProjectFiles`/`CollectItems` (Telescope/Finders/FileFinder.cs:103-177) are
   near-verbatim identical to `ProjectFiles.Enumerate` (Telescope/Finders/ProjectFiles.cs:15-103):
   same solution-folder recursion, same `FullPath` property read, same `File.Exists` +
   `seen` dedup.
2. `FileFinder.GatherHits` (:47-75) re-implements the walk instead of calling
   `ProjectFiles.Enumerate(dte)`.

### Approach

1. `FileFinder.GatherHits` (DTE branch): replace the inline walk with
   `foreach (string path in ProjectFiles.Enumerate(dte)) { hits.Add(new FileHit(path, 0)); }`.
2. Delete the now-unused `CollectProjectFiles`/`CollectItems` from FileFinder.
3. The hermetic test seam (`_testCandidateSource`) is unchanged.

### Tests / e2e gates

- **Unit:** none new (behavior-preserving dedup — no observable change to test).
- **e2e (existing scenarios, re-confirm GREEN):** `telescope-open`, `telescope-open-file`
  (FileFinder scenarios — the candidate list must be identical after the dedup).
- **Blocker:** needs ONE VS boot to re-confirm the regression case before BUILD.

---

## F14 — Ctrl+N/P hijacked in every editor

**Lane: bugfix (no-seam-ish).** The fix extracts a pure `ShouldNavigate` decision for
unit-testing; the gate itself is VS-coupled (ICompletionBroker via MEF).

### Research

1. `PopupNavigation.TryNavigate` (MyExtension/Input/PopupNavigation.cs:37-51) injects
   `VK_DOWN`/`VK_UP` whenever `!_windowManager.IsToolWindow` — i.e. in ANY editor, even
   with no popup open. So Ctrl+N/P in a plain editor injects arrows (moving the caret),
   hijacking VS's "New File"/"New Project" shortcuts.
2. `InputHandler.HandleKey` (:274-277) routes Ctrl+N/P → `_popupNav.TryNavigate(...)`.
3. **No `ICompletionBroker` exists in the current code** (grep: 0 matches) — the gate
   was removed. The comment at InputHandler.cs:267-269 explains the bare-Ctrl key-down is
   deliberately NOT intercepted (it corrupted Ctrl+chords) — but a READ of
   `IsCompletionActive()` is not an interception and is safe.

### Approach

1. Add a pure `public static bool ShouldNavigate(bool isToolWindow, bool isPopupActive)`
   to `PopupNavigation` → `return !isToolWindow && isPopupActive;`.
2. `PopupNavigation.TryNavigate`: use `ShouldNavigate(_windowManager.IsToolWindow,
   _isPopupActive())`; inject only when it returns true. `_isPopupActive` is an injected
   `Func<bool>` (default: resolve `ICompletionBroker` via MEF and return
   `IsCompletionActive()`).
3. `InputHandler` (or the PopupNavigation ctor) supplies the `_isPopupActive` predicate
   from the MEF `ICompletionBroker`.

### Tests / e2e gates

- **Unit (NeoVisual.Tests):** `Run_PopupNavigation_ShouldNavigate_*` — the pure
  decision: `(false,false)→false`, `(false,true)→true`, `(true,true)→false`,
  `(true,false)→false`. RED: `ShouldNavigate` doesn't exist → compile error.
- **e2e (existing scenarios, re-confirm GREEN):** the editor scenarios
  (`neovisual-editor-insert`, `neovisual-leader`) must stay green (Ctrl+N/P no longer
  hijacked in a plain editor). Optionally a NEW scenario asserting Ctrl+N in a plain
  editor opens VS's "New File" (not an arrow injection) — if added, it is a new
  scenario (not a diagnostic change), still bugfix lane.
- **Blocker:** the gate behavior needs e2e confirmation (VS boot).

---

## F43 — fzf unit test silently passes when fzf absent

**Lane: bugfix (test fix).** The fix is in the test itself.

### Research

1. `Run_FzfFilter_FilterMatchesPrefix` (tests/Telescope.Tests/Program.cs:282-298)
   silently `return`s when `!fzf.IsAvailable()` (:286-290) — the test "passes" without
   exercising anything when fzf is absent.
2. fzf IS present on this machine (WinGet install), so the test currently exercises the
   filter — but on a machine without fzf it silently passes (a false green).

### Approach

1. Change the guard to FAIL when fzf is absent: `Assert.True(fzf.IsAvailable(), "fzf must be on PATH for this test")` (or count it as SKIP if the runner gains a SKIP mechanism).
2. The test then genuinely fails on a machine without fzf instead of silently passing.

### Tests / e2e gates

- **Unit:** the test itself is the fix. RED proof requires fzf ABSENT (not demonstrable
  on this machine — fzf present). On a machine without fzf, the current test silently
  passes (the bug) and the fixed test fails (RED) → then passes once fzf is installed.
- **e2e:** none.
- **Blocker:** RED proof needs a machine without fzf (or a fzf-path override to a
  non-existent executable).

---

## Execution order on a VS-capable machine

1. **F13** (dedup, smallest, one VS boot) → **F5** (no-seam, one VS boot) → **F14**
   (no-seam-ish, unit seam + one VS boot) → **F8** (perf, unit seam + e2e) → **F43**
   (test fix, needs fzf-absent) → **F9** (feature lane, new diagnostic + e2e RED).
2. Each item: write its plan into `docs/implementation_plan.md`, run the RED gate
   (unit-level where a seam exists; one VS boot for the no-seam items), BUILD, VERIFY,
   commit.

---

# User-requested features 6-9 — FEATURE-TRIAGE research (2026-09-28)

> Each feature needs the FEATURE-TRIAGE gate (LOOP step 1f): research LazyVim (the
> reference), check native VS, then the USER decides build vs extend/reuse vs skip.
> Research below; the user's decision is recorded in `docs/progress.md` Decisions.

## Feature 6 — Vim motions in the Solution Explorer search box

- **LazyVim reference:** no direct analog (VS-specific surface). The reference is the
  general vim-motion-in-text-input behavior — already implemented for the text-input
  tool windows (`TextMotionHelper` + block caret).
- **Native VS:** the search box is a WPF TextBox. The extension ALREADY routes
  h/l/w/b/e/a/A/I via `TextMotionHelper` while a WPF TextBox is focused
  (`neovisual-explorer-searchbox` passes). Extend the motion set (j/k/0/$, block
  caret) to match the text-input tool-window surfaces.
- **Options:** build (extend the existing `TextMotionHelper` routing — small) / skip.

## Feature 7 — Ctrl+H/J/K/L navigation INSIDE the Telescope overlay — 3 panes, modal

- **LazyVim reference:** Telescope (the Neovim plugin) has a prompt + results + preview
  layout; the extension's current List↔Preview switch (`_focusTarget`, Ctrl+H/L) is the
  analog. LazyVim's `<C-h/j/k/l>` are window navigation (not overlay panes).
- **Native VS:** no native analog (the overlay is custom).
- **Options:** build (extend `_focusTarget` to a 3-way Input/List/Preview switch with
  Ctrl+J/K — new diagnostics likely, e.g. `[Telescope] focus target=Input|List|Preview`)
  / skip.

## Feature 8 — `Leader+C+A` — code-actions picker

- **LazyVim reference:** `<leader>ca` = `vim.lsp.buf.code_action` — a picker of the LSP
  code actions at the caret (n/x modes).
- **Native VS:** `View.QuickActions` (Ctrl+.) — the native quick-actions menu (currently
  bound to `C,A` → `command:View.QuickActions`). The feature would REBIND `C,A` to a
  Telescope-style picker of code actions, differentiating selection vs symbol placement,
  first option = the fix-it action.
- **Options:** build (a Telescope-style code-actions picker — new diagnostics likely) /
  extend-reuse (keep the native QuickActions menu, just rebind) / skip.

## Feature 9 — Solution Explorer normal-mode r/a/m — vim-mode overlay

- **LazyVim reference:** `<leader>cr` = rename symbol (inline LSP rename, insert-mode,
  renames all references); `<leader>cR` = rename/move FILE (LSP file-rename flow that
  fixes references via `workspace/willRenameFiles` → WorkspaceEdit).
- **Native VS:** rename (F2), Move dialog, Add Item dialog — standard WinForms/WPF
  controls with NO vim motions / insert-normal mode. The base keys ALREADY EXIST
  (`SolutionExplorerController` r/a/m; `neovisual-explorer-rename/-add/-move` pass).
- **Options:** build (a Telescope-like controlled overlay for rename/move/add with
  insert/normal mode + vim motions — new diagnostics likely) / extend-reuse (keep the
  native dialogs) / skip.

---

# Feature plans 6-9 (user chose BUILD 2026-09-28 — PLANNED, not executed)

> Each is a feature-lane item. M-M7 applies to any that add/change a
> `[Telescope]`/`[NeoVisual]` diagnostic. All need e2e RED booting VS (a VS-capable
> machine). The unit-only seams below can be built on any machine.

## Feature 6 — Vim motions in the Solution Explorer search box (extend to j/k/0/$ + block caret)

**Lane: feature** (new capability; reuses the existing `text-motion`/`block-caret`
diagnostics — NO M-M7 trigger, no new diagnostic format).

### Research

1. `TextMotionHelper.MapMotion` (MyExtension/ToolWindows/TextMotionHelper.cs:70-88)
   maps H/L/W/B/E/A/I only — **no J/K/0/$**.
2. `TextMotionNavigator` (Telescope/Overlay/TextMotionNavigator.cs) **ALREADY supports**
   `Down` (j, :87), `Up` (k, :105), `LineStartHome` (0, :155), `LineEnd` ($, :158) —
   the navigator is complete; only the mapping + the `TextMotion` enum are missing.
3. The **block caret is ALREADY applied** to WPF TextBoxes (the search box) via
   `ApplyCaretStyle` (TextMotionHelper.cs:216-226) — `box.CaretBrush = isInputMode ?
   null : BlockCaretBrush`. No work needed for the caret.
4. The search-box routing: `SolutionExplorerController.TryMove`
   (SolutionExplorerController.cs:159-172) delegates to
   `TextMotionHelper.TryMoveFocusedSurface` when a WPF TextBox is focused.

### Approach

1. Add `TextMotion.Down`, `TextMotion.Up`, `TextMotion.LineStartHome`,
   `TextMotion.LineEnd` to the `TextMotion` enum (TextMotionHelper.cs:17-27).
2. Add `MapMotion` cases: `Keys.J → Down`, `Keys.K → Up`, `Keys.D0 → LineStartHome`,
   `Keys.Oem4 → LineEnd` (TextMotionHelper.cs:70-88).
3. Add the corresponding cases to `ApplyMotionToBox`'s switch
   (TextMotionHelper.cs:166-177): `Down`/`Up`/`LineStartHome`/`LineEnd` → the
   navigator methods. These are non-insert motions (log `text-motion key=... caret=...`).
4. j/k on a single-line search box: `Down` moves to end, `Up` moves to start (the
   navigator's single-line behavior) — acceptable; document it.

### Tests / e2e gates

- **Unit (NeoVisual.Tests):** `Run_MapMotion_*` for J/K/0/$ — pure mapping assertions
  (`MapMotion(Keys.J, false) == TextMotion.Down`, etc.). RED: `TextMotion.Down` doesn't
  exist → compile error.
- **e2e (existing, re-confirm GREEN):** `neovisual-explorer-searchbox` (i focuses the
  search box, query filters, o opens — the new motions must not break it).
- **e2e (NEW scenario, optional):** a scenario asserting 0/$ motions in the search box
  (e.g. type a query, `0` moves the caret to start, `$` to end, via the
  `text-motion key=... caret=...` lines).
- **Blocker:** e2e confirmation (VS boot).

## Feature 7 — Ctrl+H/J/K/L navigation INSIDE the Telescope overlay (3 panes, modal)

**Lane: feature (M-M7 HARD TRIGGER).** The fix CHANGES the `focus target=` diagnostic
contract (`List|Preview` → `Input|List|Preview`), so it is the feature lane — full
pipeline (initial-plan REVIEW + e2e RED booting VS).

### Research

1. `_focusTarget` (Telescope/Overlay/TelescopeOverlay.cs:76): `FocusTarget.List` default.
2. Ctrl+H/Ctrl+L (TelescopeOverlay.cs:512-519) switch List↔Preview; the diagnostic
   `focus target={_focusTarget}` is emitted at :516 and :528.
3. `FocusTargetUi()` (TelescopeOverlay.cs:649-660): Preview → `_previewBox.Focus()`;
   else → `FocusPrompt()`. The current "List" target CONFLATES the prompt input and the
   results list (the prompt box holds focus while the list is the active surface).
4. `telescope-preview` asserts `focus target=List|Preview` — the contract change MUST
   update that scenario.

### Approach

1. Add `FocusTarget.Input` (the prompt box) — separate the prompt input from the
   results list. The enum becomes `Input | List | Preview`.
2. Ctrl+H/J/K/L cycle the 3 panes (exact mapping TBD — e.g. Ctrl+H/L move left/right,
   Ctrl+J/K move down/up through Input→List→Preview).
3. `FocusTargetUi()`: Input → `FocusPrompt()`; List → focus the results list
   (`_resultsList.Focus()`); Preview → `_previewBox.Focus()`.
4. Diagnostic: `focus target=Input|List|Preview` (contract change — M-M7).
5. Extract the pane-cycling decision into a pure `FocusTargetCycler` (given the current
   target + the key, return the next target) so it is unit-testable.

### Tests / e2e gates

- **Unit (Telescope.Tests):** `Run_FocusTargetCycler_*` — pure cycle logic (H/L/J/K from
  each target). RED: `FocusTargetCycler` doesn't exist → compile error.
- **e2e (existing, MUST UPDATE):** `telescope-preview` — its `focus target=List|Preview`
  assertions must be updated to the new `Input|List|Preview` contract.
- **e2e (NEW scenario):** a scenario asserting the 3-way pane switch (Ctrl+H/L/J/K move
  focus between Input/List/Preview, staying modal — no VS window below).
- **Blocker:** e2e RED booting VS + the `telescope-preview` scenario update.

## Feature 8 — `Leader+C+A` — code-actions picker

**Lane: feature** (new capability + new diagnostics likely — M-M7 applies to any new
`[Telescope]` diagnostic).

### Research

1. **No code-actions API exists in the codebase** (grep: 0 matches for
   QuickActions/CodeAction/SuggestedAction).
2. The existing finders (CodeIssuesFinder, ReferencesFinder, ImplementationFinder) use
   Roslyn via MEF-resolved `VisualStudioWorkspace` + `ThreadHelper.JoinableTaskFactory.Run`
   — the established pattern to follow.
3. VS code actions: `Microsoft.VisualStudio.Language.Intellisense.ISuggestedActionsSource`
   (MEF, per content type) — the lightbulb. `GetSuggestedActions(categorySet, span, ct)`
   returns `SuggestedActionSet`s; each `ISuggestedAction` has `DisplayText` + `Invoke(ct)`.
4. The keybinding: `C,A` → `command:View.QuickActions` is CURRENTLY bound
   (default-keybindings.json) — will be REBOUND to the picker.

### Approach

1. New `CodeActionsFinder` (Telescope, `Name="CodeActions"`): resolve the caret
   document/position via the active editor view (the ReferencesFinder pattern), gather
   the code actions at the caret via `ISuggestedActionsSource.GetSuggestedActions`
   (MEF-resolved; fall back to Roslyn `CodeActionService` if needed), and list them.
2. **Selection vs symbol placement:** detect whether a multi-line selection is active
   (DTE `TextSelection`); the action set differs — gather accordingly.
3. **Fix-it-first ordering:** when the caret is on a symbol marked as a warning/error,
   the FIRST option is the action that fixes it (the fix-it action surfaces first).
4. Enter applies the selected action (`ISuggestedAction.Invoke`).
5. Rebind `C,A` to the picker (default-keybindings.json + InputHandler.ResolveAction).
6. Diagnostics: `[Telescope] code-actions gathered count=...` (gather summary) and
   `[Telescope] opened code-action: ...` (applied action).

### Tests / e2e gates

- **Unit (Telescope.Tests):** the pure ordering logic (fix-it-first, selection-vs-symbol
  set) extracted into a dependency-free class — `Run_CodeActionOrdering_*`. RED: the
  class doesn't exist → compile error.
- **e2e (NEW scenario):** a scenario that opens the code-actions picker on a symbol with
  a warning/error, asserts the fix-it action is first, and applies it.
- **Blocker:** e2e RED booting VS (the Roslyn/VS code-actions gather only exists in a
  live instance).

## Feature 9 — Solution Explorer normal-mode r/a/m — vim-mode overlay

**Lane: feature** (new capability + new diagnostics likely — M-M7 applies to any new
`[NeoVisual]` diagnostic).

### Research

1. `SolutionExplorerController` r/a/m (SolutionExplorerController.cs:381-398): `r` → F2
   injected (native rename), `m` → `SolutionExplorer.Move` command, `a` →
   `SolutionExplorer.AddItem` command. The native dialogs (F2 edit box, Move dialog, Add
   Item dialog) are standard WinForms/WPF controls with NO vim motions / insert-normal
   mode.
2. The Telescope overlay pattern (TelescopeOverlay + OverlayKeyHandler + TextMotionHelper)
   is the model for a controlled surface WE own.

### Approach

1. Build a Telescope-like controlled overlay for rename/move/add:
   - **Rename:** an input box with insert/normal mode + vim motions (the prompt-box
     pattern — `TextMotionHelper`/`TextMotionNavigator`), Enter commits the new name
     (DTE `ProjectItem.Name = ...` or the native rename), Escape cancels.
   - **Move:** a picker of target folders/projects (the tree-forest pattern from
     `HierarchyResolver`), Enter moves the item.
   - **Add:** a picker of item templates + target project, Enter adds.
2. New diagnostics: `[NeoVisual] solution-explorer rename-overlay open/commit/cancel`,
   `... move-overlay ...`, `... add-overlay ...` (exact contract TBD at plan time).
3. The base keys r/a/m stay bound; they now open the overlay instead of the native
   dialog.

### Tests / e2e gates

- **Unit (NeoVisual.Tests):** the rename-input state machine (insert/normal mode +
  motions over the input box) extracted into a pure class — `Run_RenameOverlay_*`.
  RED: the class doesn't exist → compile error.
- **e2e (existing, re-confirm GREEN):** `neovisual-explorer-rename/-add/-move` — these
  assert the CURRENT native-dialog behavior; they MUST be updated to the overlay
  behavior (or replaced by new overlay scenarios).
- **e2e (NEW scenarios):** rename-overlay (open, type, Enter commits), move-overlay,
  add-overlay.
- **Blocker:** e2e RED booting VS + the existing `neovisual-explorer-rename/-add/-move`
  scenario updates.

---

## Execution order on a VS-capable machine (features)

1. **Feature 6** (smallest — extend MapMotion + enum; unit seam + e2e) → **Feature 7**
   (3-pane switch; unit seam + e2e + `telescope-preview` update) → **Feature 8**
   (code-actions picker; unit seam + e2e) → **Feature 9** (r/a/m overlay; unit seam +
   e2e + `neovisual-explorer-rename/-add/-move` updates).
2. Each item: write its plan into `docs/implementation_plan.md`, run the RED gate
   (unit-level where a seam exists; e2e RED booting VS for the feature lane), BUILD,
   VERIFY, commit.

---

# LazyVim gap-analysis pass (2026-09-28 — review/triage, NOT auto-implement)

> Scheduled in `docs/progress.md` after the tree-select + searchbox items went GREEN
> (both done). Compares the implemented features + built-in bindings against LazyVim's
> keymaps. Every gap below goes through the SAME feature-triage gate (ask the user:
> build vs extend/reuse native VS vs skip) before implementation.

## Implemented today (baseline)

- **Window nav:** Ctrl+H/J/K/L (Cardinal navigation).
- **Leader bindings:** Space+B,D close file; Space+W save; Space+Q exit; Space+E
  toggle Solution Explorer; Space+F,F GoToFile; Space+F,T telescope; Space+F,D issues;
  Space+F,G grep; Space+F,B NavigateTo; Space+F,R references; Space+F,I implementation;
  Space+C,W Command Window; Space+C,A QuickActions (→ code-actions picker, feature 8);
  Space+C,R rename; Space+/ find; Space+C,F format; Space+S,S NavigateTo; Space+G,G
  GitChanges; Space+G,B branches; Space+G,C commit; Space+T,T terminal; Space+B,B build;
  Space+B,R debug.
- **Telescope finders:** files, issues, grep, references, implementation.
- **Tool-window controllers:** Solution Explorer (hjkl + o/r/m/a/g + search box),
  text-input windows (h/l/w/b/e/a/A/I + block caret).

## Gaps vs LazyVim (prioritized by relevance to this extension's scope)

### High relevance (extends existing surfaces)

1. **Window management** — LazyVim `<leader>-` split below, `<leader>|` split right,
   `<leader>wd` delete window, `<leader>wm` toggle zoom, `<C-Up/Down/Left/Right>` resize.
   We have window NAVIGATION only. Native VS: `Window.Split`, `Window.CloseToolWindow`,
   `View.Zoom` — extend/reuse the native commands. **Triage needed.**
2. **Buffer switching** — LazyVim `<S-h>`/`<S-l>` prev/next buffer, `<leader>bb` switch
   buffer, `<leader>bd` delete buffer, `<leader>bo` delete others. We have only
   Space+B,D (File.Close). Native VS: `Window.NextDocumentWindow` /
   `Window.PreviousDocumentWindow` / `File.Close`. **Triage needed.**
3. **Diagnostics navigation** — LazyVim `]d`/`[d` next/prev diagnostic, `]e`/`[e`
   next/prev error, `]w`/`[w` next/prev warning. We have the issues FINDER (Space+F D)
   but no quick next/prev. Native VS: `Edit.NextError` / `Edit.PreviousError` (Error
   List navigation). **Triage needed.**
4. **Recent files finder** — LazyVim `<leader>fr` recent. We have no recent-files
   finder. Native VS: `File.RecentFiles` / `Window.NavigateTo` (already bound to
   Space+F,B). **Triage needed.**
5. **LSP symbols finder** — LazyVim `<leader>ss` symbols, `<leader>sS` workspace
   symbols. We have no symbols finder. Native VS: `Edit.NavigateTo` (already bound to
   Space+S,S). **Triage needed.**
6. **Goto definition / type / declaration** — LazyVim `gd`/`gy`/`gD`. We have
   references (Space+F,R) + implementation (Space+F,I) finders but no goto-definition
   binding. Native VS: `Edit.GoToDefinition`, `Edit.GoToImplementation`. **Triage
   needed.**
7. **Organize imports** — LazyVim `<leader>co`. Native VS: `Edit.RemoveAndSort` /
   `Refactor.RemoveAndSort`. **Triage needed.**

### Medium relevance (new surfaces)

8. **Quickfix finder** — LazyVim `<leader>sq`/`<leader>xQ`. Native VS: `View.ErrorList`
   / `View.Output`. **Triage needed.**
9. **Search/replace** — LazyVim `<leader>sr` (grug-far). Native VS: `Edit.ReplaceInFiles`
   (Ctrl+Shift+H). **Triage needed.**
10. **Hover / signature help** — LazyVim `K`/`gK`. Native VS: `Edit.QuickInfo` /
    `Edit.ParameterInfo`. **Triage needed.**
11. **Git status/diff/blame/log** — LazyVim `<leader>gs`/`gd`/`gb`/`gl`. We have
    GitChanges/branches/commit. Native VS: `View.GitChanges` (bound), `Team.Git.*`.
    **Triage needed.**

### Low relevance (out of scope for this extension)

12. Surround (`gsa/gsd/gsr`), yank history (`<leader>p`), marks/registers/jumps/undotree
    finders, help/keymaps finders, toggle-option bindings (`<leader>uf/us/uw/...`),
    codelens, terminal (already bound Space+T,T). These are Neovim-ecosystem features
    with weak VS analogs — likely SKIP unless the user wants them.

## Next step

Present the High + Medium gaps (1-11) to the user via the `question` tool (build /
extend-reuse native VS / skip per gap), record the decisions in `docs/progress.md`
Decisions, and add the chosen ones to the pending queue.

## Triage decisions (2026-09-28 — user answered via the `question` tool)

- **Gap 1 (window mgmt):** EXTEND/REUSE native — leader bindings for split below/right,
  delete window, toggle zoom via `Window.Split` / `Window.CloseToolWindow` /
  `View.Zoom`. Small feature.
- **Gap 2 (buffer switch):** SKIP — the user already has Shift+H/J/K/L buffer switching
  working in VsVim; no implementation needed.
- **Gap 3 (diag nav):** EXTEND/REUSE native — next/prev error bindings via
  `Edit.NextError` / `Edit.PreviousError`. Small feature.
- **Gap 4 (recent files):** BUILD — a Telescope-style recent-files finder.
- **Gap 5 (symbols finder):** BUILD — a Telescope-style LSP symbols finder
  (document/workspace).
- **Gap 6 (goto def):** BUILD — a goto-definition finder + wire VsVim's `gd`/`gr`/`gi`
  to trigger the Telescope finders (references finder for `gr`, implementation finder
  for `gi`, a new goto-definition finder for `gd`).
- **Gap 7 (org imports):** SKIP.
- **Gaps 8-11 (quickfix, search/replace, hover/signature, git status/diff/blame/log):**
  NOT YET TRIAGED — ask the user in a later pass.

---

# Telescope `fzf` finder — FEATURE-TRIAGE research (2026-09-28)

> The last finder in the roadmap (progress.md item 5, DEFERRED 2026-09-19, scope TBD).
> The overlay ALREADY uses fzf internally as its filter engine (`FzfFilter`), so the
> "fzf finder" is a finder that uses fzf's FUZZY matching as its SEARCH engine — not a
> new filter mechanism.

## Research

1. **LazyVim reference:** LazyVim's finders are `<leader>ff` find-files (file NAMES,
   Telescope `find_files`) and `<leader>fg` grep (file CONTENTS, Telescope `live_grep`
   via ripgrep). The extension's analogs: `FileFinder` (Space+F,T — file names, filtered
   by fzf) and `GrepFinder` (Space+F,G — file contents, LITERAL substring scan).
2. **The gap:** the grep finder is a literal-substring scan (`GrepFinder`). fzf's
   `--filter` mode does FUZZY matching. A finder that searches file CONTENTS with fzf's
   fuzzy matching (a "fuzzy grep") is the natural 4th finder — it is what the roadmap's
   "fzf finder" most plausibly means.
3. **Native VS:** `Edit.FindInFiles` (Ctrl+Shift+F) is literal; `Edit.NavigateTo` is
   symbol navigation. There is NO native fuzzy-content-search finder — this is a BUILD.
4. **Preview pane:** the other finders (grep/issues/references/implementation) all have
   preview panes (jump to the hit line). The fzf finder would too.

## Scope options (present to the user)

- **A. Fuzzy content finder (recommended):** a `FzfFinder` that scans the solution's
  project files for lines matching the typed query via fzf's fuzzy matching (per-query
  re-gather like the grep finder, `IsQueryDriven`), previews the hit line, Enter opens
  the file at the line. Diagnostics: `[Telescope] fzf hits=...` / `opened fzf: file=...
  line=...`. Feature lane (new diagnostics).
- **B. File-name finder:** a finder that lists the solution's files and filters by fzf
  fuzzy matching — REDUNDANT with the existing `FileFinder` (which already filters by
  fzf). Likely SKIP.
- **C. Buffers finder:** a finder listing open buffers — the user already handles buffer
  switching via VsVim Shift+H/J/K/L (gap 2 triage). Likely SKIP.

## Next step

Present the scope options to the user via the `question` tool; record the decision in
`docs/progress.md` Decisions; if BUILD, add it to the pending queue + write the plan.

## Decision (2026-09-28 — user answered via the `question` tool)

**BUILD BOTH:** the user wants the **fuzzy content finder (A)** AND the **fuzzy file
finder (B)**.

- **A. Fuzzy content finder** — a `FzfFinder` (Telescope, `Name="Fzf"`, `Space+F Z` or
  similar): scans the solution's project files for lines matching the typed query via
  fzf's fuzzy matching (per-query re-gather like the grep finder, `IsQueryDriven`),
  previews the hit line, Enter opens the file at the line. Diagnostics:
  `[Telescope] fzf hits=...` / `opened fzf: file=... line=...`. Feature lane (new
  diagnostics).
- **B. Fuzzy file finder** — the file-name finder filtered by fzf fuzzy matching. The
  existing `FileFinder` (Space+F,T) already filters by fzf — confirm/keep it as the
  fuzzy file finder (no new code needed beyond confirming the fzf filter path), OR add
  a distinct `Name="FzfFiles"` finder if the user wants it separate. TBD at plan time.

### Plan sketch (A — the new finder)

1. New `FzfFinder : FinderBase<FileHit>` (or a `FzfHit` with line info), `IsQueryDriven`
   like `GrepFinder`: per-query re-gather over `ProjectFiles.Enumerate(dte)` (shared
   walker), scanning each file's lines for fzf fuzzy matches.
2. The fzf fuzzy matching runs through the existing `FzfFilter.FilterAsync` (the overlay
   already uses it) — feed the file lines as candidates, get the fuzzy-matched lines.
3. Preview jumps to the hit line (`TextMotionNavigator.MoveToLine`); Enter opens the
   file at the line (`TextSelection.GotoLine`).
4. Diagnostics: `[Telescope] fzf hits=...` (per-query summary) and
   `[Telescope] opened fzf: file=... line=...`.
5. Register in `MyExtensionPackage` + bind a leader key (e.g. `Space+F Z`).

### Tests / e2e gates

- **Unit (Telescope.Tests):** the pure hit-gathering/line-mapping logic (extract a
  dependency-free seam like `GrepFinder`'s) — `Run_FzfFinder_*`. RED: the class doesn't
  exist → compile error.
- **e2e (NEW scenario):** a scenario that opens the fzf finder, types a query, asserts
  the fuzzy hits + preview, and opens at the line.
- **Blocker:** e2e RED booting VS (the fzf fuzzy gather only exists in a live instance).
