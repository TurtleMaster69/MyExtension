# E2E queue (deferred — Architecture consolidation)

The Architecture consolidation (5 lanes) was executed in the **unit-only lane**:
every merge is behavior-preserving, so the e2e gate is the **existing scenarios
staying green** on a capable machine (one that can boot the VS Experimental
Instance). This file queues those gates. **Status: QUEUED** — none executed on
the consolidation machine (2026-09-28).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no
log-literal drift. Diagnostics depended on: the existing
`[Telescope]`/`[NeoVisual]`/`[Hook]` lines (unchanged).

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry).

## E2E-AC-1 — Lane 1 (mechanical dedup)

- **Status:** QUEUED
- **Scenarios:** hook install + navigation + `telescope-open` (C1);
  `telescope-references`/`telescope-implementation` + `telescope-issues`/`telescope-grep`
  (C6+L4); all `telescope-*` (C2); `neovisual-leader` (C3);
  `telescope-references`/`telescope-implementation` (C4); e2e log contract (L5);
  `telescope-prompt-motions` (L7); `telescope-grep` (L6); unit suites (X3).
- **Assert:** all listed scenarios stay GREEN; no log-literal drift.

## E2E-AC-2 — Lane 2 (vim-motion / caret cluster)

- **Status:** QUEUED
- **Scenarios:** `neovisual-textinput-motions`; `explorer-open-searchbox`;
  **`explorer-open-navigation` (T3's gate)**; all `neovisual-explorer-*`;
  **full existing suite (T4 — ControllerBase touches all three controllers)**.
- **Assert:** all listed scenarios stay GREEN; no log-literal drift.

## E2E-AC-3 — Lane 3 (finder cluster)

- **Status:** QUEUED
- **Scenarios:** all `telescope-*` finder scenarios
  (`open-file`/`issues`/`grep`/`references`/`implementation`/`preview`);
  `telescope-references`/`telescope-implementation` (C5);
  `neovisual-toolwindow`/`explorer-*` (C7).
- **Assert:** all listed scenarios stay GREEN; no log-literal drift.

## E2E-AC-4 — Lane 4 (navigation cluster)

- **Status:** QUEUED
- **Scenarios:** `neovisual-window-nav`; `neovisual-toolwindow`.
- **Assert:** all listed scenarios stay GREEN; no log-literal drift.

## E2E-AC-5 — Lane 5 (cross-cutting)

- **Status:** QUEUED
- **Scenarios:** full 35-scenario suite (X1 touches the log contract); e2e suite
  (X2); `neovisual-toolwindow`/`textinput` (C8); build + unit suites (X4).
- **Assert:** all listed scenarios stay GREEN; no log-literal drift.

---

# E2E queue (deferred — Code review findings, 75 findings / 12 phases)

The Code review findings plan (`docs/implementation_plan.md`, 2026-09-29) was
executed in the **unit-only lane**: every fix is RED-proven by unit tests (or
build + existing suites for no-seam items), so the e2e gate is the listed
scenarios staying GREEN on a capable machine (one that can boot the VS
Experimental Instance). This file queues those gates. **Status: QUEUED** — none
executed on the unit-only machine (2026-09-29).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no
log-literal drift. Diagnostics depended on: the existing
`[Telescope]`/`[NeoVisual]`/`[Hook]` lines (unchanged) plus the new diagnostics
added by the plan (`[NeoVisual] stale-toolwindow sentinel active`,
`[NeoVisual] leader-binding failed:`, `[NeoVisual] shortcut-binding failed:`,
`[NeoVisual] output pane unavailable:`, `[MyExtension] init <step> ok/failed`,
`[Telescope] fzf filter failed:`, `[Telescope] fzf unavailable — showing
unfiltered list`, `[Telescope] filter failed:`).

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry).

## E2E-CR-1 — CR1 (per-type controller loop overwrites SolutionExplorerController)

- **Status:** QUEUED
- **Scenarios:** all `neovisual-explorer-*` scenarios.
- **Assert:** `o`/`Enter`/`r`/`m`/`a`/`g`/`i` still route to
  `SolutionExplorerController` (`solution-explorer open/rename/move/add/select file=`
  lines) — the specialized controller is no longer overwritten by the per-type loop.

