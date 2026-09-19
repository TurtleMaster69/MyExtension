---
name: perf-investigation
description: Use when an optimization is proposed without evidence, or a hot path is suspected. Measurement-first performance investigation — characterize, profile per dimension, name the bottleneck. Adapted from osmontero investigating-performance.
license: MIT
compatibility: opencode
---
# Performance investigation

Measurement-first: never optimize without naming the bottleneck. Adapted from `investigating-performance`.

## When to use
- A perf claim is made with no measurement (e.g. "the overlay is slow").
- A hot path is suspected (keyboard hook per-key, fzf per-keystroke spawn, preview re-tokenize per selection, DTE re-enumeration per open).

## Core method
1. **Characterize** — reproduce the cost with a real workload (a large solution / many candidates), not a tiny scratch file. Measure, don't guess.
2. **Profile per dimension** — split the work: per-keystroke vs per-open; UI-thread vs background; subprocess spawn vs in-process; allocation churn vs IO. Name which dimension dominates.
3. **Refuse to optimize without a named bottleneck** — if you can't name it, add instrumentation (a timing log line) until you can.
4. Fix the dominant dimension first; re-measure to confirm the win, and check the tail-latency case (large files, many candidates).
5. Guard against regression with a bound (e.g. cache the preview payload so a re-render is a no-op).

## Source
`osmontero/opencode-skills` (investigating-performance). Load only if the method above is insufficient.
