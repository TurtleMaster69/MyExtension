# Task Ledger — session neovim-planning-hub-20261004-143017

| ID | Objective | Assignee | Status | Artifact | Verification |
|----|-----------|----------|--------|----------|--------------|
| R1 | The VS git command names + the LazyVim git keys | feature-researcher | done | digest | Team.Git.* verified; the Git.* names unverified (the e2e loop arbitrates) |
| R2 | The focus conventions + the recent-files/symbols APIs | feature-researcher | done | digest | telescope is prompt-centric; DTE.RecentFiles MEDIUM (falsified by the Gap 4 recon) |
| R3 | The focus model + the g,* collisions + the finder skeleton | trailmark-recon | done | digest | the focus model file:line; ZERO g,* collisions; the finder skeleton |
| Q1 | The Feature 7 design + the Gap 11 keys | user | done | (question) | logical→REAL focus (the architecture directive); the lazygit deferred; g,d/g,b/g,h |
| P1 | plan-gap11.md (Stage 1) | hub (me) | done | plans/plan-gap11.md | written |
| P2 | plan-feature7.md (Stage 1) | hub (me) | done | plans/plan-feature7.md | written (revised to the pane architecture per the user) |
| P3 | plan-gap4.md (Stage 1) | hub (me) | done | plans/plan-gap4.md | written |
| P4 | plan-columns-ux.md (Stage 1) | hub (me) | done | plans/plan-columns-ux.md | written (the user's 5 requirements) |
| P5 | plan-preview-buffer.md (Stage 1) | hub (me) | done | plans/plan-preview-buffer.md | written (the research verdict) |
| B1 | Gap 11 Build Plan | implementation-planner | done | artifacts/gap11-section.md | 11 BP steps |
| B2 | Feature 7 Build Plan (A+B) | implementation-planner ×2 | done | artifacts/feature7-section-{a,b}.md | 7+12 BP steps |
| B3 | Gap 4 Build Plan (A+B) | implementation-planner ×2 | done | artifacts/gap4-section-{a,b}.md | 8+6 BP steps; the RecentFiles API falsified → the probe+MRU design |
| B4 | Columns-UX Build Plan | implementation-planner | done | artifacts/columns-ux-section.md | 11 BP steps |
| B5 | Preview-buffer Build Plan | implementation-planner | done | artifacts/preview-buffer-section.md | 10 BP steps |
| G1 | Gap 11 gate | docs-reviewer | done | APPROVE | minors noted |
| G2 | Feature 7 gate | docs-reviewer | done | APPROVE | minors noted |
| G3 | Gap 4 gate | docs-reviewer | done | APPROVE | REVISE (the false-GREEN) → APPROVE; the AC5 patch applied |
| G4 | Columns-UX gate | docs-reviewer | done | APPROVE | REVISE (the test pins) → APPROVE |
| G5 | Preview-buffer gate | docs-reviewer | done | APPROVE | the flake-count minor applied |
| H1 | Handoff (all five plans) | hub (me) | done | USER APPROVED | neovim_hub's state verified FIRST (the columns + goto plans GREEN; the loop's Done entry OWNERS the preview fix to the planning hub); the columns-ux plan → docs/implementation_plan.md (hash match); progress.md: the "Next up" bullet + the FIRST ITEM + the THEN-2..5 blockquotes; the e2e queues: E2E-GIT-1/PANES-1/RECENT-1/CUX-1/PBUF-1 appended to both; doc-refs PASS 0 unresolved (the not-yet-existing IPane/PaneHost de-backticked); doc-content PASS 12/12 |
