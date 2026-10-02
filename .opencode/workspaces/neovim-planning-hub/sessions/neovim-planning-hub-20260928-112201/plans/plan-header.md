# Implementation Plan — Architecture Consolidation (5 lanes)

> **Lane: refactor (unit-only, e2e deferred).** Executes the whole-repo
> architecture-consolidation program from `docs/architecture-consolidation.md`
> (2026-09-28) — ~30 under-factored seams collapse into the target APIs, every
> extracted pure seam ships with NEW unit tests, and **no
> `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changes** (M-M7 NOT
> triggered — the verification contract §8 forbids it). All e2e verification is
> **deferred to `e2e-queue.md`** (this machine cannot boot the VS Experimental
> Instance); the plan's gate is the two offline unit suites + the new seam tests.
>
> **Scope decision (user, 2026-09-28):** plan EVERYTHING (all 5 lanes) as one
> queue item; e2e deferred to a later date on a capable machine. The end-user
> keybindings UX items (`MyExtension:OpenKeybindings`, `ShowBindings`, surfacing
> unknown actions) are **deferred to feature-triage** — NOT in this plan.

**Goal:** Collapse the ~30 duplicated/under-factored seams identified in
`docs/architecture-consolidation.md` into the target APIs (one `NativeMethods`,
one `TelescopeLauncher`, one `ActionRegistry`, one `VsServices`, one
`WindowAdapter`, one pure `WindowNavigationEngine`, one `TextMotionEngine`, one
`FinderBase<THit>`, one log path), ship NEW unit tests for every extracted pure
seam (the `OverlayKeyHandler`/`TextMotionNavigator` pattern), keep every
diagnostic log literal byte-identical, and leave the repo behaviorally
unchanged — verified by the two offline unit suites staying green and the
deferred e2e suite.

**Approach:** Execute the doc's §7 lane order. Each lane is a self-contained,
independently verifiable unit executed top-to-bottom by the build-agent:

1. **Lane 1 — Mechanical dedup** (C1 → C6+L4 → C2 → C3 → C4 → L5 → L7 → L6 → X3)
2. **Lane 2 — Vim-motion / caret cluster** (T1 → T2 → T4 → T5 → T3)
3. **Lane 3 — Finder cluster** (L3 → L2 → L1 → C5 → C7)
4. **Lane 4 — Navigation cluster** (N2 → N4 → N3 → N1 → matrix-rebuild cache)
5. **Lane 5 — Cross-cutting** (X1 → X2 → C8 → X4)

**BP numbering:** BP numbers are **per-lane** (each lane section carries its own
BP-1..BP-N). The merge ids (C1, T1, L3, N2, X1, …) are globally unique and appear
in every BP step header, so cross-references are unambiguous. Each lane is
executed as a unit with its own gate.

**Structural grounding (trailmark-recon, 2026-09-28):** all 25 claims verified
against the graph (1104 nodes, 494 proxies, 0 entrypoints). Six corrections to
the doc are folded into the lane sections (marked **CORRECTION**): (1) N3 — NO
switch statements in `WindowMatrix`; the 5 filter methods use if/else direction
branches; (2) C2 — 6 open-finder methods, not 7; (3) C3 — `ResolveAction` has 10
cases, not ~14; (4) C4 — ~18 `GetDTE` sites, not 12; (5) L5 — the prefix is
ALREADY centralized in `DiagnosticLog.Telescope` (46 `NeoVisualLog.Log` sites, not
31 hand-written literals); (6) X1 — ~51 `Debug.WriteLine` sites, not 27. Four
false-dead-code traps must NOT be reported as dead: `WindowMatrix.CheckDte`
(called from the ctor), `GeneralToolWindowController` ctor (new-ed in
`WindowManager.GetController`), `TextMotionHelper.TryMoveFocusedTextBox` (proxy →
`SolutionExplorerController`), `LinqExtensionMethods.DistinctBy` (proxy → tests
only).

---