## E2E-CR-2 — CR2 (e2e runner false-PASS gate)

- **Status:** QUEUED
- **Scenarios:** a stub scenario returning `$false`.
- **Assert:** the run exits 1 with `RESULT: FAIL` (proves the gate catches
  `$false`); a throwing scenario does NOT double-count (only the exception
  message, no "returned false").

## E2E-CR-3 — CR3 (deferred ShowDialog after CloseOverlay)

- **Status:** QUEUED
- **Scenarios:** open + immediately close the overlay repeatedly.
- **Assert:** no unhandled dispatcher exception; `[Telescope] overlay closed`
  still emitted.

## E2E-M3 — M3 (stale-toolwindow sentinel cache + positive diagnostic)

- **Status:** QUEUED
- **Scenarios:** `neovisual-explorer-move-editor-focus` (the sentinel scenario).
- **Assert:** the positive `[NeoVisual] stale-toolwindow sentinel active` line is
  emitted (proves the fault was injected, not skipped).

## E2E-M40 — M40 (NavigateInDirection(Direction) signature change)

- **Status:** QUEUED
- **Scenarios:** `neovisual-window-nav`.
- **Assert:** `navigate direction=L/R/D/U` single-char tokens unchanged after the
  `Direction` signature change.

## E2E-M24 — M24 (TryDispatch vim-motion unification)

- **Status:** QUEUED
- **Scenarios:** `telescope-prompt-motions` / `telescope-preview-motions` /
  `neovisual-textinput-motions`.
- **Assert:** per-surface log lines unchanged after the `TryDispatch`
  unification; the `$` drift fixed (bare `4` in preview no longer jumps).

## E2E-M16 — M16 (FocusGuard textInputSurfaceFocused)

- **Status:** QUEUED
- **Scenarios:** `neovisual-textinput-motions` (D4 deviation must not regress) +
  a new assertion that a text-input surface in normal mode while the editor
  holds focus does NOT move the editor's caret.

## E2E-M34 — M34 (FocusTargetModel extraction)

- **Status:** QUEUED
- **Scenarios:** `telescope-preview`.
- **Assert:** `[Telescope] focus target=List|Preview` unchanged after the
  `FocusTargetModel` extraction.

## E2E-M17/M32 — M17/M32 (VimModeState / VimModeClassifier)

- **Status:** QUEUED
- **Scenarios:** `neovisual-editor-insert` + the focus-arrival signals.
- **Assert:** `vim-mode=Insert|Normal|Replace` unchanged.

## E2E-M1/M4 — M1/M4 (hook hot-path contract)

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** no `[Hook] key=` per-key lines; all `neovisual-*` scenarios still
  GREEN (hook hot-path contract restored).

## E2E-M5/M6/M7/M8 — M5-M8 (finder path amortization)

- **Status:** QUEUED
- **Scenarios:** `telescope-grep` / `telescope-search` / `telescope-preview`.
- **Assert:** `grep hits=...`, `results count=...`, `preview caret=...` unchanged;
  fzf failure paths log `fzf filter failed:` / `filter failed:`.

## E2E-M28/M36/M37/m26/m28 — harness hardening

- **Status:** QUEUED
- **Scenarios:** full suite + the `-SelfCheck`/`-SelfTest` seams.
- **Assert:** `seed-leak` still GREEN (no seed writes); budget + cache-index
  discipline hold.

## E2E-M20/M29/M42 — test hermeticity / scaffolding

- **Status:** QUEUED
- **Scenarios:** full suite GREEN with the consolidated test scaffolding.
- **Assert:** counts per the runner (Telescope/NeoVisual after the M29/M42
  consolidation); fzf test fails loudly when fzf absent; keybinding test hermetic.

---

# E2E queue (deferred — Code review findings, 98 findings / 15 phases + Functional restructure)

