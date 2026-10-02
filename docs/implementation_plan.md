# Plan — Code-Review Fixes (51 findings, 2026-10-02)

> **Lane: bugfix (unit-only, e2e deferred).**
> Source: `docs/reviews/code-review.md` (2026-10-02, whole-repo post-fix review of the
> 98-findings + restructure plan, commit `349fc05`, GREEN in the unit-only lane).
> **51 findings: 0 critical, 5 major, 22 minor, 24 nit.** The user requested fixes for
> ALL findings — including minors and nits.
>
> **Research:** 1 whole-repo `trailmark-recon` digest (1861 nodes, 760 proxies,
> 0 entrypoints) — 33/33 structural claim-groups CONFIRMED (current paths all correct
> post-restructure; R6 dead production code via proxy-aware `callers_of`; R14 genuinely
> dead — 0 plain + proxy; graph gaps: `PreviewRenderer.Show` + `FzfFilter.IsAvailable`
> are instance-field calls not resolved even via proxy — verify by file:line, not
> `callers_of`). 1 `arch-auditor` pass — 51/51 fix directions verified against current
> code, with **2 NEEDS-CORRECTION** (R1 surface-aware restriction; R3 IVimBuffer
> threading) + a cross-cutting-risk map. `feature-researcher` SKIPPED — internal
> bugfix, no LazyVim reference needed (prior-plan precedent).
>
> **Ground truth:** `dotnet build` = 0 errors (the LSP-reported errors are a stale
> index from the restructure). Repo is GREEN (commit `349fc05`). Unit suites:
> Telescope.Tests 151, NeoVisual.Tests 158 (all passing).
>
> e2e scenarios are DEFERRED to `e2e-queue.md` (status QUEUED) — this machine cannot
> boot the VS Experimental Instance. NO e2e RED/VERIFY step is planned; every fix is
> verified by unit tests (where a hermetic seam exists) + `dotnet build` + the existing
> unit suites, with the live behavior gated by the deferred e2e scenarios.

## Goal

Fix all 51 code-review findings (0 critical, 5 major, 22 minor, 24 nit) in dependency
order — the 4 functional majors first (prompt selection navigation, preview render
amortization, VimModeSource lifecycle, frame-enumeration fault isolation), then the
harness gate hardening, the FocusGuard/routing drift, the Vim interop dead surface, the
navigation robustness, the controller/state issues, the finder/overlay amortization, the
dead code + logging, the test hermeticity, and the docs drift — with every behavior
change proven by a RED unit test (or, where no hermetic seam exists, by build + existing
suites + the deferred e2e gate).

## Approach

The research passes produced a verified verdict + fix direction + unit-test seam for
every finding, plus a cross-cutting-risk map. The plan is organized into 10 phases in
dependency order (Phases 0-9). Cross-cutting constraints:

- **Log-line-as-contract:** several fixes touch lines the e2e harness asserts on
  (`prompt-motion key=... caret=...`, `[Telescope] preview caret=... line=...`,
  `[Telescope] focus target=List|Preview`, `[NeoVisual] text-motion key=... caret=...`,
  `[NeoVisual] toolwindow-move key=... -> arrow vk=...`, `[NeoVisual] vim-mode=...`,
  `[NeoVisual] solution-explorer select file=...`, `[NeoVisual] editor-view-opened file=...`).
  Any fix that changes a signature or a log site MUST preserve the emitted token
  byte-identical.
- **Proxy traps:** cross-class calls land on `proxy.unresolved:<Type>.<Member>`; a bare
  `callers_of` returning 0 is SUSPECT, not dead code. R6 (`FocusGuard.HasToolWindowActionKeys`)
  is dead PRODUCTION code (proxy → 9 test callers) — delete it but keep the tests' use
  in mind. R14 (`VimBufferSubscriptions.FocusedView/FocusedBuffer`) is genuinely dead
  (0 plain + proxy). `PreviewRenderer.Show` / `FzfFilter.IsAvailable` are instance-field
  calls the graph cannot resolve — never report them dead from `callers_of`.
- **Fix-direction corrections from research (fold into the phases):**
  1. **R1** — the restriction of `PromptMotionRouter.ShouldConsume` to h/l/w/b/e/0/$
     must be **surface-aware**: `HandlePreviewKey` (TelescopeOverlay.cs:628) ALSO calls
     the router and needs j/k/g/G (Down/Up/Top/Bottom) for the preview. Restrict the
     prompt surface only (a surface param, or apply the restriction in `TryPromptMotion`
     only). `OverlayAction.EnterInsertStart` is dead in production (only tests call
     `EnterInsertMode(Start)`) — wire `I` to it (insert at line start) per the review.
  2. **R3** — `UnsubscribeBuffer` only receives the `IVimTextBuffer`, but the `Closed`
     subscription is on the `IVimBuffer` (:254-255) — thread the buffer (via
     `_subscriptions.BufferFor`). The `OnBufferClosed` refcount decrement must NOT
     double-decrement with `Detach` (m40) — coordinate the two paths.
- **No-seam findings** (VS/WPF/harness-coupled, no hermetic unit surface): R4, R5, R7,
  R8, R10/R11 (as-is), R13, R15, R16, R17, R18, R21, R22 (debounce half), R28, R31,
  R32, R33, R34, R35, R37, R38, R39, R41, R42 (UI-thread half), R44 (SetText half),
  R46, R49. For these the plan states the honest verification (build + existing suites +
  deferred e2e) and, where possible, extracts a small pure seam to make part of the
  behavior unit-testable.
- **R45** is the documented/accepted n16 design (Ctrl+N/P unconditional swallow) — the
  review re-verified it as still-live accepted design. The "fix" is to keep it
  documented/accepted (no code change); the plan records this disposition.

---

# Part A — Code-review fixes (Phases 0-9)

## Phase 0 — Functional bugs (R1, R2, R3, R4)

- **R1** (major, CONFIRMED + NEEDS-CORRECTION) — the M1 fix over-broadened the prompt
  motion set: `j`/`k`/`g`/`G` are consumed in the prompt as caret motions
  (Down/Up/Top/Bottom) and never reach `OverlayKeyHandler` — selection navigation
  broken; `a`/`I` placement semantics wrong (a appends at END, I inserts at CURRENT);
  `OverlayAction.EnterInsertStart` dead. **Fix (surface-aware):** restrict the prompt
  motion set to h/l/w/b/e/0/$ in `TryPromptMotion` (NOT in the shared
  `PromptMotionRouter.ShouldConsume` — `HandlePreviewKey` needs j/k/g/G for the
  preview); capture the `insertPlacement` out param so `a`/`A`/`I` route to the correct
  `CaretPlacement` (after-caret / end / start); wire `I` to `EnterInsertMode(Start)`.
  **Unit seam (Telescope.Tests):** `Run_PromptMotionRouter_*` (pure, already tested) +
  `Run_OverlayKeyHandler_*` (pure) — RED: `ShouldConsume(Key.J, ...)` returns true for
  the prompt today (consumes j/k/g/G); `a`/`I` produce the wrong placements.
- **R2** (major, CONFIRMED) — preview rebuilds the whole `FlowDocument` + `LineIndex` +
  line pointers + `ScrollToHome()` on every selection change (M4 partial —
  `PreviewTokenCache` only caches tokenization). **Fix:** cache the built `FlowDocument`
  + `_linePointers` keyed by mtime (mirror `PreviewTokenCache`); only rebuild when
  content changes; keep `MoveToLine`/`ApplyCaret` as the per-selection work. **Unit seam
  (Telescope.Tests):** `Run_PreviewTokenCache_*` (pure, already tested) — extend the
  cache to the built document; RED: the document is rebuilt per selection change.
- **R3** (major, CONFIRMED + NEEDS-CORRECTION) — VimModeSource buffer-subscription
  lifecycle leak: `UnsubscribeBuffer` never removes the `Closed` subscription;
  `_subscribedBuffers` only cleared in `OnBufferClosed`; `OnBufferClosed` never
  decrements `_textBufferRefCounts`. **Fix:** remove the `Closed` subscription in
  `UnsubscribeBuffer` (thread the `IVimBuffer` via `_subscriptions.BufferFor` — the
  `Closed` sub is on the buffer, not the textBuffer) + `_subscribedBuffers.Remove(buffer)`;
  decrement `_textBufferRefCounts` in `OnBufferClosed` (removing the entry at 0) WITHOUT
  double-decrementing with `Detach` (m40). **Unit seam:** no-seam as-is (VsVim
  reflection); extract the lifecycle bookkeeping into a pure helper (mirroring
  `VimBufferSubscriptions`, which is tested) — RED: the helper's refcount/subscription
  invariants fail today.
- **R4** (major, CONFIRMED) — frame enumeration has no per-frame fault isolation:
  `ErrorHandler.ThrowOnFailure` + unguarded `GetWindowObject` abort the whole
  enumeration on one stale frame, escaping into the hook path. **Fix:** wrap each
  frame's `GetWindowObject` in try/catch and skip the bad frame (log once); wrap the two
  `ThrowOnFailure` calls (or the whole `Enumerate` body) to return an empty list on
  failure, mirroring the existing null-`uiShell` path. **Unit seam:** no-seam
  (VS-coupled COM) — build + existing suites + deferred e2e.

## Phase 1 — Harness gate hardening (R5, R13, R49)

- **R5** (major, CONFIRMED) — `telescope-mode` `i`/`a` insert-mode assertions are false
  positives (`Reset-LogBaseline` before `Open-Telescope` — the Open-Telescope
  `Focus prompt => True, mode=insert` line satisfies all three assertions). **Fix:**
  snapshot the log index just before the `i`/`a` tap and use a post-tap search for a NEW
  `Focus prompt => True, mode=insert`, or assert the `key=I mode=normal handled=True` /
  `key=A mode=normal handled=True` line for the tap. **Unit seam:** no-seam
  (harness-only) — verified by the harness `-SelfCheck` seam (test-e2e.ps1:1605+).
- **R13** (minor, CONFIRMED) — the "shell-wait" init step runs unconditionally on every
  package load — up to 20s (40×500ms) dead-key delay on a slow machine; exists only for
  e2e focus stability. **Fix:** gate it on `NEOVISUAL_TEST_SOLUTION`/`NEOVISUAL_LOG_DIR`
  being set (the e2e harness sets these); skip the wait in normal runs. **Unit seam:**
  no-seam (VS-coupled package init).
- **R49** (nit, CONFIRMED) — scenario order-dependence documented + self-checked, but
  subsets remain not independently runnable (e.g. `-Tests seed-leak` alone fails).
  **Fix:** make subsets self-seeding (the `seed-leak` scenario must work standalone) or
  document the limitation explicitly. **Unit seam:** no-seam (harness-only).

## Phase 2 — FocusGuard + routing (R6, R7, R10, R11, R12)

- **R6** (minor, CONFIRMED) — `FocusGuard.HasToolWindowActionKeys` dead production code
  (proxy-aware `callers_of` → 9 test callers only); doc comment still claims it is the
  hook pre-filter mechanism. **Fix:** delete the method + its `OwnsKeyboard` call site +
  fix the stale doc comment (AGENTS.md/SKILL.md document the pre-filter as
  `IsKeyOfInterest`/`ShouldRouteToolWindowKey`). **Unit seam (NeoVisual.Tests):**
  `Run_FocusGuard_*` (pure, already tested) — deletion, no RED.
