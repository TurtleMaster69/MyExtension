# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-10-02
- **Scope:** whole repo — `MyExtension/` (core + `Navigation/` + `ToolWindows/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **Fresh post-fix review** after the 2026-10-02 "Code review fixes (51 findings)" plan reached GREEN (unit-only lane; e2e gates E2E-CR51-1..10 queued in `docs/e2e-queue.md`).
- **Method:** 1 whole-repo `trailmark-recon` digest (1899 nodes, 773 proxies = 40.7%, 0 entrypoints) shared with every worker + 10 parallel workers (4 `arch-auditor` A–D + 4 `code-review-worker` A–D + 1 `test-quality-reviewer` E + 1 `docs-accuracy-reviewer`) + hub-conducted slice F (cross-cutting). Every structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`). The hub independently verified the highest-value claims by direct code reading: the two critical tautological FocusGuard tests (constant-false expressions), the `BlockCaretAdornment` deactivation bug, the `VimBufferSubscriptions._bufferToTextBuffer` leak, the `check-doc-content.ps1` RED (3/12), and the `telescope-open` false-positive e2e gate. No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.

## Summary

**72 findings: 2 critical, 8 major, 44 minor, 18 nit.** The 51-findings plan landed cleanly — every prior finding in this slice set is verified FIXED (R1 prompt j/k/g/G routing, R2 preview render, R3 VimModeSource Closed-subscription, R4 frame fault isolation, R5 telescope-mode gate, R6/R14/R24 dead code + redundant test, R11 shift narrowing, R12 InjectedKeyGuard TTL, R17/R18 focused-box reuse + caret-relative slice, R22 fzf debounce, R25–R27/R50/R51 docs drift). But this run found **two critical test-quality holes in the FocusGuard suite**: `Run_FocusGuard_InputModeBlocksActionKeys` and `Run_FocusGuard_ZeroActionKeysBlocks` both assert a **constant-false expression** (`... && !isInputMode` with `isInputMode=true`, and `... && actionKeyCount > 0` with `actionKeyCount=0`), so they pass regardless of what `FocusGuard` returns — a regression that makes input-mode/zero-action-key tool windows leak keys into the editor would pass the suite silently. The hub verified both are tautological and that they pass in the current 163-test run.

