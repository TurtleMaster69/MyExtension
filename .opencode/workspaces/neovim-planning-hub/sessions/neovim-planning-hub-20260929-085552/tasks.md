# Task Ledger — session neovim-planning-hub-20260929-085552 (code-review findings plan)

> The plan + assigned subtasks + known facts. The hub is the ONLY writer.
> Worker status vocabulary: `active / idling / done / crashed`.

## Research passes (user-requested: 5 passes × ≥10 subagents)

| ID | Objective | Assignee | Status | Artifact | Verification |
|----|-----------|----------|--------|----------|--------------|
| R0 | Whole-repo structural digest (nodes/proxies/entrypoints, hotspot + blast-radius grounding) shared with all workers | trailmark-recon | done | recon digest (in-context) | 1298 nodes/552 proxies/0 entrypoints; 2 stale citations (M34, M14); traps confirmed |
| P1-1..P1-10 | Pass 1 correctness: CR1, CR2, CR3, M9+M10+M11, M12, M13, M14, M15, M16, M17 | arch-auditor ×10 | done | artifacts/p1-*.md | 10/10 verdicts (P1-9 re-dispatched per user) |
| P2-1..P2-10 | Pass 2 hook hot-path + perf: M1, M2, M3, M4, M5, M6, M7, M8, m1+m2+m3+m7, m15+m16+m17+m18 | arch-auditor ×10 | done | artifacts/p2-*.md | 10/10 verdicts |
| P3-1..P3-10 | Pass 3 duplication: M21..M30 | arch-auditor ×10 | done | artifacts/p3-*.md | 10/10 verdicts |
| P4-1..P4-10 | Pass 4 testability + logging: M31, M32, M33, M34, M18, M35, M43, m13+m20, m23+m24+m25, m12+m14 | arch-auditor ×10 | done | artifacts/p4-*.md | 10/10 verdicts |
| P5-1..P5-10 | Pass 5 harness + tests + naming: M19, M20, M36, M37, M38, M39, M40, M41, M42+m30, m26+m27+m28+m29+n1-n12 | arch-auditor ×10 | done | artifacts/p5-*.md | 10/10 verdicts |

## Plan pipeline

| ID | Objective | Assignee | Status | Artifact | Verification |
|----|-----------|----------|--------|----------|--------------|
| S1 | Write the initial unit-only plan (all phases) at plans/plan.md | hub (me) | done | plans/plan.md | written (12 phases, 75 findings) |
| S2-0 | BP-n Build Plan for Phase 0 (CR1-CR3) → artifacts/bp-phase-0.md | implementation-planner | done | artifacts/bp-phase-0.md | 7 BP steps |
| S2-1 | BP-n Build Plan for Phase 1 (M1,M3,M4,m1) → artifacts/bp-phase-1.md | implementation-planner | done | artifacts/bp-phase-1.md | 5 BP steps |
| S2-2 | BP-n Build Plan for Phase 2 (M5-M8) → artifacts/bp-phase-2.md | implementation-planner | done | artifacts/bp-phase-2.md | 8 BP steps |
| S2-3 | BP-n Build Plan for Phase 3 (M9-M11) → artifacts/bp-phase-3.md | implementation-planner | done | artifacts/bp-phase-3.md | 4 BP steps |
| S2-4 | BP-n Build Plan for Phase 4 (M2,M12) → artifacts/bp-phase-4.md | implementation-planner | done | artifacts/bp-phase-4.md | 5 BP steps |
| S2-5 | BP-n Build Plan for Phase 5 (M13,M14) → artifacts/bp-phase-5.md | implementation-planner | done | artifacts/bp-phase-5.md | 6 BP steps |
| S2-6 | BP-n Build Plan for Phase 6 (M15-M17) → artifacts/bp-phase-6.md | implementation-planner | done | artifacts/bp-phase-6.md | 7 BP steps |
| S2-7 | BP-n Build Plan for Phase 7 (M18,M27,M35,M43,m13,m20,m23,m24,m25) → artifacts/bp-phase-7.md | implementation-planner | done | artifacts/bp-phase-7.md | 9 BP steps |
| S2-8 | BP-n Build Plan for Phase 8 (M21-M30) → artifacts/bp-phase-8.md | implementation-planner | done | artifacts/bp-phase-8.md | 9 BP steps |
| S2-9 | BP-n Build Plan for Phase 9 (M31-M34) → artifacts/bp-phase-9.md | implementation-planner | done | artifacts/bp-phase-9.md | 13 BP steps |
| S2-10 | BP-n Build Plan for Phase 10 (M19,M36,M37,m26-m29) → artifacts/bp-phase-10.md | implementation-planner | done | artifacts/bp-phase-10.md | 16 BP steps |
| S2-11 | BP-n Build Plan for Phase 11 (M20,M42,m30) → artifacts/bp-phase-11.md | implementation-planner | done | artifacts/bp-phase-11.md | 5 BP steps |
| S2-12 | BP-n Build Plan for Phase 12 (M38-M41,n1-n12) → artifacts/bp-phase-12.md | implementation-planner | done | artifacts/bp-phase-12.md | 17 BP steps (re-dispatch #2) |
| S2d | Combine BP-n into plans/plan.md | hub (me) | done | plans/plan.md | 13 sections + 13 Verification Traces appended (4246 lines) |
| S3 | Plan review gate (initial-plan + build-plan) at plans/plan.md | docs-reviewer | done | plans/plan.md | APPROVE (2 minor count fixes applied: M29 -4→-2, WindowNavigationEngine 11→10) |
| S4 | Handoff (user-approved): write docs/implementation_plan.md + progress.md FIRST item + e2e-queue.md QUEUED rows | hub (me) | done | docs/implementation_plan.md, docs/progress.md, e2e-queue.md | all 3 writes verified |
| S2 | Append BP-n Build Plan + Verification Trace to plans/plan.md | implementation-planner | pending | plans/plan.md | BP-n present |
| S3 | Plan gate (initial-plan + build-plan) at plans/plan.md | docs-reviewer | pending | APPROVE/REVISE | gate policy |
| S4 | Hand off: docs/implementation_plan.md + progress.md first item + e2e-queue.md | hub (me) | pending | docs/implementation_plan.md | user approval |
