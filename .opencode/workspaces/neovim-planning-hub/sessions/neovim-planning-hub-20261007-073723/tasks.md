# Task Ledger — neovim-planning-hub-20261007-073723

> Worker status vocabulary: `active / idling / done / crashed`. "Done" = an explicit
> terminal artifact (file path + digest), not silence.

| ID | Objective | Assignee | Status | Artifact | Verification |
|----|-----------|----------|--------|----------|--------------|
| R1 | Web research: Telescope.nvim picker architecture + native VS reuse + VS picker patterns (for the generalization question + the code-review fixes) | feature-researcher | done | digest (in-context) | Telescope.nvim decomposition + LazyVim migrated to snacks + native VS not extensible + build-light recommendation |
| R2 | Structural recon: change-area blast radius for the 107 findings + the Telescope library structure | trailmark-recon | done | digest (in-context) | M1-M6 confirmed (M3 whitespace at :175-178); blast-radius map; 8 finders / 6 hit classes / 3 panes |
| R3 | Change-area analysis: the code-review fix clusters (query-driven finders, navigation isolation, Vim interop, tool-windows, overlay, test infra, harness, docs) | arch-auditor | done | digest (in-context) | 8 clusters confirmed; sequencing hazards (M3 before M4; ~15 GrepFinder test rewrites; pinned-test updates) |
| R4 | Telescope generalization/simplification deep-dive (the user's 2nd request) | arch-auditor | done | digest (in-context) | DO IT: QueryDrivenFinderBase (biggest win), dead code, shared column builders; DEFER: SymbolFinderBase fold, ListFinderBase; DON'T: untyped Hit, full Telescope.nvim split |
| P1 | Write the initial unit-only plan at plans/plan.md | hub (me) | done | plans/plan.md | written (9 phases, 107 findings + generalization section) |
| P2a | Append the BP-n Build Plan for Section A (Phases 0-1: query-driven finder core + navigation isolation) to artifacts/section-a.md | implementation-planner | done | artifacts/section-a.md | 17 BP steps, 15/15 findings |
| P2b | Append the BP-n Build Plan for Section B (Phases 2-4: Vim interop + hook/input/package + tool-windows) to artifacts/section-b.md | implementation-planner | done | artifacts/section-b.md | 25 BP steps, 25/25 findings |
| P2c | Append the BP-n Build Plan for Section C (Phase 5: overlay + columns + finders) to artifacts/section-c.md | implementation-planner | done | artifacts/section-c.md | 32 BP steps, 32/32 findings (retry after step-cap) |
| P2d | Append the BP-n Build Plan for Section D (Phases 6-8: test infra + harness + docs) to artifacts/section-d.md | implementation-planner | done | artifacts/section-d.md | 35 BP steps, 35/35 findings (2 retries after step-caps) |
| P2e | Aggregate the 4 section Build Plans into plans/plan.md (Build Plan + Verification Trace) | hub (me) | done | plans/plan.md | 109 BP steps, structure verified |
| P3 | Approve the plan (initial-plan + build-plan gates) | docs-reviewer | done | APPROVE (after 1 REVISE round) | gate policy satisfied; 5 REVISE findings fixed + 2 nits fixed |
| P4 | Hand off: docs/implementation_plan.md + progress.md first item + e2e-queue.md entries | hub (me) | done | docs/implementation_plan.md (203KB, 110 BP steps) | user approved; doc-ref lint has only the 4 pre-existing review-file refs (BP-D36 fixes them) |
| P2 | Append the BP-n Build Plan + Verification Trace to plans/plan.md | implementation-planner | pending | plans/plan.md | BP-n present |
| P3 | Approve the plan (initial-plan + build-plan gates) | docs-reviewer | pending | APPROVE/REVISE verdict | gate policy |
| P4 | Hand off: docs/implementation_plan.md + progress.md first item + e2e-queue.md entries | hub (me) | pending | docs/implementation_plan.md | user approval |