The combined plan (`docs/implementation_plan.md`, 2026-10-01) is executed in the
**unit-only lane**: every fix is RED-proven by unit tests (or build + existing suites
for no-seam items), so the e2e gate is the listed scenarios staying GREEN on a capable
machine (one that can boot the VS Experimental Instance). This file queues those
gates. **Status: QUEUED** — none executed on the unit-only machine (2026-10-01).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal
drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]`/
`[MyExtension]` lines (unchanged) plus the new diagnostic added by the plan
(`[NeoVisual] navigate activated index=...` / `[NeoVisual] navigate no-op: <reason>`).

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry).

## E2E-NCR-1 — M1 (a/A/I prompt insert)

- **Status:** QUEUED
- **Scenarios:** `telescope-mode`.
- **Assert:** `a`/`A`/`I` in the prompt enter insert mode (`Focus prompt => True,
  mode=insert` after `a`) — the false-positive gate is replaced by a real assertion.

## E2E-NCR-2 — M8 (g opens the primary file)

- **Status:** QUEUED
- **Scenarios:** `explorer-open-navigation`.
- **Assert:** `g` opens the primary `.cs` file (`solution-explorer select file=.*\.cs`,
  not `.resx`).

## E2E-NCR-M2/M4 — finder path amortization

- **Status:** QUEUED
- **Scenarios:** `telescope-grep` / `telescope-preview`.
- **Assert:** `grep hits=...`, `preview caret=...` unchanged after the drop-Task.Run
  change (behavior-preserving).

## E2E-NCR-M5 — FocusGuard single-source ownsKeyboard

- **Status:** QUEUED
- **Scenarios:** `neovisual-textinput-motions` + all `neovisual-explorer-*`.
- **Assert:** routing unchanged after the single-source `ownsKeyboard` refactor.

## E2E-NCR-M3 — Send-Text case fidelity

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** `Send-Text` case-fidelity — the `query='Program'` assertions now verify
  case (Shift applied for uppercase).

## E2E-NCR-M7 — fzf timeout fallback

- **Status:** QUEUED
- **Scenarios:** `telescope-search`.
- **Assert:** the fzf timeout path still falls back (`fzf filter failed:` /
  `filter failed:`).

## E2E-NCR-m47 — navigation outcome diagnostic

- **Status:** QUEUED
- **Scenarios:** `neovisual-window-nav`.
- **Assert:** `navigate activated index=...` / `navigate no-op: <reason>` outcome line
  emitted.

## E2E-RESTRUCTURE-2 — Functional restructure (Phases 11-14)

- **Status:** QUEUED
- **Scenarios:** full 35-scenario suite.
- **Assert:** no behavior change after the folder/Utils restructure + the 5 renames
  (WindowMatrix→WindowNavigator, CardinalNavigationConstants→NavigationConstants,
  UtilityMethods→WindowFrameUtils, RectCoordinate→WindowRect,
  WindowAdapter→WindowFrameAdapter) — all diagnostics byte-identical.

## E2E-NCR-BACKLOG — M6 reconciliation (the prior 67-findings plan's missing gates)

- **Status:** QUEUED
- **Scenarios:** the gates the 67-findings plan (commit `92b119f`) claimed were queued
  but never materialized: E2E-NCR-1..2, E2E-NCR-M1/M2/M15 .. E2E-NCR-M26/M27/M28,
  E2E-RESTRUCTURE-1 (scenario lists from the prior implementation_plan.md:739-769).
- **Assert:** the ALREADY-GREEN 67-findings plan's deferred gates are run on a capable
  machine — the restructure's behavior-preservation gate (E2E-RESTRUCTURE-1, full
  35-scenario suite) is included.

---

# E2E queue (deferred — Code review findings, 67 findings / 15 phases, commit 92b119f)

The PRIOR 67-findings plan (`docs/implementation_plan.md` at commit `92b119f`,
2026-09-30) reached GREEN in the unit-only lane and claimed its e2e gates were queued
under E2E-NCR-1..2 / E2E-NCR-M1/M2/M15 .. E2E-NCR-M26/M27/M28 / E2E-RESTRUCTURE-1 —
but those entries never materialized in this file (the combined plan's E2E-NCR-1/2 now
own those IDs). This section re-queues the 67-findings plan's deferred gates under
DISTINCT `E2E-NCR-67-*` IDs so gate results stay attributable. **Status: QUEUED** —
none executed on the unit-only machine (2026-10-01).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal
drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]`/
`[MyExtension]` lines (unchanged).

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry).

