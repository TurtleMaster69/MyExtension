# Session log — neovim-planning-hub-20261002-100206

## 2026-10-02 10:02 — Session start
- REQUEST: user asked to read `docs/reviews/code-review.md` and create a plan that
  includes fixes for ALL problems found in the code review — even minors and nits.
- Re-orientation: read AGENTS.md (auto), vs-extension-dev SKILL.md, docs/progress.md
  (queue + resume), docs/spec.md, docs/implementation_plan.md (finished 98-findings
  plan — GREEN, safe to overwrite at handoff), e2e-queue.md, session state.
- Cataloged the 51 findings (0 critical, 5 major, 22 minor, 24 nit) from
  docs/reviews/code-review.md (2026-10-02).
- Queue reconciliation notes (Step 2): the code review says "None filed yet — pending
  the user's selection". Cross-references to the Architecture backlog: F5→R3, F7→R4,
  F8/F9→R22/R42, F10→R2, F14→R45. Prior-regression re-reports: M1→R1, M4→R2, M7→R24.
- Session workspace created: sessions/neovim-planning-hub-20261002-100206/.

## 2026-10-02 10:05 — Research phase (trailmark-recon + arch-auditor)
- trailmark-recon: DONE. 33/33 claim-groups CONFIRMED (graph 1861 nodes, 760 proxies, 0 entrypoints). Current paths all correct post-restructure. R6 dead production code (proxy → 9 test callers); R14 genuinely dead (0 plain + proxy). Graph gaps: PreviewRenderer.Show + FzfFilter.IsAvailable instance-field calls not resolved even via proxy — verify by file:line. No command failures.
- arch-auditor: DONE. 51/51 fix directions verified. 2 NEEDS-CORRECTION:
  - R1: restricting the shared PromptMotionRouter.ShouldConsume to h/l/w/b/e/0/$ breaks the PREVIEW surface (HandlePreviewKey needs j/k/g/G) — restriction must be prompt-only (surface param) or applied in TryPromptMotion only.
  - R3: UnsubscribeBuffer only receives textBuffer but the Closed sub is on the IVimBuffer — must thread the buffer; OnBufferClosed refcount decrement must not double-decrement with Detach (m40).
  - Cross-cutting: R1↔R44 (both TryPromptMotion), R3↔m38-m41 (refcount/subscription machinery), R4↔m46 (frame path), R6↔R10/R11 (FocusGuard), R17↔R18 (tool-window hot path), R22↔R42 + R23↔R24 (FzfFilter clusters), R2↔R41 (PreviewRenderer), R43↔R44 (TextMotionNavigator/LineIndex), R25/R26/R51 + R27/R51 (docs).
  - R45 = accepted n16 (documented/accepted design) — no code fix; keep documented.
- NEXT: Step 4 — write the initial unit-only plan at plans/plan.md.

## 2026-10-02 10:10 — Step 4 (initial plan written)
- WROTE plans/plan.md: 10 phases (0-9), all 51 findings with fix direction + unit seam,
  acceptance-criteria table, unit-test plan, diagnostics, known-RED allowlist (none),
  E2E queue reference (E2E-CR51-1..10), execution-order note.
- R1 fix is surface-aware (prompt-only restriction; preview keeps j/k/g/G); R3 threads
  the IVimBuffer + coordinates the refcount with Detach (m40); R45 recorded as
  documented/accepted n16 (no code change).
- NEXT: Step 5 — delegate the Build Plan to implementation-planner.

## 2026-10-02 10:15 — Step 5 (Build Plan appended)
- implementation-planner: DONE. Appended Part B (Build Plan, BP-1..BP-51, one per finding
  R1-R51) + Part C (Verification Trace, 52 rows) to plans/plan.md.
- Planner caught that Part A omitted R12/R13/R19 from the phase lists and placed them:
  R12→Phase 2 (hook hot path with R11), R13→Phase 1 (harness-only init step), R19→Phase 5
  (with R9, both HierarchyResolver). I reconciled Part A (phase headers + bullets +
  acceptance-criteria table + unit-test plan) to include them.
- Planner's key decisions: R1 surface-aware (PromptMotionRouter.ShouldConsume gains a
  surface param; I wired to EnterInsertMode(Start) via new OverlayKey.ShiftI); R3 threads
  the IVimBuffer via _subscriptions.BufferFor + coordinates refcount with Detach (m40);
  R45 disposition step (no code); BP-8 (R6) propagates the HasToolWindowActionKeys doc
  refs (spec.md:76,158 + progress.md:614,616) with the doc-ref lint as the gate.
- Verified the `preview file=... chars=...` token is real (PreviewRenderer.cs:58).
- NEXT: Step 6 — delegate the plan review to docs-reviewer.

## 2026-10-02 10:20 — Step 6 (plan review)
- docs-reviewer round 1: REVISE — 1 major (BP-1 `a` placement parenthetical wrong: bare `a` → Current, not End; `OverlayKey.ShiftI` redundant), 1 minor (R19 `Run_HierarchyWalker_*` attributed to the wrong test project — it's Telescope.Tests), 2 nits (BP-26 static-cache risk; BP-1 vs BP-39 shared TryPromptMotion).
- I verified the major finding myself (PromptMotionRouter.cs:24-26 + Run_PromptMotionRouter_InsertPlacementsNotConsumed Program.cs:1264-1282 confirm bare `a` → Current). Fixed the Part A R19 attribution myself (initial-plan section).
- Routed the Build Plan findings (BP-1, BP-26, BP-39) back to implementation-planner (round 1 revision): BP-1 placement contract corrected + ShiftI dropped; BP-26 instance-scoped cache; BP-39 explicit shared-method re-verify.
- docs-reviewer round 2: APPROVE (all 4 findings resolved). Gate closed.
- NEXT: Step 7 — present the plan + queue changes to the user via question; on approval write docs/implementation_plan.md + progress.md first item + e2e-queue.md entries.

## 2026-10-02 10:30 — Step 7 (handoff complete)
- USER APPROVED the handoff + the covered-by queue reconciliation (via question).
- WROTE docs/implementation_plan.md (864 lines) from sessions/.../plans/plan.md — SHA256 byte-identical to the approved session plan.
- APPENDED e2e-queue.md: 10 entries E2E-CR51-1..10 (status QUEUED, one per finding cluster, per-cluster scenarios + diagnostics).
- UPDATED docs/progress.md: 51-findings plan as the FIRST pending item with 'DEFER e2e tests (unit-only lane)' instructions; covered-by notes added to the Architecture review backlog (F5→R3, F7→R4, F8/F9→R22/R42, F10→R2, F14→R45); header status + 'Next up' + 'Next candidates' updated (remaining after it: F13, F43).
- VERIFIED: doc-ref lint PASS (28 docs, 6499 refs, 0 unresolved) — no NEW drift from the handoff.
- NEXT STEP for the user: tell neovim_hub to execute the FIRST pending item (Code review fixes, 51 findings) in the unit-only lane (e2e deferred to e2e-queue.md E2E-CR51-1..10).
