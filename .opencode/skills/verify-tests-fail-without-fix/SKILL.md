---
name: verify-tests-fail-without-fix
description: Use when writing a RED test — prove the test actually catches the bug (fails without the fix, passes with it). Adapts the official dotnet/maui verify-tests-fail-without-fix discipline to this repo's e2e loop.
license: MIT
compatibility: opencode
---
# Verify tests fail without fix (pointer)

A test that never failed is not a regression test. Adapted from official `dotnet/maui` `verify-tests-fail-without-fix`. Fits `e2e-test-builder`'s RED phase.

## When to use
- Writing the offline unit test or e2e scenario for a feature/bugfix.
- Proving the RED is real before implementation.

## Core method
1. **Write the test against the current (buggy) behavior** and run it — it must FAIL.
2. **Confirm the failure is the intended one** — the assertion/diagnostic that fails must be the one tied to the bug, not an unrelated crash.
3. **Run it without the fix** → RED; **with the fix** → GREEN. Both directions proven.
4. Auto-detect the test type: offline unit in `tests/*.Tests` for pure logic, e2e scenario in `tools/harness/test-e2e.ps1` when a live VS instance is needed.
5. If the test can't be made to fail first, the test is testing the wrong thing — revise it.

## Source
`dotnet/maui` `.github/skills` `verify-tests-fail-without-fix`. Load only if the method above is insufficient.
