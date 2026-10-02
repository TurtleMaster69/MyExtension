# Code Review Hub — Curated Skill Set (skills.md)

> The skill set for the hub and its subagents: chosen skills (name, source,
> license, install path, owning agent) + drop reasons. Written by hub-creator;
> re-confirmed by skill-verifier (SEE / ACCESS / WILL-USE / contradiction).

## Chosen skills (all already vendored in this repo — no new installs)

| Skill | Source | License | Install path | Owning agent |
|-------|--------|---------|--------------|--------------|
| `trailmark` | trailofbits (vendored) | CC-BY-SA-4.0 | `.opencode/skills/trailmark/` | hub + all workers |
| `trailmark-structural` | trailofbits (vendored) | CC-BY-SA-4.0 | `.opencode/skills/trailmark-structural/` | hub + arch-auditor + code-review-worker |
| `trailmark-summary` | trailofbits (vendored) | CC-BY-SA-4.0 | `.opencode/skills/trailmark-summary/` | hub |
| `dotnet-code-review` | dotnet/runtime (folded) | MIT | `.opencode/skills/dotnet-code-review/` | code-review-worker + arch-auditor |
| `review-duplication` | google-gemini (adapted) | — | `.opencode/skills/review-duplication/` | hub + arch-auditor |
| `analyzing-dotnet-performance` | — | — | `.opencode/skills/analyzing-dotnet-performance/` | arch-auditor + code-review-worker |
| `perf-investigation` | osmontero (adapted) | — | `.opencode/skills/perf-investigation/` | hub + arch-auditor |
| `dotnet-pinvoke` | dotnet/skills (folded) | MIT | `.opencode/skills/dotnet-pinvoke/` | code-review-worker |
| `test-smell-detection` | testsmells.org taxonomy | — | `.opencode/skills/test-smell-detection/` | test-quality-reviewer + arch-auditor |
| `test-anti-patterns` | — | — | `.opencode/skills/test-anti-patterns/` | test-quality-reviewer + arch-auditor |
| `assertion-quality` | — | — | `.opencode/skills/assertion-quality/` | test-quality-reviewer |
| `code-testing-agent` | microsoft/testfx (adapted) | — | `.opencode/skills/code-testing-agent/` | test-quality-reviewer |
| `dispatching-parallel-agents` | obra/superpowers (adapted) | MIT | `.opencode/skills/dispatching-parallel-agents/` | hub |
| `requesting-code-review` | obra/superpowers (adapted) | MIT | `.opencode/skills/requesting-code-review/` | hub |
| `verification-before-completion` | obra/superpowers (adapted) | MIT | `.opencode/skills/verification-before-completion/` | hub |
| `audit-verification-gates` | agentpatterns-ai (adapted) | — | `.opencode/skills/audit-verification-gates/` | hub |
| `slicing-code-context` | — | — | `.opencode/skills/slicing-code-context/` | code-slice-worker |
| `vs-extension-dev` | repo-authored | — | `.opencode/skills/vs-extension-dev/` | hub + all workers |
| `test-gap-analysis` | dotnet/skills (ADAPTED read-only) | MIT | `.opencode/skills/test-gap-analysis/` | test-quality-reviewer |
| `grade-tests` | dotnet/skills (ADAPTED: extension-loading optional) | MIT | `.opencode/skills/grade-tests/` | test-quality-reviewer |
| `find-untested-sources` | dotnet/skills (ADAPTED read-only) | MIT | `.opencode/skills/find-untested-sources/` (+ `scripts/`) | test-quality-reviewer |

## Skills added via the user approval gate (2026-09-28)

All three are Microsoft `dotnet/skills` (MIT, 5.5k★, active), verified against
their actual `SKILL.md` by `skill-researcher`, and installed for
`test-quality-reviewer` only:

- **`test-gap-analysis`** — WHAT: pseudo-mutation gap analysis ("which
  caller-visible production behaviors could change without an existing test
  failing?"). WHY: fills the coverage-gap/behavioral-blind-spot hole (the
  vendored `assertion-quality` already cross-references it). ADAPTED: stripped
  the write-mode "Close gaps" section and the mutation-verification section
  (read-only mandate); `Survived` label forbidden; static counterfactual
  tracing only.