- **R7** (minor, CONFIRMED) — `OnWindowFocusChanged` early-returns on null
  `CurrentWindow`, leaving `_isToolWindow`/`_type`/`_isTextInputType` stale from the
  previous frame. **Fix:** reset the state before the null-return. **Unit seam:**
  no-seam (VS-coupled).
- **R10** (minor, CONFIRMED) — action-key routing gates only `!ctrl && !alt`, not shift
  — `Shift+O/R/M/A/G` in Solution Explorer fire the same tree actions and swallow the
  key. **Fix:** gate shift for non-text-input controllers. **Unit seam:** no-seam as-is
  (`InputHandler` VS-coupled); extractable to `FocusGuard` (pure, NeoVisual.Tests).
- **R11** (minor, CONFIRMED) — `IsKeyOfInterest` returns true for every key while shift
  is held — every uppercase letter typed in the editor runs the full `HandleKey` path
  (hot-path cost). **Fix:** narrow the shift case in the pre-filter. **Unit seam:**
  no-seam as-is; extractable to `FocusGuard`.
- **R12** (minor, CONFIRMED) — `InjectedKeyGuard` TTL checked on consume, not on record —
  a >1s UI stall between `Press`'s Record and the injected key-down expires the record →
  re-injection storm. **Fix:** check/expire the TTL at Record time. **Unit seam
  (NeoVisual.Tests):** `Run_InjectedKeyGuard_*` (pure, already tested) — RED: a stale
  record is not expired at record time today.

## Phase 3 — Vim interop (R14, R46)

- **R14** (minor, CONFIRMED) — `VimBufferSubscriptions.FocusedView`/`FocusedBuffer`
  (and the `_focusedView` field) have zero callers (0 plain + proxy) — dead surface on
  the pure seam. **Fix:** delete them. **Unit seam (NeoVisual.Tests):**
  `Run_VimBufferSubscriptions_*` (pure, already tested) — deletion, no RED.
- **R46** (nit, CONFIRMED) — `_switchedModeDelegate` built once from the FIRST buffer's
  interface type (`??=`) and reused for every buffer — a second `IVimTextBuffer`
  implementation would not bind. **Fix:** rebuild the delegate per buffer type (or
  cache per interface type). **Unit seam:** no-seam (reflection).

## Phase 4 — Navigation robustness (R15, R16, R31, R32, R33, R34)

- **R15** (minor, CONFIRMED) — the Properties-window quirk (caption + ToolWindow↔Properties
  cross-type match) is implemented twice (`CompareWindows` vs `LinkedTo`'s inline
  predicate). **Fix:** single-source the quirk. **Unit seam:** no-seam (VS-coupled COM).
- **R16** (minor, CONFIRMED) — `GetWindowScreenRect` HRESULT ignored — a failed COM call
  returns a silent empty rect WITHOUT the n19 `window rect unavailable` diagnostic.
  **Fix:** check the HRESULT + emit the n19 diagnostic. **Unit seam:** no-seam (COM).
- **R31** (nit, CONFIRMED) — `_dte` non-readonly while the sibling `_frame4` is
  `readonly`. **Fix:** make `_dte` `readonly`. **Unit seam:** no-seam (build).
- **R32** (nit, CONFIRMED) — linked-window filter recomputed per navigation — O(n) COM
  `LinkedWindowFrame`/`Type`/`Caption` reads per keystroke. **Fix:** cache the linked
  filter. **Unit seam:** no-seam (VS-coupled).
- **R33** (nit, CONFIRMED) — "Window matrix initialization failed" diagnostic still uses
  the pre-restructure name (class is now `WindowNavigator`). **Fix:** rename the
  diagnostic to "Window navigator initialization failed". **Unit seam:** no-seam (log
  line) — the harness does not assert on this line (verify).
- **R34** (nit, CONFIRMED) — `TryGetScreenRect` (internal, reachable from the test
  assembly) lacks `ThreadHelper.ThrowIfNotOnUIThread()`. **Fix:** add the assert.
  **Unit seam:** no-seam (COM).

## Phase 5 — Controller/state (R8, R9, R17, R18, R19, R20, R21, R35, R36, R37, R38, R39)

- **R8** (minor, CONFIRMED) — `FocusKeeper.Run` creates a `DispatcherTimer` that is never
  stored/cancelled — stacked keepers race (g then i→Esc re-selects the wrong node).
  **Fix:** return a disposable/handle and cancel prior keepers on a new `Run` (or on
  `WindowManager.Dispose`). **Unit seam:** no-seam (WPF DispatcherTimer);
  `FocusKeeperSchedule` (pure, tested) covers the schedule half.
- **R9** (minor, CONFIRMED) — `HierarchyResolver.PrimaryFilePath` does `fileNames[0]`
  with no empty-list guard → `IndexOutOfRangeException` on a corrupt project item.
  **Fix:** empty-list guard (return null/empty). **Unit seam (NeoVisual.Tests):**
  `Run_HierarchyResolver_*` (pure, already tested) — RED: an empty list throws today.
- **R19** (minor, CONFIRMED) — `HierarchyResolver.FirstSourceFilePath` re-implements the
  shared `HierarchyWalker` walk; the `.cs` filter is duplicated in
  `HierarchyForestBuilder.Build` and `FirstSourceFilePath`. **Fix:** delegate to the
  shared `HierarchyWalker` (single walker). **Unit seam:** `Run_HierarchyResolver_*`
  (NeoVisual.Tests) + `Run_HierarchyWalker_*` (Telescope.Tests — `HierarchyWalker` is
  `Telescope/Finders/Utils/HierarchyWalker.cs`), both pure, tested — RED: the resolver's
  own recursion is not the shared walker today.
- **R17** (minor, CONFIRMED) — `SolutionExplorerController.TryMove` walks the visual tree
  twice per routed key (`FindFocusedTextBox` in `TryMove` + again in
  `TryMoveFocusedSurface`); `ExitInputMode` also calls it twice. **Fix:** resolve the
  focused box once and reuse it. **Unit seam:** no-seam (WPF).
- **R18** (minor, CONFIRMED) — `TextMotionHelper.ApplyMotionToBox` materializes the
  entire buffer (`wpf.Text` / `view.TextSnapshot.GetText()`) on every motion key — O(n)
  copy per h/l/w/b/e in a long console buffer. **Fix:** avoid the whole-buffer copy
  (operate on the caret-relative slice). **Unit seam:** no-seam (WPF/VS editor).
- **R20** (minor, CONFIRMED) — `ResolveController`/`DefaultControllerFor` create a fresh
  default controller on every dictionary miss — the "mode remembered per type" guarantee
  holds only because package init eagerly registers every enum value. **Fix:** cache the
  per-type default instances. **Unit seam (NeoVisual.Tests):** `Run_WindowManager_*`
  (pure static `ResolveController`/`DefaultControllerFor`, tested) — RED: a fresh
  instance is created per miss today.
- **R21** (minor, CONFIRMED) — `BlockCaretAdornment.Active` only toggled from
  `ApplyEditorViewCaret` — no focus-loss handler, so a normal-mode block caret persists
  over an unfocused editor view. **Fix:** add a focus-loss handler that deactivates the
  adornment. **Unit seam:** no-seam (WPF/VS adornment).
- **R35** (nit, CONFIRMED) — H/L inline the log+`KeyInjection.Press` while J/K delegate
  to `TryMoveArrow` — two shapes for the same hjkl→arrow press. **Fix:** unify H/L with
  J/K (delegate to `TryMoveArrow`). **Unit seam:** no-seam (VS-coupled).
- **R36** (nit, CONFIRMED) — `escapeAttempts < 4` magic literal with no named constant.
  **Fix:** name the constant. **Unit seam (NeoVisual.Tests):** `FocusKeeperSchedule`
  (pure, tested).
- **R37** (nit, CONFIRMED) — `_isInputMode` redundantly re-set to the value the base ctor
  already computed from `IsTextInputType(type)` (TextInputToolWindowController.cs:41 +
  SolutionExplorerController.cs:36). **Fix:** drop the redundant re-set. **Unit seam:**
  no-seam (VS-coupled).
- **R38** (nit, CONFIRMED) — `SelectFirstSourceFile` emits `editor-view-opened` directly
  AND `TextViewCreated` emits the same line for a newly-opened file — non-deterministic
  count (1 vs 2). **Fix:** dedupe the emission (only `TextViewCreated` emits it, or only
  the direct path). **Unit seam:** no-seam (VS-coupled).
- **R39** (nit, CONFIRMED) — the text-motion log appends `text='{sample}'` where sample =
  `Substring(0,30)` and can contain newlines/control chars — splits the `[NeoVisual]`
  log line. **Fix:** sanitize the sample (strip newlines/control chars). **Unit seam:**
  no-seam (log line).

## Phase 6 — Finder/overlay (R22, R23, R40, R41, R42, R43, R44)

- **R22** (minor, CONFIRMED) — fzf subprocess spawned per keystroke with no debounce on
  the non-query-driven path (Files/Issues/References); the 200ms debounce only applies to
  query-driven finders. **Fix:** debounce the non-query-driven path too. **Unit seam:**
  partial — the debounce lives in the WPF overlay; `FzfFilter` is hermetic (tested).
- **R23** (minor, CONFIRMED) — the fzf cancellation path returns without observing the
  pending `outputTask`/`errorTask` → `UnobservedTaskException` noise on every overlay
  close mid-filter. **Fix:** observe the tasks on cancellation. **Unit seam
  (Telescope.Tests):** `Run_FzfFilter_*` (hermetic, `AwaitedReadCount` seam) — RED: the
  cancellation path does not observe the tasks today.
- **R40** (nit, CONFIRMED) — `ResultMapper._cachedSnapshot`/`_cachedByDisplay` are static
  mutable fields — a closed overlay's candidate list stays pinned in memory; not
  thread-safe off the UI thread. **Fix:** make the cache instance-scoped. **Unit seam
  (Telescope.Tests):** `Run_ResultMapper_*` (pure, tested) — RED not provable
  hermetically (memory pinning); behavior-preserving.
- **R41** (nit, CONFIRMED) — `PreviewRenderer.Show` reads content via `GetContent` then
  `SetContent`'s `GetSegments` re-reads the file on a cache miss — two reads, can diverge
  if mtime changes between them. **Fix:** pass the content through (single read).
  **Unit seam:** no-seam (WPF).
- **R42** (nit, CONFIRMED) — `FzfFilter.IsAvailable()` runs a synchronous
  `WaitForExit(500)` subprocess probe on the UI thread during the first overlay open.
  **Fix:** offload/async the probe (or cache it — the availability is already cached once
  per session). **Unit seam:** partial — `FzfFilter` hermetic (tested); the UI-thread
  aspect is in the caller.
