# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-09-30
- **Scope:** whole repo — `MyExtension/` (core + `MyExtension/Navigation/` + `ToolWindows/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **Post-fix review** of commit `f4450cb` — the 75 findings from the 2026-09-29 report were all executed GREEN 2026-09-30 in the unit-only lane (e2e gates queued in `docs/e2e-queue.md`, E2E-CR-1..3 / E2E-M1..M42, status QUEUED).
- **Method:** 10 parallel workers (4 `arch-auditor` + 4 `code-review-worker` + 1 `test-quality-reviewer` + 1 `docs-accuracy-reviewer`), one per slice (A core / B CardinalMovment / C ToolWindows / D Telescope / E tests+tools / docs). A whole-repo `trailmark-recon` digest (1691 nodes, 681 proxies, 0 entrypoints) was shared with every worker; each structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`). The hub spot-verified every critical/major claim against current code (the `Keys.I` regression, the `VimModeSource.Detach` wrong-buffer unsubscribe, the per-key pre-filter cost, and the docs count drift). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.

## Summary

**67 findings: 2 critical, 31 major, 32 minor, 2 nit.** The 75-finding fix landed cleanly — the pure seams (`OverlayKeyHandler`, `TextMotionNavigator`, `LeaderSequenceMatcher`, `SimpleShortcutMatcher`, `FocusGuard`, `VimModeClassifier`/`VimModeSource`, `InitSteps`, `NavigationSnapshot`, `WindowNavigationEngine`, `FinderBase`, `ResultMapper`, `ProjectFileCache`, `FileContentCache`, `LineIndex`, `FocusTargetModel`, `OverlayShowState`, `FilterFailureLog`, `PaneFailureTracker`) are extracted and unit-tested, the UI-thread hook discipline holds, net472 compliance is clean, and the diagnostics-as-contract logging is consistent. The prior criticals (controller overwrite, runner false-PASS, deferred `ShowDialog`) are all fixed.

The two criticals are both **consolidation-era state bugs that the unit lane cannot see**: (1) `TextInputToolWindowController` dropped `Keys.I` from its action table — the harness-asserted `textinput-enter-input start caret=0` line can never fire, so `neovisual-textinput-motions` will be RED at the next e2e gate; (2) `VsVimModeSource.Detach` ignores its `view` parameter and unsubscribes the single global buffer — with two+ editor views open, closing a non-focused view kills the focused view's `SwitchedMode` subscription, so `_cachedTyping` goes stale and Space starts a leader sequence instead of typing a space in insert mode.