- **`grade-tests`** — WHAT: per-test A–F grading (assertion strength, structure,
  anti-pattern hygiene). WHY: per-test verdicts on curated test lists. ADAPTED:
  the mandatory `test-analysis-extensions` dependency is not vendored, so the
  extension-loading step is now optional (built-in rubric is self-contained).
- **`find-untested-sources`** — WHAT: static source-to-test pairing (which
  source files have no test referencing their declared types). WHY: coverage-gap
  dimension. ADAPTED: read-only header note — the parse-only analyzer is the ONE
  sanctioned execution (never writes, never builds the target, never runs
  tests); prerequisite failure is reported, never papered over with manual
  globbing. Scripts vendored: `scripts/Find-UntestedSources.cs` (Roslyn, needs
  SDK 11+) and `scripts/find_untested_sources.py` (tree-sitter, needs Python
  3.10+ + `tree-sitter-language-pack`). If neither toolchain is available, the
  agent reports the prerequisite failure per the skill's own rule.

## Skills DROPPED after the user approval gate

- **`testability-obstacle`** (dotnet/skills, MIT) — user approved it, but
  verification found it is a WRITER skill: its entire purpose is a production
  edit plus new tests ("Update every composition root or constructor call
  affected by the seam", "Write deterministic tests", "Run the affected
  production build"). This directly contradicts the read-only mandate of
  `test-quality-reviewer` and the hub. DROPPED (not installed). If a read-only
  "detect testability obstacles" diagnostic is ever wanted, it would be a NEW
  skill derived from the skill's Step 1, not an adaptation of this one.

## Shared skills — scoped in the agent, not the skill (skill-verifier ADAPT)

The following skills are SHARED between read-only review agents and write-mode
build-loop agents (e.g. `code-testing-agent` is used by `test-quality-reviewer`
AND `e2e-test-builder`; `perf-investigation` by `arch-auditor` AND build
agents). Adding a blanket "read-only" note to the shared skill would break the
write-mode agents, so the read-only scoping lives in each agent's OWN
instructions instead:

- `requesting-code-review` → `code-review-hub`: dispatch-with-crafted-context
  method only; file findings for the build hub, never fix.
- `perf-investigation` → `code-review-hub` + `arch-auditor`:
  characterize/profile/name-the-bottleneck steps only; report the fix as a
  recommendation, never apply it.
- `code-testing-agent` → `test-quality-reviewer`: quality rubric only; never
  write tests, never run the suite.
- `sprint-plan-gate` + `planning-and-task-breakdown` → `docs-reviewer`: Approve
  gate / review criteria only; never write or update the plan/spec.
- `verify-tests-fail-without-fix` + `dotnet-build-test-diag` →
  `verification-agent`: judgment criteria only; never write tests or apply
  fixes; skip the Testability refactor step.

## Git visibility

Per user constraint (2026-09-28): **nothing in this repo is invisible to git.**
No `.gitignore` / `.git/info/exclude` entries were added. The workspace
(`.opencode/workspaces/code-review-hub/`) and all new skills/agents are
git-visible by design.

## Skills deliberately NOT assigned (drop reasons)

- `trailmark-finding-triage`, `trailmark-review-gate`, `graph-evolution`,
  `audit-augmentation`, `trailmark-variant-neighborhood` — this VSIX has NO
  detected entrypoints, so taint / privilege-boundary / attack-surface /
  finding-triage / review-gate carry no signal (AGENTS.md repo traps). Loading
  them would push workers down a dead end. (arch-auditor already documents this.)
- `genotoxic`, `vector-forge`, `crypto-protocol-diagram`, `mermaid-to-proverif` —
  mutation-testing / crypto-vector / protocol-diagram skills; no mapping to this
  review hub's work (no crypto code, no mutation harness in scope).
- `dotnet-build-test-diag`, `binlog-failure-analysis` — build-failure diagnosis;
  the review hub is read-only and does not fix builds (verification-agent may
  load them when confirming a suspicion, but they are not assigned to the hub).
- `test-driven-development`, `verify-tests-fail-without-fix`,
  `planning-and-task-breakdown`, `sprint-plan-gate`, `systematic-debugging`,
  `debugging-and-error-recovery` — build-loop / fix-loop skills; the review hub
  never writes code or fixes bugs, so these would contradict its read-only
  mandate.
- `waypoint-planning` (global), `md-to-pdf` (global) — waypoint refactor
  workflow + PDF export; no mapping to a read-only code review.
