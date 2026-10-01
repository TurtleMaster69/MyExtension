---
name: test-driven-development
description: Use when implementing a feature or fixing a bug in this repo. Red-green-refactor cycle — write the failing test first, then the minimum code to pass, then clean up. Mirrors the RED→PLAN→BUILD→VERIFY loop. Adapted from obra/superpowers.
license: MIT
compatibility: opencode
---
# Test-driven development

Red-green-refactor, aligned to this repo's loop: `e2e-test-builder` (RED) → `docs-reviewer` initial-plan gate → `implementation-planner` (Build Plan) → `docs-reviewer` build-plan gate → `build-agent` → `debug-agent` (on failure) → `verification-agent` (then verify-time `debug-agent` → re-plan on RED).

## When to use
- Implementing any new built-in action / controller / finder.
- Fixing a bug — the regression test comes first.

## Core method
1. **RED** — write the test (offline unit in `tests/*.Tests`, or an e2e scenario in `tools/harness/test-e2e.ps1`) and prove it FAILS with the current code. Offline for pure logic; e2e only when the behavior needs a live VS instance.
2. **GREEN** — write the minimum code to pass. Follow the repo seams: a new built-in action = case in `InputHandler.ResolveAction` + a line in `default-keybindings.json`; extract pure logic into a dependency-free class.
3. **REFACTOR** — clean up, keep the diagnostic contract (`[NeoVisual]`/`[Telescope]` lines) unchanged; re-run the unit suite and the affected `-Tests` subset.
4. Verify both the new test and the pre-existing suite stay green before moving on.

## Source
Full guidance in `obra/superpowers` (test-driven-development). Load only if the method above is insufficient.
