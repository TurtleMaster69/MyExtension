---
name: dotnet-code-review
description: Use when reviewing C#/.NET code for correctness, performance, conventions, or architectural drift in this net472 VSIX. Folds the official dotnet/runtime code-review standard + .NET perf anti-patterns. Deep reference at github.com/dotnet/runtime/.github/skills/code-review and dotnet/skills dotnet-diag.
license: MIT
compatibility: opencode
---
# .NET code review (pointer)

On-demand C# review standard for THIS repo (net472 VSIX, WPF overlay, Win32 hook, COM/DTE interop). Deep reference: `dotnet/runtime` `.github/skills/code-review` (43k+ maintainer-review-derived rules) + `dotnet/skills` `dotnet-diag/analyzing-dotnet-performance`.

## When to use
- Reviewing a C# change for correctness/perf/conventions (arch-auditor slice).
- Auditing a hot path or allocation-heavy pattern.

## Core method
- **Correctness** — disposal/async pitfalls, null/empty/boundary, race conditions, off-by-one. Check UI-thread affinity (`ThreadHelper.ThrowIfNotOnUIThread()`) on any `IVs*`/DTE call.
- **Performance** — apply the ~50 .NET perf anti-patterns (async, memory, strings, collections, LINQ, regex, I/O) with tiered severity; name the bottleneck before optimizing.
- **Conventions** — net472 (no modern BCL, hand-rolled `DistinctBy`, no `IReadOnlySet<T>`), `LangVersion` 14, `CardinalMovment` typo isolated, diagnostics-as-contract.
- **Architectural drift** — does the change follow the existing pattern (pure `OverlayKeyHandler`/`TextMotionNavigator` seam, controller `ActionKeys` contract) or introduce a new one?

## Source
Load `dotnet/runtime` code-review + `dotnet/skills` `analyzing-dotnet-performance` only if the method above needs the deep checklist.
