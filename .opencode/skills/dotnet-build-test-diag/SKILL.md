---
name: dotnet-build-test-diag
description: Use when dotnet build fails or is slow, tests are flaky or hard to unit-test, or a .NET/VSIX hot path may be the bottleneck. Folds the dotnet/skills msbuild+test+diag method. Deep reference at github.com/dotnet/skills.
license: Apache-2.0
compatibility: opencode
---
# .NET build / testability / diag (pointer)

On-demand method for THIS repo (net472 VSIX). Deep reference: `github.com/dotnet/skills`.

## When to use
- `dotnet build` fails or is slow (MSBuild failure diagnosis, build perf).
- A unit test needs VS/MEF/window context (wrong seam), or coverage is missing.
- A hot path is suspected (keyboard hook, fzf subprocess, preview re-tokenize, DTE enumeration).

## Core method
- **Build:** read the MSBuild diagnostic to the root cause (the first real error, not a downstream symptom). Before blaming a missing API, check the net472 / `LangVersion` 14 / `ExcludeAssets="runtime"` assumptions — this repo hand-rolls `DistinctBy` and has no `IReadOnlySet<T>`.
- **Testability:** extract the decision into a dependency-free class (the `OverlayKeyHandler`/`TextMotionNavigator` pattern) so it needs no VS/MEF context; assert on deterministic `[NeoVisual]`/`[Telescope]` log lines.
- **Perf:** characterize (measure) before optimizing; name the bottleneck (per-keystroke spawn, re-tokenize, re-enumeration) and its complexity before touching code.

## Source
For full reference (MSBuild failure/optimize, test filtering + coverage, diag recipes) load the `dotnet-msbuild` / `dotnet-test` / `dotnet-diag` skills from `github.com/dotnet/skills` — pull a reference file only when the task needs it.
