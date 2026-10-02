# MyExtension — Code Review (code-review-hub)

- **Date:** 2026-10-01
- **Scope:** whole repo — `MyExtension/` (core + `Navigation/` + `ToolWindows/`), `Telescope/`, `tests/`, `tools/`, and the workflow docs. **Post-fix review** of the 2026-09-30 combined plan (67 findings + restructure, commit `92b119f`, GREEN in the unit-only lane; e2e gates queued).
- **Method:** 10 parallel workers (4 `arch-auditor` + 4 `code-review-worker` + 1 `test-quality-reviewer` + 1 `docs-accuracy-reviewer`), one per slice (A core / B Navigation / C ToolWindows / D Telescope / E tests+tools / docs). A whole-repo `trailmark-recon` digest (1763 nodes, 717 proxies, 0 entrypoints) was shared with every worker; each structural claim was verified with Trailmark (`QueryEngine.from_directory(..., language="c_sharp")`, proxy-aware `callers_of`). The hub spot-verified every major claim against current code (the `a`/`A`/`I` prompt interception, the `GrepFinder` UI block, the `Send-Text` Shift bug, and the `FocusGuard` two-source drift). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.

## Summary

**98 findings: 0 critical, 8 major, 69 minor, 21 nit.** The 67-findings fix landed cleanly and the pure seams hold — but this run found **four real regressions/not-fixed items from the prior report** (M4 GrepFinder UI block, M5 preview re-tokenize, M9 null-activeWindow dead guard, M22 FocusGuard two-source drift) plus **one new functional bug with a false-positive e2e gate**: in the Telescope prompt, `a`/`A`/`I` are intercepted by `TryPromptMotion` as caret motions and never enter insert mode — the `telescope-mode` scenario's `a` assertion passes only because the harness's fixed-baseline search satisfies it with the Open-Telescope `Focus prompt => True, mode=insert` line. The harness also has a **case-fidelity bug**: `Send-Text` never applies Shift for uppercase letters, so every "types `Program`" assertion actually types `program` and passes only via PowerShell's case-insensitive `-match`.

The sharp edges are the same **hot-path and scalability clusters**, now re-verified: (1) the **finder path still stalls the UI** — `GrepFinder` blocks the UI thread with `Task.Run(...).GetAwaiter().GetResult()` for the whole scan, and the preview re-tokenizes + rebuilds the `FlowDocument` per selection change; (2) a **dead-code / doc-drift cluster** — `InputHandler.HasToolWindowActionKeys` (3 workers agree it has zero production callers while AGENTS.md/SKILL.md document it as the pre-filter mechanism), `VimModeState.ResolveOnce`, `WindowAdapter._frame`, and a stale `VimBufferSubscriptions` doc comment claiming the CR2 wiring is "not done"; and (3) a **docs/queue drift** — `docs/e2e-queue.md` contains none of the E2E-NCR-*/E2E-RESTRUCTURE-1 gates the 67-findings plan claims are queued there, so the restructure's only behavior-preservation gate is silently lost.

## Findings table

