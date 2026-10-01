---
description: Launch the MyExtension architecture review hub (neovim_review_hub) for a full audit of the repo (duplication, complexity, performance, bites-later risks).
agent: neovim_review_hub
---

Run the MyExtension architectural review workflow. Load the project conventions,
spawn the parallel arch-auditor subagents per slice, consolidate and prioritize the
findings, write the live report to docs/reviews/architecture-review.md, then present the
findings as options and ask which to file into docs/progress.md for the build hub.

Boot Trailmark before auditing (`trailmark --version`; install `uv tool install
trailmark` if missing) and require every slice to be graph-backed for structural
questions instead of `grep`/manual reading — see AGENTS.md's "Trailmark is
mandatory" section.