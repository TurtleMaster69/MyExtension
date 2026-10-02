# Plan — Code-Review Fixes (72 findings, 2026-10-02)

> **Lane: bugfix (e2e enabled).**
> Source: `docs/reviews/code-review.md` (2026-10-02, whole-repo post-fix review of the
> 51-findings plan, GREEN 2026-10-02). **72 findings: 2 critical, 8 major, 44 minor, 18 nit.**
> The user requested fixes for ALL findings — including minors and nits.
>
> **E2E ENABLED (user instruction 2026-10-02):** this machine CAN boot the VS Experimental
> Instance. The e2e gates E2E-CR72-1..10 (Part B / `docs/e2e-queue.md`) are RUN at VERIFY,
> not deferred. The affected scenarios run during the loop; the full 35-scenario suite is the
> item's final gate.
>
> **Research:** 1 whole-repo `trailmark-recon` digest (1899 nodes, 773 proxies = 40.7%,
> 0 entrypoints, 3286 edges) — seam callers/callees + proxy traps delivered; confirmed
> `NavigationSettings.Invalidate` is genuinely dead (0 real + proxy callers). 1 `arch-auditor`
> pass — 67/72 fix directions CONFIRMED against current code, **5 NEEDS-CORRECTION**
> (N1/N2 fix polarity, N37 detail, N55 line ref, N57 unused-using scope), plus a
> cross-cutting risk map + unit-seam classification for all 72. `feature-researcher` SKIPPED —
> internal bugfix, no LazyVim reference needed (prior-plan precedent).
>
> **Ground truth:** repo GREEN (51-findings plan, commit `b472104`). Unit suites:
> Telescope.Tests 153, NeoVisual.Tests 163 (all passing). No known-RED e2e scenario remains
> (all 35 GREEN).
>
> e2e scenarios are RUN on this machine (e2e enabled). Every fix is verified by unit tests
> (where a hermetic seam exists) + `dotnet build` + the existing unit suites, AND by the
> affected e2e scenarios (Part B / `docs/e2e-queue.md`, E2E-CR72-1..10) at VERIFY; the full
> 35-scenario suite is the item's final gate.

## Goal

Fix all 72 code-review findings (2 critical, 8 major, 44 minor, 18 nit) in dependency
order — the 2 critical tautological FocusGuard tests first (the false-confidence hole on
the security-critical leak guard), then the functional majors (block-caret deactivation,
Vim buffer-subscription lifecycle, navigation fault isolation, harness gate hardening,
lint RED), then the minor/nit clusters (controllers, input/hook, Telescope
overlay/finders/filter/logging, harness, docs) — with every behavior change proven by a
RED unit test (or, where no hermetic seam exists, by build + existing suites + the
e2e gate at VERIFY (E2E-CR72-1..10)).

## Approach

The research passes produced a verified verdict + fix direction + unit-test seam for
every finding, plus a cross-cutting risk map. The plan is organized into 8 phases in
dependency order (Phases 0-7). Cross-cutting constraints:

- **Log-line-as-contract:** several fixes touch lines the e2e harness asserts on
  (`block-caret active=`, `window rect unavailable` (n19), `filter failed:`,
  `editor-view-opened file=`, `text-motion key=... caret=...`, `[Hook]` prefix,
  `[Telescope]` prefix, `prompt-motion key=... caret=...`). Any fix that changes a
  signature or a log site MUST preserve the emitted token byte-identical (except where
  the finding is that the line LIES — N4 `block-caret active=False` — in which case the
  fix makes the line truthful without changing its format).
- **Proxy traps:** cross-class calls land on `proxy.unresolved:<Type>.<Member>`; a bare
  `callers_of` returning 0 is SUSPECT, not dead code. `NavigationSettings.Invalidate`
  (N6) is genuinely dead (0 real + proxy callers — verified). `BlockCaretAdornment.Active`
  is a settable PROPERTY (not a method); `WindowNavigator.SelectTarget` is really
  `WindowNavigationEngine.SelectTarget`.
- **Fix-direction corrections from research (fold into the phases):**
  1. **N1/N2** — the review's suggested `Assert.False(guard(...))` is WRONG: the guard
     returns TRUE for input-mode / zero-action-key inputs (input mode owns the keyboard,
     FocusGuard.cs:41-42). Corrected: `Assert.True(FocusGuard.ShouldRouteToolWindowKey(...))`
     — the guard routes (consumes the key = blocks it from the editor); the
     `!isInputMode` / `actionKeyCount > 0` gates are caller-side (InputHandler.cs:313,
     IsKeyOfInterest:449), not part of this assertion.
  2. **N37** — the review's wording ("only GrepFinder routes through the shared cache")
     is imprecise: CodeIssuesFinder (:102) AND GrepFinder (:103) already share the cache;
     **FileFinder** (registered via the public ctor at :101, which takes no cache) is the
     one that re-walks. Fix: add a public ctor taking the cache + register FileFinder with
     the shared cache.
  3. **N55** — the review's line ref `code-review.md:66` is now N50; R50 is from the
     superseded 98-findings review. The docs fix references the correct finding.
  4. **N57** — `GlobalKeyboardHook.cs:6` `using System.Diagnostics;` IS used
     (Process :56/:170). Remove the unused usings from `MyExtensionPackage.cs:8` and
     `TelescopeLauncher.cs:5` ONLY.
- **No-seam findings** (VS/WPF/harness/COM-coupled, no hermetic unit surface): N4, N5,
  N7, N9, N10, N11, N12, N13, N14, N15, N19, N20, N22, N23, N24, N25, N28, N29, N32
  (UI-thread half), N34, N35, N37 (wiring half), N38 (UI-thread half), N42, N43, N44
  (wiring half), N52, N53, N54, N55, N57, N58, N59, N60, N65, N68, N69, N71, N72. For
  these the plan states the honest verification (build + existing suites + e2e at VERIFY)
  and, where possible, extracts a small pure seam to make part of the behavior
  unit-testable.
- **net472 constraint:** N45's `QuoteArg` fix must stay net472-compatible — no
  `ProcessStartInfo.ArgumentList` (not available on net472).

---

# Part A — Code-review fixes (Phases 0-7)

## Phase 0 — Critical test-quality + FocusGuard tests (N1, N2, N46, N47, N48, N49, N50, N8)

The review's #1 recommendation: fix the two tautological FocusGuard tests first — they
are the guard tests for the user-reported action-key leak, and a regression would pass
the suite silently. This phase also clears the other test-quality defects (duplicate /
vacuous / reflection-mutating tests) and the clock-tick flake.

- **N1** (critical, CONFIRMED + fix NEEDS-CORRECTION) — `Run_FocusGuard_InputModeBlocksActionKeys`
  (tests/NeoVisual.Tests/Program.cs:1038-1047) asserts a constant-false composite
  (`... && !isInputMode` with `isInputMode=true`), so `Assert.False` always passes
  regardless of `FocusGuard`. **Fix (corrected):** assert the guard's own return value
  directly — `Assert.True(FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true,
  editorFocused: false, isInputMode: true, isTextInputSurface: false,
  textInputSurfaceFocused: false))` — and drop the constant-false `&& !isInputMode` term.
  The guard returns TRUE (input mode owns the keyboard → routes → blocks the key from the
  editor). **Unit seam (NeoVisual.Tests):** `Run_FocusGuard_*` (pure, tested). RED proof:
  the corrected test FAILS if the guard regresses to not route in input mode (prove by
  temporarily mutating `ShouldRouteToolWindowKey` to return false for input mode — the
  OLD tautological test still passes, the NEW test fails).
- **N2** (critical, CONFIRMED + fix NEEDS-CORRECTION) — `Run_FocusGuard_ZeroActionKeysBlocks`
  (Program.cs:1049-1060) asserts a constant-false composite (`... && actionKeyCount > 0`
  with `actionKeyCount=0`). **Fix (corrected):** `Assert.True(FocusGuard.ShouldRouteToolWindowKey(
  isToolWindow: true, editorFocused: false, isInputMode: false, isTextInputSurface: false,
  textInputSurfaceFocused: false))` with a comment that the action-key-count gate lives in
  the caller (the guard routes a tree-focused tool window regardless of action-key count).
  **Unit seam (NeoVisual.Tests):** same RED-proof as N1 (mutation of the guard).
- **N46** (minor, CONFIRMED) — FocusGuard tests (Program.cs:1017-1062) assert the SAME
  expression twice with only the message differing. **Fix:** dedupe to one assertion per
  case. **Unit seam:** test-only, no RED (deletion).
- **N47** (minor, CONFIRMED) — `Run_FocusGuard_TruthTable_ActionKeysTextInputSurface`
  (Program.cs:1107-1112) is a duplicate of
  `Run_FocusGuard_TruthTable_TextInputSurfaceOwnsKeyboard` (:1077-1084). **Fix:** delete
  the duplicate. **Unit seam:** test-only, no RED (deletion).
- **N48** (minor, CONFIRMED) — `Run_ActionsRegistry_TelescopeMapsToFinder` +
  `..._TelescopeKeysMatchFinderNames` (Program.cs:1486-1516) compute the same key sets and
  assert the same `SetEquals`. **Fix:** merge into one test. **Unit seam:** test-only, no
  RED (merge).
- **N49** (minor, CONFIRMED) — `Run_ResultMapper_UnknownStringSkippedOrLogged`'s
  `All(i => i.Payload != null)` (tests/Telescope.Tests/Program.cs:2720-2731) is vacuously
  true when `items` is empty. **Fix:** delete the redundant test — the sibling
  `Run_ResultMapper_UnknownStringNullPayload` (:2707-2718) already asserts the same
  skip-behavior non-vacuously with `Assert.Equal(0, items.Count)` (the unmatched "Ghost.cs"
  is skipped, so items is empty — asserting `items.Count > 0` would fail permanently).
  **Unit seam (Telescope.Tests):** test-only, no RED (deletion).
- **N50** (minor, CONFIRMED) — `Run_WindowManager_DefaultControllerCache`
  (Program.cs:398-427) reflection-invokes private `GetController` and mutates the static
  `ThreadHelper.uiThreadDispatcher` (never restored). **Fix:** restore the static field in
  a `finally` (or avoid the mutation). **Unit seam:** test-only, no RED (behavior-preserving).
- **N8** (major, CONFIRMED) — `Run_FileContentCache_EvictsOldest`
  (tests/Telescope.Tests/Program.cs:2504-2516) uses `DateTime.UtcNow` for all three
  entries — on a ~15ms clock tick "a"/"b" share a key, so eviction is not guaranteed →
  flake. **Fix:** inject a fixed/incrementing timestamp
  (`timestamp: _ => fixedTime.AddSeconds(i++)`) so "a"/"b"/"c" have distinct deterministic
  keys (mirror the sibling `Run_FileContentCache_CachedRead` at :2467-2469). **Unit seam
  (Telescope.Tests):** RED — the current test is non-deterministic (flake); the fixed test
  is deterministic.

## Phase 1 — Block caret + Vim interop lifecycle (N4, N3, N28, N24)

The review's #2 and #4 recommendations: the user-visible block-caret bug with a lying
diagnostic, and the VsVim interop lifecycle leak (the highest-blast-radius fragile
surface).

- **N4** (major, CONFIRMED) — `BlockCaretAdornment.Active=false` → `Update()` early-returns
  at :121-124 BEFORE `_layer.RemoveAdornmentsByTag(AdornmentTag)` at :127 — deactivation
  never removes the block caret; the `block-caret active=False` diagnostic lies. **Fix:**
  in the `Active` setter's false branch (or in `Update()` before the `!_active`
  early-return), call `_layer.RemoveAdornmentsByTag(AdornmentTag)` so deactivation clears
  the adornment. **Unit seam:** no-seam (WPF adornment) — build + existing suites +
  e2e at VERIFY (E2E-CR72-1 asserts the block disappears on focus loss / leaving normal
  mode).
- **N3** (major, CONFIRMED) — `VimBufferSubscriptions._bufferToTextBuffer` is never
  removed in `DecrementRefCount` (grows per unique buffer for the whole session); the
  split-view second view is never Closed-subscribed (VimModeSource.cs:215 early-returns
  before :239 `MarkClosedSubscribed`), so its refcount is stuck at 1 and the `SwitchedMode`
  subscription never drops. **Fix:** remove `_bufferToTextBuffer[buffer]` when the refcount
  hits 0 (in `DecrementRefCount`/`Detach`), and decrement in `Detach` regardless of
  `_closedSubscribed` membership (a shared-text-buffer second view is never
  Closed-subscribed). **Unit seam (NeoVisual.Tests):** `Run_VimBufferSubscriptions_*`
  (pure, tested) — RED: after `Detach`, `_bufferToTextBuffer` still contains the buffer
  today; a shared-buffer second view's refcount stays at 1. The `VimModeSource` wiring is
  no-seam (VsVim reflection) — build + existing suites + e2e at VERIFY (E2E-CR72-2).
- **N28** (minor, CONFIRMED) — `VimModeSource.GetVim` (VimModeSource.cs:451,471) re-queries
  MEF + re-logs `VsVim integration: not found` on every view open / focus gain when VsVim
  is absent — unbounded `[NeoVisual]` log spam. **Fix:** cache the MEF resolution result
  (once per session) and log the not-found line once. **Unit seam:** no-seam (MEF) — build
  + existing suites.
- **N24** (minor, CONFIRMED) — `editor-view-opened` is emitted for every `IWpfTextView`
  (split/peek/preview) at VimModeTracker.cs:96 and can be double-emitted by
  `SelectFirstSourceFile` (SolutionExplorerController.cs:231) + `TextViewCreated`. **Fix:**
  dedupe the emission (only `TextViewCreated` emits it, or only the direct path — one
  owner). **Unit seam:** no-seam (VS-coupled) — build + existing suites + e2e at VERIFY
  (E2E-CR72-5 asserts exactly-once per open).

## Phase 2 — Navigation fault isolation + robustness (N5, N6, N7, N11, N12, N13, N14, N15, N59, N60, N72)