| id | sev | file:line | problem |
|----|-----|-----------|---------|
| M1 | major | `Telescope/Overlay/TelescopeOverlay.cs:562,583` | `a`/`A`/`I` in the prompt are intercepted by `TryPromptMotion` as caret motions and never enter insert mode; the `telescope-mode` e2e `a` assertion is a false positive |
| M2 | major | `Telescope/Finders/GrepFinder.cs:109-121` | `Task.Run(...).GetAwaiter().GetResult()` blocks the UI thread for the whole full-solution scan (M4 regression) |
| M3 | major | `tools/harness/harness-common.ps1:121-134` | `Send-Text` never applies Shift for uppercase letters — types lowercase; e2e assertions pass only via case-insensitive `-match` |
| M4 | major | `Telescope/Overlay/PreviewRenderer.cs:36,75` | Preview re-tokenizes + rebuilds the `FlowDocument` on every selection change (mtime cache only avoids the disk read) — M5 partial |
| M5 | major | `MyExtension/Input/InputHandler.cs:104-109` vs `:84-90` | FocusGuard text-input keyboard-ownership exemption computed twice with different sources — M22 not fixed |
| M6 | major | `docs/progress.md:16,205,401` + `docs/e2e-queue.md` | The 67-findings plan's deferred e2e gates (E2E-NCR-*, E2E-RESTRUCTURE-1) are claimed "queued in e2e-queue.md" but the file has no such entries — the restructure's e2e verification is lost |
| M7 | major | `tests/Telescope.Tests/Program.cs:554-608` | `Run_FzfFilter_TimeoutKillsAndFallsBack` is timing-dependent (GC-poll for `UnobservedTaskException` + 5s wall-clock bound) — flaky/false-pass |
| M8 | major | `MyExtension/ToolWindows/SolutionExplorerController.cs:331` | `pi.FileNames[(short)pi.FileCount]` indexes the LAST file of a multi-file project item — `g` opens the wrong file (e.g. `Form1.resx`) |
| m1 | minor | `MyExtension/Input/InputHandler.cs:79-92` | `HasToolWindowActionKeys` dead code (3 workers agree); AGENTS.md/SKILL.md document it as the hook pre-filter mechanism |
| m2 | minor | `MyExtension/Vim/VimModeState.cs:27-28,90-109` | `ResolveOnce` + `_resolved` latch dead — `VsVimModeSource.GetVim` implements its own latch (M17 partial) |
| m3 | minor | `MyExtension/Vim/VimBufferSubscriptions.cs:16-19` | Stale doc comment claims the CR2 wiring is "NOT wired yet" — it IS wired (Attach/Detach call it) |
| m4 | minor | `MyExtension/Package/MyExtensionPackage.cs:423-696` | ~270 lines of Roslyn/VS-coupled gatherer logic with no test seam; `IsWriteLocation` reflection untested |
| m5 | minor | `MyExtension/Package/MyExtensionPackage.cs:665-675` | `ReadLine` re-opens + re-scans the file prefix per reference hit (O(hits × line) file I/O) |
| m6 | minor | `MyExtension/Hooks/GlobalKeyboardHook.cs:198-202` | `Log` prepends its own timestamp, then `LogFileWriter.FormatLine` prepends a second — every `[Hook]` line is double-stamped |
| m7 | minor | `MyExtension/Input/InputHandler.cs:308,436,468` | The identical 5-arg `FocusGuard.ShouldRouteToolWindowKey` call repeated 3× |
| m8 | minor | `MyExtension/Input/InputHandler.cs:124` + `MyExtension/Package/MyExtensionPackage.cs:84` | Two `TelescopeLauncher` instances for the same `TelescopeController` |
| m9 | minor | `MyExtension/ToolWindows/GeneralToolWindowController.cs:66-73` + `TextMotionHelper.cs:117` | Key→arrow-VK mapping implemented twice (both emit `toolwindow-move key=... -> arrow vk=...`) |
| m10 | minor | `MyExtension/Navigation/WindowAdapter.cs:18,25` | `_frame` field assigned but never read (only `_frame4` used) |
| m11 | minor | `MyExtension/Navigation/WindowNavigationEngine.cs:24-64` | `SelectTarget` runs two full passes re-evaluating all 3 predicates + `GapTo` per candidate |
| m12 | minor | `MyExtension/Navigation/WindowMatrix.cs:39` | `NavigationSettings.FromSystemDpi()` re-reads system DPI on every navigation |
| m13 | minor | `MyExtension/Navigation/WindowAdapter.cs:94` | `LinkedTo` re-filters every adapter with O(n·m) `CompareWindows` COM property reads |
| m14 | minor | `MyExtension/Navigation/UtilityMethods.cs:21` | `GetIVsUIShell` no null check → NRE escapes `WindowMatrix`'s catch, misdiagnosed as a binding failure |
| m15 | minor | `MyExtension/ToolWindows/TextMotionHelper.cs:82,149,158` | Per motion key: 2 full buffer copies + up to 3 visual-tree walks |
| m16 | minor | `MyExtension/ToolWindows/TextInputToolWindowController.cs:44` + `SolutionExplorerController.cs:50-52` | W/B/E vim-motion action wiring duplicated verbatim across the two text controllers |
| m17 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:46` | H/L hardcode `VK_LEFT`/`VK_RIGHT` instead of reusing `KeyToArrowVk` |
| m18 | minor | `MyExtension/ToolWindows/WindowManager.cs:131` + `TextMotionHelper.cs:241-263` | `GetParent` visual/logical-tree walker duplicated verbatim |
| m19 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:262` + `Telescope/Finders/ProjectFiles.cs:21-33` | Two parallel DTE tree-walk seams (`HierarchyForestBuilder` vs `HierarchyWalker`) over the same hierarchy |
| m20 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:234` | `FocusKeeper.Run(..., 1500, ...)` literal vs the `FocusKeeperDurationMs = 1500` constant |
| m21 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:119,234` | FocusKeeper re-asserts `View.SolutionExplorer` on every tick with no cancellation on window close |
| m22 | minor | `MyExtension/ToolWindows/WindowManager.cs:25,186-190` | Shared `_defaultController` leaks `_isInputMode` across all unknown-GUID tool windows |
| m23 | minor | `MyExtension/ToolWindows/GeneralToolWindowController.cs:31` | Ctor `_isInputMode = IsTextInputType(type)` branch dead (never constructed for text-input types) |
| m24 | minor | `MyExtension/ToolWindows/TextMotionHelper.cs:98` | Editor-view path passes `styleCaret: false` — block caret persists in insert mode after `a`/`A`/`I` |
| m25 | minor | `MyExtension/ToolWindows/SolutionExplorerController.cs:137,248,376` | try/catch + `NeoVisualLog.Log(...failed: {msg})` swallow idiom repeated 3× with different prefixes |
| m26 | minor | `MyExtension/ToolWindows/HierarchyResolver.cs:22` | `FirstSourceFilePath` returns the first physical file regardless of extension (`.cs` filter lives only in the forest builder) |
| m27 | minor | `Telescope/Finders/FileContentCache.cs:30` | LRU `maxEntries` cap never passed by any production caller — caches grow unbounded (M13 partial) |
| m28 | minor | `Telescope/Overlay/TelescopeOverlay.cs:240,331` | `OverlayClosed` event declared + raised but never subscribed |
| m29 | minor | `Telescope/Finders/DteFileOpener.cs:19` | Manual `[Telescope] ` prefix concat instead of `TelescopeLog.Log` |
| m30 | minor | `Telescope/Finders/FileFinder.cs:78` | Double `File.Exists` (outer guard + `HitOpener`) |
| m31 | minor | `Telescope/Overlay/TelescopeOverlay.cs:585,498` | Fresh `new TextMotionNavigator()` per keystroke in `TryPromptMotion`/`ApplyInsertCaret` |
| m32 | minor | `Telescope/Overlay/TextMotionNavigator.cs:37` | `LineNumber` O(n) scan on every access, read on every preview keystroke log |
| m33 | minor | `Telescope/Overlay/ResultMapper.cs:21` | `MapBack` rebuilds `GroupBy(...).ToDictionary(...)` over the whole snapshot per keystroke |
| m34 | minor | `Telescope/Finders/FileFinder.cs:62` | `GatherHits` calls `ProjectFiles.Enumerate` directly — no shared `ProjectFileCache` |
| m35 | minor | `Telescope/Overlay/TelescopeOverlay.cs:603-613` | `ApplyPromptCaretStyle` duplicates `TextMotionHelper.ApplyCaretStyle` |
| m36 | minor | `Telescope/Overlay/TelescopeOverlay.cs:516,486` | `EnterInsert(Current)` calls `FocusPrompt` which resets `CaretIndex` to end — `i` discards the caret position |
| m37 | minor | `Telescope/Filter/FzfFilter.cs:95` | fzf subprocess spawned per keystroke (off-thread now, but the named bottleneck; `--listen` documented but not scheduled) |
| m38 | minor | `MyExtension/Vim/VimModeSource.cs:419-421` | `GetVim` sets `_resolved = true` even when `vim` is null — permanently latches "no VsVim" on an early resolution |
| m39 | minor | `MyExtension/Vim/VimModeSource.cs:117-125,177-183` | `Attach` unconditionally re-points `_currentBuffer` — a background/peek view hijacks the focused editor's mode state |
| m40 | minor | `MyExtension/Vim/VimModeSource.cs:128-136` | `Detach` unsubscribes the text buffer with no reference counting — two views sharing a buffer kill each other's subscription (CR2 incomplete) |
| m41 | minor | `MyExtension/Vim/VimModeState.cs:57-67,73-83` | `OnViewLostFocus`/`OnViewClosed` return typing-change not mode-change — `vim-mode=Unknown` never emitted on editor-focus loss |
| m42 | minor | `MyExtension/Input/InputHandler.cs:181-188` | `BuildBindings` classifies any binding key containing "+" as a simple shortcut — a leader key like `F,+` can never match |
| m43 | minor | `MyExtension/Hooks/InjectedKeyGuard.cs:42-67` | Per-VK counter with no TTL — a stale pending record consumes the next physical key-down of that VK |
| m44 | minor | `MyExtension/Navigation/WindowMatrix.cs:47-58` | `activeWindow` dereferenced before the null check — the graceful-degradation branch is dead (M9 not fixed) |
| m45 | minor | `MyExtension/Navigation/WindowAdapter.cs:42,48` | `Activate()`/`AutoHides()` deref `_dte` with no null guard |
| m46 | minor | `MyExtension/Navigation/WindowMatrix.cs:94` | `_activeWindows.Select(w => w.Rect).ToList()` has no per-window fault isolation — one stale frame kills all navigation |
| m47 | minor | `MyExtension/Input/InputHandler.cs:498` | No outcome diagnostic for navigation — `navigate direction=...` fires regardless; a no-op passes the harness |
| m48 | minor | `MyExtension/Navigation/WindowNavigationEngine.cs:85-86` | Down/Up tolerance asymmetry (`> 1` vs `<`) — a 1px-gap stacked layout skips Down but not Up |
| m49 | minor | `MyExtension/ToolWindows/WindowManager.cs:240` | `VSFPROPID_Type` HRESULT ignored; `(int)value` NREs if the call fails |
| m50 | minor | `Telescope/Filter/FzfFilter.cs:64` | `IsAvailable()` blocks the UI up to 500ms on every overlay open |
| m51 | minor | `Telescope/Logging/NeoVisualLog.cs:172` | `EnsurePane` catch leaves `_paneInitTried = true` — a transient `CreatePane` failure permanently disables the Output pane (M12 partial) |
| m52 | minor | `Telescope/Overlay/TelescopeOverlay.cs:616` | `HandlePreviewKey` routes `a`/`A`/`I` in the read-only preview (insert placements move the caret) |
| m53 | minor | `tests/Telescope.Tests/Program.cs:942-965` | `Run_Preview_UpFromSecondLineWithLeadingBlankLine` and `_Fixed` are byte-identical duplicates |
| m54 | minor | `tests/Telescope.Tests/Program.cs:2180-2206` | `Run_GrepFinder_GatherHitsThrowsNotSupported` name/body mismatch — never exercises the throw |
| m55 | minor | `tests/NeoVisual.Tests/Program.cs:1390-1404` | `Run_WindowAdapter_TryGetScreenRect_...` never calls `TryGetScreenRect` |
| m56 | minor | `tests/NeoVisual.Tests/Program.cs:1576-1599` | `Run_LeaderMatcher_PrefixSetBuiltOnce` asserts a private field via reflection (implementation coupling) |
| m57 | minor | `tests/Telescope.Tests/Program.cs:482-500` | `Run_FzfFilter_FilterMatchesPrefix` requires fzf on PATH (Mystery Guest) + weak presence assertion |
| m58 | minor | `tests/Telescope.Tests/Program.cs:630-648` | `Run_FzfFilter_IsAvailableBounded` asserts `< 1s` wall-clock — flaky under load |
| m59 | minor | `tests/NeoVisual.Tests/Program.cs:624-634` | `SolutionExplorerController.SelectFirstSourceFile` (`g`) never unit-tested |
| m60 | minor | `tools/harness/harness-common.ps1:226-242` | `Resolve-VsRoot` hardcodes Community-only paths before the vswhere fallback |
| m61 | minor | `tools/lint/check-doc-refs.ps1:65-105` | External allowlist permanently masks any future reference to removed symbols (`DistinctBy`, `CardinalMovment`, ...) |
| m62 | minor | `tests/TestRunner.cs:60` | `method.Invoke(null, null)` discards the return value — a future async test would silently pass |
| m63 | minor | `tools/harness/test-e2e.ps1:822-874,989-1062` | Scenarios share one live VS instance and are order-dependent by design — subsets are not independently runnable |
| m64 | minor | `tools/harness/test-e2e.ps1:1161` | `results count=\d+ selected=0` matches `count=0` (M27 partial) |
| m65 | minor | `tools/harness/harness-common.ps1:164-183` | `Wait-LogLine` joins the whole tail + `-match`es it — can match across line boundaries; O(assertions × tail) |
| m66 | minor | `docs/progress.md:10` | Header "Chunk C restructure pending" contradicts the Done entry (Chunk C COMPLETE) |
| m67 | minor | `docs/progress.md:20-28` | "Next up" still lists F5/F8/F9 as open — covered by the now-GREEN combined plan |
| m68 | minor | `docs/spec.md:186` | §4 diagnostics contract omits 4 harness-asserted lines (`open finder=`, `Focus prompt => True, mode=insert`, `results count=... selected=...`, `key=... mode=... handled=...`) |
| m69 | minor | `docs/spec.md:47` | §2.2 key-files table omits 13 real seam files (OverlayShowState, FocusTargetModel, LineIndex, TryDispatch, PreviewRenderer, BlockCaretStyle, VimModeClassifier, InitSteps, SimpleShortcutMatcher, NavigationSnapshot, FilterFailureLog, TelescopeLog, PaneFailureTracker) — M31 partial |
| n1 | nit | `MyExtension/Vim/VimModeState.cs:22-24` | `Normal`/`Insert`/`Replace` const aliases duplicate `VimModeClassifier`'s values |
| n2 | nit | `MyExtension/Adornments/BlockCaretAdornment.cs:109` | `Update()` calls `RemoveAdornmentsByTag` even when `_active` is false |
| n3 | nit | `MyExtension/Navigation/WindowNavigationEngine.cs:10` | `Pipeline` `List<Func<...>>` abstraction for a 3-line filter |
| n4 | nit | `MyExtension/Navigation/Direction.cs:3` | `using System;` inside the namespace block |
| n5 | nit | `MyExtension/Navigation/WindowAdapter.cs:97-99` | `ExtractFrames` iterator — `ThrowIfNotOnUIThread` runs lazily on first `MoveNext` |
| n6 | nit | `MyExtension/Navigation/NavigationSnapshot.cs:9` | Heap class for a 2-field value — `readonly struct` would match `RectCoordinate` |
| n7 | nit | `MyExtension/ToolWindows/WindowManager.cs:11` | Class body unindented at column 0 |
| n8 | nit | `MyExtension/ToolWindows/FocusKeeper.cs:18` | Redundant `keeperRef` local |
| n9 | nit | `MyExtension/ToolWindows/WindowManager.cs:264` | `ComputeTextInputSurfaceFocused()` computed in the non-tool branch where it returns false immediately |
| n10 | nit | `MyExtension/ToolWindows/TextMotionHelper.cs:120` | Arrow-fallback log appends `focused=... hwnd=...` — differs from `TryMoveArrow`'s bare contract |
| n11 | nit | `Telescope/Overlay/TryDispatch.cs:12` | 25-line trivial wrapper — merge into `TextMotionDispatcher` |
| n12 | nit | `Telescope/Overlay/TelescopeOverlay.cs:441` | `RenderResults` rebuilds the whole result string + re-sets TextBox text per render |
| n13 | nit | `Telescope/Logging/NeoVisualLog.cs:28` | `PaneGuid` mutable static — should be `readonly` |
| n14 | nit | `MyExtension/Input/InputHandler.cs:496-505` | `Navigate` lacks a direct `ThrowIfNotOnUIThread()` |
| n15 | nit | `MyExtension/Input/InputHandler.cs:72` + `LeaderSequenceMatcher.cs:19` | `IsLeaderActive` reads a plain (non-volatile) bool — AGENTS.md documents a volatile |
| n16 | nit | `MyExtension/Input/PopupNavigation.cs:39-53` | Ctrl+N/P injected unconditionally with no popup-active check (documented design, UX footgun) |
| n17 | nit | `MyExtension/Package/MyExtensionPackage.cs:647-654` | `GetCaretOffset` DTE fallback uses `DisplayColumn` (tab-expanded) as a char offset |
| n18 | nit | `MyExtension/Hooks/GlobalKeyboardHook.cs:165-173` | `SetHook` calls `Process.GetCurrentProcess().MainModule` — can throw `Win32Exception` |
| n19 | nit | `MyExtension/Navigation/WindowAdapter.cs:118,131` | Frames without `IVsWindowFrame4` silently mapped to `RectCoordinate.Empty` with no diagnostic |
| n20 | nit | `docs/progress.md:73-74` | Baseline parenthetical attributes 143/140 to the consolidation (it was the 67-findings plan) |
| n21 | nit | `docs/reviews/architecture-review.md:417` | "Verified-clean" still claims `CardinalNavigation`/`CardinalMovment` — post-restructure it's `MyExtension.Navigation`/`Navigation/` |

