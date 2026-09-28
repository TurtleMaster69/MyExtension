# F15 — CodeIssuesFinder per-open DTE re-enumeration (Architecture review backlog)

> **Lane: bugfix (unit-level RED, no VS boot).** F15 from `docs/progress.md`
> (Architecture review backlog, major): `CodeIssuesFinder.GatherHits`
> (`CodeIssuesFinder.cs:64-72`) calls `ProjectFiles.Enumerate(dte)` — a full DTE
> solution-tree walk — on EVERY open, even within the same session. Fix: cache the
> enumeration per session in a pure, dependency-free `ProjectFileCache` (Get +
> Invalidate), invalidated when the solution changes. Scoped to the caching part —
> the "lazy/async TODO scan" half of the finding is deferred (a separate concern).
> NO `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal change (M-M7 NOT
> triggered).

## Goal

Avoid re-enumerating the solution's project files on every CodeIssuesFinder open
within a session, by extracting a pure, unit-testable `ProjectFileCache`.

## Approach

1. **CREATE `Telescope/ProjectFileCache.cs`** — a pure, dependency-free class
   (namespace `Telescope`):
   - `internal sealed class ProjectFileCache`
   - `private IReadOnlyList<string>? _cached; private bool _dirty = true;`
   - `public IReadOnlyList<string> Get(Func<IReadOnlyList<string>> enumerate)` — if
     `_cached == null || _dirty`, `_cached = enumerate(); _dirty = false;` return `_cached`.
   - `public void Invalidate()` → `_dirty = true;`
2. **MODIFY `Telescope/CodeIssuesFinder.cs`**:
   - Add `private readonly ProjectFileCache _fileCache = new ProjectFileCache();` and
     `private string? _cachedSolutionName;`.
   - In `GatherHits` (the DTE branch): before enumerating, detect a solution change —
     `string? solutionName = dte?.Solution?.FullName; if (!string.Equals(_cachedSolutionName, solutionName, StringComparison.OrdinalIgnoreCase)) { _fileCache.Invalidate(); _cachedSolutionName = solutionName; }` — then
     `foreach (string path in _fileCache.Get(() => ProjectFiles.Enumerate(dte))) { CollectTodos(path, issues); }`.
   - The test-seam branch (`_testFileSource`) is unchanged.
3. **Tests** (in `tests/Telescope.Tests/Program.cs`, RED: `ProjectFileCache` doesn't exist → compile error):
   - `Run_ProjectFileCache_ReturnsCached` — `Get(() => { count++; return list; })` twice → the enumerator runs ONCE (count == 1), both calls return the same list.
   - `Run_ProjectFileCache_InvalidateReenumerates` — `Get` → `Invalidate()` → `Get` → the enumerator runs TWICE (count == 2).
   - `Run_ProjectFileCache_EmptyResultCached` — `Get` returns an empty list → cached (second `Get` does not re-enumerate).

## Acceptance criteria

- `Run_ProjectFileCache_*` (3) pass — RED before (compile error: `ProjectFileCache` doesn't exist), GREEN after.
- `dotnet run --project tests/Telescope.Tests` → **84 pass** (81 + 3); `dotnet run --project tests/NeoVisual.Tests` → **81 pass** (unchanged).
- No `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal change.
- Doc-ref diff gate: no NEW unresolved backticked refs.

## Known-RED allowlist

None.

## Verification Trace

| failing test | implicated steps | expected pass signal |
|---|---|---|
| `Run_ProjectFileCache_*` (RED: `ProjectFileCache` doesn't exist) | BP-1 | GREEN after the merge; the enumerator runs once per session |
| `tests/Telescope.Tests` (84) | BP-1 | all existing `Run_*` pass |
| `tests/NeoVisual.Tests` (81) | BP-1 | all existing `Run_*` pass (no NeoVisual code touched) |
| A3 log-literal check (`git diff`) | BP-1 | no `[Telescope]`/`[NeoVisual]`/`[Hook]` structured-log literal changed |

## Execution Log

(empty — populated by `neovim_hub` per attempt.)