- **R43** (nit, CONFIRMED) — `TextMotionNavigator.MoveToLine` is an O(n) linear scan
  while `SetText` already builds a `LineIndex` with binary-search line lookups.
  **Fix:** use the `LineIndex` binary search. **Unit seam (Telescope.Tests):**
  `Run_TextMotionNavigator_*` (pure, tested) — not RED-provable (perf); behavior-preserving.
- **R44** (nit, CONFIRMED) — `TryPromptMotion` calls `SetText(_promptBox.Text)` on every
  prompt-motion keystroke, rebuilding a `LineIndex` each time. **Fix:** avoid the
  `SetText` rebuild per keystroke. **Unit seam:** partial — `SetText` in the WPF overlay;
  the `LineIndex` rebuild is in the pure navigator. Same `TryPromptMotion` as R1 — one
  edit pass.

## Phase 7 — Dead code + logging (R28, R29, R30)

- **R28** (nit, CONFIRMED) — the startup banner `=== Global Keyboard Logger Package
  STARTED ===` carries no `[MyExtension]`/`[NeoVisual]` prefix — invisible to the
  prefix-based log gates. **Fix:** add the `[MyExtension]` prefix. **Unit seam:** no-seam
  (log line).
- **R29** (nit, CONFIRMED) — `VimModeState.OnViewLostFocus` and `OnViewClosed` are
  byte-identical — a future change to one silently diverges. **Fix:** one delegates to
  the other. **Unit seam (NeoVisual.Tests):** `Run_VimModeState_*` (pure, tested) — not
  RED-provable (identical behavior); behavior-preserving.
- **R30** (nit, CONFIRMED) — `NavigationSettings._cached` static never invalidated —
  `FromSystemDpi` reads `SystemDpiX` once and caches forever (stale divide tolerances
  after a mid-session scaling change). **Fix:** invalidate the cache (or re-read on a
  DPI-change signal). **Unit seam:** partial — `FromDpi` pure (tested); the invalidation
  needs a DPI-source seam.

## Phase 8 — Test hermeticity (R24, R47, R48)

- **R24** (minor, CONFIRMED) — `Run_FzfFilter_TimeoutKillsAndFallsBack`
  (tests/Telescope.Tests/Program.cs:584-638) is still timing-dependent (5s wall-clock +
  2s GC-poll) and now redundant with the deterministic `Run_FzfFilter_TimeoutAwaitsTasks`
  (line 640). **Fix:** delete it. **Unit seam:** test-only — Telescope.Tests count drops
  by 1 (deterministic signal).
- **R47** (nit, CONFIRMED) — `Run_FzfFilter_IsAvailableBounded` (tests/Telescope.Tests/
  Program.cs:688-708) still asserts a wall-clock bound (`sw.Elapsed < 3s`). **Fix:**
  replace the wall-clock bound with a deterministic seam (injected clock). **Unit seam:**
  test-only.
- **R48** (nit, CONFIRMED) — `Run_GrepFinder_GatherHitsThrowsNotSupported`
  (tests/Telescope.Tests/Program.cs:2436-2444) invokes the protected `GatherHits` via
  reflection — couples the test to the internal method name/visibility. **Fix:** assert
  behavior instead (or make the seam public/internal-testable). **Unit seam:** test-only.

## Phase 9 — Docs drift (R25, R26, R27, R50, R51)

- **R25** (minor, CONFIRMED) — `docs/progress.md:10` header "Last item" still says the
  combined plan is "in progress" while Current state records GREEN 2026-10-02.
  **Fix:** update the header. **Unit seam:** docs-only.
- **R26** (minor, CONFIRMED) — `docs/progress.md:22` "Next up" still lists F5/F8/F9 as
  open — covered by the now-GREEN combined plan (F5→CR2, F8/F9→M2). **Fix:** update the
  section. **Unit seam:** docs-only.
- **R27** (minor, CONFIRMED) — `docs/spec.md:403` (+ `AGENTS.md:314`) fzf finder status
  says "Scope deferred by user 2026-09-19" but the scope was DECIDED 2026-09-28 and the
  plan is PLANNED (not executed). **Fix:** update the status. **Unit seam:** docs-only.
- **R50** (nit, CONFIRMED) — `docs/reviews/architecture-review.md:263` "Verified-clean"
  still claims `CardinalNavigation`/`CardinalMovment` — post-restructure it's
  `MyExtension.Navigation`/`Navigation/`. **Fix:** update the claim. **Unit seam:**
  docs-only.
- **R51** (nit, CONFIRMED) — `docs/progress.md:287` (+ `:28,:259`) item 5 says the fzf
  finder is "DEFERRED (scope TBD)" while item 3 + Decisions say "SCOPE DECIDED
  2026-09-28 ... PLANNED" — internal contradiction. **Fix:** reconcile the status.
  **Unit seam:** docs-only.

---

## Acceptance criteria

Every phase's acceptance criteria map to a diagnostic line and/or a unit test. The
master table (each row = a phase's gate):

| Phase | Gate (unit tests GREEN + build + existing suites) | Diagnostic contract preserved/added |
|-------|---------------------------------------------------|-------------------------------------|
| 0 | `Run_PromptMotionRouter_*` (prompt-only restriction); `Run_OverlayKeyHandler_*` (a/A/I placements); `Run_PreviewTokenCache_*` (document cache); VimModeSource lifecycle helper (R3); build (R4) | `prompt-motion key=... caret=...` unchanged for h/l/w/b/e/0/$; NOT emitted for j/k/g/G in the prompt; `[Telescope] preview caret=... line=...` unchanged; `[NeoVisual] vim-mode=...` unchanged |
| 1 | harness `-SelfCheck` (R5 post-tap gate; R49 self-seeding); build (R13 shell-wait gate) | `Focus prompt => True, mode=insert` post-tap (R5); `key=I/A mode=normal handled=True` |
| 2 | `Run_FocusGuard_*` (R6 deletion); `Run_InjectedKeyGuard_*` (R12 TTL-at-record RED); `Run_HierarchyResolver_*` (R9 empty-list RED) | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged; no shift-swallow (R10) |
| 3 | `Run_VimBufferSubscriptions_*` (R14 deletion) | `[NeoVisual] vim-mode=...` unchanged |
| 4 | build + `Run_WindowAdapter_*` / `Run_NavigationSnapshot_*` | `navigate direction=L/R/D/U` unchanged; n19 `window rect unavailable` on HRESULT failure (R16) |
| 5 | `Run_HierarchyResolver_*` (R9/R19, NeoVisual.Tests); `Run_HierarchyWalker_*` (R19, Telescope.Tests); `Run_WindowManager_*` (R20); `Run_FocusKeeperSchedule_*` (R36) | `[NeoVisual] solution-explorer select file=...` unchanged; `[NeoVisual] text-motion key=... caret=...` unchanged (R39 sanitized) |
| 6 | `Run_FzfFilter_*` (R23 RED); `Run_ResultMapper_*` (R40); `Run_TextMotionNavigator_*` (R43) | `[Telescope] fzf filter failed: ...` unchanged; `[Telescope] result-mapper unknown display: ...` unchanged; `[Telescope] preview caret=... line=...` unchanged |
| 7 | `Run_VimModeState_*` (R29); `Run_NavigationSettings_*` (R30) | `[MyExtension]` prefix on the startup banner (R28); no log literal change |
| 8 | Telescope.Tests count drop (R24); hermeticity tests (R47/R48) | none changed |
| 9 | doc-ref lint clean after the doc updates | none changed |

## Unit test plan (summary)

- **tests/Telescope.Tests** (pure classes): `PromptMotionRouter` prompt-only restriction
  (R1), `OverlayKeyHandler` a/A/I placements (R1), `PreviewTokenCache` document cache
  (R2), `FzfFilter` cancellation-task observation (R23) + hermeticity (R47), `ResultMapper`
  instance-scope cache (R40), `TextMotionNavigator` LineIndex MoveToLine (R43), test
  deletions (R24, R48).
- **tests/NeoVisual.Tests** (pure classes): `FocusGuard` deletion (R6) + shift routing
  (R10/R11), `InjectedKeyGuard` TTL-at-record (R12), `HierarchyResolver` empty-list guard
  (R9) + HierarchyWalker delegation (R19 — the `Run_HierarchyWalker_*` tests live in
  Telescope.Tests), `WindowManager` per-type default cache (R20),
  `VimBufferSubscriptions` deletion (R14), `VimModeState` delegate (R29),
  `NavigationSettings` invalidation (R30), `FocusKeeperSchedule` named constant (R36).
- **No-seam** (build + existing suites + deferred e2e): R4, R5, R7, R8, R13, R15, R16,
  R17, R18, R21, R22 (debounce half), R28, R31, R32, R33, R34, R35, R37, R38, R39, R41,
  R42 (UI-thread half), R44 (SetText half), R46, R49, and the docs (R25-R27, R50, R51).

## Diagnostics (new/changed log lines)

- UNCHANGED (must stay byte-identical): `prompt-motion key=... caret=...` (for
  h/l/w/b/e/0/$), `[Telescope] preview caret=... line=...`, `[Telescope] focus
  target=List|Preview`, `[NeoVisual] text-motion key=... caret=...`, `[NeoVisual]
  toolwindow-move key=... -> arrow vk=...`, `[NeoVisual] vim-mode=...`, `[NeoVisual]
  solution-explorer select file=...`, `[NeoVisual] editor-view-opened file=...`,
  `navigate direction=L/R/D/U`, `[Telescope] fzf filter failed: ...`, `[Telescope]
  result-mapper unknown display: ...`.
- CHANGED (documented): R33 — "Window matrix initialization failed" →
  "Window navigator initialization failed" (verify the harness does not assert on it).
  R28 — the startup banner gains the `[MyExtension]` prefix.
- HARNESS (not product): R5 — the `telescope-mode` `i`/`a` assertions become post-tap.

## Known-RED allowlist

None — no known-RED e2e scenario remains (per docs/progress.md). All fixes are
RED-proven by unit tests (or build + existing suites for no-seam items). R45 is the
documented/accepted n16 design — no code change, disposition recorded.

## E2E queue reference (deferred — see e2e-queue.md)

The following e2e scenarios are QUEUED (not created/executed until this plan is GREEN in
docs/progress.md and the user is on a VS-capable machine). Each asserts the diagnostics
listed above:

- **E2E-CR51-1** (R1): `telescope-navigate` + `telescope-wrap` — j/k move the selection
  and g/G jump in the prompt (`results count=... selected=...` after j/k; G→last, gg→first);
  `telescope-mode` — `a`/`A`/`I` enter insert mode with the correct placements
  (`Focus prompt => True, mode=insert` after `a`; `key=I mode=normal handled=True`).
- **E2E-CR51-2** (R2): `telescope-preview` — preview caret/scroll preserved across
  selection changes in the same file (`preview caret=... line=...` unchanged; no
  scroll reset).
- **E2E-CR51-3** (R3): `neovisual-editor-insert` + a multi-view scenario — closing a
  non-focused view does NOT kill the focused view's `SwitchedMode` subscription
  (`vim-mode=Insert|Normal|Replace` unchanged).
- **E2E-CR51-4** (R4): `neovisual-window-nav` — navigation survives a stale frame
  (`navigate direction=L/R/D/U` + `navigate activated index=...`).
- **E2E-CR51-5** (R5): `telescope-mode` — the hardened `i`/`a` assertions are real
  (a re-introduced prompt-interception bug fails the gate).
- **E2E-CR51-6** (R10/R11): `neovisual-explorer-*` — Shift+O/R/M/A/G do NOT fire tree
  actions; uppercase typing in the editor is not swallowed.
- **E2E-CR51-7** (R22/R23): `telescope-search` — the non-query-driven fzf path is
  debounced; overlay close mid-filter produces no `UnobservedTaskException` noise.
- **E2E-CR51-8** (R38): `explorer-open-navigation` — `editor-view-opened file=...`
  emitted exactly once per open.
- **E2E-CR51-9** (R33/R28): full suite — the renamed "Window navigator initialization
  failed" + prefixed startup banner do not break any assertion.
- **E2E-CR51-10** (R49): `-Tests seed-leak` alone runs green (self-seeding subsets).

## Execution order note for neovim_hub

Execute phases in order 0 → 9. Phases 0-7 are the code-review fixes (each RED-proven by
its unit tests; phases 2/3/7 include deletions/refactors with behavior-preserving gates;
phase 8 is test hermeticity; phase 9 is docs). The unit-only lane applies throughout —
e2e is deferred to `e2e-queue.md` (E2E-CR51-* above).

---

# Part B — Build Plan (BP-n steps, unit-only lane)

> **Lane: bugfix (unit-only, e2e deferred).** **No RED evidence exists yet** — the unit
> tests named below are NOT written; `neovim_hub`'s `e2e-test-builder` writes them (RED)
> before the build-agent runs each step. **Verify-with = unit test names + diagnostic
> formats only** (no e2e scenario is a gate here; the deferred e2e scenarios live in the
> "E2E queue reference" section above and are NOT used as Verify-with gates).
>
> **Ground truth:** repo GREEN at `349fc05`; `dotnet build` = 0 errors; Telescope.Tests
> 151, NeoVisual.Tests 158 (all passing). Every BP step below is one finding R1-R51
> (51 steps, phases 0-9 in the plan's order). R12/R13/R19 (absent from the phase list in
> Part A) are placed in the phase whose seam they touch: R12→Phase 2 (hook hot path,
> with R11), R13→Phase 1 (harness-only init step), R19→Phase 5 (with R9, both
> `HierarchyResolver`). R45 is the documented/accepted n16 design — a disposition step,
> no code.
>
> **Log-line-as-contract:** any fix touching a log site MUST preserve the emitted token
> byte-identical (the "Diagnostics" section in Part A lists the UNCHANGED lines). R33
> renames "Window matrix initialization failed" → "Window navigator initialization
> failed" (verified: the harness does NOT assert on the old line — grep of
> `tools/harness/test-e2e.ps1` finds no "Window matrix" match). R28 adds the
> `[MyExtension]` prefix to the startup banner (verified: the harness does NOT assert on
> the banner).
>
> **Rename/removal propagation (doc-ref lint gate):** BP-8 (R6) deletes
> `FocusGuard.HasToolWindowActionKeys`, which `docs/spec.md:76,158` and
> `docs/progress.md:614,616` reference (both in the doc-ref lint scope) — those refs MUST
> be updated in the same step or `pwsh tools/lint/check-doc-refs.ps1` fails. R14's
> `FocusedView`/`FocusedBuffer` deletion does NOT touch `AGENTS.md:385`/`spec.md:357`
> (`IVim.FocusedBuffer` is a different, allowlisted symbol).
>
> **MEF/DI wiring:** this is a bugfix plan — NO new `[Export]`, `MyExtensionPackage`
> registration, `WindowManager` controller registration, `InputHandler.ResolveAction`
> case, or `default-keybindings.json` line is introduced by any BP step. The only
> package-init touch is BP-6 (R13), which GATES the existing `shell-wait` step (no new
> step, no new registration). The build-agent must not add wiring that the findings do
> not call for.