The sharp edges are the same **hot-path and scalability clusters**, now re-verified: (1) the **hook pre-filter is no longer cheap** — `IsKeyOfInterest` evaluates `TextInputSurfaceFocused` (a COM `GetProperty` + WPF visual-tree walk) and re-stats the stale-toolwindow sentinel file on every key-down, contradicting its own "reads only in-process state (no COM/interop)" doc comment; (2) the **Telescope finder path still stalls the UI** — fzf is spawned per keystroke with a synchronous stdin write, GrepFinder re-scans every line of every project file per debounced keystroke on the UI thread, and the preview re-reads + re-tokenizes the whole file per selection change; (3) a **dead-code cluster** — `VimModeState` (a false "pure owner of typing state" with a test surface production never wires in), `KeyNameBuilder` (re-implemented by `SimpleShortcutMatcher`), and four dead members in `CardinalMovment` (`GetWindowsList`, `IsOnScreen`, `DirectionExtensions.Sign`, `DistinctBy`); and (4) a **docs drift** — three docs still carry the pre-consolidation test counts (77/74) while AGENTS.md and spec.md §5.1 say 135/130.

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| CR1 | critical | `MyExtension/ToolWindows/TextInputToolWindowController.cs:42-50` | `Keys.I` (shift+I = InsertStart) dropped from `_actions` by the consolidation — the harness-asserted `textinput-enter-input start caret=0` (test-e2e.ps1:1133) can never fire; `I` falls through to the generic `i` branch |
| CR2 | critical | `MyExtension/Vim/VimModeSource.cs:111-114` | `Detach(view)` ignores `view` and unsubscribes the single global `_currentTextBuffer`; `SubscribeBuffer` unsubscribes the previous buffer on every `Attach` — with 2+ views, closing a non-focused view kills the focused view's mode subscription |
| M1 | major | `MyExtension/Input/InputHandler.cs:409-414` + `MyExtension/ToolWindows/WindowManager.cs:81-120` | `IsKeyOfInterest` evaluates `TextInputSurfaceFocused` (COM `GetProperty(VSFPROPID_DocView)` + `Keyboard.FocusedElement` + visual-tree walk) on every key-down — the pre-filter is no longer "a few Win32 calls" |
| M2 | major | `MyExtension/Input/InputHandler.cs:381` + `MyExtension/ToolWindows/WindowManager.cs:55-61` | `RefreshStaleSentinel()` re-stats the sentinel file (`File.Exists`) per key-down whenever `NEOVISUAL_LOG_DIR` is set |
| M3 | major | `Telescope/Filter/FzfFilter.cs:85-141` | fzf spawned per keystroke with the full candidate list written synchronously to stdin on the UI thread; the timeout path returns without awaiting the faulted `ReadToEndAsync` tasks |
| M4 | major | `Telescope/Finders/GrepFinder.cs:149-171` | After the 200ms debounce, `GetCandidates` scans every line of every project file synchronously on the UI thread per keystroke |
| M5 | major | `Telescope/Overlay/PreviewRenderer.cs:20-33` | Per selection change: `File.ReadAllText` + full `SyntaxHighlighter.Tokenize` on the UI thread; `_lineIndex`/`_linePointers` are static mutable state shared across preview boxes |
| M6 | major | `MyExtension/ToolWindows/TextMotionHelper.cs:110-202` | Per motion key: full buffer copy (`snapshot.GetText()` / `wpf.Text`), fresh `TextMotionNavigator` + `SetText` copy, and a second `FindFocusedTextBox()` visual-tree re-walk |
| M7 | major | `MyExtension/Input/LeaderSequenceMatcher.cs:57,74` | While a leader sequence is active, each key rebuilds the whole sequence string and scans all bindings (`StartsWith`) — O(bindings) + string concat per keystroke |
| M8 | major | `MyExtension/Input/InputHandler.cs:289-332` + `MyExtension/Hooks/GlobalKeyboardHook.cs:83-148` | The tool-window branch calls `controller.TryMove`/`EnterInputMode` with no try/catch and the hook callback has no top-level guard — a controller exception escapes the `WH_KEYBOARD_LL` callback |
| M9 | major | `MyExtension/Navigation/WindowMatrix.cs:37-60` + `WindowAdapter.cs:45,100` | `CheckDte` fetches DTE twice per navigation; the null-activeWindow guard is dead (`LinkedTo` derefs `activeWindow.LinkedWindowFrame` first); `AutoHides`/`Activate` deref a possibly-null `_dte` |
| M10 | major | `Telescope/Overlay/TextMotionNavigator.cs:120` | `Up()` off-by-one on a leading blank line (text starting with `\n`) — `k` on line 2 is a silent no-op |
| M11 | major | `Telescope/Overlay/ResultMapper.cs:39-40` | An unmatched display string falls back to a null-payload entry whose `OnSelected` silently no-ops (or NREs in `FinderBase`) |
| M12 | major | `Telescope/Logging/NeoVisualLog.cs:154` | `EnsurePane` sets `_paneInitTried=true` before `GetGlobalService`; a null service (pre-package-init) permanently disables the Output pane |
| M13 | major | `Telescope/Finders/FileContentCache.cs:17-39` | Cache never evicts — every file ever scanned keeps its full line array for the session |
| M14 | major | `MyExtension/ToolWindows/WindowManager.cs:153-165,237` | Ctor sets `CurrentWindow` but not the classification (`_type`/`_isToolWindow` stay default until the first focus event); `guid != null` on a `Guid` is always true → the `Unknown` fallback is dead |
| M15 | major | `MyExtension/Input/InputHandler.cs:311,403-406` | `Keys.I` branch not guarded on `!_leaderMatcher.IsActive` (a leader sequence can't reach a binding); `IsKeyOfInterest` returns true for bare Ctrl keys that `HandleKey` deliberately ignores |
| M16 | major | `MyExtension/Vim/VimModeTracker.cs:167-172` | `vim-mode=` logged on every focus gain/loss, not just mode switches — the documented "logs every mode switch" contract is ambiguous |
| M17 | major | `MyExtension/Vim/VimModeState.cs:17` | Dead production code with a false docstring ("pure owner of the Vim typing/mode state") — `VimModeTracker` keeps its own `_cachedTyping`/`_editorFocused`; only tests use it |
| M18 | major | `MyExtension/Input/KeyNameBuilder.cs:11` + `MyExtension/Input/SimpleShortcutMatcher.cs:77-85` | `KeyNameBuilder.Build` has zero production callers; `BuildSimpleKey` re-implements the identical canonical-shortcut logic |
| M19 | major | `Telescope/Overlay/TextMotionNavigator.cs:22` + `MyExtension/ToolWindows/TextMotionHelper.cs:70` | Second vim-motion engine — `TextMotionNavigator` (preview+prompt) and `TextMotionHelper` (tool windows) implement the same h/l/w/b/e/0/$/gg/G + a/A/I motions and can drift |
| M20 | major | `MyExtension/ToolWindows/SolutionExplorerController.cs:44-48` + `TextInputToolWindowController.cs:44-50` + `GeneralToolWindowController.cs:49,54-64` | W/B/E action wiring duplicated verbatim across the two text controllers; the J/K→arrow mapping + `toolwindow-move` log re-implemented instead of reusing `KeyToArrowVk` |
| M21 | major | `MyExtension/ToolWindows/HierarchyForestBuilder.cs:33-53` + `SolutionExplorerController.cs:270,324-325` | `pathToItem` param is dead (caller passes a throwaway map never read); the `.cs` filter applied twice |
| M22 | major | `MyExtension/Input/InputHandler.cs:95-98` + `MyExtension/ToolWindows/FocusGuard.cs:24-35` | The text-input-surface keyboard-ownership exemption computed twice with different formulations — can drift |
| M23 | major | `MyExtension/Navigation/UtilityMethods.cs:30`, `WindowAdapter.cs:51`, `Direction.cs:24`, `LinqExtensionMethods.cs:19` | Dead code cluster: `GetWindowsList`, `IsOnScreen`, `DirectionExtensions.Sign`, `DistinctBy` (test-only) — all zero production callers |
| M24 | major | `MyExtension/Package/MyExtensionPackage.cs:164` + `MyExtension/Package/InitSteps.cs` | `RunInitStep` (sync) and `InitSteps.RunAsync` (async) implement the same per-step try/catch + `[MyExtension] init <name> ok/failed` contract in two places |
| M25 | major | `MyExtension/Package/VsServices.cs:43` | `Dte` returns `dte!` (null-forgiving) when `GetService(typeof(DTE))` returns null — callers NRE inside their catch and log a misleading "Command failed" |
| M26 | major | `tools/harness/test-e2e.ps1:974` | `preview tokens=\d+` is a vacuous presence check — `\d+` matches `tokens=0`; a broken `SyntaxHighlighter` passes the only live syntax-highlighting assertion |
| M27 | major | `tools/harness/test-e2e.ps1:518` | `results count=(\d+)` matches `count=0` — the "typing filters candidates" scenario passes even if the filter returns nothing |
| M28 | major | `tests/Telescope.Tests/Program.cs:312,336` | LogFileWriter flush-timer tests are timing-dependent (wall-clock polls for a ~200ms timer) — flaky under load |
| M29 | major | `docs/spec.md:353-354`, `.opencode/skills/vs-extension-dev/SKILL.md:210-211`, `docs/progress.md:72-73` | Test-count drift: all three still say 77/74 while AGENTS.md and spec.md §5.1 say 135/130 |
| M30 | major | `docs/spec.md:162` | §4 diagnostics contract omits 8 harness-asserted lines (`stale-toolwindow sentinel active`, `leader-binding failed:`, `shortcut-binding failed:`, `output pane unavailable:`, `[MyExtension] init <step> ok/failed:`, `fzf filter failed:`, `fzf unavailable — showing unfiltered list`, `filter failed:`) |
| M31 | major | `docs/spec.md:47` + `.opencode/skills/vs-extension-dev/SKILL.md:50,68` | Key-files tables stale post-consolidation — none of the ~25 new seams (OverlayShowState, FocusTargetModel, VimModeClassifier, InitSteps, SimpleShortcutMatcher, NavigationSnapshot, HierarchyWalker, HitOpener, TryDispatch, BlockCaretStyle, FocusKeeper, FileContentCache, LineIndex, FilterFailureLog, PaneFailureTracker, KeyNameBuilder, KeyNames, StaleToolWindowSentinel, ToolWindowControllerBase, HierarchyForestBuilder, TelescopeLog, DteFileOpener, PreviewRenderer, ResultMapper, ProjectFileCache) are listed |
| m1 | minor | `MyExtension/ToolWindows/WindowManager.cs:10` | Class body unindented at column 0 inside `namespace MyExtension` |
| m2 | minor | `MyExtension/Input/KeybindingConfig.cs:94,104,106` | `File.Exists` evaluated twice; `return new KeybindingConfig(...)` at column 0 |
| m3 | minor | `MyExtension/Navigation/WindowNavigationEngine.cs:54` | `gap < minGap` in the second pass is a dead branch (minGap is the minimum); the pipeline re-evaluates all 3 predicates 2×N times |
| m4 | minor | `MyExtension/Navigation/CardinalNavigationConstants.cs:5` | `public readonly static int` — non-idiomatic modifier order, not `const`, inconsistent names |
| m5 | minor | `MyExtension/Navigation/UtilityMethods.cs:10` | `class UtilityMethods` holds only static methods but isn't `static` |
| m6 | minor | `MyExtension/Navigation/WindowMatrix.cs:11` | Not `sealed` while sibling types are |
| m7 | minor | `MyExtension/ToolWindows/FocusGuard.cs:42-46` | `IsTyping`'s `isToolWindow ? isInputMode : ...` sub-branch is dead (isInputMode is always false there) |
| m8 | minor | `MyExtension/ToolWindows/FocusKeeper.cs:19-20` | `Environment.TickCount` (int) wraps every ~24.9 days — the keeper deadline can overflow |
| m9 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:115-118` | Keeper duration `1500` passed twice (to `FocusKeeper.Run` and `FocusKeeperSchedule.Decide`) — can drift |
| m10 | minor | `MyExtension/ToolWindows/FocusGuard.cs:43` + `InputHandler.cs:464` | `IsTyping`'s `editorFocused` param actually receives `EditorFocusedVeto` — name/doc lie |
| m11 | minor | `Telescope/Overlay/OverlayKeyHandler.cs:78` | `ResultCount` property dead (only `SetResults` is used) |
| m12 | minor | `Telescope/Overlay/TextMotionNavigator.cs:54` | `ColumnNumber` has no production callers (test-only surface) |
| m13 | minor | `Telescope/Finders/FileFinder.cs:74` + `CodeIssuesFinder.cs:111` + `GrepFinder.cs:137` + `HitOpener.cs:12` | The `File.Exists` guard + open pattern duplicated 4× — a future finder copies a fifth |
| m14 | minor | `Telescope/Logging/FilterFailureLog.cs:11` | `Format()` embeds the `[Telescope]` prefix and warns callers must use `NeoVisualLog.Log` not `TelescopeLog.Log` — a fragile contract that double-prefixes if misused |
| m15 | minor | `Telescope/Finders/CodeIssuesFinder.cs:127` | `CollectTodos` reads files without the shared `FileContentCache` |
| m16 | minor | `Telescope/Filter/FzfFilter.cs:71` | `IsAvailable()` can block the UI up to 3s on a hung `fzf --version` |
| m17 | minor | `MyExtension/ToolWindows/WindowManager.cs:215` | `RefreshCurrentWindow` calls `IVsMonitorSelection` without `ThrowIfNotOnUIThread()` |
| m18 | minor | `tools/harness/test-e2e.ps1:131-138` | `Stop-HarnessVs` kills any devenv whose title matches 'Experimental'/'MyExtension' — can terminate a user's unrelated VS instance |
| m19 | minor | `tools/harness/iterate-telescope.ps1:226` | `SetEnvironmentVariable('NEOVISUAL_LOG_DIR', ..., 'User')` — persistent USER-scope env mutation |
| m20 | minor | `tools/lint/check-doc-refs.ps1:132-137` | Stale allowlist: `LeaderSequenceMatcher` and `tools/harness/harness-common.ps1` marked "proposed" but now exist — masks real drift |
| m21 | minor | `tests/TestRunner.cs:146` | `TempDir.Dispose` swallows all exceptions — failed recursive deletes leak temp dirs silently |
| m22 | minor | `tests/Telescope.Tests/Program.cs:464` | `Run_FzfFilter_FilterMatchesPrefix` requires the external fzf binary on PATH (Mystery Guest) — the "hermetic" suite fails on machines without fzf |
| m23 | minor | `tests/Telescope.Tests/Program.cs:1298` | `Run_FinderBase_OpenErrorSwallowed` has no assertion — passes even if the error is silently swallowed |
| m24 | minor | `tests/Telescope.Tests/Program.cs:1570` | `Run_Syntax_KeywordsAndIdentifiers` brace check is a weak presence assertion |
| m25 | minor | `tests/NeoVisual.Tests/Program.cs:251` | `Run_HierarchyResolver_FirstSourceFile` is an Eager Test bundling 4 behaviors with no per-assert messages |
| m26 | minor | `tests/NeoVisual.Tests/Program.cs:623,731` | Magic `actionKeyCount: 5` vs hardcoded `6` in text-input-surface tests — inconsistent |
| m27 | minor | `tests/Telescope.Tests/Program.cs:1984` | `Run_GrepFinder_GatherHitsThrowsNotSupported` reaches a protected member via reflection (implementation coupling) |
| m28 | minor | `tests/NeoVisual.Tests/Program.cs:539,548` | `TextInputToolWindowController.TryMove` and `SolutionExplorerController.SelectFirstSourceFile` (`g`) never unit-tested — action-table behavior only e2e-covered |
| m29 | minor | `tests/NeoVisual.Tests/Program.cs:196,503` | Redundant action-key presence tests duplicate the exact-set `Run_ActionTable_*_ActionKeysMatchTable` tests |
| m30 | minor | `tests/Telescope.Tests/Program.cs:177` | Stale "RED today" comment on `Run_LogFileWriter_ClearPerPath` contradicts the all-green suite |
| m31 | minor | `docs/progress.md:10` | Header still "Updated: 2026-09-28 · Last item: neovisual-editor-insert" — not bumped for the 2026-09-30 Code-review GREEN |
| m32 | minor | `.opencode/skills/vs-extension-dev/SKILL.md:179` | "the typo `CardinalMovment` folder/namespace is intentional" conflates folder and namespace (the namespace is `CardinalNavigation`) |
| n1 | nit | `tests/NeoVisual.Tests/Program.cs:437` | `Assert.False(controller.ActionKeys.Count == 0)` double negative |
| n2 | nit | `tests/Telescope.Tests/Program.cs:719` | `Run_CaretPlacement_EnumValues` only checks member names/count, not values |

## Detailed findings

### CR1 (critical) — `TextInputToolWindowController` dropped `Keys.I` (consolidation regression)
- **Where:** `MyExtension/ToolWindows/TextInputToolWindowController.cs:42-50`; harness `tools/harness/test-e2e.ps1:1132-1133`.
- **What's wrong:** The consolidation rewired the controller from a `TryMove` switch to an `_actions` dictionary with `W/B/E/A/H/L` — but **not `Keys.I`**. The pre-consolidation code handled `I` via `MapMotion` (shift+I → `InsertStart`). The controller docstring (line 18) still documents `I` = insert at line start, and the harness presses `I` on the Command Window and asserts `textinput-enter-input start caret=0` (test-e2e.ps1:1133). Today `I` falls through to `InputHandler`'s generic `i` branch (InputHandler.cs:311-321), which logs `toolwindow-enter-input` and inserts at the caret — the asserted line can never fire.
- **Why it bites:** `neovisual-textinput-motions` will be **RED at the next e2e gate** (E2E-M1..M42 are queued). This is the same class as the previous CR1 (a consolidation-introduced regression invisible to the unit lane — no unit test covers the `I` action). The `I` insert-at-line-start motion silently degrades to plain insert-at-caret.
- **Fix:** Add `[Keys.I] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.I, ref _isInputMode)` to `_actions` (bare `i` still falls through to the generic insert). Add a unit test asserting `ActionKeys` contains `I` and that `TryMove(Keys.I)` maps to `InsertStart`.

### CR2 (critical) — `VsVimModeSource.Detach` unsubscribes the wrong buffer with 2+ views
- **Where:** `MyExtension/Vim/VimModeSource.cs:111-114` (`Detach`), `:149-196` (`SubscribeBuffer`); call sites `MyExtension/Vim/VimModeTracker.cs:106,157`.
- **What's wrong:** `Detach(ITextView view)` ignores its `view` parameter and calls `UnsubscribeBuffer(_currentTextBuffer)` — the single global last-attached buffer. `SubscribeBuffer` unsubscribes the previous buffer on every `Attach`. So: open view A → `Attach(A)` subscribes A. Open view B → `Attach(B)` unsubscribes A, subscribes B. Close A (non-focused) → `OnViewClosed(A)` → `Detach(A)` → unsubscribes **B's** buffer. Now B's `SwitchedMode` subscription is gone; mode changes in B never fire.
- **Why it bites:** With two+ editor views open (a normal workflow), the focused view's mode subscription is silently killed. `_cachedTyping` goes stale → in insert mode, Space starts a leader sequence instead of typing a literal space (swallows the space, may fire a binding). This is the exact "a VsVim update will silently break mode detection" class — and it is reachable in normal use, not an exotic race. The unit lane cannot see it (the fake `IVimModeSource` doesn't model multiple views).
- **Fix:** Track the subscribed buffer per view (e.g. `Dictionary<ITextView, object>`), have `Detach(view)` unsubscribe only that view's buffer, and re-subscribe the focused view's buffer on `OnViewGotFocus`.

### M1 — the hook pre-filter is no longer cheap
- **Where:** `MyExtension/Input/InputHandler.cs:409-414`; `MyExtension/ToolWindows/WindowManager.cs:81-120` (`TextInputSurfaceFocused`).
- **What's wrong:** `IsKeyOfInterest` (documented "reads only in-process state (no COM/interop)", line 372-373) passes `_windowManager.TextInputSurfaceFocused` as an eager argument to `FocusGuard.ShouldRouteToolWindowKey` on **every key-down**. That property does `CurrentWindow.GetProperty(VSFPROPID_DocView)` (a COM round-trip) + `Keyboard.FocusedElement` + a visual-tree walk. Separately, `RefreshStaleSentinel()` (line 381) re-stats the sentinel file per key-down whenever `NEOVISUAL_LOG_DIR` is set (every harness run).
- **Why it bites:** The documented "cheap pre-filter, a few Win32 calls only" contract (AGENTS.md) is violated — every plain typing key pays a COM interop call + tree walk + filesystem stat on the UI thread. Windows silently removes a low-level hook whose callback blocks too long.
- **Fix:** Cache the focused-surface fact on focus-change events (like `_isTextInputType`) and read the cached bool per key; stat the sentinel on a timer or focus-change, not per key-down.

### M2 — fzf per-keystroke spawn + sync stdin write; unobserved timeout tasks
- **Where:** `Telescope/Filter/FzfFilter.cs:85-141`; `Telescope/Overlay/TelescopeOverlay.cs:345`.
- **What's wrong:** `FilterAsync` spawns a fresh `fzf --filter` subprocess per keystroke and writes the full candidate list to stdin synchronously on the UI thread (the process I/O runs to its first await on the UI thread). The timeout path `TryKill`s the process and returns without awaiting the faulted `ReadToEndAsync` tasks (unobserved-task noise). The prior F8/M6 fix added a timeout + `FilterFailureLog` but kept the per-keystroke spawn.
- **Why it bites:** On a large candidate set the overlay stutters per keystroke; a hung fzf leaves faulted tasks. The documented `--listen` mode (persistent server) is available.
- **Fix:** Move spawn/write into `Task.Run` (or switch to `fzf --listen`); `await` the output/error tasks after `TryKill`.

### M3 — GrepFinder per-keystroke full-solution scan on the UI thread
- **Where:** `Telescope/Finders/GrepFinder.cs:149-171` (via `TelescopeOverlay.cs:366-378`).
- **What's wrong:** After the 200ms debounce, `GetCandidates` runs synchronously on the UI thread and `IndexOf`-scans every line of every project file (content is cached, but the scan itself is O(total lines) per keystroke).
- **Why it bites:** Typing a grep query in a large solution stalls the overlay per keystroke — the exact cost `ProjectFileCache`/`FileContentCache` were built to amortize.
- **Fix:** Run the scan on a background task and marshal only the results back.

### M4 — preview re-read + re-tokenize per selection; static mutable renderer state
- **Where:** `Telescope/Overlay/PreviewRenderer.cs:20-33`.
- **What's wrong:** Every selection change does `File.ReadAllText` + full `SyntaxHighlighter.Tokenize` synchronously on the UI thread; `_lineIndex`/`_linePointers` are **static** mutable state shared across all preview boxes.
- **Why it bites:** j/k navigation on a large file = per-keystroke disk read + full tokenize on the UI thread; a second preview surface would cross-contaminate caret mapping; stale state survives overlay close.
- **Fix:** Cache the last (path, mtime, content, segments) per file (mtime-keyed) and re-tokenize only on change; make the line index instance state reset on `SetContent`.

### M5 — TextMotionHelper per-motion buffer copy + re-walk
- **Where:** `MyExtension/ToolWindows/TextMotionHelper.cs:110-202`.
- **What's wrong:** Every normal-mode motion key copies the whole buffer (`snapshot.GetText()` / `wpf.Text`), allocates a fresh `TextMotionNavigator` + `SetText` copy, and `ApplyMotionToBox` re-walks the visual tree via a second `FindFocusedTextBox()`.
- **Why it bites:** Every h/l/w/b/e/a/A/I in the Command Window/Immediate/search box is O(buffer) on the UI thread; grows with buffer size.
- **Fix:** Pass the already-found box into `ApplyMotionToBox`; reuse a navigator; avoid the full `GetText()` copy on the editor path.

### M6 — LeaderSequenceMatcher per-key sequence rebuild + O(bindings) scan
- **Where:** `MyExtension/Input/LeaderSequenceMatcher.cs:57,74`.
- **What's wrong:** While a leader sequence is active, each key rebuilds the whole sequence string (`string.Join(",", _sequence.Select(KeyNames.ToString))`) and scans all bindings (`_bindings.Keys.Any(k => k.StartsWith(sequence + ","))`).
- **Why it bites:** Per-keystroke allocation/scan on the leader path; grows with binding count.
- **Fix:** Maintain the sequence string incrementally and precompute a prefix set/trie once at construction.

### M7 — WindowMatrix.CheckDte double DTE fetch
- **Where:** `MyExtension/Navigation/WindowMatrix.cs:37-39`.
- **What's wrong:** `CheckDte(package)` fetches DTE via `VsServices.Dte` and discards it, then line 39 fetches DTE again; its inner try/catch duplicates the ctor's outer catch.
- **Why it bites:** Every navigation (Ctrl+H/J/K/L) constructs a WindowMatrix, so DTE is resolved twice per keystroke — a redundant COM service round-trip on the hot path.
- **Fix:** Delete `CheckDte` and its call; keep the single fetch inside the existing try/catch.

### M8 — tool-window branch unguarded in the hook callback
- **Where:** `MyExtension/Input/InputHandler.cs:289-332`; `MyExtension/Hooks/GlobalKeyboardHook.cs:83-148`.
- **What's wrong:** The tool-window branch calls `controller.TryMove(key)` / `EnterInputMode()` / `ExitInputMode()` with no try/catch, and `HookCallback` has no top-level guard around `HandleKey`. The leader/simple matchers wrap `action()` (LeaderSequenceMatcher.cs:63, SimpleShortcutMatcher.cs:64) but the tool-window path is the remaining unguarded route.
- **Why it bites:** Any exception from a controller action (e.g. a DTE/COM failure in a Solution Explorer action) propagates out of the `WH_KEYBOARD_LL` callback — an unhandled exception in a low-level hook callback can crash VS or silently remove the hook.
- **Fix:** Wrap the tool-window branch (or the whole `HandleKey` body) in try/catch that logs `[NeoVisual] toolwindow-move failed: {msg}` and returns false (pass through).

### M9 — null-activeWindow dead guard + unguarded `_dte` deref
- **Where:** `MyExtension/Navigation/WindowMatrix.cs:49-60`; `WindowAdapter.cs:45,100,116`.
- **What's wrong:** `WindowAdapter.LinkedTo(activeWindow, ...)` dereferences `activeWindow.LinkedWindowFrame` before the `if (activeWindow == null)` guard at :52, so the intended clean no-op branch is dead. `AutoHides()`/`Activate()` deref `_dte` unguarded, and `_dte` can be null (frames without a DTE object).
- **Why it bites:** A null active window or a frame with no DTE object aborts the whole navigation via the catch with a misleading error log (ref F7 — one bad frame kills the move).
- **Fix:** Move the null guard before `LinkedTo`; null-guard `_dte` in `AutoHides`/`Activate`.

### M10 — `TextMotionNavigator.Up()` leading-blank-line off-by-one
- **Where:** `Telescope/Overlay/TextMotionNavigator.cs:120`.
- **What's wrong:** When the caret is on line 2 and line 1 is empty (text starts with `\n`), `LastIndexOf('\n', Math.Max(0, lineStart-2))` clamps to 0 and finds the `\n` at index 0 (the end of line 1), so `prevStart == 1` (line 2's own start) and `Up()` stays put — `k` on line 2 is a silent no-op.
- **Why it bites:** Leading-newline files (rare but real) get a stuck caret on `Up()`; the preview `preview caret=... line=...` assertion can only catch it as a flaky.
- **Fix:** Only search from `lineStart-2` when `lineStart-2 >= 0`, else `prevNewline = -1` / `prevStart = 0`.

### M11 — ResultMapper null-payload fallback
- **Where:** `Telescope/Overlay/ResultMapper.cs:39-40`.
- **What's wrong:** An unmatched display string falls back to a null-payload `FinderEntry` whose `OnSelected` silently no-ops (or NREs in `FinderBase`'s `HitType` cast).
- **Why it bites:** A display-key mismatch (encoding drift, fzf reorder) silently swallows the user's selection with no diagnostic.
- **Fix:** Skip unknown matches or give the fallback a safe no-op payload; log a warning.

### M12 — `NeoVisualLog.EnsurePane` latch
- **Where:** `Telescope/Logging/NeoVisualLog.cs:154`.
- **What's wrong:** `_paneInitTried=true` is set before `GetGlobalService`; if it returns null (pre-package-init), the pane is never retried.
- **Why it bites:** Log lines emitted before package init permanently miss the Output pane (the file still captures them) — the harness's pane-based diagnostics silently vanish.
- **Fix:** Reset `_paneInitTried` on the null-service path so a later call retries.

### M13 — FileContentCache unbounded growth
- **Where:** `Telescope/Finders/FileContentCache.cs:17-39`.
- **What's wrong:** `_entries` never evicts; every file ever scanned keeps its full line array for the finder's lifetime.
- **Why it bites:** Long VS sessions on large solutions grow memory without bound; deleted/renamed files linger.
- **Fix:** Cap by count (LRU) or clear on solution change.

### M14 — WindowManager ctor doesn't classify the current window; `guid != null` always true
- **Where:** `MyExtension/ToolWindows/WindowManager.cs:153-165,237`.
- **What's wrong:** The ctor comment claims it "initializes the current window (and its classification)" but only sets `CurrentWindow`; `_type`/`_isToolWindow` stay default until the first `SEID_WindowFrame` event. And `guid != null` on a `Guid` is always true → the `Unknown` fallback branch is dead.
- **Why it bites:** Until the first focus-change event, tool-window routing (and the FocusGuard) is disabled even when a tool window is focused at startup; the dead guard misleads.
- **Fix:** Call `OnWindowFocusChanged()` in the ctor after `RefreshCurrentWindow()`; change to `guid != Guid.Empty`.

### M15 — `Keys.I` not leader-guarded; bare Ctrl wasted work
- **Where:** `MyExtension/Input/InputHandler.cs:311,403-406`.
- **What's wrong:** The `key == Keys.I` branch (enter-input-mode fallback) doesn't guard on `!_leaderMatcher.IsActive` (the adjacent hjkl/action-key branch does). And `IsKeyOfInterest` returns true for `ControlKey`/`LControlKey`/`RControlKey` while `HandleKey` deliberately does nothing with the bare Ctrl key (line 273-275).
- **Why it bites:** "Space i" in a tool window silently drops the leader sequence and enters input mode; every Ctrl key-down runs the full handler for nothing.
- **Fix:** Add `!_leaderMatcher.IsActive &&` to the `Keys.I` condition; drop the ControlKey cases from `IsKeyOfInterest` (or implement the documented Ctrl-swallow).

### M16 — `vim-mode=` logged on focus changes
- **Where:** `MyExtension/Vim/VimModeTracker.cs:167-172`.
- **What's wrong:** `UpdateTypingFromMode` logs `vim-mode={name}` on every focus gain/loss (`OnViewGotFocus`/`OnViewLostFocus` call it with current/null mode), not just on actual mode switches.
- **Why it bites:** The `vim-mode=` diagnostic is documented as "logs every mode switch"; overloading it with focus-change lines makes the log noisier and the contract ambiguous.
- **Fix:** Log only when the mode value actually changes.

### M17 — `VimModeState` dead production code
- **Where:** `MyExtension/Vim/VimModeState.cs:17`.
- **What's wrong:** Trailmark: `callers_of(VimModeState..ctor)` = [] (direct + proxy); only tests use it (`Run_VimModeState_*`). `VimModeTracker` keeps its own `_cachedTyping`/`_editorFocused` fields and calls `VimModeClassifier` directly. The class's docstring claim of being "the pure owner of the Vim typing/mode state" is false.
- **Why it bites:** Two competing owners of typing state; the M17 test surface exercises a class production never wires in, giving false confidence; a future dev may wire the wrong owner.
- **Fix:** Wire `VimModeTracker` through `VimModeState` (single owner) or delete the class and move its tests onto `VimModeClassifier`/`VimModeTracker`.

### M18 — `KeyNameBuilder` dead; `BuildSimpleKey` re-implements it
- **Where:** `MyExtension/Input/KeyNameBuilder.cs:11`; `MyExtension/Input/SimpleShortcutMatcher.cs:77-85`.
- **What's wrong:** `KeyNameBuilder.Build` has zero production callers (only 6 test callers); `BuildSimpleKey` re-implements the identical Ctrl+/Shift+/Alt+ prefix + `KeyNames.ToString` logic.
- **Why it bites:** Two copies of the canonical-shortcut builder drift independently (e.g. adding a Win modifier reaches only one); the "single-allocation via StringBuilder" path is bypassed in production.
- **Fix:** Have `BuildSimpleKey` delegate to `KeyNameBuilder.Build` (the comment at SimpleShortcutMatcher.cs:83 even says "do NOT copy") and delete the duplicate.

### M19 — second vim-motion engine
- **Where:** `Telescope/Overlay/TextMotionNavigator.cs:22`; `MyExtension/ToolWindows/TextMotionHelper.cs:70`.
- **What's wrong:** `TextMotionNavigator` (preview+prompt, via `TryDispatch`) and `TextMotionHelper` (tool windows) implement the same h/l/w/b/e/0/$/gg/G + a/A/I motions as two engines.
- **Why it bites:** Two engines drift independently (the `$`/bare-D4 fix and the `Up()` edge cases exist in one, not the other); `TextMotionHelper.TryMoveFocusedSurface` has 32 downstream nodes (RECON) so any divergence is high-blast-radius.
- **Fix:** Make `TextMotionHelper` delegate to the pure `TextMotionNavigator` (or extract one shared motion core) and keep a single unit-tested engine.

### M20 — controller action-table duplication
- **Where:** `MyExtension/ToolWindows/SolutionExplorerController.cs:44-48`; `TextInputToolWindowController.cs:44-50`; `GeneralToolWindowController.cs:49,54-64`.
- **What's wrong:** The W/B/E action wiring (`() => TextMotionHelper.TryMoveFocusedSurface(...)`) and `OnModeChanged() => StyleFocusedSurface(...)` are duplicated verbatim across the two text controllers; the J/K→arrow mapping + `toolwindow-move key=... -> arrow vk=...` log line is re-implemented in the Solution Explorer `_actions` table instead of reusing `GeneralToolWindowController.KeyToArrowVk`.
- **Why it bites:** Any change to the motion wiring or caret restyle must be made twice; the arrow VK mapping and the log-as-contract format exist in two places and can drift.
- **Fix:** Extract a shared `TextMotionControllerBase` (or a static `TextMotionActions.Build()`); have `SolutionExplorerController` reuse `KeyToArrowVk`/`TryMove` for the J/K entries.

### M21 — `HierarchyForestBuilder` dead param + double `.cs` filter
- **Where:** `MyExtension/ToolWindows/HierarchyForestBuilder.cs:33-53`; `SolutionExplorerController.cs:270,324-325`.
- **What's wrong:** `Build` writes `pathToItem[item.FullPath] = item.FullPath` but the only caller passes a fresh throwaway `Dictionary` that is never read; the `.cs` filter is applied twice (`MapChildren` already excludes non-.cs before building `HierarchyItemInfo`, then `Build` filters again).
- **Why it bites:** Misleading API surface + redundant work; the builder's documented "path→path identity" contract is silently discarded.
- **Fix:** Drop the `pathToItem` param (or return it); keep the `.cs` filter in exactly one place (the pure builder is the better home).

### M22 — FocusGuard text-input exemption computed twice
- **Where:** `MyExtension/Input/InputHandler.cs:95-98`; `MyExtension/ToolWindows/FocusGuard.cs:24-35`.
- **What's wrong:** `FocusGuard.ShouldRouteToolWindowKey`/`HasToolWindowActionKeys` take `isTextInputSurface`+`textInputSurfaceFocused` params, while `EditorFocusedVeto` re-derives the same rule inline via `GeneralToolWindowController.IsTextInputType(_windowManager.Type) && _windowManager.TextInputSurfaceFocused`.
- **Why it bites:** Two sources of truth for "does this surface own the keyboard" can drift (e.g. one adds a type, the other doesn't), changing routing behavior subtly — the exact leak class the FocusGuard exists to prevent.
- **Fix:** Pass the veto into FocusGuard as a single `ownsKeyboard` boolean, or route `EditorFocusedVeto` through the same FocusGuard helper.

### M23 — dead code cluster in CardinalMovment
- **Where:** `MyExtension/Navigation/UtilityMethods.cs:30` (`GetWindowsList`), `WindowAdapter.cs:51` (`IsOnScreen`), `Direction.cs:24` (`DirectionExtensions.Sign`), `LinqExtensionMethods.cs:19` (`DistinctBy`).
- **What's wrong:** All four have zero production callers (Trailmark direct + proxy). `DistinctBy` is documented in AGENTS.md as the net472 reason but nothing in production uses it.
- **Why it bites:** Dead COM surface + a documented-as-convention helper that is test-only — a future reader assumes they are load-bearing.
- **Fix:** Delete them (and drop the AGENTS.md `DistinctBy` claim, or wire it into a real call site).

### M24 — duplicate init-step logging contract
- **Where:** `MyExtension/Package/MyExtensionPackage.cs:164`; `MyExtension/Package/InitSteps.cs`.
- **What's wrong:** `RunInitStep` (sync, log-config steps) and `InitSteps.RunAsync` (async, init steps) implement the same per-step try/catch + `[MyExtension] init <name> ok/failed` logging contract in two places.
- **Why it bites:** The harness asserts on the `init <step> ok/failed` lines; a format drift between the two paths is a silent contract break.
- **Fix:** Fold the log-config steps into the `InitSteps` list (or have `RunInitStep` delegate to the same logging helper).

### M25 — `VsServices.Dte` null-forgiving
- **Where:** `MyExtension/Package/VsServices.cs:43`.
- **What's wrong:** `Dte` returns `dte!` (null-forgiving) when `GetService(typeof(DTE))` returns null, despite the non-nullable `DTE` signature; callers like `InputHandler.ExecuteVsCommand` then NRE inside their catch and log a misleading "Command failed".
- **Why it bites:** Masks the real failure (DTE unavailable) as a command failure; the nullability contract is a lie.
- **Fix:** Return `DTE?` and let callers handle null explicitly.

### M26–M27 — vacuous e2e assertions
- **M26** `tools/harness/test-e2e.ps1:974` — `preview tokens=\d+` matches `tokens=0`; a broken `SyntaxHighlighter` that returns zero segments passes the only live syntax-highlighting assertion. **Fix:** assert `preview tokens=[1-9]\d*` or pin the exact seeded-file token count.
- **M27** `tools/harness/test-e2e.ps1:518` — `results count=(\d+)` matches `count=0`; the "typing filters candidates" scenario passes even if the filter returns nothing (only `promptChanged` is a real gate). **Fix:** assert `results count=[1-9]\d*` (query 'pro' must match ≥1 seeded file).
- **Why they bite:** Both are false-confidence gates on the exact behaviors the harness exists to prove (syntax highlighting, filtering).

### M28 — LogFileWriter flush-timer tests timing-dependent
- **Where:** `tests/Telescope.Tests/Program.cs:312,336`.
- **What's wrong:** `Run_LogFileWriter_FlushTimer_IdleDoesNotFire` and `_OneShotFires` use wall-clock polls (20×50ms / 1s) for a ~200ms timer.
- **Why it bites:** Flaky under load — the idle test can spuriously fail if a flush lands during the idle window; the one-shot test can miss under heavy CI.
- **Fix:** Inject a controllable timer/clock seam into `LogFileWriter`; keep the poll only as a fallback.

### M29–M31 — docs drift
- **M29** `docs/spec.md:353-354`, `.opencode/skills/vs-extension-dev/SKILL.md:210-211`, `docs/progress.md:72-73` — all three still say 77/74 while AGENTS.md and spec.md §5.1 say 135/130. A reader following §8/SKILL gets the wrong expected count; the hub's resume checkpoint carries a stale baseline.
- **M30** `docs/spec.md:162` — §4 diagnostics contract omits 8 harness-asserted lines (all verified present in code). A feature author reading §4 won't know these lines are pinned by e2e.
- **M31** `docs/spec.md:47` + `SKILL.md:50,68` — key-files tables list none of the ~25 post-consolidation seams that progress.md names as regression targets ("look first at the extracted pure seams" has no table entry).

## Recommendations (ordered by effort/impact)

1. **Fix the 2 criticals first** (CR1 `Keys.I` action-table regression, CR2 `VimModeSource.Detach` wrong-buffer unsubscribe) — each is a small, high-blast-radius fix. CR1 is gated on `neovisual-textinput-motions`; CR2 needs a multi-view unit test with a fake `IVimModeSource`.
2. **Restore the hook hot-path contract** (M1 per-key COM+tree-walk, M2 sentinel stat, M15 bare-Ctrl, M6 leader sequence rebuild) — one focused pass on `InputHandler`/`WindowManager`/`LeaderSequenceMatcher` to make the pre-filter genuinely cheap again.
3. **Amortize the finder path** (M2 fzf spawn, M3 GrepFinder scan, M4 preview re-tokenize, M5 TextMotionHelper copies) — each is a localized change behind an existing seam (`Task.Run` + marshal, mtime-keyed cache, instance state).
4. **Delete the dead code** (M17 `VimModeState`, M18 `KeyNameBuilder`, M23 CardinalMovment cluster) — pure deletions with test consolidation; low risk.
5. **Merge the duplication clusters** (M19 vim-motion engine, M20 controller action tables, M21 forest builder, M22 FocusGuard exemption, M24 init logging) — start with the pure-logic merges that have hermetic test seams.
6. **Harden the harness** (M26/M27 vacuous regexes, M28 timing-dependent tests, m18 scoped kills, m19 Process-scope env, m20 stale allowlist) — prevents false PASS/FAIL and machine mutation.
7. **Fix the docs drift** (M29 counts, M30 diagnostics list, M31 key-files tables, m31 header, m32 namespace wording) — mechanical, do after the behavior fixes.

## Filed into progress.md

**None filed (user decision 2026-09-30):** the user chose to keep all findings in this report and hand it to the planner directly — nothing was appended to `docs/progress.md` (which `neovim_hub` owns). All 67 findings (CR1, CR2, M1–M31, m1–m32, n1–n2) live here. Cross-references to `docs/reviews/architecture-review.md`: F2→M1 (per-key hot path, now the COM+stat variant), F5→CR2 (stale buffer subscription, now the wrong-buffer unsubscribe), F7→M9 (one bad frame kills navigation), F8/F9→M2 (fzf per-keystroke spawn — timeout added, spawn remains), F20→M24 (missing `ThrowIfNotOnUIThread` in `ExecuteCommand`), F22→M18 (`KeyNameBuilder` not extracted — now extracted but unused), F23→M15 (Ctrl-key pre-filter cases dead). The prior report's CR1/CR2/CR3 and M1–M43 were all executed GREEN 2026-09-30 and are NOT re-reported.
