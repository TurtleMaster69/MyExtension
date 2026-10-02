# Progress Ledger — neovim-planning-hub

> Append-only raw event log (cheap, lossless) + periodic rebase into dense
> bullets. The hub is the ONLY writer of this ledger.

## 2026-09-28 — Build (hub-creator)

- Hub created: `.opencode/agent/neovim-planning-hub.md` (mode: primary, steps: 200).
- Subagent created: `.opencode/agent/feature-researcher.md` (hidden: true, steps: 60).
- Workspace initialized: `state.md` / `tasks.md` / `log.md` / `skills.md` /
  `e2e-queue.md`.
- Interview (2026-09-28): scope = plan-only, hand off to neovim_hub as FIRST
  queue item with e2e-deferral instructions; routing = trailmark-recon,
  arch-auditor, docs-reviewer, implementation-planner (+ feature-researcher new,
  + e2e-test-builder/verification-agent for queue drain); research = new
  web-enabled feature-researcher subagent; workspace =
  `.opencode/workspaces/neovim-planning-hub/`; mode = primary; placement =
  project `.opencode/agent/`; e2e queue = workspace `e2e-queue.md` drained on a
  capable machine; handoff = `docs/implementation_plan.md` + `docs/progress.md`
  first item (on approval).
- Skill research (2026-09-28): skill-researcher (18 web/tool calls) → **no new
  installs**; all needs covered by vendored skills; `track-management` +
  `context-driven-development` (wshobson/agents Conductor) DROPPED as
  conflicting with the repo's planning contract. User approved "no new installs".
- Verification (2026-09-28): skill-verifier (SEE/ACCESS/WILL-USE) + hub-reviewer
  (adversarial) — see `skills.md` and the hand-back report.

## 2026-09-28 — Session neovim-planning-hub-20260928-112201 (architecture consolidation plan)
- REQUEST: user asked to make a plan from docs/architecture-consolidation.md (5-lane, ~30-merge consolidation program).
- Re-orientation: read AGENTS.md (auto), vs-extension-dev SKILL.md, docs/progress.md (queue + resume), docs/spec.md, docs/implementation_plan.md (finished item — safe to overwrite at handoff), e2e-queue.md, session state.
- Existing implementation_plan.md = completed neovisual-editor-insert item (GREEN) — not mid-feature; overwrite OK at Step 7.
- Scope decision pending (question tool): one Lane-1 plan vs whole-program plan vs per-lane plans.

## 2026-09-28 — Research phase (architecture consolidation)
- SCOPE (user): plan EVERYTHING (all 5 lanes), defer e2e to the queue; keybindings UX items deferred to feature-triage.
- feature-researcher: SKIPPED (pure refactor, no new user-facing behavior; keybindings UX deferred to its own triage research). Documented N/A.
- arch-auditor: SKIPPED whole-repo re-audit (docs/architecture-consolidation.md IS the audit, dated today). Focused arch-auditor on standby if the plan gate flags a design risk.
- trailmark-recon: DONE. 25 claims verified (graph 1104 nodes, 494 proxies, 0 entrypoints). CONFIRMED: all dead-code claims (IVsFrameView 13 forwards, RemoveWindowsNotAdjacent, ActivateWindow, m_IVsFrames, DistinctBy prod-dead, 4 dead constants, GTC IsTextInputType branch, GetWindowControlAdapters dteWindows, _package), all duplication claims (6 open-finder methods, 5 SComponentModel, 13 DllImports, 5 finder skeletons, 4 hit models, byte-identical GotoLine/OpenReference/OpenImplementation, TextMotionHelper inverted dep, triplicated _isInputMode, 5 preview branches, Roslyn prologue, ResolvePipeline, IQueryFinder stub, magic ints).
- RECON CORRECTIONS (fold into the plan): (1) N3: NO switch statements in WindowMatrix — 5 direction-parameterized filter methods use if/else, not '5x 4-way switches'; (2) C2: 6 open-finder methods not 7; (3) C3: ResolveAction has 10 cases not ~14; (4) C4: GetDTE ~19 sites not 12; (5) L5: prefix ALREADY centralized in DiagnosticLog.Telescope (63 call sites, not 31 hand-written literals) — merge is about the DiagnosticLog.Telescope+msg boilerplate; (6) X1: ~60 Debug.WriteLine sites not 27.
- FALSE-DEAD-CODE TRAPS (do not report as dead): WindowMatrix.CheckDte (called from ctor :55), GeneralToolWindowController ctor (new-ed in WindowManager.GetController), TextMotionHelper.TryMoveFocusedTextBox (proxy->SolutionExplorerController), DistinctBy (proxy->tests only).

