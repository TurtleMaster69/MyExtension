---
name: code-testing-agent
description: Use when generating or improving .NET unit tests for the offline test projects. Write meaningful behavior tests (not implementation-coupled), cover edge cases, scaffold test projects. Adapted from microsoft/testfx code-testing-agent.
license: MIT
compatibility: opencode
---
# .NET code testing (pointer)

Write meaningful .NET unit tests. Adapted from official `microsoft/testfx` `code-testing-agent`. Fits `e2e-test-builder` writing `tests/*.Tests`.

## When to use
- Adding offline unit tests for a pure-logic class (OverlayKeyHandler / TextMotionNavigator pattern).
- Improving coverage / testability of a VS-coupled class by extracting a dependency-free seam.

## Core method
- **Behavior, not implementation** — assert what the code does, not how; tests that test the implementation pass even when behavior is broken.
- **Edge cases** — empty, boundaries, special characters, null; the boundary/error path is where bugs live.
- **Testability first** — if a unit test needs VS/MEF/window context, the seam is wrong; extract the decision into a dependency-free class and test that.
- **Run the suite** — `dotnet run --project tests/Telescope.Tests` (41) and `tests/NeoVisual.Tests` (21); substring filter as first arg; `--list`.
- Keep the assertion aligned with the `[NeoVisual]`/`[Telescope]` diagnostic contract the e2e harness also asserts.

## Source
`microsoft/testfx` `.agents/skills/code-testing-agent`. Load only if the method above is insufficient.