## Phase 0 — Functional bugs (R1, R2, R3, R4)

### BP-1 — R1: prompt selection navigation (j/k/g/G) + a/A/I placement (surface-aware)
- **Files:** `Telescope/Overlay/Utils/PromptMotionRouter.cs`, `Telescope/Overlay/TelescopeOverlay.cs`, `Telescope/Overlay/Utils/OverlayKeyHandler.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** Restrict the prompt motion set to h/l/w/b/e/0/$ **surface-aware** (arch-auditor correction): `HandlePreviewKey` (TelescopeOverlay.cs:628) also calls `PromptMotionRouter.ShouldConsume` and needs j/k/g/G (Down/Up/Top/Bottom) for the preview. Add a surface param to `ShouldConsume` (e.g. `bool allowVertical` / `bool previewSurface`): `TryPromptMotion` passes the prompt surface (Down/Up/Top/Bottom → return false, falls through to `OverlayKeyHandler`), `HandlePreviewKey` passes the preview surface (full set). **Placement routing (the R1 fix):** `TryPromptMotion` (TelescopeOverlay.cs:595, currently `out _`) captures the `insertPlacement` out param and, when it is non-null, calls `EnterInsert(placement)` directly (TelescopeOverlay.cs:525) and returns true (consumed) — the out-param routing handles ALL FOUR placements **(a→Current, A→End, I→Start, i→Current)**, bypassing `OverlayKeyHandler`'s `OverlayKey.A`→End / `OverlayKey.I`→Current mapping (OverlayKeyHandler.cs:169-172), which is the source of the R1 bug. **No `OverlayKey.ShiftI` mechanism is added** — the out-param routing covers shift+I (→ `CaretPlacement.Start` → `EnterInsert(Start)`); `OverlayAction.EnterInsertStart` is not the mechanism (it remains dead — the R1 placement behavior is delivered by the out-param routing, not by a new `OverlayKey.ShiftI`).
- **Verify-with:** RED unit tests (written by e2e-test-builder before this step): `Run_PromptMotionRouter_PromptRestrictsVerticalMotions` (new — `ShouldConsume(Key.J, false, promptSurface:true)` returns false; `ShouldConsume(Key.J, false, previewSurface:true)` returns true); extend `Run_PromptMotionRouter_MotionsConsumed` / `Run_PromptMotionRouter_InsertPlacementsNotConsumed` for the surface param — the latter already pins the placements (bare `a`→Current, shift+A→End, bare `i`→Current, shift+I→Start; Program.cs:1264-1282). The `TryPromptMotion` placement routing itself is WPF-coupled (no hermetic seam) — `dotnet build` 0 errors + existing suites green (deferred e2e E2E-CR51-1 `telescope-mode` covers the live placements). Existing `Run_KeyHandler_*` / `Run_CaretPlacement_*` stay green. Diagnostic: `prompt-motion key=... caret=...` unchanged for h/l/w/b/e/0/$; NOT emitted for j/k/g/G in the prompt; `key=J mode=normal handled=True` (selection move) emitted instead.
- **Fails-if:** `Run_PromptMotionRouter_*` RED tests still fail (j/k/g/G still consumed in the prompt); `key=J mode=normal handled=True` never appears; the placement routing is wrong — bare `a` still inserts at END (should be Current) or shift+I still inserts at CURRENT (should be Start), i.e. the R1 bug reproduced.

### BP-2 — R2: cache the built preview FlowDocument + line pointers
- **Files:** `Telescope/Overlay/Utils/PreviewRenderer.cs`, `Telescope/Overlay/Utils/PreviewTokenCache.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** `SetContent` (PreviewRenderer.cs:66-131) unconditionally rebuilds `FlowDocument` + `_lineIndex` + `_linePointers` + `ScrollToHome()` per selection change (M4 partial — `PreviewTokenCache` only caches tokenization). Cache the built document + `_linePointers` keyed by mtime (mirror `PreviewTokenCache`); only rebuild when content changes; keep `MoveToLine`/`ApplyCaret` as the per-selection work. The pure seam holds NO WPF types — extend `PreviewTokenCache` (or add a sibling pure `PreviewDocumentCache`) with an mtime-keyed "content changed?" decision; the WPF rebuild is gated on it.
- **Verify-with:** RED unit test `Run_PreviewDocumentCache_UnchangedMtimeSkipsRebuild` (new, Telescope.Tests) — RED: the rebuild decision is not mtime-keyed today; existing `Run_PreviewTokenCache_UnchangedMtimeCached` / `Run_PreviewTokenCache_ChangedMtimeRetokenizes` stay green. Diagnostic: `[Telescope] preview tokens=...` emitted only on rebuild (once per content change); `[Telescope] preview caret=... line=...` unchanged per selection.
- **Fails-if:** the document-cache RED test fails (rebuild still unconditional); `preview tokens=...` emitted on every selection change.

