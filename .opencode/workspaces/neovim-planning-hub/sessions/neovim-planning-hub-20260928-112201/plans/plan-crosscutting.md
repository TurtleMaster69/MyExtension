
---

# Cross-cutting sections

## Acceptance criteria (whole program)

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | Every extracted pure seam ships with NEW unit tests that are RED before the merge and GREEN after | — | the new seam tests (Lane 1: C3/L5/L7/L6; Lane 2: T1/T2/T5; Lane 3: L3/L2/L1; Lane 4: N2/N4/N3; Lane 5: X1) |
| A2 | Both offline unit suites stay green after every lane | — | `dotnet run --project tests/Telescope.Tests` (56, growing to 61 after X1) + `tests/NeoVisual.Tests` (38, growing with the new seam tests) |
| A3 | NO `[Telescope]`/`[NeoVisual]`/`[Hook]` **structured-log** literal changes anywhere | `git diff` shows no change to any structured-log literal | code review at VERIFY (M-M7 NOT triggered) |
| A4 | Every merge that changes a public/internal signature updates the affected unit tests in the same commit | — | per-merge unit test updates (e.g. `Run_RectCoordinate_StoresFields`, `Run_FileFinder_*` payload, `Run_LogFileWriter_WritesAndClearsFile`) |
| A5 | The dead code / duplication targets are actually removed (no zombie seams) | — | `git diff` shows the deleted members (IVsFrameView forwards, RemoveWindowsNotAdjacent, ActivateWindow, IQueryFinder, dead constants, etc.) |
| A6 | `pwsh tools/check-doc-refs.ps1` passes after any doc touch | — | doc-ref lint (mandatory in the doc-ref steps: Lane 1 BP-3/BP-8, Lane 2 BP-1, Lane 4 BP-2/BP-3/BP-4, Lane 5 BP-4) |
| A7 | The deferred e2e gates (per lane) are queued in `e2e-queue.md` and NOT executed on this machine | — | e2e-queue.md entries (QUEUED) |

## Unit test plan (RED proof, whole program)

All new seam tests follow the `OverlayKeyHandler`/`TextMotionNavigator` pattern:
dependency-free classes the UI delegates to. **RED proof** = the test references
an API that does not exist yet (compile error) or asserts behavior the current
code does not have (assertion failure); GREEN after the merge. Each test is added
in the SAME commit as its merge (A4). Full per-lane test tables live in each lane
section; the summary:

- **tests/NeoVisual.Tests** (add ~30, update ~8):
  - Lane 1: `Run_ActionsRegistry_*` (C3).
  - Lane 2: `Run_TextMotionEngine_MapMotion_*` (T1) + update `Run_TextInput_MapMotions`/`Run_TextInput_MapInsertMotions`; `Run_ActionTable_*` (T2); `Run_FocusGuard_TruthTable_*` (T5) + update the 5 existing FocusGuard tests.
  - Lane 4: `Run_RectCoordinate_*` (N2, incl. UPDATE `Run_RectCoordinate_StoresFields`); `Run_NavigationSettings_FromDpi` (N4); `Run_WindowNavigationEngine_*` (N3 — the core navigation algorithm becomes unit-testable).
- **tests/Telescope.Tests** (add ~20, update ~4):
  - Lane 1: `Run_TelescopeLog_*` (L5); `Run_CaretPlacement_*` (L7); `Run_GetCandidates_DefaultQuery_*` (L6).
  - Lane 3: `Run_FileLocation_*` + `Run_IFileLocation_*` (L3); `Run_FinderBase_*` (L2) + update `Run_FileFinder_*` payload.
  - Lane 5: `Run_LogFileWriter_Buffered_*` (X1) + update `Run_LogFileWriter_WritesAndClearsFile`.

## Diagnostics (log contract)

