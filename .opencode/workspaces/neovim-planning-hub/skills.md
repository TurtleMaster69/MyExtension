# Planning Hub — Curated Skill Set (skills.md)

> The skill set for the hub and its subagents: chosen skills (name, source,
> license, install path, owning agent) + drop reasons. Written by hub-creator;
> re-confirmed by skill-verifier (SEE / ACCESS / WILL-USE / contradiction).

## Chosen skills (all already vendored in this repo — no new installs)

| Skill | Source | License | Install path | Owning agent |
|-------|--------|---------|--------------|--------------|
| `trailmark` | trailofbits (vendored) | CC-BY-SA-4.0 | `.opencode/skills/trailmark/` | hub + all subagents |
| `trailmark-structural` | trailofbits (vendored) | CC-BY-SA-4.0 | `.opencode/skills/trailmark-structural/` | hub + arch-auditor |
| `trailmark-summary` | trailofbits (vendored) | CC-BY-SA-4.0 | `.opencode/skills/trailmark-summary/` | hub |
| `planning-and-task-breakdown` | addyosmani (adapted) | — | `.opencode/skills/planning-and-task-breakdown/` | hub + implementation-planner + docs-reviewer |
| `sprint-plan-gate` | dimkurilo wave-spec + superpowers (adapted) | — | `.opencode/skills/sprint-plan-gate/` | hub + docs-reviewer + implementation-planner |
| `dispatching-parallel-agents` | obra/superpowers (adapted) | MIT | `.opencode/skills/dispatching-parallel-agents/` | hub |
| `requesting-code-review` | obra/superpowers (adapted) | MIT | `.opencode/skills/requesting-code-review/` | hub |
| `verification-before-completion` | obra/superpowers (adapted) | MIT | `.opencode/skills/verification-before-completion/` | hub |
| `audit-verification-gates` | agentpatterns-ai (adapted) | — | `.opencode/skills/audit-verification-gates/` | hub |
| `vs-extension-dev` | repo-authored | — | `.opencode/skills/vs-extension-dev/` | hub + feature-researcher + all subagents |
| `code-testing-agent` | microsoft/testfx (adapted) | — | `.opencode/skills/code-testing-agent/` | hub |
| `verify-tests-fail-without-fix` | dotnet/maui (adapted) | — | `.opencode/skills/verify-tests-fail-without-fix/` | hub |

## Skill research (2026-09-28) — no new installs

`skill-researcher` (18 web/tool calls; anthropics, obra/superpowers,
wshobson/agents, vercel-labs, dotnet/skills, agentskills.io all checked) found no
new reputable skills worth adding:

- **No reputable web-research skill exists** in the ecosystem. The LazyVim +
  native-VS research method is baked into `feature-researcher`'s own rules
  (bounded research, ≤20 tool calls, no repeated queries, diminishing-returns
  stop, ≤1500-token digest with build-vs-extend-vs-skip).
- **Planning is already covered** by vendored `planning-and-task-breakdown` +
  `sprint-plan-gate` (which fold in addyosmani + superpowers `writing-plans` +
  dimkurilo `wave-spec` — the gold-standard plan-authoring sources).
- **DROPPED (conflict):** `track-management` (wshobson/agents Conductor) —
  mandates a competing file/registry lifecycle (`tracks.md`, `plan.md`, SHA
  recording) that collides with the repo's established contract
  (`docs/progress.md` queue, `docs/implementation_plan.md`, BP-n Build Plan,
  docs-reviewer APPROVE/REVISE gate). Would push the hub out of its one-job.
- **DROPPED (conflict):** `context-driven-development` (wshobson/agents
  Conductor) — competing planning methodology that would override the vendored
  planning skills and the docs-reviewer gates.
- **Noise (do not install):** anthropics `docx/pdf/pptx/xlsx` (source-available,
  no mapping), vercel-labs React skills, wshobson domain skills, dotnet/skills
  plugins (already folded into vendored skills).

## Skills deliberately NOT assigned (drop reasons)

- `trailmark-finding-triage`, `trailmark-review-gate`, `graph-evolution`,
  `audit-augmentation`, `trailmark-variant-neighborhood` — this VSIX has NO
  detected entrypoints, so taint / privilege-boundary / attack-surface /
  finding-triage / review-gate carry no signal (AGENTS.md repo traps).
- `test-driven-development`, `systematic-debugging`, `debugging-and-error-recovery`,
  `dotnet-build-test-diag`, `binlog-failure-analysis`, `analyzing-dotnet-performance`,
  `dotnet-code-review`, `dotnet-pinvoke`, `review-duplication`, `perf-investigation`,
  `test-smell-detection`, `test-anti-patterns`, `assertion-quality`,
  `test-gap-analysis`, `grade-tests`, `find-untested-sources`, `genotoxic`,
  `vector-forge`, `crypto-protocol-diagram`, `mermaid-to-proverif`,
  `slicing-code-context`, `diagramming-code` — build-loop / review / test-audit /
  crypto skills; the planning hub never writes code, never runs the build loop,
  and never audits tests. (The subagents it spawns — arch-auditor,
  implementation-planner, docs-reviewer, e2e-test-builder, verification-agent —
  load their OWN skills per their own files.)
- `waypoint-planning` (global), `md-to-pdf` (global) — waypoint refactor
  workflow + PDF export; no mapping to planning.

## Git visibility

Per user constraint (2026-09-28, same as code-review-hub): **nothing in this repo
is invisible to git.** No `.gitignore` / `.git/info/exclude` entries were added.
The workspace (`.opencode/workspaces/neovim-planning-hub/`), the hub agent file,
and the feature-researcher file are git-visible by design.
