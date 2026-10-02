# Combined Plan — Code-Review Fixes (98 findings) + Functional Restructure

> **Lane: feature/bugfix program + refactor (unit-only, e2e deferred).**
> Source: `docs/reviews/code-review.md` (2026-10-01, whole-repo 10-worker review of
> the post-67-fix codebase) + a user-requested repository restructure (main files
> separated from helpers/utils by functionality + renames that reflect actual function).
>
> **User decisions (2026-10-01, via question):** (1) **Utils subfolder + 5 renames** —
> per functional folder, main files stay in the folder and helpers/utils move to a
> `Utils/` subfolder; rename WindowMatrix→WindowNavigator,
> CardinalNavigationConstants→NavigationConstants, UtilityMethods→WindowFrameUtils,
> RectCoordinate→WindowRect, WindowAdapter→WindowFrameAdapter; (2) **namespaces stay
> unchanged** (MyExtension.Navigation etc.); (3) **one combined plan** — code-review
> fixes as early phases, restructure as the final phases; (4) **e2e deferred** (this
> machine cannot boot the VS Experimental Instance — user confirmed).
>
> **Research:** 1 whole-repo `trailmark-recon` digest (1763 nodes, 717 proxies,
> 0 entrypoints) + 2 `arch-auditor` slices (code-review fix verification: all 98
> findings CONFIRMED against current code with fix direction + unit seam; restructure
> impact: 90 files classified MAIN vs HELPER/UTIL + target structure + rename blast
> radius per rename). `feature-researcher` SKIPPED (both tasks are internal bugfix +
> refactor — no LazyVim reference needed).
>
> **Ground truth:** `dotnet build MyExtension.slnx` = 0 errors. Repo is GREEN
> (commit `92b119f` — the 67-findings + restructure plan).
>
> e2e scenarios are DEFERRED to `e2e-queue.md` (status QUEUED) — this machine cannot
> boot the VS Experimental Instance. NO e2e RED/VERIFY step is planned; every fix is
> verified by unit tests (where a hermetic seam exists) + `dotnet build` + the
> existing unit suites, with the live behavior gated by the deferred e2e scenarios.

## Goal

Fix all 98 code-review findings in dependency order (the 2 functional bugs first,
then the finder UI-stall cluster, the FocusGuard/routing drift, the VsVim interop
fragility, the navigation robustness, the controller/state issues, the duplication
merges, the dead-code deletions, the harness hardening, the test hermeticity, and
the docs/queue drift), then restructure the repository into functional folders
(main files + a `Utils/` subfolder per area) with the 5 approved renames — with
every behavior change proven by a RED unit test (or, where no hermetic seam exists,
by build + existing suites + the deferred e2e gate), and the restructure proven
behavior-preserving by build + unit suites + doc-ref lint + the deferred full-suite
e2e re-run.

## Approach

The research passes produced a verified verdict + fix direction + unit-test seam for
every finding, plus a full restructure impact inventory (90 files classified) with
rename blast radius. The plan is organized into 15 phases in dependency order:
Phases 0-10 are the code-review fixes, Phases 11-14 are the restructure. Cross-cutting
constraints:

- **Log-line-as-contract:** several fixes touch lines the e2e harness asserts on
  (`navigate direction=L/R/D/U`, `[Telescope] focus target=List|Preview`,
  `vim-mode=Insert|Normal|Replace`, `preview caret=... line=...`,
  `text-motion key=...`, `textinput-enter-input ...`, `prompt-motion key=...`). Any
  fix that changes a signature or a log site MUST preserve the emitted token
  byte-identical.
- **Proxy traps:** cross-class calls land on `proxy.unresolved:<Type>.<Member>`; a
  bare `callers_of` returning 0 is SUSPECT, not dead code. Never report
  `KeyInjection.Press`, `NeoVisualLog.Log`, `LogFileWriter.Write`,
  `FocusGuard.ShouldRouteToolWindowKey`, `WindowNavigationEngine.SelectTarget`,
  `LeaderSequenceMatcher.Reset`, `TextMotionNavigator.SetText`, `FinderBase.GetCandidates`
  as dead.
