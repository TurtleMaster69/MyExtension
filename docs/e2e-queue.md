# E2E queue

> **MANDATE CHANGE (2026-10-04, user):** the e2e deferral is LIFTED — this machine supports
> the VS Experimental Instance. Plans now carry their e2e scenarios INLINE (e2e RED before the
> build, e2e VERIFY at the gate); the queued gates below are drained as the loop reaches them.
> Never boot the harness concurrently with the build loop's own VS usage.

**Status: EMPTY (2026-10-04).** Every queued e2e gate has been run GREEN and removed from
the active queue — the Gap 1 gates (E2E-GAP1-1..5) and the Gap 3 gates (E2E-GAP3-1..2)
were all discharged by the **Gap 3 VERIFY's full 39-scenario fresh-boot suite run
(2026-10-04, GREEN; 1 flaky pass-on-retry)** — see "What ran GREEN" below.

## What was removed and why

The deferred gates from the earlier unit-only-lane plans were all satisfied by the
**72-findings plan's full 35-scenario e2e suite run (2026-10-02, GREEN)** — every
scenario those gates reference was exercised and passed on the VS-capable machine:

- **Architecture consolidation (2026-09-28):** `E2E-AC-1..5`.
- **Code review findings, 75 findings (2026-09-29):** `E2E-CR-1..3`,
  `E2E-M3/M40/M24/M16/M34/M17/M32/M1/M4/M5-M8/M28/M36/M37/m26/m28/M20/M29/M42`.
- **Code review findings, 98 findings + Functional restructure (2026-10-01):**
  `E2E-NCR-1..2`, `E2E-NCR-M2/M4/M5/M3/M7/m47`, `E2E-RESTRUCTURE-2`,
  `E2E-NCR-BACKLOG`.
- **Code review findings, 67 findings (2026-09-30):** `E2E-NCR-67-*`,
  `E2E-RESTRUCTURE-67-1`.
- **Code review fixes, 51 findings (2026-10-02):** `E2E-CR51-1..10`.
- **Code review fixes, 72 findings (2026-10-02):** `E2E-CR72-1..10` — RUN at VERIFY
  (the full 35-scenario suite was the item's final gate).

## Adding a new gate

When a plan defers e2e work (unit-only lane), append its gates here with:
- an ID, the finding/step it covers, the scenario list, and the assertion;
- **Status:** QUEUED.

Run: `pwsh tools/harness/test-e2e.ps1 -Tests <scenario-list>` (or the full suite for the
final entry). Remove a gate once it has run GREEN.

## What ran GREEN (2026-10-04)

The queued gates from the two unit-only-lane plans were all satisfied by the **Gap 3
VERIFY's full 39-scenario fresh-boot suite run (run 156; the two fixed scenarios re-proven
in run 155/157)** — every gate was exercised and passed on the VS-capable machine:

- **Gap 1 — window-management leader bindings (2026-10-04):** `E2E-GAP1-1`
  (`neovisual-window-management` first live run — `w,-`/`w,|`/`w,d` ×2 + the active-doc
  poll; flaky ×1 pass-on-retry, run 157 — harness order-dependency, hardening queued in
  `docs/progress.md`), `E2E-GAP1-2` (`neovisual-leader`), `E2E-GAP1-3`
  (`neovisual-editor-insert` — the save step was re-keyed to the DTE `Save-AllDocuments`
  by BP-H3; the injected Ctrl+S chord is consumed by the focused editor's key-processing
  chain), `E2E-GAP1-4` (`neovisual-explorer-move-editor-focus`), `E2E-GAP1-5` (full-suite
  re-run).
- **Gap 3 — diagnostics navigation (2026-10-04):** `E2E-GAP3-1`
  (`neovisual-diagnostic-nav` first live run — the six `leader-binding executed:` lines +
  4× `diagnostic-nav no-op: no-entries`, the expected fresh-instance outcome; zero
  `failed:` lines), `E2E-GAP3-2` (full-suite re-run, 39/39 with the flaky above).

## Queued gates — Telescope columns + preview (2026-10-04)

Plan: `docs/implementation_plan.md` — **Telescope results columns + preview-as-editor**
(the FIRST pending item; feature lane, **e2e ENABLED** — the gates run at VERIFY, nothing is
deferred). Two gates:

- **E2E-RC-1** — the NEW `telescope-results-columns` scenario (created + proven RED by
  `e2e-test-builder` before the build). Asserts the columned render; `results columns=file,dir`
  (the Files default) + the per-finder default columns lines; the byte-stable
  `results count=/selected=`; j→`selected=1`. The chooser toggle is unit-pinned + manual
  (the harness injects keys, not mouse).
- **E2E-RC-2** — full-suite regression re-run at VERIFY (40 registered scenarios): no
  regression across all telescope-* scenarios (36/37 preview sites byte-stable; the 1
  `preview tokens=` site updated per the plan's BP-D10).

## Queued gates — Gap 6 goto (2026-10-04)

Plan: `plan-goto.md` (neovim-planning-hub session) — **Gap 6 (core): goto
commands — `gd`/`gI`/`gr` → single-hit direct, multi-hit Telescope** (feature
lane, **e2e ENABLED** — the deferral is lifted; the scenario is created + proven
RED before the build and executed at VERIFY, so these gates are DRAINED at
VERIFY, not deferred). Two gates:

- **E2E-GOTO-1** — the NEW `telescope-goto` scenario (created by this plan's
  Phase 1; first live run at VERIFY). Asserts: goto-definition single-hit →
  `[Telescope] goto-direct finder=… file=…\Models\Shared.cs line=1` +
  `[Telescope] goto line=1` + the active document becomes Models/Shared.cs;
  goto-definition multi-hit (the seeded `GotoProbe` partial pair) →
  `[Telescope] open finder=Definition candidates=2` + `preview file=.*GotoProbe`;
  goto-references multi-hit → `[Telescope] open finder=References candidates=…`
  (≥2) + `references gathered reads=… writes=…`; goto-implementation single-hit →
  `[Telescope] goto-direct … file=…Shape.cs line=2` + `[Telescope] goto line=2` +
  the active document becomes Shape.cs. Covers AC1–AC6.
- **E2E-GOTO-2** — full-suite regression re-run (all 41 registered scenarios):
  no regression from the 2 new seed files (the `GotoProbe` partial pair) or the
  new scenario; the exact-count queries stay exact and `seed-leak` stays GREEN.
  The two registered-unexecuted scenarios (`neovisual-window-management`,
  `neovisual-diagnostic-nav`) are E2E-GAP1-1/E2E-GAP3-1's gates, not this one's.
