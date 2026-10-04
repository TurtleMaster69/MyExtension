# SESSION STATE — neovim-planning-hub-20261004-112850

Status: ACTIVE · Objective: plan **Gap 3 — diagnostics navigation (`]`/`[` prefix)** for
neovim_hub, two-stage fan-out tactic (Stage 1 general plan → Stage 2 parallel section
planners → Stage 3 hub aggregate). Unit-only lane; e2e deferred.

## Scope (user answer 2026-10-04, verbatim intent)

"check if there is a way we can have ]w [w (for warnings) and [e ]e (for errors) if not try
to find a way to create that functionality" → native VS has NO severity-specific commands
(researcher-verified; the backlog's `Edit.NextError`/`Edit.PreviousError` assumption is NOT
verifiable in modern VS) → the severity pairs are BUILT (custom navigator). `],d`/`[,d` stay
native (`Edit.GotoNextIssueinFile`/`Edit.GotoPreviousIssueinFile`, verified HIGH).

## Status line

Stage 1 (general plan) DONE → `plans/plan.md`. Stage 2 (parallel section planners A–E) IN
FLIGHT. Stage 3 (aggregate) pending. Gate pending. Handoff pending.

## Key research facts

- Verified native: `Edit.GotoNextIssueinFile` (Alt+PgDn) / `Edit.GotoPreviousIssueinFile`
  (Alt+PgUp) — in-file squiggle nav, closest LazyVim `]d`/`[d` analog; `Edit.GoToNextLocation`
  (F8) / `Edit.GoToPrevLocation` (Shift+F8) — Error List solution-wide. NO severity variants.
- `]`/`[` have NO KeyNames case (build enum names → abort); ZERO collisions; pre-filter passes
  any key while leader active; two-arg overload delegates non-letters → single-arg cases only.
- Post-Gap-1 tree verified: KeyNames has OemPipe + two-arg overload; JSON has 30 bindings incl.
  `w,-`/`w,|`/`w,d`; CloseWindowCommand.cs exists (LSP stale-diagnostics false alarm — logged).
- Custom nav design: pure `DiagnosticNavigator` seam (in-file, severity-filtered, NO wrap,
  null at end) + thin Error-List gatherer (the CodeIssuesFinder DTE API pattern) +
  `InputHandler.NavigateDiagnostic(forward, severityError)` + 4 registry entries (12→16) +
  NEW `[NeoVisual] diagnostic-nav ...` literal family (M-M7).

## Files

- Plan: `sessions/neovim-planning-hub-20261004-112850/plans/plan.md`
- Section outputs: `artifacts/section-{a,b,c,d,e}.md`
- Handoff targets (Step 7, on approval): `docs/implementation_plan.md`, `docs/progress.md`,
  workspace `e2e-queue.md` + canonical `docs/e2e-queue.md` (E2E-GAP3-1..2).