The review's #5 recommendation: close the navigation fault-isolation holes.

- **N5** (major, CONFIRMED) — `WindowFrameAdapter.TryGetScreenRect` (:219) discards the
  `GetWindowScreenRect` HRESULT — a failed read yields a silent empty (or garbage
  non-empty) rect WITHOUT the n19 `window rect unavailable` diagnostic. **Fix:** check the
  HRESULT; on failure return `null` so `RefreshRect` emits the n19 diagnostic and degrades
  to `WindowRect.Empty`. **Unit seam:** no-seam (COM) — build + existing suites + e2e at VERIFY
  (E2E-CR72-3 asserts n19 on a failed read).
- **N6** (major, CONFIRMED) — `NavigationSettings.Invalidate()` (:31) has zero callers
  (verified: 0 real + proxy) — `FromSystemDpi` caches `_cached` forever, so a mid-session
  DPI change leaves stale divide tolerances. **Fix:** drop the `_cached` static and re-read
  `SystemDpiX` per navigation (cheap, removes the stale state entirely), OR wire
  `Invalidate()` to a real DPI-change signal in the package/hook. **Unit seam:** partial —
  `FromDpi` pure (tested); the invalidation needs a DPI-source seam. Verify by build +
  existing suites.
- **N7** (major, CONFIRMED) — the Properties-window quirk (caption + ToolWindow↔Properties
  cross-type match) is implemented twice: `WindowFrameUtils.CompareWindows` (:94-102) vs
  `WindowFrameAdapter.LinkedTo`'s key-set path (:135-142). **Fix:** extract one
  `MatchesPropertiesQuirk(caption, type, otherType)` helper (or a single `WindowKey`-based
  comparer) and have both call it. **Unit seam:** no-seam (COM) — build + existing suites.
- **N11** (minor, CONFIRMED) — `WindowNavigator._cachedLinked` static cache (:26-27,93-100)
  keyed only on the adapters-list reference, never on the active window, never cleared.
  **Fix:** key the cache on the active window (or clear it per navigation). **Unit seam:**
  no-seam (COM) — build + existing suites.
- **N12** (minor, CONFIRMED) — the active window rect is never validated (:140) — an empty
  active rect anchors `SelectTarget` around `(0,0,0,0)`. **Fix:** validate the active rect
  (empty → no-op with the `navigate no-op:` diagnostic). **Unit seam:** no-seam (COM) —
  build + existing suites.
- **N13** (minor, CONFIRMED) — `TryGetScreenRect` (:213, internal, COM-touching) lacks
  `ThreadHelper.ThrowIfNotOnUIThread()`. **Fix:** add the assert. **Unit seam:** no-seam
  (build).
