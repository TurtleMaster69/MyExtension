# E2E queue

> **POLICY CHANGE (2026-10-04, user instruction):** e2e tests are **DEFERRED again**.
> Do NOT run `tools/harness/test-e2e.ps1` (incl. `-Tests <subset>`) until the user says so.
> Every item from now on uses the **unit-only lane** and appends its e2e gate here with
> **Status: QUEUED**. Run the queued gates later on a VS-capable machine when the user
> authorizes it. (The no-VS harness-health self-checks — `-List`, `-SelfCheck`,
> `check-doc-refs.ps1` — remain allowed.)

**Status (2026-10-02): EMPTY** — every previously-queued e2e gate had been run GREEN and
removed from the active queue. **Update 2026-10-04:** the queue is no longer empty — the
Gap 1 item (unit-only lane) queued **E2E-GAP1-1..5** (see the Gap 1 section below).

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

## Queued gates — Gap 1 (2026-10-04)

Plan: `docs/implementation_plan.md` — **Gap 1: window-management leader bindings (`w` prefix)
+ case-sensitive leader combos** (feature lane, unit-only, e2e deferred). Five gates, all
**Status: QUEUED** — run on a VS-capable machine when the user authorizes e2e:

- **E2E-GAP1-1** — the NEW `neovisual-window-management` scenario (registered by the Gap 1
  harness update, never executed; this gate is its first live run). Asserts `Space w -` →
  `[NeoVisual] leader-binding executed: w,-`; `Space w |` → `leader-binding executed: w,|`;
  `Space w d` with an editor focused → `leader-binding executed: w,d` + the active document
  changes (bounded poll); `Space w d` with Solution Explorer focused →
  `leader-binding executed: w,d`. Tab-group geometry is deliberately never asserted (VS moves
  the active tab rather than duplicating it). Covers AC1–AC3.
- **E2E-GAP1-2** — `neovisual-leader` (UPDATED in place): the leader system still fires after
  the lowercase migration — `Space e` → `leader-binding executed: e` and `Space w -` →
  `leader-binding executed: w,-`. Covers AC6.
- **E2E-GAP1-3** — `neovisual-editor-insert` (UPDATED in place): insert-mode typing still
  reaches the editor; the save step uses Ctrl+S (`Send-Ctrl 0x53`) and the file content
  contains the marker (no leader assertion). Covers AC8.
- **E2E-GAP1-4** — `neovisual-explorer-move-editor-focus` (UPDATED in place): editor-focused
  `m` still leaks no tree action; the positive focus bound is Space+B,D (`b,d` → File.Close,
  which also closes the scenario's Gamma.cs). Covers AC9.
- **E2E-GAP1-5** — full-suite regression re-run (all 38 registered scenarios): no regression
  from the lowercase migration across all leader-driven scenarios (all existing
  `[NeoVisual]`/`[Telescope]` lines, lowercased sequences).
