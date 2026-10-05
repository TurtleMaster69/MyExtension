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

## Queued gates — Code review fixes (45 findings, incl. nits) (2026-10-05)

Plan: `docs/implementation_plan.md` — **Code review fixes (45 findings, incl. nits)**
(the FIRST pending item; feature lane, **unit-only, e2e DEFERRED** — the user's 2026-10-05
instruction: "we are not on e2e capable machine so they should be put into queue"). The four
gates below are QUEUED (never created/executed now); they become READY when the plan is GREEN
and are created/executed on a capable machine.

| ID | plan | finding(s) | scenario(s) | assertion | diagnostic | status | notes |
|----|------|-----------|-------------|-----------|------------|--------|-------|
| E2E-CR45-1 | Code review fixes (45 findings, incl. nits) (2026-10-05) - docs/implementation_plan.md (FIRST pending item) | the navigation OUTCOME gate (T1) | `neovisual-window-nav` (UPDATED in place) | after each Ctrl+H/J/K/L chord, `navigate activated index=\d+` appears (NOT "no no-op at all" — some directions may legitimately no-op depending on the scratch layout) | `[NeoVisual] navigate activated index=\d+` | QUEUED | e2e DEFERRED (user 2026-10-05) — READY when the plan is GREEN |
| E2E-CR45-2 | Code review fixes (45 findings, incl. nits) (2026-10-05) - docs/implementation_plan.md (FIRST pending item) | the pane-host consolidation (D1/D2/D6/D14) | `telescope-focus-panes` stays GREEN (re-run) | the collapse must not change the focus behavior — the pinned tie-break (Ctrl+K Input->Preview) + the no-op edges byte-identical | `[Telescope] focus target=Input|List|Preview` + `[Telescope] focus no-op:` UNCHANGED | QUEUED | e2e DEFERRED (user 2026-10-05) — READY when the plan is GREEN |
| E2E-CR45-3 | Code review fixes (45 findings, incl. nits) (2026-10-05) - docs/implementation_plan.md (FIRST pending item) | the vim-mode contract (C1) | `neovisual-editor-insert` (UPDATED in place) + a named-mode assertion | `vim-mode=Insert` unchanged; a named-mode token (`vim-mode=Visual` or the documented `Unknown` on focus loss) appears | `[NeoVisual] vim-mode=Insert` + `vim-mode=Visual|Command|VisualBlock|Select` (M-M7) | QUEUED | e2e DEFERRED (user 2026-10-05) — READY when the plan is GREEN |
| E2E-CR45-4 | Code review fixes (45 findings, incl. nits) (2026-10-05) - docs/implementation_plan.md (FIRST pending item) | the full-suite regression re-run (T3/T4/T5/T6 + D3/D4/D5/D7/D8/D9/D10/D11/D12/D15 + A1-A9 + C2-C7) | none (full 44-scenario fresh-boot re-run) | no regression from the fzf/overlay/finder/tool-window/navigation/harness changes; `seed-leak` must NOT skip (T3); the fixed-baseline contract holds (T4); `telescope-navigate` via the LogCache tail-read (T5); the suite-order invariants enforced (T6) | all existing `[Telescope]`/`[NeoVisual]` lines UNCHANGED except the M-M7 literals (C1/D9/C7) | QUEUED | e2e DEFERRED (user 2026-10-05) — READY when the plan is GREEN |

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
- **Columns UX bugfix (2026-10-04):** `E2E-CUX-1` — discharged by the item's final gate
  (the full 41-scenario fresh-boot suite, run 169, `-TimeoutSec 2400`): 41/41 GREEN,
  `telescope-results-columns` first-try (no retry), `results columns=` / `results count=`
  byte-stable (proven at the source-diff AND live-log level), zero flakes; the 7 visual ACs
  verified by the MANUAL visual pass (code lines + the unit-pinned `ColumnWidths`/
  `ColumnTruncation` invariants; Telescope 221 / NeoVisual 190).
