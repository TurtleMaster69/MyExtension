# HUB STATE — code-review-hub (MyExtension)

Status: ACTIVE · Updated: 2026-09-28 · Session/compaction count: 0
Objective: Orchestrate read-only, Trailmark-graph-backed code reviews of the
MyExtension VSIX across seven dimensions (clean architecture & simplification,
correctness & bugs, performance, test quality, security & interop, conventions &
consistency, docs accuracy) and deliver a prioritized report to `docs/code-review.md`.

## Decisions (append-only; newest on top)

- [2026-09-28] DECIDED: New standalone hub, broader than `neovim_review_hub`
  (architecture-only). — reason: user wants a full code-review hub covering all
  seven dimensions; both hubs coexist, this one is the "review everything" entry
  point. — status: ACTIVE
- [2026-09-28] DECIDED: Report goes to `docs/code-review.md` (new file), not
  `docs/architecture-review.md`. — reason: avoids two hubs overwriting each
  other's report. — status: ACTIVE
- [2026-09-28] DECIDED: Reuse `arch-auditor` + `trailmark-recon` +
  `code-slice-worker` + `docs-reviewer` + `verification-agent`; scaffold
  `code-review-worker` + `test-quality-reviewer` + `docs-accuracy-reviewer`
  (all `hidden: true`). — reason: arch-auditor covers architecture; the three
  new workers cover the dimensions it doesn't (correctness/security, test
  quality, docs accuracy). — status: ACTIVE
- [2026-09-28] DECIDED: Exclude global waypoint-family agents
  (`simplification-check`, `problems-finder`). — reason: they analyze a proposed
  architecture vs the old one (waypoint refactor workflow), contradicting this
  hub's review-current-state mandate. — status: ACTIVE
- [2026-09-28] DECIDED: Nothing in this repo is invisible to git. — reason:
  user constraint; no `.gitignore` / `.git/info/exclude` entries added; the
  workspace (`.opencode/workspaces/code-review-hub/`) is git-visible by design.
  — status: ACTIVE

## Unresolved bugs / open questions

- None at build time.

## Implementation details (verbatim identifiers)

- Hub file: `.opencode/agent/code-review-hub.md` (mode: primary, steps: 200).
- New subagents: `.opencode/agent/code-review-worker.md`,
  `.opencode/agent/test-quality-reviewer.md`,
  `.opencode/agent/docs-accuracy-reviewer.md` (all `hidden: true`, steps: 60).
- Reused: `arch-auditor`, `trailmark-recon`, `code-slice-worker`,
  `docs-reviewer`, `verification-agent`.
- Report: `docs/code-review.md`. Filed findings: `docs/progress.md` (append-only,
  on user approval — `neovim_hub` owns it).
- Shared guidance: `.opencode/agent/trailmark-guidance.md`,
  `.opencode/agent/prompt-rule.md` (single-sourced, referenced not duplicated).
- Session layout: `.opencode/workspaces/code-review-hub/sessions/<session-id>/`
  (session.md + state.md/tasks.md/log.md/artifacts/).

## Plan

- [x] Phase 1 inventory (agents, skills, config, docs, gitignore).
- [x] Phase 2 interview (scope, routing, workspace, mode, placement, output).
- [x] Phase 3 design (hub + 3 new subagents).
- [x] Phase 4 skill research + curation (skills.md).
- [x] Phase 5 verification (skill-verifier + hub-reviewer).
- [x] Phase 6 write files + hand back.

## Test commands

- `pwsh tools/check-doc-refs.ps1` — doc-reference integrity (docs-accuracy).
- `dotnet run --project tests/Telescope.Tests` — Telescope unit suite.
- `dotnet run --project tests/NeoVisual.Tests` — NeoVisual unit suite.
- `trailmark --version` / `uv run trailmark --version` — Trailmark boot.

## Next move

1. Restart opencode (config loads once at startup).
2. Invoke: `Task > code-review-hub with: <objective>` (or select it as primary).
