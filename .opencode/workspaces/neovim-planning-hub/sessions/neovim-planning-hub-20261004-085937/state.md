# SESSION STATE — neovim-planning-hub-20261004-085937

Status: ACTIVE · Objective: plan **Gap 1 — window-management leader bindings (`w` prefix) +
case-sensitive leader combos** for neovim_hub, using the user's two-stage fan-out tactic
(Stage 1 general plan → Stage 2 parallel section planners → Stage 3 hub aggregate).

## Scope (user instructions 2026-10-04, verbatim decisions)

1. Skip zoom. 2. Skip resize. 3. Remove `Space+W` save (user saves with Ctrl+S); use `W` as
the window prefix. 4. Capital letters in leader combos differentiate `s+g` from `s+G`
(case-based: lowercase=unshifted, uppercase=Shift+letter; no `Shift+` prefix in the notation).
5. Delete-window = **focus-aware new action** (user's `question` answer).

## Status line

Stage 1 (general plan) DONE → `plans/plan.md`. Stage 2 (parallel section planners A–E) IN
FLIGHT. Stage 3 (aggregate) pending. Gate (docs-reviewer) pending. Handoff pending.

## Decisions

- Lane: feature (unit-only, e2e deferred). No new diagnostic literal (reuses
  `leader-binding executed:`).
- VS commands (researcher-verified, HIGH): split-below `Window.NewHorizontalTabGroup`,
  split-right `Window.NewVerticalTabGroup`, close-doc `Window.CloseDocumentWindow`,
  close-tool `Window.CloseToolWindow`.
- Case-based leader matching: `KeyNames.ToString(Keys, bool shift)` new overload; single-arg
  stays for `KeyNameBuilder` (simple shortcuts keep `Ctrl+H`); leader dict + prefix set +
  `KeybindingConfig` map → `StringComparer.Ordinal`; all existing leader bindings lowercase.
- `w,d` → new built-in `close-window` action + pure `CloseWindowCommand.For(bool)` seam.
- 3 harness scenarios updated in place (not run): `neovisual-leader`,
  `neovisual-explorer-move-editor-focus`, `neovisual-editor-insert` (save → Ctrl+S).

## Files

- Plan: `sessions/neovim-planning-hub-20261004-085937/plans/plan.md`
- Section outputs (Stage 2): `artifacts/section-{a,b,c,d,e}.md`
- Handoff targets (Step 7, on approval): `docs/implementation_plan.md`, `docs/progress.md`,
  `e2e-queue.md` (E2E-GAP1-1..5).