### BP-3 — R3: VimModeSource buffer-subscription lifecycle (Closed removal + refcount)
- **Files:** `MyExtension/Vim/Utils/VimModeSource.cs`, `MyExtension/Vim/Utils/VimBufferSubscriptions.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** (arch-auditor correction) `UnsubscribeBuffer` only receives the `IVimTextBuffer`, but the `Closed` subscription is on the `IVimBuffer` (:254-255) — thread the buffer via `_subscriptions.BufferFor`. Add a `remove_Closed` invocation + `_subscribedBuffers.Remove(buffer)` in `UnsubscribeBuffer` (mirroring the `SwitchedMode` removal). Decrement `_textBufferRefCounts` in `OnBufferClosed` (removing the entry at 0) WITHOUT double-decrementing with `Detach` (m40) — coordinate the two paths (e.g. `OnBufferClosed` decrements only while the buffer is still tracked in `_subscribedBuffers`; `Detach` skips its decrement when the buffer was already removed by `OnBufferClosed`). Extract the lifecycle bookkeeping into a pure helper (mirroring the tested `VimBufferSubscriptions`) so the refcount/subscription invariants are unit-testable.
- **Verify-with:** RED unit test on the extracted pure helper (NeoVisual.Tests) — e.g. `Run_VimBufferSubscriptions_UnsubscribeRemovesClosedAndRefcount` (new): `UnsubscribeBuffer` removes the `Closed` subscription + `_subscribedBuffers`; `OnBufferClosed` decrements the refcount to 0 and removes the entry; no double-decrement with `Detach`. Diagnostic: `[NeoVisual] vim-mode=...` unchanged.
- **Fails-if:** the lifecycle-helper RED test fails (Closed sub not removed / refcount not decremented / double-decrement); `[NeoVisual] vim-mode=...` drifts.

### BP-4 — R4: per-frame fault isolation in frame enumeration
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** `Enumerate` (WindowFrameAdapter.cs:67,71) uses `ErrorHandler.ThrowOnFailure` on `GetToolWindowEnum`/`GetDocumentWindowEnum`, and `ExtractFramesCore` (:140-154) has an unguarded `VsShellUtilities.GetWindowObject(frame[0])` per frame — one stale frame aborts the whole enumeration and escapes into the hook path. Wrap each frame's `GetWindowObject` in try/catch and skip the bad frame (log once); wrap the two `ThrowOnFailure` calls (or the whole `Enumerate` body) to return an empty list on failure, mirroring the existing null-`uiShell` path. Never throw into the hook path.
- **Verify-with:** no hermetic seam (VS-coupled COM) — `dotnet build` 0 errors + existing suites green (deferred e2e covers the live path). Diagnostic: `navigate direction=L/R/D/U` unchanged; a skipped bad frame logs once (no new token required).
- **Fails-if:** `dotnet build` fails; a stale frame still aborts the whole enumeration (exception escapes into the hook path).

## Phase 1 — Harness gate hardening (R5, R13, R49)

### BP-5 — R5: telescope-mode i/a assertions become post-tap
- **Files:** `tools/harness/test-e2e.ps1`
- **Change:** `Reset-LogBaseline` (test-e2e.ps1:578) runs BEFORE `Open-Telescope`, so the Open-Telescope `Focus prompt => True, mode=insert` line satisfies all three `i`/`a` assertions (false positives). Snapshot the log index just before the `i`/`a` tap and use a post-tap search for a NEW `Focus prompt => True, mode=insert`, or assert the `key=I mode=normal handled=True` / `key=A mode=normal handled=True` line for the tap.
- **Verify-with:** harness `-SelfCheck` seam (test-e2e.ps1:1605+). Diagnostic: `Focus prompt => True, mode=insert` post-tap; `key=I/A mode=normal handled=True`.
- **Fails-if:** the `telescope-mode` scenario still passes on the Open-Telescope baseline line (false positive not closed).

### BP-6 — R13: gate the shell-wait init step on the harness env vars
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`
- **Change:** the "shell-wait" init step (MyExtensionPackage.cs:143-150, `WaitForShellInitializedAsync`) runs unconditionally — up to 20s (40×500ms) dead-key delay on a slow machine; it exists only for e2e focus stability. Gate it on `NEOVISUAL_TEST_SOLUTION`/`NEOVISUAL_LOG_DIR` being set (the e2e harness sets these); skip the wait in normal runs.
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[MyExtension] init shell-wait ok` unchanged when the env vars are set; the step is skipped (no `shell initialized` wait) when they are not.
- **Fails-if:** `dotnet build` fails; the shell-wait still runs unconditionally (20s delay in normal runs).

### BP-7 — R49: make harness subsets self-seeding
- **Files:** `tools/harness/test-e2e.ps1`
- **Change:** scenario order-dependence is documented + self-checked, but subsets remain not independently runnable (e.g. `-Tests seed-leak` alone fails). Make subsets self-seeding (the `seed-leak` scenario must work standalone) or document the limitation explicitly.
- **Verify-with:** harness `-SelfCheck` seam. Diagnostic: none changed.
- **Fails-if:** `-Tests seed-leak` alone still fails.

## Phase 2 — FocusGuard + routing (R6, R7, R10, R11, R12)

### BP-8 — R6: delete FocusGuard.HasToolWindowActionKeys + propagate doc refs
- **Files:** `MyExtension/ToolWindows/Utils/FocusGuard.cs`, `docs/spec.md`, `docs/progress.md`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** delete `FocusGuard.HasToolWindowActionKeys` (dead production code — proxy-aware `callers_of` → 9 test callers only) + its `OwnsKeyboard` call site + fix the stale doc comment (AGENTS.md/SKILL.md document the pre-filter as `IsKeyOfInterest`/`ShouldRouteToolWindowKey`). **Rename/removal propagation (doc-ref lint gate):** `docs/spec.md:76,158` and `docs/progress.md:614,616` reference `HasToolWindowActionKeys` and are in the lint scope — update them to `ShouldRouteToolWindowKey`/`IsKeyOfInterest`/`OwnsKeyboard` in the same step. Update the 9 test callers to the surviving `ShouldRouteToolWindowKey`/`OwnsKeyboard` forms.
- **Verify-with:** `Run_FocusGuard_*` (NeoVisual.Tests) — deletion, no RED (tests updated to the surviving forms); `dotnet build` 0 errors; `pwsh tools/lint/check-doc-refs.ps1` clean (the spec.md/progress.md refs resolve to the surviving symbols). Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged.
- **Fails-if:** `dotnet build` fails (a test still references `HasToolWindowActionKeys`); doc-ref lint fails (a doc still references the deleted symbol); the stale doc comment remains.

### BP-9 — R7: reset tool-window state on null CurrentWindow
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`
- **Change:** `OnWindowFocusChanged` (WindowManager.cs:222-256) early-returns on null `CurrentWindow`, leaving `_isToolWindow`/`_type`/`_isTextInputType` stale from the previous frame. Reset the state (`_isToolWindow=false; _type=Unknown; _isTextInputType=false; _textInputSurfaceFocused=false`) before the null-return.
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged.
- **Fails-if:** `dotnet build` fails; the stale classification persists on a null frame.

### BP-10 — R10: gate shift for non-text-input controllers
- **Files:** `MyExtension/Input/InputHandler.cs`, `MyExtension/ToolWindows/Utils/FocusGuard.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** action-key routing (InputHandler.cs:324,343-349) gates only `!ctrl && !alt`, not shift — `Shift+O/R/M/A/G` in Solution Explorer fire the same tree actions and swallow the key. Gate shift for non-text-input controllers (text-input controllers still need shift to tell I/i and A/a apart). Extractable to `FocusGuard` (pure).
- **Verify-with:** RED unit test `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` (new, NeoVisual.Tests) — RED: shift is not gated today. Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged; no shift-swallow.
- **Fails-if:** the shift-gating RED test fails; `Shift+O` still fires the tree action.

### BP-11 — R11: narrow the shift case in IsKeyOfInterest
- **Files:** `MyExtension/Input/InputHandler.cs`, `MyExtension/ToolWindows/Utils/FocusGuard.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `IsKeyOfInterest` (InputHandler.cs:417) returns true for every key while shift is held — every uppercase letter typed in the editor runs the full `HandleKey` path (hot-path cost). Narrow the shift case in the pre-filter (shift alone is only interesting when a tool window with action keys is in normal mode, or a leader sequence is active). Extractable to `FocusGuard`.
- **Verify-with:** RED unit test `Run_FocusGuard_ShiftAloneNotInterestingInEditor` (new, NeoVisual.Tests) — RED: shift alone is interesting today. Diagnostic: `[NeoVisual] vim-mode=...` unchanged; uppercase typing in the editor is not swallowed.
- **Fails-if:** the shift-narrowing RED test fails; uppercase typing still runs the full `HandleKey` path.

### BP-12 — R12: InjectedKeyGuard TTL checked at record time
- **Files:** `MyExtension/Hooks/Utils/InjectedKeyGuard.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `InjectedKeyGuard` checks the TTL on consume, not on record (InjectedKeyGuard.cs:81-85) — a >1s UI stall between `Press`'s Record and the injected key-down expires the record → re-injection storm. Check/expire at Record time (a stale record is dropped when a new Record arrives, or the record is timestamped and expired on Record).
- **Verify-with:** RED unit test `Run_InjectedKeyGuard_StaleRecordExpiredAtRecord` (new, NeoVisual.Tests) — RED: a stale record survives into the next Record today. Diagnostic: none changed (no log token).
- **Fails-if:** the TTL-at-record RED test fails; a stale record still consumes the next physical key-down.

## Phase 3 — Vim interop (R14, R46)

### BP-13 — R14: delete VimBufferSubscriptions.FocusedView/FocusedBuffer
- **Files:** `MyExtension/Vim/Utils/VimBufferSubscriptions.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** delete `VimBufferSubscriptions.FocusedView`/`FocusedBuffer` (and the `_focusedView` field) — genuinely dead (0 plain + proxy callers). No doc propagation needed: `AGENTS.md:385`/`spec.md:357` reference `IVim.FocusedBuffer` (a different, allowlisted symbol).
- **Verify-with:** `Run_VimBufferSubscriptions_*` (NeoVisual.Tests) — deletion, no RED; `dotnet build` 0 errors. Diagnostic: `[NeoVisual] vim-mode=...` unchanged.
- **Fails-if:** `dotnet build` fails (a test still references `FocusedView`/`FocusedBuffer`).

### BP-14 — R46: rebuild the SwitchedMode delegate per buffer type
- **Files:** `MyExtension/Vim/Utils/VimModeSource.cs`
- **Change:** `_switchedModeDelegate` is built once from the FIRST buffer's interface type (`??=`, VimModeSource.cs:235) and reused for every buffer — a second `IVimTextBuffer` implementation would not bind. Rebuild the delegate per buffer type (or cache per interface type).
- **Verify-with:** no hermetic seam (reflection) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] vim-mode=...` unchanged.
- **Fails-if:** `dotnet build` fails; the delegate is still built once from the first buffer's type.

## Phase 4 — Navigation robustness (R15, R16, R31, R32, R33, R34)

### BP-15 — R15: single-source the Properties-window quirk
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`, `MyExtension/Navigation/Utils/WindowFrameUtils.cs`
- **Change:** the Properties-window quirk (caption + ToolWindow↔Properties cross-type match) is implemented twice (`CompareWindows` vs `LinkedTo`'s inline predicate). Single-source the quirk (one shared predicate).
- **Verify-with:** no hermetic seam (VS-coupled COM) — `dotnet build` 0 errors + existing suites (`Run_WindowAdapter_*`/`Run_NavigationSnapshot_*`) green. Diagnostic: `navigate direction=L/R/D/U` unchanged.
- **Fails-if:** `dotnet build` fails; the quirk is still duplicated.

### BP-16 — R16: check the GetWindowScreenRect HRESULT + emit n19
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** `GetWindowScreenRect` (WindowFrameAdapter.cs:183) ignores the HRESULT — a failed COM call returns a silent empty rect WITHOUT the n19 `window rect unavailable` diagnostic. Check the HRESULT + emit the n19 diagnostic.
- **Verify-with:** no hermetic seam (COM) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] window rect unavailable; using empty rect` (n19) emitted on HRESULT failure.
- **Fails-if:** `dotnet build` fails; a failed COM call still returns a silent empty rect without the n19 line.

### BP-17 — R31: make _dte readonly
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** make `_dte` `readonly` (the sibling `_frame4` is already `readonly`).
- **Verify-with:** `dotnet build` 0 errors.
- **Fails-if:** `dotnet build` fails (a mutation of `_dte` exists).

### BP-18 — R32: cache the linked-window filter
- **Files:** `MyExtension/Navigation/WindowNavigator.cs`
- **Change:** the linked-window filter is recomputed per navigation (WindowNavigator.cs:49) — O(n) COM `LinkedWindowFrame`/`Type`/`Caption` reads per keystroke. Cache the linked filter.
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` 0 errors + existing suites green. Diagnostic: `navigate direction=L/R/D/U` unchanged.
- **Fails-if:** `dotnet build` fails; the filter is still recomputed per navigation.

### BP-19 — R33: rename the "Window matrix" diagnostic
- **Files:** `MyExtension/Navigation/WindowNavigator.cs`
- **Change:** "Window matrix initialization failed" (WindowNavigator.cs:67) still uses the pre-restructure name (class is now `WindowNavigator`). Rename to "Window navigator initialization failed". Verified: the harness does NOT assert on the old line (grep of `tools/harness/test-e2e.ps1` finds no "Window matrix" match).
- **Verify-with:** `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] Window navigator initialization failed` (renamed).
- **Fails-if:** `dotnet build` fails; the harness asserts on the old "Window matrix" line (would break the deferred e2e).

