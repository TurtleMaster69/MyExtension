---
name: audit-verification-gates
description: Use when verifying whether a build/feature is actually done — audit the completion gate for self-report trust, gameability, or grading-the-execution-path instead of the outcome. Adapted from agentpatterns-ai audit-verification-gates.
license: MIT
compatibility: opencode
---
# Audit verification gates (pointer)

Can the agent's "done" be trusted? Adapted from `agentpatterns-ai` `audit-verification-gates`. Fits `verification-agent`'s job: catch a false-green.

## When to use
- Returning a PASS/FAIL verdict after a build (verification-agent).
- Judging whether a test/scenario truly proves the behavior.

## Core method
- **Trust the outcome, not the self-report** — a scenario that passes because a log line appears is only proof if that line is causally tied to the behavior, not just present.
- **Flag self-report gates** — if "done" is asserted by the agent that did the work, verify independently (re-run the e2e scenario + the unit suite).
- **Check the execution path** — a gate that grades how the code ran (not what it produced) is gameable; require evidence: a specific diagnostic line, a failing-then-passing regression test.
- **Proof the RED was real** — a test that never failed is not a regression test; confirm it catches the bug (see verify-tests-fail-without-fix).

## Source
Full checklist in `agentpatterns-ai/skills` `audit-verification-gates`. Load only if the method above is insufficient.