## Detailed findings

### M1 (major) — `a`/`A`/`I` in the prompt never enter insert mode; the e2e gate is a false positive
- **Where:** `Telescope/Overlay/TelescopeOverlay.cs:562` (the `TryPromptMotion` gate), `:583-597` (`TryPromptMotion`); `Telescope/Overlay/TextMotionDispatcher.cs:89-92` (`MapKey` maps `a`→`InsertAfter`, `A`→`InsertEnd`, `I`→`InsertStart`); `Telescope/Overlay/TryDispatch.cs:14-23` (`Handle` returns true for any mapped motion).
- **What's wrong:** In normal mode with the prompt focused, `OnPreviewKeyDown` first calls `TryPromptMotion(e.Key)` (line 562). `TryDispatch.Handle(Key.A, shift, nav, out _)` maps `a` to `InsertAfter` and `Apply` returns true, so `TryPromptMotion` consumes the key (`e.Handled = true`) and `_keyHandler.Handle(MapKey(e.Key))` (line 571) — the `OverlayKeyHandler` path that would enter insert mode — never runs. Only bare `i` (which `MapKey` maps to null) falls through. The `OverlayKeyHandler` `a`/`A`/`I` actions (OverlayKeyHandler.cs:169-172) are unreachable dead code.
- **Why it bites:** A user pressing `a` in the prompt expects append-insert; instead the caret silently moves one char right and the prompt stays read-only. The `telescope-mode` e2e scenario's `a` assertion (`Assert-NewLogLine 'Focus prompt => True, mode=insert'`) is a **false positive**: `Wait-NewLogLine` searches from the fixed per-scenario baseline without advancing, so the Open-Telescope `Focus prompt => True, mode=insert` line (emitted after the baseline) satisfies it even though `a` never entered insert mode. The harness cannot catch this regression.
- **Fix:** In `TryPromptMotion`, exclude the insert placements — return false for `Key.A`/`Key.I` (or check `MapKey` for a placement before treating it as a prompt motion) so `a`/`A`/`I` fall through to `OverlayKeyHandler.Handle` and enter insert mode as documented. Add a unit test asserting `TryPromptMotion` does not consume `a`/`A`/`I`.