## 2026-09-28 — Revision 1 + 2 (architecture consolidation plan)
- REVISION 1 (per-lane precision): 5 implementation-planner agents, one per lane, each wrote a complete source-verified lane section (merges + BP steps + unit tests + e2e gate) to its OWN file (rev1-lane1..5.md). Combined into plans/plan.md (1166 lines).
- REVISION 2 (cross-cutting consistency): 3 agents (arch-auditor x2 + implementation-planner) each wrote findings to its OWN file (rev2-a/b/c.md).
  - rev2-a: 4 CRITICAL cross-lane hazards (C1: Lane5 X1 edits WindowControlAdapter.cs deleted by Lane4 N1; C2: Lane5 X1 lists InputHandler sites deleted by Lane1 C2 + misses TelescopeLauncher.cs; C3: Lane1 BP-5 deletes GetDTE without updating TelescopeLauncher.cs; C4: Lane3 FinderBase adds Debug.WriteLine sites breaking Lane5 X1 grep gate) + 3 major (GotoLine/GetDTE/DllImport stale refs) + 4 minor.
  - rev2-b: 3 MEDIUM (AC table missing diagnostics; test-count arithmetic wrong; Lane5 X1 omits Run_TelescopeLog_* updates) + 5 LOW.
  - rev2-c: 3 MAJOR (doc-ref misses SKILL.md:113 + architecture-review.md:286,402; queue reconciliation lists m20/m16/F13 as subsumed but not fixed) + 9 minor + 5 nit.
- REVISION 2 FIXES APPLIED to plans/plan.md: baseline-relative file:line convention in header; C1/C2/C3/C4 cross-lane fixes; M1/M2/M3 stale-symbol fixes; m4 TelescopeLog in FinderBase; F1/F2/F3/F4/F5/F6/F7 AC + count fixes (cumulative: Telescope 56->63->72->77, NeoVisual 38->42->58->73); doc-ref SKILL.md:113 + architecture-review.md:286,402; queue reconciliation corrected (removed m6/m8/m16/m20/m22/m24/m34/m36/m59/F13, moved n2->N4, n3->N1); Lane2 e2e queue + explorer-open-navigation + T4; NIT 16/17/18/19. Plan now 1195 lines.