- **N14** (minor, CONFIRMED) — `WindowFrameUtils.GetLinkedWindowsList` (:41-68) is an
  O(n·m) `CompareWindows` pre-filter that `LinkedTo` re-validates with a different key-set
  strategy. **Fix:** single-source the comparison (fold into N7's helper). **Unit seam:**
  no-seam (COM) — build + existing suites.
- **N15** (minor, CONFIRMED) — `_activeWindows.IndexOf(_activeWindow)` (:138) is an O(n)
  reference scan per navigation. **Fix:** carry the index through the snapshot (or use a
  dictionary). **Unit seam:** no-seam (COM) — build + existing suites.
- **N59** (nit, CONFIRMED) — `WindowFrameAdapter._dte` non-readonly while `_frame4` is
  `readonly`; dead `_dte == null` checks on a non-nullable field (:18,41,48). **Fix:** make
  `_dte` `readonly` + drop the dead null checks. **Unit seam:** no-seam (build).
- **N60** (nit, CONFIRMED) — `WindowNavigator.cs:30` stale doc comment "initalize
  windowmatrix" (pre-restructure name). **Fix:** update the comment. **Unit seam:** no-seam
  (docs/comment).
- **N72** (nit, CONFIRMED) — `_loggedEmptyRect` is per-adapter-instance (:198) but adapters
  are recreated per focus change → n19 log spam. **Fix:** make the "logged once" flag
  static (or session-scoped). **Unit seam:** no-seam (build).

## Phase 3 — Tool-window controllers + FocusGuard routing (N16, N17, N18, N19, N20, N21, N22, N23, N25, N26, N51, N61, N62, N71)

- **N17** (minor, CONFIRMED) — the 6-arg `FocusGuard.ShouldRouteToolWindowKey` (:50)
  re-implements the editor-veto inline instead of composing with the 5-arg overload —
  veto logic in two places (the exact class of bug that caused the original action-key
  leak). **Fix:** compose the 6-arg with the 5-arg (add the shift gate on top). **Unit seam
  (NeoVisual.Tests):** `Run_FocusGuard_*` (pure, tested) — add a truth-table test that the
  6-arg == 5-arg for all non-shift inputs (behavior-preserving; not RED-provable).
- **N16** (minor, CONFIRMED) — `WindowManager.ResolveController` (:173, test-only)
  duplicates the registered→default resolution `GetController` re-implements. **Fix:**
  make `ResolveController` delegate to `GetController` (single resolution path). **Unit
  seam (NeoVisual.Tests):** `Run_WindowManager_*` (pure static, tested) —
  behavior-preserving refactor.
- **N18** (minor, CONFIRMED) — `HierarchyResolver.FirstPathMatching` (:52) hand-rolls the
  forest recursion while `FirstSourceFilePath` delegates to the shared `HierarchyWalker`.
  **Fix:** delegate `FirstPathMatching` to the shared walker. **Unit seam (NeoVisual.Tests):**
  `Run_HierarchyResolver_*` (pure, tested) — behavior-preserving refactor.
- **N19** (minor, CONFIRMED) — R18's caret-relative slice applied only to the editor-view
  path; WPF/WinForms still materialize the whole buffer per h/l/w/b/e key
  (TextMotionHelper.cs:104,136). **Fix:** apply the caret-relative slice to the WPF/WinForms
  paths too. **Unit seam:** no-seam (WPF/WinForms) — build + existing suites + e2e at VERIFY
  (E2E-CR72-6 asserts `text-motion key=... caret=...` unchanged).
- **N20** (minor, CONFIRMED) — `SolutionExplorerController` H/L inline lambdas (log +
  `TryMoveArrow`) vs J/K bare delegates (:44) — two shapes for the same hjkl→arrow press.
  **Fix:** unify H/L with J/K (delegate to `TryMoveArrow`). **Unit seam:** no-seam
  (VS-coupled) — build + existing suites.
- **N21** (minor, CONFIRMED) — `ToolWindowControllerBase._isInputMode` mutated via a `ref`
  param in `TryMoveFocusedSurface` for a/A/I (:46), bypassing `EnterInputMode()`/
  `OnModeChanged()`. **Fix:** route a/A/I through `EnterInputMode()`/`OnModeChanged()`.
  **Unit seam:** partial (base pure) — build + existing suites.
- **N22** (minor, CONFIRMED) — `FocusKeeper.Run`'s `KeeperHandle` ignored by both callers
  (:27 + SolutionExplorerController.cs:118) — a superseded keeper's queued tick can still
  re-assert the old target. **Fix:** store the handle and cancel prior keepers on a new
  `Run`. **Unit seam:** no-seam (DispatcherTimer) — `FocusKeeperSchedule` (pure, tested)
  covers the schedule half; build + existing suites.
- **N23** (minor, CONFIRMED) — editor-view `A` (InsertEnd) lands at the caret-relative
  slice boundary, not the true line end, on lines >~4096 chars (TextMotionHelper.cs:182).
  **Fix:** map InsertEnd to the true line end (resolve the full line, not the slice
  boundary). **Unit seam:** no-seam — build + existing suites + e2e at VERIFY (E2E-CR72-6).
- **N25** (minor, CONFIRMED) — unguarded `(int)value` cast on `VSFPROPID_Type` inside the
  `IVsSelectionEvents` callback (WindowManager.cs:261) — `InvalidCastException` escapes a
  VS callback. **Fix:** guard the cast (try/catch or `is` check). **Unit seam:** no-seam
  (VS callback) — build + existing suites.
- **N26** (minor, CONFIRMED) — `FocusKeeperSchedule.Decide` returns `Reassert` (not `Stop`)
  after `MaxEscapeAttempts` with the box still focused (FocusKeeper.cs:100) — the keeper
  fights the user. **Fix:** return `Stop` after `MaxEscapeAttempts`. **Unit seam
  (NeoVisual.Tests):** `Run_FocusKeeperSchedule_*` (pure, tested) — RED: a test asserting
  `Stop` after `MaxEscapeAttempts` fails today (returns `Reassert`).
- **N51** (minor, CONFIRMED) — coverage gap: `TextMotionHelper.TryMoveFocusedSurface` has no
  unit test (verified only by e2e). **Fix:** add unit tests for the pure motion dispatch in
  `TryMoveFocusedSurface` (via `TextMotionNavigator`/`TextMotionHelper` pure seams). NOTE:
  `FocusKeeperSchedule` is ALREADY covered by `Run_FocusKeeperSchedule_TruthTable`
  (tests/NeoVisual.Tests/Program.cs:1228) — do not claim it is uncovered.
  **Unit seam (NeoVisual.Tests):** new `Run_TextMotionHelper_*` tests — RED: the motion
  dispatch is uncovered today (no test exists).
- **N61** (nit, CONFIRMED) — `HierarchyForestBuilder.HierarchyItemInfo` vs
  `HierarchyResolver.HierarchyNode` near-identical DTOs with a one-to-one conversion
  (:13). **Fix:** merge into one DTO. **Unit seam (NeoVisual.Tests):** behavior-preserving
  refactor.
- **N62** (nit, CONFIRMED) — the `_actions` dict + `TryMove` dict-lookup pattern duplicated
  in `ToolWindowControllerBase` (:52) + `TextInputToolWindowController` +
  `SolutionExplorerController`. **Fix:** single-source the pattern in the base. **Unit
  seam:** partial — behavior-preserving refactor.
- **N71** (nit, CONFIRMED) — `SolutionExplorerController.ExitInputMode` resolves the box
  once but `StyleFocusedSurface` re-walks the visual tree per Esc (:61). **Fix:** pass the
  resolved box through. **Unit seam:** no-seam (WPF) — build + existing suites.

## Phase 4 — Input/hook hot path (N29, N30, N56, N57, N58, N69, N70)

- **N29** (minor, CONFIRMED) — the Ctrl+N/P popup-navigation branch (InputHandler.cs:295)
  runs before the leader state machine and never resets an in-progress leader sequence.
  **Fix:** reset the leader sequence when the popup-navigation branch fires. **Unit seam:**
  no-seam (InputHandler VS-coupled) — build + existing suites.
- **N30** (minor, CONFIRMED) — `KeyNameBuilder.Build` (:11) allocates a fresh
  `StringBuilder` + string per call on the hook hot path. **Fix:** cache the built string
  (or use a pooled builder). **Unit seam (NeoVisual.Tests):** `Run_KeyNameBuilder_*` (pure,
  tested) — behavior-preserving (the canonical string must stay identical); not
  RED-provable (perf).
- **N56** (nit, CONFIRMED) — `KeybindingConfig.Load`/`LoadDefaults`/`LoadFromJson` (:75,115,126)
  are three near-identical methods each building the merge prologue. **Fix:** extract one
  shared merge helper. **Unit seam (NeoVisual.Tests):** `Run_KeybindingConfig_*` (pure,
  tested) — behavior-preserving refactor.
- **N57** (nit, CONFIRMED + scope NEEDS-CORRECTION) — unused `using System.Diagnostics;`.
  **Fix (corrected):** remove it from `MyExtensionPackage.cs:8` and `TelescopeLauncher.cs:5`
  ONLY — `GlobalKeyboardHook.cs:6` IS used (Process :56/:170). **Unit seam:** no-seam
  (build).
- **N58** (nit, CONFIRMED) — `GlobalKeyboardHook` ctor (:66) inlines
  `NeoVisualLog.Log($"{DiagnosticLog.Hook}starting")` instead of the `Log()` helper that
  prepends the prefix. **Fix:** use the `Log()` helper. **Unit seam:** no-seam (build).
- **N69** (nit, CONFIRMED) — null-forgiving `!` on `_launcher` (MyExtensionPackage.cs:168)
  hides a real dependency — a failed telescope init step makes the hook step fail with
  `ArgumentNullException`. **Fix:** null-check `_launcher` and log the failure (don't
  crash the hook step). **Unit seam:** no-seam (package init) — build + existing suites.
- **N70** (nit, CONFIRMED) — `KeybindingConfig.ParseLeader` (:218) accepts any `Keys` value
  via `Enum.TryParse`, including modifiers (`"Ctrl"` silently disables the leader key).
  **Fix:** reject modifier keys (and non-single keys). **Unit seam (NeoVisual.Tests):**
  `Run_KeybindingConfig_*` (pure, tested) — RED: `ParseLeader("Ctrl")` succeeds today;
  the fix rejects it.

## Phase 5 — Telescope overlay/finders/filter/logging (N27, N31, N32, N33, N34, N35, N36, N37, N38, N39, N40, N41, N42, N43, N44, N45, N63, N64, N65, N66, N67, N68)

- **N31** (minor, CONFIRMED) — `PromptMotionRouter` (:33) maps `'a'` to
  `CaretPlacement.Current` (no motion) — in the prompt `a` behaves identically to `i`,
  never inserting after the caret. **Fix:** map `'a'` to a new `CaretPlacement.AfterCaret`
  value (the enum in OverlayKeyHandler.cs:51-56 currently has only `Current/End/Start` —
  add `AfterCaret` + a `TelescopeOverlay.EnterInsert` case that places the caret at
  caret+1), and update `Run_PromptMotionRouter_InsertPlacementsNotConsumed` (currently
  asserts `Current` for bare `a`). **Unit seam (Telescope.Tests):**
  `Run_PromptMotionRouter_*` (pure, tested) — RED: a test asserting `'a'` → `AfterCaret`
  fails today (returns `Current`).
- **N32** (minor, CONFIRMED) — `GrepFinder.GetCandidates` (:91-115) scans every project
  file synchronously on the UI thread per debounced query; the content cache is cold each
  overlay open. **Fix:** warm the content cache at overlay open (chosen fix — see BP-45).
  **Unit seam:** partial (test ctor) — build + existing suites + e2e at VERIFY.
- **N33** (minor, CONFIRMED) — three near-identical mtime-keyed caches
  (`PreviewTokenCache`, `PreviewDocumentCache`, `FileContentCache`); `PreviewDocumentCache`
  is a strict subset of `PreviewTokenCache`. **Fix:** consolidate into one shared cache
  (or delete the subset). **Unit seam (Telescope.Tests):** `Run_*Cache_*` (pure, tested) —
  behavior-preserving refactor.
- **N34** (minor, CONFIRMED) — on a preview cache miss the file is read twice
  (`PreviewRenderer.Show` via `_contentCache`, then `SetContent` re-reads via
  `_tokenCache`) (:54 + PreviewTokenCache.cs:39). **Fix:** pass the content through (single
  read). **Unit seam:** partial — build + existing suites.
- **N35** (minor, CONFIRMED) — `PreviewRenderer.ColorFor` (:170-181) allocates a new
  `SolidColorBrush` per segment on every `FlowDocument` rebuild. **Fix:** cache the brushes
  (per color). **Unit seam:** no-seam (WPF) — build + existing suites.
- **N36** (minor, CONFIRMED) — `TextMotionDispatcher` (:46-96) has two parallel `MapKey`
  switch tables (WinForms `Keys` vs WPF `Key`) that must be kept in sync. **Fix:** single
  table (map once, translate). **Unit seam (Telescope.Tests):** `Run_TextMotionDispatcher_*`
  (pure, tested) — behavior-preserving refactor.
- **N37** (minor, CONFIRMED + detail NEEDS-CORRECTION) — **FileFinder** re-walks the DTE
  tree per open (registered via the public ctor at MyExtensionPackage.cs:101, which takes
  no cache); CodeIssuesFinder (:102) + GrepFinder (:103) already share the cache. **Fix:**
  add a public `FileFinder(Func<DTE>, ProjectFileCache)` ctor (the production path at
  FileFinder.cs:85-87 already handles `_fileCache != null`) and register FileFinder with
  the shared cache. **Unit seam:** partial (test ctor) — build + existing suites.
- **N27** (minor, CONFIRMED) — `RoslynGatherers.IsWriteLocation` (:285) runs
  `GetProperty("IsWrittenTo")` reflection per reference location on the UI thread.
  **Fix:** resolve the `PropertyInfo` once per type (cache it) instead of per location.
  **Unit seam:** no-seam (Roslyn reflection) — build + existing suites.
- **N38** (minor, CONFIRMED) — `FzfFilter.IsAvailable()` (:98) runs a synchronous
  `WaitForExit(500)` probe on the UI thread at first overlay open. **Fix:** make the probe
  async (`IsAvailableAsync`, off the UI thread — chosen fix, see BP-52). **Unit seam:**
  partial — `FzfFilter` hermetic (tested); the UI-thread aspect is in the caller.
- **N39** (minor, CONFIRMED) — `FzfFilter.FilterAsync` (:128) never checks the cached
  `_availability` — per-keystroke `Win32Exception` + `fzf filter failed` log when fzf is
  missing. **Fix:** check `_availability` at the top of `FilterAsync` (return unfiltered
  without spawning). **Unit seam (Telescope.Tests):** `Run_FzfFilter_*` (hermetic,
  `AwaitedReadCount` seam) — RED: with `_availability=false`, `FilterAsync` still spawns
  today; the fix returns unfiltered without spawning.
- **N40** (minor, CONFIRMED) — `PaneFailureTracker._retryAllowed` (:15,35-41) is always
  true — the "retry latch" is dead logic and the `!ShouldRetry()` guard is unreachable.
  **Fix:** remove the dead latch (`ShouldRetry()`/`RecordAttempt()` + the
  `!ShouldRetry()` guard in `NeoVisualLog.EnsurePane` :133,:168), and delete the now-dead
  `Run_PaneFailureTracker_RetryAfterFailure` test (Telescope.Tests:482). **Unit seam
  (Telescope.Tests):** `Run_PaneFailureTracker_*` (pure, tested) — behavior-preserving;
  re-check `check-doc-refs.ps1` after the deletion.
- **N41** (minor, CONFIRMED) — `FilterFailureLog.Format` (:14) returns an UNPREFIXED string
  relying on the caller to log via `TelescopeLog` — a wrong logger silently breaks the
  `filter failed:` contract. **Fix:** make `Format()` return the prefixed line (or fold the
  class into the caller — see N63), and update the now-stale `spec.md` §2.2:121 note
  ("unprefixed — callers log via `TelescopeLog`"). **Unit seam (Telescope.Tests):** RED —
  a test asserting the format includes the `[Telescope]` prefix fails today.
- **N42** (minor, CONFIRMED) — `CodeIssuesFinder.Display` (:103) embeds `issue.Text`
  verbatim — a multi-line Error List description breaks fzf/`ResultMapper` and the results
  TextBox. **Fix:** sanitize/truncate the display (single-line, bounded). **Unit seam:**
  partial — build + existing suites.
- **N43** (minor, CONFIRMED) — user-controlled text interpolated into log lines
  (TelescopeOverlay.cs:348,211 + ResultMapper.cs:60) — `\n`/control chars split the line
  and break `Wait-NewLogLine` assertions. **Fix:** sanitize user text in log lines (strip
  newlines/control chars — mirror the R39 sample-sanitization pattern). **Unit seam:**
  no-seam (WPF) — build + existing suites + e2e at VERIFY (E2E-CR72-9).
- **N44** (minor, CONFIRMED) — `ProjectFileCache` invalidated only on solution-name change
  (GrepFinder.cs:99 + CodeIssuesFinder.cs:77) — added/removed files within a solution stay
  stale. **Fix:** add a bounded TTL (chosen fix — see BP-58). **Unit
  seam:** partial — build + existing suites.
- **N45** (minor, CONFIRMED) — hand-rolled `FzfFilter.QuoteArg` (:233) mis-escapes
  backslashes-before-quotes (Windows argv rules). Note: trailing backslashes (`C:\`) are
  already handled (doubled for the closing quote); the real bug is backslashes-before-quotes
  in the MIDDLE of the string (e.g. `C:\"` or `a\b"c`), where the odd backslash is lost.
  **Fix:** correct the escaping (double each backslash run that precedes a quote;
  net472-compatible — no `ProcessStartInfo.ArgumentList`). **Unit seam (Telescope.Tests):**
  `Run_FzfFilter_*` (hermetic) — RED: a test asserting a backslash-before-quote arg (e.g.
  `C:\"`) is quoted correctly fails today.
- **N63** (nit, CONFIRMED) — `FilterFailureLog` (:12-15) is a one-line string concatenation
  wrapped in a dedicated class. **Fix:** inline it (fold into N41). **Unit seam
  (Telescope.Tests):** behavior-preserving.
- **N64** (nit, CONFIRMED) — `SyntaxHighlighter.Tokenize` (:64-187) is complexity 23 with a
  per-token `text.Substring` allocation. **Fix:** reduce complexity + avoid the per-token
  Substring (use spans/offsets). **Unit seam (Telescope.Tests):** `Run_SyntaxHighlighter_*`
  (pure, tested) — behavior-preserving (the token output must stay identical — the
  `preview tokens=` diagnostic).
- **N65** (nit, CONFIRMED) — every `NeoVisualLog.Log` line is duplicated to the debug file
  via `Debug.WriteLine` → `NeoVisualTraceListener` (:101-103). **Fix:** gate the debug-file
  duplication (env flag or drop it). **Unit seam:** no-seam — build + existing suites.
- **N66** (nit, CONFIRMED) — `TextMotionNavigator.Down()` on the last line moves the caret
  to `_text.Length` instead of no-op (:61) (and `Up()` on the first to 0). **Fix:** no-op
  at the boundaries (stay at the last/first position). **Unit seam (Telescope.Tests):**
  `Run_TextMotionNavigator_*` (pure, tested) — RED: a test asserting `Down()` on the last
  line keeps the caret at the last position fails today (moves to `_text.Length`).
- **N67** (nit, CONFIRMED) — `SyntaxHighlighter` (:127) `$@"..."` (interpolated verbatim)
  is not matched by the `'$' + '"'` branch — renders as Default. **Fix:** match the
  interpolated-verbatim branch. **Unit seam (Telescope.Tests):** `Run_SyntaxHighlighter_*`
  (pure, tested) — RED: a test asserting `$@"..."` tokenizes as a string fails today.
- **N68** (nit, CONFIRMED) — `FileFinder.OpenHit` (:114) logs `opened file: {path}` even
  when `_dteFactory()` returns null (the `?.` short-circuits the open). **Fix:** only log
  when the open actually happened. **Unit seam:** partial — build + existing suites.

## Phase 6 — Harness + lint (N9, N10, N52, N53)

- **N9** (major, CONFIRMED) — `telescope-open`/`-mode`/`-navigate` assertions
  (test-e2e.ps1:514-520,583,544) are satisfied by `Open-Telescope`'s own wait line
  (baseline reset before the helper) — false-positive gates. **Fix:** assert a fresh
  post-tap line the helper does NOT confirm (a `key=... mode=insert handled=True`
  line after an explicit Escape — chosen fix, see BP-65). **Unit seam:** no-seam (harness-only) — verified by
  the harness `-SelfCheck` seam + e2e at VERIFY (E2E-CR72-4).
- **N10** (major, CONFIRMED) — `tools/lint/check-doc-content.ps1` is RED (3/12):
  assertions hardcoded to the Phase-10 (98-findings) fixed state while `docs/progress.md`
  advanced to the 51-findings plan (:131,156,162). **Fix:** update the three assertions to
  the current 51-findings state (DOC-64-3 accepts the `E2E-NCR-*` wildcard or the concrete
  E2E-CR51 IDs; DOC-66-2/3 point at the current "Next up"/baseline). **Unit seam:** no-seam
  (lint) — verified by running `pwsh tools/lint/check-doc-content.ps1` (must PASS 12/12).
- **N52** (minor, CONFIRMED) — `neovisual-explorer-open`/`-open-o` (:716-731,800-815) use a
  "try l/j/Enter until ANY file opens" loop — order-dependent, doesn't pin WHICH file
  opens. **Fix:** pin WHICH file opens (assert the specific `solution-explorer open` /
  `editor-view-opened file=` path). **Unit seam:** no-seam (e2e) — e2e at VERIFY
  (E2E-CR72-10).
- **N53** (minor, CONFIRMED) — `neovisual-editor-insert` (:1080-1083) updates the
  seed-expected copy in a `finally`, so even a FAILED marker assertion records the wrong
  content as expected. **Fix:** update the expected copy only on success (not in the
  `finally`). **Unit seam:** no-seam (e2e) — e2e at VERIFY (E2E-CR72-10).

## Phase 7 — Docs (N54, N55)

- **N54** (minor, CONFIRMED) — `docs/reviews/architecture-review.md` (:269,284,313,318,395)
  still presents F1/F16/F45 as open with no FIXED annotation (all fixed per
  `docs/progress.md`). **Fix:** add the FIXED annotations. **Unit seam:** no-seam (docs) —
  verified by `pwsh tools/lint/check-doc-refs.ps1` (PASS).
- **N55** (minor, CONFIRMED + line ref NEEDS-CORRECTION) — `code-review.md:66`'s R50
  premise is no longer true — `architecture-review.md:263` now uses the post-restructure
  names. **Fix:** update the code-review.md reference (the correct finding is N50, from the
  superseded 98-findings review). **Unit seam:** no-seam (docs) — verified by
  `check-doc-refs.ps1` (PASS).

---

# Part B — E2E gates (RUN at VERIFY)

The following e2e scenarios are RUN on this machine (e2e enabled) at VERIFY. Each
asserts the listed scenarios stay GREEN + no log-literal drift, and depends on the
named diagnostics. The full 35-scenario suite is the item's final gate.

- **E2E-CR72-1 (N4)** — block-caret deactivation: leaving normal mode / focus loss removes
  the block caret. Scenarios: `neovisual-textinput-motions` + a new assertion that the
  block is gone. Depends on `block-caret active=False` (now truthful).
- **E2E-CR72-2 (N3)** — Vim buffer-subscription lifecycle: closing a non-focused view does
  NOT kill the focused view's `SwitchedMode` subscription. Scenarios: `neovisual-editor-insert`
  + a multi-view scenario. Depends on `vim-mode=Insert|Normal|Replace`.
- **E2E-CR72-3 (N5/N72)** — a failed `GetWindowScreenRect` emits `window rect unavailable`
  and navigation degrades gracefully. Scenarios: `neovisual-window-nav`. Depends on the n19
  diagnostic.
- **E2E-CR72-4 (N9)** — `telescope-open`/`-mode`/`-navigate` assert a post-tap line the
  helper does not confirm (a re-introduced prompt-focus/mode bug fails the gate).
  Scenarios: `telescope-open`, `telescope-mode`, `telescope-navigate`. Depends on
  `Focus prompt => True, mode=insert` post-tap.
- **E2E-CR72-5 (N24)** — `editor-view-opened file=...` emitted exactly once per open.
  Scenarios: `explorer-open-navigation`. Depends on `editor-view-opened file=...`.
- **E2E-CR72-6 (N19/N23)** — caret-relative slice + `A` placement: `text-motion key=...
  caret=...` unchanged; `A` lands at the true line end. Scenarios: `neovisual-textinput-motions`.
  Depends on `text-motion key=... caret=...`.
- **E2E-CR72-7 (N31)** — prompt `a` inserts after the caret. Scenarios: `telescope-mode` /
  `telescope-prompt-motions`. Depends on `prompt-motion key=... caret=...` /
  `Focus prompt => True, mode=insert`.
- **E2E-CR72-8 (N38/N39)** — fzf availability cached: when fzf is missing, no per-keystroke
  `fzf filter failed` spam. Scenarios: `telescope-search`. Depends on `fzf filter failed:` /
  `fzf unavailable — showing unfiltered list`.
- **E2E-CR72-9 (N43)** — user-controlled text sanitized in log lines (no line splitting).
  Scenarios: `telescope-search` / `telescope-issues`. Depends on `[Telescope]` lines.
- **E2E-CR72-10 (N52/N53)** — harness: `neovisual-explorer-open`/`-open-o` pin WHICH file
  opens; `neovisual-editor-insert` updates the seed-expected copy only on success.
  Scenarios: `neovisual-explorer-open`, `neovisual-explorer-open-o`,
  `neovisual-editor-insert`, `seed-leak`. Depends on `solution-explorer open` /
  `editor-view-opened file=` / the seed-leak guard.

---

# Build Plan

> **Lane:** bugfix (e2e enabled). Every BP-n step maps to
> one or more N-ids. **Verify-with** = unit test NAMES + diagnostic formats + `dotnet build` + the
> affected e2e scenarios (Part B / `docs/e2e-queue.md`, E2E-CR72-1..10). **RED** = the named test
> fails before the fix / the named diagnostic is absent or lies; **GREEN** = the test passes / the
> diagnostic is truthful and byte-identical. **Log-line-as-contract:** any fix touching a harness-asserted line must keep the
> emitted token byte-identical (except N4, where the fix makes `block-caret active=False` truthful
> without changing its format). **net472:** no `IReadOnlySet<T>`, no `ProcessStartInfo.ArgumentList`.
> **UI thread:** `ThreadHelper.ThrowIfNotOnUIThread()` on any new VS-API method.
> **Known-RED allowlist:** none — all 35 e2e scenarios GREEN, unit suites 153/163 passing (per
> `docs/progress.md`); the verification-agent must NOT flag any of the 72 findings' pre-fix state as
> a regression.

## Phase 0 — Critical test-quality + FocusGuard tests (N1, N2, N46, N47, N48, N49, N50, N8)

**Mid-point verify (Phase 0 boundary):** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` and
`dotnet run --project tests/Telescope.Tests -- FileContentCache` both pass; the corrected N1/N2 tests
fail if `FocusGuard.ShouldRouteToolWindowKey` is mutated to not route in input mode / zero-action-key.

### BP-1 (N1, critical) — Correct the tautological input-mode FocusGuard test
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `Run_FocusGuard_InputModeBlocksActionKeys` (Program.cs:1038-1047), drop the
  constant-false `&& !isInputMode` term and assert the guard's own return value directly:
  `Assert.True(FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: true, isTextInputSurface: false, textInputSurfaceFocused: false), "...")`.
  The guard returns TRUE (input mode owns the keyboard → routes → blocks the key from the editor);
  the `!isInputMode` gate is caller-side (InputHandler.cs:313, IsKeyOfInterest:449), not part of this
  assertion.
- **Verify-with:** corrected `Run_FocusGuard_InputModeBlocksActionKeys` (NeoVisual.Tests). RED proof:
  temporarily mutate `ShouldRouteToolWindowKey` to return false for input mode — the OLD tautological
  test still passes, the NEW test fails.
- **Fails-if:** the corrected test passes while the guard is mutated to not route in input mode (the
  fix was not applied / the assertion is still tautological).

### BP-2 (N2, critical) — Correct the tautological zero-action-keys FocusGuard test
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `Run_FocusGuard_ZeroActionKeysBlocks` (Program.cs:1049-1060), drop the
  constant-false `&& actionKeyCount > 0` term and assert
  `Assert.True(FocusGuard.ShouldRouteToolWindowKey(isToolWindow: true, editorFocused: false, isInputMode: false, isTextInputSurface: false, textInputSurfaceFocused: false), "...")`
  with a comment that the action-key-count gate lives in the caller (the guard routes a tree-focused
  tool window regardless of action-key count).
- **Verify-with:** corrected `Run_FocusGuard_ZeroActionKeysBlocks` (NeoVisual.Tests). RED proof: same
  mutation as BP-1.
- **Fails-if:** the corrected test passes while the guard is mutated to not route a tree-focused tool
  window.

### BP-3 (N46, minor) — Dedupe the FocusGuard duplicate assertions
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `Run_FocusGuard_EditorFocusedBlocksRouting` (:1017-1026) and
  `Run_FocusGuard_TreeFocusedAllowsRouting` (:1028-1036), keep ONE assertion per case (the two
  `Assert.False`/`Assert.True` calls assert the same expression with only the message differing).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` passes (test-only
  deletion, no RED).
- **Fails-if:** the suite fails to compile or a FocusGuard behavior test is accidentally removed.

### BP-4 (N47, minor) — Delete the duplicate truth-table test (identical assertion args, different message)
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Delete `Run_FocusGuard_TruthTable_ActionKeysTextInputSurface` (:1107-1112) — it asserts
  the SAME expression as `Run_FocusGuard_TruthTable_TextInputSurfaceOwnsKeyboard` (:1077-1084) with
  IDENTICAL assertion args (`isToolWindow: true, editorFocused: true, isInputMode: false,
  isTextInputSurface: true, textInputSurfaceFocused: true`); only the message differs
  ("text-input-surface action keys are interesting despite the stale editor flag" vs
  "a text-input surface routes keys despite the stale editor flag").
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` passes (deletion, no RED).
- **Fails-if:** the duplicate test remains or the surviving test is accidentally deleted.

### BP-5 (N48, minor) — Merge the duplicate ActionsRegistry tests
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Merge `Run_ActionsRegistry_TelescopeMapsToFinder` (:1486-1503) +
  `Run_ActionsRegistry_TelescopeKeysMatchFinderNames` (:1505-1516) into one test that computes the key
  sets once and asserts the `SetEquals` + non-empty finder-name mapping.
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- ActionsRegistry` passes (merge, no RED).
- **Fails-if:** the merged test loses the non-empty-finder-name assertion or the suite fails to compile.

### BP-6 (N49, minor) — Delete the redundant ResultMapper skip-behavior test
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** Delete `Run_ResultMapper_UnknownStringSkippedOrLogged` (:2720-2731) — its
  `All(i => i.Payload != null)` is vacuously true when `items` is empty. The sibling
  `Run_ResultMapper_UnknownStringNullPayload` (:2707-2718) already asserts the same skip-behavior
  non-vacuously with `Assert.Equal(0, items.Count)` (the unmatched "Ghost.cs" is skipped, so items is
  EMPTY — asserting `items.Count > 0` would fail permanently).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- ResultMapper` passes (test-only
  deletion, no RED).
- **Fails-if:** the redundant test remains, or the surviving `Run_ResultMapper_UnknownStringNullPayload`
  (`Assert.Equal(0, items.Count)`) is accidentally deleted.

### BP-7 (N50, minor) — Restore the mutated ThreadHelper static in the WindowManager test
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` (:398-427), wrap the
  `uiThreadDispatcher` mutation in a `try/finally` that restores the original static value (or avoid
  the mutation entirely).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- WindowManager` passes
  (behavior-preserving, no RED).
- **Fails-if:** the static `ThreadHelper.uiThreadDispatcher` is left mutated after the test (a later
  test sees a stale dispatcher).

### BP-8 (N8, major) — De-flake the FileContentCache eviction test
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** In `Run_FileContentCache_EvictsOldest` (:2498-2517), replace
  `timestamp: _ => DateTime.UtcNow` with a **per-path STABLE** timestamp
  (`timestamp: p => fixedTime.AddSeconds(index[p])`, where `index` maps `"a"`→0, `"b"`→1, `"c"`→2)
  so each path has a distinct deterministic key that does NOT change between calls. Do **NOT** use a
  per-CALL counter (`timestamp: _ => fixedTime.AddSeconds(i++)`): it changes the key on every call, so
  a re-read of `"a"` always misses the cache and the assertion passes even if eviction is removed
  (vacuous). A constant `timestamp: _ => fixedTime` (mirroring the sibling
  `Run_FileContentCache_CachedRead` at :2464-2480) is also acceptable — a non-evicted `"a"` then hits.
- **Verify-with:** `Run_FileContentCache_EvictsOldest` (Telescope.Tests) — deterministic AND
  non-vacuous: with eviction working, the re-read of the evicted `"a"` misses (reads+1); with eviction
  removed, the re-read of `"a"` HITS (reads unchanged) so the assertion FAILS. RED today: the
  `DateTime.UtcNow` version flakes on a ~15ms clock tick where `"a"`/`"b"` share a key.
- **Fails-if:** the test still uses `DateTime.UtcNow` for all three entries (eviction not guaranteed →
  flake), or it uses a per-CALL counter (the assertion is vacuous — passes even with eviction removed).

## Phase 1 — Block caret + Vim interop lifecycle (N4, N3, N28, N24)

**Mid-point verify (Phase 1 boundary):** `dotnet build` 0 errors;
`dotnet run --project tests/NeoVisual.Tests -- VimBufferSubscriptions` passes.

### BP-9 (N4, major) — Block-caret deactivation removes the adornment
- **Files:** `MyExtension/Adornments/BlockCaretAdornment.cs`
- **Change:** In the `Active` setter's false branch (BlockCaretAdornment.cs:69-82), call
  `_layer.RemoveAdornmentsByTag(AdornmentTag)` so deactivation clears the block caret (today `Update()`
  early-returns at :121-124 BEFORE the `RemoveAdornmentsByTag` at :127, so the block persists and
  `block-caret active=False` lies). Keep the `Update()` early-return for the hot path (the deactivation
  path now owns the removal). The `block-caret active=True|False` diagnostic format stays byte-identical.
- **Verify-with:** no hermetic seam (WPF adornment) — `dotnet build` + existing suites; the diagnostic
  `[NeoVisual] block-caret active=False` is now truthful (e2e at VERIFY E2E-CR72-1 asserts the block
  disappears on focus loss / leaving normal mode).
- **Fails-if:** `block-caret active=False` is emitted but the block caret remains visible (deactivation
  still doesn't remove the adornment).

### BP-10 (N3, major) — Vim buffer-subscription lifecycle: remove the map entry + decrement on Detach
- **Files:** `MyExtension/Vim/Utils/VimBufferSubscriptions.cs`, `MyExtension/Vim/Utils/VimModeSource.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `VimBufferSubscriptions.DecrementRefCount` (:119-135), remove
  `_bufferToTextBuffer[buffer]` when the refcount hits 0 (today the entry leaks for the whole session).
  In `Detach` (:63-76), decrement regardless of `_closedSubscribed` membership — a shared-text-buffer
  second view is never Closed-subscribed (VimModeSource.cs:215 early-returns before :239
  `MarkClosedSubscribed`), so its refcount is stuck at 1 today. The `VsVimModeSource` wiring
  (VimModeSource.cs) is no-seam (VsVim reflection) — build + existing suites.
- **Verify-with:** `Run_VimBufferSubscriptions_*` (NeoVisual.Tests, pure) — RED: after `Detach`,
  `_bufferToTextBuffer` still contains the buffer today; a shared-buffer second view's refcount stays
  at 1. GREEN: the map entry is removed at refcount 0 and `Detach` decrements a non-Closed-subscribed
  shared-buffer view.
- **Fails-if:** `_bufferToTextBuffer` grows per unique buffer across the session, or a shared-buffer
  second view's `SwitchedMode` subscription never drops.

### BP-11 (N28, minor) — Cache the VsVim MEF resolution + log not-found once
- **Files:** `MyExtension/Vim/Utils/VimModeSource.cs`
- **Change:** In `GetVim` (:451-486), latch the resolution result (including the not-found/null case)
  once per session so `VsVim integration: not found` is logged once instead of on every view open /
  focus gain. **KEY DECISION:** this supersedes the m38 "don't latch not-found" comment (:472-478) —
  N28's anti-spam requirement wins; the not-found result is cached once per session.
- **Verify-with:** no hermetic seam (MEF) — `dotnet build` + existing suites; the diagnostic
  `[NeoVisual] VsVim integration: not found` appears at most once per session.
- **Fails-if:** `VsVim integration: not found` is emitted more than once per session when VsVim is
  absent (unbounded log spam).

### BP-12 (N24, minor) — Dedupe the `editor-view-opened` emission
- **Files:** `MyExtension/Vim/Utils/EditorViewOpenedLog.cs` (new), `MyExtension/Vim/VimModeTracker.cs`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change (chosen approach — one guarded helper):** Add `MyExtension/Vim/Utils/EditorViewOpenedLog.cs`
  with a pure `Emit(string? path)` that suppresses a duplicate emission for the SAME path within a
  short window (track the last emitted path + timestamp). Route BOTH emission sites through it:
  `VimModeTracker.TextViewCreated` (:96) and `SolutionExplorerController.cs:231`. This **KEEPS** the
  direct emission for the `doc.Activate()` path — `TextViewCreated` does NOT fire for an
  already-created document (the code's own R38 comment at :222-225), so removing it would drop the
  emission for that path — and dedupes the `TextViewCreated` side (split/peek/preview views and the
  `SelectFirstSourceFile` + `TextViewCreated` double-count). The diagnostic format
  `[NeoVisual] editor-view-opened file=...` stays byte-identical.
- **Verify-with:** `Run_EditorViewOpenedLog_*` (NeoVisual.Tests, pure) — RED: a second `Emit` of the
  same path within the window is suppressed (today no helper exists → compile error); plus e2e at
  VERIFY E2E-CR72-5 asserts exactly-once per open.
- **Fails-if:** `editor-view-opened file=...` is emitted twice for a single open (split/peek/preview or
  `SelectFirstSourceFile` + `TextViewCreated`), OR the `doc.Activate()` path emits nothing (the direct
  emission was wrongly removed).

## Phase 2 — Navigation fault isolation + robustness (N5, N6, N7, N11, N12, N13, N14, N15, N59, N60, N72)

**Mid-point verify (Phase 2 boundary):** `dotnet build` 0 errors; existing suites green.

### BP-13 (N5, major) — Check the GetWindowScreenRect HRESULT
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** In `TryGetScreenRect` (:213-221), check the `GetWindowScreenRect` HRESULT; on failure
  return `null` so `RefreshRect` (:192-207) emits the n19 diagnostic and degrades to `WindowRect.Empty`
  (today a failed read yields a silent empty/garbage rect WITHOUT the diagnostic).
- **Verify-with:** no hermetic seam (COM) — `dotnet build` + existing suites; the diagnostic
  `[NeoVisual] window rect unavailable; using empty rect` is emitted on a failed read (e2e at VERIFY
  E2E-CR72-3).
- **Fails-if:** a failed `GetWindowScreenRect` produces a rect without the `window rect unavailable`
  diagnostic.

### BP-14 (N6, major) — Drop the stale NavigationSettings cache
- **Files:** `MyExtension/Navigation/Utils/NavigationSettings.cs`
- **Change:** Remove the `_cached` static (:7) and the `Invalidate()` method (:31-34, verified 0 callers
  via Trailmark) — `FromSystemDpi` (:18-25) re-reads `SystemDpiX`/`SystemDpiY` per navigation (cheap),
  removing the stale mid-session DPI state entirely. `FromDpi` (:36-41) stays pure.
- **Verify-with:** `dotnet build` + existing suites (partial seam — `FromDpi` pure, tested); no caller
  of `Invalidate()` exists (Trailmark `callers_of("NavigationSettings.Invalidate")` = 0).
- **Fails-if:** `FromSystemDpi` still returns a cached value after a mid-session DPI change, or
  `Invalidate()` remains with no caller.

### BP-15 (N7 + N14, minor) — Single-source the Properties-window quirk comparison
- **Files:** `MyExtension/Navigation/Utils/WindowFrameUtils.cs`, `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** Extract one `MatchesPropertiesQuirk(caption, type, otherType)` helper (or a single
  `WindowKey`-based comparer) from `WindowFrameUtils.CompareWindows` (:94-102) and have BOTH
  `CompareWindows` and `WindowFrameAdapter.LinkedTo`'s key-set path (:135-142) call it.
  `GetLinkedWindowsList` (:41-68) delegates to the same comparison (single-source, no O(n·m)
  re-validation with a different key-set strategy).
- **Verify-with:** no hermetic seam (COM) — `dotnet build` + existing suites (behavior-preserving).
- **Fails-if:** the Properties↔ToolWindow cross-type match diverges between `CompareWindows` and
  `LinkedTo` (two comparison implementations remain).

### BP-16 (N11, minor) — Key the linked-window cache on the active window
- **Files:** `MyExtension/Navigation/WindowNavigator.cs`
- **Change:** In `BuildActiveWindows` (:87-101), key `_cachedLinked` on the active window (not just the
  adapters-list reference) or clear it per navigation — today the static cache (:26-27) is keyed only
  on the adapters-list reference and never cleared.
- **Verify-with:** no hermetic seam (COM) — `dotnet build` + existing suites.
- **Fails-if:** a stale `_cachedLinked` is served after the active window changes while the adapters
  list reference is unchanged.

### BP-17 (N12, minor) — Validate the active window rect
- **Files:** `MyExtension/Navigation/WindowNavigator.cs`
- **Change:** In `NavigateInDirection` (:108-156), validate the active window's rect before
  `SelectTarget` — an empty active rect anchors selection around `(0,0,0,0)`; on empty, return
  `NavigationOutcome.NoOp(...)` so the `navigate no-op:` diagnostic fires.
- **Verify-with:** no hermetic seam (COM) — `dotnet build` + existing suites; the diagnostic
  `[NeoVisual] navigate no-op: <reason>` fires for an empty active rect.
- **Fails-if:** navigation proceeds with an empty active rect (selection anchored at (0,0,0,0)) instead
  of a no-op.

### BP-18 (N13, minor) — Add the UI-thread assert to TryGetScreenRect
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** Add `ThreadHelper.ThrowIfNotOnUIThread()` to `TryGetScreenRect` (:213, internal,
  COM-touching).
- **Verify-with:** `dotnet build` (no hermetic seam).
- **Fails-if:** `TryGetScreenRect` is callable from a background thread without throwing.

### BP-19 (N15, minor) — Carry the active-window index through the snapshot
- **Files:** `MyExtension/Navigation/WindowNavigator.cs`
- **Change:** Replace `_activeWindows.IndexOf(_activeWindow)` (:138, an O(n) reference scan per
  navigation) with the index carried through the snapshot (or a dictionary lookup).
- **Verify-with:** no hermetic seam (COM) — `dotnet build` + existing suites.
- **Fails-if:** the active-window index is still resolved by an O(n) `IndexOf` per navigation.

### BP-20 (N59, nit) — Make `_dte` readonly + drop the dead null checks
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** Make `_dte` `readonly` (:18) and drop the dead `_dte == null` checks (:41, :48) on the
  non-nullable field.
- **Verify-with:** `dotnet build` (no hermetic seam).
- **Fails-if:** `_dte` is still mutable or the dead null checks remain.

### BP-21 (N60, nit) — Fix the stale WindowNavigator doc comment
- **Files:** `MyExtension/Navigation/WindowNavigator.cs`
- **Change:** Update the stale doc comment "initalize windowmatrix and track windows" (:30) to the
  post-restructure wording (no `CardinalMovment`/`windowmatrix`).
- **Verify-with:** `dotnet build` + `pwsh tools/lint/check-doc-refs.ps1` (PASS — no stale backticked ref).
- **Fails-if:** the comment still references the pre-restructure "windowmatrix" name.

### BP-22 (N72, nit) — Make the n19 "logged once" flag session-scoped
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** Make `_loggedEmptyRect` (:20) static (or session-scoped) so the `window rect unavailable`
  diagnostic is logged once per session, not once per adapter instance (adapters are recreated per
  focus change → log spam today).
- **Verify-with:** `dotnet build` (no hermetic seam); the diagnostic
  `[NeoVisual] window rect unavailable; using empty rect` appears at most once per session.
- **Fails-if:** `window rect unavailable` is emitted once per adapter recreation (log spam).

## Phase 3 — Tool-window controllers + FocusGuard routing (N16, N17, N18, N19, N20, N21, N22, N23, N25, N26, N51, N61, N62, N71)

**Mid-point verify (Phase 3 boundary):** `dotnet run --project tests/NeoVisual.Tests -- FocusGuard` and
`dotnet run --project tests/NeoVisual.Tests -- FocusKeeperSchedule` pass; `dotnet build` 0 errors.

### BP-23 (N16, minor) — Single resolution path for WindowManager controllers
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`
- **Change:** Make `ResolveController` (:173-179, test-only static) delegate to `GetController`
  (:181-200) so the registered→default resolution has ONE implementation (the instance `GetController`
  is the single path; `ResolveController` becomes a thin wrapper or is folded in).
- **Verify-with:** `Run_WindowManager_*` (NeoVisual.Tests, pure static) — behavior-preserving refactor,
  no RED.
- **Fails-if:** the registered→default resolution logic still exists in two places.

### BP-24 (N17, minor) — Compose the 6-arg FocusGuard with the 5-arg
- **Files:** `MyExtension/ToolWindows/Utils/FocusGuard.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Rewrite the 6-arg `ShouldRouteToolWindowKey` (:50-51) to compose with the 5-arg overload
  (:41-42) and add the shift gate on top:
  `ShouldRouteToolWindowKey(isToolWindow, editorFocused, isInputMode, isTextInputSurface, textInputSurfaceFocused) && !(shiftHeld && !isTextInputSurface)`.
  Removes the duplicated editor-veto logic.
- **Verify-with:** add a truth-table test that the 6-arg == 5-arg for all non-shift inputs
  (`Run_FocusGuard_*`, NeoVisual.Tests) — behavior-preserving, not RED-provable.
- **Fails-if:** the 6-arg still re-implements the editor veto inline (two veto formulations remain).

### BP-25 (N18, minor) — Delegate FirstPathMatching to the shared walker
- **Files:** `MyExtension/ToolWindows/Utils/HierarchyResolver.cs`, `Telescope/Finders/Utils/HierarchyWalker.cs`,
  `tests/NeoVisual.Tests/Program.cs`
- **Change:** Add a name-contains matcher to `HierarchyWalker` (e.g. `FirstPathContaining(roots, query)`
  matching `Path.GetFileName(node.Path)` OrdinalIgnoreCase — equivalent to `HierarchyNode.Name` for
  physical files) and delegate `HierarchyResolver.FirstPathMatching` (:52-68) to it, removing the
  hand-rolled forest recursion.
- **Verify-with:** `Run_HierarchyResolver_*` (NeoVisual.Tests, pure) — behavior-preserving refactor,
  no RED.
- **Fails-if:** `FirstPathMatching` still hand-rolls the recursion, or the delegated matcher changes the
  match semantics (name-contains vs path-contains).

### BP-26 (N19, minor) — Caret-relative slice for the WPF/WinForms motion paths
- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`
- **Change:** Apply the caret-relative slice (R18, `MotionSliceRadius` :30) to the WPF TextBox path
  (:104-107) and the WinForms path (:136-139) — today only the editor-view path (:121-123) slices;
  WPF/WinForms materialize the whole buffer per h/l/w/b/e key. Keep the
  `text-motion key=... caret=... len=... text='...'` diagnostic byte-identical (the `len=`/`text=`
  fields already carry the full-buffer values).
- **Verify-with:** no hermetic seam (WPF/WinForms) — `dotnet build` + existing suites; the diagnostic
  `[NeoVisual] text-motion key=... caret=... len=... text='...'` is unchanged (e2e at VERIFY
  E2E-CR72-6).
- **Fails-if:** a WPF/WinForms motion still materializes the whole buffer, or the `text-motion`
  diagnostic format drifts.

### BP-27 (N20, minor) — Unify the Solution Explorer H/L lambdas with J/K
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** Replace the H/L inline lambdas (:44-45, log + `TryMoveArrow`) with bare delegates matching
  J/K (:46-47) — the log line moves into `TryMoveArrow` (or a shared helper) so all four hjkl keys use
  one shape.
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` + existing suites; the diagnostics
  `[NeoVisual] solution-explorer expand` / `solution-explorer collapse` stay byte-identical.
- **Fails-if:** H/L still use a different lambda shape than J/K, or the expand/collapse diagnostics
  drift.

### BP-28 (N21, minor) — Route a/A/I through EnterInputMode/OnModeChanged
- **Files:** `MyExtension/ToolWindows/ToolWindowControllerBase.cs`
- **Change:** In `TextMotion` (:46), stop mutating `_isInputMode` via the `ref` param for a/A/I — route
  the insert placements through `EnterInputMode()`/`OnModeChanged()` so the mode-change side effects
  (caret restyle) fire.
- **Verify-with:** partial (base pure) — `dotnet build` + existing suites.
- **Fails-if:** a/A/I still bypass `OnModeChanged()` (the caret style is not updated on insert
  placement).

### BP-29 (N22, minor) — Store + cancel prior focus keepers
- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs`, `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** Both callers (SolutionExplorerController.cs:118, :244) store the returned `IDisposable`
  handle and dispose the prior keeper before starting a new `Run` — a superseded keeper's queued tick
  must not re-assert the old target (belt-and-suspenders on top of `FocusKeeper.Run`'s `_current?.Stop()`
  at :21).
- **Verify-with:** no hermetic seam (DispatcherTimer) — `FocusKeeperSchedule` (pure, tested) covers the
  schedule half; `dotnet build` + existing suites.
- **Fails-if:** a superseded keeper's queued tick re-asserts the old target after a new keeper starts.

### BP-30 (N23, minor) — Map InsertEnd to the true line end
- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`
- **Change:** In `ApplyMotionToBox` (:182), map `CaretPlacement.End` (A) to the TRUE line end — resolve
  the full line (not the caret-relative slice boundary) for lines >~4096 chars. The
  `textinput-enter-input end caret=...` diagnostic stays byte-identical.
- **Verify-with:** no hermetic seam — `dotnet build` + existing suites; e2e at VERIFY E2E-CR72-6 asserts
  `A` lands at the true line end.
- **Fails-if:** `A` on a >4096-char line lands at the slice boundary instead of the true line end.

### BP-31 (N25, minor) — Guard the VSFPROPID_Type cast in the selection callback
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`
- **Change:** In `OnWindowFocusChanged` (:260-261), guard the `(int)value` cast on `VSFPROPID_Type`
  (try/catch or `is` check) so an `InvalidCastException` cannot escape the `IVsSelectionEvents`
  callback.
- **Verify-with:** no hermetic seam (VS callback) — `dotnet build` + existing suites.
- **Fails-if:** a non-int `VSFPROPID_Type` value throws `InvalidCastException` out of the selection
  callback.

### BP-32 (N26, minor) — FocusKeeperSchedule returns Stop after MaxEscapeAttempts
- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `FocusKeeperSchedule.Decide` (:94-105), return `Decision.Stop` (not `Reassert`) after
  `MaxEscapeAttempts` with the box still focused — the keeper must not fight the user.
- **Verify-with:** `Run_FocusKeeperSchedule_*` (NeoVisual.Tests, pure) — RED: a test asserting `Stop`
  after `MaxEscapeAttempts` fails today (returns `Reassert`).
- **Fails-if:** `Decide` returns `Reassert` after `MaxEscapeAttempts` with the search box still focused.

### BP-33 (N51, minor) — Add unit tests for the uncovered TextMotionHelper motion dispatch
- **Files:** `tests/NeoVisual.Tests/Program.cs`
- **Change:** Add `Run_TextMotionHelper_*` tests for the motion dispatch in `TryMoveFocusedSurface`
  (via the `TextMotionNavigator`/`TextMotionDispatcher` pure seams). **NOTE:** `FocusKeeperSchedule` is
  ALREADY covered by `Run_FocusKeeperSchedule_TruthTable` (tests/NeoVisual.Tests/Program.cs:1228) — do
  NOT claim it is uncovered; only `TextMotionHelper.TryMoveFocusedSurface` is genuinely uncovered.
- **Verify-with:** new `Run_TextMotionHelper_*` (NeoVisual.Tests) — RED: `TryMoveFocusedSurface`'s
  motion dispatch is uncovered today (no test exists). The existing
  `Run_FocusKeeperSchedule_TruthTable` (:1228) already covers the schedule decision.
- **Fails-if:** the new `Run_TextMotionHelper_*` tests are absent, or the motion behavior is still
  verified only by e2e.

### BP-34 (N61, nit) — Merge the near-identical hierarchy DTOs
- **Files:** `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs`,
  `MyExtension/ToolWindows/Utils/HierarchyResolver.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** Keep `HierarchyNode` (the resolver's DTO, referenced by spec.md:74) as the single DTO;
  delete `HierarchyItemInfo` (HierarchyForestBuilder.cs:13) and update `HierarchyForestBuilder.Build` +
  the tests (tests/NeoVisual.Tests/Program.cs:584-668) to use `HierarchyNode`.
- **Verify-with:** `Run_HierarchyResolver_*` / `Run_HierarchyForestBuilder_*` (NeoVisual.Tests) —
  behavior-preserving refactor, no RED; `pwsh tools/lint/check-doc-refs.ps1` PASS (spec.md:74
  `HierarchyNode` ref stays valid).
- **Fails-if:** `HierarchyItemInfo` remains, or the merged DTO changes the forest-build output.

### BP-35 (N62, nit) — Single-source the `_actions` dict pattern in the base
- **Files:** `MyExtension/ToolWindows/ToolWindowControllerBase.cs`,
  `MyExtension/ToolWindows/SolutionExplorerController.cs`, `MyExtension/ToolWindows/TextInputToolWindowController.cs`
- **Change:** Move the `_actions` dict + `TryMove` dict-lookup pattern into `ToolWindowControllerBase`
  (a protected `_actions` + a base `TryMove` that looks up the dict), so `SolutionExplorerController`
  and `TextInputToolWindowController` stop duplicating it.
- **Verify-with:** partial — behavior-preserving refactor; `dotnet build` + existing suites.
- **Fails-if:** the `_actions` dict + `TryMove` lookup pattern still exists in more than one place.

### BP-36 (N71, nit) — Pass the resolved search box through ExitInputMode
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** In `ExitInputMode` (:54-75), pass the already-resolved `focusedBox` (:61) through to
  `StyleFocusedSurface` (via `OnModeChanged`) so the visual tree is not re-walked per Esc.
- **Verify-with:** no hermetic seam (WPF) — `dotnet build` + existing suites.
- **Fails-if:** `StyleFocusedSurface` still re-walks the visual tree per Esc.

## Phase 4 — Input/hook hot path (N29, N30, N56, N57, N58, N69, N70)

**Mid-point verify (Phase 4 boundary):** `dotnet run --project tests/NeoVisual.Tests -- Keybinding`
passes; `dotnet build` 0 errors.

### BP-37 (N29, minor) — Reset the leader sequence on the popup-navigation branch
- **Files:** `MyExtension/Input/InputHandler.cs`
- **Change:** In `HandleKey`, the Ctrl+N/P popup-navigation branch (:295-298) runs before the leader
  state machine — call `ResetSequence()` when it fires so an in-progress leader sequence is not left
  dangling.
- **Verify-with:** no hermetic seam (InputHandler VS-coupled) — `dotnet build` + existing suites.
- **Fails-if:** a Ctrl+N/P during an in-progress leader sequence leaves the leader state active.

### BP-38 (N30, minor) — Cache the KeyNameBuilder canonical string
- **Files:** `MyExtension/Input/Utils/KeyNameBuilder.cs`
- **Change:** In `Build` (:11-19), cache the built string (or use a pooled builder) so the hook hot
  path stops allocating a fresh `StringBuilder` + string per call. The canonical string (e.g. `Ctrl+H`,
  `Shift+F4`, `Alt+X`) must stay byte-identical.
- **Verify-with:** `Run_KeyNameBuilder_*` (NeoVisual.Tests, pure) — behavior-preserving (the canonical
  string stays identical); not RED-provable (perf).
- **Fails-if:** the canonical shortcut string changes, or a fresh allocation remains per call.

### BP-39 (N56, nit) — Extract the shared KeybindingConfig merge helper
- **Files:** `MyExtension/Input/Utils/KeybindingConfig.cs`
- **Change:** Extract one shared merge helper from `Load` (:75-108), `LoadFromJson` (:115-121),
  `LoadDefaults` (:126-132) — each currently builds the same bindings-dict + leaderKey + `ApplyJson`
  prologue.
- **Verify-with:** `Run_KeybindingConfig_*` (NeoVisual.Tests, pure) — behavior-preserving refactor,
  no RED.
- **Fails-if:** the three load methods still duplicate the merge prologue.

### BP-40 (N57, nit) — Remove the unused `using System.Diagnostics;`
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`, `MyExtension/Package/Utils/TelescopeLauncher.cs`
- **Change:** Remove `using System.Diagnostics;` from MyExtensionPackage.cs:8 and TelescopeLauncher.cs:5
  ONLY — `GlobalKeyboardHook.cs:6` IS used (Process :56/:170) and must stay.
- **Verify-with:** `dotnet build` 0 errors (no hermetic seam).
- **Fails-if:** the build fails with an unused-using warning-as-error, or `GlobalKeyboardHook.cs`'s
  `using System.Diagnostics;` is wrongly removed.

### BP-41 (N58, nit) — Use the Log() helper in the hook ctor
- **Files:** `MyExtension/Hooks/GlobalKeyboardHook.cs`
- **Change:** In the ctor (:66), replace the inlined `NeoVisualLog.Log($"{DiagnosticLog.Hook}starting")`
  with the `Log()` helper that prepends the prefix. The emitted `[Hook] starting` line stays
  byte-identical.
- **Verify-with:** `dotnet build` (no hermetic seam); the diagnostic `[Hook] starting` is emitted with
  the prefix.
- **Fails-if:** the ctor still inlines the prefix, or the `[Hook] starting` line drifts.

### BP-42 (N69, nit) — Null-check `_launcher` in the hook init step
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`
- **Change:** Replace the null-forgiving `_launcher!` (:168) with a null-check that logs the failure
  (`[MyExtension] init hook failed: ...`) instead of crashing the hook step with
  `ArgumentNullException`.
- **Verify-with:** no hermetic seam (package init) — `dotnet build` + existing suites; the diagnostic
  `[MyExtension] init hook failed: ...` fires when `_launcher` is null.
- **Fails-if:** a null `_launcher` crashes the hook init step with `ArgumentNullException` instead of a
  logged failure.

### BP-43 (N70, nit) — Reject modifier keys in ParseLeader
- **Files:** `MyExtension/Input/Utils/KeybindingConfig.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** In `ParseLeader` (:216-223), reject modifier keys (Ctrl/Shift/Alt/Win) and non-single
  keys — `Enum.TryParse` today accepts `"Ctrl"`, silently disabling the leader key.
- **Verify-with:** `Run_KeybindingConfig_*` (NeoVisual.Tests, pure) — RED: `ParseLeader("Ctrl")`
  succeeds today; the fix rejects it (falls back to `Keys.Space`).
- **Fails-if:** `ParseLeader("Ctrl")` still returns a modifier key as the leader.

## Phase 5 — Telescope overlay/finders/filter/logging (N27, N31, N32, N33, N34, N35, N36, N37, N38, N39, N40, N41, N42, N43, N44, N45, N63, N64, N65, N66, N67, N68)

**Mid-point verify (Phase 5 boundary):** `dotnet run --project tests/Telescope.Tests` (full suite)
passes; `dotnet build` 0 errors.

### BP-44 (N31, minor) — Prompt `a` inserts after the caret
- **Files:** `Telescope/Overlay/Utils/PromptMotionRouter.cs`, `Telescope/Overlay/Utils/OverlayKeyHandler.cs`,
  `Telescope/Overlay/TelescopeOverlay.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Add `CaretPlacement.AfterCaret` to the enum (OverlayKeyHandler.cs:51-56, currently
  `Current/End/Start`); map `TextMotion.InsertAfter` (`a`) to `AfterCaret` in `PromptMotionRouter`
  (:33, today `Current`); add a `TelescopeOverlay.EnterInsert` case that places the caret at caret+1;
  update `Run_PromptMotionRouter_InsertPlacementsNotConsumed` (currently asserts `Current` for bare `a`).
- **Verify-with:** `Run_PromptMotionRouter_*` (Telescope.Tests, pure) — RED: a test asserting `'a'` →
  `AfterCaret` fails today (returns `Current`). Diagnostic `[Telescope] prompt-motion key=... caret=...`
  unchanged for h/l/w/b/e/0/$.
- **Fails-if:** bare `a` in the prompt still behaves identically to `i` (caret at current position, not
  caret+1).

### BP-45 (N32, minor) — Warm the GrepFinder content cache at overlay open
- **Files:** `Telescope/Finders/GrepFinder.cs`
- **Change (chosen: warm the cache at open):** In `GetCandidates` (:64-125), warm the shared
  `FileContentCache` for the solution's project files when the Grep overlay opens (a one-time
  pre-read), so the per-query scan hits a warm cache instead of re-reading every file on the UI thread
  per debounced query. Do **NOT** also offload the scan to a background thread in this step (single
  implementation; the warm-cache path is the chosen fix).
- **Verify-with:** partial (test ctor) — `dotnet build` + existing suites; the diagnostic
  `[Telescope] grep hits=...` stays byte-identical; e2e at VERIFY (E2E-CR72-1..10) keeps
  `telescope-grep` GREEN.
- **Fails-if:** the per-query scan still re-reads every project file with a cold cache.

### BP-46 (N33, minor) — Consolidate the three mtime-keyed caches
- **Files:** `Telescope/Overlay/Utils/PreviewTokenCache.cs`, `Telescope/Overlay/Utils/PreviewDocumentCache.cs`,
  `Telescope/Overlay/Utils/PreviewRenderer.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Delete `PreviewDocumentCache` (a strict subset of `PreviewTokenCache` — both mtime-keyed)
  and serve the FlowDocument-rebuild decision from the surviving cache (or `FileContentCache`). Update
  `Run_PreviewDocumentCache_UnchangedMtimeSkipsRebuild` (Telescope.Tests:1995) to drive the surviving
  cache so the rebuild-decision behavior stays covered.
- **Verify-with:** `Run_*Cache_*` (Telescope.Tests, pure) — behavior-preserving refactor, no RED; the
  diagnostics `[Telescope] preview tokens=...` / `[Telescope] preview caret=... line=...` stay
  byte-identical.
- **Fails-if:** the rebuild decision regresses (preview re-renders per selection), or a cache test is
  deleted without preserving the behavior.

### BP-47 (N34, minor) — Single read on a preview cache miss
- **Files:** `Telescope/Overlay/Utils/PreviewRenderer.cs`, `Telescope/Overlay/Utils/PreviewTokenCache.cs`
- **Change:** In `PreviewRenderer.Show` (:54) + `SetContent` (:95), pass the content read by
  `_contentCache` through to the token cache so a cache miss reads the file ONCE (today `Show` reads via
  `_contentCache`, then `SetContent` re-reads via `_tokenCache`'s `_contentReader`).
- **Verify-with:** partial — `dotnet build` + existing suites; the diagnostics
  `[Telescope] preview file=... chars=...` / `preview tokens=...` stay byte-identical.
- **Fails-if:** a preview cache miss still reads the file twice.

### BP-48 (N35, minor) — Cache the preview brushes
- **Files:** `Telescope/Overlay/Utils/PreviewRenderer.cs`
- **Change:** In `ColorFor` (:170-181), cache the `SolidColorBrush` per `SyntaxCategory` (static
  readonly) instead of allocating a new brush per segment on every `FlowDocument` rebuild.
- **Verify-with:** no hermetic seam (WPF) — `dotnet build` + existing suites; the diagnostic
  `[Telescope] preview tokens=...` stays byte-identical.
- **Fails-if:** a new `SolidColorBrush` is still allocated per segment.

### BP-49 (N36, minor) — Single MapKey table in TextMotionDispatcher
- **Files:** `Telescope/Overlay/Utils/TextMotionDispatcher.cs`
- **Change:** Replace the two parallel `MapKey` switch tables (WinForms `Keys` :46-64 vs WPF `Key`
  :72-96) with one canonical key→motion table mapped once and translated per surface.
- **Verify-with:** `Run_TextMotionDispatcher_*` (Telescope.Tests, pure) — behavior-preserving refactor,
  no RED.
- **Fails-if:** the WinForms and WPF key→motion mappings drift (two tables remain).

### BP-50 (N37, minor) — FileFinder shares the project-file cache
- **Files:** `Telescope/Finders/FileFinder.cs`, `MyExtension/Package/MyExtensionPackage.cs`
- **Change:** Add a public `FileFinder(Func<DTE>, ProjectFileCache)` ctor (the production path at
  FileFinder.cs:85-87 already handles `_fileCache != null`) and register FileFinder with the shared
  cache at MyExtensionPackage.cs:101 (today it re-walks the DTE tree per open via the no-cache ctor;
  CodeIssuesFinder :102 + GrepFinder :103 already share the cache).
- **Verify-with:** partial (test ctor) — `dotnet build` + existing suites; the diagnostic
  `[Telescope] opened file: ...` stays byte-identical.
- **Fails-if:** FileFinder still re-walks the DTE tree per open (no shared cache).

### BP-51 (N27, minor) — Cache the IsWrittenTo PropertyInfo
- **Files:** `MyExtension/Package/RoslynGatherers.cs`
- **Change:** In `IsWriteLocation` (:285-298), resolve the `PropertyInfo` once per type (static cache)
  instead of per reference location — today `GetProperty("IsWrittenTo")` reflection runs per location on
  the UI thread.
- **Verify-with:** no hermetic seam (Roslyn reflection) — `dotnet build` + existing suites; the
  diagnostics `[Telescope] references gathered reads=... writes=...` /
  `opened reference: ... access=read|write` stay byte-identical.
- **Fails-if:** `GetProperty("IsWrittenTo")` still runs per reference location.

### BP-52 (N38, minor) — Make the fzf availability probe async
- **Files:** `Telescope/Filter/FzfFilter.cs`
- **Change (chosen: async probe):** Change `IsAvailable()` (:75-83) to `IsAvailableAsync()` returning
  `Task<bool>`, running the `WaitForExit(500)` probe (:98) inside `Task.Run` so the UI thread is not
  blocked; the overlay's open path awaits it (the open path is already async). The availability result
  stays cached once per session. Do **NOT** keep a synchronous `IsAvailable()` overload (single
  implementation).
- **Verify-with:** `Run_FzfFilter_*` (Telescope.Tests, hermetic) — the async probe returns the cached
  availability; `dotnet build` + existing suites; the diagnostic
  `[Telescope] fzf unavailable — showing unfiltered list` stays byte-identical; e2e at VERIFY
  E2E-CR72-8.
- **Fails-if:** the first overlay open still blocks the UI thread on the 500ms fzf probe.

### BP-53 (N39, minor) — FilterAsync checks the cached availability
- **Files:** `Telescope/Filter/FzfFilter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** At the top of `FilterAsync` (:116-127), check the cached `_availability` — when false,
  return the unfiltered list WITHOUT spawning (today every keystroke attempts `p.Start()` →
  `Win32Exception` + `fzf filter failed` log when fzf is missing).
- **Verify-with:** `Run_FzfFilter_*` (Telescope.Tests, hermetic, `AwaitedReadCount` seam) — RED: with
  `_availability=false`, `FilterAsync` still spawns today; the fix returns unfiltered without spawning.
- **Fails-if:** `FilterAsync` spawns fzf when `_availability` is false (per-keystroke `fzf filter
  failed` spam).

### BP-54 (N40, minor) — Remove the dead PaneFailureTracker retry latch
- **Files:** `Telescope/Logging/Utils/PaneFailureTracker.cs`, `Telescope/Logging/NeoVisualLog.cs`,
  `tests/Telescope.Tests/Program.cs`
- **Change:** Remove `_retryAllowed` (:15), `ShouldRetry()` (:35), `RecordAttempt()` (:38-41) and the
  `!ShouldRetry()` guard in `NeoVisualLog.EnsurePane` (:133) + the `RecordAttempt()` call (:168); delete
  the now-dead `Run_PaneFailureTracker_RetryAfterFailure` test (Telescope.Tests:482). The
  `[NeoVisual] output pane unavailable: {reason}` fallback behavior is unchanged.
- **Verify-with:** `Run_PaneFailureTracker_*` (Telescope.Tests, pure) — behavior-preserving; re-run
  `pwsh tools/lint/check-doc-refs.ps1` after the test deletion (PASS).
- **Fails-if:** the dead latch remains, or the test deletion breaks `check-doc-refs.ps1`.

### BP-55 (N41 + N63, minor + nit) — FilterFailureLog returns the prefixed line (emitted exactly once)
- **Files:** `Telescope/Logging/Utils/FilterFailureLog.cs`, `Telescope/Overlay/TelescopeOverlay.cs`,
  `tests/Telescope.Tests/Program.cs`, `docs/spec.md`
- **Change:** Make `FilterFailureLog.Format` (:14) return the PREFIXED line
  (`DiagnosticLog.Telescope + "filter failed: " + ex.Message`) so a wrong logger cannot silently break
  the `filter failed:` contract; keep the class (spec.md:121 references it) — the N63 "inline it" option
  is folded in by keeping the one-liner but making it self-contained. **CRITICAL — the prefix must be
  emitted exactly once:** the only caller `TelescopeOverlay.cs:418` currently wraps the result in
  `TelescopeLog.Log(...)`, which prepends `[Telescope]` AGAIN → `[Telescope] [Telescope] filter
  failed: ...`. Switch that call site to `NeoVisualLog.Log(FilterFailureLog.Format(ex))` (the
  `Format()` result already carries the prefix; `NeoVisualLog.Log` adds none). Update the existing
  `Run_FilterFailureLog_Format` (tests/Telescope.Tests/Program.cs:2652-2657) to assert the PREFIXED
  form: `Assert.Equal("[Telescope] filter failed: boom", FilterFailureLog.Format(new Exception("boom")))`
  (today it asserts the unprefixed `"filter failed: boom"` and would fail). Update the now-stale
  spec.md §2.2:121 note ("unprefixed — callers log via `TelescopeLog`").
- **Verify-with:** `Run_FilterFailureLog_Format` (Telescope.Tests) — RED: the updated test asserting
  the `[Telescope]` prefix fails today (the current `Format()` returns the unprefixed string). The
  emitted `[Telescope] filter failed: {msg}` line stays byte-identical (exactly one prefix).
- **Fails-if:** `Format()` still returns an unprefixed string, the caller still double-prefixes
  (`[Telescope] [Telescope] filter failed: ...`), `Run_FilterFailureLog_Format` still asserts the
  unprefixed form, or spec.md:121 still claims the unprefixed contract.

### BP-56 (N42, minor) — Sanitize the CodeIssuesFinder display
- **Files:** `Telescope/Finders/CodeIssuesFinder.cs`
- **Change:** In `ToEntry` (:103), sanitize/truncate `issue.Text` (single-line, bounded) so a multi-line
  Error List description cannot break fzf/`ResultMapper` or the results TextBox.
- **Verify-with:** partial — `dotnet build` + existing suites; the diagnostics
  `[Telescope] opened issue: ... line=...` stay byte-identical.
- **Fails-if:** a multi-line Error List description still breaks the display/result mapping.

### BP-57 (N43, minor) — Sanitize user text in log lines
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`, `Telescope/Overlay/Utils/ResultMapper.cs`
- **Change:** Sanitize user-controlled text before interpolating into log lines —
  `promptChanged query='{query}'` (TelescopeOverlay.cs:348), `textinput='{e.Text}' focused=...` (:211),
  `result-mapper unknown display: {m}` (ResultMapper.cs:60) — strip newlines/control chars (mirror the
  R39 `SanitizeSample` pattern in TextMotionHelper.cs:231-244). The diagnostic formats stay
  byte-identical for normal input.
- **Verify-with:** no hermetic seam (WPF) — `dotnet build` + existing suites; e2e at VERIFY E2E-CR72-9
  asserts no line splitting.
- **Fails-if:** a `\n`/control char in user text still splits a `[Telescope]` log line.

### BP-58 (N44, minor) — Invalidate ProjectFileCache on a bounded TTL
- **Files:** `Telescope/Finders/Utils/ProjectFileCache.cs`, `Telescope/Finders/GrepFinder.cs`,
  `Telescope/Finders/CodeIssuesFinder.cs`, `tests/Telescope.Tests/Program.cs`
- **Change (chosen: bounded TTL):** Add a bounded TTL to `ProjectFileCache` (e.g. 5s, with an
  injectable clock for tests) so a cached project-file list expires and the next `Get` re-walks —
  today it is invalidated only on solution-name change (GrepFinder.cs:99-103 +
  CodeIssuesFinder.cs:78-82), so added/removed files within a solution stay stale. Do **NOT** also
  wire a file-add/remove VS event (single implementation; the TTL is the chosen fix).
- **Verify-with:** `Run_ProjectFileCache_*` (Telescope.Tests, pure, injectable clock) — RED: a cache
  entry older than the TTL is still served today; the fix re-walks. `dotnet build` + existing suites.
- **Fails-if:** a file added/removed within a solution is still served from the stale cache past the
  TTL.

### BP-59 (N45, minor) — Correct the QuoteArg backslash-before-quote escaping
- **Files:** `Telescope/Filter/FzfFilter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Correct `QuoteArg` (:233-238) for Windows argv rules — double each backslash run that
  PRECEDES a quote (backslashes-before-quotes in the MIDDLE of the string, e.g. `C:\"` or `a\b"c`,
  where the odd backslash is lost today). Trailing backslashes (`C:\`) are ALREADY handled (doubled for
  the closing quote at :236-237) — do not regress that path. net472-compatible (no
  `ProcessStartInfo.ArgumentList`).
- **Verify-with:** `Run_FzfFilter_*` (Telescope.Tests, hermetic) — RED: a test asserting a
  backslash-before-quote arg (e.g. `C:\"`) is quoted correctly fails today (`QuoteArg(@"C:\"")` returns
  `"C:\\""` instead of `"C:\\\""`). The existing `Run_FzfFilter_QuoteArg_TrailingBackslash`
  (:645-654) must stay GREEN (trailing-backslash + plain-quote cases unchanged).
- **Fails-if:** `QuoteArg(@"C:\"")` still produces a mis-escaped argument (the odd backslash before the
  quote is lost), or the trailing-backslash case (`C:\`) regresses.

### BP-60 (N64, nit) — Reduce SyntaxHighlighter complexity + avoid per-token Substring
- **Files:** `Telescope/Overlay/Utils/SyntaxHighlighter.cs`
- **Change:** Refactor `Tokenize` (:64-187, complexity 23) and avoid the per-token `text.Substring`
  allocations (use spans/offsets where net472 allows, or a single pass with start/length). The token
  output must stay identical — the `[Telescope] preview tokens=...` diagnostic.
- **Verify-with:** `Run_SyntaxHighlighter_*` (Telescope.Tests, pure) — behavior-preserving (the token
  output stays identical); no RED.
- **Fails-if:** the tokenizer output changes (a token boundary/color drifts), breaking
  `preview tokens=...`.

### BP-61 (N65, nit) — Gate the debug-file duplication
- **Files:** `Telescope/Logging/NeoVisualLog.cs`
- **Change:** Gate the `Debug.WriteLine` duplication (:102 → `NeoVisualTraceListener`) behind an env
  flag (or drop it) so every `NeoVisualLog.Log` line is not unconditionally duplicated to the debug
  file.
- **Verify-with:** no hermetic seam — `dotnet build` + existing suites.
- **Fails-if:** every `NeoVisualLog.Log` line is still duplicated to the debug file unconditionally.

### BP-62 (N66, nit) — TextMotionNavigator Down/Up boundary no-op
- **Files:** `Telescope/Overlay/Utils/TextMotionNavigator.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In `Down()` (:61-76), on the last line stay at the last position (no-op) instead of moving
  to `_text.Length`; in `Up()` (:79-102), on the first line stay put instead of moving to 0.
- **Verify-with:** `Run_TextMotionNavigator_*` (Telescope.Tests, pure) — RED: a test asserting `Down()`
  on the last line keeps the caret at the last position fails today (moves to `_text.Length`).
- **Fails-if:** `Down()` on the last line still moves the caret to `_text.Length`, or `Up()` on the
  first line still moves to 0.

### BP-63 (N67, nit) — Match the interpolated-verbatim string branch
- **Files:** `Telescope/Overlay/Utils/SyntaxHighlighter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** In `Tokenize`, match `$@"..."` (interpolated verbatim) — today the `'$' + '"'` branch
  (:128) misses it (next is `@`, not `"`), so it renders as Default.
- **Verify-with:** `Run_SyntaxHighlighter_*` (Telescope.Tests, pure) — RED: a test asserting `$@"..."`
  tokenizes as a string fails today.
- **Fails-if:** `$@"..."` still renders as `SyntaxCategory.Default`.

### BP-64 (N68, nit) — FileFinder logs only on an actual open
- **Files:** `Telescope/Finders/FileFinder.cs`
- **Change:** In `OpenHit` (:112-116), only log `opened file: {path}` when the open actually happened —
  today `_dteFactory()?.ItemOperations.OpenFile(path)` short-circuits on a null DTE but the log still
  fires.
- **Verify-with:** partial — `dotnet build` + existing suites; the diagnostic
  `[Telescope] opened file: ...` is emitted only on a real open.
- **Fails-if:** `opened file: ...` is logged when `_dteFactory()` returns null (no open happened).

## Phase 6 — Harness + lint (N9, N10, N52, N53)

**Mid-point verify (Phase 6 boundary):** `pwsh tools/lint/check-doc-content.ps1` PASSES 12/12;
`dotnet build` 0 errors.

### BP-65 (N9, major) — Assert a fresh post-tap line in telescope-open/-mode/-navigate
- **Files:** `tools/harness/test-e2e.ps1`
- **Change (chosen: assert a fresh post-tap line):** In `telescope-open` (:514-520),
  `telescope-mode`, `telescope-navigate` (:537-...), replace the assertion satisfied by
  `Open-Telescope`'s own wait line (baseline reset before the helper) with a fresh post-tap line the
  helper does NOT confirm: after `Open-Telescope` returns, send an explicit Escape then a key and
  assert a NEW `key=... mode=insert handled=True` line appears after the baseline. Do **NOT** simply
  drop the assertion (single implementation; the fresh-line gate is the chosen fix).
- **Verify-with:** no hermetic seam (harness-only) — verified by the harness `-SelfCheck` seam + e2e
  at VERIFY (E2E-CR72-4).
- **Fails-if:** the gate is still satisfied by `Open-Telescope`'s own wait line (a re-introduced
  prompt-focus/mode bug passes).

### BP-66 (N10, major) — Update check-doc-content.ps1 to the 51-findings state
- **Files:** `tools/lint/check-doc-content.ps1`
- **Change:** Update the three assertions hardcoded to the Phase-10 (98-findings) fixed state: DOC-64-3
  (:131) accepts the `E2E-NCR-*` wildcard or the concrete E2E-CR51 IDs; DOC-66-2 (:156) and DOC-66-3
  (:162) point at the current "Next up"/baseline (51-findings plan).
- **Verify-with:** `pwsh tools/lint/check-doc-content.ps1` PASSES 12/12 (RED today: 3/12 fail).
- **Fails-if:** the lint still fails 3/12 (assertions still hardcoded to the 98-findings state).

### BP-67 (N52, minor) — Pin WHICH file opens in explorer-open/-open-o
- **Files:** `tools/harness/test-e2e.ps1`
- **Change:** In `neovisual-explorer-open` (:716-731) and `neovisual-explorer-open-o` (:800-815),
  replace the "try l/j/Enter until ANY file opens" loop with an assertion that pins WHICH file opens
  (assert the specific `solution-explorer open` / `editor-view-opened file=` path).
- **Verify-with:** no hermetic seam (e2e) — e2e at VERIFY (E2E-CR72-10).
- **Fails-if:** the scenario still passes when ANY file opens (order-dependent, unpinned).

### BP-68 (N53, minor) — Update the seed-expected copy only on success
- **Files:** `tools/harness/test-e2e.ps1`
- **Change:** In `neovisual-editor-insert` (:1080-1083), move `Update-SeedExpected` out of the `finally`
  so a FAILED marker assertion does not record the wrong content as expected.
- **Verify-with:** no hermetic seam (e2e) — e2e at VERIFY (E2E-CR72-10).
- **Fails-if:** a failed marker assertion still updates the seed-expected copy.

## Phase 7 — Docs (N54, N55)

**Mid-point verify (Phase 7 boundary):** `pwsh tools/lint/check-doc-refs.ps1` PASSES.

### BP-69 (N54, minor) — Add FIXED annotations to architecture-review.md
- **Files:** `docs/reviews/architecture-review.md`
- **Change:** Add FIXED annotations to F1 (:269), F16 (:284), F45 (:313) and the detailed sections
  (:318, :395) — all fixed per `docs/progress.md`.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS (no hermetic seam — docs).
- **Fails-if:** F1/F16/F45 are still presented as open with no FIXED annotation.

### BP-70 (N55, minor) — Correct the code-review.md R50 reference
- **Files:** `docs/reviews/code-review.md`
- **Change:** Update the code-review.md reference that cites R50 (from the superseded 98-findings
  review) to reference the correct finding N50 (code-review.md:66) — the R50 premise is no longer true
  since architecture-review.md:263 uses the post-restructure names.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` PASS (no hermetic seam — docs).
- **Fails-if:** the code-review.md reference still cites R50 from the superseded review.

---

## Verification Trace

| failing test / gate | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_FocusGuard_InputModeBlocksActionKeys` (corrected, N1) | BP-1 | `Assert.True(FocusGuard.ShouldRouteToolWindowKey(...))` — guard routes in input mode (no constant-false composite) |
| `Run_FocusGuard_ZeroActionKeysBlocks` (corrected, N2) | BP-2 | `Assert.True(...)` — guard routes a tree-focused tool window regardless of action-key count |
| `Run_FocusGuard_*` (dedupe/merge, N46/N47/N48) | BP-3, BP-4, BP-5 | suite compiles + passes; no duplicate/tautological assertion remains |
| `Run_ResultMapper_UnknownStringSkippedOrLogged` DELETED (N49) | BP-6 | test deleted; the sibling `Run_ResultMapper_UnknownStringNullPayload` (:2707-2718) asserts the same skip-behavior non-vacuously with `Assert.Equal(0, items.Count)` (unmatched "Ghost.cs" skipped → items empty) |
| `Run_WindowManager_DefaultControllerCache_*` (N50) | BP-7 | `ThreadHelper.uiThreadDispatcher` restored in `finally` |
| `Run_FileContentCache_EvictsOldest` (N8) | BP-8 | per-path STABLE timestamp — deterministic AND non-vacuous (a non-evicted "a" hits; a broken eviction fails) |
| `block-caret active=False` (N4) | BP-9 | `[NeoVisual] block-caret active=False` truthful — adornment removed on deactivation |
| `Run_VimBufferSubscriptions_*` (N3) | BP-10 | `_bufferToTextBuffer` entry removed at refcount 0; `Detach` decrements a non-Closed-subscribed shared-buffer view |
| `VsVim integration: not found` (N28) | BP-11 | `[NeoVisual] VsVim integration: not found` logged at most once per session |
| `editor-view-opened file=...` (N24) | BP-12 | one guarded `EditorViewOpenedLog.Emit` helper; `doc.Activate()` path KEEPS its direct emission; exactly once per open |
| `window rect unavailable` (N5/N72) | BP-13, BP-22 | `[NeoVisual] window rect unavailable; using empty rect` on a failed read, at most once per session |
| `NavigationSettings.Invalidate` (N6) | BP-14 | 0 callers (Trailmark) — `_cached` static removed; `FromSystemDpi` re-reads per navigation |
| Properties-quirk comparison (N7/N14) | BP-15 | one `MatchesPropertiesQuirk` helper used by both `CompareWindows` and `LinkedTo` |
| linked-window cache (N11) | BP-16 | `_cachedLinked` keyed on the active window / cleared per navigation |
| active rect validation (N12) | BP-17 | `[NeoVisual] navigate no-op: <reason>` on an empty active rect |
| `TryGetScreenRect` (N13) | BP-18 | `ThreadHelper.ThrowIfNotOnUIThread()` present |
| active-window index (N15) | BP-19 | no O(n) `IndexOf` per navigation |
| `_dte` readonly (N59) | BP-20 | `_dte` readonly; dead null checks removed |
| WindowNavigator doc comment (N60) | BP-21 | no "windowmatrix" pre-restructure name |
| `Run_WindowManager_*` (N16) | BP-23 | `ResolveController` delegates to `GetController` (single resolution path) |
| `Run_FocusGuard_*` 6-arg==5-arg (N17) | BP-24 | 6-arg composes with 5-arg + shift gate |
| `Run_HierarchyResolver_*` (N18) | BP-25 | `FirstPathMatching` delegates to the shared `HierarchyWalker` |
| `text-motion key=... caret=...` (N19/N23) | BP-26, BP-30 | `[NeoVisual] text-motion key=... caret=... len=... text='...'` unchanged; `A` lands at the true line end |
| `solution-explorer expand/collapse` (N20) | BP-27 | `[NeoVisual] solution-explorer expand` / `collapse` byte-identical |
| a/A/I mode-change (N21) | BP-28 | insert placements route through `EnterInputMode()`/`OnModeChanged()` |
| focus-keeper handle (N22) | BP-29 | prior keeper disposed on a new `Run` |
| `VSFPROPID_Type` cast (N25) | BP-31 | no `InvalidCastException` escapes the selection callback |
| `Run_FocusKeeperSchedule_*` (N26/N51) | BP-32, BP-33 | `Stop` after `MaxEscapeAttempts`; new `Run_TextMotionHelper_*` tests (schedule already covered by `Run_FocusKeeperSchedule_TruthTable` :1228) |
| `Run_HierarchyForestBuilder_*` (N61) | BP-34 | single `HierarchyNode` DTO; `HierarchyItemInfo` deleted |
| `_actions` dict pattern (N62) | BP-35 | pattern single-sourced in `ToolWindowControllerBase` |
| search-box re-walk (N71) | BP-36 | resolved box passed through `ExitInputMode` |
| leader reset on popup-nav (N29) | BP-37 | Ctrl+N/P resets an in-progress leader sequence |
| `Run_KeyNameBuilder_*` (N30) | BP-38 | canonical shortcut string byte-identical; no per-call allocation |
| `Run_KeybindingConfig_*` (N56/N70) | BP-39, BP-43 | shared merge helper; `ParseLeader("Ctrl")` rejected (RED) |
| unused `using System.Diagnostics;` (N57) | BP-40 | removed from MyExtensionPackage.cs:8 + TelescopeLauncher.cs:5 only |
| `[Hook] starting` (N58) | BP-41 | `[Hook] starting` emitted via the `Log()` helper, byte-identical |
| `_launcher` null (N69) | BP-42 | `[MyExtension] init hook failed: ...` logged instead of `ArgumentNullException` |
| `Run_PromptMotionRouter_*` (N31) | BP-44 | `'a'` → `CaretPlacement.AfterCaret` (RED today: `Current`) |
| `grep hits=...` (N32) | BP-45 | `[Telescope] grep hits=...` byte-identical; content cache warmed at overlay open |
| `Run_*Cache_*` (N33/N34) | BP-46, BP-47 | caches consolidated; single read on a preview cache miss; `preview tokens=...` unchanged |
| `preview tokens=...` (N35/N64/N67) | BP-48, BP-60, BP-63 | brushes cached; tokenizer output identical; `$@"..."` tokenizes as a string (RED) |
| `Run_TextMotionDispatcher_*` (N36) | BP-49 | single MapKey table |
| `opened file: ...` (N37/N68) | BP-50, BP-64 | FileFinder shares the cache; `[Telescope] opened file: ...` only on a real open |
| `references gathered reads=... writes=...` (N27) | BP-51 | `IsWrittenTo` PropertyInfo cached per type |
| `fzf unavailable — showing unfiltered list` (N38/N39) | BP-52, BP-53 | async `IsAvailableAsync` probe (off the UI thread); `FilterAsync` returns unfiltered without spawning when unavailable (RED) |
| `output pane unavailable: {reason}` (N40) | BP-54 | dead retry latch removed; fallback behavior unchanged |
| `filter failed: {msg}` (N41/N63) | BP-55 | `Format()` returns the prefixed line; caller switched to `NeoVisualLog.Log` (prefix emitted exactly once); `Run_FilterFailureLog_Format` asserts the prefixed form; spec.md:121 note updated |
| `opened issue: ... line=...` (N42) | BP-56 | display sanitized/truncated (single-line) |
| `[Telescope]` log lines (N43) | BP-57 | user text sanitized — no line splitting |
| ProjectFileCache staleness (N44) | BP-58 | bounded TTL (injectable clock) — stale entry re-walks past the TTL |
| `Run_FzfFilter_*` QuoteArg (N45) | BP-59 | `C:\"` (backslash-before-quote) quoted correctly (RED) |
| debug-file duplication (N65) | BP-61 | `Debug.WriteLine` duplication gated |
| `Run_TextMotionNavigator_*` (N66) | BP-62 | `Down()`/`Up()` no-op at the boundaries (RED) |
| `telescope-open`/`-mode`/`-navigate` gates (N9) | BP-65 | fresh post-tap line the helper does NOT confirm |
| `check-doc-content.ps1` (N10) | BP-66 | PASSES 12/12 (RED today: 3/12) |
| `neovisual-explorer-open`/`-open-o` (N52) | BP-67 | pins WHICH file opens |
| `neovisual-editor-insert` seed copy (N53) | BP-68 | `Update-SeedExpected` only on success |
| `check-doc-refs.ps1` (N54/N55) | BP-69, BP-70 | PASS — FIXED annotations added; R50 reference corrected to N50 |

**Known-RED allowlist:** none — all 35 e2e scenarios GREEN, unit suites 153/163 passing (per
`docs/progress.md`). The verification-agent must NOT flag any of the 72 findings' pre-fix state (e.g.
the tautological N1/N2 tests, the N8 flake, the N66 boundary behavior) as a regression — they are the
RED targets of this plan.