The other sharp edges cluster into: (1) the **Vim buffer-subscription lifecycle** still leaks (`_bufferToTextBuffer` is never cleaned in `DecrementRefCount`, and the split-view path never Closed-subscribes the second view — the R3 fix was partial); (2) the **block caret deactivation path is broken** (`Active=false` → `Update()` early-returns before `RemoveAdornmentsByTag`, so the white block stays glued over the native caret after leaving normal mode or losing focus — the `block-caret active=False` diagnostic lies); (3) the **navigation fault-isolation story still has holes** (`TryGetScreenRect` ignores the HRESULT → silent empty/garbage rect without the n19 diagnostic; `NavigationSettings.Invalidate()` is dead code → stale DPI divide tolerances; the `_cachedLinked` cache is keyed only on the adapters list, never the active window); and (4) a **RED harness-health gate** — `check-doc-content.ps1` fails 3/12 assertions because its checks are hardcoded to the Phase-10 (98-findings) fixed state while `docs/progress.md` has advanced to the 51-findings plan.

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| N1 | critical | `tests/NeoVisual.Tests/Program.cs:1043-1046` | `Run_FocusGuard_InputModeBlocksActionKeys` is tautological: `isInputMode=true` makes `... && !isInputMode` constant-false, so `Assert.False` always passes regardless of `FocusGuard` |
| N2 | critical | `tests/NeoVisual.Tests/Program.cs:1055-1059` | `Run_FocusGuard_ZeroActionKeysBlocks` is tautological: `actionKeyCount=0` makes `... && actionKeyCount > 0` constant-false, so `Assert.False` always passes |
| N3 | major | `MyExtension/Vim/Utils/VimBufferSubscriptions.cs:52,119-135` + `VimModeSource.cs:215-239` | `_bufferToTextBuffer` never removed in `DecrementRefCount` (grows per unique buffer for the whole session); split-view second view never Closed-subscribed → refcount stuck at 1, `SwitchedMode` subscription never dropped |
| N4 | major | `MyExtension/Adornments/BlockCaretAdornment.cs:100,121-127` | `Active=false` → `Update()` early-returns before `RemoveAdornmentsByTag` — deactivation never removes the block caret; the `block-caret active=False` diagnostic lies |
| N5 | major | `MyExtension/Navigation/Utils/WindowFrameAdapter.cs:219` | `TryGetScreenRect` ignores the `GetWindowScreenRect` HRESULT — a failed read yields a silent empty (or garbage non-empty) rect WITHOUT the n19 `window rect unavailable` diagnostic |
| N6 | major | `MyExtension/Navigation/Utils/NavigationSettings.cs:31` | `Invalidate()` has zero callers (verified via Trailmark) — `FromSystemDpi` caches `_cached` forever, so a mid-session DPI change leaves stale divide tolerances |
| N7 | major | `MyExtension/Navigation/Utils/WindowFrameUtils.cs:94-102` + `WindowFrameAdapter.cs:135-142` | The Properties-window quirk (caption + ToolWindow↔Properties cross-type match) is implemented twice (`CompareWindows` vs `LinkedTo`'s key-set path) |
| N8 | major | `tests/Telescope.Tests/Program.cs:2504-2516` | `Run_FileContentCache_EvictsOldest` uses `DateTime.UtcNow` for all three entries — on a ~15ms clock tick "a"/"b" share a key, so eviction is not guaranteed → flake |
| N9 | major | `tools/harness/test-e2e.ps1:514-520,583,544` | `telescope-open`/`-mode`/`-navigate` assertions are satisfied by `Open-Telescope`'s own wait line (baseline reset before the helper) — false-positive gates |
| N10 | major | `tools/lint/check-doc-content.ps1:131,156,162` | Doc-content lint is RED (3/12): assertions hardcoded to the Phase-10 (98-findings) fixed state while `docs/progress.md` advanced to the 51-findings plan |
| N11 | minor | `MyExtension/Navigation/WindowNavigator.cs:26-27,93-100` | `_cachedLinked` static cache keyed only on the adapters-list reference, never on the active window, never cleared |
| N12 | minor | `MyExtension/Navigation/WindowNavigator.cs:140` | Active window rect never validated — an empty active rect anchors `SelectTarget` around `(0,0,0,0)` |
| N13 | minor | `MyExtension/Navigation/Utils/WindowFrameAdapter.cs:213` | `TryGetScreenRect` (internal, COM-touching) lacks `ThreadHelper.ThrowIfNotOnUIThread()` |
| N14 | minor | `MyExtension/Navigation/Utils/WindowFrameUtils.cs:41-68` | `GetLinkedWindowsList` O(n·m) `CompareWindows` pre-filter that `LinkedTo` re-validates with a different key-set strategy |
| N15 | minor | `MyExtension/Navigation/WindowNavigator.cs:138` | `_activeWindows.IndexOf(_activeWindow)` O(n) reference scan per navigation |
| N16 | minor | `MyExtension/ToolWindows/WindowManager.cs:173` | `ResolveController` (test-only) duplicates the registered→default resolution `GetController` re-implements |
| N17 | minor | `MyExtension/ToolWindows/Utils/FocusGuard.cs:50` + `InputHandler.cs:325` | The 6-arg `ShouldRouteToolWindowKey` re-implements the editor-veto inline instead of composing with the 5-arg overload — veto logic in two places |
| N18 | minor | `MyExtension/ToolWindows/Utils/HierarchyResolver.cs:52` | `FirstPathMatching` hand-rolls the forest recursion while `FirstSourceFilePath` delegates to the shared `HierarchyWalker` |
| N19 | minor | `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:104,136` | R18's caret-relative slice applied only to the editor-view path; WPF/WinForms still materialize the whole buffer per h/l/w/b/e key |
| N20 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:44` | H/L inline lambdas (log + `TryMoveArrow`) vs J/K bare delegates — two shapes for the same hjkl→arrow press |
| N21 | minor | `MyExtension/ToolWindows/ToolWindowControllerBase.cs:46` | `_isInputMode` mutated via a `ref` param in `TryMoveFocusedSurface` for a/A/I, bypassing `EnterInputMode()`/`OnModeChanged()` |
| N22 | minor | `MyExtension/ToolWindows/Utils/FocusKeeper.cs:27` + `SolutionExplorerController.cs:118` | `Run`'s `KeeperHandle` ignored by both callers — a superseded keeper's queued tick can still re-assert the old target |
| N23 | minor | `MyExtension/ToolWindows/Utils/TextMotionHelper.cs:182` | Editor-view `A` (InsertEnd) lands at the caret-relative slice boundary, not the true line end, on lines >~4096 chars |
| N24 | minor | `MyExtension/Vim/VimModeTracker.cs:96` + `SolutionExplorerController.cs:231` | `editor-view-opened` emitted for every `IWpfTextView` (split/peek/preview) and can be double-emitted by `SelectFirstSourceFile` + `TextViewCreated` |
| N25 | minor | `MyExtension/ToolWindows/WindowManager.cs:261` | Unguarded `(int)value` cast on `VSFPROPID_Type` inside the `IVsSelectionEvents` callback — `InvalidCastException` escapes a VS callback |
| N26 | minor | `MyExtension/ToolWindows/Utils/FocusKeeper.cs:100` | `FocusKeeperSchedule.Decide` returns `Reassert` (not `Stop`) after `MaxEscapeAttempts` with the box still focused — keeper fights the user |
| N27 | minor | `MyExtension/Package/RoslynGatherers.cs:285` | `IsWriteLocation` runs `GetProperty("IsWrittenTo")` reflection per reference location on the UI thread |
| N28 | minor | `MyExtension/Vim/Utils/VimModeSource.cs:451,471` | `GetVim` re-queries MEF + re-logs `VsVim integration: not found` on every view open / focus gain when VsVim is absent |
| N29 | minor | `MyExtension/Input/InputHandler.cs:295` | The Ctrl+N/P popup-navigation branch runs before the leader state machine and never resets an in-progress leader sequence |
| N30 | minor | `MyExtension/Input/Utils/KeyNameBuilder.cs:11` | `Build` allocates a fresh `StringBuilder` + string per call on the hook hot path |
| N31 | minor | `Telescope/Overlay/Utils/PromptMotionRouter.cs:33` | `'a'` maps to `CaretPlacement.Current` (no motion) — in the prompt `a` behaves identically to `i`, never inserting after the caret |
| N32 | minor | `Telescope/Finders/GrepFinder.cs:91-115` | `GetCandidates` scans every project file synchronously on the UI thread per debounced query; the content cache is cold each overlay open |
| N33 | minor | `Telescope/Overlay/Utils/PreviewTokenCache.cs` + `PreviewDocumentCache.cs` + `Finders/Utils/FileContentCache.cs` | Three near-identical mtime-keyed caches; `PreviewDocumentCache` is a strict subset of `PreviewTokenCache` |
| N34 | minor | `Telescope/Overlay/Utils/PreviewRenderer.cs:54` + `PreviewTokenCache.cs:39` | On a preview cache miss the file is read twice (`Show` via `_contentCache`, then `SetContent` re-reads via `_tokenCache`) |
| N35 | minor | `Telescope/Overlay/Utils/PreviewRenderer.cs:170-181` | `ColorFor` allocates a new `SolidColorBrush` per segment on every `FlowDocument` rebuild |
| N36 | minor | `Telescope/Overlay/Utils/TextMotionDispatcher.cs:46-96` | Two parallel `MapKey` switch tables (WinForms `Keys` vs WPF `Key`) that must be kept in sync |
| N37 | minor | `Telescope/Finders/Utils/ProjectFiles.cs:21` | Only `GrepFinder` routes through the shared `ProjectFileCache`; the other finders re-walk the DTE tree per open |
| N38 | minor | `Telescope/Filter/FzfFilter.cs:98` | `IsAvailable()` runs a synchronous `WaitForExit(500)` probe on the UI thread at first overlay open |
| N39 | minor | `Telescope/Filter/FzfFilter.cs:128` | `FilterAsync` never checks the cached `_availability` — per-keystroke `Win32Exception` + `fzf filter failed` log when fzf is missing |
| N40 | minor | `Telescope/Logging/Utils/PaneFailureTracker.cs:15,35-41` | `_retryAllowed` is always true — the "retry latch" is dead logic and the `!ShouldRetry()` guard is unreachable |
| N41 | minor | `Telescope/Logging/Utils/FilterFailureLog.cs:14` | `Format()` returns an UNPREFIXED string relying on the caller to log via `TelescopeLog` — a wrong logger silently breaks the `filter failed:` contract |
| N42 | minor | `Telescope/Finders/CodeIssuesFinder.cs:103` | `Display` embeds `issue.Text` verbatim — a multi-line Error List description breaks fzf/`ResultMapper` and the results TextBox |
| N43 | minor | `Telescope/Overlay/TelescopeOverlay.cs:348,211` + `ResultMapper.cs:60` | User-controlled text interpolated into log lines — `\n`/control chars split the line and break `Wait-NewLogLine` assertions |
| N44 | minor | `Telescope/Finders/GrepFinder.cs:99` + `CodeIssuesFinder.cs:77` | `ProjectFileCache` invalidated only on solution-name change — added/removed files within a solution stay stale |
| N45 | minor | `Telescope/Filter/FzfFilter.cs:233` | Hand-rolled `QuoteArg` mis-escapes backslashes-before-quotes (Windows argv rules) — a query like `C:\` filters against the wrong string |
| N46 | minor | `tests/NeoVisual.Tests/Program.cs:1017-1062` | FocusGuard tests assert the SAME expression twice with only the message differing — duplicate assertions |
| N47 | minor | `tests/NeoVisual.Tests/Program.cs:1107-1112` | `Run_FocusGuard_TruthTable_ActionKeysTextInputSurface` is a byte-identical duplicate of `..._TextInputSurfaceOwnsKeyboard` |
| N48 | minor | `tests/NeoVisual.Tests/Program.cs:1486-1516` | `Run_ActionsRegistry_TelescopeMapsToFinder` + `..._TelescopeKeysMatchFinderNames` compute the same key sets and assert the same `SetEquals` |
| N49 | minor | `tests/Telescope.Tests/Program.cs:2720-2731` | `Run_ResultMapper_UnknownStringSkippedOrLogged`'s `All(i => i.Payload != null)` is vacuously true when `items` is empty |
| N50 | minor | `tests/NeoVisual.Tests/Program.cs:398-427` | `Run_WindowManager_DefaultControllerCache` reflection-invokes private `GetController` and mutates the static `ThreadHelper.uiThreadDispatcher` (never restored) |
| N51 | minor | `tests/NeoVisual.Tests/Program.cs:864,1228` | Coverage gap: `TextMotionHelper.TryMoveFocusedSurface` and `FocusKeeper` have no unit test — verified only by e2e |
| N52 | minor | `tools/harness/test-e2e.ps1:716-731,800-815` | `neovisual-explorer-open`/`-open-o` use a "try l/j/Enter until ANY file opens" loop — order-dependent, doesn't pin WHICH file opens |
| N53 | minor | `tools/harness/test-e2e.ps1:1080-1083` | `neovisual-editor-insert` updates the seed-expected copy in a `finally`, so even a FAILED marker assertion records the wrong content as expected |
| N54 | minor | `docs/reviews/architecture-review.md:269,284,313,318,395` | F1/F16/F45 still presented as open with no FIXED annotation (all fixed per `docs/progress.md`) |
| N55 | minor | `docs/reviews/code-review.md:66` | R50's premise is no longer true — `architecture-review.md:263` now uses the post-restructure names |
| N56 | nit | `MyExtension/Input/Utils/KeybindingConfig.cs:75` | `Load`/`LoadDefaults`/`LoadFromJson` are three near-identical methods each building the merge prologue |
| N57 | nit | `MyExtension/Hooks/GlobalKeyboardHook.cs:6` (+ `TelescopeLauncher.cs:5`, `MyExtensionPackage.cs:8`) | Unused `using System.Diagnostics;` |
| N58 | nit | `MyExtension/Hooks/GlobalKeyboardHook.cs:66` | Ctor inlines `NeoVisualLog.Log($"{DiagnosticLog.Hook}starting")` instead of the `Log()` helper that prepends the prefix |
| N59 | nit | `MyExtension/Navigation/Utils/WindowFrameAdapter.cs:18,41,48` | `_dte` non-readonly while `_frame4` is `readonly`; dead `_dte == null` checks on a non-nullable field |
| N60 | nit | `MyExtension/Navigation/WindowNavigator.cs:30` | Stale doc comment "initalize windowmatrix" — pre-restructure name |
| N61 | nit | `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs:13` | `HierarchyItemInfo` vs `HierarchyNode` near-identical DTOs with a one-to-one conversion |
| N62 | nit | `MyExtension/ToolWindows/ToolWindowControllerBase.cs:52` | The `_actions` dict + `TryMove` dict-lookup pattern duplicated in `TextInputToolWindowController` + `SolutionExplorerController` |
| N63 | nit | `Telescope/Logging/Utils/FilterFailureLog.cs:12-15` | A one-line string concatenation wrapped in a dedicated class |
| N64 | nit | `Telescope/Overlay/Utils/SyntaxHighlighter.cs:64-187` | `Tokenize` is complexity 23 with a per-token `text.Substring` allocation |
| N65 | nit | `Telescope/Logging/NeoVisualLog.cs:101-103` | Every `Log` line is duplicated to the debug file via `Debug.WriteLine` → `NeoVisualTraceListener` |
| N66 | nit | `Telescope/Overlay/Utils/TextMotionNavigator.cs:61` | `Down()` on the last line moves the caret to `_text.Length` instead of no-op (and `Up()` on the first to 0) |
| N67 | nit | `Telescope/Overlay/Utils/SyntaxHighlighter.cs:127` | `$@"..."` (interpolated verbatim) is not matched by the `'$' + '"'` branch — renders as Default |
| N68 | nit | `Telescope/Finders/FileFinder.cs:114` | `OpenHit` logs `opened file: {path}` even when `_dteFactory()` returns null (the `?.` short-circuits the open) |
| N69 | nit | `MyExtension/Package/MyExtensionPackage.cs:168` | Null-forgiving `!` on `_launcher` hides a real dependency — a failed telescope init step makes the hook step fail with `ArgumentNullException` |
| N70 | nit | `MyExtension/Input/Utils/KeybindingConfig.cs:218` | `ParseLeader` accepts any `Keys` value via `Enum.TryParse`, including modifiers (`"Ctrl"` silently disables the leader key) |
| N71 | nit | `MyExtension/ToolWindows/SolutionExplorerController.cs:61` | `ExitInputMode` resolves the box once but `StyleFocusedSurface` re-walks the visual tree per Esc |
| N72 | nit | `MyExtension/Navigation/Utils/WindowFrameAdapter.cs:198` | `_loggedEmptyRect` is per-adapter-instance but adapters are recreated per focus change — n19 log spam |

## Detailed findings

### N1/N2 (critical) — the FocusGuard leak-guard tests are tautological

- **Where:** `tests/NeoVisual.Tests/Program.cs:1038-1047` (`Run_FocusGuard_InputModeBlocksActionKeys`), `:1049-1060` (`Run_FocusGuard_ZeroActionKeysBlocks`).
- **What's wrong:** Both tests assert a **constant-false** expression. N1: `isInputMode = true`, so `FocusGuard.ShouldRouteToolWindowKey(...) && !isInputMode` is `X && false` = always false → `Assert.False` always passes. N2: `actionKeyCount = 0`, so `... && actionKeyCount > 0` is `X && false` = always false → `Assert.False` always passes. The comments even say "the deleted `HasToolWindowActionKeys` was exactly this" — the tests were written to pin the composite pre-filter decision, but the composite term is constant-false, so `FocusGuard`'s return value is never exercised. The hub verified both pass in the current 163-test run.
- **Why it bites:** These are the guard tests for the **user-reported action-key leak** (the `ljoljoljoljoljo` storm) that `FocusGuard` exists to prevent. A regression that makes input-mode or zero-action-key tool windows leak keys into the editor would pass the suite silently — the exact false-confidence class `audit-verification-gates` warns about. The e2e `neovisual-explorer-move-editor-focus` scenario is the only remaining guard, and it is QUEUED-not-run (no VS boot on this machine).
- **Fix:** Assert the guard's own return value directly: `Assert.False(FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: true, isTextInputSurface: false, textInputSurfaceFocused: false))` and drop the constant-false `&& !isInputMode` / `&& actionKeyCount > 0` terms (the action-key-count gate lives in the caller, not in this assertion).

### N3 (major) — Vim buffer-subscription lifecycle still leaks (R3 was partial)

- **Where:** `MyExtension/Vim/Utils/VimBufferSubscriptions.cs:52` (`_bufferToTextBuffer[buffer] = textBuffer` written in `Attach`, never removed), `:119-135` (`DecrementRefCount` removes `_textBufferRefCounts` at 0 but never `_bufferToTextBuffer`); `MyExtension/Vim/Utils/VimModeSource.cs:215-239` (`SubscribeBuffer` early-returns on `!_subscribedTextBuffers.Add(textBuffer)` before `MarkClosedSubscribed(buffer)`).
- **What's wrong:** Three lifecycle gaps: (1) `_bufferToTextBuffer` grows one entry per unique buffer for the whole VS session — never cleaned; (2) a shared-text-buffer second view (split view) never gets a `Closed` subscription, so when it closes `Detach` finds `_closedSubscribed.Remove(buffer)` false and never decrements the refcount — `_textBufferRefCounts[T]` stuck at 1 and the `SwitchedMode` subscription permanently attached; (3) `Detach` only decrements when the buffer was Closed-subscribed, so a shared-buffer view's refcount is permanently inflated.
- **Why it bites:** This is the VsVim interop surface (the highest-blast-radius fragile surface per prior reports). A stale `_currentTextBuffer` can mis-route a later `SwitchedMode` event into the wrong view's mode state — the exact silent mode-detection degradation class F5/m39/m40 were built to prevent. The map also grows unboundedly per closed-but-open document.
- **Fix:** Remove `_bufferToTextBuffer[buffer]` when the refcount hits 0 (in `DecrementRefCount`/`Detach`), and decrement in `Detach` regardless of `_closedSubscribed` membership (a shared-text-buffer second view is never Closed-subscribed).

### N4 (major) — block-caret deactivation never removes the adornment

- **Where:** `MyExtension/Adornments/BlockCaretAdornment.cs:69-82` (`Active` setter), `:111-148` (`Update`).
- **What's wrong:** `Active=false` calls `Update()`, but `Update()` early-returns at `:121-124` (`if (!_active) return;`) **before** `_layer.RemoveAdornmentsByTag(AdornmentTag)` at `:127`. The comment at `:118-120` claims "the deactivation path already removed it", but no deactivation path removes it — the only removal is inside `Update()` when `_active` is true. So the last-drawn white block stays glued to the last caret position over the editor's native line caret.
- **Why it bites:** When the user leaves normal mode (insert mode, or the view loses focus via `OnLostFocus` → `Active=false`), the white block caret persists on screen — the exact visual bug the feature exists to prevent — and the `[NeoVisual] block-caret active=False` diagnostic lies (the block is still drawn). No e2e assertion covers the block's disappearance, so the harness can't catch it.
- **Fix:** In the `Active` setter's false branch (or in `Update()` before the `!_active` early-return), call `_layer.RemoveAdornmentsByTag(AdornmentTag)` so deactivation actually clears the adornment.

### N5 (major) — `TryGetScreenRect` ignores the HRESULT (silent empty/garbage rect, no n19)

- **Where:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs:219`.
- **What's wrong:** `frame4.GetWindowScreenRect(...)`'s return HRESULT is discarded; the method returns `new WindowRect(left, top, width, height)` unconditionally. A failed COM read yields either a zeroed rect (silently filtered by `!IsEmpty` with NO n19 `window rect unavailable` diagnostic — violating the documented log contract) or partially-written non-zero out-params that become a **phantom candidate**.
- **Why it bites:** A stale/torn-down frame's failed read is indistinguishable from a genuinely empty rect, and a partially-written rect passes the `!IsEmpty` filter and can be selected as a navigation target — navigation can move to a hidden/wrong window with no log trail. This is the hole in the slice's fault-isolation story (R4/m46).
- **Fix:** Check the HRESULT; on failure return `null` so `RefreshRect` emits the n19 diagnostic and degrades to `WindowRect.Empty`.