### M2 (major) — `GrepFinder` blocks the UI thread for the whole scan (M4 regression)
- **Where:** `Telescope/Finders/GrepFinder.cs:109-121`; caller `Telescope/Overlay/TelescopeOverlay.cs:366-378` (`RefreshQueryDrivenAsync`).
- **What's wrong:** `GetCandidates` runs on the UI thread (`ThreadHelper.ThrowIfNotOnUIThread()` at line 92) and then blocks it with `Task.Run(...).GetAwaiter().GetResult()` while scanning every project file. The comment (lines 105-107) claims the scan runs "on a background task and marshal only the hits back" — but `.GetResult()` blocks synchronously, so the `Task.Run` adds a thread-pool hop + blocking wait with zero responsiveness benefit.
- **Why it bites:** The 200ms debounce was built to avoid stalling the UI per keystroke, but each settled query still freezes the overlay for the full scan duration (bounded only by `HitCap=200`; a query matching nothing scans every file). On a large solution this is a visible multi-second freeze per query change — the exact cost `ProjectFileCache`/`FileContentCache` were built to amortize.
- **Fix:** Make the gather truly async (`IFinder.GetCandidates` → `Task<IReadOnlyList<FinderEntry>>`, awaited in `RefreshQueryDrivenAsync`), or drop the `Task.Run` and scan inline (same blocking, no wasted thread hop).