### BP-20 — R34: add ThrowIfNotOnUIThread to TryGetScreenRect
- **Files:** `MyExtension/Navigation/Utils/WindowFrameAdapter.cs`
- **Change:** `TryGetScreenRect` (internal, reachable from the test assembly, WindowFrameAdapter.cs:177) lacks `ThreadHelper.ThrowIfNotOnUIThread()`. Add the assert.
- **Verify-with:** `dotnet build` 0 errors + existing suites green.
- **Fails-if:** `dotnet build` fails.

## Phase 5 — Controller/state (R8, R9, R17, R18, R19, R20, R21, R35, R36, R37, R38, R39)

### BP-21 — R8: FocusKeeper.Run returns a disposable / cancels prior keepers
- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs`
- **Change:** `FocusKeeper.Run` (FocusKeeper.cs:17-38) creates a `DispatcherTimer` that is never stored/cancelled — stacked keepers race (g then i→Esc re-selects the wrong node). Return a disposable/handle and cancel prior keepers on a new `Run` (or on `WindowManager.Dispose`).
- **Verify-with:** no hermetic seam (WPF DispatcherTimer); `Run_FocusKeeperSchedule_*` (pure, tested) covers the schedule half. Diagnostic: `[NeoVisual] solution-explorer select file=...` unchanged.
- **Fails-if:** `dotnet build` fails; stacked keepers still race.

### BP-22 — R9: HierarchyResolver.PrimaryFilePath empty-list guard
- **Files:** `MyExtension/ToolWindows/Utils/HierarchyResolver.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `PrimaryFilePath` (HierarchyResolver.cs:34-37) does `fileNames[0]` with no empty-list guard → `IndexOutOfRangeException` on a corrupt project item. Add an empty-list guard (return null/empty).
- **Verify-with:** RED unit test `Run_HierarchyResolver_PrimaryFilePath_EmptyReturnsNull` (new, NeoVisual.Tests) — RED: an empty list throws today. Diagnostic: `[NeoVisual] solution-explorer select file=...` unchanged.
- **Fails-if:** the empty-list RED test fails (still throws `IndexOutOfRangeException`).

### BP-23 — R17: reuse the resolved focused text box
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** `TryMove` (SolutionExplorerController.cs:156) walks the visual tree twice per routed key (`FindFocusedTextBox` in `TryMove` + again in `TryMoveFocusedSurface`); `ExitInputMode` also calls it twice. Resolve the focused box once and reuse it.
- **Verify-with:** no hermetic seam (WPF) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged.
- **Fails-if:** `dotnet build` fails; the visual tree is still walked twice per key.

### BP-24 — R18: avoid the whole-buffer copy per motion key
- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`
- **Change:** `ApplyMotionToBox` (TextMotionHelper.cs:84) materializes the entire buffer (`wpf.Text` / `view.TextSnapshot.GetText()`) on every motion key — O(n) copy per h/l/w/b/e in a long console buffer. Avoid the whole-buffer copy (operate on the caret-relative slice).
- **Verify-with:** no hermetic seam (WPF/VS editor) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] text-motion key=... caret=...` unchanged.
- **Fails-if:** `dotnet build` fails; the whole-buffer copy remains per motion key.

### BP-25 — R19: delegate FirstSourceFilePath to the shared HierarchyWalker
- **Files:** `MyExtension/ToolWindows/Utils/HierarchyResolver.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `FirstSourceFilePath` (HierarchyResolver.cs:39) re-implements the shared `HierarchyWalker` walk; the `.cs` filter is duplicated in `HierarchyForestBuilder.Build` and `FirstSourceFilePath`. Delegate to the shared `HierarchyWalker` (single-source the `.cs` filter).
- **Verify-with:** `Run_HierarchyResolver_*` (NeoVisual.Tests) — behavior-preserving (existing `Run_HierarchyResolver_FirstSourceFile_*` tests stay green). Diagnostic: `[NeoVisual] solution-explorer select file=...` unchanged.
- **Fails-if:** `Run_HierarchyResolver_FirstSourceFile_*` tests fail (behavior drift); `dotnet build` fails.

### BP-26 — R20: cache per-type default controllers
- **Files:** `MyExtension/ToolWindows/WindowManager.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `ResolveController`/`DefaultControllerFor` (WindowManager.cs:166,185) create a fresh default controller on every dictionary miss — the "mode remembered per type" guarantee holds only because package init eagerly registers every enum value. Cache the per-type default instances in an **INSTANCE-scoped** `_defaultControllers` dictionary on `WindowManager` (mirroring the existing `_controllers` instance dictionary, WindowManager.cs:158), populated on miss in `GetController` (WindowManager.cs:174-178). **Do NOT use a static cache** — static mutable state is the same class of issue R40 flags for ResultMapper (BP-35). The pure static `ResolveController`/`DefaultControllerFor` factories stay pure (no cache); the instance `GetController` consults the instance cache.
- **Verify-with:** RED unit test `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` (new, NeoVisual.Tests) — RED: a fresh instance is created per miss today (no instance cache). Existing `Run_WindowManager_*` (pure static `ResolveController`/`DefaultControllerFor`) stay green. Diagnostic: none changed.
- **Fails-if:** the caching RED test fails (a fresh instance is still created per miss); the cache is STATIC (static mutable state — the R40 class of issue BP-35 fixes).

### BP-27 — R21: BlockCaretAdornment focus-loss handler
- **Files:** `MyExtension/Adornments/BlockCaretAdornment.cs`
- **Change:** `BlockCaretAdornment.Active` (BlockCaretAdornment.cs:68) only toggled from `ApplyEditorViewCaret` — no focus-loss handler, so a normal-mode block caret persists over an unfocused editor view. Add a focus-loss handler that deactivates the adornment.
- **Verify-with:** no hermetic seam (WPF/VS adornment) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] block-caret active=True|False` unchanged.
- **Fails-if:** `dotnet build` fails; the block caret still persists over an unfocused view.

### BP-28 — R35: unify H/L with J/K (delegate to TryMoveArrow)
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** H/L inline the log+`KeyInjection.Press` while J/K delegate to `TryMoveArrow` (SolutionExplorerController.cs:46) — two shapes for the same hjkl→arrow press. Unify H/L with J/K (delegate to `TryMoveArrow`).
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged.
- **Fails-if:** `dotnet build` fails; H/L still inline the press.

### BP-29 — R36: name the escapeAttempts constant
- **Files:** `MyExtension/ToolWindows/Utils/FocusKeeper.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `escapeAttempts < 4` (FocusKeeper.cs:62) is a magic literal with no named constant. Name the constant.
- **Verify-with:** `Run_FocusKeeperSchedule_*` (NeoVisual.Tests) — behavior-preserving. Diagnostic: none changed.
- **Fails-if:** `Run_FocusKeeperSchedule_*` fails; the magic literal remains.

### BP-30 — R37: drop the redundant _isInputMode re-set
- **Files:** `MyExtension/ToolWindows/TextInputToolWindowController.cs`, `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** `_isInputMode` is redundantly re-set to the value the base ctor already computed from `IsTextInputType(type)` (TextInputToolWindowController.cs:41 + SolutionExplorerController.cs:36). Drop the redundant re-set.
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input` unchanged.
- **Fails-if:** `dotnet build` fails; the redundant re-set remains.

### BP-31 — R38: dedupe the editor-view-opened emission
- **Files:** `MyExtension/ToolWindows/SolutionExplorerController.cs`
- **Change:** `SelectFirstSourceFile` (SolutionExplorerController.cs:224,250) emits `editor-view-opened` directly AND `TextViewCreated` emits the same line for a newly-opened file — non-deterministic count (1 vs 2). Dedupe the emission (only `TextViewCreated` emits it, or only the direct path).
- **Verify-with:** no hermetic seam (VS-coupled) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] editor-view-opened file=...` emitted exactly once per open.
- **Fails-if:** `dotnet build` fails; `editor-view-opened` is still emitted 1 vs 2 times non-deterministically.

### BP-32 — R39: sanitize the text-motion log sample
- **Files:** `MyExtension/ToolWindows/Utils/TextMotionHelper.cs`
- **Change:** the text-motion log appends `text='{sample}'` where sample = `Substring(0,30)` (TextMotionHelper.cs:173-174) and can contain newlines/control chars — splits the `[NeoVisual]` log line. Sanitize the sample (strip newlines/control chars).
- **Verify-with:** no hermetic seam (log line) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[NeoVisual] text-motion key=... caret=...` unchanged (sample sanitized).
- **Fails-if:** `dotnet build` fails; a newline in the sample still splits the log line.

## Phase 6 — Finder/overlay (R22, R23, R40, R41, R42, R43, R44)

### BP-33 — R22: debounce the non-query-driven fzf path
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** fzf subprocess spawned per keystroke with no debounce on the non-query-driven path (Files/Issues/References); the 200ms debounce only applies to query-driven finders (TelescopeOverlay.cs:359-363). Debounce the non-query-driven path too.
- **Verify-with:** partial — the debounce lives in the WPF overlay; `FzfFilter` is hermetic (tested). `dotnet build` 0 errors + existing suites green. Diagnostic: `[Telescope] fzf filter failed: ...` unchanged.
- **Fails-if:** `dotnet build` fails; the non-query-driven path still spawns per keystroke.

### BP-34 — R23: observe the fzf cancellation tasks
- **Files:** `Telescope/Filter/FzfFilter.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** the fzf cancellation path (FzfFilter.cs:172-175) returns without observing the pending `outputTask`/`errorTask` → `UnobservedTaskException` noise on every overlay close mid-filter. Observe the tasks on cancellation (mirror the timeout path's fault-only continuation + `AwaitedReadCount` seam).
- **Verify-with:** RED unit test `Run_FzfFilter_CancellationObservesTasks` (new, Telescope.Tests, using the `AwaitedReadCount` seam) — RED: the cancellation path does not observe the tasks today. Diagnostic: `[Telescope] fzf filter failed: ...` unchanged.
- **Fails-if:** the cancellation-observation RED test fails; `UnobservedTaskException` noise persists on overlay close mid-filter.

### BP-35 — R40: instance-scope the ResultMapper cache
- **Files:** `Telescope/Overlay/Utils/ResultMapper.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** `ResultMapper._cachedSnapshot`/`_cachedByDisplay` (ResultMapper.cs:22-23) are static mutable fields — a closed overlay's candidate list stays pinned in memory; not thread-safe off the UI thread. Make the cache instance-scoped (ResultMapper becomes an instance class, or the cache moves to an instance holder).
- **Verify-with:** `Run_ResultMapper_*` (Telescope.Tests) — behavior-preserving (existing tests updated to the instance form). Diagnostic: `[Telescope] result-mapper unknown display: ...` unchanged.
- **Fails-if:** `Run_ResultMapper_*` fails; `dotnet build` fails (a static call site remains).

