---
name: sprint-plan-gate
description: Use before starting any multi-step feature or bugfix. Intent → spec/plan → approve → dispatch → lifecycle gate, so nothing ships without an approved plan. Mirrors the docs-reviewer spec/plan gates and the build loop. Adapted from dimkurilo wave-spec + superpowers writing-plans/verifying-before-completion.
license: MIT
compatibility: opencode
---
# Sprint plan gate

A plan-gate discipline so work never skips planning or verification. Mirrors this repo's `docs-reviewer` spec/plan gates.

## When to use
- Starting a new feature/bugfix (every `neovim_hub` loop item).
- When a change could touch the diagnostics-as-contract log lines or the net472/UI-thread hard requirements.

## Core method
1. **Intent** — one sentence: what changes and why.
2. **Spec/Plan** — write `docs/implementation_plan.md`: goal, approach, acceptance criteria, the E2E test plan (scenario names + the diagnostics they assert), and which offline unit tests to extend. Every criterion maps to a diagnostic + a test.
3. **Approve** — run the `docs-reviewer` gate (`spec` / `initial-plan` / `build-plan`); if REVISE, fix and re-review (max 3 rounds). Do not build on an unapproved plan.
4. **Dispatch** — e2e-test-builder (RED) → implementation-planner (Build Plan) → docs-reviewer (build-plan gate) → build-agent → debug-agent (only on failure) → implementation-planner (re-plan) → verification-agent; then a verify-time debug-agent → re-plan on RED. Max 5 iterations (real regressions only).
5. **Lifecycle gate** — after GREEN: update `docs/spec.md`, re-run the SPEC gate, mark the item done in `docs/progress.md`; on RED, feed the verifier feedback back to the planner.

## Source
`dimkurilo/opencode-skills` (wave-spec) and `obra/superpowers` (writing-plans, verifying-before-completion). Load only if the gate above needs the full template.