### M3 (major) — `Send-Text` never applies Shift for uppercase letters
- **Where:** `tools/harness/harness-common.ps1:121-134`.
- **What's wrong:** `$vk = [int][char]::ToUpper($ch)` taps the letter VK with no Shift; the `$shift` flag is only set for punctuation. So `Send-Text 'Program'` types `program` (lowercase).
- **Why it bites:** Every e2e assertion that claims to type uppercase is technically false: `promptChanged query='Program'` (test-e2e.ps1:586, and Gamma/Beta/Shared/IShape/GREPME/Motions/Service) passes only because PowerShell `-match` is case-insensitive. The harness never verifies the case it claims to type; a future case-sensitive finder or a case-sensitive assertion change silently breaks.
- **Fix:** Add a shift flag for `[char]::IsUpper($ch)` (tap Shift down/up around the VK, like the punctuation branch) so `Send-Text` is a faithful keyboard emulator.

### M4 (major) — preview re-tokenizes + rebuilds the `FlowDocument` per selection change (M5 partial)
- **Where:** `Telescope/Overlay/PreviewRenderer.cs:36` (`Show` → `SetContent` unconditionally), `:75` (`SetContent` always runs `SyntaxHighlighter.Tokenize` + full `FlowDocument` rebuild); caller `TelescopeOverlay.RenderResults` → `LoadPreviewForSelection` on every j/k move and filter update.
- **What's wrong:** The class docstring (lines 20-22) claims "the file is re-read and re-tokenized only when its LastWriteTimeUtc changes" — but the mtime cache (`FileContentCache`) only avoids the disk read, NOT the re-tokenize/re-render. Every selection change re-tokenizes the entire file and rebuilds the WPF document.
- **Why it bites:** Moving the selection between hits in the same file re-tokenizes + re-renders per keystroke — the dominant preview cost for large files; the user's preview scroll/caret is destroyed just by moving list selection.
- **Fix:** Cache the tokenized segments + built `FlowDocument` keyed by mtime (or content hash) and only re-render when the file actually changed; keep `ApplyCaret`/`MoveToLine` as the per-selection work.

