# HUB STATE — neovim-planning-hub (MyExtension)

Status: ACTIVE · Updated: 2026-09-28 · Session/compaction count: 0
Objective: Research (LazyVim reference + native VS reuse) and produce unit-only
implementation plans for neovim_hub, queue deferred e2e tests, and reconcile the
build queue.

## Decisions (append-only; newest on top)

- [2026-09-28] DECIDED: New standalone planning hub, upstream of `neovim_hub`.
  — reason: user wants a hub that researches and produces unit-only plans for the
  build hub; no existing hub covers research + planning + e2e deferral. — status: ACTIVE
- [2026-09-28] DECIDED: Plans are **unit-only**; e2e scenarios are queued in
  `e2e-queue.md` (status QUEUED → READY when the linked plan is GREEN), never
  created/executed until the plan is finished. — reason: the user is not on a
  machine that can run the e2e harness (boots VS Experimental Instance). — status: ACTIVE
- [2026-09-28] DECIDED: Handoff = write the plan to `docs/implementation_plan.md`
  + append a pending item to `docs/progress.md` as the FIRST item with
  instructions to defer e2e tests (unit-only lane), on user approval. — reason:
  that is the file neovim_hub reads; the queue item makes it the first thing the
  build hub does. — status: ACTIVE
- [2026-09-28] DECIDED: Queue reconciliation — check `docs/progress.md` before
  planning; remove duplicates, move prerequisites up, add the new plan first.
  Applied only on user approval. — reason: user instruction; the plan must not
  duplicate existing work or skip a prerequisite. — status: ACTIVE
- [2026-09-28] DECIDED: Research is delegated to a new web-enabled
  `feature-researcher` subagent (hidden); the hub does NOT do web research
  in-context. — reason: clean window, parallelizable, matches the hub pattern. — status: ACTIVE
- [2026-09-28] DECIDED: Route to `trailmark-recon`, `arch-auditor`,
  `docs-reviewer`, `implementation-planner` (existing) + `feature-researcher`
  (new); `e2e-test-builder` + `verification-agent` whitelisted for e2e-queue
  drain on a capable machine. — reason: user-selected routing; the four existing
  agents cover structural grounding, change-area analysis, the plan gate, and the
  BP-n Build Plan. — status: ACTIVE
- [2026-09-28] DECIDED: No new skill installs — all needs covered by vendored
  skills; the research method is baked into `feature-researcher`'s rules.
  — reason: skill-researcher (18 web/tool calls) found no reputable web-research
  skill exists; the only candidate (track-management) conflicts with the repo's
  planning contract. — status: ACTIVE
- [2026-09-28] DECIDED: Nothing in this repo is invisible to git. — reason: user
  constraint (same as code-review-hub); no `.gitignore` / `.git/info/exclude`
  entries added; the workspace (`.opencode/workspaces/neovim-planning-hub/`) is
  git-visible by design. — status: ACTIVE

## Unresolved bugs / open questions

- None at build time.

## Implementation details (verbatim identifiers)

- Hub file: `.opencode/agent/neovim-planning-hub.md` (mode: primary, steps: 200).
- New subagent: `.opencode/agent/feature-researcher.md` (hidden: true, steps: 60).
- Reused: `trailmark-recon`, `arch-auditor`, `docs-reviewer`,
  `implementation-planner`, `e2e-test-builder`, `verification-agent`.
- Plan handoff: `docs/implementation_plan.md` + `docs/progress.md` (first item,
  unit-only lane, e2e deferred).
- e2e queue: `.opencode/workspaces/neovim-planning-hub/e2e-queue.md`.
- Shared guidance: `.opencode/agent/trailmark-guidance.md`,
  `.opencode/agent/prompt-rule.md` (single-sourced, referenced not duplicated).
- Session layout: `.opencode/workspaces/neovim-planning-hub/sessions/<session-id>/`
  (session.md + state.md/tasks.md/log.md/artifacts/ + plans/).

## Plan

- [x] Phase 1 inventory (agents, skills, config, docs, gitignore).
- [x] Phase 2 interview (scope, routing, research, workspace, mode, placement,
      e2e queue, handoff).
- [x] Phase 3 design (hub + feature-researcher subagent).
- [x] Phase 4 skill research + curation (skills.md).
- [x] Phase 5 verification (skill-verifier + hub-reviewer).
- [x] Phase 6 write files + hand back.

## Test commands

- `dotnet run --project tests/Telescope.Tests` — Telescope unit suite.
- `dotnet run --project tests/NeoVisual.Tests` — NeoVisual unit suite.
- `trailmark --version` / `uv run trailmark --version` — Trailmark boot.
- `pwsh tools/check-doc-refs.ps1` — doc-reference integrity (before handoff).

## Modified files (path: why it matters)

- (populated at handoff, Step 7 — e.g. `docs/implementation_plan.md`: the plan;
  `docs/progress.md`: the first pending item with e2e-deferral instructions;
  `e2e-queue.md`: the deferred e2e scenarios.)

## Next move

1. Restart opencode (config loads once at startup).
2. Invoke: `Task > neovim-planning-hub with: <feature/bugfix to plan>` (or select
   it as primary).