## 2026-09-28 — Revision 3 + gate (architecture consolidation plan)
- REVISION 3 (adversarial + gate): 2 agents (arch-auditor adversarial + docs-reviewer formal gate), each wrote to its OWN file (rev3-a.md, rev3-b.md).
  - rev3-a: 2 CRITICAL (N3 engine/test contradiction — the plan's Scan 2 'max adjacency, first-encountered' contradicted its own Up test; DOWN tolerance mis-translated as GapTo>1 instead of c.Y-a.Y>1 top-vs-top) + 4 major (C8 calls pre-T5 2-arg FocusGuard; doc-ref misses SetWindowDivideSelectionSizes@SKILL.md:109 + ActivateWindow@architecture-review.md:402; X2 F39 risks the advancing-cursor bug) + 5 minor + 1 nit.
  - rev3-b: REVISE (1 MAJOR: doc-ref gate 'exits 0' unachievable — baseline lint already red on 7 pre-existing refs; 1 NIT: X1 counts).
- REVISION 3 FIXES APPLIED: N3 engine now reproduces the CURRENT algorithm (verified against WindowMatrix.cs:155-456 — max adjacency within [minGap,minGap+divide], last-wins ties, DOWN c.Y-a.Y>1, exact IsInDirection/IsAligned formulas); 10 Run_WindowNavigationEngine_* tests rewritten to pin it; C8 uses 4-arg ShouldRouteToolWindowKey; doc-ref SKILL.md:109 + ActivateWindow added; X2 F39 keeps the fixed baseline (CRITICAL CONSTRAINT); X1 timestamps at Write() + e2e-only risk note; Lane 1 BP-5 enumerates surviving GetDTE sites; doc-ref gate redefined as DIFF-BASED (header + A6); X1 counts reconciled (17 files/52 sites, 6 die in Lane 1 BP-3, 46 survive).
- RE-REVIEW (docs-reviewer): **APPROVE** (0 critical / 0 major / 1 minor M1 — X1 count reconciliation, fixed). Gate closed. Plan: 1210 lines, 28 BP steps, 5 lanes.
- NEXT: Step 7 handoff — present the plan + queue changes to the user via question; on approval write docs/implementation_plan.md + progress.md first item + e2e-queue.md entries.

## 2026-09-28 — HANDOFF COMPLETE (architecture consolidation plan)
- USER APPROVED the handoff + the subsumed-findings marking.
- WROTE docs/implementation_plan.md (1210 lines, 28 BP steps, 5 lanes) from sessions/.../plans/plan.md.
- APPENDED e2e-queue.md: 5 entries E2E-AC-1..5 (status QUEUED, one per lane, 'none (re-run existing)' scenarios, per-lane affected scenarios + diagnostics).
- UPDATED docs/progress.md: consolidation plan as FIRST pending item with 'DEFER e2e tests (unit-only lane)' instructions; covered-by notes added to the Architecture review backlog (F2,F3,F4,F6,F7,F11,F36-F41,F44,F46) and Code review backlog (M4,M5,M8,M9 + m17,m18,m19,m21,m23,m25,m33,m37,m38,m43,m44,m45 + n2,n3); 'Next up' + 'Next candidates' notes updated.
- VERIFIED: doc-ref lint reports exactly the 7 pre-existing unresolved refs (documented in the plan header) — NO NEW drift from the handoff (diff-based gate passes).
- NEXT STEP for the user: tell neovim_hub to execute the FIRST pending item (Architecture consolidation) in the unit-only lane (e2e deferred to e2e-queue.md E2E-AC-1..5).

## 2026-10-02 — Session neovim-planning-hub-20261002-100206 (code-review 51-findings plan)
- REQUEST: read docs/reviews/code-review.md (2026-10-02, 51 findings: 0 critical, 5 major, 22 minor, 24 nit) and create a plan fixing ALL findings — even minors and nits.
- Re-orientation: read AGENTS.md (auto), SKILL.md, progress.md, spec.md, implementation_plan.md (finished 98-findings plan, GREEN 349fc05 — safe to overwrite at handoff), e2e-queue.md, session state.
- Queue reconciliation (Step 2): code review says "None filed yet — pending the user's selection". Cross-refs to Architecture backlog: F5→R3, F7→R4, F8/F9→R22/R42, F10→R2, F14→R45. Prior-regression re-reports: M1→R1, M4→R2, M7→R24.
- Research phase: feature-researcher SKIPPED (internal bugfix, no LazyVim reference — prior-plan precedent). Dispatching trailmark-recon (structural) + arch-auditor (fix-direction/unit-seam verification) in parallel.

## 2026-10-02 — Session neovim-planning-hub-20261002-100206 (code-review 51-findings plan) — HANDOFF COMPLETE
- Research: trailmark-recon 33/33 claim-groups CONFIRMED; arch-auditor 51/51 fix directions verified (2 NEEDS-CORRECTION: R1 surface-aware, R3 IVimBuffer threading).
- Plan: 10 phases (0-9), 51 BP steps (BP-1..BP-51), Verification Trace (52 rows). docs-reviewer gate: REVISE round 1 (BP-1 `a` placement + ShiftI redundancy; R19 test-project attribution; BP-26 static-cache; BP-39 shared TryPromptMotion) → fixed (hub fixed Part A; implementation-planner revised BP-1/26/39) → APPROVE round 2.
- USER APPROVED the handoff + covered-by queue reconciliation.
- WROTE docs/implementation_plan.md (864 lines, SHA256 byte-identical to the approved session plan); appended e2e-queue.md E2E-CR51-1..10 (QUEUED); updated docs/progress.md (51-findings plan as FIRST pending item, unit-only lane, e2e deferred; covered-by F5→R3/F7→R4/F8/F9→R22/R42/F10→R2/F14→R45; header + Next up + Next candidates updated).
- VERIFIED: doc-ref lint PASS (28 docs, 6499 refs, 0 unresolved).
- NEXT STEP for the user: tell neovim_hub to execute the FIRST pending item (Code review fixes, 51 findings) in the unit-only lane (e2e deferred to e2e-queue.md E2E-CR51-1..10).