---

## Execution Log

### Attempt 1 — GREEN (2026-10-02)
- **Lane:** bugfix (e2e enabled).
- **BUILD:** executed in 5 scoped chunks (the 70 BP steps exceeded a single build-agent's step
  budget). `dotnet build` 0 errors; both unit suites GREEN (Telescope 157, NeoVisual 168).
- **DEVIATIONS adjudicated (M-M3):**
  - D1 (BP-44 enum `CaretPlacement.AfterCaret` added early as a compile fix) → **ACCEPT**
    (legitimate; BP-44 implements the behavior).
  - D2 (BP-39 `Merge` catches parse errors for all sources; `LoadFromJson`/`LoadDefaults`
    log-and-return instead of propagating) → **ACCEPT** (no test relies on propagation; `Load`'s
    error messages preserved).
  - D3 (BP-29 line refs stale; actual call sites ~:156/:292) → **ACCEPT** (adapted to real locations).
  - D4 (BP-50 ctor made `internal` not `public`; `ProjectFileCache` is internal) → **ACCEPT**
    (correct accessibility).
  - D5 (BP-52 required updating 3 test call sites + `TelescopeController.cs`) → **ACCEPT**
    (necessary for compilation).
  - D6 (BP-18 UI-thread assert required updating `Run_WindowAdapter_TryGetScreenRect_NullFrameReturnsNull`)
    → **ACCEPT** (necessary test update).
  - D7 (BP-44 new enum member required updating `Run_CaretPlacement_EnumValues`) → **ACCEPT**
    (necessary test update).
  - D8 (VERIFY-round-1 regression fixes: `TelescopeOverlay.EnterInsert` mode-flip;
    `WindowManager.ComputeTextInputSurfaceFocused` COM-DocView fallback) → **ACCEPT** (both fix real
    regressions caused by the plan's changes; no diagnostic format changed).
- **VERIFY round 1:** FAIL — 5 failures (2 harness-layer: BP-67 `Get-LastLogMatch` `ReadAllLines`
  file-lock, BP-65 incomplete `i` assertion; 3 product `i`-regressions: overlay `EnterInsert`
  mode-flip, Command Window `TextInputSurfaceFocused=False`). All fixed; re-verified.
- **VERIFY round 2 (final gate):** PASS — full 35-scenario suite GREEN (fresh boot), both unit suites
  GREEN, all harness-health checks PASS.
- **Failure-log sweep:** 6 existing entries read (all already FIXED); 3 new entries appended
  (1 `tool-bug` — the `dte-command.ps1` arg-binding bug, FIXED by this item; 2 `agent-syntax` —
  Unix `head` in pwsh, and the `-Tests a,b,c` native-boundary array binding; both added to the
  command-log known-bad index).
- **Cost:** `delegations: 13 | VS boots: 6 | iterations: 1`.
