# Session Log — neovim-planning-hub-20260930-075819

## 2026-09-30 — Session start

- REQUEST: (1) create a plan for the 67 findings in docs/code-review.md (latest
  review, 2026-09-30); (2) restructure the repo into logical folders (nested
  subfolders allowed).
- Re-orientation: read AGENTS.md (auto), vs-extension-dev SKILL.md, docs/progress.md,
  docs/spec.md, docs/implementation_plan.md (old 75-finding plan — DONE, safe to
  overwrite at handoff), docs/code-review.md (the new 67-finding review), e2e-queue.md,
  session state. Inspected repo layout (MyExtension/ ~30 flat .cs, Telescope/ ~40 flat
  .cs, tools/ 5 .ps1, docs/ 8 .md), csproj files (SDK-style globbing — moves are
  build-safe), check-doc-refs.ps1 (file-path refs break on move; symbol refs survive).
- CLARIFICATION (question tool): user chose **folders + namespaces**, **one combined
  plan** (fixes first, restructure final), **source + tools + docs** scope.
- Session created: neovim-planning-hub-20260930-075819.
- GROUND TRUTH: `dotnet build MyExtension.slnx` = 0 errors (115 pre-existing warnings). Repo is GREEN (commit f4450cb + progress 164e757). LSP errors were stale IntelliSense artifacts.

## 2026-09-30 — Research phase (3 parallel agents)