### N6 (major) — `NavigationSettings.Invalidate()` is dead code (stale DPI divide tolerances)

- **Where:** `MyExtension/Navigation/Utils/NavigationSettings.cs:31` (verified: `callers_of("proxy.unresolved:NavigationSettings.Invalidate")` = `[]`).
- **What's wrong:** `FromSystemDpi` fills the `_cached` static from `DpiAwareness.SystemDpiX` once and caches forever; `Invalidate()` — the seam the R30 comment claims is the DPI-change hook — has zero callers.
- **Why it bites:** After a mid-session scaling change (monitor re-plug, Windows scaling), `XDivide`/`YDivide` stay at the old DPI, so `SelectTarget`'s divide window `[minGap, minGap+divide]` admits wrong windows → navigation activates the wrong window or no-ops.
- **Fix:** Wire `Invalidate()` to a real DPI-change signal in the package/hook, or drop the cache and re-read `SystemDpiX` per navigation (cheap, removes the stale state entirely).

### N7 (major) — Properties-window quirk implemented twice

- **Where:** `MyExtension/Navigation/Utils/WindowFrameUtils.cs:94-102` (`CompareWindows`) vs `MyExtension/Navigation/Utils/WindowFrameAdapter.cs:135-142` (`LinkedTo`'s `WindowKey` key-set path).
- **What's wrong:** The subtle Properties-window matching rule (caption-equal + ToolWindow↔Properties cross-type match) is implemented twice with two different comparison strategies.
- **Why it bites:** A future change (case-insensitive caption, extra type pair) fixed in one place silently breaks the other, producing wrong linked-group/active-window matches during navigation — the exact drift class that already bit the caret rendering once.
- **Fix:** Extract one `MatchesPropertiesQuirk(caption, type, otherType)` helper (or a single `WindowKey`-based comparer) and have both `CompareWindows` and `LinkedTo` call it.

### N8 (major) — `Run_FileContentCache_EvictsOldest` clock-tick flake

- **Where:** `tests/Telescope.Tests/Program.cs:2504-2516`.
- **What's wrong:** All three cache entries use `DateTime.UtcNow` as the timestamp; on a Windows clock tick (~15ms resolution) "a" and "b" share a key, so eviction of "a" is not guaranteed and `Assert.Equal(before + 1, reads)` flakes. The sibling test `Run_FileContentCache_CachedRead` (2467-2469) explicitly documents this exact flake class and uses a fixed timestamp — this test does not.
- **Why it bites:** A slow CI machine can intermittently fail the suite on a non-failure, burning verify budget on the item's final gate.
- **Fix:** Inject a fixed/incrementing timestamp (`timestamp: _ => fixedTime.AddSeconds(i++)`) so "a"/"b"/"c" have distinct, deterministic keys.

### N9 (major) — `telescope-open`/`-mode`/`-navigate` assertions are false-positive gates

- **Where:** `tools/harness/test-e2e.ps1:514-520` (`telescope-open`), `:583` (`telescope-mode`), `:544` (`telescope-navigate`).
- **What's wrong:** `Reset-LogBaseline` runs BEFORE `Open-Telescope`, and `Open-Telescope` itself waits for `Focus prompt => True, mode=insert` before returning (per AGENTS.md). So the Open-Telescope line is inside the fixed search window and satisfies the scenario's `Assert-NewLogLine 'Focus prompt => True, mode=insert'` — the scenario would pass with its assertion deleted.
- **Why it bites:** The scenario cannot fail on the exact regression it guards. A re-introduction of a prompt-focus/mode bug would pass the e2e suite silently.
- **Fix:** Assert something the helper does NOT already confirm (e.g. a fresh `key=... mode=insert handled=True` line after an explicit Escape), or drop the redundant assertion and rely on `Open-Telescope`'s internal throw.

### N10 (major) — `check-doc-content.ps1` lint is RED (3/12)

- **Where:** `tools/lint/check-doc-content.ps1:131` (DOC-64-3), `:156` (DOC-66-2), `:162` (DOC-66-3). Verified by running it: `DOC-CONTENT CHECK: FAIL (RED)`.
- **What's wrong:** The three assertions are hardcoded to the Phase-10 (98-findings plan) fixed state, but `docs/progress.md` has advanced to the 51-findings plan (GREEN 2026-10-02): "Current state" cites `E2E-NCR-*` (wildcard) instead of the concrete `E2E-NCR-1` the check greps for; "Next up" points at F13/F43; the baseline is attributed to "Code review fixes (51 findings)" which matches neither the check's `wrongAttr` nor `rightAttr` regexes.
- **Why it bites:** The workflow treats this lint as a harness-health gate — `docs/progress.md:473` records "check-doc-content 12/12" PASS for the 98-findings plan, so the recorded PASS is now false and any future gate run fails on a stale assertion, masking real doc drift.
- **Fix:** Update the three assertions to the current 51-findings state (or retire the Phase-10-specific check once its plan is closed); make DOC-64-3 accept the `E2E-NCR-*` wildcard or the concrete E2E-CR51 IDs.

### N11–N55 (minor) — see the findings table; highlights:

- **N11/N12** — navigation robustness: `_cachedLinked` is keyed only on the adapters-list reference (correctness depends entirely on `WindowManager.GetWindowAdapters` returning a NEW list per focus change), and the active window's rect is never validated (an empty active rect anchors `SelectTarget` around the screen origin).
- **N17** — the FocusGuard editor-veto logic lives in two places (the 5-arg overload and the 6-arg shift-gated variant) — the exact class of bug that caused the original action-key leak.
- **N19** — R18's caret-relative slice was applied only to the editor-view path; WPF/WinForms text surfaces still pay an O(n) whole-buffer copy + `LineIndex` build per h/l/w/b/e key.
- **N22** — `FocusKeeper.Run`'s handle is ignored by both callers; a superseded keeper's already-queued tick can still re-assert the old tree target (the R8 race is only partially defended).
- **N23** — editor-view `A` (InsertEnd) lands at the caret-relative slice boundary, not the true line end, on lines longer than ~4096 chars past the caret — typing inserts in the wrong place.
- **N24** — `editor-view-opened` is emitted for every `IWpfTextView` (split/peek/preview) and can be double-emitted by `SelectFirstSourceFile` + `TextViewCreated` — the harness's per-scenario "wait for a new `editor-view-opened` line" can be satisfied by a spurious emission.
- **N28** — `GetVim` re-queries MEF + re-logs `VsVim integration: not found` on every view open / focus gain when VsVim is absent — unbounded `[NeoVisual]` log spam in the pane the harness scans.
- **N31** — `'a'` in the prompt maps to `CaretPlacement.Current` (no motion), so it behaves identically to `i` — a real caret-placement deviation from the documented "a = after caret" and from the tool-window surfaces.
- **N32/N44** — the grep finder scans every project file synchronously on the UI thread per debounced query (the content cache is cold each overlay open), and `ProjectFileCache` is invalidated only on solution-name change — added/removed files stay stale.
- **N39** — `FilterAsync` never checks the cached `_availability`, so when fzf is missing every keystroke attempts `p.Start()` (Win32Exception) and logs `fzf filter failed` — log spam + a wasted process spawn per keystroke.
- **N40** — `PaneFailureTracker._retryAllowed` is always true — the "retry latch" is dead logic and the `!ShouldRetry()` guard in `NeoVisualLog.EnsurePane` is unreachable.
- **N41** — `FilterFailureLog.Format` returns an UNPREFIXED string relying on the caller to log via `TelescopeLog` — a caller using the wrong logger silently breaks the harness's `filter failed:` assertion contract.
- **N42/N43** — user-controlled text (Error List descriptions, prompt queries, display strings) interpolated into log lines — `\n`/control chars split the line and break `Wait-NewLogLine`'s line-based assertions.
- **N45** — the hand-rolled `QuoteArg` mis-escapes backslashes-before-quotes (Windows argv rules) — a query like `C:\` filters against the wrong string.
- **N46–N50** — test-quality: duplicate assertions, a byte-identical duplicate test, near-duplicate action-registry tests, a vacuously-true `All(...)` assertion, and a reflection-invoking test that mutates a static `ThreadHelper` field (never restored).
- **N51** — coverage gap: `TextMotionHelper.TryMoveFocusedSurface` and `FocusKeeper` have no unit test — verified only by the live harness.
- **N52/N53** — harness: `neovisual-explorer-open`/`-open-o` use a "try until ANY file opens" loop (order-dependent, doesn't pin WHICH file), and `neovisual-editor-insert` updates the seed-expected copy in a `finally` even on failure (weakening the seed-leak guard).
- **N54/N55** — docs: `architecture-review.md` still presents F1/F16/F45 as open (all fixed), and `code-review.md`'s R50 premise is no longer true.

### N56–N72 (nit) — see the findings table. Highlights: three near-identical `KeybindingConfig` load methods (N56); unused `using System.Diagnostics` (N57); the `[Hook]` prefix contract in two places (N58); `_dte` non-readonly + dead null checks (N59); stale "window matrix" doc comment (N60); near-identical DTOs (N61); the `_actions` dict pattern duplicated across controllers (N62); `FilterFailureLog` over-engineering (N63); `SyntaxHighlighter.Tokenize` complexity 23 + per-token Substring (N64); every log line duplicated to the debug file (N65); `Down()`/`Up()` edge behavior (N66); `$@"..."` not colored (N67); `OpenHit` logs a successful open that never happened (N68); null-forgiving `!` on `_launcher` (N69); `ParseLeader` accepting modifier keys (N70); `ExitInputMode` re-walking the visual tree (N71); `_loggedEmptyRect` per-adapter log spam (N72).

## Cross-cutting (slice F — hub-conducted)

- **Duplication between slices:** the big clusters are resolved (`TextMotionDispatcher` single dispatch, `BlockCaretStyle` single caret renderer, `KeyToArrowVk` single-sourced, `GetAsyncKeyState` declared once). Remaining: the FocusGuard veto in two overloads (N17), the Vim subscription bookkeeping split across `VimBufferSubscriptions` + `VimModeSource` (N3), the three mtime-keyed caches (N33), the Properties-window quirk (N7), the two `MapKey` tables (N36), the tree-walk trio `HierarchyResolver`/`HierarchyWalker`/`HierarchyForestBuilder` (N18, N61), and the `_actions` dict pattern (N62).
- **net472/BCL consistency:** clean — no `IReadOnlySet<T>`/modern-BCL usage anywhere (all workers verified).
- **Namespace/folder hygiene:** clean post-restructure except the stale "window matrix" doc comment (N60).
- **Log-format drift across the two projects:** the remaining drift points are `FilterFailureLog` unprefixed (N41), user-controlled text in log lines (N43), multi-line Error List displays (N42), `editor-view-opened` non-determinism (N24), and the debug-file duplication (N65); the `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` prefix contract is otherwise centralized in `DiagnosticLog`.
- **Hook-path cost:** `KeyNameBuilder` allocation (N30) is the remaining hot-path concern; the shift handling (R11) and `InjectedKeyGuard` TTL (R12) are verified fixed; the per-key `[Hook]` log stays fixed.

## Verified-clean (checked this run, no action)

- **Prior 51-findings plan:** every prior finding in scope is verified FIXED in current code — R1 (prompt j/k/g/G now reach `OverlayKeyHandler`; a/A/I placement routing via `PromptMotionRouter`), R2 (preview render amortized), R3 (VimModeSource Closed-subscription + refcount — **partial**, see N3), R4 (frame fault isolation — **partial**, see N5), R5 (telescope-mode gate — **partial**, see N9), R6/R14/R24 (dead code + redundant test deleted), R7 (null-frame state reset), R8 (FocusKeeper disposable — **partial**, see N22), R10/R11 (shift gating), R12 (InjectedKeyGuard TTL-at-record), R15 (Properties-window single-source — **partial**, see N7), R16 (HRESULT + n19 — **partial**, see N5), R17/R18 (focused-box reuse + caret-relative slice — **partial**, see N19), R20 (instance-scoped default-controller cache), R21 (BlockCaretAdornment focus-loss handler — **partial**, see N4), R22 (fzf debounce), R25–R27/R50/R51 (docs drift).
- **net472 compliance:** no modern-BCL APIs anywhere in `MyExtension/` or `Telescope/`.
- **Build:** `dotnet build` 0 errors (per the 51-findings GREEN record; no build was re-run this session — the review is read-only).
- **Unit suites:** Telescope.Tests 153, NeoVisual.Tests 163 — both pass (verified by the test-quality-reviewer via `--list` + full runs).

## Recommendations (ordered by effort/impact)

1. **Fix the two tautological FocusGuard tests (N1/N2) first** — assert the guard's return value directly. Small, closes the false-confidence hole on the security-critical leak guard.
2. **Fix the block-caret deactivation bug (N4)** — remove the adornment on `Active=false`. Small, user-visible visual bug with a lying diagnostic.
3. **Harden the harness gates (N9)** — make `telescope-open`/`-mode`/`-navigate` assert a post-tap line the helper does not confirm. Small harness change.
4. **Fix the Vim buffer-subscription lifecycle (N3)** — clean `_bufferToTextBuffer` at refcount 0 and Closed-subscribe the split-view second buffer. The VsVim interop is the highest-blast-radius fragile surface.
5. **Close the navigation fault-isolation holes (N5, N6, N11, N12)** — check the HRESULT (emit n19), wire `Invalidate()` to a DPI-change signal, key `_cachedLinked` on the active window, validate the active rect.
6. **Fix the RED harness-health gate (N10)** — update `check-doc-content.ps1` to the 51-findings state so the recorded "12/12 PASS" is true again.
7. **Delete the redundant/tautological tests (N46–N50)** and fix the flaky `Run_FileContentCache_EvictsOldest` (N8).
8. **Tighten the hot path + UI-thread stalls (N19, N30, N32, N38, N39)** — slice the WPF/WinForms text paths, cache the `KeyNameBuilder` output, offload the grep scan + fzf probe.
9. **Reconcile the docs (N10, N54, N55)** — architecture-review F1/F16/F45 annotations, code-review R50 closure, check-doc-content assertions.

## Filed into progress.md

**Nothing filed** — per the user's decision (Step 4, 2026-10-02): all 72 findings stay in this report and the user passes it directly to the planner. No append was made to `docs/progress.md` (owned by `neovim_hub`). Cross-references to `docs/reviews/architecture-review.md`: F5→N3 (VimModeSource subscription, now the `_bufferToTextBuffer`/split-view variant), F7→N5 (one bad frame kills navigation, now the HRESULT variant), F8/F9→N38/N39 (fzf per-keystroke spawn + `IsAvailable` UI block), F10→(fixed, preview render amortized), F14→(documented/accepted n16, re-verified still live). Prior-report regressions re-reported here: R3→N3 (partial), R4→N5 (partial), R5→N9 (partial), R8→N22 (partial), R15→N7 (partial), R16→N5 (partial), R17→N19 (partial), R21→N4 (partial).