- **Preview buffer-source swap (2026-10-04):** `E2E-PBUF-1` — discharged by the item's final
  gate (the full 41-scenario fresh-boot suite, run 170, `-TimeoutSec 2400`): 41/41 GREEN,
  first-time, zero flakes; `preview file=/caret=/tokens=` byte-stable (63/63/63); the tokens
  VALUE read all-0 (the timing-bound read — see the adjudication in the item's Execution Log);
  the semantic coloring verified by the manual visual pass (code inspection + the deployment
  metadata; Telescope 224 / NeoVisual 190).
- **Gap 11 — git bindings (2026-10-04):** `E2E-GIT-1` — discharged by the item's final gate
  (the full 42-scenario fresh-boot suite, run 174, `-TimeoutSec 2400`): 42/42 GREEN;
  `neovisual-git-bindings` FIRST-RUN PASS — the three `leader-binding executed: g,b|g,h|g,d`
  lines in the pinned order + the POST-retry absence gate clean (the one first-pass
  `ViewHistory` warm-up refusal absorbed by the DESIGNED bounded retry); the dirty-on-disk +
  byte-exact-restore mechanism left `seed-leak`/`seed-reset` GREEN (no allowlist); Telescope
  224 / NeoVisual 191. (The first VERIFY — run 172/173 — FAILED on the
  `CompareWithUnmodified` clean-file refusal, fail-twice → the RE-PLAN fixed it scenario-side;
  zero C# changes.)
- **Feature 7 — the pane architecture (2026-10-05):** `E2E-PANES-1` — discharged by the item's
  final gate (the full 43-scenario fresh-boot suite, run 179, `-TimeoutSec 2400`): 43/43
  GREEN; `telescope-focus-panes` FIRST-RUN PASS — the directional sequences live (Ctrl+K from
  open → `focus target=Preview` the pinned tie-break; Ctrl+H→List; Ctrl+L→Preview; Ctrl+J→
  Input), the two pinned no-op edges the only no-ops, the modal guarantee RE-VERIFIED (the
  overlay never deactivated); the M-M7 three-state sites (`telescope-preview`/
  `telescope-preview-motions`) GREEN; Telescope 258 / NeoVisual 191. (The first VERIFY — runs
  176/177 — FAILED on the layout inversion: the composition kept the prompt docked TOP vs the
  pinned bottom-Input geometry, fail-twice → the RE-PLAN's BP-A5 rev 2 one-line dock flip
  fixed it.)
- **Gap 4 — the recent-files finder (2026-10-05):** `E2E-RECENT-1` — discharged by the item's
  final gate (the full 44-scenario fresh-boot suite, run 185, `-TimeoutSec 2400`): 44/44
  GREEN; `telescope-recent` verified end-to-end — the probe literal ONCE
  (`recent files probe unavailable: ...DISP_E_UNKNOWNNAME` — the pinned best-effort path),
  `recent files gathered count=16`, the Step-2 Order.cs top-match preview proof (the
  most-recent-first AC2), the Step-3 tightened `results count=1 selected=0`, the Step-4
  `opened file:`; Telescope 268 / NeoVisual 191. (Two VERIFY iterations: the lazy session-MRU
  floor → BP-3 rev 2's EAGER hookup; the structurally-unsatisfiable Step-3 preview assertion →
  BP-B1 rev 2's Step-2 attribution; the `telescope-goto` 3rd-strike M-M2 upgrade → BP-B7's gg
  normalization + the bounded re-walk — GREEN outright, the re-walk never fired.)

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

## Queued gates - the planning hub's five plans (2026-10-04)

All five plans are gate-APPROVED and handed off in order (the FIRST pending item is written to
`docs/implementation_plan.md`; the rest live in the hub session
`.opencode/workspaces/neovim-planning-hub/sessions/neovim-planning-hub-20261004-143017/plans/`).
e2e is ENABLED - the gates drain at each plan's VERIFY.

- ~~**E2E-CUX-1** - the columns UX bugfix (FIRST)~~ — **DISCHARGED GREEN 2026-10-04** (the
  item's final gate: the full 41-scenario fresh-boot suite, run 169; `telescope-results-columns`
  first-try, the diagnostics byte-stable at source-diff + live-log level; see the What ran
  GREEN section). No new scenario; the visual ACs were the MANUAL visual pass (all 7 verified).
- ~~**E2E-PBUF-1** - the preview buffer-source swap (second)~~ — **DISCHARGED GREEN 2026-10-04**
  (the item's final gate: the full 41-scenario fresh-boot suite, run 170; the preview scenarios
  GREEN; `preview file=/caret=/tokens=` byte-stable; the tokens VALUE read all-0 — the
  timing-bound read cannot discriminate engagement, adjudicated + doc-corrected; see the What
  ran GREEN section). No new scenario; the semantic coloring is the MANUAL visual pass.
- ~~**E2E-GIT-1** - Gap 11 (third)~~ — **DISCHARGED GREEN 2026-10-04** (the item's final gate:
  the full 42-scenario fresh-boot suite, run 174; `neovisual-git-bindings` FIRST-RUN PASS —
  the three `leader-binding executed:` lines + the POST-retry absence gate clean; the
  dirty-on-disk fix for the `CompareWithUnmodified` clean-file refusal landed via the
  RE-PLAN; see the What ran GREEN section).
- ~~**E2E-PANES-1** - Feature 7 (fourth)~~ — **DISCHARGED GREEN 2026-10-05** (the item's final
  gate: the full 43-scenario fresh-boot suite, run 179; `telescope-focus-panes` FIRST-RUN PASS
  after the BP-A5 rev 2 layout re-compose fixed the run-176/177 layout-inversion regression —
  the directional sequences live, the pinned no-op edges, the modal guarantee re-verified; the
  M-M7 three-state sites GREEN; see the What ran GREEN section). The left-click path is
  unit-pinned + manual.
- ~~**E2E-RECENT-1** - Gap 4 (fifth)~~ — **DISCHARGED GREEN 2026-10-05** (the item's final
  gate: the full 44-scenario fresh-boot suite, run 185; `telescope-recent` verified
  end-to-end — the probe literal once, `recent files gathered count=16`, the Step-2 Order.cs
  top-match preview proof, the Step-3 tightened `results count=1 selected=0`, the Step-4
  open; see the What ran GREEN section). The `telescope-goto` 3rd-strike regression was also
  fixed in-item (BP-B7 — GREEN outright, the re-walk never fired).
