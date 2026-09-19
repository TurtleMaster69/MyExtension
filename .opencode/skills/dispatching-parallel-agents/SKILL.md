---
name: dispatching-parallel-agents
description: Use when several independent review, analysis, or build slices can run at once. Parallel task dispatch with fresh context per agent, then collect and reconcile. Mirrors the review hub's parallel arch-auditor fan-out. Adapted from obra/superpowers.
license: MIT
compatibility: opencode
---
# Dispatching parallel agents

Run independent work concurrently with fresh context per agent, then reconcile. Mirrors the review hub's parallel `arch-auditor` slices.

## When to use
- Auditing multiple independent areas (e.g. MyExtension core / CardinalMovment / ToolWindows / Telescope / tests) at once.
- Independent research or multi-file analysis that doesn't need to share context.

## Core method
1. **Partition** into non-overlapping slices; give each agent an explicit file list and the same conventions to judge against.
2. **Dispatch** all slices in one batched message so they run concurrently; each gets a fresh context and a fixed return format.
3. **Collect** all results; dedupe overlapping findings and merge into canonical ones.
4. **Reconcile** — verify high-impact claims against the code yourself before reporting (a finding that two independent slices agree on is higher confidence).
5. Keep each slice's scope tight (a slice that grows unbounded loses the concurrency benefit).

## Source
`obra/superpowers` (dispatching-parallel-agents, subagent-driven-development). Load only if the method above is insufficient.