### BP-36 — R41: single read in PreviewRenderer.Show
- **Files:** `Telescope/Overlay/Utils/PreviewRenderer.cs`
- **Change:** `PreviewRenderer.Show` (PreviewRenderer.cs:50-51,81-83) reads content via `GetContent` then `SetContent`'s `GetSegments` re-reads the file on a cache miss — two reads, can diverge if mtime changes between them. Pass the content through (single read).
- **Verify-with:** no hermetic seam (WPF) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[Telescope] preview file=... chars=...` unchanged.
- **Fails-if:** `dotnet build` fails; the file is still read twice on a cache miss.

### BP-37 — R42: offload/cache the IsAvailable probe
- **Files:** `Telescope/Filter/FzfFilter.cs`, `Telescope/Overlay/TelescopeOverlay.cs`
- **Change:** `FzfFilter.IsAvailable()` (FzfFilter.cs:75-109) runs a synchronous `WaitForExit(500)` subprocess probe on the UI thread during the first overlay open. Offload/async the probe (or cache it — the availability is already cached once per session).
- **Verify-with:** partial — `FzfFilter` hermetic (tested); the UI-thread aspect is in the caller. `dotnet build` 0 errors + existing suites green. Diagnostic: `[Telescope] fzf unavailable — showing unfiltered list` unchanged.
- **Fails-if:** `dotnet build` fails; the probe still blocks the UI thread on first open.

### BP-38 — R43: LineIndex binary search in MoveToLine
- **Files:** `Telescope/Overlay/Utils/TextMotionNavigator.cs`, `tests/Telescope.Tests/Program.cs`
- **Change:** `TextMotionNavigator.MoveToLine` (TextMotionNavigator.cs:148-178) is an O(n) linear scan while `SetText` already builds a `LineIndex` with binary-search line lookups. Use the `LineIndex` binary search.
- **Verify-with:** `Run_TextMotionNavigator_*` (Telescope.Tests) — behavior-preserving (existing `Run_TextMotionNavigator_LineNumber` etc. stay green). Diagnostic: `[Telescope] preview caret=... line=...` unchanged.
- **Fails-if:** `Run_TextMotionNavigator_*` fails (behavior drift); `dotnet build` fails.

### BP-39 — R44: avoid the SetText rebuild per prompt-motion keystroke
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs`, `Telescope/Overlay/Utils/TextMotionNavigator.cs`
- **Change:** `TryPromptMotion` (TelescopeOverlay.cs:600) calls `SetText(_promptBox.Text)` on every prompt-motion keystroke, rebuilding a `LineIndex` each time (TextMotionNavigator.cs:41-46). Avoid the `SetText` rebuild per keystroke. **Shared-method note (explicit, not advisory):** this step edits the SAME `TryPromptMotion` method (TelescopeOverlay.cs:592) that BP-1 (R1) modified in Phase 0 — the Phase 6 build-agent MUST re-verify the Phase 0 behavior is preserved after this edit: the `insertPlacement` out-param routing added by BP-1 (a→Current, A→End, I→Start, i→Current) and the prompt-surface restriction (j/k/g/G NOT consumed in the prompt) must still hold. The `LineIndex` rebuild is in the pure navigator.
- **Verify-with:** partial — `SetText` in the WPF overlay; the `LineIndex` rebuild is in the pure navigator. `dotnet build` 0 errors + existing suites green. **Re-verify Phase 0:** the BP-1-extended `Run_PromptMotionRouter_*` tests (prompt-only restriction + placement routing) stay green after this edit. Diagnostic: `prompt-motion key=... caret=...` unchanged.
- **Fails-if:** `dotnet build` fails; the `LineIndex` is still rebuilt per prompt-motion keystroke; the BP-1-extended `Run_PromptMotionRouter_*` tests fail (Phase 0 behavior regressed by this edit).

## Phase 7 — Dead code + logging (R28, R29, R30, R45)

### BP-40 — R28: prefix the startup banner
- **Files:** `MyExtension/Package/MyExtensionPackage.cs`
- **Change:** the startup banner `=== Global Keyboard Logger Package STARTED ===` (MyExtensionPackage.cs:61) carries no `[MyExtension]`/`[NeoVisual]` prefix — invisible to the prefix-based log gates. Add the `[MyExtension]` prefix. Verified: the harness does NOT assert on the banner.
- **Verify-with:** no hermetic seam (log line) — `dotnet build` 0 errors + existing suites green. Diagnostic: `[MyExtension] === Global Keyboard Logger Package STARTED ===` (prefixed).
- **Fails-if:** `dotnet build` fails; the banner still lacks the prefix.

### BP-41 — R29: delegate OnViewClosed to OnViewLostFocus
- **Files:** `MyExtension/Vim/Utils/VimModeState.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `VimModeState.OnViewLostFocus` and `OnViewClosed` (VimModeState.cs:52-60,67-75) are byte-identical — a future change to one silently diverges. One delegates to the other.
- **Verify-with:** `Run_VimModeState_*` (NeoVisual.Tests) — behavior-preserving (existing tests stay green). Diagnostic: `[NeoVisual] vim-mode=...` unchanged.
- **Fails-if:** `Run_VimModeState_*` fails; `dotnet build` fails.

### BP-42 — R30: invalidate NavigationSettings._cached
- **Files:** `MyExtension/Navigation/Utils/NavigationSettings.cs`, `tests/NeoVisual.Tests/Program.cs`
- **Change:** `NavigationSettings._cached` (NavigationSettings.cs:7) static never invalidated — `FromSystemDpi` reads `SystemDpiX` once and caches forever (stale divide tolerances after a mid-session scaling change). Invalidate the cache (or re-read on a DPI-change signal).
- **Verify-with:** partial — `FromDpi` pure (tested); the invalidation needs a DPI-source seam. `Run_NavigationSettings_FromDpi` stays green. Diagnostic: none changed.
- **Fails-if:** `Run_NavigationSettings_FromDpi` fails; `dotnet build` fails.

### BP-43 — R45: record the accepted n16 disposition (no code change)
- **Files:** none (no code change) — record the disposition in `docs/progress.md` (or the plan's disposition note).
- **Change:** Ctrl+N/P unconditional swallow in `PopupNavigation` (PopupNavigation.cs:47-61) is the documented/accepted n16 design — re-verified still live. No code change; record the disposition (keep documented/accepted).
- **Verify-with:** no code — `dotnet build` 0 errors + existing suites unchanged. Diagnostic: none.
- **Fails-if:** a future change re-opens the swallow without the documented disposition.

## Phase 8 — Test hermeticity (R24, R47, R48)

### BP-44 — R24: delete the redundant timing-dependent fzf test
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** delete `Run_FzfFilter_TimeoutKillsAndFallsBack` (tests/Telescope.Tests/Program.cs:584-638) — timing-dependent (5s wall-clock + 2s GC-poll) and now redundant with the deterministic `Run_FzfFilter_TimeoutAwaitsTasks` (line 640).
- **Verify-with:** Telescope.Tests count drops by 1 (deterministic signal); `dotnet run --project tests/Telescope.Tests` green.
- **Fails-if:** the test count does not drop by 1; the deleted test is still referenced.

### BP-45 — R47: replace the wall-clock bound in IsAvailableBounded
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** `Run_FzfFilter_IsAvailableBounded` (tests/Telescope.Tests/Program.cs:688-708) still asserts a wall-clock bound (`sw.Elapsed < 3s`). Replace the wall-clock bound with a deterministic seam (injected clock).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- IsAvailableBounded` green without a wall-clock bound.
- **Fails-if:** the test still asserts a wall-clock bound.

### BP-46 — R48: behavior-based GrepFinder test
- **Files:** `tests/Telescope.Tests/Program.cs`
- **Change:** `Run_GrepFinder_GatherHitsThrowsNotSupported` (tests/Telescope.Tests/Program.cs:2436-2444) invokes the protected `GatherHits` via reflection — couples the test to the internal method name/visibility. Assert behavior instead (or make the seam public/internal-testable).
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- GrepFinder` green without reflection.
- **Fails-if:** the test still invokes `GatherHits` via reflection.

## Phase 9 — Docs drift (R25, R26, R27, R50, R51)

### BP-47 — R25: progress.md header "Last item"
- **Files:** `docs/progress.md`
- **Change:** header "Last item" (progress.md:10) still says the combined plan is "in progress" while Current state records GREEN 2026-10-02. Update the header.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` clean.
- **Fails-if:** doc-ref lint fails; the header still says "in progress".

### BP-48 — R26: progress.md "Next up"
- **Files:** `docs/progress.md`
- **Change:** "Next up" (progress.md:22) still lists F5/F8/F9 as open — covered by the now-GREEN combined plan (F5→CR2, F8/F9→M2). Update the section.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` clean.
- **Fails-if:** doc-ref lint fails; the section still lists F5/F8/F9 as open.

### BP-49 — R27: spec.md + AGENTS.md fzf status
- **Files:** `docs/spec.md`, `AGENTS.md`
- **Change:** fzf finder status (spec.md:403 + AGENTS.md:314) says "Scope deferred by user 2026-09-19" but the scope was DECIDED 2026-09-28 and the plan is PLANNED (not executed). Update the status.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` clean.
- **Fails-if:** doc-ref lint fails; the status still says "deferred".

### BP-50 — R50: architecture-review.md "Verified-clean"
- **Files:** `docs/reviews/architecture-review.md`
- **Change:** "Verified-clean" (architecture-review.md:263) still claims `CardinalNavigation`/`CardinalMovment` — post-restructure it's `MyExtension.Navigation`/`Navigation/`. Update the claim.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` clean.
- **Fails-if:** doc-ref lint fails; the claim still references the old names.

