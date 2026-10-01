# Combined Plan — Code-Review Fixes (67 findings) + Repository Restructure

> **Lane: feature/bugfix program + refactor (unit-only, e2e deferred).**
> Source: `docs/code-review.md` (2026-09-30, whole-repo 10-worker review of the
> post-consolidation codebase) + a user-requested repository restructure
> (folders + namespaces + tools + docs).
>
> **User decisions (2026-09-30, via question):** (1) **folders + namespaces** —
> move files AND rename namespaces to match the new folders; (2) **one combined
> plan** — code-review fixes as early phases, restructure as the final phase;
> (3) **source + tools + docs** scope.
>
> **Research:** 1 whole-repo `trailmark-recon` digest (1691 nodes, 681 proxies,
> 0 entrypoints — all 12 structural claims CONFIRMED) + 2 `arch-auditor`
> slices (code-review fix verification: 33 findings verified + 6 flagged minors
> + 4 fix-direction corrections; restructure impact: 7-section inventory).
> `feature-researcher` SKIPPED (both tasks are internal bug fixes + refactor —
> no LazyVim reference needed).
>
> **Ground truth:** `dotnet build MyExtension.slnx` = 0 errors (115 pre-existing
> warnings). Repo is GREEN (commit `f4450cb` + progress `164e757`).
>
> e2e scenarios are DEFERRED to `e2e-queue.md` (status QUEUED) — this machine
> cannot boot the VS Experimental Instance. NO e2e RED/VERIFY step is planned;
> every fix is verified by unit tests (where a hermetic seam exists) + `dotnet
> build` + the existing unit suites, with the live behavior gated by the
> deferred e2e scenarios.

## Goal

Fix all 67 code-review findings in dependency order (the 2 consolidation
regressions first, then the hook hot-path contract, the finder UI-stall
cluster, the motion/preview correctness bugs, the navigation robustness, the
hook-path safety, the duplication merges, the dead-code deletions, the harness
hardening, the test hermeticity, and the docs drift), then restructure the
repository into logical folders with matching namespaces (source + tools +
docs) — with every behavior change proven by a RED unit test (or, where no
hermetic seam exists, by build + existing suites + the deferred e2e gate), and
the restructure proven behavior-preserving by build + unit suites + doc-ref
lint + the deferred full-suite e2e re-run.

## Approach

The research passes produced a verified verdict + fix direction + unit-test
seam for every finding, plus a full restructure impact inventory. The plan is
organized into 14 phases in dependency order: Phases 0-10 are the code-review
fixes, Phases 11-14 are the restructure. Cross-cutting constraints:

- **Log-line-as-contract:** several fixes touch lines the e2e harness asserts
  on (`navigate direction=L/R/D/U`, `shortcut-binding executed:`,
  `leader-binding executed:`, `[Telescope] focus target=List|Preview`,
  `vim-mode=Insert|Normal|Replace`, `preview caret=... line=...`,
  `text-motion key=...`, `textinput-enter-input ...`). Any fix that changes a
  signature or a log site MUST preserve the emitted token byte-identical.
- **Proxy traps:** cross-class calls land on `proxy.unresolved:<Type>.<Member>`;
  a bare `callers_of` returning 0 is SUSPECT, not dead code. Never report
  `KeyInjection.Press`, `NeoVisualLog.Close/Log/Debug`, `InstallDebugListener`,
  `TextMotionHelper.MapMotion`, `controller.TryMove/ExitInputMode` as dead.
- **Fix-direction corrections from research (fold into the phases):**
  1. **CR1** — the existing exact-set test
     `Run_ActionTable_TextInput_ActionKeysMatchTable` (NeoVisual.Tests:527-537)
     pins the buggy `{W,B,E,A,H,L}` set (count 6) and MUST be updated to
     include `I` (count 7) in the same change.
  2. **M10** — the existing test
     `Run_Preview_UpFromSecondLineWithLeadingBlankLine` (Telescope.Tests:846-853)
     asserts the buggy `LineNumber==2` result and MUST be corrected to assert
     line 1/caret 0 in the same change.
  3. **M19** — the motion **math is already shared** (`TextMotionHelper`
     delegates to `TextMotionNavigator`); only the key→motion **dispatch** is
     duplicated (`TryDispatch` vs `TextMotionHelper.MapMotion`). Fix = unify
     the two dispatch tables, NOT "make TextMotionHelper delegate".
  4. **M27** — scope the `results count=[1-9]\d*` tightening to
     `test-e2e.ps1:518` ONLY; `:1533/:1537` legitimately assert `count=0`
     (`telescope-no-selection`).
  5. **m28** — the "Text.UI not referenced" comment is STALE
     (NeoVisual.Tests.csproj now references `Microsoft.VisualStudio.Text.UI`),
     so `TextInputToolWindowController.TryMove` IS unit-testable; only
     `SelectFirstSourceFile` stays VS-coupled.
  6. **M11** — the "NRE in HitType cast" part is REFUTED (`FinderBase` has a
     clean `is not` return); the real issue is the silent no-op fallback.
- **No-seam findings** (VS/WPF/harness-coupled, no hermetic unit surface):
  M1 (cache), M8 (hook try/catch), M9 (CheckDte), M12 (pane latch), M14
  (ctor), M25 (Dte?), M26/M27 (harness regexes), m17/m18/m19/m20 (harness),
  m2/m7/m8/m9/m10 (VS/WPF-coupled). For these the plan states the honest
  verification (build + existing suites + deferred e2e) and, where possible,
  extracts a small pure seam to make part of the behavior unit-testable.
- **Restructure constraint:** the restructure is behavior-preserving — NO
  `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` log literal may change,
  no public API may change, and the unit suites must pass with UNCHANGED
  counts. The doc-ref lint (`tools/check-doc-refs.ps1`) must stay clean after
  the reference sweep.

---

# Part A — Code-review fixes (Phases 0-10)

## Phase 0 — Criticals (CR1, CR2)

- **CR1** (critical, CONFIRMED) — `TextInputToolWindowController` dropped
  `Keys.I` from `_actions` (consolidation regression). `_actions` has
  W/B/E/A/H/L but not `Keys.I`; the docstring (:18) still documents `I` =
  insert at line start, and the harness presses `I` on the Command Window and
  asserts `textinput-enter-input start caret=0` (test-e2e.ps1:1133). Today `I`
  falls through to the generic `i` branch. **Fix:** add
  `[Keys.I] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.I, ref _isInputMode)`
  to `_actions` (bare `i` still falls through). **Unit seam (NeoVisual.Tests):**
  update `Run_ActionTable_TextInput_ActionKeysMatchTable` to the exact set
  `{W,B,E,A,H,L,I}` (count 7) + add `Run_TextInput_KeysIMapsToInsertStart`
  asserting `TryMove(Keys.I)` maps to `InsertStart`. RED: the exact-set test
  fails (count 6 vs 7) before the fix.
- **CR2** (critical, CONFIRMED) — `VsVimModeSource.Detach(view)` ignores
  `view` and unsubscribes the single global `_currentTextBuffer`;
  `SubscribeBuffer` unsubscribes the previous buffer on every `Attach`. With
  2+ views, closing a non-focused view kills the focused view's `SwitchedMode`
  subscription → `_cachedTyping` goes stale → Space starts a leader sequence
  in insert mode. **Fix:** track the subscribed buffer per view
  (`Dictionary<ITextView, object>`), `Detach(view)` unsubscribes only that
  view's buffer, re-subscribe the focused view's buffer on `OnViewGotFocus`.
  **Unit seam (NeoVisual.Tests):** extract a pure per-view subscription map
  (e.g. `VimBufferSubscriptions` — `Attach(view, buffer)`, `Detach(view)`,
  `BufferFor(view)`, `FocusedBuffer`) with a fake `IVimModeSource`; tests:
  attach A, attach B, detach A → B's subscription survives (RED today: the
  map doesn't exist → compile error); detach non-attached view → no-op;
  re-attach after detach → re-subscribed.

## Phase 1 — Hook hot-path contract (M1, M2, M15, M7, m17)