### M5 (major) — FocusGuard text-input exemption computed twice (M22 not fixed)
- **Where:** `MyExtension/Input/InputHandler.cs:104-109` (`EditorFocusedVeto` uses `GeneralToolWindowController.IsTextInputType(_windowManager.Type)` — recomputed, NOT sentinel-aware) vs `:84-90` (`HasToolWindowActionKeys` uses `_windowManager.IsTextInputType` — cached at focus-change, sentinel-aware).
- **What's wrong:** Two sources of truth for "does this surface own the keyboard". The prior report's M22 asked for this to be single-sourced; it is still computed twice with different inputs.
- **Why it bites:** The two formulations can drift (one adds a text-input type to the switch, the other doesn't; or the sentinel logic changes one path only), silently changing key routing — the exact leak class the FocusGuard exists to prevent (action keys leaking into a focused editor, or being swallowed while a text surface owns the keyboard).
- **Fix:** Route `EditorFocusedVeto` through the same `_windowManager.IsTextInputType` cached value (or pass the veto into `FocusGuard` as a single `ownsKeyboard` bool) so all three routing formulations read one source.

### M6 (major) — the 67-findings plan's e2e gates never materialized in `e2e-queue.md`
- **Where:** `docs/progress.md:16,205,401-402` and `docs/implementation_plan.md:778,2049` claim the deferred gates are "queued in docs/e2e-queue.md (E2E-NCR-1..2, E2E-NCR-M1/M2/M15 .. E2E-NCR-M26/M27/M28, E2E-RESTRUCTURE-1, status QUEUED)" — but `docs/e2e-queue.md` contains NO E2E-NCR-* or E2E-RESTRUCTURE-1 entries (rg: 0 matches; the file only has the E2E-AC-* and 75-findings E2E-CR-*/E2E-M* sections).
- **What's wrong:** The most recent GREEN plan (67 findings + restructure, commit `92b119f`) never materialized its e2e gate list in the file the hub reads.
- **Why it bites:** When the user moves to a VS-capable machine, the hub runs the deferred gates listed in `e2e-queue.md` — the 67-findings gates (including E2E-RESTRUCTURE-1, the full 35-scenario suite that is the restructure's only behavior-preservation gate) are silently never run, so the restructure's e2e verification is lost with no record.
- **Fix:** Append the E2E-NCR-1..2 / E2E-NCR-M1..M28 / E2E-RESTRUCTURE-1 entries (with the scenario lists from implementation_plan.md:739-769) to `e2e-queue.md`, or correct the progress.md/implementation_plan.md claims.

### M7 (major) — `Run_FzfFilter_TimeoutKillsAndFallsBack` is timing-dependent
- **Where:** `tests/Telescope.Tests/Program.cs:554-608`.
- **What's wrong:** A 5s wall-clock bound plus a 2s `GC.Collect()`/`WaitForPendingFinalizers()` polling loop to detect `UnobservedTaskException`.
- **Why it bites:** The GC loop is non-deterministic — finalization order/timing is not guaranteed, so if the unobserved-task bug is reintroduced the faulted task may not finalize inside the 2s window and the test false-passes (its entire purpose); on a slow machine the 5s bound can also false-fail.
- **Fix:** Replace the GC-poll with a deterministic seam: have the timeout path expose the awaited-task outcome (e.g. a `TaskCompletionSource`/count of awaited reads) and assert on that instead of relying on finalizer timing.

### M8 (major) — `g` opens the LAST file of a multi-file project item
- **Where:** `MyExtension/ToolWindows/SolutionExplorerController.cs:331` (`string fullPath = pi.FileNames[(short)pi.FileCount];`).
- **What's wrong:** `FileNames` is 1-based, so index `FileCount` is the LAST file. For a WinForms `Form1.cs` item (FileCount 3), `FileNames[3]` = `Form1.resx` — not the primary source file.
- **Why it bites:** For any solution containing a multi-file `.cs` item, `g` (SelectFirstSourceFile) opens the wrong file (e.g. the `.resx`) via `ItemOperations.OpenFile(first)` and logs `solution-explorer select file=...Form1.resx`, breaking the harness's `select file=.*\.cs` assertion and opening a non-source file in the editor. The seeded scratch solution has only single-file `.cs` items, so the e2e never exercises this.
- **Fix:** Use `pi.FileNames[1]` (the primary file's full path) for the forest path, or filter to the file whose name matches `pi.Name`.

## Recommendations (ordered by effort/impact)

1. **Fix M1 first** — the `a`/`A`/`I` prompt interception is a real functional bug with a false-positive e2e gate; a one-line guard in `TryPromptMotion` + a unit test. Small, high-blast-radius.
2. **Restore the harness's case fidelity (M3)** — add Shift for uppercase in `Send-Text`; then the `query='Program'` assertions actually verify case. Small harness change; may expose real case-sensitivity gaps.
3. **Amortize the finder path (M2, M4)** — make `GrepFinder.GetCandidates` truly async and cache the tokenized preview per mtime. Both behind existing seams.
4. **Single-source the FocusGuard exemption (M5)** and the key→arrow mapping (m9/m17) — one source of truth each, killing the drift class.
5. **Fix the `g` multi-file bug (M8)** and the `VimModeSource` latch/subscription issues (m38/m39/m40) — the VsVim interop is the highest-blast-radius fragile surface.
6. **Harden the tests (M7, m53-m59, m62)** — deterministic timeout seam, delete duplicate/reflection tests, hermetic fzf stub, await async test methods.
7. **Reconcile the docs/queue (M6, m66-m69, n20-n21)** — materialize the E2E-NCR gates in `e2e-queue.md`, fix the header/"Next up" drift, add the missing §4 lines + §2.2 seams.
8. **Delete the dead code (m1, m2, m10, m28)** and fix the stale doc comments (m3) — pure deletions with test consolidation.

## Filed into progress.md

**None filed yet** — pending the user's selection (Step 4). Cross-references to `docs/reviews/architecture-review.md`: F2→(per-key hot path, now the `HasToolWindowActionKeys` dead-code/doc-drift variant m1), F5→m39/m40 (stale buffer subscription, now the shared-buffer no-refcount variant), F7→m44/m46 (one bad frame kills navigation), F8/F9→m37/m50 (fzf per-keystroke spawn + `IsAvailable` UI block), F20→(missing `ThrowIfNotOnUIThread` — now only n14), F22→(leader matcher extracted; `IsLeaderActive` volatile doc drift n15), F23→(Ctrl-key pre-filter — now n16). Prior-report regressions re-reported here: M4→M2, M5→M4, M9→m44, M12→m51, M13→m27, M17→m2, M20→m16/m17, M22→M5, M27→m64, M31→m69.