## E2E-NCR-67-1 — CR1 (Command Window `I` insert)

- **Status:** QUEUED
- **Scenarios:** `neovisual-textinput-motions`.
- **Assert:** `textinput-enter-input start caret=0` fires when `I` is pressed on the
  Command Window.

## E2E-NCR-67-2 — CR2 (multi-view `vim-mode=` subscription)

- **Status:** QUEUED
- **Scenarios:** a multi-view scenario — with 2+ editor views open, closing a
  non-focused view does NOT kill the focused view's `vim-mode=` subscription (Space
  types a literal space in insert mode).
- **Assert:** `vim-mode=Insert|Normal|Replace` unchanged after closing a non-focused
  view.

## E2E-NCR-67-M1, E2E-NCR-67-M2, E2E-NCR-67-M15 — hook hot-path contract

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** no per-key COM/stat; all `neovisual-*` scenarios still GREEN (hook
  hot-path contract restored).

## E2E-NCR-67-M3/M4/M5/M13 — finder path amortization

- **Status:** QUEUED
- **Scenarios:** `telescope-grep` / `telescope-search` / `telescope-preview`.
- **Assert:** `grep hits=...`, `results count=...`, `preview caret=...` unchanged; fzf
  failure paths log `fzf filter failed:` / `filter failed:`.

## E2E-NCR-67-M10/M11/M19/M6 — motion/preview

- **Status:** QUEUED
- **Scenarios:** `telescope-preview-motions` / `telescope-prompt-motions` /
  `neovisual-textinput-motions`.
- **Assert:** caret positions unchanged; the `$` drift fixed (bare `4` in preview no
  longer jumps).

## E2E-NCR-67-M9/M14 — navigation

- **Status:** QUEUED
- **Scenarios:** `neovisual-window-nav`.
- **Assert:** `navigate direction=L/R/D/U` unchanged.

## E2E-NCR-67-M8/M12/M16/M25 — safety/logging

- **Status:** QUEUED
- **Scenarios:** `neovisual-editor-insert` + `neovisual-textinput-motions`.
- **Assert:** `vim-mode=` unchanged; a controller exception logs
  `toolwindow-move failed:` and passes through.

## E2E-NCR-67-M18/M20/M21/M22/M24 — duplication

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** no log-literal drift.

## E2E-NCR-67-M17/M23 — dead code

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** no log-literal drift.

## E2E-NCR-67-M26, E2E-NCR-67-M27, E2E-NCR-67-M28 — harness

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** the tightened `preview tokens=`/`results count=` assertions pass;
  `seed-leak` still GREEN.

## E2E-RESTRUCTURE-67-1 — Phases 11-14 restructure

- **Status:** QUEUED
- **Scenarios:** full 35-scenario suite.
- **Assert:** no behavior change after the folder/namespace restructure (all
  diagnostics byte-identical).

---

# E2E queue (deferred — Code review fixes, 51 findings / 10 phases)