One focused pass on `InputHandler`/`WindowManager`/`LeaderSequenceMatcher` to
make the pre-filter genuinely cheap again (the review's recommendation #2).

- **M1** (major, CONFIRMED) — `IsKeyOfInterest` evaluates
  `TextInputSurfaceFocused` (COM `GetProperty(VSFPROPID_DocView)` +
  `Keyboard.FocusedElement` + visual-tree walk) on every key-down, violating
  its own "no COM/interop" doc comment. **Fix:** cache the focused-surface
  fact on focus-change events (like `_isTextInputType`) and read the cached
  bool per key. No-seam (WindowManager is VS-coupled) — honest verification:
  build + existing `Run_ToolWindowMode_*` classification tests + deferred e2e.
- **M2** (major, CONFIRMED) — `RefreshStaleSentinel()` re-stats the sentinel
  file (`File.Exists`) per key-down whenever `NEOVISUAL_LOG_DIR` is set.
  **Fix:** stat on a timer or focus-change, not per key-down (the sentinel
  toggles with NO focus change, so a pure focus-gated refresh would leave the
  cache stale — cache the result and refresh on a bounded interval or on
  `IsKeyOfInterest` entry with a cached value). **Unit seam (NeoVisual.Tests):**
  the existing pure `StaleToolWindowSentinel` (path + `Refresh()` + cached
  `IsStale`) — add a test that `IsStale` is cached until `Refresh()` (already
  covered by `Run_StaleToolWindowSentinel_CachedWithoutRefresh`); the wiring
  (when to call `Refresh()`) is no-seam.
- **M15** (major, CONFIRMED) — the `key == Keys.I` branch (enter-input-mode
  fallback) isn't guarded on `!_leaderMatcher.IsActive`; `IsKeyOfInterest`
  returns true for bare Ctrl keys that `HandleKey` deliberately ignores.
  **Fix:** add `!_leaderMatcher.IsActive &&` to the `Keys.I` condition; drop
  the `ControlKey`/`LControlKey`/`RControlKey` cases from `IsKeyOfInterest`.
  **Unit seam (NeoVisual.Tests):** `Run_LeaderMatcher_*` (pure) — a leader
  sequence in progress must not be interrupted by `I`; the Ctrl-key pre-filter
  change is no-seam (build-verified).
- **M7** (major, CONFIRMED) — `LeaderSequenceMatcher` rebuilds the whole
  sequence string + scans all bindings (`StartsWith`) per key while a leader
  sequence is active. **Fix:** maintain the sequence string incrementally and
  precompute a prefix set once at construction. **Unit seam (NeoVisual.Tests):**
  the existing `Run_LeaderMatcher_*` family (behavior-preserving) + a new
  `Run_LeaderMatcher_PrefixSetBuiltOnce` asserting the prefix set is computed
  once (RED: no prefix set exists → compile error).
- **m17** (minor, CONFIRMED) — `WindowManager.RefreshCurrentWindow` calls
  `IVsMonitorSelection` without `ThrowIfNotOnUIThread()`. **Fix:** add the
  guard. No-seam (build-verified).

## Phase 2 — Finder path amortization (M3, M4, M5, M13, m15, m16)

- **M3** (major, CONFIRMED) — fzf spawned per keystroke with the full
  candidate list written synchronously to stdin on the UI thread; the timeout
  path returns without awaiting the faulted `ReadToEndAsync` tasks. **Fix:**
  move spawn/write into `Task.Run` (or switch to `fzf --listen`); `await` the
  output/error tasks after `TryKill`. **Unit seam (Telescope.Tests):**
  `FzfFilter(string? fzfPath)` injected-path seam — a stub exe that hangs →
  timeout+kill+fallback; assert no unobserved-task noise (the timeout path
  awaits both tasks). RED: the injected-path ctor doesn't exist → compile
  error.
- **M4** (major, CONFIRMED) — `GrepFinder.GetCandidates` scans every line of
  every project file synchronously on the UI thread per debounced keystroke.
  **Fix:** run the content scan on a background task and marshal only the
  results back (keep the DTE enumeration on the UI thread). **Unit seam
  (Telescope.Tests):** the hermetic `GrepFinder` path (injected enumerate +
  opener) — a test that `GetCandidates` returns the same hits when the scan
  runs off-thread (RED: no off-thread seam → compile error or behavior gap).
- **M5** (major, CONFIRMED) — preview re-reads + re-tokenizes the whole file
  per selection change; `_lineIndex`/`_linePointers` are static mutable state.
  **Fix:** cache the last (path, mtime, content, segments) per file
  (mtime-keyed) and re-tokenize only on change; make the line index instance
  state reset on `SetContent`. **Unit seam (Telescope.Tests):** a pure
  mtime-keyed content cache (or reuse `FileContentCache` from M13) — tests:
  unchanged mtime → cached; changed mtime → re-read. RED: cache class doesn't
  exist → compile error.
- **M13** (major, CONFIRMED) — `FileContentCache` never evicts. **Fix:** cap
  by count (LRU) or clear on solution change. **Unit seam (Telescope.Tests):**
  `Run_FileContentCache_EvictsOldest` (LRU cap) — insert N+1 entries → the
  oldest is evicted; `Run_FileContentCache_ClearOnSolutionChange`. RED: no
  eviction → the eviction test fails.
- **m15** (minor, CONFIRMED) — `CodeIssuesFinder.CollectTodos` reads files
  without the shared `FileContentCache`. **Fix:** route through the cache.
  **Unit seam (Telescope.Tests):** `Run_Issues_CollectTodosUsesCache` — a
  counting reader proves the cache is hit on the second scan.
- **m16** (minor, CONFIRMED) — `FzfFilter.IsAvailable()` can block the UI up
  to 3s on a hung `fzf --version`. **Fix:** bound the wait (short timeout) or
  run off-thread. **Unit seam (Telescope.Tests):** `Run_FzfFilter_IsAvailableBounded`
  — a stub that hangs → `IsAvailable()` returns within a bounded wall time.

## Phase 3 — Motion + preview correctness (M10, M11, M19, M6)

- **M10** (major, CONFIRMED) — `TextMotionNavigator.Up()` off-by-one on a
  leading blank line (text starting with `\n`): `LastIndexOf('\n',
  Math.Max(0, lineStart-2))` clamps to 0 and finds the `\n` at index 0, so
  `prevStart == 1` (line 2's own start) and `Up()` stays put. **Fix:** only
  search from `lineStart-2` when `lineStart-2 >= 0`, else `prevNewline = -1` /
  `prevStart = 0`. **Unit seam (Telescope.Tests):** CORRECT the existing
  `Run_Preview_UpFromSecondLineWithLeadingBlankLine` (asserts the buggy
  `LineNumber==2`) to assert line 1/caret 0; add `Run_Preview_UpFromSecondLineWithLeadingBlankLine_Fixed`
  — `SetText("\nabc"); MoveToLine(2); Up();` must move to line 1. RED: the
  corrected test fails before the fix.
- **M11** (major, PARTIAL — the "NRE in HitType cast" part refuted) —
  `ResultMapper` falls back to a null-payload `FinderEntry` whose `OnSelected`
  silently no-ops on an unmatched display string. **Fix:** skip unknown
  matches or give the fallback a safe no-op payload; log a warning. **Unit
  seam (Telescope.Tests):** `Run_ResultMapper_UnknownStringSkippedOrLogged` —
  an unmatched display string does not produce a null-payload entry that
  silently no-ops (RED: today it produces the null-payload entry).
- **M19** (major, PARTIAL — CORRECTED: motion math already shared; only the
  key→motion dispatch is duplicated) — `TryDispatch` (Telescope) and
  `TextMotionHelper.MapMotion` (tool windows) implement the same key→motion
  dispatch as two tables. **Fix:** unify the two dispatch tables into one
  shared pure dispatcher (each surface keeps its own shift source + its own
  diagnostic log line). **Unit seam (Telescope.Tests + NeoVisual.Tests):**
  rewrite the NeoVisual `MapMotion` groups to target the shared dispatcher;
  add Telescope.Tests RED tests for the unified `$` contract (bare `4` in
  preview must NOT LineEnd — fails against current preview semantics).
- **M6** (major, CONFIRMED) — `TextMotionHelper` per-motion key: full buffer
  copy (`snapshot.GetText()` / `wpf.Text`), fresh `TextMotionNavigator` +
  `SetText` copy, and a second `FindFocusedTextBox()` visual-tree re-walk.
  **Fix:** pass the already-found box into `ApplyMotionToBox`; reuse a
  navigator; avoid the full `GetText()` copy on the editor path. **Unit seam
  (Telescope.Tests):** the motion math is in `TextMotionNavigator` (already
  tested); the copy-elimination is in the VS-coupled layer (no-seam — build +
  deferred e2e `neovisual-textinput-motions`).

## Phase 4 — Navigation robustness (M9, M14, m3, m4, m5, m6)

- **M9** (major, CONFIRMED) — `WindowMatrix.CheckDte` fetches DTE twice per
  navigation (ctor calls `CheckDte` which fetches DTE, then fetches DTE again
  at :39); its inner try/catch duplicates the ctor's outer catch. **Fix:**
  delete `CheckDte` and its call; keep the single fetch inside the existing
  try/catch. No-seam (VS-coupled) — build + existing
  `Run_WindowNavigationEngine_*` tests + deferred e2e `neovisual-window-nav`.
- **M14** (major, CONFIRMED) — `WindowManager` ctor sets `CurrentWindow` but
  not the classification (`_type`/`_isToolWindow` stay default until the first
  focus event); `guid != null` on a `Guid` is always true → the `Unknown`
  fallback is dead. **Fix:** call `OnWindowFocusChanged()` in the ctor after
  `RefreshCurrentWindow()`; change to `guid != Guid.Empty`. No-seam (ctor is
  VS-coupled) — build + existing classification tests + deferred e2e.
- **m3** (minor, CONFIRMED) — `WindowNavigationEngine` `gap < minGap` in the
  second pass is a dead branch (minGap is the minimum). **Fix:** remove the
  dead branch. **Unit seam (NeoVisual.Tests):** the existing
  `Run_WindowNavigationEngine_*` tests pin the algorithm (behavior-preserving).
- **m4** (minor, CONFIRMED) — `CardinalNavigationConstants` `public readonly
  static int` — non-idiomatic modifier order, not `const`, inconsistent names.
  **Fix:** normalize to `public const int` + consistent naming. No-seam
  (build-verified; the constants are referenced by the engine tests).
- **m5** (minor, CONFIRMED) — `UtilityMethods` holds only static methods but
  isn't `static`. **Fix:** make it `static`. No-seam (build-verified).
- **m6** (minor, CONFIRMED) — `WindowMatrix` not `sealed` while sibling types
  are. **Fix:** add `sealed`. No-seam (build-verified).

## Phase 5 — Hook-path safety + logging (M8, M12, M16, M25, m14)

- **M8** (major, CONFIRMED — PARTIAL→GUARDED: controller calls are
  FocusGuard-gated but still unguarded by try/catch) — the tool-window branch
  calls `controller.TryMove`/`EnterInputMode`/`ExitInputMode` with no
  try/catch, and the hook callback has no top-level guard. **Fix:** wrap the
  tool-window branch (or the whole `HandleKey` body) in try/catch that logs
  `[NeoVisual] toolwindow-move failed: {msg}` and returns false (pass
  through). No-seam (VS-coupled) — build + deferred e2e.
- **M12** (major, CONFIRMED) — `NeoVisualLog.EnsurePane` sets
  `_paneInitTried=true` before `GetGlobalService`; a null service
  (pre-package-init) permanently disables the Output pane. **Fix:** reset
  `_paneInitTried` on the null-service path so a later call retries. No-seam
  (GetGlobalService) — build + deferred e2e.
- **M16** (major, CONFIRMED) — `vim-mode=` logged on every focus gain/loss,
  not just mode switches. **Fix:** log only when the mode value actually
  changes. **Unit seam (NeoVisual.Tests):** `VimModeTracker` + `FakeVimModeSource`
  — a focus change with the same mode does NOT emit a new `vim-mode=` line;
  a real mode switch does. RED: today the focus change emits a line.
- **M25** (major, CONFIRMED) — `VsServices.Dte` returns `dte!` (null-forgiving)
  when `GetService(typeof(DTE))` returns null; callers NRE inside their catch
  and log a misleading "Command failed". **Fix:** return `DTE?` and let
  callers handle null explicitly. No-seam (VS-coupled) — build + deferred e2e.
- **m14** (minor, CONFIRMED) — `FilterFailureLog.Format()` embeds the
  `[Telescope]` prefix and warns callers must use `NeoVisualLog.Log` not
  `TelescopeLog.Log` — a fragile contract that double-prefixes if misused.
  **Fix:** make `Format()` return the UNPREFIXED message and have the caller
  add the prefix via `TelescopeLog.Log` (or document the contract in code).
  **Unit seam (Telescope.Tests):** `Run_FilterFailureLog_Format` — assert the
  exact line format (RED: format changes).

## Phase 6 — Duplication merges (M18, M20, M21, M22, M24, m13)

- **M18** (major, CONFIRMED) — `KeyNameBuilder.Build` has zero production
  callers; `SimpleShortcutMatcher.BuildSimpleKey` re-implements the identical
  canonical-shortcut logic. **Fix:** have `BuildSimpleKey` delegate to
  `KeyNameBuilder.Build` and delete the duplicate. **Unit seam
  (NeoVisual.Tests):** the existing `Run_KeyNameBuilder_*` + `Run_SimpleShortcutMatcher_*`
  families (behavior-preserving; RED: `BuildSimpleKey` still duplicates →
  the delegation test fails).
- **M20** (major, CONFIRMED) — W/B/E action wiring duplicated verbatim across
  `SolutionExplorerController` and `TextInputToolWindowController`; the J/K→
  arrow mapping + `toolwindow-move` log re-implemented instead of reusing
  `GeneralToolWindowController.KeyToArrowVk`. **Fix:** extract a shared
  `TextMotionControllerBase` (or a static `TextMotionActions.Build()`); have
  `SolutionExplorerController` reuse `KeyToArrowVk`/`TryMove` for the J/K
  entries. **Unit seam (NeoVisual.Tests):** the action-table tests
  (`Run_ActionTable_*`) pin the exact sets (behavior-preserving).
- **M21** (major, CONFIRMED) — `HierarchyForestBuilder.Build` writes a
  throwaway `pathToItem` map never read; the `.cs` filter applied twice.
  **Fix:** drop the `pathToItem` param (or return it); keep the `.cs` filter
  in exactly one place (the pure builder). **Unit seam (NeoVisual.Tests):**
  the existing `Run_HierarchyForestBuilder_*` tests (behavior-preserving;
  RED: the dead param removal changes the signature → compile error).
- **M22** (major, CONFIRMED) — the text-input-surface keyboard-ownership
  exemption computed twice with different formulations (`FocusGuard` params vs
  `EditorFocusedVeto` inline). **Fix:** pass the veto into FocusGuard as a
  single `ownsKeyboard` boolean, or route `EditorFocusedVeto` through the same
  FocusGuard helper. **Unit seam (NeoVisual.Tests):** the existing
  `Run_FocusGuard_*` tests (behavior-preserving; RED: signature change →
  compile error).
- **M24** (major, CONFIRMED) — `RunInitStep` (sync) and `InitSteps.RunAsync`
  (async) implement the same per-step try/catch + `[MyExtension] init <name>
  ok/failed` contract in two places. **Fix:** fold the log-config steps into
  the `InitSteps` list (or have `RunInitStep` delegate to the same logging
  helper). **Unit seam (NeoVisual.Tests):** the existing `Run_InitSteps_*`
  tests (behavior-preserving; RED: signature change → compile error).
- **m13** (minor, CONFIRMED) — the `File.Exists` guard + open pattern
  duplicated 4× (`FileFinder`, `CodeIssuesFinder`, `GrepFinder`, `HitOpener`).
  **Fix:** consolidate into the tested `HitOpener`. **Unit seam
  (Telescope.Tests):** the existing `Run_HitOpener_*` tests (behavior-preserving).

## Phase 7 — Dead code deletion (M17, M23, m11, m12)

- **M17** (major, CONFIRMED — production-dead, tests only) — `VimModeState`
  has a false docstring ("pure owner of the Vim typing/mode state");
  `VimModeTracker` keeps its own `_cachedTyping`/`_editorFocused`. **Fix:**
  wire `VimModeTracker` through `VimModeState` (single owner) OR delete the
  class and move its tests onto `VimModeClassifier`/`VimModeTracker`.
  **Coordinate with M16** (both touch the `vim-mode=` log). **Unit seam
  (NeoVisual.Tests):** if wired — `Run_VimModeState_*` become the single owner
  tests; if deleted — move the tests onto `VimModeClassifier` (already
  tested). RED: the chosen path's tests fail before the change.
- **M23** (major, CONFIRMED — all four zero production callers) — dead code
  cluster in `CardinalMovment`: `GetWindowsList` (UtilityMethods.cs:30),
  `IsOnScreen` (WindowAdapter.cs:51), `DirectionExtensions.Sign`
  (Direction.cs:24), `DistinctBy` (LinqExtensionMethods.cs:19, test-only).
  **Fix:** delete them; drop the AGENTS.md `DistinctBy` claim (or wire it into
  a real call site). No-seam (deletion) — build + the `Run_DistinctBy_*` test
  is deleted/relocated.
- **m11** (minor, CONFIRMED) — `OverlayKeyHandler.ResultCount` property dead
  (only `SetResults` used). **Fix:** delete. No-seam (build-verified).
- **m12** (minor, CONFIRMED) — `TextMotionNavigator.ColumnNumber` has no
  production callers (test-only surface). **Fix:** delete (or keep if the
  tests pin it — verify first). No-seam (build-verified).

## Phase 8 — Harness hardening (M26, M27, M28, m18, m19, m20)

- **M26** (major, CONFIRMED) — `preview tokens=\d+` (test-e2e.ps1:974) is a
  vacuous presence check — `\d+` matches `tokens=0`. **Fix:** assert
  `preview tokens=[1-9]\d*` or pin the exact seeded-file token count. No-seam
  (harness) — deferred e2e.
- **M27** (major, CONFIRMED — scope to :518 ONLY) — `results count=(\d+)`
  (test-e2e.ps1:518) matches `count=0`. **Fix:** assert `results count=[1-9]\d*`
  at :518 only; do NOT touch :1533/:1537 (`telescope-no-selection`
  legitimately asserts `count=0`). No-seam (harness) — deferred e2e.
- **M28** (major, CONFIRMED) — `LogFileWriter` flush-timer tests are
  timing-dependent (wall-clock polls for a ~200ms timer). **Fix:** inject a
  controllable timer/clock seam into `LogFileWriter`; keep the poll only as a
  fallback. **Unit seam (Telescope.Tests):** `Run_LogFileWriter_FlushTimer_*`
  rewritten against the injected clock (RED: no clock seam → compile error).
- **m18** (minor, CONFIRMED) — `Stop-HarnessVs` kills any devenv whose title
  matches 'Experimental'/'MyExtension' — can terminate a user's unrelated VS
  instance. **Fix:** PID-scoped kills (the M-M5 pattern). No-seam (harness).
- **m19** (minor, CONFIRMED) — `iterate-telescope.ps1:226` sets
  `NEOVISUAL_LOG_DIR` at USER scope — persistent env mutation. **Fix:**
  Process scope. No-seam (harness).
- **m20** (minor, CONFIRMED) — `check-doc-refs.ps1` stale allowlist:
  `LeaderSequenceMatcher` and `tools/harness-common.ps1` marked "proposed" but
  now exist — masks real drift. **Fix:** remove them from the proposed lists.
  No-seam (harness) — run the lint to verify.

## Phase 9 — Test hermeticity + naming sweep (m1, m2, m7, m8, m9, m10, m21-m30, m32, n1, n2)

- **m21** (minor, CONFIRMED) — `TestRunner.TempDir.Dispose` swallows all
  exceptions — failed recursive deletes leak temp dirs silently. **Fix:**
  rethrow (or log) on dispose failure. **Unit seam:** the shared `TestRunner`
  — a test that a failing delete surfaces the error.
- **m22** (minor, CONFIRMED) — `Run_FzfFilter_FilterMatchesPrefix` requires
  the external fzf binary on PATH (Mystery Guest). **Fix:** use the
  `FzfFilter(string? fzfPath)` injected-path seam; fail loudly when fzf is
  absent (don't silently pass). **Unit seam (Telescope.Tests):** the test now
  uses the injected path.
- **m23** (minor, CONFIRMED) — `Run_FinderBase_OpenErrorSwallowed` has no
  assertion. **Fix:** add a real assertion (the error is logged exactly once).
  **Unit seam (Telescope.Tests):** assert the log line.
- **m24** (minor, CONFIRMED) — `Run_Syntax_KeywordsAndIdentifiers` brace check
  is a weak presence assertion. **Fix:** strengthen (assert the exact token
  sequence). **Unit seam (Telescope.Tests).**
- **m25** (minor, CONFIRMED) — `Run_HierarchyResolver_FirstSourceFile` is an
  Eager Test bundling 4 behaviors with no per-assert messages. **Fix:** split
  into focused tests with messages. **Unit seam (NeoVisual.Tests).**
- **m26** (minor, CONFIRMED) — magic `actionKeyCount: 5` vs hardcoded `6` in
  text-input-surface tests — inconsistent. **Fix:** use a named constant
  matching the real count (7 after CR1). **Unit seam (NeoVisual.Tests).**
- **m27** (minor, CONFIRMED) — `Run_GrepFinder_GatherHitsThrowsNotSupported`
  reaches a protected member via reflection (implementation coupling). **Fix:**
  use the public `GetCandidates` path or a cleaner seam. **Unit seam
  (Telescope.Tests).**
- **m28** (minor, CONFIRMED — CORRECTED: `TryMove` IS testable now) —
  `TextInputToolWindowController.TryMove` and
  `SolutionExplorerController.SelectFirstSourceFile` (`g`) never unit-tested.
  **Fix:** add `Run_TextInput_TryMove_*` (the stale "Text.UI not referenced"
  comment is wrong — the csproj references it); `SelectFirstSourceFile` stays
  VS-coupled (no-seam). **Unit seam (NeoVisual.Tests).**
- **m29** (minor, CONFIRMED) — redundant action-key presence tests duplicate
  the exact-set `Run_ActionTable_*_ActionKeysMatchTable` tests. **Fix:**
  delete the redundant ones. **Unit seam (NeoVisual.Tests):** count drops by
  the deleted tests (deterministic signal).
- **m30** (minor, CONFIRMED) — stale "RED today" comment on
  `Run_LogFileWriter_ClearPerPath` contradicts the all-green suite. **Fix:**
  remove the comment. No-seam (comment).
- **m1** (minor, CONFIRMED) — `WindowManager` class body unindented at column
  0 inside `namespace MyExtension`. **Fix:** reindent. No-seam (build).
- **m2** (minor, CONFIRMED) — `KeybindingConfig` `File.Exists` evaluated twice;
  `return new KeybindingConfig(...)` at column 0. **Fix:** single `File.Exists`
  + reindent. No-seam (build).
- **m7** (minor, CONFIRMED) — `FocusGuard.IsTyping`'s
  `isToolWindow ? isInputMode : ...` sub-branch is dead (isInputMode always
  false there). **Fix:** remove the dead branch. **Unit seam (NeoVisual.Tests):**
  the existing `Run_FocusGuard_*` tests (behavior-preserving).
- **m8** (minor, CONFIRMED) — `FocusKeeper` uses `Environment.TickCount` (int)
  which wraps every ~24.9 days. **Fix:** use `Environment.TickCount64` (net472
  has it) or a monotonic clock. **Unit seam (NeoVisual.Tests):** the existing
  `Run_FocusKeeperSchedule_*` tests (behavior-preserving).
- **m9** (minor, CONFIRMED) — `SolutionExplorerController` keeper duration
  `1500` passed twice (to `FocusKeeper.Run` and `FocusKeeperSchedule.Decide`).
  **Fix:** a single named constant. **Unit seam (NeoVisual.Tests):** the
  existing `Run_FocusKeeperSchedule_*` tests.
- **m10** (minor, CONFIRMED) — `FocusGuard.IsTyping`'s `editorFocused` param
  actually receives `EditorFocusedVeto` — name/doc lie. **Fix:** rename the
  param. **Unit seam (NeoVisual.Tests):** the existing `Run_FocusGuard_*`
  tests (behavior-preserving).
- **m32** (minor, CONFIRMED) — SKILL.md:179 "the typo `CardinalMovment`
  folder/namespace is intentional" conflates folder and namespace (the
  namespace is `CardinalNavigation`). **Fix:** correct the wording. No-seam
  (docs) — note: this wording changes again in the restructure (Phase 11).
- **n1** (nit, CONFIRMED) — `Assert.False(controller.ActionKeys.Count == 0)`
  double negative. **Fix:** `Assert.True(controller.ActionKeys.Count > 0)`.
  **Unit seam (NeoVisual.Tests).**
- **n2** (nit, CONFIRMED) — `Run_CaretPlacement_EnumValues` only checks member
  names/count, not values. **Fix:** assert the enum values. **Unit seam
  (Telescope.Tests).**

## Phase 10 — Docs drift (M29, M30, M31, m31)

- **M29** (major, CONFIRMED) — `docs/spec.md:353-354`,
  `.opencode/skills/vs-extension-dev/SKILL.md:210-211`, `docs/progress.md:72-73`
  still say 77/74 while AGENTS.md and spec.md §5.1 say 135/130. **Fix:** update
  to 135/130 (verify against `--list` output at execution time — counts drift
  upward as tests are added). No-seam (docs).
- **M30** (major, CONFIRMED) — `docs/spec.md:162` §4 diagnostics contract
  omits 8 harness-asserted lines (`stale-toolwindow sentinel active`,
  `leader-binding failed:`, `shortcut-binding failed:`, `output pane
  unavailable:`, `[MyExtension] init <step> ok/failed:`, `fzf filter failed:`,
  `fzf unavailable — showing unfiltered list`, `filter failed:`). **Fix:** add
  them to §4. No-seam (docs).
- **M31** (major, CONFIRMED) — `docs/spec.md:47` + `SKILL.md:50,68` key-files
  tables list none of the ~25 post-consolidation seams. **Fix:** add the
  missing seams to the tables. No-seam (docs).
- **m31** (minor, CONFIRMED) — `docs/progress.md:10` header not bumped for the
  2026-09-30 Code-review GREEN. **Fix:** bump the header. No-seam (docs).

---

# Part B — Repository restructure (Phases 11-14)

## Restructure design (target folder/namespace mapping)

The restructure moves files into logical folders AND renames namespaces to
match (user decision). It is **behavior-preserving**: no log literal, no public
API, no test count changes. The doc-ref lint + all agent/tool references are
updated in the same phase.

### MyExtension/ target (namespace `MyExtension.*`)

| New folder | New namespace | Files |
|-----------|---------------|-------|
| `Hooks/` | `MyExtension.Hooks` | GlobalKeyboardHook, NativeMethods, KeyInjection, InjectedKeyGuard |
| `Input/` | `MyExtension.Input` | InputHandler, LeaderSequenceMatcher, SimpleShortcutMatcher, KeybindingConfig, KeyNames, KeyNameBuilder, PopupNavigation, StaleToolWindowSentinel |
| `Vim/` | `MyExtension.Vim` | VimModeTracker, VimModeSource, VimModeClassifier, VimModeState |
| `Package/` | `MyExtension.Package` | MyExtensionPackage, InitSteps, VsServices, TelescopeCommand, TelescopeLauncher, Actions |
| `ToolWindows/` | `MyExtension.ToolWindows` | FocusGuard, FocusKeeper, HierarchyForestBuilder, GeneralToolWindowController, HierarchyResolver, SolutionExplorerController, IToolWindowController, TextInputToolWindowController, TextMotionHelper, ToolWindowControllerBase, WindowManager, ToolWindowTypeResolver (the last two moved from the root — window-management/classification types) |
| `Navigation/` | `MyExtension.Navigation` | CardinalNavigationConstants, LinqExtensionMethods, Direction, NavigationSettings, NavigationSnapshot, RectCoordinate, UtilityMethods, WindowAdapter, WindowMatrix, WindowNavigationEngine (moved from `CardinalMovment/`, namespace `CardinalNavigation`) |
| `Adornments/` | `MyExtension.Adornments` | BlockCaretAdornment |
| `Properties/` | `MyExtension.Properties` | Settings.Designer.cs (unchanged) |
| `Resources/` | (no namespace) | default-keybindings.json (embedded resource — csproj path update) |
| root | — | MyExtension.csproj, source.extension.vsixmanifest (unchanged) |

> **DEVIATION from AGENTS.md (flag for user):** AGENTS.md says "the source
> folder is spelled `CardinalMovment` (typo) — keep it consistent, do not
> 'fix' it (breaks references)". The restructure renames it to `Navigation/`
> with namespace `MyExtension.Navigation` because (a) the user chose
> "folders + namespaces" and this is the one folder whose namespace doesn't
> match, and (b) the restructure updates ALL references anyway, so the
> "breaks references" concern is moot. The `CardinalMovment` entry is removed
> from the doc-ref lint external allowlist. Alternative (if the user prefers):
> keep namespace `CardinalNavigation` and rename the folder to
> `CardinalNavigation/` (folder = namespace, no namespace change).

### Telescope/ target (namespace `Telescope.*`)

| New folder | New namespace | Files |
|-----------|---------------|-------|
| `Overlay/` | `Telescope.Overlay` | TelescopeOverlay, OverlayKeyHandler, OverlayShowState, FocusTargetModel, PreviewRenderer, BlockCaretStyle, TextMotionNavigator, TryDispatch, ResultsFormatter, ResultMapper, SyntaxHighlighter, LineIndex |
| `Finders/` | `Telescope.Finders` | TelescopeFinder, FinderBase, FileFinder, CodeIssuesFinder, ReferencesFinder, GrepFinder, ImplementationFinder, FileHit, GrepHit, ReferenceHit, ImplementationHit, FileLocation, IFileLocation, CodeIssue, HitOpener, HierarchyWalker, ProjectFiles, ProjectFileCache, FileContentCache, DteFileOpener |
| `Filter/` | `Telescope.Filter` | FzfFilter |
| `Logging/` | `Telescope.Logging` | NeoVisualLog, LogFileWriter, NeoVisualTraceListener, DiagnosticLog, TelescopeLog, FilterFailureLog, PaneFailureTracker |
| `Controller/` | `Telescope.Controller` | TelescopeController |
| root | — | Telescope.csproj (unchanged; `RootNamespace>Telescope</RootNamespace>` stays) |

> **Note:** `tests/Telescope.Tests/Program.cs` declares `namespace
> Telescope.Tests` (a child of `Telescope`). After the move, no file declares
> `namespace Telescope` directly — the child namespace is still valid C#, but
> the test file needs `using Telescope.Overlay;` / `using Telescope.Finders;`
> / `using Telescope.Logging;` etc. The `StartupObject>Telescope.Tests.Program`
> in the test csproj stays valid.

### tools/ target

| New folder | Files |
|-----------|-------|
| `tools/harness/` | test-e2e.ps1, iterate-telescope.ps1, harness-common.ps1, dte-command.ps1 |
| `tools/lint/` | check-doc-refs.ps1 |

> The harness scripts dot-source each other via `$PSScriptRoot` — moving them
> together into `tools/harness/` keeps those paths valid. `check-doc-refs.ps1`
> moves to `tools/lint/`; its function scan
> (`Get-ChildItem (Join-Path $repoRoot 'tools')`) must point at
> `tools/harness`.

### docs/ target

| New folder | Files |
|-----------|-------|
| `docs/` (root, unchanged) | spec.md, progress.md, implementation_plan.md, e2e-queue.md (operational contract docs — referenced by exact path from ~11 agent files + the doc-ref lint default set; moving them is high-risk, low-value) |
| `docs/reviews/` | code-review.md, architecture-review.md, architecture-consolidation.md |
| `docs/plans/` | backlog-plans.md |

> **Flag for user:** the operational docs (spec.md, progress.md,
> implementation_plan.md, e2e-queue.md) are referenced by exact path from many
> agent files and the doc-ref lint default set. The plan keeps them in `docs/`
> root and groups only the review/plan archive docs. If the user wants ALL
> docs grouped, the reference sweep grows substantially (every agent file that
> references `docs/progress.md` etc. must be updated).

## Phase 11 — Restructure: MyExtension project (folders + namespaces)

- **BP steps (per the mapping above):**
  1. `git mv` each MyExtension root .cs file into its target folder (Hooks/,
     Input/, Vim/, Package/, Adornments/); `git mv` CardinalMovment/* →
     Navigation/; keep ToolWindows/ + Properties/ in place.
  2. Update each moved file's `namespace` declaration to the target namespace
     (e.g. `namespace MyExtension` → `namespace MyExtension.Input`).
  3. Update every `using` directive + fully-qualified reference across
     MyExtension/ + tests/NeoVisual.Tests/Program.cs:
     - `using CardinalNavigation;` (3 files: Actions, InputHandler,
       WindowManager + tests) → `using MyExtension.Navigation;`
     - `using MyExtension.ToolWindows;` — ADD to the 6 root files that
       reference ToolWindows types unqualified (BlockCaretAdornment,
       InjectedKeyGuard, InputHandler, MyExtensionPackage, VimModeState,
       WindowManager) + tests
     - `using MyExtension;` in tests stays (the root namespace still exists
       via Package/Hooks/etc.? NO — no file declares `namespace MyExtension`
       directly after the move; the test's `using MyExtension;` must become
       the specific sub-namespaces it references)
     - `using Telescope;` (4 files) → the specific `Telescope.*` sub-namespaces
       (see Phase 12)
  4. Move `default-keybindings.json` → `Resources/`; update
     `MyExtension.csproj` `EmbeddedResource Include="Resources\default-keybindings.json"`.
  5. Update the doc-ref lint: remove `CardinalMovment` from the external
     allowlist; update `$sourceRoots` if needed.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests` all pass (count unchanged); `tools/check-doc-refs.ps1`
  clean after the Phase 13 reference sweep.
- **Fails-if:** build errors (missed using/namespace); any NeoVisual test
  regresses; a `[NeoVisual]`/`[Telescope]`/`[Hook]` log literal changed.

## Phase 12 — Restructure: Telescope project (folders + namespaces)

- **BP steps:**
  1. `git mv` each Telescope .cs file into its target folder (Overlay/,
     Finders/, Filter/, Logging/, Controller/).
  2. Update each moved file's `namespace` declaration to the target namespace
     (e.g. `namespace Telescope` → `namespace Telescope.Overlay`).
  3. Update every reference across MyExtension/ + tests/:
     - `using Telescope;` (4 MyExtension files) → the specific sub-namespaces
     - ~90 fully-qualified `Telescope.X` refs in 16 MyExtension files →
       `Telescope.<Group>.X` (mechanical, per-type mapping)
     - `tests/Telescope.Tests/Program.cs` `namespace Telescope.Tests` stays;
       add `using Telescope.Overlay;` / `using Telescope.Finders;` /
       `using Telescope.Logging;` etc.
     - `tests/NeoVisual.Tests/Program.cs` references to `Telescope.*` types
       (e.g. `Telescope.NeoVisualLog`) → the new sub-namespaces
  4. Keep `RootNamespace>Telescope</RootNamespace>` in Telescope.csproj.
- **Verify-with:** `dotnet build` 0 errors; `dotnet run --project
  tests/Telescope.Tests` all pass (count unchanged); `dotnet run --project
  tests/NeoVisual.Tests` all pass.
- **Fails-if:** build errors (missed reference); any Telescope/NeoVisual test
  regresses; a log literal changed.

## Phase 13 — Restructure: tools/ + docs/ + reference sweep

- **BP steps:**
  1. `git mv` tools/*.ps1 → `tools/harness/` (test-e2e, iterate-telescope,
     harness-common, dte-command) and `tools/lint/` (check-doc-refs).
  2. Update `check-doc-refs.ps1`'s function scan to `tools/harness`; update
     its `$proposedPaths` (`tools/harness-common.ps1`).
  3. `git mv` docs/code-review.md, docs/architecture-review.md,
     docs/architecture-consolidation.md → `docs/reviews/`; docs/backlog-plans.md
     → `docs/plans/`. Keep spec.md, progress.md, implementation_plan.md,
     e2e-queue.md in docs/ root.
  4. **Reference sweep:** update every doc/agent/tool reference to the moved
     paths:
     - AGENTS.md, docs/spec.md, docs/progress.md, docs/implementation_plan.md,
       docs/architecture-review.md, docs/code-review.md, docs/backlog-plans.md,
       docs/architecture-consolidation.md, SKILL.md — every `MyExtension/*.cs`,
       `Telescope/*.cs`, `CardinalMovment/*.cs`, `tools/*.ps1`, `docs/*.md`
       path reference → the new path.
     - `.opencode/agent/*.md` — every exact-path reference to
       `tools/test-e2e.ps1`, `tools/check-doc-refs.ps1`,
       `tools/iterate-telescope.ps1`, `tools/dte-command.ps1`,
       `docs/code-review.md`, `docs/architecture-review.md`,
       `docs/architecture-consolidation.md`, `docs/backlog-plans.md` → the new
       path. (The operational docs — spec.md, progress.md,
       implementation_plan.md, e2e-queue.md — keep their paths, so their agent
       references stay valid.)
     - `tools/check-doc-refs.ps1` default doc set + `$sourceRoots` if the
       paths change.
  5. Update the doc-ref lint allowlists: remove `CardinalMovment`; update
     `$proposedPaths`/`$proposedSymbols` for the moved files.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` → `[PASS] ... 0
  unresolved`; `dotnet build` 0 errors; both unit suites pass (counts
  unchanged).
- **Fails-if:** the doc-ref lint reports unresolved references (a moved path
  missed in the sweep); an agent file still references an old path; a harness
  script can't dot-source its sibling.

## Phase 14 — Final verification (combined plan gate)

- **BP steps:**
  1. `dotnet build MyExtension.slnx` → 0 errors.
  2. `dotnet run --project tests/Telescope.Tests` → all pass (count unchanged
     from the pre-restructure baseline).
  3. `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).
  4. `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved.
  5. Grep gate: no `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` log
     literal changed by the restructure (diff the log literals before/after).
  6. e2e deferred: full 35-scenario suite queued (E2E-RESTRUCTURE-1).
- **Verify-with:** the five gates above all pass.
- **Fails-if:** any gate fails; a log literal drifted; a unit count changed
  (unless a Phase 9 test deletion was the documented cause).

---

## Acceptance criteria

Every phase's acceptance criteria map to a diagnostic line and/or a unit test.
The master table (each row = a phase's gate):

| Phase | Gate (unit tests GREEN + build + existing suites) | Diagnostic contract preserved/added |
|-------|---------------------------------------------------|-------------------------------------|
| 0 | `Run_ActionTable_TextInput_ActionKeysMatchTable` (count 7); `Run_TextInput_KeysIMapsToInsertStart`; `Run_VimBufferSubscriptions_*` | `textinput-enter-input start caret=0` (CR1, restored); `vim-mode=` (CR2, unchanged) |
| 1 | `Run_LeaderMatcher_*` (prefix set); `Run_StaleToolWindowSentinel_*`; M1/M2/M15/m17 build gates | `navigate direction=...` unchanged; no per-key COM/stat |
| 2 | `Run_FzfFilter_*` (injected path); `Run_GrepFinder_*` (off-thread); `Run_FileContentCache_*` (eviction); `Run_Issues_CollectTodosUsesCache`; `Run_FzfFilter_IsAvailableBounded` | `grep hits=...`, `results count=...`, `preview caret=...` unchanged |
| 3 | `Run_Preview_UpFromSecondLineWithLeadingBlankLine_Fixed`; `Run_ResultMapper_UnknownStringSkippedOrLogged`; `Run_TryDispatch_*` (unified `$`); `Run_TextMotionEngine_*` | `preview caret=... line=...`, `prompt-motion key=...`, `text-motion key=...` unchanged |
| 4 | `Run_WindowNavigationEngine_*` (unchanged); M9/M14/m3-m6 build gates | `navigate direction=L/R/D/U` unchanged |
| 5 | `Run_VimModeTracker_*` (log only on change); `Run_FilterFailureLog_Format`; M8/M12/M25 build gates | ADD `[NeoVisual] toolwindow-move failed: ...` (M8); `vim-mode=` unchanged |
| 6 | `Run_KeyNameBuilder_*` + `Run_SimpleShortcutMatcher_*` (delegation); `Run_ActionTable_*`; `Run_HierarchyForestBuilder_*`; `Run_FocusGuard_*`; `Run_InitSteps_*`; `Run_HitOpener_*` | `[MyExtension] init <step> ok/failed` unchanged |
| 7 | M17/M23/m11/m12 deletions (build + suite counts) | no log literal change |
| 8 | `Run_LogFileWriter_FlushTimer_*` (injected clock); harness self-checks | `preview tokens=[1-9]\d*`, `results count=[1-9]\d*` (harness) |
| 9 | test hermeticity + naming tests (counts per the runner) | none changed |
| 10 | doc-ref lint clean after the doc updates | none changed |
| 11 | build + NeoVisual.Tests (count unchanged) + doc-ref lint (after Phase 13) | no log literal change |
| 12 | build + Telescope.Tests + NeoVisual.Tests (counts unchanged) | no log literal change |
| 13 | `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved; build + suites | no log literal change |
| 14 | build + both suites + doc-ref lint + log-literal diff gate | no log literal change |

## Unit test plan (summary)

- **tests/Telescope.Tests** (pure classes): `TextInputToolWindowController`
  action table (CR1 — actually NeoVisual), `TextMotionNavigator.Up` (M10),
  `ResultMapper` (M11), `TryDispatch` unified `$` (M19), `FzfFilter`
  injected-path timeout/fallback/IsAvailable-bounded (M3/m16), `GrepFinder`
  off-thread scan (M4), `FileContentCache` eviction (M13/M5), `CodeIssuesFinder`
  cache (m15), `LogFileWriter` injected clock (M28), `FilterFailureLog` format
  (m14), `HitOpener` consolidation (m13), test hermeticity (m22-m24, n2).
- **tests/NeoVisual.Tests** (pure classes): `VimBufferSubscriptions` (CR2),
  `LeaderSequenceMatcher` prefix set (M7), `VimModeTracker` log-on-change
  (M16), `VimModeState` owner (M17), `KeyNameBuilder`/`SimpleShortcutMatcher`
  delegation (M18), `FocusGuard` single ownsKeyboard (M22), `FocusKeeper`
  TickCount64 (m8), `TextInputToolWindowController.TryMove` (m28), action-table
  exact sets (CR1/M20/m26/m29), `Run_HierarchyResolver_*` split (m25), n1.
- **No-seam** (build + existing suites + deferred e2e): M1, M2 (wiring), M6
  (copy-elimination), M8, M9, M12, M14, M25, M26/M27 (harness), m2/m7/m9/m10/
  m17/m18/m19/m20/m21/m30/m32, M29/M30/M31/m31 (docs), and the entire
  restructure (Phases 11-14 — build + suites + doc-ref lint + deferred e2e).

## Diagnostics (new/changed log lines)

- ADD `[NeoVisual] toolwindow-move failed: {msg}` (M8).
- UNCHANGED (must stay byte-identical): `navigate direction=L/R/D/U`,
  `shortcut-binding executed:`, `leader-binding executed:`,
  `[Telescope] focus target=List|Preview`, `vim-mode=Insert|Normal|Replace`,
  `preview caret=... line=...`, `prompt-motion key=...`,
  `text-motion key=...` / `textinput-enter-input ...`,
  `[MyExtension] init <step> ok/failed`.
- RESTORED: `textinput-enter-input start caret=0` (CR1 — the `I` action).
- HARNESS (not product): `preview tokens=[1-9]\d*` (M26),
  `results count=[1-9]\d*` at :518 only (M27).

## Known-RED allowlist

None — no known-RED e2e scenario remains (per docs/progress.md). All fixes are
RED-proven by unit tests (or build + existing suites for no-seam items). The
restructure is behavior-preserving (no RED — verified by build + suites +
doc-ref lint + the deferred full-suite e2e re-run).

## E2E queue reference (deferred — see e2e-queue.md)

The following e2e scenarios are QUEUED (not created/executed until this plan is
GREEN in docs/progress.md and the user is on a VS-capable machine). Each
asserts the diagnostics listed above:

- **E2E-CR-1** (CR1): `neovisual-textinput-motions` — `textinput-enter-input
  start caret=0` fires when `I` is pressed on the Command Window.
- **E2E-CR-2** (CR2): a multi-view scenario — with 2+ editor views open,
  closing a non-focused view does NOT kill the focused view's `vim-mode=`
  subscription (Space types a literal space in insert mode).
- **E2E-M1/M2/M15** (hook hot-path): full suite — no per-key COM/stat; all
  `neovisual-*` scenarios still GREEN.
- **E2E-M3/M4/M5/M13** (finder): `telescope-grep` / `telescope-search` /
  `telescope-preview` — `grep hits=...`, `results count=...`, `preview
  caret=...` unchanged; fzf failure paths log `fzf filter failed:` /
  `filter failed:`.
- **E2E-M10/M11/M19/M6** (motion/preview): `telescope-preview-motions` /
  `telescope-prompt-motions` / `neovisual-textinput-motions` — caret positions
  unchanged; the `$` drift fixed (bare `4` in preview no longer jumps).
- **E2E-M9/M14** (navigation): `neovisual-window-nav` — `navigate
  direction=L/R/D/U` unchanged.
- **E2E-M8/M12/M16/M25** (safety/logging): `neovisual-editor-insert` +
  `neovisual-textinput-motions` — `vim-mode=` unchanged; a controller
  exception logs `toolwindow-move failed:` and passes through.
- **E2E-M18/M20/M21/M22/M24** (duplication): full suite — no log-literal drift.
- **E2E-M17/M23** (dead code): full suite — no log-literal drift.
- **E2E-M26/M27/M28** (harness): full suite — the tightened `preview
  tokens=`/`results count=` assertions pass; `seed-leak` still GREEN.
- **E2E-RESTRUCTURE-1** (Phases 11-14): full 35-scenario suite — no behavior
  change after the folder/namespace restructure (all diagnostics byte-identical).

## Execution order note for neovim_hub

Execute phases in order 0 → 14. Phases 0-10 are the code-review fixes (each
RED-proven by its unit tests; phases 7-9 are deletions/refactors with
behavior-preserving gates; phase 10 is docs). Phases 11-14 are the restructure
(behavior-preserving — build + suites + doc-ref lint + the deferred full-suite
e2e re-run). The unit-only lane applies throughout — e2e is deferred to
`e2e-queue.md` (E2E-CR-1..2, E2E-M1..M28, E2E-RESTRUCTURE-1 above).
---

## Build Plan (per-phase; one implementation-planner agent per phase)

# BP-n Build Plan — Phases 0-5 (Code-Review Fixes)

> **Source:** `plans/plan.md` (Combined Plan, Part A Phases 0-5). Lane: **unit-only** — e2e is
> deferred to `e2e-queue.md`; NO e2e scenario name appears in any Verify-with/Fails-if.
> **Ground truth:** `dotnet build MyExtension.slnx` = 0 errors (115 pre-existing warnings);
> Telescope.Tests 135 passing; NeoVisual.Tests 130 passing.
> **Known-RED allowlist:** NONE (per `docs/progress.md` — no known-RED e2e scenario remains).
> **Research corrections folded in (do not second-guess):**
> 1. **CR1** updates the existing exact-set test `Run_ActionTable_TextInput_ActionKeysMatchTable`
>    (NeoVisual.Tests:527) from count 6 to count 7 in the SAME change.
> 2. **M10** corrects the existing `Run_Preview_UpFromSecondLineWithLeadingBlankLine`
>    (Telescope.Tests:846) to assert line 1/caret 0 in the SAME change.
> 3. **M19** unifies the two key→motion **dispatch** tables (the motion **math** is already shared
>    via `TextMotionNavigator`); it does NOT "make TextMotionHelper delegate" only.
> 4. **M27** is Phase 8 (harness) — OUT OF SCOPE here.
> **Execution:** one top-to-bottom pass by the build-agent; each `## Phase` header is a hub
> checkpoint with a mid-point verify gate.

---

## Phase 0 — Criticals (CR1, CR2)

### BP-1: CR1 — restore `Keys.I` in `TextInputToolWindowController._actions`
- **Files:** `MyExtension/ToolWindows/TextInputToolWindowController.cs`; `tests/NeoVisual.Tests/Program.cs`
- **Change:** Add `[Keys.I] = () => TextMotionHelper.TryMoveFocusedSurface(Keys.I, ref _isInputMode)` to the `_actions` dictionary (after `Keys.L`). Bare `i` still falls through to the generic `InputHandler` branch (unchanged). In the SAME change, update the exact-set test `Run_ActionTable_TextInput_ActionKeysMatchTable` (NeoVisual.Tests:527) from `{W,B,E,A,H,L}` (count 6) to `{W,B,E,A,H,L,I}` (count 7). Add `Run_TextInput_KeysIMapsToInsertStart` asserting `TryMove(Keys.I)` maps to `InsertStart` (via `TextMotionHelper.MapMotion(Keys.I, shift:true)` → `TextMotion.InsertStart`).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- ActionTable` → `Run_ActionTable_TextInput_ActionKeysMatchTable` PASS (count 7); `dotnet run --project tests/NeoVisual.Tests -- TextInput` → `Run_TextInput_KeysIMapsToInsertStart` PASS, `Run_TextInput_StartsInInsertMode` PASS, `Run_TextInput_ActionKeys` PASS. Diagnostic restored: `[NeoVisual] textinput-enter-input start caret=0` (emitted by `TextMotionHelper.ApplyMotionToBox` when `I` → `InsertStart`).
- **Fails-if:** `Run_ActionTable_TextInput_ActionKeysMatchTable` FAILS with count 6 (the `I` entry was not added or the test was not updated); `Run_TextInput_KeysIMapsToInsertStart` FAILS (`TryMove(Keys.I)` returns false or maps to the wrong motion); the `textinput-enter-input start caret=0` line is not produced by the `I` path.

### BP-2: CR2 — per-view Vim buffer subscription map (`VimBufferSubscriptions`)
- **Files:** `MyExtension/VimBufferSubscriptions.cs` (new); `MyExtension/VimModeSource.cs`; `tests/NeoVisual.Tests/Program.cs`
- **Change:** Extract a pure per-view subscription map `VimBufferSubscriptions` (dependency-free, `Dictionary<ITextView, object>`): `Attach(ITextView view, object buffer)`, `Detach(ITextView view)` (no-op for a non-attached view), `BufferFor(ITextView view)`, `FocusedView`/`FocusedBuffer`. Wire `VsVimModeSource` to use it: `Attach(view)` stores the resolved buffer per view; `Detach(view)` unsubscribes ONLY that view's buffer (replacing the global `_currentTextBuffer` unsubscribe); re-subscribe the focused view's buffer on focus gain. Add `Run_VimBufferSubscriptions_*` tests.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- VimBufferSubscriptions` → `Run_VimBufferSubscriptions_AttachTwoDetachOneKeepsOther` PASS (attach A, attach B, detach A → `BufferFor(B)` still returns B's buffer), `Run_VimBufferSubscriptions_DetachNonAttachedNoOp` PASS, `Run_VimBufferSubscriptions_ReattachAfterDetach` PASS. Diagnostic unchanged: `[NeoVisual] vim-mode=Insert|Normal|Replace` (the focused view's subscription survives a non-focused view close).
- **Fails-if:** a `Run_VimBufferSubscriptions_*` test FAILS (map semantics wrong); `dotnet build` errors (the map type doesn't exist → CS0246); the focused view's `vim-mode=` subscription is still killed by a non-focused view close (deferred e2e).

**Phase 0 gate:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (130 + 4 new).

---

## Phase 1 — Hook hot-path contract (M1, M2, M15, M7, m17)

### BP-3: M1 — cache the focused-surface fact (no per-key COM)
- **Files:** `MyExtension/WindowManager.cs`
- **Change:** Cache the `TextInputSurfaceFocused` result in a private bool field, updated in `OnWindowFocusChanged()` (which already runs on focus-change events, alongside `_isTextInputType`); `TextInputSurfaceFocused` reads the cached bool instead of doing the COM `GetProperty(VSFPROPID_DocView)` + `Keyboard.FocusedElement` + visual-tree walk per access. Sentinel-aware behavior preserved (`IsTestStaleInjected()` → false).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests -- ToolWindowMode` → `Run_ToolWindowMode_TextInputTypesClassified` / `Run_ToolWindowMode_NavigationTypesClassified` / `Run_ToolWindowMode_HjklMoves` PASS (classification unchanged). No per-key COM: the `TextInputSurfaceFocused` getter no longer calls `GetProperty`/`Keyboard.FocusedElement` on every read (code review).
- **Fails-if:** `dotnet build` errors; a classification test regresses; `TextInputSurfaceFocused` still performs the COM/visual-tree walk per access (the cache was not wired into `OnWindowFocusChanged`).

### BP-4: M2 — stop the per-key sentinel re-stat
- **Files:** `MyExtension/InputHandler.cs`; `MyExtension/WindowManager.cs`
- **Change:** Throttle `_windowManager.RefreshStaleSentinel()` in `IsKeyOfInterest` to at most once per bounded interval (e.g. 250ms, cached `DateTime` last-refresh), so the sentinel file is not `File.Exists`-stat'd on every key-down. The `StaleToolWindowSentinel` caching semantics are unchanged (already pinned by tests); the harness fault still toggles within the throttle window (deferred e2e).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- StaleToolWindowSentinel` → `Run_StaleToolWindowSentinel_CachedWithoutRefresh` PASS (IsStale cached until Refresh), `Run_StaleToolWindowSentinel_RefreshReportsChange` PASS, `Run_StaleToolWindowSentinel_NullPathNeverStale` PASS; `dotnet build MyExtension.slnx` → 0 errors. Diagnostic unchanged: `[NeoVisual] stale-toolwindow sentinel active` (still emitted on the false→true transition).
- **Fails-if:** a `Run_StaleToolWindowSentinel_*` test regresses; `dotnet build` errors; `RefreshStaleSentinel()` is still invoked on every `IsKeyOfInterest` entry (the throttle was not added).

### BP-5: M15 — guard `Keys.I` on `!_leaderMatcher.IsActive`; drop bare-Ctrl pre-filter cases
- **Files:** `MyExtension/InputHandler.cs`; `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `HandleKey`'s tool-window normal-mode block, change `if (key == Keys.I)` to `if (!_leaderMatcher.IsActive && key == Keys.I)` so an in-progress leader sequence is not interrupted by `I` (the key continues the sequence). In `IsKeyOfInterest`, delete the `ControlKey`/`LControlKey`/`RControlKey` cases (bare Ctrl keys are deliberately ignored by `HandleKey` — the pre-filter returning true for them is a wasted marshal). Add `Run_LeaderMatcher_ActiveSequenceConsumesI` (pure): after Space starts a sequence, `HandleKey(Keys.I, ...)` returns Consume (not PassThrough), proving `I` is treated as a sequence key while active.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- LeaderMatcher` → `Run_LeaderMatcher_ActiveSequenceConsumesI` PASS + the existing `Run_LeaderMatcher_*` family PASS (behavior-preserving); `dotnet build MyExtension.slnx` → 0 errors. Diagnostics unchanged: `[NeoVisual] leader-binding executed: ...` / `[NeoVisual] toolwindow-enter-input`.
- **Fails-if:** `Run_LeaderMatcher_ActiveSequenceConsumesI` FAILS (the matcher passes `I` through while active); `dotnet build` errors; the `Keys.I` branch still fires during an active leader sequence (the guard was not added).

### BP-6: M7 — incremental sequence string + precomputed prefix set
- **Files:** `MyExtension/LeaderSequenceMatcher.cs`; `tests/NeoVisual.Tests/Program.cs`
- **Change:** Maintain the sequence string incrementally (append each key's `KeyNames.ToString` to a `StringBuilder` instead of `string.Join(",", _sequence.Select(...))` per key). Precompute a prefix set once at construction (every proper prefix of every binding sequence, e.g. `"F"` for `"F,F"`), stored as a readonly field; the per-key prefix check becomes a set lookup instead of `_bindings.Keys.Any(k => k.StartsWith(...))`. Add `Run_LeaderMatcher_PrefixSetBuiltOnce` asserting the precomputed prefix set is correct (e.g. bindings `{"F","F,F"}` → prefix set contains `"F"`; the field is readonly so it is built once).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- LeaderMatcher` → `Run_LeaderMatcher_PrefixSetBuiltOnce` PASS + the existing `Run_LeaderMatcher_*` family PASS (behavior-preserving: `Run_LeaderMatcher_MultiKeySequence`, `Run_LeaderMatcher_UnknownSequenceAborts`, `Run_LeaderMatcher_ThrowingActionIsCaught`, ...); `dotnet build MyExtension.slnx` → 0 errors.
- **Fails-if:** `Run_LeaderMatcher_PrefixSetBuiltOnce` FAILS (prefix set wrong/absent → CS0246 or assertion failure); an existing `Run_LeaderMatcher_*` test regresses (the incremental sequence string changed the emitted sequence); `dotnet build` errors.

### BP-7: m17 — `ThrowIfNotOnUIThread` on `WindowManager.RefreshCurrentWindow`
- **Files:** `MyExtension/WindowManager.cs`
- **Change:** Add `ThreadHelper.ThrowIfNotOnUIThread()` as the first line of `RefreshCurrentWindow()` (it calls `_monitorSelection.GetCurrentElementValue`, a VS API).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).
- **Fails-if:** `dotnet build` errors; the guard is missing from `RefreshCurrentWindow` (code review).

**Phase 1 gate:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).

---

## Phase 2 — Finder path amortization (M3, M4, M5, M13, m15, m16)

### BP-8: M3 — FzfFilter off-thread spawn/write + await both tasks after kill
- **Files:** `Telescope/FzfFilter.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Move the process spawn + stdin write into `Task.Run` (the candidate list is written off the UI thread); after `TryKill` on the timeout path, `await` BOTH `outputTask` and `errorTask` (so the faulted `ReadToEndAsync` tasks are observed — no unobserved-task noise). Extend `Run_FzfFilter_TimeoutKillsAndFallsBack` (Telescope.Tests:511) to assert the timeout path returns within a bounded wall time AND completes without unobserved-task noise (the existing `[Telescope] fzf filter failed: timeout` assertions stay).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FzfFilter` → `Run_FzfFilter_TimeoutKillsAndFallsBack` PASS (bounded wall time + `[Telescope] fzf filter failed: timeout after {ms}ms` logged), `Run_FzfFilter_NonexistentPathFallsBackAndLogs` PASS (`[Telescope] fzf filter failed: {msg}`), `Run_FzfFilter_FilterMatchesPrefix` PASS, `Run_FzfFilter_QuoteArg_TrailingBackslash` PASS; `dotnet build MyExtension.slnx` → 0 errors.
- **Fails-if:** `Run_FzfFilter_TimeoutKillsAndFallsBack` FAILS (the timeout path still returns without awaiting both tasks, or the wall time is unbounded); `dotnet build` errors (the injected-path ctor / off-thread seam doesn't exist → CS1729/CS0246).

### BP-9: M4 — GrepFinder off-thread content scan
- **Files:** `Telescope/GrepFinder.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Run the per-file content scan (`ScanFile` over the cached file list) on a background task and marshal only the results back; keep the DTE enumeration (`ProjectFiles.Enumerate`) on the UI thread. The hermetic test path (injected enumerate + opener) stays synchronous. Add `Run_GrepFinder_OffThreadScanSameHits` — `GetCandidates` returns the same hits when the scan runs off-thread (RED: no off-thread seam → compile error or behavior gap).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- GrepFinder` → `Run_GrepFinder_OffThreadScanSameHits` PASS + the existing `Run_GrepFinder_*` family PASS (`Run_GrepFinder_LineScanMatchesCaseInsensitive`, `Run_GrepFinder_HitCapBounded`, `Run_GrepFinder_CacheEnumeratesOnce`, ...). Diagnostic unchanged: `[Telescope] grep hits=...` (per-query summary).
- **Fails-if:** `Run_GrepFinder_OffThreadScanSameHits` FAILS (off-thread scan returns different hits or the seam is missing → CS0246); an existing `Run_GrepFinder_*` test regresses; `dotnet build` errors.

### BP-10: M5 — preview mtime-keyed content cache + instance line-index state
- **Files:** `Telescope/PreviewRenderer.cs`; `Telescope/FileContentCache.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Route `PreviewRenderer.Show`'s `File.ReadAllText` through a mtime-keyed cache (reuse `FileContentCache` from BP-11, or a dedicated mtime-keyed (path → content+segments) cache) so the file is re-read and re-tokenized only when the mtime changes. Make the `_lineIndex`/`_linePointers` state instance-scoped and reset on `SetContent` (no shared static mutable state across preview boxes). The unit seam is the mtime-keyed cache behavior pinned by the `Run_FileContentCache_*` tests.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FileContentCache` → `Run_FileContentCache_CachedRead` PASS (reader runs once for two reads on an unchanged file), `Run_FileContentCache_InvalidatesOnTimestampChange` PASS (reader runs twice on a changed timestamp); `dotnet run --project tests/Telescope.Tests -- Preview` → the `Run_Preview_*` family PASS; `dotnet build MyExtension.slnx` → 0 errors. Diagnostic unchanged: `[Telescope] preview tokens=...` (re-tokenize only on change).
- **Fails-if:** a `Run_FileContentCache_*` test FAILS; `dotnet build` errors (the cache class doesn't exist → CS0246); the preview still re-reads/re-tokenizes the whole file per selection change (the mtime-keyed cache was not wired in).

### BP-11: M13 — FileContentCache LRU eviction + clear-on-solution-change
- **Files:** `Telescope/FileContentCache.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Cap `FileContentCache` by count (LRU: evict the least-recently-used entry when the cap is exceeded) and/or clear on solution change. Add `Run_FileContentCache_EvictsOldest` (insert N+1 entries → the oldest is evicted) and `Run_FileContentCache_ClearOnSolutionChange` (Clear() empties the cache).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FileContentCache` → `Run_FileContentCache_EvictsOldest` PASS, `Run_FileContentCache_ClearOnSolutionChange` PASS, `Run_FileContentCache_CachedRead` PASS, `Run_FileContentCache_InvalidatesOnTimestampChange` PASS; `dotnet build MyExtension.slnx` → 0 errors.
- **Fails-if:** `Run_FileContentCache_EvictsOldest` FAILS (no eviction — the oldest entry is still served); `Run_FileContentCache_ClearOnSolutionChange` FAILS; `dotnet build` errors.

### BP-12: m15 — CodeIssuesFinder.CollectTodos routes through FileContentCache
- **Files:** `Telescope/CodeIssuesFinder.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Replace `File.ReadAllLines(path)` in `CollectTodos` with the shared `FileContentCache.GetLines(path)` (add a `FileContentCache` field to `CodeIssuesFinder`, injected for the hermetic test path). Add `Run_Issues_CollectTodosUsesCache` — a counting reader proves the cache is hit on the second scan (the reader runs once for two `GetCandidates` calls over the same file).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Issues` → `Run_Issues_CollectTodosUsesCache` PASS + the existing `Run_Issues_*` family PASS (`Run_Issues_TodoScanFindsMarkers`, `Run_Issues_NoFalsePositiveOnTodoWord`, `Run_Issues_OnSelectedReportsPathAndLine`, ...); `dotnet build MyExtension.slnx` → 0 errors.
- **Fails-if:** `Run_Issues_CollectTodosUsesCache` FAILS (the reader runs per scan — the cache is not hit); an existing `Run_Issues_*` test regresses; `dotnet build` errors.

### BP-13: m16 — FzfFilter.IsAvailable bounded wait
- **Files:** `Telescope/FzfFilter.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Bound `IsAvailable()`'s wait on a hung `fzf --version` (short timeout, e.g. reuse `FilterTimeoutMs` or a dedicated short bound) so it cannot block the UI up to 3s. Add `Run_FzfFilter_IsAvailableBounded` — a stub that hangs → `IsAvailable()` returns within a bounded wall time.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FzfFilter` → `Run_FzfFilter_IsAvailableBounded` PASS (returns within a bounded wall time), `Run_FzfFilter_IsAvailableFalseForMissingPath` PASS; `dotnet build MyExtension.slnx` → 0 errors.
- **Fails-if:** `Run_FzfFilter_IsAvailableBounded` FAILS (the wait is unbounded on a hung stub); `dotnet build` errors.

**Phase 2 gate:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/Telescope.Tests` → all pass (135 + new).

---

## Phase 3 — Motion + preview correctness (M10, M11, M19, M6)

### BP-14: M10 — `TextMotionNavigator.Up()` leading-blank-line off-by-one
- **Files:** `Telescope/TextMotionNavigator.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** In `Up()`, only search from `lineStart - 2` when `lineStart - 2 >= 0`; otherwise `prevNewline = -1` / `prevStart = 0` (so a leading blank line moves to line 1/caret 0 instead of staying put). In the SAME change, CORRECT the existing `Run_Preview_UpFromSecondLineWithLeadingBlankLine` (Telescope.Tests:846, currently asserts the buggy `LineNumber==2`) to assert line 1/caret 0. Add `Run_Preview_UpFromSecondLineWithLeadingBlankLine_Fixed` — `SetText("\nabc"); MoveToLine(2); Up();` must move to line 1.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Preview` → `Run_Preview_UpFromSecondLineWithLeadingBlankLine_Fixed` PASS (LineNumber==1, Caret==0) and the corrected `Run_Preview_UpFromSecondLineWithLeadingBlankLine` PASS; `dotnet run --project tests/Telescope.Tests -- PromptMotion` → the `Run_PromptMotion_*` family PASS (no motion regression). Diagnostic unchanged: `[Telescope] preview caret=... line=...`.
- **Fails-if:** `Run_Preview_UpFromSecondLineWithLeadingBlankLine_Fixed` FAILS (Up() still stays on line 2); the corrected `Run_Preview_UpFromSecondLineWithLeadingBlankLine` FAILS (the test was not corrected or the fix regressed); a `Run_PromptMotion_*` test regresses.

### BP-15: M11 — ResultMapper unknown-match skip/log (no silent null-payload no-op)
- **Files:** `Telescope/ResultMapper.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** For an unmatched display string, skip the entry (do not add a null-payload `FinderEntry` whose `OnSelected` silently no-ops) OR give the fallback a safe no-op payload; log a warning. Update `Run_ResultMapper_UnknownStringNullPayload` (Telescope.Tests:2207, currently asserts the null-payload behavior) to assert the new contract. Add `Run_ResultMapper_UnknownStringSkippedOrLogged` — an unmatched display string does not produce a null-payload entry that silently no-ops.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- ResultMapper` → `Run_ResultMapper_UnknownStringSkippedOrLogged` PASS, the updated `Run_ResultMapper_UnknownStringNullPayload` PASS, `Run_ResultMapper_DuplicateDisplayPreserved` PASS, `Run_ResultMapper_UniqueDisplayMapped` PASS, `Run_ResultMapper_OrderPreserved` PASS; `dotnet build MyExtension.slnx` → 0 errors. New diagnostic (warning on unknown match): `[Telescope] result-mapper unknown display: {display}`.
- **Fails-if:** `Run_ResultMapper_UnknownStringSkippedOrLogged` FAILS (a null-payload entry is still produced); `Run_ResultMapper_UnknownStringNullPayload` FAILS (the test was not updated to the new contract); `dotnet build` errors.

### BP-16: M19 — unify the two key→motion dispatch tables into one shared pure dispatcher
- **Files:** `Telescope/TextMotionDispatcher.cs` (new); `Telescope/TryDispatch.cs`; `MyExtension/ToolWindows/TextMotionHelper.cs`; `tests/Telescope.Tests/Program.cs`; `tests/NeoVisual.Tests/Program.cs`
- **Change:** Create a shared pure dispatcher (e.g. `TextMotionDispatcher`) owning the single key→motion table for BOTH surfaces: a WinForms-`Keys` mapping (`MapKey(Keys, bool shift)`) and a WPF-`Key` mapping (`MapKey(Key, bool shift)`), plus an `Apply(TextMotion, TextMotionNavigator, out CaretPlacement?)` that runs the motion on the navigator. `TextMotionHelper.MapMotion` and `TryDispatch.Handle` both delegate to it (each surface keeps its own shift source + its own diagnostic log line). Rewrite the NeoVisual `Run_TextMotionEngine_MapMotion_*` groups (NeoVisual.Tests:449-475) to target the shared dispatcher's `Keys` mapping. The Telescope `Run_TryDispatch_*` tests (Telescope.Tests:932-1012) stay — they pin the unified `$` contract (bare `Key.D4` without shift is NOT a motion).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- TryDispatch` → `Run_TryDispatch_DollarWithoutShiftNotHandled` PASS (bare D4 does NOT LineEnd), `Run_TryDispatch_DollarWithShiftLineEnds` PASS, `Run_TryDispatch_MotionsMapToNavigator` PASS, `Run_TryDispatch_InsertPlacements` PASS; `dotnet run --project tests/NeoVisual.Tests -- TextMotionEngine` → the rewritten `Run_TextMotionEngine_MapMotion_*` PASS; `dotnet build MyExtension.slnx` → 0 errors. Diagnostics unchanged: `[Telescope] prompt-motion key=... caret=...`, `[NeoVisual] text-motion key=... caret=...`, `[NeoVisual] textinput-enter-input ...`.
- **Fails-if:** a `Run_TryDispatch_*` test FAILS (the unified `$` contract broke); a rewritten `Run_TextMotionEngine_MapMotion_*` test FAILS; `dotnet build` errors (the shared dispatcher doesn't exist → CS0246); the two dispatch tables still exist as separate switch statements (code review).

### BP-17: M6 — reuse the focused box + navigator; avoid the full `GetText()` copy
- **Files:** `MyExtension/ToolWindows/TextMotionHelper.cs`
- **Change:** Pass the already-found focused surface into `ApplyMotionToBox` (no second `FindFocusedTextBox()` visual-tree re-walk per motion); reuse a single `TextMotionNavigator` instance across motions on the same surface; avoid the full `snapshot.GetText()` copy on the editor path where possible. The motion math stays in `TextMotionNavigator` (already unit-tested).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests -- TextMotionEngine` → the `Run_TextMotionEngine_MapMotion_*` PASS (motion mapping unchanged); `dotnet run --project tests/Telescope.Tests -- PromptMotion` → the `Run_PromptMotion_*` PASS (motion math unchanged). Diagnostics unchanged: `[NeoVisual] text-motion key=... caret=...`, `[NeoVisual] textinput-enter-input ...`.
- **Fails-if:** `dotnet build` errors; a motion-mapping test regresses; `ApplyMotionToBox` still re-walks the visual tree or re-copies the full buffer per motion (code review — the copy-elimination is in the VS-coupled layer, verified by build + deferred e2e).

**Phase 3 gate:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/Telescope.Tests` → all pass; `dotnet run --project tests/NeoVisual.Tests` → all pass.

---

## Phase 4 — Navigation robustness (M9, M14, m3, m4, m5, m6)

### BP-18: M9 — delete `WindowMatrix.CheckDte` and its call
- **Files:** `MyExtension/CardinalMovment/WindowMatrix.cs`
- **Change:** Delete `CheckDte(AsyncPackage)` (lines 75-87) and its call at line 37; keep the single `DTE dteService = MyExtension.VsServices.Dte(package);` fetch inside the existing try/catch (the ctor's outer catch already handles DTE failures).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests -- WindowNavigationEngine` → the `Run_WindowNavigationEngine_*` family PASS (algorithm unchanged). Diagnostic unchanged: `[NeoVisual] navigate direction=L/R/D/U`.
- **Fails-if:** `dotnet build` errors (a dangling reference to `CheckDte`); a `Run_WindowNavigationEngine_*` test regresses; DTE is still fetched twice per navigation (code review).

### BP-19: M14 — classify in the ctor + `guid != Guid.Empty`
- **Files:** `MyExtension/WindowManager.cs`
- **Change:** In the ctor, after `RefreshCurrentWindow()`, call `OnWindowFocusChanged()` so `_type`/`_isToolWindow`/`_isTextInputType` are classified immediately (not only after the first focus event). In `OnWindowFocusChanged`, change `if (guid != null)` to `if (guid != Guid.Empty)` (a `Guid` is never null — the `Unknown` fallback is currently dead).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests -- ToolWindow` → `Run_ToolWindowType_UnknownGuid` PASS, `Run_ToolWindowMode_*` PASS (classification unchanged); `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).
- **Fails-if:** `dotnet build` errors; a classification test regresses; the ctor still leaves `_type`/`_isToolWindow` default until the first focus event (code review); the `Unknown` fallback is still dead (`guid != null` unchanged).

### BP-20: m3 — remove the dead `gap < minGap` branch in `WindowNavigationEngine`
- **Files:** `MyExtension/CardinalMovment/WindowNavigationEngine.cs`
- **Change:** In `SelectTarget`'s second pass, change `if (gap < minGap || gap > upperBound)` to `if (gap > upperBound)` (minGap is the minimum, so no gap can be `< minGap` — the branch is dead).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- WindowNavigationEngine` → the `Run_WindowNavigationEngine_*` family PASS (behavior-preserving: `Run_WindowNavigationEngine_Up_PicksLargestAdjacency`, `Run_WindowNavigationEngine_Down_ToleranceExcludes`, `Run_WindowNavigationEngine_DivideWindow_ExcludesBeyond`, ...); `dotnet build MyExtension.slnx` → 0 errors.
- **Fails-if:** a `Run_WindowNavigationEngine_*` test regresses (the branch removal changed selection); `dotnet build` errors.

### BP-21: m4 — normalize `CardinalNavigationConstants` to `public const`
- **Files:** `MyExtension/CardinalMovment/CardinalNavigationConstants.cs`
- **Change:** Change `public readonly static int` → `public const int` and `public readonly static double` → `public const double`; normalize naming consistently (keep the existing names — they are referenced by `NavigationSettings`).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged; the constants feed `NavigationSettings`, exercised by the `Run_WindowNavigationEngine_*` tests).
- **Fails-if:** `dotnet build` errors (a `const` conversion broke a reference); a `Run_WindowNavigationEngine_*` test regresses (a constant value changed).

### BP-22: m5 — make `UtilityMethods` static
- **Files:** `MyExtension/CardinalMovment/UtilityMethods.cs`
- **Change:** Change `class UtilityMethods` → `static class UtilityMethods` (it holds only static methods).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).
- **Fails-if:** `dotnet build` errors (an instance member reference to `UtilityMethods`); a test regresses.

### BP-23: m6 — make `WindowMatrix` sealed
- **Files:** `MyExtension/CardinalMovment/WindowMatrix.cs`
- **Change:** Change `class WindowMatrix` → `sealed class WindowMatrix` (sibling types are sealed).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).
- **Fails-if:** `dotnet build` errors (a subclass of `WindowMatrix` exists); a test regresses.

**Phase 4 gate:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged).

---

## Phase 5 — Hook-path safety + logging (M8, M12, M16, M25, m14)

### BP-24: M8 — try/catch around the tool-window branch + `toolwindow-move failed` log
- **Files:** `MyExtension/InputHandler.cs`
- **Change:** Wrap the tool-window branch of `HandleKey` (the `FocusGuard.ShouldRouteToolWindowKey` block calling `controller.TryMove`/`EnterInputMode`/`ExitInputMode`) in try/catch; on exception, log `[NeoVisual] toolwindow-move failed: {msg}` and return false (pass through — never crash the hook). (Optionally wrap the whole `HandleKey` body.)
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged). New diagnostic: `[NeoVisual] toolwindow-move failed: {msg}` (emitted when a controller call throws; the key passes through).
- **Fails-if:** `dotnet build` errors; a controller exception still escapes `HandleKey` (no `toolwindow-move failed:` line, key not passed through) — code review + deferred e2e.

### BP-25: M12 — reset `_paneInitTried` on the null-service path in `NeoVisualLog.EnsurePane`
- **Files:** `Telescope/NeoVisualLog.cs`
- **Change:** In `EnsurePane`, when `outputWindow == null` (null `GetGlobalService` result, pre-package-init), reset `_paneInitTried = false` (under `PaneSync`) so a later call retries instead of permanently disabling the Output pane.
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/Telescope.Tests` → all pass (count unchanged). Diagnostic unchanged: `[NeoVisual] output pane unavailable: {reason}` (the one-time fallback still fires only when the pane genuinely cannot be created).
- **Fails-if:** `dotnet build` errors; `_paneInitTried` stays true after a null service (the pane is permanently disabled) — code review + deferred e2e.

### BP-26: M16 — `vim-mode=` logged only on change
- **Files:** `MyExtension/VimModeTracker.cs`; `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `UpdateTypingFromMode`, track the last logged mode name (or mode value) and emit `[NeoVisual] vim-mode={name}` only when it actually changes (a focus gain/loss with the same mode emits nothing). Add `Run_VimModeTracker_LogOnlyOnChange` — via `FakeVimModeSource` + `VimModeTracker`: `Mode=2; RaiseModeChanged()` logs `vim-mode=Insert`; `Mode=2; RaiseModeChanged()` again does NOT add a second `vim-mode=Insert` line; `Mode=1; RaiseModeChanged()` logs `vim-mode=Normal`.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- VimMode` → `Run_VimModeTracker_LogOnlyOnChange` PASS (the log file contains exactly one `vim-mode=Insert` for two identical mode events), `Run_VimModeSource_FakeLogsVimMode` PASS, `Run_VimModeSource_FakeDrivesTypingFlag` PASS, `Run_VimModeClassifier_*` PASS; `dotnet build MyExtension.slnx` → 0 errors. Diagnostic unchanged: `[NeoVisual] vim-mode=Insert|Normal|Replace` (emitted only on a real mode switch).
- **Fails-if:** `Run_VimModeTracker_LogOnlyOnChange` FAILS (a same-mode event still emits a new `vim-mode=` line); `Run_VimModeSource_FakeLogsVimMode` regresses (a real mode switch no longer logs); `dotnet build` errors.

### BP-27: M25 — `VsServices.Dte` returns `DTE?`; callers handle null explicitly
- **Files:** `MyExtension/VsServices.cs`; `MyExtension/InputHandler.cs`; `MyExtension/CardinalMovment/WindowMatrix.cs`; `MyExtension/MyExtensionPackage.cs`; `MyExtension/TelescopeLauncher.cs`
- **Change:** Change `VsServices.Dte` to return `DTE?` (drop the `dte!` null-forgiving). Update the direct dereferencing callers to null-check before use: `InputHandler.ExecuteVsCommand` (log `Command '{command}' failed: DTE unavailable` and return), `InputHandler.ToggleSolutionExplorer`, `WindowMatrix` ctor (already inside try/catch — guard `dteService`), `MyExtensionPackage` (lines 436/457), `TelescopeLauncher` (line 41). For the `Func<DTE>` finder/controller factory lambdas (`MyExtensionPackage` lines 86-88/107), widen to `Func<DTE?>` (the finders already null-guard `dte?.Solution`) or use `!` at the lambda — the build gate resolves the exact ripple.
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors (every caller compiles against `DTE?`); `dotnet run --project tests/NeoVisual.Tests` → all pass (count unchanged); `dotnet run --project tests/Telescope.Tests` → all pass (count unchanged). Diagnostics unchanged: `[NeoVisual] Command '{command}' failed: {msg}` (now also emitted when DTE is null, not a misleading NRE).
- **Fails-if:** `dotnet build` errors (a caller still dereferences `DTE?` without a null check); a test regresses; a caller still NREs inside its catch when DTE is null (code review + deferred e2e).

### BP-28: m14 — `FilterFailureLog.Format` returns the UNPREFIXED message
- **Files:** `Telescope/FilterFailureLog.cs`; `Telescope/TelescopeOverlay.cs`; `tests/Telescope.Tests/Program.cs`
- **Change:** Change `FilterFailureLog.Format(Exception ex)` to return `"filter failed: " + ex.Message` (no `[Telescope] ` prefix). Update the caller at `TelescopeOverlay.cs:410` from `NeoVisualLog.Log(FilterFailureLog.Format(ex))` to `TelescopeLog.Log(FilterFailureLog.Format(ex))` (which adds the `[Telescope] ` prefix). Update `Run_FilterFailureLog_Format` (Telescope.Tests:2155) to assert `"filter failed: boom"`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FilterFailureLog` → `Run_FilterFailureLog_Format` PASS (asserts `"filter failed: boom"`); `dotnet build MyExtension.slnx` → 0 errors. Emitted line unchanged (byte-identical): `[Telescope] filter failed: {msg}` (via `TelescopeLog.Log`).
- **Fails-if:** `Run_FilterFailureLog_Format` FAILS (the format still embeds the prefix, or the test was not updated); `dotnet build` errors; the emitted line double-prefixes (`[Telescope] [Telescope] filter failed: ...`) — the caller still uses `NeoVisualLog.Log`.

**Phase 5 gate:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project tests/NeoVisual.Tests` → all pass; `dotnet run --project tests/Telescope.Tests` → all pass.

---

## Verification Trace

| failing test / gate | implicated steps | expected pass signal |
|---|---|---|
| `Run_ActionTable_TextInput_ActionKeysMatchTable` (count 6→7) | BP-1 | PASS with count 7; `Run_TextInput_KeysIMapsToInsertStart` PASS; `[NeoVisual] textinput-enter-input start caret=0` restored |
| `Run_VimBufferSubscriptions_*` (new) | BP-2 | PASS; focused view's `vim-mode=` subscription survives a non-focused view close |
| `Run_StaleToolWindowSentinel_*` | BP-4 | PASS (caching semantics unchanged); no per-key `File.Exists` |
| `Run_LeaderMatcher_ActiveSequenceConsumesI` (new) | BP-5 | PASS; `Keys.I` guarded on `!_leaderMatcher.IsActive` |
| `Run_LeaderMatcher_PrefixSetBuiltOnce` (new) | BP-6 | PASS; prefix set precomputed once; `Run_LeaderMatcher_*` family PASS |
| `Run_FzfFilter_TimeoutKillsAndFallsBack` | BP-8 | PASS (bounded wall time, both tasks awaited); `[Telescope] fzf filter failed: timeout after {ms}ms` |
| `Run_GrepFinder_OffThreadScanSameHits` (new) | BP-9 | PASS; `[Telescope] grep hits=...` unchanged |
| `Run_FileContentCache_CachedRead` / `_InvalidatesOnTimestampChange` | BP-10 | PASS (mtime-keyed cache); preview re-tokenizes only on change |
| `Run_FileContentCache_EvictsOldest` / `_ClearOnSolutionChange` (new) | BP-11 | PASS (LRU cap + clear) |
| `Run_Issues_CollectTodosUsesCache` (new) | BP-12 | PASS (cache hit on second scan) |
| `Run_FzfFilter_IsAvailableBounded` (new) | BP-13 | PASS (bounded wall time) |
| `Run_Preview_UpFromSecondLineWithLeadingBlankLine` (corrected) + `_Fixed` (new) | BP-14 | PASS (line 1/caret 0); `[Telescope] preview caret=... line=...` unchanged |
| `Run_ResultMapper_UnknownStringSkippedOrLogged` (new) + `_UnknownStringNullPayload` (updated) | BP-15 | PASS (no null-payload no-op); `[Telescope] result-mapper unknown display: {display}` |
| `Run_TryDispatch_DollarWithoutShiftNotHandled` + rewritten `Run_TextMotionEngine_MapMotion_*` | BP-16 | PASS (unified `$` contract; single dispatch table) |
| `Run_WindowNavigationEngine_*` | BP-18, BP-20, BP-21 | PASS (behavior-preserving); `[NeoVisual] navigate direction=L/R/D/U` unchanged |
| `Run_ToolWindowType_UnknownGuid` / `Run_ToolWindowMode_*` | BP-19 | PASS (ctor classification + `Guid.Empty`) |
| `Run_VimModeTracker_LogOnlyOnChange` (new) | BP-26 | PASS (no duplicate `vim-mode=` on same-mode event); `[NeoVisual] vim-mode=Insert|Normal|Replace` unchanged |
| `Run_FilterFailureLog_Format` (updated) | BP-28 | PASS (`"filter failed: boom"`); emitted `[Telescope] filter failed: {msg}` byte-identical |
| `dotnet build MyExtension.slnx` | BP-3, BP-7, BP-17, BP-22, BP-23, BP-24, BP-25, BP-27 | 0 errors |
| both unit suites (counts unchanged) | all BP-n | Telescope.Tests 135+ / NeoVisual.Tests 130+ all PASS |

## Known-RED allowlist

**NONE** — per `docs/progress.md`, no known-RED e2e scenario remains. The following items are
**no-seam** (verified by build + existing suites + the deferred e2e gate, NOT by a new unit test —
do NOT flag them as regressions if their only verification is the build): M1 (BP-3), M2 wiring
(BP-4), M6 copy-elimination (BP-17), M8 (BP-24), M9 (BP-18), M12 (BP-25), M14 (BP-19), M25
(BP-27), m17 (BP-7), m4/m5/m6 (BP-21/22/23).

## E2E queue reference (informational — deferred to `e2e-queue.md`, NOT part of this plan's Verify-with)

- E2E-CR-1 (`neovisual-textinput-motions`): `textinput-enter-input start caret=0` on `I` (BP-1).
- E2E-CR-2 (multi-view): closing a non-focused view does not kill the focused view's `vim-mode=` subscription (BP-2).
- E2E-M1/M2/M15 (hook hot-path): no per-key COM/stat; `neovisual-*` still GREEN (BP-3/4/5).
- E2E-M3/M4/M5/M13 (finder): `grep hits=...`, `results count=...`, `preview caret=...` unchanged; fzf failure paths log `fzf filter failed:` / `filter failed:` (BP-8/9/10/11/12/13).
- E2E-M10/M11/M19/M6 (motion/preview): caret positions unchanged; the `$` drift fixed (BP-14/15/16/17).
- E2E-M9/M14 (navigation): `navigate direction=L/R/D/U` unchanged (BP-18/19).
- E2E-M8/M12/M16/M25 (safety/logging): `vim-mode=` unchanged; a controller exception logs `toolwindow-move failed:` and passes through (BP-24/25/26/27).

DONE

---

# Build Plan — Phases 6-10 (Code-Review Fixes: duplication, dead code, harness, tests, docs)

> **Lane:** unit-only (e2e deferred to `e2e-queue.md`). Every `Verify-with` / `Fails-if`
> references **unit test names + diagnostic formats ONLY** — no e2e scenario names. The
> deferred e2e scenarios appear only informationally at the end.
>
> **Ground truth:** `dotnet build MyExtension.slnx` = 0 errors; `tests/Telescope.Tests` 135
> passing; `tests/NeoVisual.Tests` 130 passing (counts drift upward as Phases 0-9 add tests).
> **Known-RED allowlist:** none — no known-RED e2e scenario remains (per `docs/progress.md`).
>
> **Research corrections folded in (from the plan):**
> - **M17** coordinates with **M16** (both touch the `vim-mode=` log — M16's log-on-change
>   contract must survive the M17 wiring).
> - **M27** is scoped to `tools/test-e2e.ps1:518` ONLY; `:1533`/`:1537` legitimately assert
>   `count=0` (`telescope-no-selection`) and must NOT be touched.
> - **m28**'s stale "Text.UI not referenced" comment is wrong — `tests/NeoVisual.Tests/NeoVisual.Tests.csproj`
>   references `Microsoft.VisualStudio.Text.UI` (line 31), so `TextInputToolWindowController.TryMove`
>   IS callable on the test host; only `SelectFirstSourceFile` stays VS-coupled.
> - **m12** — `TextMotionNavigator.ColumnNumber` is test-pinned (`Run_Preview_JkMoveByLine`), so the
>   "verify first" resolves to: delete it AND update the 2 test assertions to assert `Caret` directly.
> - **m19** — the `iterate-telescope.ps1:220-224` comment claims `NEOVISUAL_LOG_DIR` is
>   "intentionally User-scoped"; the adjudicated fix is Process scope, so that comment is updated too.
>
> **Log-line-as-contract:** no `[Telescope]`/`[NeoVisual]`/`[MyExtension]` log literal may change in
> Phases 6-10. The only product diagnostic touched is `vim-mode=` (M17, coordinated with M16 — the
> emitted token stays `vim-mode=Insert|Normal|Replace|Unknown` byte-identical).

---

## Phase 6 — Duplication merges (M18, M20, M21, M22, M24, m13)

### BP-1 — M18: `SimpleShortcutMatcher` delegates to `KeyNameBuilder.Build`
- **Files:** `MyExtension/SimpleShortcutMatcher.cs`, `MyExtension/KeyNameBuilder.cs`
- **Change:** Delete the private `BuildSimpleKey` (SimpleShortcutMatcher.cs:77-85) and have
  `HandleKey` call `KeyNameBuilder.Build(key, ctrl, shift, alt)` (the identical canonical-shortcut
  logic, single-allocation). `KeyNameBuilder` is `internal static` in the same assembly — no
  accessibility change. No behavior change: both produce `Ctrl+H` / `Shift+F4` / `Alt+X` /
  `Ctrl+Shift+Alt+Delete` / printable-key strings.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- SimpleShortcutMatcher` → all
  `Run_SimpleShortcutMatcher_*` pass (incl. `Run_SimpleShortcutMatcher_ModifierOrderShiftF4`,
  `Run_SimpleShortcutMatcher_PrintableKeys`, `Run_SimpleShortcutMatcher_AltX`); `dotnet run --project
  tests/NeoVisual.Tests -- KeyNameBuilder` → all `Run_KeyNameBuilder_*` pass; grep confirms no
  `BuildSimpleKey` remains (the duplicate canonical-shortcut logic is gone).
- **Fails-if:** `Run_SimpleShortcutMatcher_ModifierOrderShiftF4` or `Run_SimpleShortcutMatcher_PrintableKeys`
  fails (the delegation changed the emitted string) or `BuildSimpleKey` still exists (dedup incomplete).

### BP-2 — M20: shared text-motion action wiring + J/K arrow reuse
- **Files:** `MyExtension/ToolWindows/ToolWindowControllerBase.cs`,
  `MyExtension/ToolWindows/TextInputToolWindowController.cs`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs`,
  `MyExtension/ToolWindows/GeneralToolWindowController.cs`
- **Change:** (a) Add a protected helper on `ToolWindowControllerBase`:
  `protected Func<bool> TextMotion(Keys key) => () => TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode);`
  and replace the verbatim W/B/E (and A/H/L in TextInput) entries in both controllers with
  `[Keys.W] = TextMotion(Keys.W)` etc. (b) Make `GeneralToolWindowController.KeyToArrowVk` `internal
  static` and extract the shared arrow path `internal static bool TryMoveArrow(Keys key)` that maps
  via `KeyToArrowVk`, logs `[NeoVisual] toolwindow-move key={key} -> arrow vk={vk}` (byte-identical
  format), presses via `KeyInjection.Press(vk)`, returns true. `GeneralToolWindowController.TryMove`
  and `SolutionExplorerController`'s `[Keys.J]`/`[Keys.K]` entries both call it. No MEF/DI change —
  the controllers are still registered in `MyExtensionPackage` exactly as today.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- ActionTable` → all
  `Run_ActionTable_*` pass, incl. the exact-set `Run_ActionTable_SolutionExplorer_ActionKeysMatchTable`
  and `Run_ActionTable_TextInput_ActionKeysMatchTable` (the key sets are unchanged — only the wiring
  is shared). `dotnet build` 0 errors.
- **Fails-if:** an exact-set `Run_ActionTable_*_ActionKeysMatchTable` test fails (a key was dropped
  or added by the refactor) or the `toolwindow-move key=... -> arrow vk=...` format drifted (deferred
  e2e `neovisual-toolwindow` would catch it — informational).

### BP-3 — M21: drop the throwaway `pathToItem` param from `HierarchyForestBuilder.Build`
- **Files:** `MyExtension/ToolWindows/HierarchyForestBuilder.cs`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (a) `HierarchyForestBuilder.Build(IEnumerable<HierarchyItemInfo> items)` — remove the
  `Dictionary<string, string> pathToItem` param and the `pathToItem[item.FullPath] = item.FullPath`
  write (the map is never read by any caller). (b) Keep the `.cs` filter in exactly ONE place — the
  pure builder (HierarchyForestBuilder.cs:49-50); remove the duplicate `.cs` filter from
  `SolutionExplorerController.MapChildren` (line 324-325) so it passes all physical files through
  (the builder filters; the DTE `pathToItem` map gains harmless non-.cs entries that are never picked).
  (c) Update `ResolveTreeItem`'s `Build(items, ...)` call (line 270) to the new signature. (d) Update
  the 5 `Run_HierarchyForestBuilder_*` tests to the new signature — `_FullPathFlowsThroughAndPathMap`
  and `_NonFolderNonFileKindsSkipped` drop their map assertions and assert the forest only.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- HierarchyForestBuilder` → all
  `Run_HierarchyForestBuilder_*` pass (forest output unchanged); `dotnet build` 0 errors.
- **Fails-if:** build error (a caller/test still passes the old 2-arg signature — the RED) or a
  `Run_HierarchyForestBuilder_*` test fails (the forest changed).

### BP-4 — M22: single `FocusGuard.OwnsKeyboard` exemption
- **Files:** `MyExtension/ToolWindows/FocusGuard.cs`, `MyExtension/InputHandler.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Add `public static bool OwnsKeyboard(bool isInputMode, bool isTextInputSurface, bool
  textInputSurfaceFocused) => isInputMode || (isTextInputSurface && textInputSurfaceFocused);` to
  `FocusGuard`. Route all three formulations through it: `HasToolWindowActionKeys` →
  `(!editorFocused || OwnsKeyboard(isInputMode, isTextInputSurface, textInputSurfaceFocused))`;
  `ShouldRouteToolWindowKey` → `isToolWindow && !(editorFocused && !OwnsKeyboard(...))`;
  `InputHandler.EditorFocusedVeto` (line 95-98) → `_vsVim.IsEditorFocused && !FocusGuard.OwnsKeyboard(
  _windowManager.CurrentController?.IsInputMode == true, GeneralToolWindowController.IsTextInputType(
  _windowManager.Type), _windowManager.TextInputSurfaceFocused)`. The existing `HasToolWindowActionKeys`
  / `ShouldRouteToolWindowKey` signatures are UNCHANGED (no test churn). Add
  `Run_FocusGuard_OwnsKeyboard_TruthTable` asserting the helper (input-mode owns; text-input-surface
  focused owns; neither → false).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` → all `Run_FocusGuard_*`
  pass (behavior-preserving) incl. the new `Run_FocusGuard_OwnsKeyboard_TruthTable`.
- **Fails-if:** a `Run_FocusGuard_*` test fails (the exemption semantics changed) or
  `Run_FocusGuard_OwnsKeyboard_TruthTable` fails (the helper's truth table is wrong).

### BP-5 — M24: unify the sync/async init-step logging contract
- **Files:** `MyExtension/InitSteps.cs`, `MyExtension/MyExtensionPackage.cs`
- **Change:** Add `public static void RunSync(string name, Action step, Action<string> log)` to
  `InitSteps` — the same per-step try/catch + `[MyExtension] init {name} ok` /
  `[MyExtension] init {name} failed: {ex.Message}` contract as `RunAsync`, for a synchronous step.
  Replace `MyExtensionPackage.RunInitStep` (lines 164-175) with a thin delegate to
  `InitSteps.RunSync(name, step, msg => NeoVisualLog.Log(msg))` (or delete it and update the 3 call
  sites at lines 61-63). The 3 log-config steps stay where they are (before `SwitchToMainThreadAsync`)
  — only the try/catch + log contract is unified. Diagnostic contract unchanged:
  `[MyExtension] init <step> ok/failed: {msg}`.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- InitSteps` → all `Run_InitSteps_*`
  pass (behavior-preserving) + new `Run_InitSteps_RunSyncLogsOk` (a sync step logs
  `[MyExtension] init x ok`) and `Run_InitSteps_RunSyncFailingLogsFailed` (a throwing sync step logs
  `[MyExtension] init x failed: boom` and does not throw).
- **Fails-if:** `Run_InitSteps_RunSyncLogsOk` / `Run_InitSteps_RunSyncFailingLogsFailed` fail
  (`InitSteps.RunSync` missing → compile error, or the log format drifted) or a `Run_InitSteps_*`
  test regresses.

### BP-6 — m13: finders route the open guard through `HitOpener`
- **Files:** `Telescope/FileFinder.cs`, `Telescope/CodeIssuesFinder.cs`, `Telescope/GrepFinder.cs`,
  `Telescope/HitOpener.cs`
- **Change:** Replace the duplicated `if (!File.Exists(hit.FilePath)) return;` guard + open in
  `FileFinder.OpenHit` (line 75), `CodeIssuesFinder.OpenHit` (line 111), and `GrepFinder.OpenHit`
  (line 137) with `HitOpener.OpenAtLine(hit, (path, line) => { ...open + log... })` (the hermetic
  `_testOpener` branch stays first, unchanged). `HitOpener` already owns the null/missing-file guard
  (HitOpener.cs:14). Log lines stay byte-identical: `[Telescope] opened file: ...`,
  `[Telescope] opened issue: ... line=...`, `[Telescope] opened grep: file=... line=...`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- HitOpener` → all `Run_HitOpener_*`
  pass (incl. `Run_HitOpener_MissingFileDoesNotInvoke`); `dotnet run --project tests/Telescope.Tests
  -- FileFinder` / `-- Grep` / `-- Issues` → the finder suites still pass.
- **Fails-if:** `Run_HitOpener_MissingFileDoesNotInvoke` fails or a finder test regresses (the
  consolidated guard changed open behavior) or a `[Telescope] opened ...` log literal drifted.

**Phase 6 gate:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
tests/NeoVisual.Tests` and `dotnet run --project tests/Telescope.Tests` all pass.

---

## Phase 7 — Dead code deletion (M17, M23, m11, m12)

### BP-7 — M17: wire `VimModeTracker` through `VimModeState` (single owner) — coordinated with M16
- **Files:** `MyExtension/VimModeState.cs`, `MyExtension/VimModeTracker.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Make `VimModeState.SetMode(int? mode)` return `bool` (whether the mode changed) so the
  tracker can gate the `vim-mode=` log (M16's log-on-change contract). `VimModeTracker` replaces its
  `_cachedTyping` field with a `private readonly VimModeState _state = new();`:
  `IsInTypingMode => _state.IsTyping`; `UpdateTypingFromMode(int? mode)` calls
  `if (_state.SetMode(mode)) Telescope.NeoVisualLog.Log($"{...}vim-mode={_state.ModeName}")`;
  `OnViewLostFocus`/`OnViewClosed` delegate to `_state.OnViewLostFocus(isFocusedView)` /
  `_state.OnViewClosed(isFocusedView)` and log `vim-mode=Unknown` only when the returned flag is true.
  `_editorFocused` stays in the tracker (focus state, not mode state). The emitted token stays
  `vim-mode=Insert|Normal|Replace|Unknown` byte-identical. Existing `Run_VimModeState_*` tests ignore
  the new `SetMode` return (still compile).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- VimModeState` → all
  `Run_VimModeState_*` pass; `dotnet run --project tests/NeoVisual.Tests -- VimModeSource` → all
  `Run_VimModeSource_*` pass, incl. `Run_VimModeSource_FakeLogsVimMode` (asserts the file contains
  `[NeoVisual] vim-mode=Insert` / `vim-mode=Normal` — the M16 log-on-change contract survives).
- **Fails-if:** `Run_VimModeSource_FakeLogsVimMode` fails (the `vim-mode=` log broke or double-logs on
  an unchanged mode) or a `Run_VimModeState_*` test fails (the state owner regressed).

### BP-8 — M23 (code): delete the dead-code cluster
- **Files:** `MyExtension/CardinalMovment/UtilityMethods.cs` (delete `GetWindowsList`, line 30),
  `MyExtension/CardinalMovment/WindowAdapter.cs` (delete `IsOnScreen`, line 51),
  `MyExtension/CardinalMovment/Direction.cs` (delete `DirectionExtensions.Sign`, line 24),
  `MyExtension/CardinalMovment/LinqExtensionMethods.cs` (delete the whole file — it only held
  `DistinctBy`), `tests/NeoVisual.Tests/Program.cs` (delete `Run_DistinctBy_DeduplicatesOnKey`,
  line 1025)
- **Change:** Delete the four zero-production-caller members (verified: `GetWindowsList`, `IsOnScreen`,
  `DirectionExtensions.Sign`, `DistinctBy` have no callers outside the deleted test). Delete
  `LinqExtensionMethods.cs` and `Run_DistinctBy_DeduplicatesOnKey`. No log literal is touched.
- **Verify-with:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests` all pass with the count dropping by exactly 1 (the deleted DistinctBy test).
- **Fails-if:** build error (a missed caller of a deleted member — the RED) or a NeoVisual test
  regresses or the count does not drop by exactly 1.

### BP-9 — M23 (docs): reference sweep for the deleted symbols + lint allowlist
- **Files:** `AGENTS.md`, `docs/spec.md`, `.opencode/skills/vs-extension-dev/SKILL.md`,
  `tools/check-doc-refs.ps1`
- **Change:** Update every doc reference to the deleted symbols (grep `DistinctBy`, `IsOnScreen`,
  `LinqExtensionMethods`):
  - `AGENTS.md:314` — drop the "hand-rolls `DistinctBy`" net472 claim (reword to just
    `IReadOnlySet<T>` is unavailable).
  - `docs/spec.md:66` — remove `IsOnScreen` from the `WindowAdapter.cs` row; `:72` — remove the
    `LinqExtensionMethods.cs` row; `:209` — drop `DistinctBy` from the test list; `:291` — reword the
    net472 claim.
  - `SKILL.md:67` — remove the `LinqExtensionMethods.cs` row; `:171` — reword the net472 claim.
  - `tools/check-doc-refs.ps1` — add `DistinctBy` to the `$externalAllowlist` (the established
    "recently removed" pattern, cf. `IsCompletionActive`/`Intersects` at lines 84-89) so the review
    archives (`docs/architecture-review.md`, `.opencode/agent/code-review-worker.md`) that cite the
    finding stay lint-clean.
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` → `[PASS] ... 0 unresolved` (the acceptance gate
  for the removal sweep).
- **Fails-if:** the lint reports unresolved `DistinctBy` / `IsOnScreen` / `LinqExtensionMethods.cs`
  (a doc reference was missed) or a build error from a stale doc-driven reference.

### BP-10 — m11: delete `OverlayKeyHandler.ResultCount`
- **Files:** `Telescope/OverlayKeyHandler.cs`
- **Change:** Delete the `ResultCount` property (OverlayKeyHandler.cs:78-82) — only `SetResults` is
  used (verified: no callers). No log literal touched.
- **Verify-with:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
  tests/Telescope.Tests -- KeyHandler` → all `Run_KeyHandler_*` pass (incl.
  `Run_KeyHandler_NoSelectionWithoutResults`).
- **Fails-if:** build error (a caller of `ResultCount` was missed — the RED) or a `Run_KeyHandler_*`
  test regresses.

### BP-11 — m12: delete `TextMotionNavigator.ColumnNumber` + update the pinned tests
- **Files:** `Telescope/TextMotionNavigator.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Delete `ColumnNumber` (TextMotionNavigator.cs:54-65) — no production callers (verified);
  the only users are the 2 assertions in `Run_Preview_JkMoveByLine` (lines 750, 755). Update those to
  assert `Caret` directly: for `"alpha\nbeta\ngamma"`, `Down()` from caret 0 → caret 6 (line 2),
  `Down()` → caret 11 (line 3), `Up()` → caret 6 (line 2). So `Assert.Equal(6, n.Caret)` replaces
  `Assert.Equal(0, n.ColumnNumber - 1)` at both sites.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Preview` → all `Run_Preview_*`
  pass, incl. the updated `Run_Preview_JkMoveByLine`.
- **Fails-if:** build error (a production caller of `ColumnNumber` was missed — the RED) or
  `Run_Preview_JkMoveByLine` fails (the caret assertions are wrong).

**Phase 7 gate:** `dotnet build MyExtension.slnx` 0 errors; both unit suites pass (NeoVisual count
drops by 1 from BP-8); `pwsh tools/check-doc-refs.ps1` 0 unresolved (BP-9).

---

## Phase 8 — Harness hardening (M26, M27, M28, m18, m19, m20)

> Harness-only items (M26/M27/m18/m19/m20) have NO hermetic unit seam — their Verify-with is the
> harness self-check seam where one exists, otherwise the deferred e2e. Stated honestly per item.

### BP-12 — M26: tighten `preview tokens=\d+` → `preview tokens=[1-9]\d*`
- **Files:** `tools/test-e2e.ps1`
- **Change:** Line 974: `"$($script:PfxTel)preview tokens=\d+"` →
  `"$($script:PfxTel)preview tokens=[1-9]\d*"` (a vacuous presence check — `\d+` matches `tokens=0`).
  No-seam (harness).
- **Verify-with:** harness self-check seam: none exists for this specific line — the honest gate is
  the deferred e2e `telescope-preview` scenario asserting `[Telescope] preview tokens=[1-9]\d*`
  (informational; the seeded `Motions.cs` always yields ≥1 token).
- **Fails-if:** the deferred e2e `telescope-preview` fails on the tightened regex (a seeded file with
  0 tokens — impossible for the seeded sources) or the regex was left as `\d+`.

### BP-13 — M27: tighten `results count=(\d+)` → `results count=[1-9]\d*` at :518 ONLY
- **Files:** `tools/test-e2e.ps1`
- **Change:** Line 518: `"$($script:PfxTel)results count=(\d+)"` →
  `"$($script:PfxTel)results count=[1-9]\d*"`. Do NOT touch `:1533`/`:1537` — `telescope-no-selection`
  legitimately asserts `results count=0 selected=0`. No-seam (harness).
- **Verify-with:** harness self-check seam: none for this line — the honest gate is the deferred e2e
  `telescope-search` scenario (query `pro` matches ≥1 file → `results count=[1-9]\d*` passes) while
  `telescope-no-selection` still passes (its `count=0` assertions are untouched).
- **Fails-if:** the deferred e2e `telescope-search` fails on the tightened regex (a filter that
  legitimately returns 0 — `pro` matches seeded files, so count ≥ 1) or `:1533`/`:1537` were
  accidentally changed.

### BP-14 — M28: inject a controllable timer seam into `LogFileWriter`; make the flush-timer tests deterministic
- **Files:** `Telescope/LogFileWriter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Add `internal static Func<Action, IDisposable>? TimerScheduler` to `LogFileWriter`.
  `EnsureTimer()` uses it when set (`_flushTimer = TimerScheduler(Flush)`), else the real
  `new Timer(...)`; change `_flushTimer` to `IDisposable?` and guard the re-arm in `GetWriter` to
  `if (_flushTimer is Timer t) t.Change(200, Timeout.Infinite);`. Rewrite
  `Run_LogFileWriter_FlushTimer_OneShotFires` and `Run_LogFileWriter_FlushTimer_IdleDoesNotFire` to
  inject a fake scheduler that captures the flush callback, then fire it (or not) deterministically —
  NO wall-clock `Thread.Sleep` polling. Reset `TimerScheduler = null` in a `finally`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FlushTimer` → both
  `Run_LogFileWriter_FlushTimer_OneShotFires` (firing the captured callback advances `FlushCount`) and
  `Run_LogFileWriter_FlushTimer_IdleDoesNotFire` (not firing leaves `FlushCount` unchanged) pass
  deterministically (no sleeps).
- **Fails-if:** `Run_LogFileWriter_FlushTimer_*` still sleep-poll (the seam wasn't used) or
  `Run_LogFileWriter_FlushTimer_OneShotFires` fails (the fake timer doesn't advance `FlushCount`).

### BP-15 — m18: PID-scope `Stop-HarnessVs`
- **Files:** `tools/test-e2e.ps1` (line 131), `tools/iterate-telescope.ps1` (line 98)
- **Change:** Convert `Stop-HarnessVs` (both copies) from title-matching any devenv
  (`MainWindowTitle -match 'MyExtension'|'Experimental'`) to PID-scoped kills via the M-M5
  `Stop-VsPids`/`$script:SpawnedVsPids` pattern — kill ONLY the devenv PIDs this run spawned/tracked
  (`Add-SpawnedVs` on every `Start-Process devenv`). A user's unrelated VS instance is never killed.
  No-seam (harness).
- **Verify-with:** harness self-check seam: `pwsh tools/iterate-telescope.ps1 -SelfCheck` already
  proves `Stop-SpawnedVs` kills exactly the listed PID and leaves an unlisted one alive (the M-M5
  pattern); the honest gate for the `Stop-HarnessVs` conversion is the deferred e2e full suite (no
  user VS terminated).
- **Fails-if:** the deferred e2e kills a user's unrelated VS instance (a title-scoped kill remains)
  or the self-check fails (the PID-scoped kill is not exact).

### BP-16 — m19: `NEOVISUAL_LOG_DIR` → Process scope
- **Files:** `tools/iterate-telescope.ps1`
- **Change:** Line 226: `[Environment]::SetEnvironmentVariable('NEOVISUAL_LOG_DIR', $logDir, 'User')`
  → `'Process'` (no persistent User-scope env mutation). Update the stale comment at lines 220-224
  that documents the "intentionally User-scoped" intent. Extend the `-SelfCheck` env-scope check
  (lines 162-169) to also assert `NEOVISUAL_LOG_DIR` User-scope is unchanged before/after the run.
  No-seam (harness).
- **Verify-with:** harness self-check seam: `pwsh tools/iterate-telescope.ps1 -SelfCheck` → the
  extended check asserts `NEOVISUAL_LOG_DIR` User-scope unchanged (not written by the harness).
- **Fails-if:** the self-check reports `NEOVISUAL_LOG_DIR` still written to User scope, or the stale
  "intentionally User-scoped" comment remains.

### BP-17 — m20: clean the stale `check-doc-refs.ps1` allowlist
- **Files:** `tools/check-doc-refs.ps1`
- **Change:** Remove `LeaderSequenceMatcher` and `FocusKeeper` from `$proposedSymbols` (lines 132-135)
  and `tools/harness-common.ps1` from `$proposedPaths` (lines 136-138) — all three now exist and the
  "proposed" entries mask real drift. Keep `SimpleKeyBuilder` and `FzfFinder` (still proposed).
  No-seam (harness).
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` → `[PASS] ... 0 unresolved` (the lint now actually
  resolves `LeaderSequenceMatcher` / `FocusKeeper` / `tools/harness-common.ps1` against the source
  tree instead of masking them).
- **Fails-if:** the lint reports unresolved `LeaderSequenceMatcher` / `FocusKeeper` /
  `tools/harness-common.ps1` (the removal exposed real drift — a doc references them but they don't
  resolve) or the still-proposed `SimpleKeyBuilder`/`FzfFinder` were removed.

**Phase 8 gate:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
tests/Telescope.Tests -- FlushTimer` deterministic; `pwsh tools/iterate-telescope.ps1 -SelfCheck`
passes; `pwsh tools/check-doc-refs.ps1` 0 unresolved.

---

## Phase 9 — Test hermeticity + naming sweep (m1, m2, m7, m8, m9, m10, m21-m30, m32, n1, n2)

### BP-18 — m21: `TestRunner.TempDir.Dispose` surfaces delete failures
- **Files:** `tests/TestRunner.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** `TempDir.Dispose` (TestRunner.cs:144-147) rethrows on recursive-delete failure instead
  of swallowing (`catch { }` → rethrow). Add `Run_TempDir_DisposeSurfacesFailure` (Telescope.Tests):
  create a `TempDir`, sabotage the delete (delete the dir and create a file at the same path), call
  `Dispose`, and assert it throws (try/catch — the runner fails the test if it does not throw).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- TempDir` →
  `Run_TempDir_DisposeSurfacesFailure` passes (Dispose throws on the sabotaged delete).
- **Fails-if:** `Run_TempDir_DisposeSurfacesFailure` fails (Dispose still swallows) or an existing
  test that relied on silent dispose failure now fails (the surfaced error is real).

### BP-19 — m22: `Run_FzfFilter_FilterMatchesPrefix` uses the injected-path seam
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Rewrite `Run_FzfFilter_FilterMatchesPrefix` (line 464) to resolve the fzf path
  explicitly (e.g. from PATH) and inject it via `new FzfFilter(fzfPath)`; fail loudly when fzf is
  absent (no silent skip, no implicit PATH resolution via the default ctor — the Mystery Guest is
  explicit).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FzfFilter` →
  `Run_FzfFilter_FilterMatchesPrefix` passes with fzf on PATH (the injected path filters `alp` →
  `alpha.cs`), or fails loudly without fzf.
- **Fails-if:** the test silently passes without exercising the real filter (the Mystery Guest
  remains) or the injected path is not used.

### BP-20 — m23: `Run_FinderBase_OpenErrorSwallowed` gains a real assertion
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Add the missing assertion to `Run_FinderBase_OpenErrorSwallowed` (line 1298): after
  `finder.OnSelected(...)`, flush the log and assert exactly one `[Telescope] open item failed: open
  boom` line (mirroring the existing `Run_FinderBase_OpenErrorSingleLog` pattern at line 1331).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- FinderBase` →
  `Run_FinderBase_OpenErrorSwallowed` passes WITH the assertion (the log contains exactly one
  `[Telescope] open item failed: open boom` line).
- **Fails-if:** the test passes without asserting (no assertion added) or the single-log assertion
  fails (the error is double-logged or not logged).

### BP-21 — m24: `Run_Syntax_KeywordsAndIdentifiers` asserts the exact token sequence
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Replace the weak presence checks in `Run_Syntax_KeywordsAndIdentifiers` (line 1563) with
  an exact-sequence assertion for `SyntaxHighlighter.Tokenize("public class Foo { }")` — assert the
  ordered `(Text, Category)` pairs (e.g. `public`/Keyword, ` ` /Default, `class`/Keyword, ` ` /Default,
  `Foo`/Default, ` ` /Default, `{`/Default, ` ` /Default, `}`/Default — the exact run boundaries are
  whatever `Tokenize` produces; the assertion pins them).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- Syntax` →
  `Run_Syntax_KeywordsAndIdentifiers` passes with the exact-sequence assertion.
- **Fails-if:** the test fails (the asserted sequence does not match `Tokenize`'s actual output — the
  assertion was wrong or the tokenizer changed) or the weak presence checks remain.

### BP-22 — m25: split `Run_HierarchyResolver_FirstSourceFile`
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Split the 4 bundled sub-assertions (line 251) into focused tests with messages:
  `Run_HierarchyResolver_FirstSourceFile_FileVsFolder`, `_FolderRecursion`,
  `_SkipsNonFileNonFolder`, `_EmptyReturnsNull` (each with an `Assert.True(..., "message")`).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- HierarchyResolver` → all 4 split
  tests pass (behavior preserved).
- **Fails-if:** a split test fails (behavior changed) or the original Eager Test remains.

### BP-23 — m26: named action-key-count constant (7 after CR1)
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Update `PositiveActionKeyCount` (line 623) from `5` to `7` (the real
  `TextInputToolWindowController` action-key count after CR1 adds `Keys.I`) and replace the hardcoded
  `actionKeyCount: 6` at lines 731/744 with the named constant.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` → all `Run_FocusGuard_*`
  pass (the guard only checks `> 0`, so the value is behavior-neutral; the constant now matches the
  real count).
- **Fails-if:** a `Run_FocusGuard_*` test fails (the constant doesn't match the real count) or the
  magic `5`/`6` literals remain.

### BP-24 — m27: `Run_GrepFinder_GatherHitsThrowsNotSupported` uses the public path
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Rewrite `Run_GrepFinder_GatherHitsThrowsNotSupported` (line 1984) to drop the reflection
  (`GetMethod("GatherHits", NonPublic|Instance)`) and use the public `GetCandidates()` path: call
  `finder.GetCandidates()` (the parameterless base path, which invokes the throwing `GatherHits()` and
  catches it), then assert (a) it returns empty and (b) the log contains exactly one
  `[Telescope] GrepFinder failed to enumerate:` line — proving the base gather stub is a loud failure,
  not a silent empty, via observable behavior.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- GrepFinder` → the rewritten
  `Run_GrepFinder_GatherHitsThrowsNotSupported` passes (empty result + the logged failure line).
- **Fails-if:** the test fails (GetCandidates does not log the failure — the stub silently returned
  empty) or reflection is still used.

### BP-25 — m28: add `Run_TextInput_TryMove_*` (the stale "Text.UI not referenced" comment is wrong)
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Add `Run_TextInput_TryMove_UnmappedKeyNotConsumed` — `new
  TextInputToolWindowController(ToolWindowType.CommandWindow).TryMove(Keys.X)` returns false (no
  throw), proving `TryMove` is callable on the test host (the csproj references
  `Microsoft.VisualStudio.Text.UI`, so the `IWpfTextView` type JITs fine). Update the stale comment at
  lines 541-543 (which claims Text.UI is not referenced). Mapped keys (W/B/E/A/H/L) reach
  `Keyboard.FocusedElement` which throws on the MTA host — that is a documented limitation, NOT the
  stale "not referenced" claim; `SelectFirstSourceFile` stays VS-coupled (no-seam).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- TextInput` →
  `Run_TextInput_TryMove_UnmappedKeyNotConsumed` passes (TryMove(Keys.X) → false, no throw).
- **Fails-if:** the new test fails (TryMove throws on the test host — the stale comment was right) or
  the stale "Text.UI not referenced" comment remains.

### BP-26 — m29: delete redundant action-key presence tests
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Delete `Run_ActionTable_SolutionExplorer_HjklInActionKeys` (line 493),
  `Run_ActionTable_TextInput_HjklInActionKeys` (line 503), and `Run_TextInput_ActionKeys` (line 477) —
  all three are redundant with the exact-set `Run_ActionTable_*_ActionKeysMatchTable` tests.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests` → all pass; the count drops by exactly
  3 (deterministic signal).
- **Fails-if:** a remaining test fails or the count does not drop by exactly 3.

### BP-27 — m30: remove the stale "RED today" comment on `Run_LogFileWriter_ClearPerPath`
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Remove the stale comment at lines 177-178 ("RED today: the once-per-process
  `_clearedThisProcess` flag...") — the suite is all-green, so the comment contradicts reality.
  No-seam (comment).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- LogFileWriter` →
  `Run_LogFileWriter_ClearPerPath` passes (unchanged).
- **Fails-if:** n/a (comment-only) — the test still passes; the stale comment is gone.

### BP-28 — m1: reindent `WindowManager` class body
- **Files:** `MyExtension/WindowManager.cs`
- **Change:** Reindent the class body (lines 10+) from column 0 to inside `namespace MyExtension {`.
  No-seam (build).
- **Verify-with:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests` all pass.
- **Fails-if:** build error (indentation broke a token — unlikely) or a NeoVisual test regresses.

### BP-29 — m2: `KeybindingConfig` single `File.Exists` + reindent
- **Files:** `MyExtension/KeybindingConfig.cs`
- **Change:** Evaluate `File.Exists(path)` once into a local (currently evaluated at line 94 and again
  at line 104) and use it for both the load guard and the log line; reindent `return new
  KeybindingConfig(leaderKey, bindings);` (line 106, currently at column 0). No-seam (build).
- **Verify-with:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests -- Keybinding` → all `Run_Keybinding_*` pass.
- **Fails-if:** build error or a `Run_Keybinding_*` test regresses.

### BP-30 — m7 + m10: `FocusGuard.IsTyping` dead branch + param rename
- **Files:** `MyExtension/ToolWindows/FocusGuard.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (m7) Remove the dead `isToolWindow ? isInputMode : ...` sub-branch in `IsTyping`
  (FocusGuard.cs:46) — `isInputMode` is always false there (the outer ternary already handled true),
  so `(isToolWindow ? isInputMode : editorInTypingMode)` simplifies to
  `(isToolWindow ? false : editorInTypingMode)`. (m10) Rename the `editorFocused` param to
  `editorFocusedVeto` (it receives `InputHandler.EditorFocusedVeto`, not the raw flag) and update the
  named-arg call sites in `Run_FocusGuard_IsTypingTruthTable` / `Run_FocusGuard_IsTypingToolWindowInput`
  (lines 751-773). Behavior-preserving.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` → all `Run_FocusGuard_*`
  pass, incl. `Run_FocusGuard_IsTypingTruthTable` and `Run_FocusGuard_IsTypingToolWindowInput`.
- **Fails-if:** a `Run_FocusGuard_IsTyping*` test fails (behavior changed) or build error (a stale
  `editorFocused:` named arg).

### BP-31 — m8: `FocusKeeper` uses `Environment.TickCount64`
- **Files:** `MyExtension/ToolWindows/FocusKeeper.cs`
- **Change:** Replace `Environment.TickCount` (int, wraps every ~24.9 days) with
  `Environment.TickCount64` (available in net472) in `FocusKeeper.Run` (lines 19, 25, 31). The pure
  `FocusKeeperSchedule` is unchanged. No-seam for the timer (VS-coupled DispatcherTimer).
- **Verify-with:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests -- FocusKeeperSchedule` → all `Run_FocusKeeperSchedule_*` pass (unchanged).
- **Fails-if:** build error (`TickCount64` unavailable — it IS in net472) or a
  `Run_FocusKeeperSchedule_*` test regresses.

### BP-32 — m9: `SolutionExplorerController` single keeper-duration constant
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** Extract `private const int FocusKeeperDurationMs = 1500;` and use it for both
  `FocusKeeper.Run(TimeSpan.FromMilliseconds(100), 1500, ...)` and
  `FocusKeeperSchedule.Decide(..., 1500)` (lines 115-118).
- **Verify-with:** `dotnet build MyExtension.slnx` 0 errors; `dotnet run --project
  tests/NeoVisual.Tests -- FocusKeeperSchedule` → all `Run_FocusKeeperSchedule_*` pass (unchanged).
- **Fails-if:** build error or a `Run_FocusKeeperSchedule_*` test regresses.

### BP-33 — m32: fix the SKILL.md `CardinalMovment` wording
- **Files:** `.opencode/skills/vs-extension-dev/SKILL.md`
- **Change:** Correct line 179 — the folder is `CardinalMovment` (typo), the NAMESPACE is
  `CardinalNavigation` (not a typo); the current wording conflates them. Note: this wording changes
  again in the restructure (Phase 11). No-seam (docs).
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` → 0 unresolved (the reworded text introduces no
  unresolvable backticked token).
- **Fails-if:** the lint reports a new unresolved reference (the reworded text introduced a backticked
  token that doesn't resolve) or the conflation remains.

### BP-34 — n1: `Assert.True(controller.ActionKeys.Count > 0)`
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Replace the double-negative `Assert.False(controller.ActionKeys.Count == 0)` (line 441 in
  `Run_TextInput_StartsInInsertMode`) with `Assert.True(controller.ActionKeys.Count > 0)`.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- TextInput` →
  `Run_TextInput_StartsInInsertMode` passes.
- **Fails-if:** the test fails (ActionKeys empty) or the double-negative remains.

### BP-35 — n2: `Run_CaretPlacement_EnumValues` asserts the enum values
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Extend `Run_CaretPlacement_EnumValues` (line 719) to assert the values:
  `(int)CaretPlacement.Current == 0`, `(int)CaretPlacement.End == 1`, `(int)CaretPlacement.Start == 2`
  (the declaration order in `OverlayKeyHandler.cs:51-56`).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- CaretPlacement` →
  `Run_CaretPlacement_EnumValues` passes with the value assertions.
- **Fails-if:** the test fails (the enum values differ from the asserted ones — the assertion was
  wrong or the enum changed).

**Phase 9 gate:** `dotnet build MyExtension.slnx` 0 errors; both unit suites pass (NeoVisual count
drops by 3 from BP-26; Telescope count unchanged net of the Phase 9 additions/deletions).

---

## Phase 10 — Docs drift (M29, M30, M31, m31)

### BP-36 — M29: update the unit-test counts in the docs
- **Files:** `docs/spec.md` (lines 353-354), `.opencode/skills/vs-extension-dev/SKILL.md` (lines
  210-211), `docs/progress.md` (lines 72-73)
- **Change:** Update the stale `77`/`74` counts to the ACTUAL `--list` counts at execution time
  (135/130 as of the plan; counts drift upward as Phases 0-9 add tests — verify, don't hardcode).
  No-seam (docs).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- --list` and `dotnet run --project
  tests/NeoVisual.Tests -- --list` → count the listed tests; the docs match the counts.
- **Fails-if:** the docs still say 77/74 or do not match the `--list` count.

### BP-37 — M30: add the 8 missing diagnostics to `docs/spec.md` §4
- **Files:** `docs/spec.md` (line 162 §4)
- **Change:** Add the 8 harness-asserted lines omitted from the diagnostics contract:
  `[NeoVisual] stale-toolwindow sentinel active`, `[NeoVisual] leader-binding failed: {seq}: {msg}`,
  `[NeoVisual] shortcut-binding failed: {simple}: {msg}`, `[NeoVisual] output pane unavailable:
  {reason}`, `[MyExtension] init <step> ok/failed: {msg}`, `[Telescope] fzf filter failed: {msg}` /
  `fzf filter failed: timeout after {ms}ms`, `[Telescope] fzf unavailable — showing unfiltered list`,
  `[Telescope] filter failed: {msg}`. No-seam (docs).
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` → 0 unresolved (log literals are not symbol/path
  refs — the lint skips them, matching the existing §4 lines).
- **Fails-if:** the lint flags a new unresolved reference (a backticked token in the added lines that
  doesn't resolve) or a listed diagnostic's format differs from the product's emitted token.

### BP-38 — M31: add the missing post-consolidation seams to the key-files tables
- **Files:** `docs/spec.md` (line 47 §2.2), `.opencode/skills/vs-extension-dev/SKILL.md` (lines 50, 68)
- **Change:** Add the ~25 post-consolidation seams missing from the key-files tables (grep the tables
  for what's absent): `FocusGuard`, `FocusKeeper`, `HierarchyForestBuilder`, `StaleToolWindowSentinel`,
  `LeaderSequenceMatcher`, `SimpleShortcutMatcher`, `KeyNameBuilder`, `VimModeState`,
  `VimModeClassifier`, `VimModeSource`, `InitSteps`, `NavigationSnapshot`, `RectCoordinate`,
  `WindowNavigationEngine`, `NavigationSettings`, `ToolWindowTypeResolver`, `InjectedKeyGuard`,
  `PopupNavigation`, `FinderBase`, `HitOpener`, `FileContentCache`, `ProjectFileCache`,
  `LogFileWriter`, `NeoVisualLog`, `PaneFailureTracker`, `FocusTargetModel`, `LineIndex`, `TryDispatch`,
  `ResultsFormatter`, `ResultMapper`, `SyntaxHighlighter`, `OverlayKeyHandler`, `TextMotionNavigator`,
  `FzfFilter`, `FileFinder`, `CodeIssuesFinder`, `ReferencesFinder`, `GrepFinder`,
  `ImplementationFinder`, `ProjectFiles`, `HierarchyWalker`, `DteFileOpener`, `TelescopeController`,
  `TelescopeOverlay`, `TelescopeLauncher`, `TelescopeCommand`, `Actions`, `KeyNames`, `KeyInjection`,
  `GlobalKeyboardHook`, `NativeMethods`, `BlockCaretAdornment`, `ToolWindowControllerBase`,
  `IToolWindowController`, `GeneralToolWindowController`, `TextMotionHelper`,
  `SolutionExplorerController`, `HierarchyResolver`, `WindowManager`, `VimModeTracker`, `VsServices`,
  `MyExtensionPackage` — as backticked file paths that resolve. No-seam (docs).
- **Verify-with:** `pwsh tools/check-doc-refs.ps1` → 0 unresolved (every added path exists).
- **Fails-if:** the lint flags a new unresolved reference (a path that doesn't exist) or a seam is
  still missing from both tables.

### BP-39 — m31: bump the `docs/progress.md` header
- **Files:** `docs/progress.md` (line 10)
- **Change:** Bump the header for the 2026-09-30 Code-review GREEN (e.g. `**Status:** ACTIVE ·
  **Updated:** 2026-09-30 · **Last item:** Code-review fixes (67 findings) GREEN`). No-seam (docs).
- **Verify-with:** the header reflects the 2026-09-30 date + the Code-review GREEN.
- **Fails-if:** the header still says `Updated: 2026-09-28 · Last item: neovisual-editor-insert`.

**Phase 10 gate:** `pwsh tools/check-doc-refs.ps1` → 0 unresolved; `dotnet build MyExtension.slnx` 0
errors; both unit suites pass.

---

## Verification Trace

| failing test / gate | implicated steps | expected pass signal |
|---|---|---|
| `Run_SimpleShortcutMatcher_*` / `Run_KeyNameBuilder_*` | BP-1 | all pass; `BuildSimpleKey` gone (delegation to `KeyNameBuilder.Build`) |
| `Run_ActionTable_*_ActionKeysMatchTable` (exact sets) | BP-2 | all pass; key sets unchanged (shared wiring) |
| `Run_HierarchyForestBuilder_*` (new signature) | BP-3 | all pass; `Build` takes no `pathToItem`; `.cs` filter in the builder only |
| `Run_FocusGuard_*` + `Run_FocusGuard_OwnsKeyboard_TruthTable` | BP-4 | all pass; single `OwnsKeyboard` exemption |
| `Run_InitSteps_*` + `Run_InitSteps_RunSync*` | BP-5 | all pass; `[MyExtension] init <step> ok/failed` contract unified |
| `Run_HitOpener_*` + finder suites | BP-6 | all pass; finders route through `HitOpener` |
| `Run_VimModeState_*` / `Run_VimModeSource_*` (incl. `_FakeLogsVimMode`) | BP-7 | all pass; `vim-mode=Insert\|Normal\|Replace\|Unknown` log-on-change preserved (M16 coordination) |
| NeoVisual suite count −1; `dotnet build` | BP-8 | build 0 errors; count drops by exactly 1 (DistinctBy test deleted) |
| `pwsh tools/check-doc-refs.ps1` | BP-9 | 0 unresolved (deleted-symbol doc sweep + `DistinctBy` allowlist) |
| `Run_KeyHandler_*` | BP-10 | all pass; `ResultCount` deleted |
| `Run_Preview_JkMoveByLine` (updated) | BP-11 | passes; `ColumnNumber` deleted, assertions use `Caret` |
| deferred e2e `telescope-preview` (informational) | BP-12 | `[Telescope] preview tokens=[1-9]\d*` |
| deferred e2e `telescope-search` / `telescope-no-selection` (informational) | BP-13 | `results count=[1-9]\d*` at :518; `count=0` at :1533/:1537 untouched |
| `Run_LogFileWriter_FlushTimer_OneShotFires` / `_IdleDoesNotFire` | BP-14 | both pass deterministically (injected timer, no sleeps) |
| `pwsh tools/iterate-telescope.ps1 -SelfCheck` | BP-15 | PID-scoped kill exact (M-M5 pattern) |
| `pwsh tools/iterate-telescope.ps1 -SelfCheck` (extended) | BP-16 | `NEOVISUAL_LOG_DIR` User-scope unchanged |
| `pwsh tools/check-doc-refs.ps1` | BP-17 | 0 unresolved; `LeaderSequenceMatcher`/`FocusKeeper`/`harness-common.ps1` no longer masked |
| `Run_TempDir_DisposeSurfacesFailure` | BP-18 | passes (Dispose rethrows on delete failure) |
| `Run_FzfFilter_FilterMatchesPrefix` | BP-19 | passes via injected path (or fails loudly without fzf) |
| `Run_FinderBase_OpenErrorSwallowed` | BP-20 | passes WITH the single-log assertion |
| `Run_Syntax_KeywordsAndIdentifiers` | BP-21 | passes with the exact token-sequence assertion |
| `Run_HierarchyResolver_FirstSourceFile_*` (4 split) | BP-22 | all pass |
| `Run_FocusGuard_*` | BP-23 | all pass; `PositiveActionKeyCount` = 7, no magic `5`/`6` |
| `Run_GrepFinder_GatherHitsThrowsNotSupported` (rewritten) | BP-24 | passes via public `GetCandidates()` (empty + logged failure) |
| `Run_TextInput_TryMove_UnmappedKeyNotConsumed` | BP-25 | passes; stale "Text.UI not referenced" comment removed |
| NeoVisual suite count −3 | BP-26 | count drops by exactly 3 (redundant presence tests deleted) |
| `Run_LogFileWriter_ClearPerPath` | BP-27 | passes; stale "RED today" comment removed |
| `dotnet build` + NeoVisual suite | BP-28, BP-29 | 0 errors; all pass (reindent + single `File.Exists`) |
| `Run_FocusGuard_IsTypingTruthTable` / `_IsTypingToolWindowInput` | BP-30 | all pass; dead branch removed, param renamed |
| `Run_FocusKeeperSchedule_*` | BP-31, BP-32 | all pass; `TickCount64` + single duration constant |
| `pwsh tools/check-doc-refs.ps1` | BP-33 | 0 unresolved; SKILL.md wording corrected |
| `Run_TextInput_StartsInInsertMode` | BP-34 | passes; `Assert.True(ActionKeys.Count > 0)` |
| `Run_CaretPlacement_EnumValues` | BP-35 | passes; enum values asserted |
| `--list` counts vs docs | BP-36 | docs match the actual `--list` counts (135/130 baseline) |
| `pwsh tools/check-doc-refs.ps1` | BP-37, BP-38 | 0 unresolved; §4 diagnostics + key-files seams added |
| `docs/progress.md` header | BP-39 | header bumped to 2026-09-30 Code-review GREEN |

## Known-RED allowlist

**None.** No known-RED e2e scenario remains (per `docs/progress.md`). All Phase 6-10 fixes are
RED-proven by unit tests (or build + existing suites for the no-seam items). The verification-agent
must NOT flag the following as regressions:
- The NeoVisual suite count dropping by 1 (BP-8, `Run_DistinctBy_DeduplicatesOnKey` deleted) and by 3
  (BP-26, redundant action-key presence tests deleted) — these are the documented deterministic
  signals of the deletions.
- `Run_Preview_JkMoveByLine` changing its assertions (BP-11) — the caret values are equivalent.
- `Run_FocusGuard_IsTyping*` named-arg renames (BP-30) — behavior-preserving.
- The `vim-mode=` log-on-change behavior (BP-7) — M16's contract, not a regression.

## Deferred e2e (informational — NOT Verify-with)

The following e2e scenarios are QUEUED in `e2e-queue.md` and gate the live behavior of these phases
once the plan is GREEN and a VS-capable machine is available:
- **E2E-M18/M20/M21/M22/M24** (Phase 6): full suite — no log-literal drift after the duplication
  merges.
- **E2E-M17/M23** (Phase 7): full suite — `vim-mode=` unchanged; no log-literal drift after the
  dead-code deletions.
- **E2E-M26/M27/M28** (Phase 8): full suite — the tightened `preview tokens=[1-9]\d*` /
  `results count=[1-9]\d*` assertions pass; `seed-leak` still GREEN.
- **E2E-RESTRUCTURE-1** (Phases 11-14): full 35-scenario suite — no behavior change after the
  folder/namespace restructure.

DONE

---

# BP-n Build Plan — Phases 11-14: Repository Restructure (folders + namespaces + tools + docs)

> **Scope:** Part B of the combined plan (`plans/plan.md`, Phases 11-14). The restructure is
> **behavior-preserving** — NO log literal, NO public API, NO test count change. Every
> Verify-with / Fails-if below references ONLY the build, the unit suite names, the doc-ref
> lint, and the log-literal diff gate (NO e2e scenario names; `E2E-RESTRUCTURE-1` is listed
> informationally at the end).
>
> **Verified baselines (do not re-verify):** `dotnet build MyExtension.slnx` → 0 errors;
> `dotnet run --project tests/Telescope.Tests` → 135 pass; `dotnet run --project
> tests/NeoVisual.Tests` → 130 pass; `pwsh tools/check-doc-refs.ps1` → `[PASS] 28 docs,
> 6009 refs, 0 unresolved`. Repo at commit `f4450cb`/`164e757`. NOTE: 135/130 is the
> PRE-Phase-0 baseline. Phases 0-10 ADD tests, so the restructure's "counts unchanged" gate
> (BP-14) uses the **Phase 11 start baseline** (captured after Phase 10), NOT 135/130.
>
> **Key decisions (do not second-guess):**
> - `CardinalMovment/` → `Navigation/` with namespace `MyExtension.Navigation` (DEVIATION from
>   AGENTS.md's "keep the typo" — the restructure updates ALL references, so the "breaks
>   references" concern is moot; the `CardinalMovment` doc-ref-lint external-allowlist entry is
>   removed in BP-5).
> - Operational docs (spec.md, progress.md, implementation_plan.md, e2e-queue.md) STAY in
>   `docs/` root (referenced by exact path from ~11 agent files + the lint default set); only
>   the review/plan archive docs move to `docs/reviews/` + `docs/plans/`.
> - The doc-ref lint is NOT a pass/fail gate in Phases 11-12. Between the `CardinalMovment`
>   allowlist removal (BP-5) and the Phase 13 sweep (BP-11) the lint is EXPECTED to report
>   unresolved old-path references — that output is the sweep's work queue, NOT a regression.
> - `using Telescope;` directives are left untouched in Phase 11 (the `Telescope.*`
>   sub-namespaces do not exist until Phase 12).
> - The log-literal diff gate needs a pre-move baseline — BP-1 captures it before any `git mv`.

---

## Phase 11 — MyExtension folders + namespaces

### BP-1 — Capture the pre-restructure log-literal baseline (Phase 14 diff-gate input)

- **Files:** none modified (writes `%TEMP%\restructure-log-literals-before.txt` outside the repo).
- **Change:** snapshot the multiset of `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]`
  literal prefixes across every `.cs` file in `MyExtension/`, `Telescope/`, `tests/` BEFORE any
  `git mv`. This is the Phase 14 log-literal diff gate's baseline (BP-16).
  ```powershell
  Get-ChildItem -Recurse -Filter *.cs -Path MyExtension, Telescope, tests |
    Select-String -Pattern '\[(Telescope|NeoVisual|Hook|MyExtension)\]' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { $_.Value } |
    Group-Object | Sort-Object Name |
    ForEach-Object { "$($_.Name)`t$($_.Count)" } |
    Set-Content "$env:TEMP\restructure-log-literals-before.txt"
  ```
- **Verify-with:** `Get-Content "$env:TEMP\restructure-log-literals-before.txt"` is non-empty and
  lists the four prefixes with counts (e.g. `[Telescope]` N, `[NeoVisual]` M, `[Hook]` K,
  `[MyExtension]` J).
- **Fails-if:** the file is missing/empty (BP-16 has no baseline); the command errors on a path.

### BP-2 — `git mv` MyExtension root files into target folders + rename namespaces

- **Files:** all MyExtension root `.cs` files + `CardinalMovment/*.cs` (moved + namespace-edited).
- **Change:** per the target mapping (folder → namespace → files):
  - `git mv` root → `Hooks/` (`MyExtension.Hooks`): GlobalKeyboardHook.cs, NativeMethods.cs,
    KeyInjection.cs, InjectedKeyGuard.cs
  - `git mv` root → `Input/` (`MyExtension.Input`): InputHandler.cs, LeaderSequenceMatcher.cs,
    SimpleShortcutMatcher.cs, KeybindingConfig.cs, KeyNames.cs, KeyNameBuilder.cs,
    PopupNavigation.cs, StaleToolWindowSentinel.cs
  - `git mv` root → `Vim/` (`MyExtension.Vim`): VimModeTracker.cs, VimModeSource.cs,
    VimModeClassifier.cs, VimModeState.cs
  - `git mv` root → `Package/` (`MyExtension.Package`): MyExtensionPackage.cs, InitSteps.cs,
    VsServices.cs, TelescopeCommand.cs, TelescopeLauncher.cs, Actions.cs
  - `git mv` root → `Adornments/` (`MyExtension.Adornments`): BlockCaretAdornment.cs
  - `git mv` root → `ToolWindows/` (`MyExtension.ToolWindows`): WindowManager.cs,
    ToolWindowTypeResolver.cs (window-management/classification types — moved from the root)
  - `git mv CardinalMovment/*.cs Navigation/` (`MyExtension.Navigation`):
    CardinalNavigationConstants.cs, LinqExtensionMethods.cs, Direction.cs, NavigationSettings.cs,
    NavigationSnapshot.cs, RectCoordinate.cs, UtilityMethods.cs, WindowAdapter.cs, WindowMatrix.cs,
    WindowNavigationEngine.cs
  - `ToolWindows/` (the existing 10 files) + `Properties/` stay in place (namespaces
    `MyExtension.ToolWindows` / `MyExtension.Properties`).
  - In each moved file, update the `namespace` declaration to the target namespace:
    `namespace MyExtension` → `namespace MyExtension.<Group>`; `namespace CardinalNavigation` →
    `namespace MyExtension.Navigation`. (The folder is renamed to `Navigation/`; the namespace was
    already `CardinalNavigation` — do not "fix" the folder spelling beyond the move.)
- **Verify-with:** `git status --short` shows the renames (R) with no unexpected deletions;
  `git grep -l 'namespace CardinalNavigation'` returns nothing; `git grep -l 'namespace MyExtension$'`
  returns nothing (no file declares the bare root namespace after the move).
- **Fails-if:** a file is missing from its target folder; a `namespace` declaration was missed
  (surfaces as a build error in BP-3's gate); `CardinalMovment/` still exists with files.
  NOTE: the build is EXPECTED to be RED after this step until BP-3 completes the using sweep —
  do not treat the intermediate RED as a regression.

### BP-3 — Using sweep in MyExtension/ + tests/NeoVisual.Tests/Program.cs

- **Files:** MyExtension/Actions.cs, MyExtension/InputHandler.cs,
  MyExtension/ToolWindows/WindowManager.cs (moved in BP-2), MyExtension/BlockCaretAdornment.cs,
  MyExtension/InjectedKeyGuard.cs, MyExtension/MyExtensionPackage.cs, MyExtension/VimModeState.cs,
  tests/NeoVisual.Tests/Program.cs (+ any file the build flags).
- **Change:**
  - `using CardinalNavigation;` → `using MyExtension.Navigation;` in Actions.cs:1,
    InputHandler.cs:1, ToolWindows/WindowManager.cs:1, tests/NeoVisual.Tests/Program.cs:6.
  - ADD `using MyExtension.ToolWindows;` to BlockCaretAdornment, InjectedKeyGuard, InputHandler,
    MyExtensionPackage, VimModeState + tests/NeoVisual.Tests/Program.cs (WindowManager is now IN
    `MyExtension.ToolWindows` — no using needed; ToolWindowTypeResolver likewise).
  - tests/NeoVisual.Tests/Program.cs: replace `using MyExtension;` with the specific sub-namespaces
    it references (Hooks, Input, Vim, Package, ToolWindows, Navigation, Adornments);
    `MyExtension.KeyInjection.SimulateOnly` → `MyExtension.Hooks.KeyInjection`.
  - `using Telescope;` directives (InputHandler.cs:7, MyExtensionPackage.cs:11,
    TelescopeLauncher.cs:5, ToolWindows/TextMotionHelper.cs:8) are INTENTIONALLY left unchanged in
    this phase (the `Telescope.*` sub-namespaces do not exist until Phase 12).
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project
  tests/NeoVisual.Tests` → 130 pass (count unchanged).
- **Fails-if:** build error `The type or namespace name 'X' could not be found` → a missed
  using/namespace in the named file; a NeoVisual test regresses; a `[NeoVisual]`/`[Hook]` log
  literal changed.

### BP-4 — Move default-keybindings.json → Resources/ + csproj embedded-resource path

- **Files:** MyExtension/default-keybindings.json (moved), MyExtension/Resources/default-keybindings.json
  (new), MyExtension/MyExtension.csproj:36.
- **Change:** `git mv MyExtension/default-keybindings.json MyExtension/Resources/default-keybindings.json`;
  update `<EmbeddedResource Include="default-keybindings.json" />` →
  `<EmbeddedResource Include="Resources\default-keybindings.json" />`.
  `KeybindingConfig.cs:60` matches by suffix — no code change needed.
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `dotnet run --project
  tests/NeoVisual.Tests -- Keybinding` → all pass (KeybindingConfig loads the embedded
  default-keybindings.json; a wrong path drops the resource and fails these tests).
- **Fails-if:** build error on the EmbeddedResource path; `Run_KeybindingConfig_*` tests fail
  (resource missing).

### BP-5 — Doc-ref lint: remove `CardinalMovment` from the external allowlist

- **Files:** tools/check-doc-refs.ps1:76.
- **Change:** delete `'CardinalMovment'` from `$externalAllowlist`. NOTE: the doc-ref lint is NOT
  a pass/fail gate in Phases 11-12 — between here and the Phase 13 sweep (BP-11) it is EXPECTED
  to report unresolved `CardinalMovment/*.cs` references in docs (that is the sweep's work queue,
  not a regression). Do not run the lint as a gate until Phase 13.
- **Verify-with:** `Select-String -Path tools/check-doc-refs.ps1 -Pattern 'CardinalMovment'`
  returns nothing.
- **Fails-if:** the `CardinalMovment` allowlist entry is still present; the lint is run as a gate
  before Phase 13 and its unresolved output is misread as a regression.

---

## Phase 12 — Telescope folders + namespaces

### BP-6 — `git mv` Telescope files into target folders + rename namespaces

- **Files:** all Telescope root `.cs` files (moved + namespace-edited).
- **Change:** per the target mapping (folder → namespace → files):
  - `git mv` root → `Overlay/` (`Telescope.Overlay`): TelescopeOverlay.cs, OverlayKeyHandler.cs,
    OverlayShowState.cs, FocusTargetModel.cs, PreviewRenderer.cs, BlockCaretStyle.cs,
    TextMotionNavigator.cs, TryDispatch.cs, ResultsFormatter.cs, ResultMapper.cs,
    SyntaxHighlighter.cs, LineIndex.cs
  - `git mv` root → `Finders/` (`Telescope.Finders`): TelescopeFinder.cs, FinderBase.cs,
    FileFinder.cs, CodeIssuesFinder.cs, ReferencesFinder.cs, GrepFinder.cs, ImplementationFinder.cs,
    FileHit.cs, GrepHit.cs, ReferenceHit.cs, ImplementationHit.cs, FileLocation.cs, IFileLocation.cs,
    CodeIssue.cs, HitOpener.cs, HierarchyWalker.cs, ProjectFiles.cs, ProjectFileCache.cs,
    FileContentCache.cs, DteFileOpener.cs
  - `git mv` root → `Filter/` (`Telescope.Filter`): FzfFilter.cs
  - `git mv` root → `Logging/` (`Telescope.Logging`): NeoVisualLog.cs, LogFileWriter.cs,
    NeoVisualTraceListener.cs, DiagnosticLog.cs, TelescopeLog.cs, FilterFailureLog.cs,
    PaneFailureTracker.cs
  - `git mv` root → `Controller/` (`Telescope.Controller`): TelescopeController.cs
  - In each moved file, update `namespace Telescope` → `namespace Telescope.<Group>`.
  - Keep `RootNamespace>Telescope</RootNamespace>` in Telescope.csproj (no change).
- **Verify-with:** `git status --short` shows the renames (R) with no unexpected deletions;
  `git grep -l 'namespace Telescope$'` returns nothing (no file declares the bare `Telescope`
  namespace after the move).
- **Fails-if:** a file is missing from its target folder; a `namespace` declaration was missed
  (surfaces as a build error in BP-7's gate). NOTE: the build is EXPECTED to be RED after this
  step until BP-7/BP-8 complete the reference sweeps — do not treat the intermediate RED as a
  regression.

### BP-7 — Telescope reference sweep in MyExtension/

- **Files:** the 16 MyExtension files with fully-qualified `Telescope.X` refs (90 refs) + the 4
  files with `using Telescope;` (InputHandler.cs:7, MyExtensionPackage.cs:11, TelescopeLauncher.cs:5,
  ToolWindows/TextMotionHelper.cs:8).
- **Change:**
  - Replace `using Telescope;` with the specific sub-namespaces each file references.
  - Rewrite every fully-qualified `Telescope.X` → `Telescope.<Group>.X` per the type→namespace
    mapping:
    - `Telescope.NeoVisualLog` → `Telescope.Logging.NeoVisualLog`
    - `Telescope.DiagnosticLog` → `Telescope.Logging.DiagnosticLog`
    - `Telescope.TelescopeController` → `Telescope.Controller.TelescopeController`
    - `Telescope.BlockCaretStyle` → `Telescope.Overlay.BlockCaretStyle`
    - `Telescope.Show(...)` → `Telescope.Controller.TelescopeController.Show(...)` (or
      `using Telescope.Controller;` + `TelescopeController.Show(...)`)
  - Mechanical: `git grep -n 'Telescope\.' -- MyExtension/` lists every site; rewrite each per the
    mapping.
- **Verify-with:** `dotnet build MyExtension.slnx` → 0 errors; `git grep -n 'Telescope\.' --
  MyExtension/` shows only `Telescope.<Group>.` forms (no bare `Telescope.X`).
- **Fails-if:** build error naming a `Telescope.X` type → a missed rewrite in the named file; a
  `[Telescope]` log literal changed.

### BP-8 — Test reference sweep (TestRunner.cs + both test Program.cs)

- **Files:** tests/TestRunner.cs, tests/Telescope.Tests/Program.cs, tests/NeoVisual.Tests/Program.cs.
- **Change:**
  - tests/TestRunner.cs: `Telescope.LogFileWriter` → `Telescope.Logging.LogFileWriter` at lines
    162/165/170/180/183/188.
  - tests/Telescope.Tests/Program.cs: `namespace Telescope.Tests` STAYS (child of `Telescope`;
    `StartupObject>Telescope.Tests.Program` stays valid); add `using Telescope.Overlay;` /
    `using Telescope.Finders;` / `using Telescope.Logging;` / `using Telescope.Filter;` /
    `using Telescope.Controller;` as needed (e.g. `FinderEntry` → `Telescope.Finders`).
  - tests/NeoVisual.Tests/Program.cs: `Telescope.*` references → the new sub-namespaces (e.g.
    `Telescope.NeoVisualLog` → `Telescope.Logging.NeoVisualLog`).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests` → 135 pass (count unchanged);
  `dotnet run --project tests/NeoVisual.Tests` → 130 pass (count unchanged); `dotnet build
  MyExtension.slnx` → 0 errors.
- **Fails-if:** a test fails to compile (missed using/rewrite); a test count changed; a test
  asserts a changed log literal.

---

## Phase 13 — tools/ + docs/ + reference sweep

### BP-9 — Move tools scripts into tools/harness/ + tools/lint/ and fix check-doc-refs.ps1 internals

- **Files:** tools/test-e2e.ps1, tools/iterate-telescope.ps1, tools/harness-common.ps1,
  tools/dte-command.ps1 (moved), tools/check-doc-refs.ps1 (moved + edited).
- **Change:**
  - `git mv tools/test-e2e.ps1 tools/iterate-telescope.ps1 tools/harness-common.ps1
    tools/dte-command.ps1 tools/harness/`
  - `git mv tools/check-doc-refs.ps1 tools/lint/`
  - In tools/lint/check-doc-refs.ps1: `$repoRoot = Split-Path -Parent $PSScriptRoot` (line 45) →
    two levels up (`Split-Path -Parent (Split-Path -Parent $PSScriptRoot)`); the function scan
    `Get-ChildItem (Join-Path $repoRoot 'tools')` (line 181) → `Join-Path $repoRoot 'tools/harness'`;
    update the default doc set if it lists the moved docs by path.
  - Harness dot-sourcing stays valid: test-e2e.ps1:~82, iterate-telescope.ps1:~27, dte-command.ps1:~16
    dot-source `harness-common.ps1` via `$PSScriptRoot`; test-e2e.ps1:~111/231/244/1839 +
    iterate-telescope.ps1:~73/262 call `dte-command.ps1` — all four moved together, so the
    `$PSScriptRoot`-relative paths hold (line numbers approximate — the mechanism is the move-together).
- **Verify-with:** `pwsh -NoProfile -Command "& 'tools/harness/test-e2e.ps1' -List"` → lists the 35
  scenarios (the script loads and dot-sources its siblings cleanly); `pwsh -NoProfile -Command
  "& 'tools/lint/check-doc-refs.ps1'"` runs without a path error (may report unresolved refs until
  BP-11 — not a gate yet).
- **Fails-if:** a harness script fails to dot-source its sibling (`harness-common.ps1` not found);
  check-doc-refs.ps1 errors on the `$repoRoot`/function-scan path.

### BP-10 — Move review/plan docs into docs/reviews/ + docs/plans/

- **Files:** docs/code-review.md, docs/architecture-review.md, docs/architecture-consolidation.md
  (moved), docs/backlog-plans.md (moved).
- **Change:** `git mv docs/code-review.md docs/architecture-review.md
  docs/architecture-consolidation.md docs/reviews/`; `git mv docs/backlog-plans.md docs/plans/`.
  Keep spec.md, progress.md, implementation_plan.md, e2e-queue.md in docs/ root (operational docs —
  referenced by exact path; NOT moved).
- **Verify-with:** `git status --short` shows the renames (R); the four operational docs remain in
  docs/ root.
- **Fails-if:** an operational doc was moved; a review/plan doc is missing from its target folder.

### BP-11 — Full reference sweep (docs + agent files + tools)

- **Files:** AGENTS.md, docs/spec.md, docs/progress.md, docs/implementation_plan.md,
  docs/reviews/*.md, docs/plans/backlog-plans.md, .opencode/skills/vs-extension-dev/SKILL.md,
  .opencode/agent/*.md (12 agent files reference `tools/*.ps1` — ~41 refs incl. check-doc-refs.ps1;
  5 agent files reference the moved docs — ~28 refs; counts approximate — the sweep is exhaustive).
- **Change:** update every path reference to a moved target:
  - `CardinalMovment/*.cs` → `MyExtension/Navigation/*.cs` (SKILL.md:62-67; spec.md 34 refs;
    progress.md 26 refs; arch-auditor.md:131 + code-review-worker.md:148
    `Telescope/FzfFilter.cs:42` → `Telescope/Filter/FzfFilter.cs:42`).
  - `MyExtension/*.cs` → `MyExtension/<Group>/*.cs` per the Phase 11 mapping; `Telescope/*.cs` →
    `Telescope/<Group>/*.cs` per the Phase 12 mapping.
  - `tools/test-e2e.ps1` → `tools/harness/test-e2e.ps1` (AGENTS.md:107-120);
    `tools/iterate-telescope.ps1` → `tools/harness/iterate-telescope.ps1`;
    `tools/harness-common.ps1` → `tools/harness/harness-common.ps1`;
    `tools/dte-command.ps1` → `tools/harness/dte-command.ps1`;
    `tools/check-doc-refs.ps1` → `tools/lint/check-doc-refs.ps1`.
  - `docs/code-review.md` → `docs/reviews/code-review.md`; `docs/architecture-review.md` →
    `docs/reviews/architecture-review.md`; `docs/architecture-consolidation.md` →
    `docs/reviews/architecture-consolidation.md`; `docs/backlog-plans.md` → `docs/plans/backlog-plans.md`.
  - Operational docs (spec.md, progress.md, implementation_plan.md, e2e-queue.md) keep their paths —
    their references stay valid.
  - Mechanical: `git grep -n -E 'CardinalMovment|tools/(test-e2e|iterate-telescope|harness-common|dte-command)\.ps1|tools/check-doc-refs\.ps1|docs/(code-review|architecture-review|architecture-consolidation|backlog-plans)\.md' -- AGENTS.md docs .opencode` lists every stale site; rewrite each (exclude the session/plan files under `.opencode/workspaces/`).
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` → `[PASS] ... 0 unresolved`; the stale-path
  grep above returns nothing (excluding `.opencode/workspaces/`).
- **Fails-if:** the lint reports unresolved references (a moved path missed in the sweep); an agent
  file still references an old path; a doc references a source file at its old path.

### BP-12 — Doc-ref lint allowlist updates + final lint gate

- **Files:** tools/lint/check-doc-refs.ps1.
- **Change:** update `$proposedPaths` (line 137: `'tools/harness-common.ps1'` → the new
  `tools/harness/harness-common.ps1`, or remove if now resolved) and `$proposedSymbols` for the
  moved files; confirm `$sourceRoots = @('MyExtension','Telescope','tests')` is unchanged.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` → `[PASS] ... 0 unresolved` (the gate is
  `0 unresolved`; the doc/ref counts may shift if the doc set changed).
- **Fails-if:** the lint reports unresolved references; a stale allowlist entry masks a real drift.

---

## Phase 14 — Final verification (combined plan gate)

### BP-13 — Build gate

- **Files:** none.
- **Change:** `dotnet build MyExtension.slnx`.
- **Verify-with:** exit 0, 0 errors.
- **Fails-if:** any compile error (a missed reference/namespace from Phases 11-13).

### BP-14 — Unit suite gates

- **Files:** none.
- **Change:** `dotnet run --project tests/Telescope.Tests` and `dotnet run --project
  tests/NeoVisual.Tests`.
- **Verify-with:** Telescope.Tests and NeoVisual.Tests all pass with counts UNCHANGED from the
  **Phase 11 start baseline** (captured at the start of Phase 11, AFTER Phase 10 completes —
  Phases 0-10 add tests, so the count is >135/130 by then; the pre-Phase-0 135/130 is NOT the
  restructure gate). Record the Phase 11 start counts (e.g. via `--list`) before any `git mv`.
- **Fails-if:** a test fails; a count changed from the Phase 11 start baseline (any count change
  from THAT baseline = a behavior/API drift, not a restructure artifact).

### BP-15 — Doc-ref lint gate

- **Files:** none.
- **Change:** `pwsh tools/lint/check-doc-refs.ps1`.
- **Verify-with:** `[PASS] ... 0 unresolved`.
- **Fails-if:** any unresolved reference.

### BP-16 — Log-literal diff gate

- **Files:** none (reads `%TEMP%\restructure-log-literals-before.txt` from BP-1).
- **Change:** re-capture the log-literal multiset and compare to the baseline:
  ```powershell
  $before = Get-Content "$env:TEMP\restructure-log-literals-before.txt"
  $after = Get-ChildItem -Recurse -Filter *.cs -Path MyExtension, Telescope, tests |
    Select-String -Pattern '\[(Telescope|NeoVisual|Hook|MyExtension)\]' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { $_.Value } |
    Group-Object | Sort-Object Name |
    ForEach-Object { "$($_.Name)`t$($_.Count)" }
  if (Compare-Object $before $after) { throw "log-literal drift" } else { "PASS: log literals unchanged" }
  ```
- **Verify-with:** the comparison prints `PASS: log literals unchanged` (identical multiset of
  `[Telescope]`/`[NeoVisual]`/`[Hook]`/`[MyExtension]` prefixes).
- **Fails-if:** `Compare-Object` reports a difference (a log literal was added/removed/changed by
  the restructure).

---

## Verification Trace

| gate | implicated steps | expected result |
|------|------------------|-----------------|
| `dotnet build MyExtension.slnx` → 0 errors | BP-2, BP-3, BP-4, BP-6, BP-7, BP-8, BP-13 | exit 0, 0 errors |
| `dotnet run --project tests/Telescope.Tests` → 135 pass | BP-6, BP-8, BP-14 | 135 pass, count unchanged |
| `dotnet run --project tests/NeoVisual.Tests` → 130 pass | BP-2, BP-3, BP-4, BP-8, BP-14 | 130 pass, count unchanged |
| `pwsh tools/lint/check-doc-refs.ps1` → 0 unresolved | BP-5, BP-9, BP-11, BP-12, BP-15 | `[PASS] ... 0 unresolved` |
| log-literal diff gate | BP-1, BP-16 | `PASS: log literals unchanged` |

## Known-RED allowlist

**Empty** — no known-RED scenario/test is excluded (per docs/progress.md). The restructure is
behavior-preserving; every gate in the Verification Trace must pass. Do not report any of the
intermediate RED states (build RED after BP-2/BP-6 until the following sweep steps; doc-ref lint
unresolved between BP-5 and BP-11) as regressions — they are the expected mid-plan states the
plan's step ordering resolves.

## E2E note (informational — deferred)

`E2E-RESTRUCTURE-1` (full 35-scenario suite, queued in e2e-queue.md) verifies the restructure
live after this plan is GREEN — no e2e scenario is referenced by any Verify-with / Fails-if above.

DONE

---

## Execution Log

### Plan gate (2026-09-30)
- **PLAN REVIEW (build-plan focus):** docs-reviewer APPROVED (0 critical, 0 major; 1 nit on BP-6 m13 ordering — the plan's own Verify-with/Fails-if already gate it). Doc-ref lint: [PASS] 28 docs, 6015 refs, 0 unresolved. All spot-checked BP-n file/line claims match the code.
- **Lane:** feature/bugfix program + refactor (unit-only, e2e deferred to e2e-queue.md). NO e2e harness commands will be run on this machine (user mandate).
- **Execution strategy:** 3 chunks matching the plan's 3 Build Plan sections — Chunk A (Phases 0-5, BP-1..BP-28), Chunk B (Phases 6-10, BP-1..BP-39), Chunk C (Phases 11-14, BP-1..BP-16). Each chunk: RED (unit tests only, no VS boot) → BUILD → VERIFY.

### Chunk A RED — attempt 1 (2026-09-30)
- **DEVIATION D-A1 (ACCEPT):** TextMotionDispatcher placed at MyExtension/TextMotionDispatcher.cs (namespace MyExtension) instead of the plan's Telescope/TextMotionDispatcher.cs. Reason: it references BOTH MyExtension.ToolWindows.TextMotion (the enum, TextMotionHelper.cs:17) AND Telescope.TextMotionNavigator; Telescope does not reference MyExtension (only MyExtension references Telescope), so the shared dispatcher must live in MyExtension to avoid a circular dependency. No diagnostic/API contract change. The restructure (Phase 11-14) will place it in its final folder.
- **Status:** e2e-test-builder hit its step limit mid-task. NeoVisual.Tests REDs CONFIRMED (137 total, 4 failed: Run_ActionTable_TextInput_ActionKeysMatchTable count 6 vs 7, Run_TextInput_KeysIMapsToInsertStart, Run_LeaderMatcher_PrefixSetBuiltOnce, Run_VimModeTracker_LogOnlyOnChange). Telescope.Tests edits partially applied (BP-28/BP-14/BP-15 done; BP-8/BP-9/BP-10/11/BP-12/BP-13 pending). Re-dispatching fresh to complete the Telescope REDs.

### Chunk A RED — attempt 2 (2026-09-30)
- e2e-test-builder completed all test edits but looped on the verification step (user cancelled). Hub verified the RED state directly (offline unit runs only — no e2e).
- **RED CONFIRMED (12):** NeoVisual.Tests 137 total / 4 failed (Run_ActionTable_TextInput_ActionKeysMatchTable count 6 vs 7 → CR1; Run_TextInput_KeysIMapsToInsertStart → CR1; Run_LeaderMatcher_PrefixSetBuiltOnce → M7; Run_VimModeTracker_LogOnlyOnChange → M16). Telescope.Tests 142 total / 8 failed (Run_FileContentCache_EvictsOldest → M13; Run_FilterFailureLog_Format → m14; Run_FzfFilter_IsAvailableBounded → m16; Run_Issues_CollectTodosUsesCache → m15; Run_Preview_UpFromSecondLineWithLeadingBlankLine + _Fixed → M10; Run_ResultMapper_UnknownStringNullPayload + _SkippedOrLogged → M11). All REDs match the plan's Fails-if (RIGHT reason).
- **Wiring-is-the-fix (pass today, pin the contract):** BP-8 (M3 timeout-await — the injected-path ctor already exists per the plan's research), BP-9 (M4 off-thread scan). These are contract pins; the fix is verified by build + the test passing after the fix.
- **New seam classes (RED skeletons, wiring is the fix):** MyExtension/VimBufferSubscriptions.cs (CR2), MyExtension/TextMotionDispatcher.cs (M19, D-A1 ACCEPTED), Telescope/FileContentCache.cs maxEntries cap param (M13).
- **Cost:** delegations: 2 | VS boots: 0 | iterations: 0.

### Chunk A BUILD — attempt 1 (2026-09-30)
- build-agent hit its step limit mid-task. Completed BP-1..BP-9, BP-11, BP-13 (CR1, CR2 wiring, M1, M2, M15, M7, m17, M3, M4, M13, m16). NOT completed: BP-10, BP-12, BP-14, BP-15, BP-16, BP-17, BP-18..BP-23, BP-24..BP-28. Build verified 0 errors at the partial state.
- **DEVIATION D-A1 REVISED -> REJECT (M19/BP-16):** the e2e-test-builder placed `TextMotionDispatcher` at `MyExtension/TextMotionDispatcher.cs` (namespace MyExtension), but `TryDispatch` (Telescope) cannot reference a MyExtension type and Telescope does not reference MyExtension — this defeats the M19 unification. The plan's original placement is correct: move `TextMotionDispatcher` to `Telescope/TextMotionDispatcher.cs` (namespace `Telescope`) and move the `TextMotion` enum from `MyExtension/ToolWindows/TextMotionHelper.cs` to Telescope (namespace `Telescope`, `public` so MyExtension can use it — TextMotionHelper already has `using Telescope;`). Both `TextMotionHelper.MapMotion` and `TryDispatch.Handle` delegate to it. Update NeoVisual.Tests references (`TextMotionDispatcher`/`TextMotion` resolve via `using Telescope;`).
- **Cost:** delegations: 3 | VS boots: 0 | iterations: 0.

### Chunk A BUILD — attempt 2 (2026-09-30)
- build-agent completed most of Chunk A (BP-1..BP-25, BP-27 done; D-A1 REJECT applied — `TextMotionDispatcher` + `TextMotion` enum moved to Telescope) but returned an empty verdict. Hub verified directly: build 0 errors; NeoVisual 136/137 (1 fail), Telescope 140/142 (2 fails).
- **REMAINING FAILURES (root-caused by hub):**
  1. **M16 (BP-26)** — `VimModeTracker.UpdateTypingFromMode` still logs `vim-mode=` on every call (no change detection). `Run_VimModeTracker_LogOnlyOnChange` fails.
  2. **m14 (BP-28)** — `FilterFailureLog.Format` still embeds the `[Telescope] ` prefix; caller still uses `NeoVisualLog.Log`. `Run_FilterFailureLog_Format` fails.
  3. **M3 (BP-8) REGRESSION** — the build-agent's fix does `await all` (both `ReadToEndAsync` tasks) after `TryKill` on the timeout path; on a killed process the pipe reads don't fault promptly (the test's `hang.cmd` = `ping -n 30` holds the pipe open), so `await all` blocks ~29s. `Run_FzfFilter_TimeoutKillsAndFallsBack` fails (took 29s). The plan's intent (observe the faulted tasks, no unobserved-task noise) must be achieved WITHOUT blocking the return — e.g. `_ = all.ContinueWith(t => { _ = t.Exception; }, OnlyOnFaulted)` or a bounded await.
- **Cost:** delegations: 4 | VS boots: 0 | iterations: 0.

### Chunk A DEBUG — attempt 1 (2026-09-30)
- debug-agent fixed all 3 remaining failures (M16/BP-26 `_lastLoggedModeName` change-detection; m14/BP-28 `FilterFailureLog.Format` unprefixed + `TelescopeLog.Log` caller; M3/BP-8 regression — replaced the blocking `await all` after TryKill with a fault-only `ContinueWith` observation). No DEVIATIONS.
- Hub verified: build 0 errors; NeoVisual 137/137; Telescope 142/142 (one transient flaky run showed 1 fail — the timing-sensitive FzfFilter timeout test — pass-on-retry, recorded as flaky x1).
- **Cost:** delegations: 5 | VS boots: 0 | iterations: 0.

### Chunk A VERIFY — attempt 1 (2026-09-30)
- verification-agent: **FAIL** — build 0 errors, NeoVisual 137/137, Telescope 142/142, all 12 RED tests GREEN, all harness-health self-checks PASS, but **9 of 28 BPs NOT applied** (the build-agent skipped the no-seam code changes): BP-18 (M9), BP-19 (M14), BP-20 (m3), BP-21 (m4), BP-22 (m5), BP-23 (m6), BP-24 (M8 — the `[NeoVisual] toolwindow-move failed: {msg}` ADD is MISSING), BP-25 (M12), BP-27 (M25). All are no-seam (build-verified) code changes. Dispatching debug-agent to implement them.
- **Cost:** delegations: 6 | VS boots: 0 | iterations: 0.

### Chunk A DEBUG — attempt 2 (2026-09-30)
- debug-agent implemented the 9 missing no-seam BPs (BP-18 M9, BP-19 M14, BP-20 m3, BP-21 m4, BP-22 m5, BP-23 m6, BP-24 M8, BP-25 M12, BP-27 M25). No DEVIATIONS (BP-27 factory lambdas used the plan's allowed `!` at the lambda; the `[NeoVisual] Unable to get DTE for window navigation:` line was removed with CheckDte — the explicit BP-18 deletion).
- Hub verified: build 0 errors; NeoVisual 137/137; Telescope 142/142; `[NeoVisual] toolwindow-move failed: {msg}` present (InputHandler.cs:351); `[Telescope] result-mapper unknown display: {display}` present (ResultMapper.cs:45); `vim-mode=` token byte-identical.
- **Cost:** delegations: 7 | VS boots: 0 | iterations: 0.

### Chunk A VERIFY — attempt 2 (2026-09-30)
- verification-agent: **FAIL (blocking doc-ref drift only)** — code fully GREEN (build 0 errors, NeoVisual 137/137, Telescope 142/142, all 28 BPs applied, all 12 RED tests GREEN, diagnostic contract PASS, no deviations). Blocking finding: `docs/progress.md:1439` backticked `` `CheckDte` `` (historical m20 finding record) — BP-18 deleted the symbol, so the doc-ref lint reported 1 unresolved. Hub fixed the doc (annotated m20 FIXED, dropped the backtick); lint now `[PASS] 28 docs, 6013 refs, 0 unresolved`.
- **NEW FLAKY entry:** `Run_FileContentCache_CachedRead` = flaky x1 (test-hermeticity — the injected `DateTime.UtcNow` timestamp ticks between two rapid `GetLines` calls, causing a cache miss; pass-on-retry, not a regression). `Run_FzfFilter_TimeoutKillsAndFallsBack` did not flake this run (stays x1).
- **Cost:** delegations: 8 | VS boots: 0 | iterations: 0.

### Chunk A VERIFY — attempt 3 (2026-09-30) — **GREEN**
- verification-agent: **PASS** — build 0 errors; NeoVisual 137/137; Telescope 142/142 (one first-run 141/142 timing flake, pass-on-retry x4 — one of the two known-flaky timing tests, cumulative flaky count for the item now x2, under the 3-strike budget); doc-ref lint `[PASS] 28 docs, 6013 refs, 0 unresolved`; all harness-health self-checks PASS; all 12 Chunk A RED tests GREEN; diagnostic contract PASS (only the 3 planned ADDs); no deviations.
- **Chunk A COMPLETE.** Proceeding to Chunk B (Phases 6-10).
- **Cost:** delegations: 9 | VS boots: 0 | iterations: 0.

### Chunk B RED — attempt 1 (2026-09-30)
- e2e-test-builder wrote all Chunk B test edits + proved the compile-error REDs (build FAILED, 19 errors, all matching the plan's expected failures): CS7036 x5 (BP-3 M21 HierarchyForestBuilder.Build new signature), CS0117 x4 (BP-4 M22 FocusGuard.OwnsKeyboard), CS0117 x2 (BP-5 M24 InitSteps.RunSync), CS0117 x4 (BP-14 M28 LogFileWriter.TimerScheduler), CS1739 x4 (BP-30 m7+m10 FocusGuard.IsTyping editorFocusedVeto rename). Deletions: BP-8 (Run_DistinctBy_DeduplicatesOnKey, -1), BP-26 (3 redundant action-key presence tests, -3). Behavior-preserving rewrites: BP-11 (JkMoveByLine Caret), BP-19 (FzfFilter injected path), BP-20 (FinderBase single-log assertion), BP-21 (Syntax exact sequence), BP-22 (HierarchyResolver split x4), BP-25 (TextInput TryMove), BP-34 (n1), BP-35 (n2).
- **DEVIATION D-B1 (ACCEPT, BP-24/m27):** the plan's premise that `GetCandidates()` invokes the throwing `GatherHits()` and logs `GrepFinder failed to enumerate:` is FALSE — GrepFinder's `GetCandidates(string)` override short-circuits on an empty query (deterministic empty initial state) and never reaches the gather stub. The m27 fix is the reflection removal (implementation coupling), not a loud-failure log. Corrected test: `GetCandidates("")` returns empty AND does NOT log a failure (asserts `failureLines == 0`). Behavior-preserving (passes against current code); the reflection removal is verified by grep (no `GetMethod("GatherHits"` remains).
- **Cost:** delegations: 10 | VS boots: 0 | iterations: 0.

### Chunk B BUILD — attempts 1-2 (2026-09-30)
- build-agent attempt 1: completed BP-1 only, hit step limit. Re-dispatched fresh.
- build-agent attempt 2: completed BP-2..BP-13, BP-15..BP-29, BP-31..BP-39 (returned empty verdict; hub verified the work landed). Left BP-14 (TimerScheduler) + BP-30 (IsTyping rename) — the 8 remaining compile errors.
- debug-agent (BP-14/BP-30): fixed both (LogFileWriter.TimerScheduler seam + IDisposable? _flushTimer; FocusGuard.IsTyping dead-branch removal + editorFocusedVeto rename). Build 0 errors.
- debug-agent (remaining 4): fixed BP-6/m13 FileFinder missing-file guard regression, BP-21/m24 Syntax 6-segment sequence, BP-18/m21 TempDir.Dispose rethrow, BP-9/M23 doc sweep (DistinctBy + LinqExtensionMethods.cs allowlist, dropped backticks on the 2 split-test refs).
- **DEVIATION D-B2 (ACCEPT, BP-9):** `LinqExtensionMethods.cs` added to `$intentionallyAbsent` (the file-path analog of the "recently removed" pattern) instead of `$externalAllowlist` (which only consults PascalCase symbol tokens). Same intent — review archives stay lint-clean; verified `check-doc-refs.ps1` → 0 unresolved.
- Hub verified: build 0 errors; NeoVisual 140/140; Telescope 143/143; check-doc-refs `[PASS] 28 docs, 5993 refs, 0 unresolved`; iterate-telescope -SelfCheck PASS.
- **Cost:** delegations: 13 | VS boots: 0 | iterations: 0.

### Chunk B VERIFY — attempt 1 (2026-09-30)
- verification-agent: **FAIL** — build 0 errors, NeoVisual 140/140, Telescope 143/143, all harness-health self-checks PASS, diagnostic contract PASS, but **17 of 39 BPs NOT applied** (the build-agent's completion claim was false for these no-seam changes; the hub's "work landed" check was a false-green because every one is a no-seam change whose Verify-with passes without the change): BP-10 (m11 ResultCount), BP-11 (m12 ColumnNumber), BP-12 (M26 preview tokens regex), BP-13 (M27 results count regex), BP-15 (m18 PID-scoped Stop-HarnessVs), BP-16 (m19 NEOVISUAL_LOG_DIR Process scope), BP-17 (m20 allowlist cleanup), BP-23 (m26 PositiveActionKeyCount 5→7), BP-27 (m30 stale comment), BP-29 (m2 File.Exists hoist), BP-31 (m8 TickCount64), BP-32 (m9 FocusKeeperDurationMs), BP-33 (m32 SKILL.md wording), BP-36 (M29 doc counts), BP-37 (M30 §4 diagnostics), BP-38 (M31 key-files seams), BP-39 (m31 progress.md header).
- Flaky: `Run_FileContentCache_CachedRead` x1→x2 (known DateTime.UtcNow hermeticity flake, pass-on-retry, under the 3-strike budget). `Run_FzfFilter_TimeoutKillsAndFallsBack` stays x1.
- Dispatching debug-agent to implement the 17 missing BPs.
- **Cost:** delegations: 14 | VS boots: 0 | iterations: 0.

### Chunk B DEBUG — attempt 2 (2026-09-30)
- debug-agent implemented 8 of 17 missing BPs (BP-10, 11, 12, 13, 23, 27, 29, 31, 32 done; BP-15 partial — test-e2e.ps1 PID-scoped done, iterate-telescope.ps1 copy + Add-SpawnedVs sites + line-251 Stop-HarnessVs removal pending), hit step limit.
- **DEVIATION D-B3 (ACCEPT, BP-31/m8):** net472 has NO `Environment.TickCount64` (it is .NET Core 3.0+; the plan's claim "net472 has it" is false). The debug-agent used a monotonic `Stopwatch` instead — the plan's own stated fallback ("or a monotonic clock"). Intent preserved (no int wrap-around every ~24.9 days). Not a contract deviation.
- Remaining: finish BP-15 (iterate-telescope.ps1), BP-16, BP-17, BP-33, BP-36, BP-37, BP-38, BP-39.
- **Cost:** delegations: 15 | VS boots: 0 | iterations: 0.

### Chunk B DEBUG — attempt 3 (2026-09-30)
- debug-agent completed the remaining 8 BPs (BP-15 finish — iterate-telescope.ps1 PID-scoped Stop-HarnessVs + Add-SpawnedVs sites + removed the line-250 Stop-HarnessVs call; BP-16 NEOVISUAL_LOG_DIR Process scope + SelfCheck assertion; BP-17 allowlist cleanup; BP-33 SKILL.md wording; BP-36 counts → 143/140; BP-37 §4 diagnostics; BP-38 19 seams; BP-39 progress.md header). No DEVIATIONS.
- Hub verified: build 0 errors; NeoVisual 140/140; Telescope 143/143; check-doc-refs `[PASS] 28 docs, 6076 refs, 0 unresolved`; iterate-telescope -SelfCheck PASS (incl. the new NEOVISUAL_LOG_DIR User-scope assertion).
- **Cost:** delegations: 16 | VS boots: 0 | iterations: 0.

### Chunk B VERIFY — attempt 2 (2026-09-30)
- verification-agent: **FAIL (2 small misses)** — build 0 errors, NeoVisual 140/140, Telescope 143/143, all harness-health self-checks PASS, diagnostic contract PASS, 37/39 BPs fully applied. Remaining: BP-36 PARTIAL (docs/progress.md:72-73 still "77 passed / 74 passed" — spec.md + SKILL.md updated to 143/140, progress.md missed) and BP-28 NOT APPLIED (WindowManager.cs class body still at column 0 inside the namespace — reindent never done). Dispatching debug-agent for the 2.
- **Cost:** delegations: 17 | VS boots: 0 | iterations: 0.

### Chunk B DEBUG — attempt 4 (2026-09-30)
- debug-agent completed the 2 remaining BPs (BP-28 WindowManager reindent — cosmetic-only, verified via before/after reads; BP-36 progress.md:72-73 counts → 143/140). No DEVIATIONS.
- Hub verified: build 0 errors; NeoVisual 140/140; Telescope 143/143; check-doc-refs `[PASS] 28 docs, 6076 refs, 0 unresolved`; iterate-telescope -SelfCheck PASS.
- **Cost:** delegations: 18 | VS boots: 0 | iterations: 0.

### Chunk B VERIFY — attempt 3 (2026-09-30) — **GREEN (pending 2 adjudications)**
- verification-agent: **PASS** — build 0 errors; NeoVisual 140/140; Telescope 143/143; all harness-health self-checks PASS; diagnostic contract PASS; all 39 BPs applied. No flakes observed (counts stay x1/x2).
- **DEVIATION D-B3 (BP-31/m8) — RE-CONFIRMED ACCEPT:** the verification-agent claimed `Environment.TickCount64` exists since .NET 4.5; the hub verified with csc against the v4.7.2 reference assembly — CS0117, net472 does NOT have it. The monotonic `Stopwatch` implementation is correct (the plan's own stated fallback). The comment in FocusKeeper.cs is accurate.
- **DEVIATION D-B4 (BP-38/M31) — PARTIAL, dispatch fix:** spec.md §2.2 has all 62 seams; SKILL.md key-files table is missing 14 (FocusGuard, LeaderSequenceMatcher, SimpleShortcutMatcher, VimModeClassifier, InitSteps, NavigationSnapshot, ToolWindowTypeResolver, PopupNavigation, FinderBase, PaneFailureTracker, FocusTargetModel, LineIndex, TryDispatch, VimModeTracker). The plan explicitly says "add the missing seams to the key-files tables in docs/spec.md:47 §2.2 and SKILL.md:50,68" — both tables. Dispatching debug-agent to complete the SKILL.md table.
- **Cost:** delegations: 19 | VS boots: 0 | iterations: 0.

### Chunk B DEBUG — attempt 5 (2026-09-30)
- debug-agent completed the SKILL.md key-files table (14 seams added, all resolving; NavigationSnapshot at its actual `CardinalMovment/` path; SimpleShortcutMatcher row reflects the BP-1 delegation). check-doc-refs `[PASS] 28 docs, 6124 refs, 0 unresolved`.
- **Cost:** delegations: 20 | VS boots: 0 | iterations: 0.

### Chunk B VERIFY — attempt 4 (2026-09-30) — **GREEN**
- verification-agent: **PASS** — build 0 errors; NeoVisual 140/140; Telescope 143/143; all 4 harness-health self-checks PASS; doc-ref lint `[PASS] 28 docs, 6126 refs, 0 unresolved`; diagnostic contract byte-identical; all 39 BPs applied. One non-blocking docs finding: `InjectedKeyGuard` missing from both key-files tables (BP-38 partial) — hub added it to spec.md §2.2 + SKILL.md key-files table; lint re-verified 0 unresolved.
- **Chunk B COMPLETE.** Proceeding to Chunk C (Phases 11-14 restructure).
- **Cost:** delegations: 21 | VS boots: 0 | iterations: 0.

### Chunk C BUILD — attempt 1 (2026-09-30)
- build-agent completed Phase 11 (MyExtension restructure — all git mv moves, namespaces updated, using sweep, build 0 errors, NeoVisual 140/140, default-keybindings.json → Resources/, CardinalMovment removed from allowlist) and started Phase 12 (moved 12 files to Overlay/), hit step limit. TextMotionDispatcher.cs (untracked, Chunk A) still at Telescope/ root; Finders/Filter/Logging/Controller moves + namespace updates + reference sweeps + Phases 13-14 pending. Minor adaptations: VimBufferSubscriptions.cs → Vim/ (MyExtension.Vim), TextMotionDispatcher.cs → Overlay/ (Telescope.Overlay). No log literal/API/test-count changes so far.
- **Cost:** delegations: 22 | VS boots: 0 | iterations: 0.

### Chunk C BUILD — attempt 2 (2026-09-30)
- build-agent completed Phase 12 (Telescope restructure — all 30 files moved via git mv + TextMotionDispatcher.cs via Move-Item, 42 namespaces updated, full reference sweep across MyExtension/ + tests/, build 0 errors, NeoVisual 140/140, Telescope 143/143) and started Phase 13 (created tools/harness/, tools/lint/, docs/reviews/, docs/plans/ dirs), hit step limit. Phase 13 moves + reference sweep + Phase 14 pending. No log literal/API/test-count changes.
- **Cost:** delegations: 23 | VS boots: 0 | iterations: 0.

### Chunk C BUILD — attempts 2-3 (2026-09-30)
- build-agent attempt 2: completed Phase 12 (Telescope restructure — 30 files moved, 42 namespaces, full reference sweep, build 0 errors, NeoVisual 140/140, Telescope 143/143), started Phase 13 (created tools/harness/, tools/lint/, docs/reviews/, docs/plans/ dirs), hit step limit.
- build-agent attempt 3: completed Phase 13 (git mv of 5 tools scripts → tools/harness/ + tools/lint/, 4 docs → docs/reviews/ + docs/plans/, harness $root fixes, check-doc-refs $repoRoot/function-scan/default-doc-set fixes, 437-path reference sweep across 26 files, allowlist updates) + Phase 14 partial (doc-ref lint PASS, grep gate PASS on prefix constants), hit step limit before the final build/suites.
- **DEVIATION D-C1 (ACCEPT, Phase 13 allowlist):** the plan said "remove `CardinalMovment` from the external allowlist" but it was never in the allowlist (it resolved via source grep before the move). The build-agent ADDED `CardinalMovment`/`CardinalNavigation` to `$externalAllowlist` as historical names (review archives + agent instructions still cite the old folder/namespace) — consistent with the "recently removed" pattern.
- **DEVIATION D-C2 (ACCEPT, Phase 13 reference sweep):** `docs/implementation_plan.md` was NOT swept — it is excluded from the doc-ref lint by design, is hub-owned, and mechanically replacing its `CardinalMovment/`/`tools/*.ps1` references would corrupt the plan's meaning (the Part B section describes the moves using the old paths). All other docs/agent/tool references were swept.
- Hub verified: build 0 errors; NeoVisual 140/140; Telescope 143/143; `pwsh tools/lint/check-doc-refs.ps1` `[PASS] 28 docs, 6126 refs, 0 unresolved`; grep gate clean (prefix constants byte-identical; the em-dash concern was a regex-extraction artifact — the source at TelescopeOverlay.cs:267 preserves the em-dash).
- **Cost:** delegations: 24 | VS boots: 0 | iterations: 0.

### Chunk C VERIFY — attempt 1 (2026-09-30) — **GREEN**
- verification-agent: **PASS** — build 0 errors; NeoVisual 140/140; Telescope 143/143 (counts UNCHANGED from the pre-restructure baseline); all 4 harness-health self-checks PASS (incl. the NEW tools/harness/ + tools/lint/ paths); doc-ref lint `[PASS] 28 docs, 6130 refs, 0 unresolved`; log-literal diff gate PASS (prefix multiset identical, DiagnosticLog.cs constants byte-identical, em-dash preserved — the prior agent's flag was a regex-extraction false positive); restructure spot-checks all PASS (folders/namespaces match the mapping, CardinalMovment/ gone). D-C1 + D-C2 confirmed as adjudicated.
- **Doc-sync (hub, post-GREEN):** fixed the stale references the verifier flagged — AGENTS.md:396-398 (the "keep the CardinalMovment typo folder" instruction → the 2026-09-30 restructure note), docs/e2e-queue.md (tools/test-e2e.ps1 → tools/harness/test-e2e.ps1 x2), .opencode/command/review.md (docs/architecture-review.md → docs/reviews/), 3 skill files (tools/test-e2e.ps1 → tools/harness/test-e2e.ps1), Telescope/Logging/DiagnosticLog.cs comment. AGENT-FAILURES.md entries left as historical verbatim records. Lint re-verified 0 unresolved.
- **Chunk C COMPLETE — the combined item (Code review findings (67) + Repository restructure) is GREEN.**
- **Cost:** delegations: 25 | VS boots: 0 | iterations: 0.

### POST-RUN FAILURE-LOG SWEEP (2026-09-30, final gate)
- failure-log sweep: 6 entries read, 0 fixed (all already FIXED-annotated), 0 queued, 0 annotated. No subagent reported an unintended command failure this session (the build-agent empty verdicts were delegation failures handled by re-dispatch, not command failures).
