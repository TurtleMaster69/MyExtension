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