The 51-findings plan (`docs/implementation_plan.md`, 2026-10-02) is executed in the
**unit-only lane**: every fix is RED-proven by unit tests (or build + existing suites
for no-seam items), so the e2e gate is the listed scenarios staying GREEN on a capable
machine (one that can boot the VS Experimental Instance). This file queues those
gates. **Status: QUEUED** — none executed on the unit-only machine (2026-10-02).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal
drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]`/
`[MyExtension]` lines (unchanged) plus the plan's documented changes (R33 renamed
"Window matrix initialization failed" → "Window navigator initialization failed"; R28
prefixed the startup banner with `[MyExtension]`).

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry).

## E2E-CR51-1 — R1 (prompt selection navigation + a/A/I placements)

- **Status:** QUEUED
- **Scenarios:** `telescope-navigate` + `telescope-wrap` + `telescope-mode`.
- **Assert:** j/k move the selection and g/G jump in the prompt (`results count=...`
  `selected=...` after j/k; G→last, gg→first); `a`/`A`/`I` enter insert mode with the
  correct placements (`Focus prompt => True, mode=insert` after `a`; `key=I mode=normal
  handled=True`).

## E2E-CR51-2 — R2 (preview document cache)

- **Status:** QUEUED
- **Scenarios:** `telescope-preview`.
- **Assert:** preview caret/scroll preserved across selection changes in the same file
  (`preview caret=... line=...` unchanged; no scroll reset; `preview tokens=...` once per
  content change).

## E2E-CR51-3 — R3 (VimModeSource buffer-subscription lifecycle)

- **Status:** QUEUED
- **Scenarios:** `neovisual-editor-insert` + a multi-view scenario.
- **Assert:** closing a non-focused view does NOT kill the focused view's `SwitchedMode`
  subscription (`vim-mode=Insert|Normal|Replace` unchanged).

## E2E-CR51-4 — R4 (per-frame fault isolation)

- **Status:** QUEUED
- **Scenarios:** `neovisual-window-nav`.
- **Assert:** navigation survives a stale frame (`navigate direction=L/R/D/U` +
  `navigate activated index=...`).

## E2E-CR51-5 — R5 (telescope-mode i/a post-tap assertions)

- **Status:** QUEUED
- **Scenarios:** `telescope-mode`.
- **Assert:** the hardened `i`/`a` assertions are real (a re-introduced
  prompt-interception bug fails the gate) — `Focus prompt => True, mode=insert` post-tap.

## E2E-CR51-6 — R10/R11 (shift gating + IsKeyOfInterest narrowing)

- **Status:** QUEUED
- **Scenarios:** all `neovisual-explorer-*` + `neovisual-editor-insert`.
- **Assert:** Shift+O/R/M/A/G do NOT fire tree actions; uppercase typing in the editor is
  not swallowed (`vim-mode=...` unchanged).

## E2E-CR51-7 — R22/R23 (fzf debounce + cancellation observation)

- **Status:** QUEUED
- **Scenarios:** `telescope-search`.
- **Assert:** the non-query-driven fzf path is debounced; overlay close mid-filter
  produces no `UnobservedTaskException` noise (`fzf filter failed:` unchanged).

## E2E-CR51-8 — R38 (editor-view-opened dedupe)

- **Status:** QUEUED
- **Scenarios:** `explorer-open-navigation`.
- **Assert:** `editor-view-opened file=...` emitted exactly once per open.

## E2E-CR51-9 — R33/R28 (renamed diagnostic + prefixed banner)

- **Status:** QUEUED
- **Scenarios:** full suite.
- **Assert:** the renamed "Window navigator initialization failed" + prefixed
  `[MyExtension]` startup banner do not break any assertion.

## E2E-CR51-10 — R49 (self-seeding subsets)

- **Status:** QUEUED
- **Scenarios:** `-Tests seed-leak` alone.
- **Assert:** `-Tests seed-leak` alone runs green (self-seeding subsets).

---

# E2E gates (ACTIVE — Code review fixes, 72 findings / 8 phases)

The 72-findings plan (`docs/implementation_plan.md`, 2026-10-02) is executed in the
**bugfix lane with e2e ENABLED** (user instruction 2026-10-02 — this machine boots the
VS Experimental Instance). Every fix is RED-proven by unit tests (or build + existing
suites for no-seam items), and the listed e2e gates are RUN at VERIFY (affected
scenarios during the loop; the full 35-scenario suite as the item's final gate).
**Status: ACTIVE** — run at VERIFY (2026-10-02).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal
drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]`/
`[MyExtension]` lines (unchanged) plus the plan's documented changes (N4 makes
`block-caret active=False` truthful without changing its format; N5/N72 keep the n19
`window rect unavailable` diagnostic; N24 dedupes `editor-view-opened file=...`).

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry).

## E2E-CR72-1 — N4 (block-caret deactivation)