- **Fix-direction corrections from research (fold into the phases):**
  1. **M1** — the fix is in `TryPromptMotion`: return false when `TryDispatch`
     reports an insert placement (`out CaretPlacement != null`), so `a`/`A`/`I`
     fall through to `OverlayKeyHandler`. The `telescope-mode` e2e `a` assertion is
     a false positive (fixed-baseline re-match) — the unit test is the real gate.
  2. **M2** — the comment at GrepFinder.cs:105-107 claims "background + marshal" but
     `.GetAwaiter().GetResult()` blocks synchronously. Fix = make `GetCandidates`
     truly async (await in `RefreshQueryDrivenAsync`) OR drop the `Task.Run` (same
     blocking, no wasted thread hop). **BP-4 chooses the drop-Task.Run alternative**
     (GrepFinder-only, fully scoped, behavior-preserving) — the async interface
     change is NOT taken.
  3. **M8** — `FileNames` is 1-based; `FileNames[1]` is the primary file's full path
     (the comment's "index 1 is the short name" is wrong). Extract a pure
     primary-path pick into a testable helper.
  4. **M5** — route `EditorFocusedVeto` through the same cached
     `_windowManager.IsTextInputType` (or a single `ownsKeyboard` bool into
     `FocusGuard`) so all three routing formulations read one source.
  5. **M7** — replace the GC-poll with a deterministic seam (the timeout path exposes
     the awaited-task outcome via a `TaskCompletionSource`/awaited-read count).
- **No-seam findings** (VS/WPF/harness-coupled, no hermetic unit surface): M3, M6,
  m3, m6 (the hook-wiring part only — the `LogFileWriter` format part has a hermetic
  seam, BP-51), m10, m14, m18, m20, m21, m24, m25, m28 (wiring), m38, m39, m40,
  m45, m46, m47, m49, m60, m63, m64, m65, m66, m67, m68, m69, n2, n4, n5, n7, n8,
  n9, n10, n12, n13, n14, n15, n16, n17, n18, n19, n20, n21. For these the plan
  states the honest verification (build + existing suites + deferred e2e) and, where
  possible, extracts a small pure seam to make part of the behavior unit-testable.
- **Restructure constraint:** the restructure is behavior-preserving — NO
  `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` log literal may change, no
  internal type/API change except the 5 approved renames (which are the point), and
  the unit suites must pass with UNCHANGED counts. The doc-ref lint
  (`tools/lint/check-doc-refs.ps1`) must stay clean after the reference sweep.

---

# Part A — Code-review fixes (Phases 0-10)

## Phase 0 — Functional bugs (M1, M8, m36, m52)

- **M1** (major, CONFIRMED) — `a`/`A`/`I` in the Telescope prompt are intercepted by
  `TryPromptMotion` (TelescopeOverlay.cs:562,583-597) as caret motions and never
  enter insert mode; `OverlayKeyHandler`'s `a`/`A`/`I` actions are unreachable dead
  code; the `telescope-mode` e2e `a` assertion is a false positive. **Fix:** in
  `TryPromptMotion`, return false when `TryDispatch.Handle` reports an insert
  placement (`out CaretPlacement != null`), so `a`/`A`/`I` fall through to
  `_keyHandler.Handle`. **Unit seam (Telescope.Tests):** `Run_TryDispatch_InsertPlacementsNotPromptMotions`
  — `TryDispatch.Handle(Key.A/A/I, ...)` reports a placement (RED: today the prompt
  consumes them); plus a `Run_OverlayKeyHandler_*` assertion that `a`/`A`/`I` map to
  insert-mode actions.
- **M8** (major, CONFIRMED) — `SolutionExplorerController.cs:331`
  `pi.FileNames[(short)pi.FileCount]` indexes the LAST file of a multi-file project
  item (FileNames is 1-based; FileCount=3 → `Form1.resx`), so `g` opens the wrong
  file. **Fix:** use `pi.FileNames[1]` (the primary file's full path). **Unit seam
  (NeoVisual.Tests):** pin a dependency-free helper signature —
  `HierarchyResolver.PrimaryFilePath(IReadOnlyList<string> fileNames)` returns
  `fileNames[0]` (the primary file; `EnvDTE.ProjectItem` is a COM interface that
  cannot be constructed hermetically, so the helper takes primitive inputs) —
  `Run_HierarchyResolver_PrimaryFilePath` asserts the primary file is picked for a
  multi-file list (RED: today the caller indexes the last).
- **m36** (minor, CONFIRMED) — `EnterInsert(Current)` calls `FocusPrompt()` which
  resets `_promptBox.CaretIndex` to the end (TelescopeOverlay.cs:516,486) — `i`
  discards the caret position set by h/l/w/b/e/0/$. **Fix:** don't reset `CaretIndex`
  in `FocusPrompt` when entering insert at the current position. **Unit seam
  (Telescope.Tests):** `Run_OverlayKeyHandler_*` (behavior-preserving; coupled to the
  M1 prompt-insert fix).
- **m52** (minor, CONFIRMED) — `HandlePreviewKey` routes `a`/`A`/`I` in the read-only
  preview (TelescopeOverlay.cs:616) — insert placements move the caret in a read-only
  surface. **Fix:** exclude the insert placements from the preview motion surface.
  **Unit seam (Telescope.Tests):** `Run_TryDispatch_InsertPlacementsNotPreviewMotions`
  (RED: today the preview maps them).

## Phase 1 — Finder path amortization (M2, M4, m5, m15, m27, m30, m31, m32, m33, m34, m37, m50)

- **M2** (major, CONFIRMED) — `GrepFinder.GetCandidates` (GrepFinder.cs:109-121)
  blocks the UI thread with `Task.Run(...).GetAwaiter().GetResult()` for the whole
  scan. **Fix:** make `GetCandidates` truly async (return
  `Task<IReadOnlyList<FinderEntry>>`, awaited in `RefreshQueryDrivenAsync`) or drop
  the `Task.Run`. **Unit seam (Telescope.Tests):** the hermetic `GrepFinder` path
  (injected enumerate + opener) — a test that `GetCandidates` returns the same hits
  when the scan runs off-thread (RED: no off-thread seam → compile error or behavior
  gap).
- **M4** (major, CONFIRMED) — `PreviewRenderer` re-tokenizes + rebuilds the
  `FlowDocument` on every selection change (PreviewRenderer.cs:36,62-75); the mtime
  cache only avoids the disk read. **Fix:** cache the tokenized segments + built
  `FlowDocument` keyed by mtime (or content hash); keep `ApplyCaret`/`MoveToLine`
  per-selection. **Unit seam (Telescope.Tests):** a pure mtime-keyed token cache —
  `Run_PreviewTokenCache_UnchangedMtimeCached` / `_ChangedMtimeRetokenizes` (RED:
  cache class doesn't exist → compile error).
- **m5** (minor, CONFIRMED) — `MyExtensionPackage.ReadLine` (MyExtensionPackage.cs:665-675)
  re-opens + re-scans the file prefix per reference hit (O(hits × line)). **Fix:**
  read once per gather (or reuse the Roslyn `SourceText`). **Unit seam
  (Telescope.Tests):** `Run_FileContentCache_*` (the cache is the read-once seam).
- **m27** (minor, CONFIRMED) — `FileContentCache` LRU `maxEntries` cap never passed
  by any production caller (PreviewRenderer.cs:34, CodeIssuesFinder.cs:40,
  GrepFinder.cs:29 all use `new FileContentCache()`). **Fix:** pass a cap (e.g. 500)
  at the construction sites. **Gate:** `Run_FileContentCache_EvictsOldest` already
  exists and passes (the LRU eviction is implemented) — the m27 bug is that
  production callers don't pass a cap, so the gate is a code-review assertion that
  the three construction sites pass a cap + build (no new RED test needed).
- **m31** (minor, CONFIRMED) — `TryPromptMotion`/`ApplyInsertCaret` allocate a fresh
  `new TextMotionNavigator()` per keystroke (TelescopeOverlay.cs:585,498). **Fix:**
  reuse a `_promptNavigator` field. **Unit seam (Telescope.Tests):** behavior-preserving
  (the motion math is unchanged).
- **m32** (minor, CONFIRMED) — `TextMotionNavigator.LineNumber` is an O(n) scan read
  on every preview keystroke log (TextMotionNavigator.cs:37-51). **Fix:** cache the
  line number incrementally or use the existing `LineIndex`. **Unit seam
  (Telescope.Tests):** `Run_TextMotionNavigator_LineNumber` (behavior-preserving).
- **m33** (minor, CONFIRMED) — `ResultMapper.MapBack` rebuilds
  `GroupBy(...).ToDictionary(...)` over the whole snapshot per keystroke
  (ResultMapper.cs:21-24). **Fix:** cache the byDisplay map per snapshot. **Unit
  seam (Telescope.Tests):** `Run_ResultMapper_*` (behavior-preserving).
- **m34** (minor, CONFIRMED) — `FileFinder.GatherHits` calls `ProjectFiles.Enumerate`
  directly (FileFinder.cs:62) — no shared `ProjectFileCache`. **Fix:** route through
  the cache. **Unit seam (Telescope.Tests):** `Run_FileFinder_UsesProjectFileCache`.
- **m37** (minor, CONFIRMED) — fzf subprocess spawned per keystroke (FzfFilter.cs:95);
  documented bites-later. **Fix:** schedule `fzf --listen` / in-process matcher when
  latency is measured to matter. **Unit seam (Telescope.Tests):** behavior-preserving.
- **m50** (minor, CONFIRMED) — `FzfFilter.IsAvailable()` blocks the UI up to 500ms on
  every overlay open (FzfFilter.cs:64-88). **Fix:** cache the availability once per
  session or probe async. **Unit seam (Telescope.Tests):**
  `Run_FzfFilter_IsAvailableBounded` (injected clock — note: the clock injection
  lands in Phase 9's m58; the Phase 1 BP step defers the bounded assertion to Phase 9).
- **m15** (minor, CONFIRMED) — `TextMotionHelper` per-motion key: 2 full buffer
  copies + up to 3 `FindFocusedTextBox` visual-tree walks (TextMotionHelper.cs:82,149,158).
  **Fix:** return the found box once, reuse the navigator. No-seam (WPF) — build +
  deferred e2e `neovisual-textinput-motions`.
- **m30** (minor, CONFIRMED) — `FileFinder.OpenHit` double `File.Exists` (outer guard
  + `HitOpener`) (FileFinder.cs:78). **Fix:** drop the outer guard, let `HitOpener`
  own it. **Unit seam (Telescope.Tests):** `Run_HitOpener_*` (behavior-preserving).

## Phase 2 — FocusGuard + routing (M5, m7, m42, m43, m47, n14, n15, n16)

- **M5** (major, CONFIRMED) — the text-input keyboard-ownership exemption computed
  twice: `EditorFocusedVeto` (InputHandler.cs:104-109) uses
  `GeneralToolWindowController.IsTextInputType(_windowManager.Type)` (recomputed)
  while `HasToolWindowActionKeys` (InputHandler.cs:84-90) uses the cached
  `_windowManager.IsTextInputType`. **Fix:** route both through one cached
  `IsTextInputType` (or a single `ownsKeyboard` bool into `FocusGuard`). **Unit seam
  (NeoVisual.Tests):** `Run_FocusGuard_OwnsKeyboard` (behavior-preserving; RED:
  signature change → compile error).
- **m7** (minor, CONFIRMED) — the identical 5-arg `FocusGuard.ShouldRouteToolWindowKey`
  call repeated 3× (InputHandler.cs:308,436,468). **Fix:** hoist to one private
  helper. **Unit seam (NeoVisual.Tests):** `Run_FocusGuard_*` (behavior-preserving).
- **m42** (minor, CONFIRMED) — `BuildBindings` classifies any binding key containing
  "+" as a simple shortcut (InputHandler.cs:181-188) — a leader key like `F,+` can
  never match. **Fix:** classify by a modifier prefix ("Ctrl+"/"Shift+"/"Alt+") not
  any "+". **Unit seam (NeoVisual.Tests):** the classification lives in the private
  VS-coupled `InputHandler.BuildBindings`, so extract a pure classification method
  into `KeybindingConfig` (e.g. `KeybindingConfig.IsSimpleShortcut(string key)` — a
  modifier-prefix check) — `Run_KeybindingConfig_IsSimpleShortcut` (RED: a `F,+`
  leader binding is misclassified today).
- **m43** (minor, CONFIRMED) — `InjectedKeyGuard` per-VK counter with no TTL
  (InjectedKeyGuard.cs:42-67) — a stale pending record consumes the next physical
  key-down. **Fix:** add a bounded TTL or accept+document. **Unit seam
  (NeoVisual.Tests):** `Run_InjectedKeyGuard_*` (behavior-preserving).
- **m47** (minor, CONFIRMED) — no outcome diagnostic for navigation (InputHandler.cs:498)
  — a no-op passes the harness. **Fix:** log the target/outcome
  (`[NeoVisual] navigate activated index=...` / `no-op: <reason>`). No-seam
  (diagnostic) — deferred e2e.
- **n14** (nit, CONFIRMED) — `Navigate` lacks a direct `ThrowIfNotOnUIThread()`
  (InputHandler.cs:496). **Fix:** add the assert. No-seam (build).
- **n15** (nit, CONFIRMED) — `IsLeaderActive` reads a plain (non-volatile) bool
  (InputHandler.cs:72 + LeaderSequenceMatcher.cs:19) — AGENTS.md documents volatile.
  **Fix:** make `_active` volatile or correct the doc. No-seam (build).
- **n16** (nit, CONFIRMED) — Ctrl+N/P injected unconditionally with no popup-active
  check (PopupNavigation.cs:39-53) — documented design, UX footgun. **Fix:**
  accept+document or gate on `ICompletionBroker.IsCompletionActive`. No-seam.

## Phase 3 — Vim interop (m38, m39, m40, m41, m3, n1)

- **m38** (minor, CONFIRMED) — `GetVim` sets `_resolved = true` even when `vim` is
  null (VimModeSource.cs:419-421) — permanently latches "no VsVim" on an early
  resolution. **Fix:** only latch on `vim != null`. No-seam (VsVim reflection) —
  build + deferred e2e.
- **m39** (minor, CONFIRMED) — `Attach` unconditionally re-points
  `_currentBuffer`/`_currentTextBuffer` (VimModeSource.cs:117-125,177-183) — a
  background/peek view hijacks the focused mode state. **Fix:** only re-point on
  focus gain. No-seam (VsVim reflection).
- **m40** (minor, CONFIRMED) — `Detach` unsubscribes the text buffer with no
  reference counting (VimModeSource.cs:128-136) — two views sharing a buffer kill
  each other's subscription (CR2 incomplete). **Fix:** refcount shared buffers.
  No-seam (VsVim reflection).
- **m41** (minor, CONFIRMED) — `OnViewLostFocus`/`OnViewClosed` return typing-change
  not mode-change (VimModeState.cs:57-67,73-83) — `vim-mode=Unknown` never emitted
  on editor-focus loss. **Fix:** return the mode-change (or log on `ModeName`
  change). **Unit seam (NeoVisual.Tests):** `Run_VimModeState_*` (RED: the
  mode-change return is missing today).
- **m3** (minor, CONFIRMED) — `VimBufferSubscriptions` doc comment claims the CR2
  wiring is "NOT wired yet" (VimBufferSubscriptions.cs:16-19) but `VsVimModeSource`
  calls it. **Fix:** correct the comment. No-seam (doc).
- **n1** (nit, CONFIRMED) — `VimModeState` `Normal`/`Insert`/`Replace` const aliases
  duplicate `VimModeClassifier` (VimModeState.cs:22-24). **Fix:** delete the aliases,
  use the classifier. **Unit seam (NeoVisual.Tests):** `Run_VimModeClassifier_*`.

## Phase 4 — Navigation robustness (m11, m12, m13, m14, m44, m45, m46, m48, m49, n3, n4, n5, n6, n19)

- **m11** (minor, CONFIRMED) — `SelectTarget` runs two full passes re-evaluating all
  3 predicates + `GapTo` per candidate (WindowNavigationEngine.cs:24-64). **Fix:**
  single pass collecting (index, gap, adjacency) for passing candidates. **Unit seam
  (NeoVisual.Tests):** `Run_WindowNavigationEngine_*` (behavior-preserving).
- **m12** (minor, CONFIRMED) — `NavigationSettings.FromSystemDpi()` re-reads system
  DPI on every navigation (WindowMatrix.cs:39). **Fix:** cache the settings. **Unit
  seam (NeoVisual.Tests):** `Run_NavigationSettings_*` (behavior-preserving).
- **m13** (minor, CONFIRMED) — `LinkedTo` re-filters every adapter with O(n·m)
  `CompareWindows` COM reads (WindowAdapter.cs:94). **Fix:** precompute a key set /
  index. **Unit seam (NeoVisual.Tests):** behavior-preserving.
- **m14** (minor, CONFIRMED) — `GetIVsUIShell` no null check (UtilityMethods.cs:21) →
  NRE escapes `WindowMatrix`'s catch, misdiagnosed as a binding failure. **Fix:**
  null-guard + log. No-seam (VS-coupled).
- **m44** (minor, CONFIRMED) — `activeWindow` dereferenced (`LinkedTo`) before the
  null check (WindowMatrix.cs:47-58) — the graceful-degradation branch is dead (M9
  not fixed). **Fix:** move the null check before `LinkedTo`. **Unit seam
  (NeoVisual.Tests):** the null-check ordering is in the VS-coupled `WindowMatrix`
  ctor (starts with `ThreadHelper.ThrowIfNotOnUIThread()`), so extract a pure static
  helper (e.g. `WindowNavigator.BuildActiveWindows(EnvDTE.Window? active,
  IReadOnlyList<WindowFrameAdapter> adapters)` that null-checks before linking) —
  `Run_WindowNavigator_BuildActiveWindowsNullActive` (RED: today the deref precedes
  the null check).
- **m45** (minor, CONFIRMED) — `Activate()`/`AutoHides()` deref `_dte` with no null
  guard (WindowAdapter.cs:42,48). **Fix:** null-guard. No-seam.
- **m46** (minor, CONFIRMED) — `_activeWindows.Select(w => w.Rect).ToList()` has no
  per-window fault isolation (WindowMatrix.cs:94) — one stale frame kills all
  navigation. **Fix:** per-window try/catch. No-seam.
- **m48** (minor, CONFIRMED) — Down/Up tolerance asymmetry (`> 1` vs `<`)
  (WindowNavigationEngine.cs:85-86). **Fix:** symmetric tolerance. **Unit seam
  (NeoVisual.Tests):** `Run_WindowNavigationEngine_*` (RED: the 1px-gap case).
- **m49** (minor, CONFIRMED) — `VSFPROPID_Type` HRESULT ignored; `(int)value` NREs
  (WindowManager.cs:240-241). **Fix:** check HRESULT + null. No-seam.
- **n3** (nit, CONFIRMED) — `Pipeline` `List<Func<...>>` abstraction for a 3-line
  filter (WindowNavigationEngine.cs:10-16). **Fix:** inline the 3 predicates. **Unit
  seam (NeoVisual.Tests):** behavior-preserving.
- **n4** (nit, CONFIRMED) — `using System;` inside the namespace block
  (Direction.cs:3). **Fix:** move to top. No-seam (build).
- **n5** (nit, CONFIRMED) — `ExtractFrames` iterator — `ThrowIfNotOnUIThread` runs
  lazily on first `MoveNext` (WindowAdapter.cs:97-99). **Fix:** assert before the
  iterator. No-seam.
- **n6** (nit, CONFIRMED) — `NavigationSnapshot` heap class for a 2-field value
  (NavigationSnapshot.cs:9). **Fix:** make it a `readonly struct`. **Unit seam
  (NeoVisual.Tests):** behavior-preserving.
- **n19** (nit, CONFIRMED) — frames without `IVsWindowFrame4` silently map to
  `RectCoordinate.Empty` with no diagnostic (WindowAdapter.cs:118,131). **Fix:** log
  the fallback once. No-seam.

## Phase 5 — Controller/state (m20, m21, m22, m23, m24, m25, m26, m59, n7, n8, n9, n10)

- **m20** (minor, CONFIRMED) — literal `1500` vs the `FocusKeeperDurationMs`
  constant (SolutionExplorerController.cs:234). **Fix:** use the constant. No-seam.
- **m21** (minor, CONFIRMED) — FocusKeeper re-asserts `View.SolutionExplorer` on
  every tick with no cancellation on window close (SolutionExplorerController.cs:119,234).
  **Fix:** add a stop-on-close (verify the window is still visible before
  re-asserting). No-seam (VS-coupled).
- **m22** (minor, CONFIRMED) — shared `_defaultController` leaks `_isInputMode`
  across all unknown-GUID tool windows (WindowManager.cs:25,186-190). **Fix:**
  per-type default instances. **Unit seam (NeoVisual.Tests):** `Run_WindowManager_*`
  (RED: the shared-instance leak is observable today).
- **m23** (minor, CONFIRMED) — ctor `_isInputMode = IsTextInputType(type)` branch
  dead (GeneralToolWindowController.cs:31). **Fix:** drop the branch. **Unit seam
  (NeoVisual.Tests):** behavior-preserving.
- **m24** (minor, CONFIRMED) — editor-view path passes `styleCaret: false`
  (TextMotionHelper.cs:98) — block caret persists in insert mode after `a`/`A`/`I`.
  **Fix:** style the editor view on insert placements. No-seam (WPF editor view).
- **m25** (minor, CONFIRMED) — try/catch + `Log(...failed: {msg})` swallow idiom
  repeated 3× with different prefixes (SolutionExplorerController.cs:137,248,376).
  **Fix:** unify the catch/log helper. No-seam.
- **m26** (minor, CONFIRMED) — `FirstSourceFilePath` returns the first physical file
  regardless of extension (HierarchyResolver.cs:27) — the `.cs` filter lives only in
  the forest builder. **Fix:** move the `.cs` filter into the resolver (or rename to
  `FirstPhysicalFilePath`). **Unit seam (NeoVisual.Tests):** `Run_HierarchyResolver_*`
  (RED: an unfiltered forest returns a non-.cs file today).
- **m59** (minor, CONFIRMED) — `SolutionExplorerController.SelectFirstSourceFile`
  (`g`) never unit-tested (NeoVisual.Tests/Program.cs:624-634). **Fix:** extract the
  pure pick (which node to select) and test it. **Unit seam (NeoVisual.Tests):**
  `Run_HierarchyResolver_*` / `Run_HierarchyForestBuilder_*`.
- **n7** (nit, CONFIRMED) — `WindowManager` class body unindented at column 0
  (WindowManager.cs:11). **Fix:** reindent. No-seam (build).
- **n8** (nit, CONFIRMED) — redundant `keeperRef` local (FocusKeeper.cs:18). **Fix:**
  delete it. No-seam.
- **n9** (nit, CONFIRMED) — `ComputeTextInputSurfaceFocused()` computed in the
  non-tool branch where it returns false immediately (WindowManager.cs:264). **Fix:**
  skip in the else branch. No-seam.
- **n10** (nit, CONFIRMED) — arrow-fallback log appends `focused=... hwnd=...`
  (TextMotionHelper.cs:120) — differs from `TryMoveArrow`'s bare contract. **Fix:**
  align the log contract. No-seam.

## Phase 6 — Duplication merges (m8, m9, m16, m17, m18, m19, m35, n11)

- **m9** (minor, CONFIRMED) — key→arrow-VK mapping implemented twice
  (GeneralToolWindowController.cs:66-73 + TextMotionHelper.cs:117), both emitting
  `toolwindow-move key=... -> arrow vk=...`. **Fix:** single-source `KeyToArrowVk`.
  **Unit seam (NeoVisual.Tests):** `Run_GeneralToolWindowController_*`
  (behavior-preserving).
- **m16** (minor, CONFIRMED) — W/B/E vim-motion action wiring duplicated verbatim
  (TextInputToolWindowController.cs:44-46 + SolutionExplorerController.cs:50-52).
  **Fix:** move to `ToolWindowControllerBase`. **Unit seam (NeoVisual.Tests):**
  `Run_ActionTable_*` (behavior-preserving).
- **m17** (minor, CONFIRMED) — H/L hardcode `VK_LEFT`/`VK_RIGHT` instead of
  `KeyToArrowVk` (SolutionExplorerController.cs:46-47). **Fix:** reuse `KeyToArrowVk`.
  **Unit seam (NeoVisual.Tests):** behavior-preserving.
- **m18** (minor, CONFIRMED) — `GetParent` visual/logical-tree walker duplicated
  verbatim (WindowManager.cs:131-153 + TextMotionHelper.cs:241-263). **Fix:** share
  one helper. No-seam (WPF).
- **m19** (minor, CONFIRMED) — two parallel DTE tree-walk seams
  (`HierarchyForestBuilder` vs `HierarchyWalker`/`ProjectFiles`). **Fix:** unify on
  one walker. **Unit seam (NeoVisual.Tests):** `Run_HierarchyForestBuilder_*` +
  **Telescope.Tests:** `Run_HierarchyWalker_*`.
- **m35** (minor, CONFIRMED) — `ApplyPromptCaretStyle` duplicates
  `TextMotionHelper.ApplyCaretStyle` (TelescopeOverlay.cs:603-613). **Fix:** delegate
  to the shared helper. **Unit seam (Telescope.Tests):** behavior-preserving.
- **n11** (nit, CONFIRMED) — `TryDispatch` is a 25-line trivial wrapper
  (TryDispatch.cs:12-24). **Fix:** merge into `TextMotionDispatcher`. **Unit seam
  (Telescope.Tests):** `Run_TryDispatch_*` → `Run_TextMotionDispatcher_*`.
- **m8** (minor, CONFIRMED) — two `TelescopeLauncher` instances for the same
  `TelescopeController` (InputHandler.cs:124 + MyExtensionPackage.cs:84) — duplicated
  construction of a stateful-looking wrapper. **Fix:** inject the package's `_launcher`
  into `InputHandler` instead of constructing a second one. No-seam (VS-coupled) —
  build + deferred e2e.

## Phase 7 — Dead code + logging (m1, m2, m10, m28, m6, m29, m51, n2, n12, n13, n17, n18)

- **m1** (minor, CONFIRMED) — `InputHandler.HasToolWindowActionKeys` dead code
  (InputHandler.cs:79-92); AGENTS.md/SKILL.md document it as the pre-filter
  mechanism. **Fix:** delete the property + fix the docs. **Unit seam
  (NeoVisual.Tests):** `Run_FocusGuard_*` (the guard is already tested).
- **m2** (minor, CONFIRMED) — `VimModeState.ResolveOnce` + `_resolved` latch dead
  (VimModeState.cs:90-109); `VsVimModeSource.GetVim` has its own latch. **Fix:**
  delete `ResolveOnce` + its test. **Unit seam (NeoVisual.Tests):** count drops by
  the deleted test (deterministic signal).
- **m10** (minor, CONFIRMED) — `WindowAdapter._frame` assigned never read
  (WindowAdapter.cs:18,25). **Fix:** delete the field. No-seam (build).
- **m28** (minor, CONFIRMED) — `OverlayClosed` event declared + raised, 0 subscribers
  (TelescopeOverlay.cs:240,331). **Fix:** delete the event + raise. **Unit seam
  (Telescope.Tests):** behavior-preserving.
- **m6** (minor, CONFIRMED) — `GlobalKeyboardHook.Log` prepends its own timestamp
  then `LogFileWriter.FormatLine` prepends a second (GlobalKeyboardHook.cs:200) —
  every `[Hook]` line double-stamped. **Fix:** drop the local timestamp. **Unit seam
  (Telescope.Tests):** `Run_LogFileWriter_*` (format contract).
- **m29** (minor, CONFIRMED) — `DteFileOpener` manual `[Telescope] ` prefix concat
  (DteFileOpener.cs:19). **Fix:** use `TelescopeLog.Log`. **Unit seam
  (Telescope.Tests):** behavior-preserving.
- **m51** (minor, CONFIRMED) — `NeoVisualLog.EnsurePane` catch leaves
  `_paneInitTried = true` (NeoVisualLog.cs:172-178) — a transient `CreatePane`
  failure permanently disables the pane (M12 fixed only the null-service path).
  **Fix:** reset the latch in the catch. **Unit seam (Telescope.Tests):** the
  `_paneInitTried` latch lives in the VS-coupled `NeoVisualLog.EnsurePane`, so
  extract the retry latch into the pure `PaneFailureTracker` (e.g.
  `ShouldRetry()`/`RecordAttempt()`) — `Run_PaneFailureTracker_RetryAfterFailure`
  (RED: the retry-after-failure path is missing today).
- **n2** (nit, CONFIRMED) — `BlockCaretAdornment.Update()` calls
  `RemoveAdornmentsByTag` even when `_active` is false (BlockCaretAdornment.cs:109).
  **Fix:** early-return before the remove. No-seam.
- **n13** (nit, CONFIRMED) — `PaneGuid` mutable static (NeoVisualLog.cs:28). **Fix:**
  make `readonly`. No-seam (build).
- **n12** (nit, CONFIRMED) — `RenderResults` rebuilds the whole result string + re-sets
  the TextBox text on every render (TelescopeOverlay.cs:441) — O(n) string build +
  WPF text set per keystroke/j-k. **Fix:** only re-render when the results or
  selection actually changed. No-seam (WPF) — build + deferred e2e.
- **n17** (nit, CONFIRMED) — `GetCaretOffset`'s DTE fallback uses
  `selection.ActivePoint.DisplayColumn` (tab-expanded display column) as a character
  offset into `SourceText` (MyExtensionPackage.cs:647-654) — on lines with tabs the
  offset is too large, so the references/implementations finders can resolve the
  wrong symbol. **Fix:** use a non-expanded character column (or map display→char via
  the text line). No-seam (VS-coupled) — build + deferred e2e.
- **n18** (nit, CONFIRMED) — `SetHook` calls `Process.GetCurrentProcess().MainModule`
  which can throw `Win32Exception` (GlobalKeyboardHook.cs:165-173) — if it throws,
  the hook init step fails and the extension degrades to a no-op. **Fix:** guard the
  `MainModule` access and fall back to `IntPtr.Zero` for `hMod` (valid for
  `WH_KEYBOARD_LL` when the proc is in-process), logging the failure. No-seam (build).

## Phase 8 — Harness hardening (M3, m60, m63, m64, m65)

- **M3** (major, CONFIRMED) — `Send-Text` never applies Shift for uppercase letters
  (harness-common.ps1:121-134) — `Send-Text 'Program'` types `program`; e2e
  assertions pass only via case-insensitive `-match`. **Fix:** add a shift flag for
  `[char]::IsUpper($ch)`. No-seam (PowerShell harness) — deferred e2e.
- **m60** (minor, CONFIRMED) — `Resolve-VsRoot` hardcodes Community-only paths before
  the vswhere fallback (harness-common.ps1:229-233). **Fix:** add
  Professional/Enterprise/Preview candidates. No-seam (PowerShell).
- **m63** (minor, CONFIRMED) — scenarios share one live VS instance and are
  order-dependent by design (test-e2e.ps1:822-874,989-1062). **Fix:** document the
  ordering + make subsets self-seeding. No-seam (PowerShell).
- **m64** (minor, CONFIRMED) — `results count=\d+ selected=0` matches `count=0`
  (test-e2e.ps1:1161). **Fix:** require `count=[1-9]\d*`. No-seam (PowerShell).
- **m65** (minor, CONFIRMED) — `Wait-LogLine` joins the whole tail + `-match`es it —
  can match across line boundaries; O(assertions × tail) (harness-common.ps1:176).
  **Fix:** per-line match over the cache. No-seam (PowerShell).

## Phase 9 — Test hermeticity (M7, m4, m53, m54, m55, m56, m57, m58, m62)

- **M7** (major, CONFIRMED) — `Run_FzfFilter_TimeoutKillsAndFallsBack` is
  timing-dependent (5s wall-clock bound + 2s GC-poll for `UnobservedTaskException`)
  (Telescope.Tests/Program.cs:554-608). **Fix:** expose the awaited-task outcome via
  a deterministic seam (TaskCompletionSource / awaited-read count) and assert on it.
  **Unit seam (Telescope.Tests):** `Run_FzfFilter_TimeoutAwaitsTasks` (RED: no seam →
  compile error).
- **m53** (minor, CONFIRMED) — `Run_Preview_UpFromSecondLineWithLeadingBlankLine` and
  `_Fixed` are byte-identical duplicates (Telescope.Tests/Program.cs:942-965).
  **Fix:** delete one. **Unit seam (Telescope.Tests):** count drops by 1.
- **m54** (minor, CONFIRMED) — `Run_GrepFinder_GatherHitsThrowsNotSupported` never
  exercises the throw (calls `GetCandidates("")`) (Telescope.Tests/Program.cs:2180-2206).
  **Fix:** rename to what it asserts + add a test that reaches the base gather stub.
  **Unit seam (Telescope.Tests).**
- **m55** (minor, CONFIRMED) — `Run_WindowAdapter_TryGetScreenRect_...` never calls
  `TryGetScreenRect` (NeoVisual.Tests/Program.cs:1390-1404). **Fix:** rename or call
  it. **Unit seam (NeoVisual.Tests).**
- **m56** (minor, CONFIRMED) — `Run_LeaderMatcher_PrefixSetBuiltOnce` asserts a
  private field via reflection (NeoVisual.Tests/Program.cs:1576-1599). **Fix:** assert
  behavior instead. **Unit seam (NeoVisual.Tests).**
- **m57** (minor, CONFIRMED) — `Run_FzfFilter_FilterMatchesPrefix` requires fzf on
  PATH (Mystery Guest) + weak presence assertion (Telescope.Tests/Program.cs:482-500).
  **Fix:** hermetic fzf stub / stronger assertion. **Unit seam (Telescope.Tests).**
- **m58** (minor, CONFIRMED) — `Run_FzfFilter_IsAvailableBounded` asserts `< 1s`
  wall-clock (Telescope.Tests/Program.cs:630-648). **Fix:** inject a clock / relax.
  **Unit seam (Telescope.Tests).**
- **m62** (minor, CONFIRMED) — `TestRunner.method.Invoke(null, null)` discards the
  return value (TestRunner.cs:60) — a future async test silently passes. **Fix:**
  await `Task`-returning methods. **Unit seam (tests/TestRunner.cs).**
- **m4** (minor, CONFIRMED) — ~270 lines of Roslyn/VS-coupled gatherer logic
  (`TryGetCaretSymbol`, `GatherReferences`, `GatherImplementations`, `GetCaretOffset`,
  `ReadLine`, `IsWriteLocation`) live inside `MyExtensionPackage` (MyExtensionPackage.cs:423-696)
  with no test seam; `IsWriteLocation` reads the internal `ReferenceLocation.IsWrittenTo`
  via reflection with no offline test. **Fix:** extract the gatherers to a
  `RoslynGatherers` class with an injectable seam (mirroring the finder host-injection
  pattern) + a reflection-robustness test for `IsWriteLocation`. **Unit seam
  (NeoVisual.Tests):** `Run_RoslynGatherers_IsWriteLocation` (RED: no seam → compile
  error).

## Phase 10 — Docs drift (M6, m61, m66, m67, m68, m69, n20, n21)

- **M6** (major, CONFIRMED) — `docs/progress.md:16,205,401-402` claims the 67-findings
  plan's deferred e2e gates are "queued in e2e-queue.md (E2E-NCR-*, E2E-RESTRUCTURE-1)"
  but the file has NO such entries — the restructure's e2e verification is lost.
  **Fix:** append the E2E-NCR-*/E2E-RESTRUCTURE-1 entries (from the prior
  implementation_plan.md:739-769) to `e2e-queue.md`, or correct the claims. No-seam
  (docs).
- **m61** (minor, CONFIRMED) — `check-doc-refs.ps1` external allowlist permanently
  masks future refs to removed symbols (`DistinctBy`, `CardinalMovment`,
  `CardinalNavigation`) (check-doc-refs.ps1:87-98). **Fix:** scope the allowlist to
  the specific archive docs. No-seam (lint).
- **m66** (minor, CONFIRMED) — `docs/progress.md:10` header "Chunk C restructure
  pending" contradicts the Done entry (Chunk C COMPLETE). **Fix:** fix the header.
  No-seam (docs).
- **m67** (minor, CONFIRMED) — `docs/progress.md:20-28` "Next up" still lists F5/F8/F9
  as open (covered by the GREEN combined plan). **Fix:** update the section. No-seam
  (docs).
- **m68** (minor, CONFIRMED) — `docs/spec.md:186-217` §4 diagnostics contract omits 4
  harness-asserted lines (`open finder=`, `Focus prompt => True, mode=insert`,
  `results count=... selected=...`, `key=... mode=... handled=...`). **Fix:** add the
  4 lines. No-seam (docs).
- **m69** (minor, CONFIRMED) — `docs/spec.md:47-109` §2.2 key-files table omits 13
  real seam files (OverlayShowState, FocusTargetModel, LineIndex, TryDispatch,
  PreviewRenderer, BlockCaretStyle, VimModeClassifier, InitSteps,
  SimpleShortcutMatcher, NavigationSnapshot, FilterFailureLog, TelescopeLog,
  PaneFailureTracker). **Fix:** add the rows. No-seam (docs).
- **n20** (nit, CONFIRMED) — `docs/progress.md:73-74` baseline parenthetical
  attributes 143/140 to the consolidation (it was the 67-findings plan). **Fix:** fix
  the attribution. No-seam (docs).
- **n21** (nit, CONFIRMED) — `docs/reviews/architecture-review.md:417` "Verified-clean"
  still claims `CardinalNavigation`/`CardinalMovment` — post-restructure it's
  `MyExtension.Navigation`/`Navigation/`. **Fix:** update the claim. No-seam (docs).

---

# Part B — Repository restructure (Phases 11-14)

## Restructure design (approved 2026-10-01)

The restructure separates MAIN files from their HELPERS/UTILS by functionality: each
functional folder keeps its main files and gains a `Utils/` subfolder for the
helpers. It also applies the 5 approved renames. It is **behavior-preserving**: no
log literal, no public API change (except the renames), no test count changes.
Namespaces stay UNCHANGED. The doc-ref lint + all agent/tool references are updated
in the same phase.

### MyExtension/ target (namespaces unchanged)

| Folder | MAIN files | Utils/ subfolder |
|--------|-----------|------------------|
| `Hooks/` | GlobalKeyboardHook | Utils/{NativeMethods, KeyInjection, InjectedKeyGuard} |
| `Input/` | InputHandler | Utils/{KeybindingConfig, LeaderSequenceMatcher, SimpleShortcutMatcher, KeyNames, KeyNameBuilder, PopupNavigation, StaleToolWindowSentinel} |
| `Vim/` | VimModeTracker | Utils/{VimModeState, VimModeSource, VimModeClassifier, VimBufferSubscriptions} |
| `Package/` | MyExtensionPackage | Utils/{InitSteps, VsServices, Actions, TelescopeLauncher, TelescopeCommand} |
| `Adornments/` | BlockCaretAdornment | (none — single file) |
| `Navigation/` | **WindowNavigator** (was WindowMatrix), WindowNavigationEngine | Utils/{**WindowFrameAdapter** (was WindowAdapter), **WindowFrameUtils** (was UtilityMethods), **WindowRect** (was RectCoordinate), **NavigationConstants** (was CardinalNavigationConstants), NavigationSettings, NavigationSnapshot, Direction} |
| `ToolWindows/` | WindowManager, GeneralToolWindowController, TextInputToolWindowController, SolutionExplorerController, ToolWindowControllerBase, IToolWindowController | Utils/{ToolWindowTypeResolver, TextMotionHelper, HierarchyResolver, HierarchyForestBuilder, FocusKeeper, FocusGuard} |

### Telescope/ target (namespaces unchanged)

| Folder | MAIN files | Utils/ subfolder |
|--------|-----------|------------------|
| `Controller/` | TelescopeController | (none — single file) |
| `Overlay/` | TelescopeOverlay | Utils/{OverlayKeyHandler, TextMotionNavigator, TextMotionDispatcher, TryDispatch, SyntaxHighlighter, ResultsFormatter, ResultMapper, PreviewRenderer, OverlayShowState, LineIndex, FocusTargetModel, BlockCaretStyle} |
| `Filter/` | FzfFilter | (none — single file) |
| `Finders/` | FileFinder, CodeIssuesFinder, ReferencesFinder, GrepFinder, ImplementationFinder, FinderBase, TelescopeFinder | Utils/{ProjectFiles, ProjectFileCache, FileContentCache, HitOpener, HierarchyWalker, DteFileOpener, FileLocation, IFileLocation, FileHit, CodeIssue, GrepHit, ImplementationHit, ReferenceHit} |
| `Logging/` | NeoVisualLog | Utils/{TelescopeLog, PaneFailureTracker, NeoVisualTraceListener, LogFileWriter, FilterFailureLog, DiagnosticLog} |

### Rename blast radius (from research — the reference sweep must cover these)

- `WindowMatrix` → `WindowNavigator`: referenced by `Input/InputHandler.cs:503`
  (`new WindowMatrix` + `wm.NavigateInDirection`), `WindowMatrix.cs` (decl), doc
  refs in AGENTS.md:326, SKILL.md, docs/spec.md:80,123,134, docs/progress.md,
  architecture-review.md, agent docs.
- `CardinalNavigationConstants` → `NavigationConstants`: referenced by
  `Navigation/NavigationSettings.cs:20-21` (4 refs) + doc refs (AGENTS.md:407,
  SKILL.md, docs/spec.md:85,136, docs/progress.md, architecture-review.md, agent docs).
- `UtilityMethods` → `WindowFrameUtils`: referenced by `Navigation/WindowAdapter.cs:59,83,93,94`
  (`GetIVsUIShell`, `GetLinkedWindowsList`, `CompareWindows`) + doc refs
  (docs/spec.md:84, docs/progress.md, architecture-review.md, agent docs).
- `RectCoordinate` → `WindowRect`: referenced by `Navigation/NavigationSnapshot.cs`,
  `WindowMatrix.cs:94`, `WindowAdapter.cs`, `WindowNavigationEngine.cs`,
  `tests/NeoVisual.Tests/Program.cs` (many) + doc refs (AGENTS.md:93, SKILL.md,
  docs/spec.md:86,244, docs/progress.md, architecture-review.md, agent docs).
- `WindowAdapter` → `WindowFrameAdapter`: referenced by
  `ToolWindows/WindowManager.cs:18,211,213,218`, `Navigation/WindowMatrix.cs:14,16,47,62`,
  `tests/NeoVisual.Tests/Program.cs:1386` + doc refs (AGENTS.md:404, SKILL.md,
  docs/spec.md:81, docs/progress.md, architecture-review.md, agent docs).

## Phase 11 — Restructure: MyExtension (folders + Utils subfolders + 5 renames)

- **BP steps:**
  1. `git mv` each helper .cs into its target `Utils/` subfolder (Hooks/Utils/,
     Input/Utils/, Vim/Utils/, Package/Utils/, Navigation/Utils/, ToolWindows/Utils/).
  2. Apply the 5 renames (`git mv` + class rename): WindowMatrix→WindowNavigator,
     CardinalNavigationConstants→NavigationConstants, UtilityMethods→WindowFrameUtils,
     RectCoordinate→WindowRect, WindowAdapter→WindowFrameAdapter.
  3. Update every `using` directive + fully-qualified reference across MyExtension/ +
     tests/NeoVisual.Tests/Program.cs for the moved files (namespaces unchanged, so
     only the type names change for the 5 renames; the folder moves do NOT change
     namespaces).
  4. Update the doc-ref lint: remove `CardinalMovment`/`CardinalNavigation` from the
     external allowlist; add the renamed symbols.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests` all pass (count unchanged); no `[NeoVisual]`/`[Telescope]`/
  `[Hook]`/`[MyExtension]` log literal changed.
- **Fails-if:** build errors (missed rename/reference); any NeoVisual test regresses;
  a log literal changed.

## Phase 12 — Restructure: Telescope (folders + Utils subfolders)

- **BP steps:**
  1. `git mv` each helper .cs into its target `Utils/` subfolder (Overlay/Utils/,
     Finders/Utils/, Logging/Utils/).
  2. Update every reference across MyExtension/ + tests/ (namespaces unchanged, so
     only the file paths change — no type renames in Telescope).
  3. Keep `RootNamespace>Telescope</RootNamespace>` in Telescope.csproj.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project
  tests/Telescope.Tests` all pass (count unchanged); `dotnet run --project
  tests/NeoVisual.Tests` all pass.
- **Fails-if:** build errors (missed reference); any Telescope/NeoVisual test
  regresses; a log literal changed.

## Phase 13 — Restructure: reference sweep (docs, agents, tools, tests) + doc-ref lint

- **BP steps:**
  1. Update every doc/agent/tool reference to the moved/renamed paths:
     - AGENTS.md, docs/spec.md, docs/progress.md, docs/implementation_plan.md,
       docs/reviews/*.md, SKILL.md — every `MyExtension/*.cs`, `Telescope/*.cs`
       path + the 5 renamed type names.
     - `.opencode/agent/*.md` — every exact-path reference to the moved files.
     - `tools/lint/check-doc-refs.ps1` — update the allowlists (`CardinalMovment`,
       `CardinalNavigation`, the renamed symbols) + `$sourceRoots` if needed.
  2. Run `pwsh tools/lint/check-doc-refs.ps1` → `[PASS] ... 0 unresolved`.
- **Verify-with:** doc-ref lint clean; `dotnet build` 0 errors; both unit suites pass
  (counts unchanged).
- **Fails-if:** the doc-ref lint reports unresolved references (a moved/renamed path
  missed in the sweep); an agent file still references an old path.

## Phase 14 — Final verification (combined plan gate)

- **BP steps:**
  1. `dotnet build MyExtension.slnx` → 0 errors.
  2. `dotnet run --project tests/Telescope.Tests` → all pass (count unchanged).
  3. `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).
  4. `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved.
  5. Grep gate: no `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` log literal
     changed by the restructure (diff the log literals before/after).
  6. e2e deferred: full 35-scenario suite queued (E2E-RESTRUCTURE-2).
- **Verify-with:** the five gates above all pass.
- **Fails-if:** any gate fails; a log literal drifted; a unit count changed (unless a
  Phase 9 test deletion was the documented cause).

---

## Acceptance criteria

Every phase's acceptance criteria map to a diagnostic line and/or a unit test. The
master table (each row = a phase's gate):

| Phase | Gate (unit tests GREEN + build + existing suites) | Diagnostic contract preserved/added |
|-------|---------------------------------------------------|-------------------------------------|
| 0 | `Run_TryDispatch_InsertPlacementsNotPromptMotions`; `Run_HierarchyResolver_PrimaryFilePath` | `prompt-motion key=...` unchanged; `solution-explorer select file=...` (M8, correct file) |
| 1 | `Run_GrepFinder_*` (drop-Task.Run, behavior-preserving); `Run_PreviewTokenCache_*`; `Run_FileContentCache_*` (eviction); `Run_FileFinder_UsesProjectFileCache`; `Run_FzfFilter_IsAvailableBounded` | `grep hits=...`, `results count=...`, `preview caret=...` unchanged |
| 2 | `Run_FocusGuard_OwnsKeyboard`; `Run_KeybindingConfig_*`; `Run_InjectedKeyGuard_*` | `navigate direction=...` unchanged; ADD `[NeoVisual] navigate activated/no-op` (m47) |
| 3 | `Run_VimModeState_*` (mode-change); `Run_VimModeClassifier_*` | `vim-mode=Insert|Normal|Replace` unchanged |
| 4 | `Run_WindowNavigationEngine_*`; `Run_WindowNavigator_BuildActiveWindowsNullActive`; `Run_NavigationSettings_*` | `navigate direction=L/R/D/U` unchanged |
| 5 | `Run_WindowManager_*`; `Run_HierarchyResolver_*`; `Run_HierarchyForestBuilder_*` | `solution-explorer select file=...` unchanged |
| 6 | `Run_GeneralToolWindowController_*`; `Run_ActionTable_*`; `Run_TextMotionDispatcher_*` | `toolwindow-move key=... -> arrow vk=...` unchanged |
| 7 | `Run_LogFileWriter_*`; `Run_PaneFailureTracker_*`; deletions (count drops) | `[Hook]` single-stamped; no log literal change |
| 8 | harness self-checks | `Send-Text` case-fidelity (M3, deferred e2e) |
| 9 | `Run_FzfFilter_TimeoutAwaitsTasks`; test hermeticity tests | none changed |
| 10 | doc-ref lint clean after the doc updates | none changed |
| 11 | build + NeoVisual.Tests (count unchanged) + doc-ref lint (after Phase 13) | no log literal change |
| 12 | build + Telescope.Tests + NeoVisual.Tests (counts unchanged) | no log literal change |
| 13 | `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved; build + suites | no log literal change |
| 14 | build + both suites + doc-ref lint + log-literal diff gate | no log literal change |

## Unit test plan (summary)

- **tests/Telescope.Tests** (pure classes): `TryDispatch`/`TextMotionDispatcher`
  insert-placements-not-prompt-motions (M1), `PreviewRenderer` token cache (M4),
  `FileContentCache` eviction + read-once (m5/m27), `TextMotionNavigator.LineNumber`
  (m32), `ResultMapper` byDisplay cache (m33), `FileFinder` ProjectFileCache (m34),
  `FzfFilter` drop-Task.Run/IsAvailable-bounded/timeout-awaits (M2/M7/m50), `LogFileWriter`
  format (m6), `PaneFailureTracker` retry (m51), `Run_TextMotionDispatcher_*` (n11),
  test hermeticity (m53/m54/m57/m58).
- **tests/NeoVisual.Tests** (pure classes): `HierarchyResolver.PrimaryFilePath`
  (M8/m26/m59), `FocusGuard.OwnsKeyboard` (M5/m7), `KeybindingConfig` "+" classify
  (m42), `InjectedKeyGuard` TTL (m43), `VimModeState` mode-change (m41),
  `VimModeClassifier` (n1), `WindowNavigationEngine` single-pass + symmetric
  tolerance (m11/m48), `WindowMatrix` null-active-window (m44), `NavigationSettings`
  cache (m12), `WindowManager` per-type defaults (m22), `GeneralToolWindowController`
  KeyToArrowVk (m9), `ToolWindowControllerBase` W/B/E (m16), `HierarchyForestBuilder`
  (m19), test hermeticity (m55/m56).
- **No-seam** (build + existing suites + deferred e2e): M3, M6, m3, m10, m14, m18,
  m20, m21, m24, m25, m28 (wiring), m38, m39, m40, m45, m46, m47, m49, m60, m63,
  m64, m65, m66, m67, m68, m69, n2, n4, n5, n7, n8, n9, n10, n12, n13, n14, n15,
  n16, n17, n18, n19, n20, n21, and the entire restructure (Phases 11-14 — build +
  suites + doc-ref lint + deferred e2e).

## Diagnostics (new/changed log lines)

- ADD `[NeoVisual] navigate activated index=...` / `[NeoVisual] navigate no-op: <reason>`
  (m47 — outcome diagnostic).
- UNCHANGED (must stay byte-identical): `navigate direction=L/R/D/U`,
  `[Telescope] focus target=List|Preview`, `vim-mode=Insert|Normal|Replace`,
  `preview caret=... line=...`, `prompt-motion key=...`, `text-motion key=...` /
  `textinput-enter-input ...`, `[MyExtension] init <step> ok/failed`,
  `solution-explorer select file=...`, `toolwindow-move key=... -> arrow vk=...`.
- HARNESS (not product): `Send-Text` case-fidelity (M3), `results count=[1-9]\d*`
  at :1161 only (m64).

## Known-RED allowlist

None — no known-RED e2e scenario remains (per docs/progress.md). All fixes are
RED-proven by unit tests (or build + existing suites for no-seam items). The
restructure is behavior-preserving (no RED — verified by build + suites + doc-ref
lint + the deferred full-suite e2e re-run).

## E2E queue reference (deferred — see e2e-queue.md)

The following e2e scenarios are QUEUED (not created/executed until this plan is
GREEN in docs/progress.md and the user is on a VS-capable machine). Each asserts the
diagnostics listed above:

- **E2E-NCR-1** (M1): `telescope-mode` — `a`/`A`/`I` in the prompt enter insert mode
  (`Focus prompt => True, mode=insert` after `a`); the false-positive gate is
  replaced by a real assertion.
- **E2E-NCR-2** (M8): `explorer-open-navigation` — `g` opens the primary `.cs` file
  (`solution-explorer select file=.*\.cs`, not `.resx`).
- **E2E-NCR-M2/M4** (finder): `telescope-grep` / `telescope-preview` — `grep hits=...`,
  `preview caret=...` unchanged after the drop-Task.Run change (behavior-preserving).
- **E2E-NCR-M5** (FocusGuard): `neovisual-textinput-motions` + `neovisual-explorer-*`
  — routing unchanged after the single-source `ownsKeyboard` refactor.
- **E2E-NCR-M3** (harness): full suite — `Send-Text` case-fidelity (the
  `query='Program'` assertions now verify case).
- **E2E-NCR-M7** (test): `telescope-search` — fzf timeout path still falls back.
- **E2E-NCR-m47** (diagnostic): `neovisual-window-nav` — `navigate activated/no-op`
  outcome line.
- **E2E-RESTRUCTURE-2** (Phases 11-14): full 35-scenario suite — no behavior change
  after the folder/Utils restructure + the 5 renames (all diagnostics byte-identical).
- **E2E-NCR-BACKLOG** (M6 reconciliation): the prior plan's missing gates
  (E2E-NCR-1..2, E2E-NCR-M1/M2/M15 .. E2E-NCR-M26/M27/M28, E2E-RESTRUCTURE-1 from
  the 67-findings plan) are appended to `e2e-queue.md` in Phase 10 — they are the
  deferred gates for the ALREADY-GREEN 67-findings plan and must be run on a capable
  machine.

## Execution order note for neovim_hub

Execute phases in order 0 → 14. Phases 0-10 are the code-review fixes (each
RED-proven by its unit tests; phases 7-9 are deletions/refactors with
behavior-preserving gates; phase 10 is docs + the e2e-queue reconciliation). Phases
11-14 are the restructure (behavior-preserving — build + suites + doc-ref lint + the
deferred full-suite e2e re-run). The unit-only lane applies throughout — e2e is
deferred to `e2e-queue.md` (E2E-NCR-*, E2E-RESTRUCTURE-2, E2E-NCR-BACKLOG above).

---

# Build Plan (BP-n) — agent-executable steps

> **Lane:** unit-only. **NO RED evidence exists yet** — the unit tests named below are
> NOT written; `neovim_hub`'s `e2e-test-builder` writes them (RED) before the build-agent
> runs each step. Every Verify-with is a **unit test name + diagnostic format** (or
> build + existing suites + doc-ref lint for no-seam items). **No e2e scenario is a
> Verify-with gate** — e2e is deferred to the "E2E queue reference" section above
> (E2E-NCR-*, E2E-RESTRUCTURE-2, E2E-NCR-BACKLOG).
>
> **Execution:** one top-to-bottom pass, phases 0 → 14. Each phase header is a natural
> checkpoint; the phase gate at each boundary is the mid-point verify (build + the
> phase's unit tests GREEN + the phase's diagnostic contract intact).
>
> **Cross-cutting constraints (do not second-guess):**
> - net472 — no `IReadOnlySet<T>`; `ThreadHelper.ThrowIfNotOnUIThread()` on every VS-API
>   method; UI-thread affinity; `ExcludeAssets="runtime"` untouched.
> - Log-line-as-contract: any fix that touches a log site MUST preserve the emitted token
>   byte-identical (the UNCHANGED list in the plan's "Diagnostics" section).
> - The restructure (Phases 11-14) is behavior-preserving: no log literal, no public API
>   change (except the 5 approved renames), no test-count change; namespaces stay
>   UNCHANGED (so no MEF/DI wiring changes — the renamed types are `new`-instantiated or
>   static, never `[Export]`ed/registered).
> - Doc-ref lint (`pwsh tools/lint/check-doc-refs.ps1`) is the acceptance gate for every
>   step that renames/removes a doc-referenced symbol.

## Phase 0 — Functional bugs (M1, M8, m36, m52)

### BP-1 — M1 + m36: Telescope prompt `a`/`A`/`I` fall through to insert mode + caret preservation
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In `TryPromptMotion` (TelescopeOverlay.cs:583-597), capture the
  `out CaretPlacement? insertPlacement` from `TryDispatch.Handle(key, shift, navigator, out _)`
  (line 589). When `insertPlacement != null` (a/A/I), return `false` WITHOUT consuming the
  key and WITHOUT logging `prompt-motion` — so `a`/`A`/`I` fall through to
  `_keyHandler.Handle` (OverlayKeyHandler maps `OverlayKey.A`→`EnterInsertMode(CaretPlacement.End)`,
  `OverlayKey.I`→`EnterInsertMode(CaretPlacement.Current)`). The `prompt-motion key=... caret=...`
  log must NOT be emitted for a/A/I.
- **Verify-with:** `Run_TryDispatch_InsertPlacementsNotPromptMotions` (Telescope.Tests) —
  `TryDispatch.Handle(Key.A/A/I, ...)` reports a non-null `CaretPlacement` (RED today: the
  prompt consumes them); `Run_OverlayKeyHandler_*` — `OverlayKey.A`/`OverlayKey.I` map to
  `EnterInsertAppend`/`EnterInsert`; **m36:** `Run_OverlayKeyHandler_InsertAtCurrentPreservesCaret`
  — `EnterInsert(CaretPlacement.Current)` (i) preserves the prompt caret set by h/l/w/b/e/0/$
  (RED today: `FocusPrompt` resets `_promptBox.CaretIndex` to the end at TelescopeOverlay.cs:486).
  Diagnostic: `prompt-motion key=... caret=...` NOT emitted for a/A/I; the insert path emits
  `Focus prompt => True, mode=insert`.
- **Fails-if:** `Run_TryDispatch_InsertPlacementsNotPromptMotions` fails; `prompt-motion key=A ...`
  still logged; `a`/`A`/`I` still consumed by `TryPromptMotion`; **m36:**
  `Run_OverlayKeyHandler_InsertAtCurrentPreservesCaret` fails — `i` discards the caret position set
  by h/l/w/b/e/0/$ (CaretIndex reset in `FocusPrompt`).

### BP-2 — M8: Solution Explorer `g` picks the primary file
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`,
  `MyExtension/ToolWindows/HierarchyResolver.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `MapChildren` (SolutionExplorerController.cs:331), replace
  `pi.FileNames[(short)pi.FileCount]` (the LAST file of a multi-file item) with a call to the pure
  `HierarchyResolver.PrimaryFilePath(...)` over the item's file-name list (the primary file). Fix
  the stale comment at lines 329-330 (FileNames is 1-based; index 1 is the primary file's full
  path — the comment's "index 1 is the short name" is wrong). The helper takes PRIMITIVE inputs:
  `HierarchyResolver.PrimaryFilePath(IReadOnlyList<string> fileNames)` returns `fileNames[0]` (the
  primary file; `EnvDTE.ProjectItem` is a COM interface that cannot be constructed hermetically, so
  the helper takes a primitive string list — the caller extracts the names from the COM item).
- **Verify-with:** `Run_HierarchyResolver_PrimaryFilePath` (NeoVisual.Tests) — over a primitive
  multi-file string list, the primary (first) file is returned (RED today: the caller indexes the
  last file). Diagnostic: `[NeoVisual] solution-explorer select file=...` now logs the primary
  `.cs` path (not `.resx`).
- **Fails-if:** `Run_HierarchyResolver_PrimaryFilePath` fails; `solution-explorer select file=...`
  still logs the last file of a multi-file item.

### BP-3 — m52: exclude insert placements from the preview motion surface
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In `HandlePreviewKey` (TelescopeOverlay.cs:616-619), capture the
  `out CaretPlacement? insertPlacement` from `TryDispatch.Handle(...)` and return `false` when
  `insertPlacement != null` (a/A/I) — the preview is read-only, so insert placements must NOT move
  the caret there. The a/A/I keys fall through to the list/mode state machine instead.
- **Verify-with:** `Run_TryDispatch_InsertPlacementsNotPreviewMotions` (Telescope.Tests) —
  `TryDispatch.Handle(Key.A/A/I, ...)` reports a non-null `CaretPlacement` and the preview motion
  surface rejects it (RED today: the preview maps them). Diagnostic: `[Telescope] preview caret=... line=...`
  NOT emitted for a/A/I in the preview; `focus target=Preview` unchanged.
- **Fails-if:** `Run_TryDispatch_InsertPlacementsNotPreviewMotions` fails; a/A/I still move the
  caret in the read-only preview.

## Phase 1 — Finder path amortization (M2, M4, m5, m15, m27, m30, m31, m32, m33, m34, m37, m50)

### BP-4 — M2: GrepFinder drops the wasted `Task.Run` thread hop (fully scoped)
- **Files:** `Telescope/Finders/GrepFinder.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In `GrepFinder.GetCandidates` (GrepFinder.cs:109-121), DROP the
  `Task.Run(...).GetAwaiter().GetResult()` wrapper and run the per-file content scan inline (the
  scan is pure file I/O; the synchronous blocking is unchanged, only the wasted thread hop is
  removed). This is the "drop the Task.Run" alternative from the M2 fix direction — it is fully
  scoped to GrepFinder.cs and does NOT change the `IFinder.GetCandidates` / `FinderBase<THit>`
  interface (which would force changes to all 5 finders + both overlay call sites at
  TelescopeOverlay.cs:253,376). Keep the hermetic test path (injected enumerate + opener) and the
  `grep hits={hits.Count}` log byte-identical.
- **Verify-with:** behavior-preserving (no RED — the synchronous scan is unchanged): `Run_GrepFinder_*`
  (Telescope.Tests) — the hermetic `GrepFinder` (injected enumerate + opener) returns the same hits;
  `dotnet build` 0 errors; code review confirms the `Task.Run(...).GetAwaiter().GetResult()` wrapper
  is gone. Diagnostic: `[Telescope] grep hits=...` unchanged.
- **Fails-if:** `Run_GrepFinder_*` fails; `grep hits=...` missing or double-emitted; the
  `Task.Run(...).GetAwaiter().GetResult()` wrapper is still present (code review).

### BP-5 — M4: PreviewRenderer token/FlowDocument cache
- **Files:** `Telescope/Overlay/PreviewRenderer.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Add a pure mtime-keyed cache of the tokenized segments + built `FlowDocument`
  (keyed by `LastWriteTimeUtc`) so `SetContent` (PreviewRenderer.cs:62-123) re-tokenizes/rebuilds
  only on content change; keep `ApplyCaret`/`MoveToLine` per-selection. Extract the cache as a
  dependency-free class (e.g. `PreviewTokenCache`) for hermetic testing.
- **Verify-with:** `Run_PreviewTokenCache_UnchangedMtimeCached` /
  `Run_PreviewTokenCache_ChangedMtimeRetokenizes` (Telescope.Tests) — unchanged mtime returns the
  cached segments (no re-tokenize), changed mtime re-tokenizes (RED: cache class doesn't exist →
  compile error). Diagnostic: `[Telescope] preview tokens=...` unchanged (same count on cache hit).
- **Fails-if:** `Run_PreviewTokenCache_*` fails; `preview tokens=...` count changes on a cache
  hit; the FlowDocument is rebuilt per selection change.

### BP-6 — m5: `MyExtensionPackage.ReadLine` read-once per gather
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In the references-finder gather path, replace the per-hit `ReadLine`
  (MyExtensionPackage.cs:665-675 — re-opens + re-scans the file prefix per reference hit) with a
  read-once-per-gather `FileContentCache` (or reuse the Roslyn `SourceText`).
- **Verify-with:** `Run_FileContentCache_*` (Telescope.Tests) — the cache returns the same line
  content across repeated reads without re-reading (the read-once seam). Diagnostic:
  `[Telescope] references gathered reads=... writes=...` unchanged.
- **Fails-if:** `Run_FileContentCache_*` fails; the references gather still re-reads the file
  per hit.

### BP-7 — m27: FileContentCache LRU cap at construction sites
- **Files:** `Telescope/Overlay/PreviewRenderer.cs`, `Telescope/Finders/CodeIssuesFinder.cs`,
  `Telescope/Finders/GrepFinder.cs`
- **Change:** Pass a cap (e.g. 500) at the three `new FileContentCache()` construction sites
  (PreviewRenderer.cs:34, CodeIssuesFinder.cs:40, GrepFinder.cs:29) — `new FileContentCache(500)`.
  The `FileContentCache` ctor already takes `int? maxEntries` (FileContentCache.cs:30); the m27 bug
  is that no production caller passes it.
- **Verify-with:** code-review assertion (NO new RED test — `Run_FileContentCache_EvictsOldest`
  already exists at tests/Telescope.Tests/Program.cs:2244 and passes; the LRU eviction is
  implemented): the three construction sites pass a cap + `dotnet build` 0 errors.
- **Fails-if:** a construction site still uses `new FileContentCache()` with no cap (code review);
  build error.

### BP-8 — m31: reuse `_promptNavigator` field
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** Replace the per-keystroke `new TextMotionNavigator()` in `TryPromptMotion`
  (TelescopeOverlay.cs:585) and `ApplyInsertCaret` (TelescopeOverlay.cs:498) with a reused
  `_promptNavigator` field.
- **Verify-with:** behavior-preserving — `Run_TryDispatch_*` / `Run_TextMotionNavigator_*`
  (Telescope.Tests) still pass (motion math unchanged). Diagnostic: `prompt-motion key=... caret=...`
  unchanged.
- **Fails-if:** a motion test regresses; `prompt-motion` caret drifts.

### BP-9 — m32: `TextMotionNavigator.LineNumber` cache
- **Files:** `Telescope/Overlay/TextMotionNavigator.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Cache the line number incrementally (or use the existing `LineIndex`) instead of the
  O(n) scan in `LineNumber` (TextMotionNavigator.cs:37-51).
- **Verify-with:** `Run_TextMotionNavigator_LineNumber` (Telescope.Tests) — behavior-preserving
  (same line numbers across motions). Diagnostic: `[Telescope] preview caret=... line=...` unchanged.
- **Fails-if:** `Run_TextMotionNavigator_LineNumber` fails; `preview caret=... line=...` drifts.

### BP-10 — m33: ResultMapper byDisplay cache
- **Files:** `Telescope/Overlay/ResultMapper.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Cache the `GroupBy(...).ToDictionary(...)` byDisplay map per snapshot
  (ResultMapper.cs:21-24) instead of rebuilding per keystroke.
- **Verify-with:** `Run_ResultMapper_*` (Telescope.Tests) — behavior-preserving (same mapping incl.
  duplicate-safe ordinal consumption). Diagnostic: `[Telescope] result-mapper unknown display: {display}`
  unchanged.
- **Fails-if:** `Run_ResultMapper_*` fails; the unknown-display warning drifts.

### BP-11 — m34: FileFinder routes through ProjectFileCache
- **Files:** `Telescope/Finders/FileFinder.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Route `FileFinder.GatherHits` (FileFinder.cs:62) through the shared `ProjectFileCache`
  instead of calling `ProjectFiles.Enumerate` directly.
- **Verify-with:** `Run_FileFinder_UsesProjectFileCache` (Telescope.Tests) — the finder's enumerate
  delegate is served by the cache (single enumeration across gathers). Diagnostic:
  `[Telescope] opened file: ...` unchanged.
- **Fails-if:** `Run_FileFinder_UsesProjectFileCache` fails; the finder re-enumerates per gather.

### BP-12 — m37 + m50: FzfFilter availability cache + latency note
- **Files:** `Telescope/Filter/FzfFilter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** (m50) Cache `IsAvailable()` once per session (or probe async) so the 500ms probe
  (FzfFilter.cs:64-88) doesn't run on every overlay open. (m37) Document the per-keystroke
  subprocess spawn as bites-later (no code change; the `--listen`/in-process matcher is deferred
  until latency is measured).
- **Verify-with:** the availability cache is in place — `Run_FzfFilter_IsAvailableBounded`
  (Telescope.Tests) passes with the existing assertion. NOTE (phase-ordering dependency): the
  bounded `< 1s` wall-clock assertion is DEFERRED to Phase 9 (BP-61, m58 — the clock injection
  lands there); BP-12 does NOT inject the clock. Diagnostic: `[Telescope] fzf unavailable — showing
  unfiltered list` unchanged (once at overlay open).
- **Fails-if:** `Run_FzfFilter_IsAvailableBounded` fails; the probe still runs per overlay open
  (cache not in place).

### BP-13 — m15: TextMotionHelper returns the focused box once + reuses the navigator
- **Files:** `MyExtension/ToolWindows/TextMotionHelper.cs`
- **Change:** In `TryMoveFocusedSurface` (TextMotionHelper.cs:70-125), resolve the focused text
  surface ONCE (a single `FindFocusedTextBox()` walk) and reuse it for both the motion apply and
  the caret-style call — currently `ApplyMotionToBox` re-walks `FindFocusedTextBox()` up to 3×
  (TextMotionHelper.cs:82,149,158) and copies the full buffer text twice per motion key. No-seam
  (WPF) — the motion math and the `text-motion key=... caret=...` / `textinput-enter-input ...`
  log contract are unchanged.
- **Verify-with:** build 0 errors; existing suites pass (behavior-preserving). Diagnostic:
  `[NeoVisual] text-motion key=... caret=...` / `[NeoVisual] textinput-enter-input start|end|after caret=...`
  unchanged (deferred e2e `neovisual-textinput-motions`).
- **Fails-if:** build error; the motion/caret-style behavior changes; a log literal drifts.

### BP-14 — m30: FileFinder.OpenHit drops the outer `File.Exists` guard
- **Files:** `Telescope/Finders/FileFinder.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In `OpenHit` (FileFinder.cs:74-93), drop the outer `if (!File.Exists(hit.FilePath)) return;`
  guard (FileFinder.cs:78) and let `HitOpener` own the missing-file no-op (it guards internally).
  The hermetic `_testOpener` branch keeps its own behavior (a missing file is a no-op there too).
- **Verify-with:** `Run_HitOpener_*` (Telescope.Tests) — behavior-preserving (a missing file is a
  no-op on both the hermetic and real open paths; an existing file opens). Diagnostic:
  `[Telescope] opened file: ...` unchanged.
- **Fails-if:** `Run_HitOpener_*` fails; a missing file throws instead of no-op'ing; the
  opened-file line drifts.

## Phase 2 — FocusGuard + routing (M5, m7, m42, m43, m47, n14, n15, n16)

### BP-15 — M5: single-source `ownsKeyboard`
- **Files:** `MyExtension/Input/InputHandler.cs`, `MyExtension/ToolWindows/FocusGuard.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Route `EditorFocusedVeto` (InputHandler.cs:104-109) through the same cached
  `_windowManager.IsTextInputType` used by `HasToolWindowActionKeys` (InputHandler.cs:84-90) —
  replace `GeneralToolWindowController.IsTextInputType(_windowManager.Type)` with
  `_windowManager.IsTextInputType` (or pass a single `ownsKeyboard` bool into `FocusGuard`).
- **Verify-with:** `Run_FocusGuard_OwnsKeyboard` (NeoVisual.Tests) — behavior-preserving (RED:
  signature change → compile error). Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...`
  unchanged.
- **Fails-if:** `Run_FocusGuard_OwnsKeyboard` fails; the two routing formulations disagree (a
  text-input window vetoed or a nav window not vetoed).

### BP-16 — m7: hoist the 5-arg `ShouldRouteToolWindowKey` call
- **Files:** `MyExtension/Input/InputHandler.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Hoist the identical 5-arg `FocusGuard.ShouldRouteToolWindowKey(...)` call
  (InputHandler.cs:308,436,468) into one private helper.
- **Verify-with:** `Run_FocusGuard_*` (NeoVisual.Tests) — behavior-preserving.
- **Fails-if:** `Run_FocusGuard_*` fails; routing behavior changes.

### BP-17 — m42: `BuildBindings` modifier-prefix classification
- **Files:** `MyExtension/Input/InputHandler.cs`, `MyExtension/Input/KeybindingConfig.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Extract a pure classification method into `KeybindingConfig` —
  `KeybindingConfig.IsSimpleShortcut(string key)` — a modifier-prefix check (true only for
  `Ctrl+`/`Shift+`/`Alt+` prefixes, not any `+`). In `BuildBindings` (InputHandler.cs:181-188),
  replace `pair.Key.Contains("+")` with `KeybindingConfig.IsSimpleShortcut(pair.Key)` so a leader
  key like `F,+` is classified as a leader sequence, not a simple shortcut.
- **Verify-with:** `Run_KeybindingConfig_IsSimpleShortcut` (NeoVisual.Tests) — a `F,+` leader
  binding is classified as a leader sequence, not a simple shortcut (RED: misclassified today).
  Diagnostic: `[NeoVisual] leader-binding executed: ...` unchanged.
- **Fails-if:** `Run_KeybindingConfig_IsSimpleShortcut` fails; a `F,+` binding never matches.

### BP-18 — m43: InjectedKeyGuard TTL
- **Files:** `MyExtension/Hooks/InjectedKeyGuard.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Add a bounded TTL to the per-VK pending counter (InjectedKeyGuard.cs:42-67) so a
  stale pending record cannot consume the next physical key-down (or accept+document the current
  behavior).
- **Verify-with:** `Run_InjectedKeyGuard_*` (NeoVisual.Tests) — behavior-preserving (a stale record
  expires; a fresh record still consumes once).
- **Fails-if:** `Run_InjectedKeyGuard_*` fails; a stale record swallows a physical key.

### BP-19 — m47 + n14 + n15: Navigate outcome diagnostic + UI-thread assert + volatile
- **Files:** `MyExtension/Input/InputHandler.cs`, `MyExtension/Input/LeaderSequenceMatcher.cs`
- **Change:** (m47) In `Navigate` (InputHandler.cs:496-505), log the outcome:
  `[NeoVisual] navigate activated index=...` when a target was activated,
  `[NeoVisual] navigate no-op: <reason>` when not (no-seam diagnostic — deferred e2e, E2E queue
  reference E2E-NCR-m47). (n14) Add `ThreadHelper.ThrowIfNotOnUIThread()` at the top of `Navigate`.
  (n15) Make `LeaderSequenceMatcher._active` volatile (or correct the doc).
- **Verify-with:** build 0 errors (n14/n15); the new diagnostic format
  `[NeoVisual] navigate activated index=...` / `[NeoVisual] navigate no-op: <reason>` is emitted.
  `navigate direction=L/R/D/U` unchanged.
- **Fails-if:** build error; `navigate direction=...` drifts; the outcome line is missing.

### BP-20 — n16: PopupNavigation accept+document or gate
- **Files:** `MyExtension/Input/PopupNavigation.cs`
- **Change:** Accept+document the unconditional Ctrl+N/P injection (PopupNavigation.cs:39-53) or
  gate on `ICompletionBroker.IsCompletionActive`.
- **Verify-with:** build 0 errors; existing suites pass.
- **Fails-if:** build error; Ctrl+N/P behavior changes unexpectedly.

## Phase 3 — Vim interop (m38, m39, m40, m41, m3, n1)

### BP-21 — m38: `GetVim` latch only on success
- **Files:** `MyExtension/Vim/VimModeSource.cs`
- **Change:** In `GetVim` (VimModeSource.cs:398-428), set `_resolved = true` only when `vim != null`
  (currently latches "no VsVim" on an early resolution).
- **Verify-with:** build 0 errors; existing suites pass (no-seam VsVim reflection — deferred e2e).
- **Fails-if:** build error; VsVim resolution permanently latches "not found".

### BP-22 — m39: `Attach` re-point only on focus gain
- **Files:** `MyExtension/Vim/VimModeSource.cs`
- **Change:** In `Attach`/`SubscribeBuffer` (VimModeSource.cs:117-125,177-183), only re-point
  `_currentBuffer`/`_currentTextBuffer` on focus gain (a background/peek view must not hijack the
  focused mode state).
- **Verify-with:** build 0 errors; existing suites pass (no-seam VsVim reflection — deferred e2e).
- **Fails-if:** build error; a background view hijacks the focused mode.

### BP-23 — m40: `Detach` refcount shared buffers
- **Files:** `MyExtension/Vim/VimModeSource.cs`
- **Change:** In `Detach` (VimModeSource.cs:128-136), refcount shared text buffers so two views
  sharing a buffer don't kill each other's subscription.
- **Verify-with:** build 0 errors; existing suites pass (no-seam VsVim reflection — deferred e2e).
- **Fails-if:** build error; closing one view kills the other's SwitchedMode subscription.

### BP-24 — m41: `VimModeState` mode-change return
- **Files:** `MyExtension/Vim/VimModeState.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `OnViewLostFocus`/`OnViewClosed` (VimModeState.cs:57-83) return the mode-change (not
  just the typing-change), so `vim-mode=Unknown` is emitted on editor-focus loss.
- **Verify-with:** `Run_VimModeState_*` (NeoVisual.Tests) — the mode-change return is present (RED:
  missing today). Diagnostic: `[NeoVisual] vim-mode=Insert|Normal|Replace` unchanged;
  `vim-mode=Unknown` emitted on focus loss.
- **Fails-if:** `Run_VimModeState_*` fails; `vim-mode=Unknown` never emitted on focus loss.

### BP-25 — m3 + n1: VimBufferSubscriptions comment + VimModeState aliases
- **Files:** `MyExtension/Vim/VimBufferSubscriptions.cs`, `MyExtension/Vim/VimModeState.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** (m3) Fix the stale "NOT wired yet" comment (VimBufferSubscriptions.cs:16-19) —
  `VsVimModeSource` calls it. (n1) Delete the `Normal`/`Insert`/`Replace` const aliases
  (VimModeState.cs:22-24), use `VimModeClassifier` (update the M17 test surface).
- **Verify-with:** `Run_VimModeClassifier_*` (NeoVisual.Tests) — the classifier is the single owner
  of the truth table. Build 0 errors.
- **Fails-if:** `Run_VimModeClassifier_*` fails; build error (a stale alias reference).

## Phase 4 — Navigation robustness (m11, m12, m13, m14, m44, m45, m46, m48, m49, n3, n4, n5, n6, n19)

### BP-26 — m11 + n3: `SelectTarget` single pass + inline predicates
- **Files:** `MyExtension/Navigation/WindowNavigationEngine.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (m11) Rewrite `SelectTarget` (WindowNavigationEngine.cs:18-67) as a single pass
  collecting (index, gap, adjacency) for passing candidates. (n3) Inline the 3-predicate `Pipeline`
  (WindowNavigationEngine.cs:10-16).
- **Verify-with:** `Run_WindowNavigationEngine_*` (NeoVisual.Tests) — behavior-preserving (same
  target index for the same rects/direction/settings).
- **Fails-if:** `Run_WindowNavigationEngine_*` fails; the target index changes for a known rect set.

### BP-27 — m48: symmetric Down/Up tolerance
- **Files:** `MyExtension/Navigation/WindowNavigationEngine.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Make the Down/Up tolerance symmetric (WindowNavigationEngine.cs:85-86) — the `> 1`
  Down tolerance vs the bare `<` Up.
- **Verify-with:** `Run_WindowNavigationEngine_*` (NeoVisual.Tests) — the 1px-gap case is handled
  symmetrically (RED: today Down rejects a 1px-gap candidate Up accepts).
- **Fails-if:** `Run_WindowNavigationEngine_*` fails; a 1px-gap candidate is rejected in one
  direction but accepted in the other.

### BP-28 — m12: NavigationSettings cache
- **Files:** `MyExtension/Navigation/NavigationSettings.cs`, `MyExtension/Navigation/WindowMatrix.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Cache the `NavigationSettings` (from `FromSystemDpi()`, WindowMatrix.cs:39) instead of
  re-reading system DPI per navigation.
- **Verify-with:** `Run_NavigationSettings_*` (NeoVisual.Tests) — behavior-preserving (same divides
  for the same DPI).
- **Fails-if:** `Run_NavigationSettings_*` fails; the divides change.

### BP-29 — m13: `LinkedTo` precomputed key set
- **Files:** `MyExtension/Navigation/WindowAdapter.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Precompute a key set / index in `LinkedTo` (WindowAdapter.cs:89-95) instead of the
  O(n·m) `CompareWindows` COM reads.
- **Verify-with:** behavior-preserving — `Run_WindowAdapter_*` (NeoVisual.Tests) still pass.
- **Fails-if:** `Run_WindowAdapter_*` fails; the linked-window set changes.

### BP-30 — m14 + m45 + m49: null guards (GetIVsUIShell, Activate/AutoHides, VSFPROPID_Type)
- **Files:** `MyExtension/Navigation/UtilityMethods.cs`, `MyExtension/Navigation/WindowAdapter.cs`,
  `MyExtension/ToolWindows/WindowManager.cs`
- **Change:** (m14) Null-guard `GetIVsUIShell` (UtilityMethods.cs:17-22) + log. (m45) Null-guard
  `_dte` in `Activate()`/`AutoHides()` (WindowAdapter.cs:42,48). (m49) Check the `VSFPROPID_Type`
  HRESULT + null before `(int)value` (WindowManager.cs:240-241).
- **Verify-with:** build 0 errors; existing suites pass (no-seam VS-coupled).
- **Fails-if:** build error; an NRE escapes the navigation catch and is misdiagnosed as a binding
  failure.

### BP-31 — m44: null-active-window check before `LinkedTo` (pure helper)
- **Files:** `MyExtension/Navigation/WindowMatrix.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Extract a pure static helper `WindowMatrix.BuildActiveWindows(EnvDTE.Window? active,
  IReadOnlyList<WindowAdapter> adapters)` that null-checks `active` BEFORE linking (moves the
  `activeWindow == null` check at WindowMatrix.cs:50-58 ahead of `WindowAdapter.LinkedTo(...)` at
  WindowMatrix.cs:47, so the graceful-degradation branch is reachable). The ctor calls it.
  **Phase 4 extracts the helper under the CURRENT names** (`WindowMatrix`/`WindowAdapter`); the
  class is renamed `WindowMatrix`→`WindowNavigator` and `WindowAdapter`→`WindowFrameAdapter` in
  Phase 11 (BP-69), and BP-70 updates the helper + test type references to the post-restructure
  names (`WindowNavigator.BuildActiveWindows(EnvDTE.Window? active, IReadOnlyList<WindowFrameAdapter>
  adapters)`).
- **Verify-with:** `Run_WindowNavigator_BuildActiveWindowsNullActive` (NeoVisual.Tests) — a null
  active window degrades to an empty/no-op window list (RED: today the deref precedes the null
  check). The test name uses the post-restructure class name; the type reference is updated in
  BP-70.
- **Fails-if:** `Run_WindowNavigator_BuildActiveWindowsNullActive` fails; a null active window
  throws.

### BP-32 — m46: per-window fault isolation
- **Files:** `MyExtension/Navigation/WindowMatrix.cs`
- **Change:** Wrap each `w.Rect` read in `_activeWindows.Select(w => w.Rect).ToList()`
  (WindowMatrix.cs:94) in a per-window try/catch so one stale frame can't kill all navigation.
- **Verify-with:** build 0 errors; existing suites pass (no-seam VS-coupled).
- **Fails-if:** build error; one stale frame still kills all navigation.

### BP-33 — n4 + n5 + n6 + n19: Direction using, ExtractFrames assert, NavigationSnapshot struct, frame4 fallback log
- **Files:** `MyExtension/Navigation/Direction.cs`, `MyExtension/Navigation/WindowAdapter.cs`,
  `MyExtension/Navigation/NavigationSnapshot.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (n4) Move `using System;` to the top of Direction.cs:3. (n5) Assert
  `ThrowIfNotOnUIThread` before the `ExtractFrames` iterator (WindowAdapter.cs:97-99). (n6) Make
  `NavigationSnapshot` a `readonly struct` (NavigationSnapshot.cs:9). (n19) Log the
  `RectCoordinate.Empty` fallback once when a frame lacks `IVsWindowFrame4` (WindowAdapter.cs:118,131).
- **Verify-with:** build 0 errors; `Run_WindowAdapter_*` / `Run_NavigationSnapshot_*`
  (NeoVisual.Tests) pass (behavior-preserving).
- **Fails-if:** build error; a navigation test regresses.

## Phase 5 — Controller/state (m20, m21, m22, m23, m24, m25, m26, m59, n7, n8, n9, n10)

### BP-34 — m20 + m21: FocusKeeper constant + stop-on-close
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** (m20) Replace the literal `1500` (SolutionExplorerController.cs:234) with the
  `FocusKeeperDurationMs` constant. (m21) Add a stop-on-close to the FocusKeeper re-assert (verify
  the window is still visible before re-asserting).
- **Verify-with:** build 0 errors; existing suites pass (no-seam VS-coupled).
- **Fails-if:** build error; the keeper re-asserts after the window closes.

### BP-35 — m22: per-type default controllers
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Replace the shared `_defaultController` (WindowManager.cs:25,186-190) with per-type
  default instances so `_isInputMode` doesn't leak across unknown-GUID tool windows.
- **Verify-with:** `Run_WindowManager_*` (NeoVisual.Tests) — the shared-instance leak is gone (RED:
  observable today).
- **Fails-if:** `Run_WindowManager_*` fails; one tool window's input mode leaks into another.

### BP-36 — m23: drop the dead ctor branch
- **Files:** `MyExtension/ToolWindows/GeneralToolWindowController.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Drop the `_isInputMode = IsTextInputType(type)` ctor branch
  (GeneralToolWindowController.cs:31) if the base/initial-mode flow already covers it.
- **Verify-with:** behavior-preserving — `Run_GeneralToolWindowController_*` (NeoVisual.Tests) pass.
- **Fails-if:** `Run_GeneralToolWindowController_*` fails; the initial mode changes.

### BP-37 — m24: style the editor view on insert placements
- **Files:** `MyExtension/ToolWindows/TextMotionHelper.cs`
- **Change:** In the editor-view path (TextMotionHelper.cs:98), pass `styleCaret: true` (or style
  the editor view on insert placements) so the block caret doesn't persist in insert mode after
  `a`/`A`/`I`.
- **Verify-with:** build 0 errors; existing suites pass (no-seam WPF editor view — deferred e2e).
- **Fails-if:** build error; the block caret persists in insert mode.

### BP-38 — m25: unify the catch/log helper
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** Unify the 3× try/catch + `Log(...failed: {msg})` swallow idiom
  (SolutionExplorerController.cs:137,248,376) into one helper.
- **Verify-with:** build 0 errors; existing suites pass (no-seam).
- **Fails-if:** build error; a failure path stops logging.

### BP-39 — m26 + m59: HierarchyResolver `.cs` filter + `g` pick test
- **Files:** `MyExtension/ToolWindows/HierarchyResolver.cs`,
  `MyExtension/ToolWindows/HierarchyForestBuilder.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (m26) Move the `.cs` filter into `HierarchyResolver.FirstSourceFilePath`
  (HierarchyResolver.cs:22-36) (or rename to `FirstPhysicalFilePath`). (m59) Extract the pure pick
  (which node to select) and test it.
- **Verify-with:** `Run_HierarchyResolver_*` (NeoVisual.Tests) — an unfiltered forest returns a
  `.cs` file, not a non-.cs file (RED: today it returns the first physical file regardless of
  extension). `Run_HierarchyForestBuilder_*` still pass. Diagnostic: `[NeoVisual] solution-explorer
  select file=...` unchanged.
- **Fails-if:** `Run_HierarchyResolver_*` fails; `g` selects a non-.cs file.

### BP-40 — n7 + n8 + n9 + n10: WindowManager reindent, keeperRef, ComputeTextInputSurfaceFocused, arrow-fallback log
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `MyExtension/ToolWindows/FocusKeeper.cs`,
  `MyExtension/ToolWindows/TextMotionHelper.cs`
- **Change:** (n7) Reindent the WindowManager class body (WindowManager.cs:11). (n8) Delete the
  redundant `keeperRef` local (FocusKeeper.cs:18). (n9) Skip `ComputeTextInputSurfaceFocused()` in
  the non-tool branch (WindowManager.cs:264). (n10) Align the arrow-fallback log contract
  (TextMotionHelper.cs:120) with `TryMoveArrow`'s bare `toolwindow-move key=... -> arrow vk=...`.
- **Verify-with:** build 0 errors; existing suites pass. Diagnostic:
  `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged (n10 aligns the fallback to the
  same contract).
- **Fails-if:** build error; the arrow-fallback log drifts from the contract.

## Phase 6 — Duplication merges (m8, m9, m16, m17, m18, m19, m35, n11)

### BP-41 — m9 + m17: single-source `KeyToArrowVk`
- **Files:** `MyExtension/ToolWindows/GeneralToolWindowController.cs`,
  `MyExtension/ToolWindows/TextMotionHelper.cs`, `MyExtension/ToolWindows/SolutionExplorerController.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** (m9) Single-source `KeyToArrowVk` (GeneralToolWindowController.cs:66-76 +
  TextMotionHelper.cs:117). (m17) Make SolutionExplorerController's H/L use `KeyToArrowVk` instead
  of hardcoded `VK_LEFT`/`VK_RIGHT` (SolutionExplorerController.cs:46-47).
- **Verify-with:** `Run_GeneralToolWindowController_*` (NeoVisual.Tests) — behavior-preserving.
  Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged.
- **Fails-if:** `Run_GeneralToolWindowController_*` fails; the arrow VK mapping drifts.

### BP-42 — m16: W/B/E wiring to `ToolWindowControllerBase`
- **Files:** `MyExtension/ToolWindows/ToolWindowControllerBase.cs`,
  `MyExtension/ToolWindows/TextInputToolWindowController.cs`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Move the duplicated W/B/E vim-motion action wiring
  (TextInputToolWindowController.cs:44-46 + SolutionExplorerController.cs:50-52) into
  `ToolWindowControllerBase`.
- **Verify-with:** `Run_ActionTable_*` (NeoVisual.Tests) — behavior-preserving.
- **Fails-if:** `Run_ActionTable_*` fails; W/B/E stop routing.

### BP-43 — m18: share `GetParent`
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `MyExtension/ToolWindows/TextMotionHelper.cs`
- **Change:** Share one visual/logical-tree `GetParent` helper (WindowManager.cs:131-153 +
  TextMotionHelper.cs:241-263).
- **Verify-with:** build 0 errors; existing suites pass (no-seam WPF).
- **Fails-if:** build error; the tree walk changes behavior.

### BP-44 — m19: unify the DTE tree-walk seams
- **Files:** `MyExtension/ToolWindows/HierarchyForestBuilder.cs`, `Telescope/Finders/HierarchyWalker.cs`,
  `Telescope/Finders/ProjectFiles.cs`, `tests/NeoVisual.Tests/Program.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Unify `HierarchyForestBuilder` vs `HierarchyWalker`/`ProjectFiles` on one walker.
- **Verify-with:** `Run_HierarchyForestBuilder_*` (NeoVisual.Tests) + `Run_HierarchyWalker_*`
  (Telescope.Tests) — behavior-preserving.
- **Fails-if:** either suite fails; the walk order changes.

### BP-45 — m35: `ApplyPromptCaretStyle` delegates to the shared helper
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`, `MyExtension/ToolWindows/TextMotionHelper.cs`
- **Change:** `ApplyPromptCaretStyle` (TelescopeOverlay.cs:603-613) delegates to
  `TextMotionHelper.ApplyCaretStyle` instead of duplicating it.
- **Verify-with:** behavior-preserving — `Run_TextMotionNavigator_*` / `Run_TryDispatch_*`
  (Telescope.Tests) pass.
- **Fails-if:** a Telescope test regresses; the prompt caret style changes.

### BP-46 — n11: merge `TryDispatch` into `TextMotionDispatcher`
- **Files:** `Telescope/Overlay/TryDispatch.cs`, `Telescope/Overlay/TextMotionDispatcher.cs`,
  `tests/Telescope.Tests/Program.cs`
- **Change:** Merge the 25-line `TryDispatch` wrapper (TryDispatch.cs:12-24) into
  `TextMotionDispatcher`; update callers (TelescopeOverlay.cs:589,618).
- **Verify-with:** `Run_TryDispatch_*` → `Run_TextMotionDispatcher_*` (Telescope.Tests) — the
  renamed tests pass. Diagnostic: `[Telescope] prompt-motion key=... caret=...` /
  `preview caret=... line=...` unchanged.
- **Fails-if:** `Run_TextMotionDispatcher_*` fails; a motion stops routing.

### BP-47 — m8: inject the package's `_launcher` into `InputHandler`
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`, `MyExtension/Hooks/GlobalKeyboardHook.cs`,
  `MyExtension/Input/InputHandler.cs`
- **Change:** Stop constructing a second `TelescopeLauncher` in `InputHandler` (InputHandler.cs:124).
  Change the `InputHandler` ctor to accept the package's `_launcher` (MyExtensionPackage.cs:84) and
  store it; `GlobalKeyboardHook` (GlobalKeyboardHook.cs:62) receives the package's `_launcher` and
  passes it through to `InputHandler`. The `telescope` init step (MyExtensionPackage.cs:81-86) stays
  the single construction site. No-seam (VS-coupled) — build + deferred e2e.
- **Verify-with:** build 0 errors; existing suites pass. Diagnostic: `[Telescope] open finder=...`
  / `[Telescope] overlay closed` unchanged (the injected launcher opens the same overlay).
- **Fails-if:** build error (ctor signature change); a second `TelescopeLauncher` is still
  constructed; the overlay open path regresses.

## Phase 7 — Dead code + logging (m1, m2, m10, m28, m6, m29, m51, n2, n12, n13, n17, n18)

### BP-48 — m1: delete `HasToolWindowActionKeys` + fix docs
- **Files:** `MyExtension/Input/InputHandler.cs`, `AGENTS.md`,
  `.opencode/skills/vs-extension-dev/SKILL.md`
- **Change:** Delete the dead `InputHandler.HasToolWindowActionKeys` property (InputHandler.cs:79-92);
  fix the AGENTS.md/SKILL.md references that document it as the pre-filter mechanism.
- **Verify-with:** `Run_FocusGuard_*` (NeoVisual.Tests) — the guard is already tested; build 0
  errors. Doc-ref lint clean (the removed symbol's doc refs are updated).
- **Fails-if:** build error; a doc still references `HasToolWindowActionKeys` (doc-ref lint flags it).

### BP-49 — m2: delete `VimModeState.ResolveOnce` + its test
- **Files:** `MyExtension/Vim/VimModeState.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Delete `ResolveOnce` + `_resolved` latch (VimModeState.cs:90-109) and its test.
- **Verify-with:** NeoVisual.Tests count drops by the deleted test (deterministic signal); build 0
  errors.
- **Fails-if:** build error; the count doesn't drop by exactly 1.

### BP-50 — m10 + m28: delete `WindowAdapter._frame` + `OverlayClosed` event
- **Files:** `MyExtension/Navigation/WindowAdapter.cs`, `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** (m10) Delete the never-read `_frame` field (WindowAdapter.cs:18,25). (m28) Delete the
  `OverlayClosed` event + raise (TelescopeOverlay.cs:240,331).
- **Verify-with:** build 0 errors; existing suites pass (behavior-preserving).
- **Fails-if:** build error; a subscriber reference remains.

### BP-51 — m6: single-stamp `[Hook]` lines
- **Files:** `MyExtension/Hooks/GlobalKeyboardHook.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Drop the local timestamp in `GlobalKeyboardHook.Log` (GlobalKeyboardHook.cs:200) so
  `LogFileWriter.FormatLine` is the only stamper.
- **Verify-with:** `Run_LogFileWriter_*` (Telescope.Tests) — the format contract (single
  `HH:mm:ss.fff ` prefix). Diagnostic: `[Hook]` lines single-stamped.
- **Fails-if:** `Run_LogFileWriter_*` fails; a `[Hook]` line is still double-stamped.

### BP-52 — m29: `DteFileOpener` uses `TelescopeLog`
- **Files:** `Telescope/Finders/DteFileOpener.cs`
- **Change:** Replace the manual `[Telescope] ` prefix concat (DteFileOpener.cs:19) with
  `TelescopeLog.Log`.
- **Verify-with:** behavior-preserving — `Run_FileFinder_*` / `Run_GrepFinder_*` (Telescope.Tests)
  pass. Diagnostic: `[Telescope] opened file: ...` unchanged.
- **Fails-if:** a finder test regresses; the opened-file line drifts.

### BP-53 — m51: `NeoVisualLog.EnsurePane` retry latch via `PaneFailureTracker`
- **Files:** `Telescope/Logging/NeoVisualLog.cs`, `Telescope/Logging/PaneFailureTracker.cs`,
  `tests/Telescope.Tests/Program.cs`
- **Change:** Extract the retry latch into the pure `PaneFailureTracker` (add
  `ShouldRetry()`/`RecordAttempt()` — the current tracker only has the one-time `ShouldEmit()`
  fallback). `NeoVisualLog.EnsurePane` (NeoVisualLog.cs:172-178) delegates the `_paneInitTried`
  latch to it so a transient `CreatePane` failure retries instead of permanently disabling the pane.
- **Verify-with:** `Run_PaneFailureTracker_RetryAfterFailure` (Telescope.Tests) — after a recorded
  failure, `ShouldRetry()` returns true (RED: the retry-after-failure path is missing today).
  Diagnostic: `[NeoVisual] output pane unavailable: {reason}` unchanged.
- **Fails-if:** `Run_PaneFailureTracker_RetryAfterFailure` fails; a transient pane failure
  permanently disables the pane.

### BP-54 — n2 + n13: BlockCaretAdornment early-return + PaneGuid readonly
- **Files:** `MyExtension/Adornments/BlockCaretAdornment.cs`, `Telescope/Logging/NeoVisualLog.cs`
- **Change:** (n2) Early-return in `Update()` before `RemoveAdornmentsByTag` when `_active` is false
  (BlockCaretAdornment.cs:109). (n13) Make `PaneGuid` `readonly` (NeoVisualLog.cs:28).
- **Verify-with:** build 0 errors; existing suites pass.
- **Fails-if:** build error; the adornment removes its tag when inactive.

### BP-55 — n12: `RenderResults` re-renders only on change
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** In `RenderResults` (TelescopeOverlay.cs:438-444), skip the `_resultsBox.Text = ...`
  rebuild + `LoadPreviewForSelection()` when neither the results nor the selection changed since the
  last render (cache the last-rendered results reference + `_selectedIndex`). No-seam (WPF) — the
  `results count=... selected=...` diagnostic is unchanged.
- **Verify-with:** build 0 errors; existing suites pass (behavior-preserving). Diagnostic:
  `[Telescope] results count=... selected=...` unchanged.
- **Fails-if:** build error; the results box text or selection drifts on a no-change render.

### BP-56 — n17: `GetCaretOffset` DTE fallback uses a non-expanded character column
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`
- **Change:** In `GetCaretOffset`'s DTE fallback (MyExtensionPackage.cs:645-655), replace
  `selection.ActivePoint.DisplayColumn` (tab-expanded display column) with a non-expanded character
  column — map display→char via the text line (or use `ActivePoint`'s character column) so the
  offset into `SourceText` is correct on lines with tabs (the references/implementations finders
  resolve the wrong symbol today). No-seam (VS-coupled) — build + deferred e2e.
- **Verify-with:** build 0 errors; existing suites pass. Diagnostic:
  `[Telescope] references gathered reads=... writes=...` / `[Telescope] implementations gathered count=...`
  unchanged (the caret symbol now resolves at the correct offset).
- **Fails-if:** build error; the DTE fallback still uses `DisplayColumn` (code review); the
  references/implementations finders resolve the wrong symbol on tab-indented lines.

### BP-57 — n18: `SetHook` guards `Process.GetCurrentProcess().MainModule`
- **Files:** `MyExtension/Hooks/GlobalKeyboardHook.cs`
- **Change:** In `SetHook` (GlobalKeyboardHook.cs:165-173), guard the
  `Process.GetCurrentProcess().MainModule` access (it can throw `Win32Exception`) — on failure log
  the exception and fall back to `IntPtr.Zero` for `hMod` (valid for `WH_KEYBOARD_LL` when the proc
  is in-process), so a `MainModule` failure can't fail the hook init step and degrade the extension
  to a no-op. No-seam (build).
- **Verify-with:** build 0 errors; existing suites pass. Diagnostic: the hook init step still
  completes (`[MyExtension] init hook ok`); the failure path logs the `Win32Exception` once.
- **Fails-if:** build error; `SetHook` still dereferences `MainModule` unguarded (code review); a
  `MainModule` failure fails the hook init step.

## Phase 8 — Harness hardening (M3, m60, m63, m64, m65)

### BP-58 — M3: `Send-Text` shift flag
- **Files:** `tools/harness/harness-common.ps1`
- **Change:** In `Send-Text` (harness-common.ps1:121-137), apply Shift for `[char]::IsUpper($ch)`
  letters (currently only punctuation gets a shift flag).
- **Verify-with:** harness self-check — add a `-SelfCheck` seam in `harness-common.ps1` that
  asserts `Send-Text 'Program'` emits the uppercase VK + Shift sequence (no-seam PowerShell —
  deferred e2e, E2E queue reference E2E-NCR-M3). `Send-Text 'Program'` types `Program`
  (case-fidelity).
- **Fails-if:** `Send-Text 'Program'` still types `program`.

### BP-59 — m60 + m63 + m64 + m65: harness robustness
- **Files:** `tools/harness/harness-common.ps1`, `tools/harness/test-e2e.ps1`
- **Change:** (m60) Add Professional/Enterprise/Preview candidates to `Resolve-VsRoot`
  (harness-common.ps1:229-233). (m63) Document the scenario ordering + make subsets self-seeding
  (test-e2e.ps1:822-874,989-1062). (m64) Require `count=[1-9]\d*` at test-e2e.ps1:1161. (m65)
  Per-line match over the cache in `Wait-LogLine` (harness-common.ps1:176).
- **Verify-with:** harness self-check — a `-SelfCheck` seam in `harness-common.ps1`/`test-e2e.ps1`
  asserting the `count=[1-9]\d*` regex rejects `count=0` and `Wait-LogLine` matches per-line
  (no-seam PowerShell — deferred e2e).
- **Fails-if:** `results count=0` still matches `count=\d+`; `Wait-LogLine` still matches across
  line boundaries.

## Phase 9 — Test hermeticity (M7, m4, m53, m54, m55, m56, m57, m58, m62)

### BP-60 — M7: FzfFilter timeout deterministic seam
- **Files:** `Telescope/Filter/FzfFilter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Expose the awaited-task outcome via a deterministic seam (TaskCompletionSource /
  awaited-read count) instead of the 5s wall-clock + 2s GC-poll for `UnobservedTaskException`.
- **Verify-with:** `Run_FzfFilter_TimeoutAwaitsTasks` (Telescope.Tests) — the timeout path observes
  the awaited tasks deterministically (RED: no seam → compile error).
- **Fails-if:** `Run_FzfFilter_TimeoutAwaitsTasks` fails; the timeout test is still timing-dependent.

### BP-61 — m53 + m54 + m55 + m56 + m57 + m58: test hermeticity fixes
- **Files:** `tests/Telescope.Tests/Program.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (m53) Delete the byte-identical duplicate
  `Run_Preview_UpFromSecondLineWithLeadingBlankLine`/`_Fixed` (Telescope.Tests/Program.cs:942-965).
  (m54) Rename `Run_GrepFinder_GatherHitsThrowsNotSupported` to what it asserts + add a test
  reaching the base gather stub. (m55) Rename or call `TryGetScreenRect` in
  `Run_WindowAdapter_TryGetScreenRect_...` (NeoVisual.Tests/Program.cs:1390-1404). (m56) Assert
  behavior instead of the private field in `Run_LeaderMatcher_PrefixSetBuiltOnce`
  (NeoVisual.Tests/Program.cs:1576-1599). (m57) Hermetic fzf stub / stronger assertion in
  `Run_FzfFilter_FilterMatchesPrefix` (Telescope.Tests/Program.cs:482-500). (m58) Inject a clock /
  relax in `Run_FzfFilter_IsAvailableBounded` (Telescope.Tests/Program.cs:630-648).
- **Verify-with:** Telescope.Tests count drops by 1 (m53); the renamed/strengthened tests pass
  hermetically (no fzf-on-PATH Mystery Guest).
- **Fails-if:** a hermeticity test still requires fzf on PATH; the duplicate test remains.

### BP-62 — m62: `TestRunner` awaits Task-returning methods
- **Files:** `tests/TestRunner.cs`
- **Change:** In `TestRunner.Run` (TestRunner.cs:60), await `Task`-returning methods instead of
  discarding the return value.
- **Verify-with:** build 0 errors; both suites pass (a future async test can't silently pass).
- **Fails-if:** build error; an async test's failure is swallowed.

### BP-63 — m4: extract `RoslynGatherers` with an injectable seam + `IsWriteLocation` test
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`, `MyExtension/Package/RoslynGatherers.cs`
  (new), `tests/NeoVisual.Tests/Program.cs`
- **Change:** Extract the ~270 lines of Roslyn/VS-coupled gatherer logic
  (`TryGetCaretSymbol`, `GatherReferences`, `GatherImplementations`, `GetCaretOffset`, `ReadLine`,
  `IsWriteLocation` — MyExtensionPackage.cs:423-696) into a `RoslynGatherers` class with an
  injectable seam (mirroring the finder host-injection pattern: the package supplies the DTE /
  workspace / document factories, the class owns the pure gather + reflection logic). `IsWriteLocation`
  (MyExtensionPackage.cs:683-696) becomes a static member of `RoslynGatherers` so the reflection
  read of `ReferenceLocation.IsWrittenTo` is unit-testable offline. `MyExtensionPackage` delegates
  to the extracted class (the `references gathered reads=... writes=...` / `implementations gathered
  count=...` diagnostics stay byte-identical).
- **Verify-with:** `Run_RoslynGatherers_IsWriteLocation` (NeoVisual.Tests) — the reflection read of
  `ReferenceLocation.IsWrittenTo` returns the write flag for a write location and false on a
  reflection failure (RED: no seam → compile error). Diagnostic:
  `[Telescope] references gathered reads=... writes=...` unchanged.
- **Fails-if:** `Run_RoslynGatherers_IsWriteLocation` fails (compile error — no seam); the
  references/implementations gather diagnostics drift; a Roslyn call escapes the
  `ThreadHelper.JoinableTaskFactory.Run` wrapper.

## Phase 10 — Docs drift (M6, m61, m66, m67, m68, m69, n20, n21)

### BP-64 — M6: e2e-queue reconciliation
- **Files:** `docs/e2e-queue.md`, `docs/progress.md`
- **Change:** Append the E2E-NCR-*/E2E-RESTRUCTURE-1 entries (from the prior
  implementation_plan.md:739-769) to `e2e-queue.md`, or correct the claims in docs/progress.md:16.
- **Verify-with:** doc-ref lint clean; `e2e-queue.md` contains the E2E-NCR-*/E2E-RESTRUCTURE-1
  entries.
- **Fails-if:** docs/progress.md:16 still claims entries that don't exist in `e2e-queue.md`.

### BP-65 — m61: scope the doc-ref allowlist
- **Files:** `tools/lint/check-doc-refs.ps1`
- **Change:** Scope the external allowlist (check-doc-refs.ps1:87-98) to the specific archive docs
  instead of permanently masking future refs to `DistinctBy`/`CardinalMovment`/`CardinalNavigation`.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved (the archive docs still
  resolve via the scoped allowlist).
- **Fails-if:** the lint reports unresolved refs in the archive docs; or a future ref to a removed
  symbol is silently masked.

### BP-66 — m66 + m67 + n20 + n21: progress.md/architecture-review.md doc fixes
- **Files:** `docs/progress.md`, `docs/reviews/architecture-review.md`
- **Change:** (m66) Fix the "Chunk C restructure pending" header (docs/progress.md:10). (m67)
  Update the "Next up" section (docs/progress.md:20-28). (n20) Fix the 143/140 baseline attribution
  (docs/progress.md:73-74). (n21) Update the "Verified-clean" claim (architecture-review.md:417) to
  `MyExtension.Navigation`/`Navigation/`.
- **Verify-with:** doc-ref lint clean; the claims match the current state.
- **Fails-if:** the lint flags a stale reference; a claim still contradicts the Done state.

### BP-67 — m68 + m69: spec.md diagnostics + key-files table
- **Files:** `docs/spec.md`
- **Change:** (m68) Add the 4 omitted harness-asserted lines to §4 (docs/spec.md:186-217):
  `open finder=`, `Focus prompt => True, mode=insert`, `results count=... selected=...`,
  `key=... mode=... handled=...`. (m69) Add the 13 omitted seam-file rows to §2.2
  (docs/spec.md:47-109): OverlayShowState, FocusTargetModel, LineIndex, TryDispatch,
  PreviewRenderer, BlockCaretStyle, VimModeClassifier, InitSteps, SimpleShortcutMatcher,
  NavigationSnapshot, FilterFailureLog, TelescopeLog, PaneFailureTracker.
- **Verify-with:** doc-ref lint clean (the new rows resolve against the source tree).
- **Fails-if:** the lint flags a new row; a diagnostic line is still missing from §4.

## Phase 11 — Restructure: MyExtension (folders + Utils subfolders + 5 renames)

### BP-68 — git mv helpers into Utils/ subfolders (MyExtension)
- **Files:** all MyExtension helper .cs files (see the restructure table: Hooks/Utils/,
  Input/Utils/, Vim/Utils/, Package/Utils/, Navigation/Utils/, ToolWindows/Utils/)
- **Change:** `git mv` each helper .cs into its target `Utils/` subfolder. Namespaces unchanged.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project tests/NeoVisual.Tests` all pass
  (count unchanged); no `[NeoVisual]`/`[Telescope]`/`[Hook]`/`[MyExtension]` log literal changed.
- **Fails-if:** build errors (missed reference); a NeoVisual test regresses; a log literal changed.

### BP-69 — apply the 5 renames
- **Files:** `MyExtension/Navigation/WindowMatrix.cs`→`WindowNavigator.cs`,
  `CardinalNavigationConstants.cs`→`NavigationConstants.cs`, `UtilityMethods.cs`→`WindowFrameUtils.cs`,
  `RectCoordinate.cs`→`WindowRect.cs`, `WindowAdapter.cs`→`WindowFrameAdapter.cs` (+ class renames)
- **Change:** `git mv` + class rename: WindowMatrix→WindowNavigator,
  CardinalNavigationConstants→NavigationConstants, UtilityMethods→WindowFrameUtils,
  RectCoordinate→WindowRect, WindowAdapter→WindowFrameAdapter.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project tests/NeoVisual.Tests` all pass
  (count unchanged); no log literal changed.
- **Fails-if:** build errors (missed rename/reference); a NeoVisual test regresses.

### BP-70 — update using directives + references (MyExtension + NeoVisual.Tests)
- **Files:** `MyExtension/**/*.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Update every `using` directive + fully-qualified reference for the moved files
  (namespaces unchanged, so only the type names change for the 5 renames; the folder moves do NOT
  change namespaces).
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project tests/NeoVisual.Tests` all pass
  (count unchanged).
- **Fails-if:** build errors (missed rename/reference).

### BP-71 — doc-ref lint allowlist for the renames (OLD names scoped, NEW names NOT added)
- **Files:** `tools/lint/check-doc-refs.ps1`
- **Change:** After the 5 renames the OLD type names (WindowMatrix, CardinalNavigationConstants,
  UtilityMethods, RectCoordinate, WindowAdapter) are removed from source but are still cited by the
  archive docs (architecture-review.md etc.) — add them to the scoped external allowlist (per m61's
  scoping in BP-65, scoped to the specific archive docs). Do NOT add the NEW names (WindowNavigator,
  NavigationConstants, WindowFrameUtils, WindowRect, WindowFrameAdapter) — they resolve via source
  grep after the rename. Do NOT remove `CardinalMovment`/`CardinalNavigation` (m61's scoping in
  BP-65 keeps them for the archive docs).
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved (the archive docs' OLD-name
  refs resolve via the scoped allowlist; the NEW names resolve via source grep).
- **Fails-if:** the lint reports unresolved refs to the OLD names in the archive docs; a NEW name is
  added to the allowlist (masking a future source-grep miss); `CardinalMovment`/`CardinalNavigation`
  removed (contradicts m61's scoping in BP-65).

## Phase 12 — Restructure: Telescope (folders + Utils subfolders)

### BP-72 — git mv helpers into Utils/ subfolders (Telescope)
- **Files:** all Telescope helper .cs files (see the restructure table: Overlay/Utils/,
  Finders/Utils/, Logging/Utils/)
- **Change:** `git mv` each helper .cs into its target `Utils/` subfolder. Namespaces unchanged.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project tests/Telescope.Tests` all pass
  (count unchanged); `dotnet run --project tests/NeoVisual.Tests` all pass.
- **Fails-if:** build errors (missed reference); a Telescope/NeoVisual test regresses; a log literal
  changed.

### BP-73 — update references (MyExtension + tests)
- **Files:** `MyExtension/**/*.cs`, `tests/**/*.cs`
- **Change:** Update every reference across MyExtension/ + tests/ for the moved Telescope files
  (namespaces unchanged, so only the file paths change — no type renames in Telescope).
- **Verify-with:** `dotnet build` 0 errors; both unit suites pass (counts unchanged).
- **Fails-if:** build errors (missed reference); a test regresses.

### BP-74 — keep RootNamespace
- **Files:** `Telescope/Telescope.csproj`
- **Change:** Keep `<RootNamespace>Telescope</RootNamespace>` in Telescope.csproj (no change needed —
  verify it's intact).
- **Verify-with:** `dotnet build` 0 errors; both unit suites pass.
- **Fails-if:** the RootNamespace drifted (build/namespace errors).

## Phase 13 — Restructure: reference sweep (docs, agents, tools, tests) + doc-ref lint

### BP-75 — doc/agent/tool reference sweep
- **Files:** `AGENTS.md`, `docs/spec.md`, `docs/progress.md`, `docs/implementation_plan.md`,
  `docs/reviews/*.md`, `.opencode/skills/vs-extension-dev/SKILL.md`, `.opencode/agent/*.md`,
  `tools/lint/check-doc-refs.ps1`
- **Change:** Update every doc/agent/tool reference to the moved/renamed paths: every
  `MyExtension/*.cs`/`Telescope/*.cs` path + the 5 renamed type names; every exact-path reference in
  `.opencode/agent/*.md`; the `check-doc-refs.ps1` allowlists + `$sourceRoots` if needed.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` → `[PASS] ... 0 unresolved`; `dotnet build`
  0 errors; both unit suites pass (counts unchanged).
- **Fails-if:** the doc-ref lint reports unresolved references (a moved/renamed path missed in the
  sweep); an agent file still references an old path.

## Phase 14 — Final verification (combined plan gate)

### BP-76 — combined-plan gate
- **Files:** (verification only)
- **Change:** Run the five gates: (1) `dotnet build MyExtension.slnx` → 0 errors; (2)
  `dotnet run --project tests/Telescope.Tests` → all pass (count unchanged); (3)
  `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged); (4)
  `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved; (5) log-literal diff gate (no
  `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` literal changed by the restructure). e2e
  deferred: full 35-scenario suite queued (E2E queue reference E2E-RESTRUCTURE-2).
- **Verify-with:** the five gates all pass.
- **Fails-if:** any gate fails; a log literal drifted; a unit count changed (unless a Phase 9 test
  deletion was the documented cause).

---

# Verification Trace

Maps every failing unit test / gate to its implicated Build Plan step(s) and the expected
diagnostic line that proves it. **Known-RED allowlist: NONE** — no known-RED e2e scenario remains
(per docs/progress.md); the deferred e2e scenarios in the "E2E queue reference" section are QUEUED,
not failing, and must NOT be reported as regressions. The restructure (Phases 11-14) is
behavior-preserving (no RED — verified by build + suites + doc-ref lint + the deferred full-suite
e2e re-run).

| failing test / gate | implicated steps | expected diagnostic |
|---|---|---|
| `Run_TryDispatch_InsertPlacementsNotPromptMotions` | BP-1 | `prompt-motion key=... caret=...` NOT emitted for a/A/I; `Focus prompt => True, mode=insert` after a/A/I |
| `Run_OverlayKeyHandler_*` (a/A/I insert actions) | BP-1 | `Focus prompt => True, mode=insert` |
| `Run_OverlayKeyHandler_InsertAtCurrentPreservesCaret` (m36) | BP-1 | `Focus prompt => True, mode=insert`; prompt caret preserved after `i` (not reset to end) |
| `Run_HierarchyResolver_PrimaryFilePath` (primitive string lists) | BP-2 | `[NeoVisual] solution-explorer select file=...` (primary `.cs`, not `.resx`) |
| `Run_TryDispatch_InsertPlacementsNotPreviewMotions` (m52) | BP-3 | `[Telescope] preview caret=... line=...` NOT emitted for a/A/I in the preview |
| `Run_GrepFinder_*` (drop-Task.Run, behavior-preserving) | BP-4 | `[Telescope] grep hits=...` unchanged |
| `Run_PreviewTokenCache_UnchangedMtimeCached` / `_ChangedMtimeRetokenizes` | BP-5 | `[Telescope] preview tokens=...` unchanged (same count on cache hit) |
| `Run_FileContentCache_*` (read-once) | BP-6 | `[Telescope] references gathered reads=... writes=...` unchanged |
| code-review: 3 construction sites pass a cap (m27; `Run_FileContentCache_EvictsOldest` already exists + passes) | BP-7 | (none — no new RED test) |
| `Run_TryDispatch_*` / `Run_TextMotionNavigator_*` (m31) | BP-8 | `prompt-motion key=... caret=...` unchanged |
| `Run_TextMotionNavigator_LineNumber` | BP-9 | `[Telescope] preview caret=... line=...` unchanged |
| `Run_ResultMapper_*` | BP-10 | `[Telescope] result-mapper unknown display: {display}` unchanged |
| `Run_FileFinder_UsesProjectFileCache` | BP-11 | `[Telescope] opened file: ...` unchanged |
| `Run_FzfFilter_IsAvailableBounded` (bounded assertion deferred to Phase 9/BP-61) | BP-12 | `[Telescope] fzf unavailable — showing unfiltered list` unchanged (once at overlay open) |
| build (m15) | BP-13 | `[NeoVisual] text-motion key=... caret=...` / `textinput-enter-input ...` unchanged |
| `Run_HitOpener_*` (m30) | BP-14 | `[Telescope] opened file: ...` unchanged |
| `Run_FocusGuard_OwnsKeyboard` | BP-15 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| `Run_FocusGuard_*` (m7) | BP-16 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| `Run_KeybindingConfig_IsSimpleShortcut` (m42) | BP-17 | `[NeoVisual] leader-binding executed: ...` unchanged |
| `Run_InjectedKeyGuard_*` | BP-18 | (none — guard behavior) |
| build (n14/n15) + m47 outcome diagnostic | BP-19 | `[NeoVisual] navigate direction=L/R/D/U` unchanged; ADD `[NeoVisual] navigate activated index=...` / `navigate no-op: <reason>` |
| build (n16) | BP-20 | (none) |
| build (m38) | BP-21 | (none) |
| build (m39) | BP-22 | (none) |
| build (m40) | BP-23 | (none) |
| `Run_VimModeState_*` (mode-change) | BP-24 | `[NeoVisual] vim-mode=Insert\|Normal\|Replace` unchanged; `vim-mode=Unknown` on focus loss |
| `Run_VimModeClassifier_*` | BP-25 | (none) |
| `Run_WindowNavigationEngine_*` (m11/n3) | BP-26 | (none) |
| `Run_WindowNavigationEngine_*` (m48) | BP-27 | (none) |
| `Run_NavigationSettings_*` | BP-28 | (none) |
| `Run_WindowAdapter_*` (m13) | BP-29 | (none) |
| build (m14/m45/m49) | BP-30 | (none) |
| `Run_WindowNavigator_BuildActiveWindowsNullActive` | BP-31 | (none) |
| build (m46) | BP-32 | (none) |
| build + `Run_WindowAdapter_*` / `Run_NavigationSnapshot_*` (n4/n5/n6/n19) | BP-33 | (none) |
| build (m20/m21) | BP-34 | (none) |
| `Run_WindowManager_*` | BP-35 | (none) |
| `Run_GeneralToolWindowController_*` (m23) | BP-36 | (none) |
| build (m24) | BP-37 | (none) |
| build (m25) | BP-38 | (none) |
| `Run_HierarchyResolver_*` / `Run_HierarchyForestBuilder_*` | BP-39 | `[NeoVisual] solution-explorer select file=...` unchanged (`.cs`) |
| build (n7/n8/n9/n10) | BP-40 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged (n10 aligned) |
| `Run_GeneralToolWindowController_*` (m9/m17) | BP-41 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| `Run_ActionTable_*` | BP-42 | (none) |
| build (m18) | BP-43 | (none) |
| `Run_HierarchyForestBuilder_*` + `Run_HierarchyWalker_*` | BP-44 | (none) |
| `Run_TextMotionNavigator_*` / `Run_TryDispatch_*` (m35) | BP-45 | (none) |
| `Run_TextMotionDispatcher_*` (n11) | BP-46 | `[Telescope] prompt-motion key=... caret=...` / `preview caret=... line=...` unchanged |
| build (m8) | BP-47 | `[Telescope] open finder=...` / `overlay closed` unchanged (single injected launcher) |
| `Run_FocusGuard_*` (m1) + doc-ref lint | BP-48 | (none) |
| NeoVisual.Tests count drop (m2) | BP-49 | (none) |
| build (m10/m28) | BP-50 | (none) |
| `Run_LogFileWriter_*` | BP-51 | `[Hook]` lines single-stamped |
| `Run_FileFinder_*` / `Run_GrepFinder_*` (m29) | BP-52 | `[Telescope] opened file: ...` unchanged |
| `Run_PaneFailureTracker_RetryAfterFailure` | BP-53 | `[NeoVisual] output pane unavailable: {reason}` unchanged |
| build (n2/n13) | BP-54 | (none) |
| build (n12) | BP-55 | `[Telescope] results count=... selected=...` unchanged |
| build (n17) | BP-56 | `[Telescope] references gathered reads=... writes=...` / `implementations gathered count=...` unchanged |
| build (n18) | BP-57 | `[MyExtension] init hook ok` (hook init completes; `MainModule` failure logged, `hMod=IntPtr.Zero`) |
| harness self-check (M3) | BP-58 | `Send-Text` case-fidelity (deferred e2e, E2E queue reference E2E-NCR-M3) |
| harness self-check (m60/m63/m64/m65) | BP-59 | `results count=[1-9]\d*` (deferred e2e) |
| `Run_FzfFilter_TimeoutAwaitsTasks` | BP-60 | (none) |
| Telescope.Tests count drop (m53) + hermeticity tests (m54-m58) | BP-61 | (none) |
| build + both suites (m62) | BP-62 | (none) |
| `Run_RoslynGatherers_IsWriteLocation` (m4) | BP-63 | `[Telescope] references gathered reads=... writes=...` unchanged |
| doc-ref lint + `e2e-queue.md` content (M6) | BP-64 | (none) |
| `pwsh tools/lint/check-doc-refs.ps1` (m61) | BP-65 | (none) |
| doc-ref lint (m66/m67/n20/n21) | BP-66 | (none) |
| doc-ref lint (m68/m69) | BP-67 | (none) |
| build + NeoVisual.Tests (Phase 11 folder moves) | BP-68 | no `[NeoVisual]`/`[Telescope]`/`[Hook]`/`[MyExtension]` log literal change |
| build + NeoVisual.Tests (5 renames) | BP-69 | no log literal change |
| build + NeoVisual.Tests (references) | BP-70 | no log literal change |
| doc-ref lint (allowlist: OLD names scoped for archive docs, NEW names NOT added) | BP-71 | (none) |
| build + both suites (Phase 12 folder moves) | BP-72 | no log literal change |
| build + both suites (references) | BP-73 | no log literal change |
| build + both suites (RootNamespace) | BP-74 | (none) |
| doc-ref lint + build + suites (sweep) | BP-75 | `[PASS] ... 0 unresolved` |
| five gates (Phase 14) | BP-76 | no log literal change; both suites pass (counts unchanged) |
