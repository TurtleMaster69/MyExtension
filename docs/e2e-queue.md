# E2E queue (deferred — Architecture consolidation)

The Architecture consolidation (5 lanes) was executed in the **unit-only lane**:
every merge is behavior-preserving, so the e2e gate is the **existing scenarios
staying green** on a capable machine (one that can boot the VS Experimental
Instance). This file queues those gates. **Status: QUEUED** — none executed on
the consolidation machine (2026-09-28).

Each entry asserts: the listed scenarios stay GREEN + `git diff` shows no
log-literal drift. Diagnostics depended on: the existing
`[Telescope]`/`[NeoVisual]`/`[Hook]` lines (unchanged).

Run: `pwsh tools/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
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

Run: `pwsh tools/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
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