- **Status:** QUEUED
- **Scenarios:** `neovisual-textinput-motions` + a new assertion that the block caret is
  gone after leaving normal mode / focus loss.
- **Assert:** `[NeoVisual] block-caret active=False` is truthful — the adornment is
  removed on deactivation (the white block does not stay glued over the native caret).

## E2E-CR72-2 — N3 (Vim buffer-subscription lifecycle)

- **Status:** QUEUED
- **Scenarios:** `neovisual-editor-insert` + a multi-view scenario.
- **Assert:** closing a non-focused view does NOT kill the focused view's `SwitchedMode`
  subscription (`vim-mode=Insert|Normal|Replace` unchanged); no unbounded
  `_bufferToTextBuffer` growth.

## E2E-CR72-3 — N5/N72 (HRESULT + n19)

- **Status:** QUEUED
- **Scenarios:** `neovisual-window-nav`.
- **Assert:** a failed `GetWindowScreenRect` emits `window rect unavailable; using empty
  rect` (n19) and navigation degrades gracefully (`navigate direction=L/R/D/U` +
  `navigate activated index=...` / `navigate no-op:`), with the n19 line at most once per
  session.

## E2E-CR72-4 — N9 (false-positive gates)

- **Status:** QUEUED
- **Scenarios:** `telescope-open`, `telescope-mode`, `telescope-navigate`.
- **Assert:** the hardened assertions are real — a fresh post-tap line the
  `Open-Telescope` helper does NOT confirm (a re-introduced prompt-focus/mode bug fails
  the gate), e.g. `Focus prompt => True, mode=insert` after an explicit Escape.

## E2E-CR72-5 — N24 (editor-view-opened dedupe)

- **Status:** QUEUED
- **Scenarios:** `explorer-open-navigation`.
- **Assert:** `editor-view-opened file=...` emitted exactly once per open (no
  double-emission from `SelectFirstSourceFile` + `TextViewCreated`).

## E2E-CR72-6 — N19/N23 (caret-relative slice + A placement)

- **Status:** QUEUED
- **Scenarios:** `neovisual-textinput-motions`.
- **Assert:** `text-motion key=... caret=... len=... text='...'` unchanged after the
  WPF/WinForms caret-relative slice; `A` (InsertEnd) lands at the true line end, not the
  slice boundary.

## E2E-CR72-7 — N31 (prompt `a` after-caret)

- **Status:** QUEUED
- **Scenarios:** `telescope-mode` / `telescope-prompt-motions`.
- **Assert:** `a` in the prompt enters insert mode with the caret AFTER the current
  position (`Focus prompt => True, mode=insert` after `a`; `prompt-motion key=... caret=...`
  unchanged for the motion set).

## E2E-CR72-8 — N38/N39 (fzf availability cached)

- **Status:** QUEUED
- **Scenarios:** `telescope-search`.
- **Assert:** when fzf is missing, no per-keystroke `fzf filter failed` spam — the
  availability is cached and `FilterAsync` returns unfiltered without spawning
  (`fzf unavailable — showing unfiltered list` once; `fzf filter failed:` not per-key).

## E2E-CR72-9 — N43 (user text sanitized)

- **Status:** QUEUED
- **Scenarios:** `telescope-search` / `telescope-issues`.
- **Assert:** user-controlled text (prompt queries, Error List descriptions, display
  strings) is sanitized in log lines — no `\n`/control chars split a `[Telescope]` line
  (`Wait-NewLogLine` assertions stay line-based).

## E2E-CR72-10 — N52/N53 (harness pinning)

- **Status:** QUEUED
- **Scenarios:** `neovisual-explorer-open`, `neovisual-explorer-open-o`,
  `neovisual-editor-insert`, `seed-leak`.
- **Assert:** `neovisual-explorer-open`/`-open-o` pin WHICH file opens (specific
  `solution-explorer open` / `editor-view-opened file=` path, not "ANY file");
  `neovisual-editor-insert` updates the seed-expected copy only on success (a failed
  marker assertion does not record the wrong content as expected); `seed-leak` stays
  GREEN.