### BP-51 — R51: progress.md fzf status contradiction
- **Files:** `docs/progress.md`
- **Change:** item 5 (progress.md:287 + :28,:259) says the fzf finder is "DEFERRED (scope TBD)" while item 3 + Decisions say "SCOPE DECIDED 2026-09-28 ... PLANNED" — internal contradiction. Reconcile the status.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` clean.
- **Fails-if:** doc-ref lint fails; the contradiction remains.

---

# Part C — Verification Trace

> **Known-RED allowlist:** None — no known-RED e2e scenario remains (per docs/progress.md).
> All fixes are RED-proven by unit tests (or build + existing suites for no-seam items).
> R45 is the documented/accepted n16 design — no code change, disposition recorded in
> BP-43. The verification-agent must NOT flag R45 as a regression.

| failing test / gate | implicated steps | expected diagnostic |
|---|---|---|
| `Run_PromptMotionRouter_*` (prompt-only restriction) | BP-1 | `prompt-motion key=... caret=...` for h/l/w/b/e/0/$; NOT emitted for j/k/g/G in the prompt |
| `Run_PromptMotionRouter_InsertPlacementsNotConsumed` (extended for the surface param — pins a→Current, A→End, i→Current, I→Start); existing `Run_KeyHandler_*` / `Run_CaretPlacement_*` stay green | BP-1 | `key=I mode=normal handled=True`; `Focus prompt => True, mode=insert` (live placements via deferred e2e E2E-CR51-1) |
| `Run_PreviewTokenCache_*` / `Run_PreviewDocumentCache_*` (document cache) | BP-2 | `[Telescope] preview tokens=...` once per content change; `[Telescope] preview caret=... line=...` per selection |
| VimModeSource lifecycle helper (R3) | BP-3 | `[NeoVisual] vim-mode=...` unchanged |
| build (R4) | BP-4 | `navigate direction=L/R/D/U` unchanged |
| harness `-SelfCheck` (R5 post-tap gate) | BP-5 | `Focus prompt => True, mode=insert` post-tap; `key=I/A mode=normal handled=True` |
| build (R13 shell-wait gate) | BP-6 | `[MyExtension] init shell-wait ok` when env vars set; skipped otherwise |
| harness `-SelfCheck` (R49 self-seeding) | BP-7 | none changed |
| `Run_FocusGuard_*` (R6 deletion) | BP-8 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| build (R7) | BP-9 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| `Run_FocusGuard_*` (R10 shift gating) | BP-10 | no shift-swallow; `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| `Run_FocusGuard_*` (R11 shift narrowing) | BP-11 | `[NeoVisual] vim-mode=...` unchanged; uppercase typing not swallowed |
| `Run_InjectedKeyGuard_*` (R12 TTL at record) | BP-12 | none changed |
| `Run_VimBufferSubscriptions_*` (R14 deletion) | BP-13 | `[NeoVisual] vim-mode=...` unchanged |
| build (R46) | BP-14 | `[NeoVisual] vim-mode=...` unchanged |
| `Run_WindowAdapter_*` / `Run_NavigationSnapshot_*` (R15) | BP-15 | `navigate direction=L/R/D/U` unchanged |
| build (R16) | BP-16 | `[NeoVisual] window rect unavailable; using empty rect` (n19) on HRESULT failure |
| build (R31) | BP-17 | none changed |
| build (R32) | BP-18 | `navigate direction=L/R/D/U` unchanged |
| build (R33) | BP-19 | `[NeoVisual] Window navigator initialization failed` (renamed) |
| build (R34) | BP-20 | none changed |
| `Run_FocusKeeperSchedule_*` (R8) | BP-21 | `[NeoVisual] solution-explorer select file=...` unchanged |
| `Run_HierarchyResolver_*` (R9 empty-list RED) | BP-22 | `[NeoVisual] solution-explorer select file=...` unchanged |
| build (R17) | BP-23 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| build (R18) | BP-24 | `[NeoVisual] text-motion key=... caret=...` unchanged |
| `Run_HierarchyResolver_*` (R19 delegation) | BP-25 | `[NeoVisual] solution-explorer select file=...` unchanged |
| `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` (R20 caching RED, instance-scoped) | BP-26 | none changed |
| build (R21) | BP-27 | `[NeoVisual] block-caret active=True|False` unchanged |
| build (R35) | BP-28 | `[NeoVisual] toolwindow-move key=... -> arrow vk=...` unchanged |
| `Run_FocusKeeperSchedule_*` (R36) | BP-29 | none changed |
| build (R37) | BP-30 | `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input` unchanged |
| build (R38) | BP-31 | `[NeoVisual] editor-view-opened file=...` exactly once per open |
| build (R39) | BP-32 | `[NeoVisual] text-motion key=... caret=...` unchanged (sample sanitized) |
| build (R22) | BP-33 | `[Telescope] fzf filter failed: ...` unchanged |
| `Run_FzfFilter_*` (R23 cancellation RED) | BP-34 | `[Telescope] fzf filter failed: ...` unchanged |
| `Run_ResultMapper_*` (R40) | BP-35 | `[Telescope] result-mapper unknown display: ...` unchanged |
| build (R41) | BP-36 | `[Telescope] preview file=... chars=...` unchanged |
| build (R42) | BP-37 | `[Telescope] fzf unavailable — showing unfiltered list` unchanged |
| `Run_TextMotionNavigator_*` (R43) | BP-38 | `[Telescope] preview caret=... line=...` unchanged |
| build (R44) + re-verify Phase 0 (`Run_PromptMotionRouter_*` extended by BP-1 stay green) | BP-39 | `prompt-motion key=... caret=...` unchanged |
| build (R28) | BP-40 | `[MyExtension] === Global Keyboard Logger Package STARTED ===` |
| `Run_VimModeState_*` (R29) | BP-41 | `[NeoVisual] vim-mode=...` unchanged |
| `Run_NavigationSettings_*` (R30) | BP-42 | none changed |
| build (R45 disposition) | BP-43 | none changed |
| Telescope.Tests count drop (R24) | BP-44 | none changed |
| hermeticity (R47) | BP-45 | none changed |
| hermeticity (R48) | BP-46 | none changed |
| doc-ref lint (R25) | BP-47 | none changed |
| doc-ref lint (R26) | BP-48 | none changed |
| doc-ref lint (R27) | BP-49 | none changed |
| doc-ref lint (R50) | BP-50 | none changed |
| doc-ref lint (R51) | BP-51 | none changed |

---

# Part D — Execution Log

## Attempt 1 — GREEN (2026-10-02)

**Lane:** bugfix (unit-only, e2e deferred). **Verdict:** GREEN — all 51 BP steps done,
build 0 errors, Telescope.Tests 153/153, NeoVisual.Tests 163/163, doc-ref lint clean,
harness `-List` 35 scenarios + `-SelfCheck` 12/12 pass.

**Per-BP status (all done):**
- Phase 0: BP-1 (R1) done — `Run_PromptMotionRouter_PromptRestrictsVerticalMotions` RED→GREEN; BP-2 (R2) done — `Run_PreviewDocumentCache_UnchangedMtimeSkipsRebuild`; BP-3 (R3) done — `Run_VimBufferSubscriptions_UnsubscribeRemovesClosedAndRefcount`; BP-4 (R4) done (build).
- Phase 1: BP-5 (R5) done — telescope-mode i/a post-tap assertions (`Assert-NewLogLineAfter`); BP-6 (R13) done — shell-wait gated on `IsHarnessRun()`; BP-7 (R49) done — seed-leak standalone `-SelfCheck` seam.
- Phase 2: BP-8 (R6) done — `HasToolWindowActionKeys` deleted + doc refs propagated; BP-9 (R7) done; BP-10 (R10) done — `Run_FocusGuard_ShiftGatesActionKeysForNonTextInput` RED→GREEN (6-arg shift-aware overload); BP-11 (R11) done — shift narrowed in `IsKeyOfInterest`; BP-12 (R12) done — `Run_InjectedKeyGuard_StaleRecordExpiredAtRecord` RED→GREEN.
- Phase 3: BP-13 (R14) done — `FocusedView`/`FocusedBuffer`/`_focusedView` deleted (completed by debug-agent after VERIFY flagged it missing); BP-14 (R46) done.
- Phase 4: BP-15/16/17/20 (R15/R16/R31/R34) done; BP-18 (R32) done — linked-filter cache (DEVIATION D1 ACCEPT: static cache keyed by adapters list reference, invalidated on focus change — bounded, correct); BP-19 (R33) done — "Window navigator initialization failed" rename.
- Phase 5: BP-21 (R8) done — `FocusKeeper.Run` returns `IDisposable` + cancels prior; BP-22 (R9) done — `Run_HierarchyResolver_PrimaryFilePath_EmptyReturnsNull` RED→GREEN; BP-23 (R17) done; BP-24 (R18) done — caret-relative slice (DEVIATION D2 ACCEPT: editor-view path only; WPF/WinForms have no substring API); BP-25 (R19) done — `HierarchyWalker.FirstPathEndingWith` (DEVIATION D3 ACCEPT: walk-without-existence-filter to keep existing tests green); BP-26 (R20) done — instance-scoped `_defaultControllers` cache (NOT static), `Run_WindowManager_DefaultControllerCache_ReturnsCachedInstance` RED→GREEN; BP-27 (R21) done; BP-28 (R35) done — H/L delegate to `TryMoveArrow`; BP-29 (R36) done — `MaxEscapeAttempts`; BP-30 (R37) done; BP-31 (R38) done — `editor-view-opened` exactly once; BP-32 (R39) done — sample sanitized.
- Phase 6: BP-33 (R22) done; BP-34 (R23) done — `Run_FzfFilter_CancellationObservesTasks` RED→GREEN; BP-35 (R40) done — `ResultMapper` instance class; BP-36 (R41) done; BP-37 (R42) done; BP-38 (R43) done — `LineIndex` binary search; BP-39 (R44) done — `SetText`-skip guard (already present from the BP-1 edit; Phase 0 re-verified).
- Phase 7: BP-40 (R28) done — `[MyExtension]` banner prefix; BP-41 (R29) done — `OnViewClosed` delegates; BP-42 (R30) done — `NavigationSettings.Invalidate()`; BP-43 (R45) done — n16 disposition recorded (no code).
- Phase 8: BP-44 (R24) done — timing test deleted (count drop); BP-45 (R47) done — deterministic `IsAvailableBounded`; BP-46 (R48) done — behavior-based GrepFinder test.
- Phase 9: BP-47/48 (R25/R26) done; BP-49 (R27) done — fzf status; BP-50 (R50) done — architecture-review names; BP-51 (R51) done — fzf status reconciled.

**DEVIATIONS (all ACCEPT — none change a diagnostic format or contract):**
- D1 (R32): static linked-filter cache in `WindowNavigator` — keyed by the adapters list reference, invalidated on focus change (WindowManager re-enumerates → new reference); bounded single entry; the plan's anti-static Fails-if was BP-26/R20's (implemented instance-scoped).
- D2 (R18): caret-relative slice for the editor-view path only; WPF/WinForms materialize full text (no substring API). Log lines byte-identical.
- D3 (R19): added `HierarchyWalker.FirstPathEndingWith` (walk + `.cs` filter, no `File.Exists`) to keep the existing `Run_HierarchyResolver_FirstSourceFile_*` tests green.
- D4 (BP-8 doc propagation): editing `docs/spec.md`/`docs/progress.md` was plan-mandated (BP-8 names them); "9 vs 10 test callers" count discrepancy.
- D5 (VERIFY-time): R14/BP-13 deletion was initially skipped — completed by debug-agent (REJECT-as-is → fixed). E2E-CR51-1..10 were missing from `e2e-queue.md` — appended by the hub.

**Verifier verdict:** PASS (no regressions; R45 disposition not flagged). **Debug verdicts:** pass (broken build fix + BP-13 completion).

**Evidence (capped):** RED tests all failed for the right reason pre-fix (R1 j/k/g/G consumed in prompt; R23 cancellation path unobserved; R10 shift not gated; R9 empty-list throws; R12 stale record carried; R20 fresh instance per miss) and are GREEN post-fix. Final: Telescope 153/153, NeoVisual 163/163, build 0 errors, doc-ref lint 0 unresolved, harness `-List` 35 + `-SelfCheck` 12/12.

**Cost:** `delegations: 11 | VS boots: 0 | iterations: 0` (the build-agent's two step-limit interruptions were delegation-time budget exhaustion, not regressions; no re-plan loop was entered).

**Failure-log sweep (final gate):** 6 entries read, 0 fixed (all 6 already `FIXED` in prior sweeps), 0 queued, 0 annotated — no new command failures were reported during this item.
