---
description: Launch the MyExtension build hub (neovim_hub). Initializes docs/spec.md + docs/progress.md, then drives the red/green build loop (e2e-test-builder -> implementation-planner -> docs-reviewer plan gates -> build-agent -> debug-agent -> implementation-planner -> verification-agent) until the current feature works.
agent: neovim_hub
---

Run the MyExtension build workflow. Initialize the workflow docs if missing, then
take the next pending item from docs/progress.md and drive it through the red/green
loop until it works, then ask me what feature to add next (via options, not inline
text).

Boot Trailmark before planning (`trailmark --version`; install `uv tool install
trailmark` if missing) and use it for all structural questions instead of
`grep`/manual reading — see AGENTS.md's "Trailmark is mandatory" section.