- trailmark-recon: DONE. Graph 1691 nodes / 681 proxies (40.3%) / 0 entrypoints. All 12 structural claims CONFIRMED (CR2, M17, M18, M23, M19, M9, M7, M1/M2, M3/M4/M5, M8, M24). M8 = PARTIAL→GUARDED (controller calls FocusGuard-gated but no try/catch). Namespace inventory: MyExtension (35 files, FLAT incl. ToolWindows/ — no MyExtension.ToolWindows ns), CardinalNavigation (10 files from CardinalMovment/), Telescope (41 files), MyExtension.Properties (1).
- arch-auditor (fixes): DONE. All 33 findings verified (2 critical, 31 major) + 6 flagged minors. CORRECTIONS: (1) CR1 — existing exact-set test Run_ActionTable_TextInput_ActionKeysMatchTable pins the buggy set (count 6→7); (2) M10 — existing test Run_Preview_UpFromSecondLineWithLeadingBlankLine asserts the buggy result; (3) M19 — motion math ALREADY shared (TextMotionHelper delegates to TextMotionNavigator); only the key→motion dispatch is duplicated — unify dispatch tables, not "make TextMotionHelper delegate"; (4) M27 — scope to :518 only (:1533/:1537 legitimately assert count=0); (5) m28 — stale "Text.UI not referenced" comment (csproj now references it, TryMove IS testable); (6) M11 — "NRE in HitType cast" refuted (FinderBase has clean `is not`). High-risk interactions: CR1↔exact-set test, M10↔existing test, M27↔telescope-no-selection, M19↔M10/M6, M17↔M16, M1/M2/M15 all touch InputHandler/WindowManager.
- arch-auditor (restructure): DONE. 7-section impact inventory. KEY: no MyExtension.ToolWindows/MyExtension.CardinalMovment namespaces exist (ToolWindows files declare `namespace MyExtension`; CardinalMovment declares `CardinalNavigation`). using CardinalNavigation: 3 files + tests; using Telescope: 4 files + 90 fully-qualified Telescope. refs in 16 MyExtension files; tests/Telescope.Tests is a child of Telescope (renames break it). IVT = assembly names (safe); MEF = external Vim contracts (safe); no reflection type-name strings (safe). csproj: EmbeddedResource default-keybindings.json (relative — move breaks); RootNamespace Telescope; slnx + test csproj paths. tools dot-source harness-common.ps1 via $PSScriptRoot (breaks if split). ~15 agent files reference docs/*.md + tools/*.ps1 by exact path.
- feature-researcher: SKIPPED (both tasks are internal bug fixes + refactor — no LazyVim reference needed).

## 2026-09-30 — Plan design (Step 4)

- WROTE the initial combined plan at plans/plan.md (14 phases): Phase 0 criticals
  (CR1, CR2), 1 hook hot-path (M1/M2/M15/M7/m17), 2 finder amortization
  (M3/M4/M5/M13/m15/m16), 3 motion/preview correctness (M10/M11/M19/M6), 4
  navigation robustness (M9/M14/m3-m6), 5 hook-path safety + logging
  (M8/M12/M16/M25/m14), 6 duplication merges (M18/M20/M21/M22/M24/m13), 7 dead
  code (M17/M23/m11/m12), 8 harness hardening (M26/M27/M28/m18-m20), 9 test
  hermeticity + naming (m1/m2/m7-m10/m21-m30/m32/n1/n2), 10 docs drift
  (M29/M30/M31/m31), 11-14 restructure (MyExtension, Telescope, tools/docs,
  final verification). Restructure design: MyExtension → Hooks/Input/Vim/
  Package/ToolWindows/Navigation/Adornments/Properties/Resources; Telescope →
  Overlay/Finders/Filter/Logging/Controller; tools → harness/+lint/; docs →
  reviews/+plans/ (operational docs stay in root). Flagged deviations: (a)
  CardinalMovment → Navigation/ + MyExtension.Navigation (AGENTS.md typo rule
  deviation — user to confirm at handoff); (b) operational docs kept in docs/
  root (agent exact-path references).
- NEXT: dispatch 3 implementation-planners (Phases 0-5, 6-10, 11-14) in parallel.

## 2026-09-30 — Build Plan (Step 5)

- Planner 1 (Phases 0-5): DONE — BP-1..BP-28 written to artifacts/bp-phases-0-5.md.
- Planner 2 (Phases 6-10): first dispatch returned EMPTY verdict (no artifact). Re-dispatched fresh → DONE — BP-1..BP-39 written to artifacts/bp-phases-6-10.md.
- Planner 3 (Phases 11-14): 3 failures (broken verdict "输入中...", step-cap with full research returned, broken verdict "输入"). Escalated via question → user chose "Try a 4th fresh planner". 4th dispatch (research pre-loaded) → DONE — BP-1..BP-16 written to artifacts/bp-phases-11-14.md. (User's "split into 4 subagents" contingency did not trigger — the artifact was written.)
- COMBINED plans/plan.md: 2045 lines — initial plan (Part A phases 0-10 + Part B phases 11-14 design + AC/unit-test/diagnostics/known-RED/e2e-queue) + 3 BP-n Build Plan sections (BP-1..BP-28, BP-1..BP-39, BP-1..BP-16), each ending DONE.
- NEXT: Step 6 — docs-reviewer plan gate (initial-plan + build-plan) at the session plan path.

## 2026-09-30 — Plan gate (Step 6)

- docs-reviewer dispatch #1: LOOPED → cancelled. Re-dispatched fresh with a tighter brief (read plan in chunks; do NOT re-verify findings against code; ~40 tool-call budget).
- docs-reviewer dispatch #2: **REVISE** — 2 major + 2 nit:
  1. MAJOR: `WindowManager.cs` + `ToolWindowTypeResolver.cs` (MyExtension/ root, `namespace MyExtension`) OMITTED from the restructure mapping — BP-2's `git grep -l 'namespace MyExtension$'` gate would fail; BP-3 treated WindowManager as staying at root.
  2. MAJOR: Phase 14 BP-14 hardcoded 135/130 as the "unchanged" baseline, but Phases 0-10 ADD tests — the gate would false-RED at Phase 14.
  3. NIT: BP-11 ref counts (36→~41, 26→~28).
  4. NIT: BP-9 dot-source line numbers drifted.
- FIXES APPLIED by hub (DEVIATION from REVIEW-GATE POLICY: Build Plan REVISE normally routes back to implementation-planner, but the planner failed 3× on this section; the fixes were surgical): (1) added WindowManager + ToolWindowTypeResolver to the ToolWindows/ mapping in the design table + BP-2 git mv + BP-3 using list; (2) BP-14 now uses the Phase 11 start baseline (captured after Phase 10) + header note; (3) BP-11 counts marked approximate; (4) BP-9 line numbers marked approximate.
- NEXT: re-review (docs-reviewer) focused on the 4 REVISE fixes.

## 2026-09-30 — Plan gate re-review + handoff (Steps 6-7)

- docs-reviewer re-review: **APPROVE** — all 4 REVISE fixes confirmed resolved; no new critical/major defects. Gate closed.
- HANDOFF (user approved via question): (1) combined plan + queue changes; (2) CardinalMovment → Navigation/ + MyExtension.Navigation; (3) operational docs stay in docs/ root.
- WROTE docs/implementation_plan.md (2052 lines) from plans/plan.md.
- APPENDED e2e-queue.md: 11 new QUEUED entries (E2E-NCR-1, E2E-NCR-2, E2E-NCR-M1/M2/M15, E2E-NCR-M3/M4/M5/M13, E2E-NCR-M10/M11/M19/M6, E2E-NCR-M9/M14, E2E-NCR-M8/M12/M16/M25, E2E-NCR-M18/M20/M21/M22/M24, E2E-NCR-M17/M23, E2E-NCR-M26/M27/M28, E2E-RESTRUCTURE-1) — distinct IDs to avoid collision with the old plan's E2E-CR-*/E2E-M* entries.
- UPDATED docs/progress.md: combined plan as FIRST pending item (unit-only lane, e2e deferred, subsumed-findings note); "Next candidates" updated (F5/F8/F9/F22 covered, F12/F15 fixed, remaining F13/F14/F43); header bumped to 2026-09-30.
- VERIFIED: doc-ref lint `[PASS] 28 docs, 6015 refs, 0 unresolved` (no drift from the handoff).
- NEXT STEP for the user: tell neovim_hub to execute the FIRST pending item (Code review findings (67) + Repository restructure) in the unit-only lane (e2e deferred to e2e-queue.md E2E-NCR-* + E2E-RESTRUCTURE-1).
