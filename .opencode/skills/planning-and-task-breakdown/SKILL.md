---
name: planning-and-task-breakdown
description: Use when writing an implementation/build plan from a spec — decompose into small, verifiable tasks with acceptance criteria and dependency ordering. Maps to the BP-n / Verify-with / Fails-if plan contract. Adapted from addyosmani planning-and-task-breakdown.
license: MIT
compatibility: opencode
---
# Planning & task breakdown (pointer)

Decompose a feature/bugfix into executable, verifiable tasks. Adapted from `addyosmani` `planning-and-task-breakdown`. Mirrors `implementation-planner`'s BP-n Build Plan.

## When to use
- Writing `docs/implementation_plan.md` for a loop item.

## Core method
1. **Read-only planning** — read the spec + relevant code; map dependencies; note risks. No code yet.
2. **Dependency graph** — build foundations first, bottom-up.
3. **Slice vertically** — one complete feature path at a time, not horizontal layers.
4. **Write tasks** — each BP-n step: exact files touched, the acceptance diagnostic it must emit, a Verify-with and a Fails-if. Size S/M; break L/XL down (agents perform best on S/M).
5. **Traceability** — every acceptance criterion maps to a `[NeoVisual]`/`[Telescope]` log line and a test; the Verification Trace table links each failing test to its implicated step.

## Source
Full template in `addyosmani/agent-skills` `planning-and-task-breakdown`. Load only if the method above is insufficient.