The verification contract (§8 of the doc) is absolute: **no change to any
`[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal.** Every merge is
behavior-preserving; the diagnostics the e2e harness asserts on are unchanged.
Two **documented deviations** (adjudicated, not regressions) are carried in the
lane Verification Traces and must NOT be reported as regressions:
1. **Lane 1 BP-3 (C2):** the 5 distinct `[NeoVisual]Failed to open Telescope ...`
   Debug.WriteLine error-path literals collapse into one parameterized form —
   failure-path only, never asserted by the harness, not in the structured exp log.
2. **Lane 5 BP-1 (X1):** the hook's install/uninstall lines change prefix
   `[GlobalKeyboard]` → `[Hook]` (the "one `[Hook]` prefix" target);
   `DiagnosticLog.GlobalKeyboard` is KEPT (pinned by `Run_LogPrefixes_Pinned`) but
   no longer emitted by the hook. The harness never asserts on `[GlobalKeyboard]`.

X1 (LogPath) is the only lane that touches the log *write path* — it must change
the write mechanism, never the emitted text.

## Known-RED allowlist

- **None.** `docs/progress.md` reports no known-RED e2e scenario remains (all 35
  green as of 2026-09-27). e2e is deferred to the queue anyway; the unit-only
  gate is the two offline suites + the new seam tests.

## E2E queue reference (deferred — see `e2e-queue.md`)

No NEW e2e scenarios are needed — every merge is behavior-preserving, so the e2e
gate is the **existing scenarios staying green**. Five queue entries (one per
lane), each listing the affected existing scenarios to re-run on a capable
machine after the plan is GREEN:

- **Lane 1:** hook install + navigation + telescope-open (C1); telescope-references/
  implementation + telescope-issues/grep (C6+L4); all telescope-* (C2);
  neovisual-leader (C3); telescope-references/implementation (C4); e2e log
  contract (L5); telescope-prompt-motions (L7); telescope-grep (L6); unit suites (X3).
- **Lane 2:** neovisual-textinput-motions; explorer-open-searchbox; all
  neovisual-explorer-*.
- **Lane 3:** all telescope-* finder scenarios (open-file/issues/grep/references/
  implementation/preview); telescope-references/implementation (C5);
  neovisual-toolwindow/explorer-* (C7).
- **Lane 4:** neovisual-window-nav; neovisual-toolwindow.
- **Lane 5:** full 35-scenario suite (X1 touches the log contract); e2e suite (X2);
  neovisual-toolwindow/textinput (C8); build + unit suites (X4).

Each asserts: the listed scenarios stay GREEN + `git diff` shows no log-literal
drift. Diagnostics depended on: the existing `[Telescope]`/`[NeoVisual]`/`[Hook]`
lines (unchanged).

## Queue reconciliation (Step 2 — applied at handoff, on approval)

The consolidation plan **subsumes** a large subset of the filed backlogs. At
handoff the new plan becomes the FIRST pending item, and the following filed
findings are marked **covered-by the consolidation plan** (not double-executed):

- **Architecture review backlog:** F2 (X1/C8), F3 (T1/L7), F4 (T1), F6 (N3),
  F7 (N1), F11 (L2), F13 (L2), F36 (X3), F37 (X2), F38 (X2), F39 (X2), F40 (X2),
  F41 (X2), F44 (X2), F46 (X4).
- **Code review backlog:** M4 (C2), M5 (C5/C6), M8 (N3), M9 (T1), and the minors
  m6 (C1), m8 (C7), m16 (N4), m17 (N1/N2), m18 (N1), m19 (N1), m20 (N1),
  m21 (N3), m22 (N3), m23 (N1), m24 (N1), m25 (N2), m33 (T1), m34 (T1/L7),
  m36 (T2), m37 (T5), m38 (T1), m43 (T1/L7), m44 (L4), m45 (X1), m59 (X2).

**NOT subsumed** (stay in the backlog for later items): F5, F8, F9, F12, F14,
F15, F22, F43, M19, m7, m9, m10, m11, m12, m13, m32, m35, m46, m47, m48, m58,
m60, m62, m63, m64, m65, n1-n16.

## Verification Trace (whole program)

| failing test / scenario | implicated lane | expected diagnostic / pass signal |
|---|---|---|
| the new seam tests (A1) — RED before each merge | the lane owning the merge | GREEN after the merge; the seam's diagnostic unchanged |
| `tests/Telescope.Tests` (56→61) | all lanes | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (38→~68) | all lanes | all existing `Run_*` pass |
| `pwsh tools/check-doc-refs.ps1` | the doc-ref steps (L1 BP-3/BP-8, L2 BP-1, L4 BP-2/BP-3/BP-4, L5 BP-4) | exit 0 |
| A3 log-literal check (`git diff`) | all lanes | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |
| e2e gates (deferred, queued in `e2e-queue.md`) | all lanes | the affected existing scenarios stay green on a capable machine |

**Known-RED allowlist (do NOT report as regressions):** none — `docs/progress.md`
reports no known-RED e2e scenario remains (all 35 green as of 2026-09-27); e2e is
deferred to `e2e-queue.md` anyway.

---

## Execution Log

(empty — populated by `neovim_hub` per attempt; each lane's attempt is recorded
here with `delegations: | VS boots: | iterations:` cost lines.)
