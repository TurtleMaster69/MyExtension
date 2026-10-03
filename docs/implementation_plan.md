# Plan — Telescope `fzf` finder (fuzzy content finder + fuzzy file finder)

> **Lane: feature (e2e enabled).** M-M7 HARD TRIGGER: this item adds new `[Telescope]`
> diagnostics (`fzf hits=...`, `fzf unavailable — literal fallback`, `opened fzf:
> file=... line=...`), so it is the full feature lane with e2e RED booting the VS
> Experimental Instance.
>
> **Source:** `docs/progress.md` "Next up" (item 5) + `docs/plans/backlog-plans.md`
> "Telescope `fzf` finder" (FEATURE-TRIAGE 2026-09-28, scope DECIDED 2026-09-28: **BUILD
> BOTH**). Every claim in the backlog sketch was re-verified against the current code
> (line refs in the sketch are stale; the current refs are cited below).
>
> **Ground truth:** repo GREEN (Code-review fixes 72 findings, 2026-10-02). Unit suites:
> `tests/Telescope.Tests` **157 passed**, `tests/NeoVisual.Tests` **168 passed** (verified live
> 2026-10-03; `docs/spec.md:248,260,410-411` already records 157/168). Live E2E:
> `tools/harness/test-e2e.ps1` lists **35 scenarios, no known-RED** (all GREEN).
> **Doc-sync flag:** `docs/progress.md:64-67` "Baseline" is STALE at 153/163 — the GREEN
> doc-sync step must update it to 157/168 (do not treat the stale number as a regression).
>
> **Research:** read `GrepFinder` (the query-driven model), `FzfFilter` (the fzf `--filter`
> engine), `FinderBase`/`IFinder`/`FinderEntry`, `ProjectFiles`/`FileContentCache`/
> `ProjectFileCache`/`GrepHit`/`HitOpener`/`FileLocation`, `TelescopeOverlay` (the
> `IsQueryDriven` seam + preview path), `TelescopeController`, `MyExtensionPackage`
> finder registration, `InputHandler.ResolveAction` + `Actions` + `TelescopeLauncher` +
> `default-keybindings.json`, the `telescope-grep` harness scenario + seed block, and the
> `Run_GrepFinder_*` unit tests. No Trailmark graph query was needed — this is a new
> additive finder mirroring an existing one; the only structural question (does the overlay
> await query-driven gathers?) was answered by reading `RefreshQueryDrivenAsync`.

> **Initial-plan review (APPROVE, 8 minor/nit findings) folded in 2026-10-03.** The
> findings are resolved inline: (1) both `GetCandidates`/`GetCandidatesAsync` short-circuit
> on empty query + an async empty-query test; (2) an `IFzfEngine` availability+filter seam
> for the hermetic ctor; (3) the sync non-empty branch throws `NotSupportedException`
> (dead at open); (4) `FzfFinder` implements `GatherHits()` (throw stub) + `OpenErrorNoun
> => "fzf"`; (5) the real ctor takes an `FzfFilter`-backed engine explicitly; (6) a shared
> `LiteralLineScanner` (or a citing comment) for the fallback; (7) the `spec.md:181-185`
> `ResolveAction` correction is in the doc-sync step; (8) `FzfLineMapper.Map` returns
> `IReadOnlyList<int>` line numbers. No Build Plan / BP-n steps yet — those come after RED.

## Goal

Add a **fuzzy content finder** (`FzfFinder`, Telescope library, `Name="Fzf"`, leader
`Space+F Z`) that searches the solution's project-file **contents** using **fzf's fuzzy
matching** (per-query re-gather like `GrepFinder`, `IsQueryDriven`), previews the hit line,
and opens the file at the line on Enter — emitting the new diagnostics
`[Telescope] fzf hits=...`, `[Telescope] fzf unavailable — literal fallback` (when fzf is
missing), and `[Telescope] opened fzf: file=... line=...`. Confirm the
existing `FileFinder` (`Space+F T`) already satisfies the **fuzzy file finder** requirement
(no new code).

## Approach

The new finder mirrors `GrepFinder`'s query-driven shape but replaces the literal
substring scan with fzf's fuzzy matcher. The overlay already owns the query-driven
debounce (`RefreshQueryDrivenAsync`, `TelescopeOverlay.cs:375-387`) and the preview path
(`LoadPreviewForSelection`, `:463-479`), so the finder only needs to gather + map + open.

### Resolved design decisions

**D1 — Sync/async seam: add `IFinder.GetCandidatesAsync(string query)` as an ABSTRACT
interface member, implemented once in `FinderBase<THit>` as
`Task.FromResult(GetCandidates(query))`; the overlay awaits it for query-driven finders.**
The overlay's query-driven path is currently synchronous (`RefreshQueryDrivenAsync` calls
`finder.GetCandidates(query)` on the UI thread after a 200 ms debounce). fzf is async
(`FzfFilter.FilterAsync` spawns a subprocess). Running fzf synchronously
(`.GetAwaiter().GetResult()`) would block the UI thread for the subprocess lifetime
(up to `FilterTimeoutMs` = 3000 ms) on every settled keystroke — unacceptable. Instead:
- Add `Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "")` to `IFinder`
  as an **abstract** member (NOT a default interface method — C# 8 default interface
  *implementations* are NOT supported on net472; invoking one throws
  `MissingMethodException`/CS8701, so the default-method form is a compile/runtime
  failure and is explicitly rejected).
- Implement it **once** in `FinderBase<THit>`:
  `public virtual Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "") =>
  Task.FromResult(GetCandidates(query));`. Every current finder derives from
  `FinderBase<THit>` (`FileFinder`, `CodeIssuesFinder`, `ReferencesFinder`,
  `ImplementationFinder`, `GrepFinder`), so none needs a source change.
- `FzfFinder` overrides `GetCandidatesAsync` to `await _fzf.FilterAsync(...)`.
- `RefreshQueryDrivenAsync` changes `finder.GetCandidates(query)` →
  `await finder.GetCandidatesAsync(query)`. The `await` captures the WPF
  `SynchronizationContext`, so the continuation (and the subsequent `RenderResults()`)
  resumes on the UI thread — the same guarantee the current code relies on for the
  debounce. The existing generation counter (`_queryGeneration`) already invalidates a
  stale gather, so an in-flight fzf run that finishes after the query changed is discarded.
- The debounce stays as-is (200 ms) — it now also coalesces fzf spawns.

**D2 — Mapping fzf-matched lines back to (file, line), duplicate-safely: per-file fzf
filtering with a stable per-file index, via a pure `FzfLineMapper` seam.**
fzf `--filter` echoes the matched input lines verbatim, so identical line text in two
files is ambiguous. Rather than a global candidate list (which would need `--nth`/`--with-nth`
id fields and still risk collisions), `FzfFinder` filters **one file at a time**: for each
project file it feeds that file's lines to `FzfFilter.FilterAsync`, and the returned
matched lines are mapped back to line numbers by a **pure, dependency-free**
`FzfLineMapper.Map(string[] fileLines, IReadOnlyList<string> matchedLines)` that consumes
each matched string against the next unconsumed identical line in that file (the same
ordinal-consumption strategy `ResultMapper` uses for duplicate display strings). Because
the file is known at the call site, the (file, line) pair is unambiguous even when two
files contain the same line text. The mapper is hermetic-testable (no fzf, no DTE).
- **Return type (finding 8):** `Map` returns `IReadOnlyList<int>` — the **1-based line
  numbers** of the matched lines, in matched order (NOT `(line, text)` pairs). The caller
  (`FzfFinder`) recovers the line text from `fileLines[line - 1]` when building the
  `FzfHit`; a matched string with no remaining unconsumed identical line is skipped (not
  emitted). This keeps the pure seam minimal and the `Run_FzfLineMapper_*` tests assert
  line numbers directly.
- Per-file filtering also bounds the fzf input size (one file's lines, not the whole
  solution) and keeps the hit cap meaningful.
- `FzfFinder` caps total hits at 200 (mirroring `GrepFinder.HitCap`) and stops scanning
  once the cap is reached.

**D2b — `FzfFinder` ALSO overrides the SYNC `GetCandidates(string query)` (the overlay-open
path).** `TelescopeOverlay.ShowOverlayAsync` calls the synchronous `finder.GetCandidates()`
at open (`TelescopeOverlay.cs:258`), NOT the async seam. If `FzfFinder` only overrode
`GetCandidatesAsync`, the open path would fall through to `FinderBase.GetCandidates` →
`GatherHits()` → `NotSupportedException` → caught by `FinderBase` → logs
`FzfFinder failed to enumerate: ...` and returns empty — contradicting AC1/AC7. So
`FzfFinder` overrides the sync `GetCandidates(string query)` with the **empty-query
short-circuit** (mirroring `GrepFinder.GetCandidates`, `GrepFinder.cs:64-74`): empty query
→ warm the content cache and return `Array.Empty<FinderEntry>()` with NO gather log and NO
failure log. A unit test asserts the open path emits no `FzfFinder failed to enumerate`
line (AC7).

- **BOTH overrides short-circuit on empty query (finding 1).** The overlay calls
  `GetCandidatesAsync` on **every** prompt change, including a cleared prompt
  (`RefreshQueryDrivenAsync`, `TelescopeOverlay.cs:375-387`), so the async override must
  NOT spawn fzf for an empty query. `FzfFinder.GetCandidatesAsync(string query)` begins with
  `if (string.IsNullOrEmpty(query)) return Task.FromResult(GetCandidates(query));` — i.e. it
  **delegates to the sync short-circuit** (warm cache + empty list, no gather log, no fzf
  spawn). Only a non-empty query proceeds to the per-file fzf gather. The
  `Run_FzfFinder_EmptyQuery*` tests must exercise the **async** path
  (`await finder.GetCandidatesAsync("")`), not only `GetCandidates("")`, so the
  cleared-prompt behavior is pinned.
- **Sync non-empty branch (finding 3).** A sync method cannot `await` fzf, so the sync
  `GetCandidates(query)` non-empty branch is `throw new NotSupportedException("FzfFinder is
  query-driven; call GetCandidatesAsync(query)")` — mirroring `GrepFinder.GatherHits`
  (`GrepFinder.cs:62`). This branch is **dead at open** (the overlay-open path always passes
  an empty query, `TelescopeOverlay.cs:258`); it exists only so a future non-empty sync call
  fails loudly instead of silently returning the wrong (unfiltered) list. The sync override
  is therefore: empty → short-circuit; non-empty → `NotSupportedException`.

**D2c — fzf-unavailable fallback: literal substring scan, NOT the full list.**
`FzfFilter.FilterAsync` returns the ENTIRE candidate list when `_availability == false`
(`FzfFilter.cs:133-136`) or on any exception (`:220-225`). Per-file, that would make every
line of every file a "hit" → `fzf hits={count}` becomes the full line count (capped at 200),
not a fuzzy match — a silent wrong result. `FzfFinder` therefore does NOT rely on
`FzfFilter`'s full-list fallback: before filtering it checks availability
(`await _fzf.IsAvailableAsync()`), and when fzf is unavailable it falls back to a **literal
case-insensitive substring scan** of the file's lines (the `GrepFinder.ScanFile` semantics,
`GrepFinder.cs:208-230`) and logs `[Telescope] fzf unavailable — literal fallback` once per
gather. This keeps `fzf hits={n}` meaningful (a real match count) and the feature usable
without fzf. A unit test drives the unavailable path (injected fzf seam returning
unavailable) and asserts the literal fallback hits + the diagnostic.

- **Availability seam for the hermetic test ctor (finding 2).** The test ctor must be able
  to drive "fzf unavailable" without a real subprocess. Introduce a small seam interface
  (or a pair of delegates) that exposes BOTH `IsAvailableAsync()` and `FilterAsync(...)`.
  **Decision:** define `internal interface IFzfEngine { Task<bool> IsAvailableAsync();
  Task<IReadOnlyList<string>> FilterAsync(IEnumerable<string> candidates, string query,
  CancellationToken ct); }` in `Telescope/Filter/` (or nested in `FzfFinder.cs`), with
  `FzfFilter` wrapped by a thin adapter (or `FzfFilter` itself implementing it — it already
  has both methods with matching signatures). The hermetic test ctor takes an `IFzfEngine`
  (a fake returning `IsAvailableAsync() => Task.FromResult(false)` drives the fallback);
  the real ctor takes the `FzfFilter`-backed engine. This replaces the previously-sketched
  bare `Func<...>` fzf seam, which could not express availability.
- **Literal fallback duplication (finding 6).** The literal fallback re-implements
  `GrepFinder.ScanFile` semantics (`GrepFinder.cs:208-230`). **Decision:** extract a shared
  `LiteralLineScanner` helper (pure, dependency-free: `IReadOnlyList<GrepHit>
  Scan(string path, string[] lines, string query, int cap)` or equivalent) used by BOTH
  `GrepFinder.ScanFile` and `FzfFinder`'s fallback, so the case-insensitive substring +
  hit-cap semantics have a single source of truth. If extraction proves to churn
  `GrepFinder` more than the feature warrants, the fallback may instead **intentionally
  duplicate** the ~10-line scan with a comment citing `GrepFinder.ScanFile` — but the
  preferred path is the shared helper (the build-agent must not silently duplicate without
  the comment).
- **Residual (out of scope):** a filter-time exception when availability was true is
  swallowed by `FzfFilter` (it returns the full list) and cannot be distinguished by
  `FzfFinder` without changing `FzfFilter`; that path is bounded by the hit cap and already
  logged by `FzfFilter`'s existing `fzf filter failed: {msg}` line. `FzfFilter` is reused
  unchanged (the finding's required fix is the unavailable path).

**D3 — Leader key + action: `Space+F Z` → `telescope-fzf` → finder `Fzf`.**
- `default-keybindings.json`: add `"F,Z": "telescope-fzf"`.
- `TelescopeLauncher.FinderNames`: add `["telescope-fzf"] = "Fzf"`. Because `Actions`
  derives its telescope registry entries from `FinderNames` (`Actions.cs:33-38`), this
  single line wires the action — no `ResolveAction` case is needed (the existing
  `Actions.Resolve` path handles it). `InputHandler.ResolveAction` already delegates to
  `Actions.Resolve` (`InputHandler.cs:205`), so no change there.
- `MyExtensionPackage` "finders" step: register the real `FzfFinder` alongside the other
  finders (`MyExtensionPackage.cs:100-108`). **Ctor shape (finding 5):** the real ctor
  creates/accepts the fzf engine, e.g.
  `new FzfFinder(() => VsServices.Dte(this)!, fileCache, new FzfFilter())` — the third arg
  is the `IFzfEngine` (finding 2) that `FzfFinder` uses for `IsAvailableAsync`/`FilterAsync`.
  The plan must show this explicitly so the registration line is unambiguous; the
  `FzfFilter` is NOT obtained implicitly.
- **`FzfFinder` must implement `GatherHits()` and override `OpenErrorNoun` (finding 4).**
  `FinderBase<THit>.GatherHits()` is abstract (`FinderBase.cs:21`), so `FzfFinder` must
  provide it: `protected override IReadOnlyList<FzfHit> GatherHits() => throw new
  NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)");`
  (mirroring `GrepFinder.GatherHits`, `GrepFinder.cs:62`). `OpenErrorNoun` must be
  `"fzf"` (mirroring `GrepFinder.OpenErrorNoun`, `GrepFinder.cs:206`) so a failed open logs
  `open fzf failed: {msg}`. Both are added to the `FzfFinder.cs` bullet in "Files to be
  touched".

**D4 — Exact diagnostics contract (new `[Telescope]` lines).**
- `[Telescope] fzf hits={count}` — emitted once per settled query by
  `FzfFinder.GetCandidatesAsync` (the per-query gather summary), mirroring
  `grep hits={count}`. Empty query → no gather log (deterministic empty initial state,
  matching `GrepFinder`).
- `[Telescope] fzf unavailable — literal fallback` — emitted once per gather when fzf is
  unavailable and `FzfFinder` falls back to the literal substring scan (D2c). Exact text
  (em-dash, matching the existing `fzf unavailable — showing unfiltered list` line at
  `TelescopeOverlay.cs:273`).
- `[Telescope] opened fzf: file={path} line={line}` — emitted by `FzfFinder.OpenHit` after
  a successful open, mirroring `opened grep: file=... line=...`.
- The generic `[Telescope] open finder=Fzf candidates=0` line is emitted by the overlay
  (`TelescopeOverlay.cs:269`) and is asserted for the empty-query open.
- Preview reuses the existing `[Telescope] preview file=...` / `preview caret=... line=...`
  lines (the `FzfHit` payload is an `IFileLocation`, so `LoadPreviewForSelection` →
  `PreviewRenderer.Show` handles it with no overlay change).

**D5 — Whether B (fuzzy file finder) needs any code: NO.**
`FileFinder` (`Name="Files"`, `Space+F T`) gathers the solution's file paths once at open
and the overlay filters them with `FzfFilter.FilterAsync` (the non-query-driven path,
`TelescopeOverlay.cs:389-422`). That is exactly "file names filtered by fzf fuzzy
matching". No new finder, no code change. The proof is the existing `telescope-open-file*`
e2e scenarios (they type a query and open the fzf-filtered row) — **no new unit test**:
the previously-planned `Run_FileFinder_IsFuzzyFilteredByOverlay` asserted
`FileFinder.IsQueryDriven == false`, a constant inherited from `FinderBase.cs:19`, so it
was tautological and is dropped. (The fzf filtering lives in the overlay, not in
`FileFinder`, so there is no observable `FileFinder` behavior to assert hermetically.)

### Constraints honored

- **net472:** no `IReadOnlySet<T>`, no `ProcessStartInfo.ArgumentList`; `FzfFilter` is
  reused unchanged (its `QuoteArg` is already net472-safe).
- **UI-thread affinity:** `FzfFinder.GetCandidatesAsync` asserts
  `ThreadHelper.ThrowIfNotOnUIThread()` on the DTE-enumeration path (the fzf subprocess
  itself runs off-thread inside `FzfFilter`); `OpenHit` asserts it before touching DTE.
- **Log-line-as-contract:** the three new diagnostics (`fzf hits=`, `fzf unavailable —
  literal fallback`, `opened fzf:`) are specified byte-exactly above.
- **Pure seam:** `FzfLineMapper` is dependency-free and unit-tested (`Map` →
  `IReadOnlyList<int>` 1-based line numbers); `FzfFinder` has hermetic test ctors
  (injected enumerate + opener + an `IFzfEngine` availability/filter seam) mirroring
  `GrepFinder`.

## Acceptance criteria (each mapped to a diagnostic + a test)

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `Space+F Z` opens the `Fzf` finder with an empty query → 0 candidates | `[Telescope] open finder=Fzf candidates=0` | e2e `telescope-fzf` step 1 |
| AC2 | Typing a query fuzzy-matches file contents and reports the hit count | `[Telescope] fzf hits={n}` | e2e `telescope-fzf` step 2; unit `Run_FzfFinder_*` |
| AC3 | The preview shows the first hit's file and jumps the caret to its line | `[Telescope] preview file=...` / `preview caret=... line=...` | e2e `telescope-fzf` step 3 |
| AC4 | Enter opens the hit file at the hit line | `[Telescope] opened fzf: file=... line=...` | e2e `telescope-fzf` step 4; unit `Run_FzfFinder_OnSelectedOpensHitAtLine` |
| AC5 | Duplicate line text in different files maps to the correct (file, line) | (unit-only — pure logic, no diagnostic) | unit `Run_FzfLineMapper_DuplicateLinesMapOrdinal` |
| AC6 | The fuzzy file finder (B) is the existing `FileFinder` filtered by fzf | (existing `opened file: ...`) | e2e `telescope-open-file*` stay GREEN (no unit test — see D5) |
| AC7 | Empty query is a clean empty (no gather log, no failure log) on BOTH the sync open path AND the async cleared-prompt path | (absence of `fzf hits=` / `FzfFinder failed to enumerate`; async path does not call `IFzfEngine.FilterAsync`) | unit `Run_FzfFinder_EmptyQueryCleanEmptyNoFailureLog`; unit `Run_FzfFinder_OpenPathNoFailureLog`; unit `Run_FzfFinder_EmptyQueryAsyncShortCircuits` |
| AC8 | The hit cap bounds the result set | (unit-only — pure logic, no diagnostic) | unit `Run_FzfFinder_HitCapBounded` |
| AC9 | fzf unavailable/error falls back to a literal substring scan (not the full list) | `[Telescope] fzf unavailable — literal fallback` | unit `Run_FzfFinder_UnavailableFallsBackToLiteralScan` (drives `IFzfEngine.IsAvailableAsync() => false`) |

> **Pure-logic criteria (AC5, AC8) have NO diagnostic by design** — they assert the
> `FzfLineMapper`/hit-cap state machine directly. The verification-agent must NOT treat the
> missing log line as a coverage gap.

## E2E test plan

**New scenario: `telescope-fzf`** (added to `tools/harness/test-e2e.ps1` via
`Register-Scenario`, modeled on `telescope-grep` at `:1335-1360`). It depends on the new
seed marker (below) and the new diagnostics.

Seed addition (in `$script:SeedCanonical`, `test-e2e.ps1:345-377`): a `FzfProbe.cs` file
with a distinctive fuzzy token on a pinned line. Use PowerShell backtick escapes
(`` `r`n ``), NOT literal `\r\n` (a double-quoted `"...\r\n..."` is a literal backslash-r,
not CRLF — the existing seed at `test-e2e.ps1:346-376` uses `` `r`n ``):

```powershell
'FzfProbe.cs' = "// FzfProbe.cs`r`nclass FzfProbe`r`n{`r`n    // FUZZYPROBE first hit line 4`r`n    int alpha = 1;`r`n}`r`n"
```

The token `FUZZYPROBE` appears on exactly ONE line (line 4), so `fzf hits=1` is exact and
line 4 pins the preview + opened-line assertions. (A single-hit marker keeps the assertion
deterministic; fzf's `--no-sort` output is input order, so a second hit would be stable too,
but a single hit keeps the count assertion exact.)

Scenario body (copy-pasteable; mirrors `telescope-grep` at `test-e2e.ps1:1335-1360` —
`-Vs`/`-LogPath` on `Open-TelescopeFinder`, `Start-Sleep` after Enter, args on
`Close-Telescope`):

```powershell
Register-Scenario 'telescope-fzf' {
    param($vs, $logPath)
    Reset-LogBaseline $logPath
    Open-TelescopeFinder -Vs $vs -LogPath $logPath -Key 'F,Z' -Finder 'Fzf'
    Assert-OverlayFocused $vs

    # Step 1: empty query -> deterministic 0 candidates.
    Assert-NewLogLine $logPath "$($script:PfxTel)open finder=Fzf candidates=0" 'fzf finder opened with empty query -> 0 candidates'

    # Step 2: type the token; the debounce re-runs the query-driven gather after typing settles.
    Send-Text 'FUZZYPROBE'
    Assert-NewLogLine $logPath "$($script:PfxTel)fzf hits=1$" 'fzf fuzzy-matched the 1 seeded FUZZYPROBE line'

    # Step 3: preview jumps to the hit's file/line (FzfProbe.cs, line 4).
    Assert-NewLogLine $logPath "$($script:PfxTel)preview file=.*FzfProbe\.cs" 'preview shows the fzf hit file'
    Assert-NewLogLine $logPath "$($script:PfxTel)preview caret=\d+ line=4" 'preview caret jumped to the hit line'

    # Step 4: Enter opens the file at the pinned hit line (line 4).
    Send-Tap $script:VkEnter; Start-Sleep -Milliseconds 800
    Assert-NewLogLine $logPath "$($script:PfxTel)opened fzf: file=.*FzfProbe\.cs line=4" 'Enter opened the fzf hit at line 4'

    # Step 5: close.
    Close-Telescope $vs $logPath
}
```

**Existing scenarios that must stay GREEN:** all 35, in particular `telescope-open`,
`telescope-search`, `telescope-navigate`, `telescope-open-file*`, `telescope-grep`
(the query-driven path is shared — the `GetCandidatesAsync` change must not regress it),
`telescope-preview`, and `seed-leak` (the new seed file must be added to the canonical map
so the seed-consistency + leak guards stay GREEN).

## Offline unit tests to extend

**Project: `tests/Telescope.Tests`** (the Telescope library logic; `NeoVisual.Tests` is
untouched — no `[NeoVisual]` behavior changes).

New tests (modeled on `Run_GrepFinder_*`, `Program.cs:2287-2495`):
- `Run_FzfLineMapper_ExactLineMapsToLineNumber` — a matched line maps to its 1-based line.
- `Run_FzfLineMapper_DuplicateLinesMapOrdinal` — two identical lines in one file map to
  their two distinct line numbers in order (AC5).
- `Run_FzfLineMapper_UnknownLineSkipped` — a matched string not present in the file is
  skipped (no bogus line).
- `Run_FzfFinder_EmptyQueryReturnsZeroCandidates` — empty query → 0 candidates (AC1/AC7).
- `Run_FzfFinder_EmptyQueryCleanEmptyNoFailureLog` — empty query logs no `fzf hits=` and
  no failure line (AC7).
- `Run_FzfFinder_EmptyQueryAsyncShortCircuits` — **the async path** (`await
  finder.GetCandidatesAsync("")`) returns 0 candidates, logs no `fzf hits=`, and does NOT
  invoke the injected `IFzfEngine.FilterAsync` (finding 1: the cleared-prompt path must not
  spawn fzf). The fake engine records whether `FilterAsync` was called; assert it was not.
- `Run_FzfFinder_FuzzyMatchReportsHits` — a query fuzzy-matching a line yields the hit and
  logs `fzf hits=1` (AC2).
- `Run_FzfFinder_DisplayIsFileNameLineText` — deterministic `{fileName}:{line}: {lineText}`.
- `Run_FzfFinder_PayloadRoundTripsFzfHit` — the `FzfHit` payload round-trips file/line/text.
- `Run_FzfFinder_OnSelectedOpensHitAtLine` — `OnSelected` invokes the opener with the hit (AC4).
- `Run_FzfFinder_HitCapBounded` — the result set is bounded by the cap (AC8).
- `Run_FzfFinder_CacheEnumeratesOnce` — the shared `ProjectFileCache` serves the enumerate
  delegate once across two queries (mirrors `Run_GrepFinder_CacheEnumeratesOnce`).
- `Run_FzfFinder_QueryDrivenBehavior` — `IsQueryDriven == true`; empty query short-circuits.
- `Run_FzfFinder_OpenPathNoFailureLog` — the sync `GetCandidates("")` open path returns 0
  candidates and logs NO `FzfFinder failed to enumerate` line (AC7; guards D2b).
- `Run_FzfFinder_UnavailableFallsBackToLiteralScan` — with the injected `IFzfEngine`
  reporting `IsAvailableAsync() => false`, a query falls back to the literal substring scan,
  reports the real hit count, and logs `fzf unavailable — literal fallback` (AC9; guards
  D2c). The fake engine's `FilterAsync` must NOT be called on the unavailable path.

The hermetic `FzfFinder` test ctor injects the file enumerate + opener + an **`IFzfEngine`**
(finding 2) so the tests need no real fzf subprocess and no DTE, and can drive both
availability states. The real ctor takes the `FzfFilter`-backed engine (finding 5).

## Known-RED allowlist

**None.** All 35 e2e scenarios are GREEN and both unit suites pass (157 / 168). The
verification-agent must NOT flag any pre-existing scenario as a regression. The only
expected RED is the new `telescope-fzf` scenario + the new `Run_FzfFinder_*` /
`Run_FzfLineMapper_*` tests before the feature exists (the RED evidence for the Build Plan).

## Files to be touched (initial estimate)

- **New:** `Telescope/Finders/FzfFinder.cs` (implements `GatherHits()` as a
  `NotSupportedException` stub and overrides `OpenErrorNoun => "fzf"` — finding 4),
  `Telescope/Finders/Utils/FzfHit.cs`, `Telescope/Finders/Utils/FzfLineMapper.cs`
  (`Map` returns `IReadOnlyList<int>` 1-based line numbers — finding 8),
  `Telescope/Filter/IFzfEngine.cs` (the availability+filter seam — finding 2), and
  `Telescope/Finders/Utils/LiteralLineScanner.cs` (shared literal scan used by both
  `GrepFinder.ScanFile` and the `FzfFinder` fallback — finding 6; if not extracted, the
  fallback duplicates `GrepFinder.ScanFile` with a citing comment).
- **Modified:** `Telescope/Finders/TelescopeFinder.cs` (declare the **abstract**
  `IFinder.GetCandidatesAsync`), `Telescope/Finders/FinderBase.cs` (implement
  `GetCandidatesAsync` as `Task.FromResult(GetCandidates(query))`),
  `Telescope/Finders/GrepFinder.cs` (BP-4: `ScanFile` delegates to the shared
  `LiteralLineScanner`), `Telescope/Overlay/TelescopeOverlay.cs` (`RefreshQueryDrivenAsync` awaits
  `GetCandidatesAsync`), `MyExtension/Package/MyExtensionPackage.cs` (register `FzfFinder`),
  `MyExtension/Package/Utils/TelescopeLauncher.cs` (`FinderNames` entry),
  `MyExtension/Resources/default-keybindings.json` (`F,Z`), `tools/harness/test-e2e.ps1`
  (seed + `telescope-fzf` scenario), `tests/Telescope.Tests/Program.cs` (new tests),
  `tests/NeoVisual.Tests/Program.cs` (BP-13: update the stale `Actions.Registry` count 10→11 and
  add `telescope-fzf` to the asserted names in `Run_ActionsRegistry_ContainsAllBuiltins`),
  `docs/spec.md` (finder table + diagnostics + keybindings + counts), `docs/progress.md` (item state
  + stale Baseline 153/163 → 157/168), `AGENTS.md` (finder list + the three new `[Telescope]`
  diagnostics + the `Space+F Z` keybinding + counts),
  `.opencode/skills/vs-extension-dev/SKILL.md` (BP-12b: test/scenario counts).
- **Doc-sync correction (finding 7):** `docs/spec.md:181-185` still says "To add a *new
  built-in action*, add a case in `ResolveAction` and a line in `default-keybindings.json`."
  This is STALE — D3 wires the action via the `FinderNames`-derived registry
  (`Actions.cs:33-38`), with no `ResolveAction` case. The doc-sync step must correct
  `spec.md:181-185` to state that telescope actions are derived from
  `TelescopeLauncher.FinderNames` (add a `FinderNames` entry + a `default-keybindings.json`
  line), and that `ResolveAction` cases are only for non-telescope built-ins. Also add
  `telescope-fzf` to the `spec.md:181-183` action list and `Space+F Z` to the
  `spec.md:187-193` defaults list.
- **Not touched:** `FzfFilter.cs` (reused as-is; the availability check + literal fallback
  live in `FzfFinder`, D2c), `FileFinder.cs` (B needs no code),
  `InputHandler.cs` / `Actions.cs` (the `FinderNames`-derived registry wires the action).

## Open risks / uncertainty

1. **fzf output order.** `FzfFilter.FilterAsync` passes `--no-sort` (`FzfFilter.cs:142`),
   so fzf's `--filter` output is **input order** (stable), NOT score-based. The e2e
   scenario still pins a **single-hit** marker so the count (`fzf hits=1`) and line are
   exact; a future seed with a second hit would be order-stable but the count assertion
   would need updating.
2. **Per-file fzf spawn cost.** Filtering one file at a time spawns one fzf process per
   file per settled query. For the scratch solution (~15 files) this is fine; for a large
   solution it could be slow. Mitigation: the 200 ms debounce coalesces keystrokes, the
   `ProjectFileCache`/`FileContentCache` avoid re-walking/re-reading, and the hit cap stops
   early. If profiling shows a stall, a follow-up can batch files with an id-prefixed
   candidate list (`--nth`/`--with-nth`) — noted, not planned.
3. **`GetCandidatesAsync` on net472 — RESOLVED (not a risk).** C# 8 default interface
   *implementations* are NOT supported on net472 (CS8701 / `MissingMethodException` at
   runtime), so the default-method form is rejected. **Decision (D1):** declare
   `GetCandidatesAsync` **abstract** on `IFinder` and implement it once in
   `FinderBase<THit>` as `Task.FromResult(GetCandidates(query))` — every current finder
   derives from `FinderBase`, so none needs a source change. The build-agent must NOT
   second-guess this: no default interface method.
4. **Preview for a hit whose file changed on disk.** `PreviewRenderer` re-reads on mtime
   change; the `FzfHit` line number is from the gather-time content. A concurrent edit could
   shift the line — acceptable (same as `GrepFinder`).

## Build Plan

> **RED evidence (2026-10-03).** `tests/Telescope.Tests` fails to build (exit 1):
> `Program.cs(2510,46): CS0246 'IFzfEngine'`; with a temporary stub, `CS0103 'FzfLineMapper'`
> (2545/2558/2571), `CS0246 'FzfFinder'` (2583+), `CS0246 'FzfHit'` (2693/2708), and
> `CS0019`/`CS1061` on `FzfHit?` (2713-2716). E2E `telescope-fzf` FAILs:
> `Telescope finder 'Fzf' did not open` (exit 1); post-baseline log has 0 lines matching
> `open finder=|fzf hits=|opened fzf|leader-binding executed`, and
> `[NeoVisual] Keybindings loaded: 27 binding(s), leader = Space` (no `F,Z`). Root cause: the
> `telescope-fzf` action/finder is not registered (`TelescopeLauncher.FinderNames` +
> `default-keybindings.json` + package registration absent).
>
> **The harness scenario + seed ALREADY EXIST** (`tools/harness/test-e2e.ps1:1373-1396`
> `Register-Scenario 'telescope-fzf'`; `:373` `'FzfProbe.cs'` seed) — added by the
> e2e-test-builder. Do NOT re-add them; BP-8/BP-9 make them pass.
>
> **Known-RED allowlist: none.** All 35 existing e2e scenarios are GREEN; unit suites 157/168.
> The only RED is the new `telescope-fzf` scenario + the new `Run_FzfFinder_*` /
> `Run_FzfLineMapper_*` tests before this feature exists. The verification-agent must NOT flag
> the pre-fix RED state as a regression.
>
> **Iteration 1 (VERIFY re-plan, 2026-10-03) — real regression, DEVIATION adjudicated ACCEPT.**
> The build was GREEN on the feature (full 36-scenario e2e PASS incl. `telescope-fzf` /
> `telescope-grep` / `seed-leak`; `Telescope.Tests` 172/172) but `tests/NeoVisual.Tests`
> FAILED: `Run_ActionsRegistry_ContainsAllBuiltins` (`Program.cs:1597`) asserted
> `Actions.Registry.Count == 10` and got **11**. Root cause: BP-8 legitimately added
> `["telescope-fzf"] = "Fzf"` to `TelescopeLauncher.FinderNames` (`TelescopeLauncher.cs:34`),
> and `Actions.BuildRegistry()` derives the telescope registry entries from `FinderNames`
> (`Actions.cs:33-38`), so the registry grew 10→11. The pre-existing count assertion (and its
> `names` array at `Program.cs:1598-1603`) was never updated. **Hub adjudication: the
> `FinderNames` addition is a legitimate new builtin action → ACCEPT; the fix is to update the
> stale test, NOT to revert the registry change.** Classification: deterministic regression,
> not on the known-RED allowlist, not flaky. Fixed by **BP-13** (below). This is the only
> change in iteration 1; all other BP steps are unchanged and already GREEN.

### Phase 1 — Core seams & models (unit-testable, no VS)

**BP-1 — `IFzfEngine` seam + `FzfFilter` implements it.**
- **Files:** `Telescope/Filter/IFzfEngine.cs` (new); `Telescope/Filter/FzfFilter.cs` (modify: class declaration only).
- **Usings (new file):** `IFzfEngine.cs` MUST declare `using System.Collections.Generic;`,
  `using System.Threading;`, `using System.Threading.Tasks;` (the csproj has NO `<ImplicitUsings>`,
  so `Task`/`IReadOnlyList`/`CancellationToken` are CS0246 without them). `FzfFilter.cs` already has
  all three.
- **Change:** Add `internal interface IFzfEngine` in namespace `Telescope.Filter` with exactly
  `Task<bool> IsAvailableAsync();` and
  `Task<IReadOnlyList<string>> FilterAsync(IEnumerable<string> candidates, string query, CancellationToken ct);`.
  Change `internal sealed class FzfFilter` → `internal sealed class FzfFilter : IFzfEngine`.
  `FzfFilter`'s existing `public async Task<bool> IsAvailableAsync()` and
  `public async Task<IReadOnlyList<string>> FilterAsync(IEnumerable<string>, string, CancellationToken)`
  already match the interface exactly — NO other change to `FzfFilter` (behavior untouched; the
  availability check + literal fallback live in `FzfFinder`, D2c).
- **Verify-with:** `dotnet build` compiles; the test double `FakeFzfEngine : IFzfEngine`
  (`Program.cs:2510`) resolves. Unit `Run_FzfFinder_EmptyQueryAsyncShortCircuits` (asserts
  `engine.FilterCalls == 0`).
- **Fails-if:** `CS0246 'IFzfEngine'` persists; or `FzfFilter` no longer compiles (interface
  member names/params must match byte-for-byte).

**BP-2 — `FzfHit` hit model.**
- **Files:** `Telescope/Finders/Utils/FzfHit.cs` (new).
- **Usings (new file):** none required — `string` is a keyword and the base `FileLocation` is in the
  same `Telescope.Finders` namespace. (Do NOT add unused usings; the build treats warnings as
  non-fatal but the repo keeps files clean.)
- **Change:** `public sealed class FzfHit : FileLocation` in `Telescope.Finders`; ctor
  `(string filePath, int lineNumber, string lineText) : base(filePath, lineNumber)`; `public string
  LineText { get; }` (null-coalesced to `string.Empty`). Mirrors `GrepHit`.
- **Verify-with:** Unit `Run_FzfFinder_PayloadRoundTripsFzfHit` (FilePath/LineNumber/LineText
  round-trip); `Run_FzfFinder_OnSelectedOpensHitAtLine`.
- **Fails-if:** `CS0246 'FzfHit'`; or `FzfHit?` member access fails (`CS1061`).

**BP-3 — `FzfLineMapper` (pure).**
- **Files:** `Telescope/Finders/Utils/FzfLineMapper.cs` (new).
- **Usings (new file):** `using System;` (for `StringComparison.Ordinal`),
  `using System.Collections.Generic;` (for `IReadOnlyList<int>`). No `<ImplicitUsings>` — both are
  required.
- **Change:** `internal static class FzfLineMapper` in `Telescope.Finders`;
  `public static IReadOnlyList<int> Map(string[] fileLines, IReadOnlyList<string> matchedLines)`.
  Ordinal duplicate-safe consumption: for each matched string, scan `fileLines` for the first
  index not yet consumed with `string.Equals(fileLines[i], m, StringComparison.Ordinal)`, mark
  consumed, add `i + 1` (1-based); a matched string with no remaining unconsumed identical line is
  skipped. Dependency-free (no fzf, no DTE).
- **Verify-with:** Unit `Run_FzfLineMapper_ExactLineMapsToLineNumber` (→[2]);
  `Run_FzfLineMapper_DuplicateLinesMapOrdinal` (→[1,3]); `Run_FzfLineMapper_UnknownLineSkipped` (→[]).
- **Fails-if:** duplicate lines both map to the first occurrence; unknown line emits a bogus
  number; return type is not `IReadOnlyList<int>`.

**BP-4 — `LiteralLineScanner` shared helper + `GrepFinder.ScanFile` delegates to it.**
- **Files:** `Telescope/Finders/Utils/LiteralLineScanner.cs` (new); `Telescope/Finders/GrepFinder.cs` (modify `ScanFile`).
- **Usings (new file):** `using System;` (for `StringComparison.OrdinalIgnoreCase`),
  `using System.Collections.Generic;` (for `IReadOnlyList<int>`). `GrepFinder.cs` already has both.
- **Change:** `internal static class LiteralLineScanner` in `Telescope.Finders`;
  `public static IReadOnlyList<int> Scan(string[] lines, string query, int cap)` — case-insensitive
  `IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0`, returns 1-based line numbers, stops at
  `cap`. Refactor `GrepFinder.ScanFile` to call `LiteralLineScanner.Scan(lines, query, HitCap -
  hits.Count)` and add `new GrepHit(path, ln, lines[ln - 1])` for each returned line number
  (behavior identical to the current inline loop). `FzfFinder`'s unavailable fallback (BP-6b) uses
  the same helper.
- **Verify-with:** Existing `Run_GrepFinder_LineScanMatchesCaseInsensitive` (2 hits),
  `Run_GrepFinder_HitCapBounded` (≤200), `Run_GrepFinder_DisplayIsFileNameLineText`
  ("A.cs:2: NEEDLE here") stay GREEN; e2e `telescope-grep` stays GREEN.
- **Fails-if:** any `Run_GrepFinder_*` regresses; `telescope-grep` e2e goes RED (the refactor
  changed the cap/scan semantics).

**BP-5 — `IFinder.GetCandidatesAsync` (abstract) + `FinderBase<THit>` implementation.**
- **Files:** `Telescope/Finders/TelescopeFinder.cs` (modify `IFinder`); `Telescope/Finders/FinderBase.cs` (modify).
- **Change:** Add to `IFinder`: `Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "");`
  (ABSTRACT — NOT a default interface method; net472 rejects C# 8 default implementations). Add
  `using System.Threading.Tasks;` to `TelescopeFinder.cs`. **Also add `using System.Threading.Tasks;`
  to `FinderBase.cs`** — it currently has NO such using (`FinderBase.cs:1-6`) and the csproj has no
  `<ImplicitUsings>`, so the `Task.FromResult(...)` return type below is CS0246 `Task` without it.
  In `FinderBase<THit>`:
  `public virtual Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "") =>
  Task.FromResult(GetCandidates(query));`. Every current finder derives from `FinderBase`, so none
  needs a source change.
- **Verify-with:** `dotnet build` compiles all finders; existing `Run_GetCandidates_*` tests stay
  GREEN.
- **Fails-if:** `CS0246 'Task'` in `FinderBase.cs` (the using was omitted); `CS8701`/
  `MissingMethodException` (a default interface method was used instead of abstract); a finder fails
  to compile because it does not derive from `FinderBase`.

**BP-6a — `FzfFinder` skeleton: type, fields, ctors, `Name`/`IsQueryDriven`, `GatherHits` stub, `OpenErrorNoun`.**
- **Files:** `Telescope/Finders/FzfFinder.cs` (new).
- **Usings (new file):** `using System;`, `using System.Collections.Generic;`, `using System.IO;`,
  `using System.Linq;`, `using System.Threading;`, `using System.Threading.Tasks;`, `using EnvDTE;`,
  `using Microsoft.VisualStudio.Shell;`, `using Telescope.Filter;`, `using Telescope.Logging;`.
  (No `<ImplicitUsings>` — every referenced type needs its using; `Telescope.Finders` is the file's
  own namespace.)
- **Change:** `public sealed class FzfFinder : FinderBase<FzfHit>` in `Telescope.Finders`.
  - `public override string Name => "Fzf";` `public override bool IsQueryDriven => true;`
  - Fields: `Func<DTE> _dteFactory`, `ProjectFileCache _fileCache`,
    `FileContentCache _contentCache = new FileContentCache(500)`, `IFzfEngine _fzf`,
    `string? _cachedSolutionName`, `Func<IReadOnlyList<string>>? _testEnumerate`,
    `Action<FzfHit>? _testOpener`; `private const int HitCap = 200;`
  - Real ctor: `internal FzfFinder(Func<DTE> dteFactory, ProjectFileCache fileCache, IFzfEngine fzf)`.
  - Test ctor A: `internal FzfFinder(Func<IReadOnlyList<string>> fileSource, Action<FzfHit> opener,
    IFzfEngine fzf) : this(new ProjectFileCache(), fileSource, opener, fzf)`.
  - Test ctor B: `internal FzfFinder(ProjectFileCache cache, Func<IReadOnlyList<string>> enumerate,
    Action<FzfHit> opener, IFzfEngine fzf)` — **must assign `_dteFactory = () => null!;`** (mirroring
    `GrepFinder`'s test ctor, `GrepFinder.cs:59`). `_dteFactory` is a non-nullable `Func<DTE>` field;
    leaving it unassigned yields **CS8618** under `<Nullable>enable</Nullable>`. The test path never
    dereferences it (the `_testEnumerate`/`_testOpener` seams bypass DTE), so the null-forgiving
    lambda is the established pattern.
  - `protected override IReadOnlyList<FzfHit> GatherHits() => throw new
    NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)");`
  - `protected override string OpenErrorNoun => "fzf";`
- **Verify-with:** `dotnet build` compiles; `CS0246 'FzfFinder'` resolves. Unit
  `Run_FzfFinder_QueryDrivenBehavior` (`IsQueryDriven == true`).
- **Fails-if:** `CS0246 'FzfFinder'` persists; the class does not derive `FinderBase<FzfHit>`;
  `CS0246 'IFzfEngine'`/`'FzfHit'` (BP-1/BP-2 not done first).

**BP-6b — `FzfFinder` gather: sync short-circuit + async per-file fzf/literal gather + `WarmContentCache` + harness anchor.**
- **Files:** `Telescope/Finders/FzfFinder.cs` (modify); `tools/harness/test-e2e.ps1` (modify the
  `telescope-fzf` step-2 pattern).
- **Change:**
  - `public override IReadOnlyList<FinderEntry> GetCandidates(string query)`: empty →
    `WarmContentCache(); return Array.Empty<FinderEntry>();`; non-empty → `throw new
    NotSupportedException("FzfFinder is query-driven; call GetCandidatesAsync(query)")`.
  - `public override async Task<IReadOnlyList<FinderEntry>> GetCandidatesAsync(string query = "")`:
    empty → `return GetCandidates(query);` (delegates to the sync short-circuit — no fzf spawn, no
    gather log). Non-empty: `bool available = await _fzf.IsAvailableAsync();` if `!available` log
    `fzf unavailable — literal fallback`; enumerate files (test seam `_fileCache.Get(_testEnumerate)`
    OR `ThreadHelper.ThrowIfNotOnUIThread()` + `_fileCache.Get(() => ProjectFiles.Enumerate(dte))`
    with solution-change invalidation, catch → `FzfFinder failed to enumerate: {ex.Message}`); per
    file read `_contentCache.GetLines(path)` (catch → skip); if available
    `var matched = await _fzf.FilterAsync(lines, query, CancellationToken.None); foreach (int ln in
    FzfLineMapper.Map(lines, matched))` add `new FzfHit(path, ln, lines[ln - 1])` until `HitCap`;
    else `foreach (int ln in LiteralLineScanner.Scan(lines, query, HitCap - hits.Count))` add
    `new FzfHit(path, ln, lines[ln - 1])`. Finally `TelescopeLog.Log($"fzf hits={hits.Count}")` and
    `return hits.Select(ToEntry).ToList();`.
  - `WarmContentCache()` mirrors `GrepFinder.WarmContentCache` (best-effort `_contentCache.GetLines`
    per file, catch → skip).
  - **Harness anchor (finding 4):** in `tools/harness/test-e2e.ps1:1384`, change the step-2 pattern
    from `"$($script:PfxTel)fzf hits=1"` to `"$($script:PfxTel)fzf hits=1$"`. `Assert-NewLogLine` →
    `Wait-LogLine` uses unanchored `-match` (`harness-common.ps1:183`), so the bare pattern also
    matches `fzf hits=10`; the `$` anchor pins the exact count. (The scenario pre-exists — only the
    pattern string changes; do NOT re-add the scenario.)
- **Verify-with:** Unit `Run_FzfFinder_EmptyQueryReturnsZeroCandidates`,
  `Run_FzfFinder_EmptyQueryCleanEmptyNoFailureLog`, `Run_FzfFinder_EmptyQueryAsyncShortCircuits`,
  `Run_FzfFinder_FuzzyMatchReportsHits` (`fzf hits=1`), `Run_FzfFinder_HitCapBounded`,
  `Run_FzfFinder_CacheEnumeratesOnce`, `Run_FzfFinder_OpenPathNoFailureLog`,
  `Run_FzfFinder_UnavailableFallsBackToLiteralScan` (`fzf unavailable — literal fallback`,
  `FilterCalls == 0`). e2e `telescope-fzf` step 2 (`fzf hits=1$`).
- **Fails-if:** empty query spawns fzf (`FilterCalls != 0`) or logs `fzf hits=`; unavailable path
  calls `FilterAsync` or returns the full list; `fzf hits=` count wrong; the harness pattern is left
  unanchored (a `fzf hits=10` line would satisfy it).

**BP-6c — `FzfFinder` mapping + open: `ToEntry` / `OpenHit`.**
- **Files:** `Telescope/Finders/FzfFinder.cs` (modify).
- **Change:**
  - `protected override FinderEntry ToEntry(FzfHit hit) => new
    FinderEntry($"{Path.GetFileName(hit.FilePath)}:{hit.LineNumber}: {hit.LineText}", hit);`
  - `protected override void OpenHit(FzfHit hit)`: test opener → invoke; else
    `HitOpener.OpenAtLine(hit, (path, line) => { DTE dte = _dteFactory(); DteFileOpener.OpenAtLine(dte,
    path, line); TelescopeLog.Log($"opened fzf: file={path} line={line}"); });`
- **Verify-with:** Unit `Run_FzfFinder_DisplayIsFileNameLineText` (`A.cs:2: NEEDLE here`),
  `Run_FzfFinder_PayloadRoundTripsFzfHit`, `Run_FzfFinder_OnSelectedOpensHitAtLine`. e2e
  `telescope-fzf` step 4 (`opened fzf: file=.*FzfProbe\.cs line=4`).
- **Fails-if:** display not `{fileName}:{line}: {lineText}`; payload not the `FzfHit`; no
  `opened fzf:` line after Enter.

**Phase 1 mid-point verify:** `dotnet run --project tests/Telescope.Tests -- Fzf` → all
`Run_FzfFinder_*` + `Run_FzfLineMapper_*` PASS; `dotnet run --project tests/Telescope.Tests --
GrepFinder` → all `Run_GrepFinder_*` still PASS.

### Phase 2 — Overlay + registration

**BP-7 — Overlay awaits `GetCandidatesAsync`.**
- **Files:** `Telescope/Overlay/TelescopeOverlay.cs` (modify `RefreshQueryDrivenAsync`, ~line 381).
- **Change:** `results = finder.GetCandidates(query) ?? Array.Empty<FinderEntry>();` →
  `results = await finder.GetCandidatesAsync(query) ?? Array.Empty<FinderEntry>();`. The `await`
  captures the WPF `SynchronizationContext`, so the continuation (and `RenderResults()`) resumes on
  the UI thread; the existing `_queryGeneration` counter still discards a stale in-flight gather.
- **Verify-with:** e2e `telescope-grep` stays GREEN (shared query-driven path); e2e `telescope-fzf`
  step 2 (`fzf hits=1$`).
- **Fails-if:** `telescope-grep` regresses (the await changed the resume thread); `telescope-fzf`
  never logs `fzf hits=`.

**BP-8 — Action wiring: `FinderNames` + `default-keybindings.json`.**
- **Files:** `MyExtension/Package/Utils/TelescopeLauncher.cs` (modify `FinderNames`);
  `MyExtension/Resources/default-keybindings.json` (modify).
- **Change:** Add `["telescope-fzf"] = "Fzf",` to `TelescopeLauncher.FinderNames` (the `Actions`
  registry derives the action from this dictionary — NO `ResolveAction` case). Add
  `"F,Z": "telescope-fzf",` to `default-keybindings.json` bindings.
- **Verify-with:** e2e `telescope-fzf` step 1 (`open finder=Fzf candidates=0`); the harness log
  `[NeoVisual] Keybindings loaded: 28 binding(s), leader = Space` (was 27).
- **Fails-if:** `F,Z` does nothing (no `leader-binding executed: F,Z`); `Unknown finder 'Fzf'`;
  binding count stays 27.

**BP-9 — Register `FzfFinder` in the package.**
- **Files:** `MyExtension/Package/MyExtensionPackage.cs` (modify the `"finders"` step, ~line 100-108).
- **Change:** Add `using Telescope.Filter;` to the usings; add
  `_telescope.RegisterFinder(new FzfFinder(() => VsServices.Dte(this)!, fileCache, new FzfFilter()));`
  alongside the other finders. `FzfFilter` is `internal` (visible via `InternalsVisibleTo("MyExtension")`);
  it implements `IFzfEngine` (BP-1).
- **Verify-with:** e2e `telescope-fzf` step 1 (`open finder=Fzf candidates=0`); the finder resolves
  (no `Unknown finder 'Fzf'`).
- **Fails-if:** `Unknown finder 'Fzf'`; `CS0246 'FzfFilter'` (missing using); `CS0122`
  (InternalsVisibleTo missing).

**Phase 2 mid-point verify:** `dotnet build` 0 errors;
`pwsh tools/harness/test-e2e.ps1 -Tests telescope-fzf` GREEN;
`pwsh tools/harness/test-e2e.ps1 -Tests telescope-grep` GREEN.

### Phase 3 — Doc sync

**BP-10 — `docs/spec.md` sync.**
- **Files:** `docs/spec.md`.
- **Change:** (a) Add a finder-table row near line 95:
  `| Telescope/Finders/FzfFinder.cs | Query-driven fuzzy content finder over ProjectFiles.Enumerate (per-file fzf --filter; literal fallback when fzf unavailable). |`.
  (b) Add the three diagnostics to §4 (near line 219): `[Telescope] fzf hits=...` /
  `[Telescope] opened fzf: file=... line=...` and `[Telescope] fzf unavailable — literal fallback`.
  (c) Add `telescope-fzf` to the action list (line 181-183) and `Space+F,Z` to the defaults list
  (line 187-193). (d) **Correct lines 181-185:** replace "To add a *new built-in action*, add a case
  in `ResolveAction` and a line in `default-keybindings.json`." with the truth: telescope actions are
  derived from `TelescopeLauncher.FinderNames` (add a `FinderNames` entry + a `default-keybindings.json`
  line); `ResolveAction` cases are only for non-telescope built-ins. (e) **Count sync (finding 1):**
  update the Telescope test count `157` → `172` at `spec.md:248` and `:410`, and the scenario count
  `35` → `36` at `spec.md:286`, `:291`, `:412` (NeoVisual stays `168` at `:260`/`:411`).
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1` (0 unresolved backticked refs); grep
  `docs/spec.md` for `telescope-fzf` + `fzf hits=` + `172 tests` + `36 scenarios`.
- **Fails-if:** doc-ref lint reports an unresolved ref; the stale `ResolveAction` sentence remains;
  `spec.md` still reads `157 tests` / `35 scenarios`.

**BP-11 — `AGENTS.md` sync.**
- **Files:** `AGENTS.md`.
- **Change:** Add `FzfFinder` to the finder list (the "Done and tested" / finder bullets), add the
  three `[Telescope]` diagnostics to the diagnostics list, and add `Space+F Z` to the keybinding list.
  **Count sync (finding 1):** update the Telescope test count `157` → `172` (the "Currently **157
  tests, all passing**" line) and the scenario count `35` → `36` (the "Scenarios (35 total" line);
  NeoVisual stays `168`.
- **Verify-with:** `pwsh tools/lint/check-doc-refs.ps1`; grep `AGENTS.md` for `fzf hits=` + `Space+F Z`
  + `172 tests` + `36 scenarios`.
- **Fails-if:** doc-ref lint fails; the new finder/diagnostics are absent; `AGENTS.md` still reads
  `157 tests` / `35 total`.

**BP-12 — `docs/progress.md` sync.**
- **Files:** `docs/progress.md`.
- **Change:** Update the stale Baseline (lines 64-67) from `153 passed`/`163 passed` to the current
  verified `157 passed`/`168 passed` (the task's required correction; the stale number is NOT a
  regression). Mark the Telescope `fzf` finder item (item 5, ~line 304) DONE with the new scenario +
  diagnostics. **Arithmetic note:** the feature adds 15 tests (3 `Run_FzfLineMapper_*` + 12
  `Run_FzfFinder_*`), so the post-feature `Telescope.Tests` count is **172** (157 + 15); the Done
  entry records 172/168 while the Baseline correction records 157/168.
- **Verify-with:** grep `docs/progress.md` for `157 passed` + `168 passed`; the item is marked done.
- **Fails-if:** the Baseline still reads 153/163; the item is still "PLANNED".

**BP-12b — `.opencode/skills/vs-extension-dev/SKILL.md` count sync (finding 1).**
- **Files:** `.opencode/skills/vs-extension-dev/SKILL.md`.
- **Change:** Update the Telescope test count `157` → `172` (`SKILL.md:244`) and the scenario count
  `35` → `36` (`SKILL.md:14`, `:247`); NeoVisual stays `168` (`:245`). This is the third doc the
  feature's counts change (alongside `spec.md` BP-10 and `AGENTS.md` BP-11); the post-feature counts
  are **172 Telescope / 168 NeoVisual / 36 scenarios**.
- **Verify-with:** grep `SKILL.md` for `172` + `36 scenarios`; `pwsh tools/lint/check-doc-refs.ps1`
  clean.
- **Fails-if:** `SKILL.md` still reads `157` / `35 scenarios`.

### Phase 4 — VERIFY regression fix (iteration 1)

**BP-13 — Update the stale `Actions.Registry` count assertion in `NeoVisual.Tests`.**
- **Files:** `tests/NeoVisual.Tests/Program.cs` (modify `Run_ActionsRegistry_ContainsAllBuiltins`,
  `:1593-1608`).
- **Change:** The registry now holds **11** built-in action names (BP-8 added `telescope-fzf` via
  `TelescopeLauncher.FinderNames`, which `Actions.BuildRegistry()` derives from — `Actions.cs:33-38`).
  Update the assertion to match:
  - `Assert.Equal(10, Actions.Registry.Count);` → `Assert.Equal(11, Actions.Registry.Count);`
  - Add `"telescope-fzf",` to the `names` array (`:1598-1603`), mirroring the existing
    `"telescope-grep",` entry (place it after `"telescope-grep",` and before
    `"toggle-solution-explorer",`).
  - Update the leading comment `// ... exactly the 10 built-in action names ...` → `11`.
  Do NOT change `Actions.cs` / `TelescopeLauncher.cs` — the registry growth is the intended
  contract (hub adjudication ACCEPT). This is a test-only change; the NeoVisual test **count**
  stays 168 (an existing test is corrected, none added).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- ActionsRegistry` →
  `Run_ActionsRegistry_ContainsAllBuiltins` PASSES (count 11, all 11 names present);
  `Run_ActionsRegistry_TelescopeMapsToFinder` stays GREEN (it derives both key sets from the
  same `FinderNames` source, so it already passes with 6 telescope keys). Full suite
  `dotnet run --project tests/NeoVisual.Tests` → **168/168**.
- **Fails-if:** `Expected [10] but got [11]` persists (the count was not updated); or
  `registry contains 'telescope-fzf'` fails (the name was not added to the array); or
  `Run_ActionsRegistry_TelescopeMapsToFinder` regresses (it must not — no source change).

**Final gate:** `dotnet build` 0 errors; `dotnet run --project tests/Telescope.Tests` (172) and
`dotnet run --project tests/NeoVisual.Tests` (168) all PASS; `pwsh tools/harness/test-e2e.ps1`
(36 scenarios) GREEN; `pwsh tools/lint/check-doc-refs.ps1` clean.

## Verification Trace

| failing test/scenario | implicated steps | expected diagnostic |
|---|---|---|
| build `CS0246 'IFzfEngine'` (Program.cs:2510) | BP-1 | (compile) `FakeFzfEngine : IFzfEngine` resolves |
| build `CS0103 'FzfLineMapper'` (2545/2558/2571) | BP-3 | (compile) `FzfLineMapper.Map` resolves |
| build `CS0246 'FzfFinder'` (2583+) | BP-6a | (compile) `FzfFinder` resolves |
| build `CS0246 'Task'` in `FinderBase.cs` | BP-5 | (compile) `using System.Threading.Tasks;` present |
| build `CS0246 'FzfHit'` (2693/2708) + `CS0019`/`CS1061` (2713-2716) | BP-2 | (compile) `FzfHit` + nullable member access resolve |
| `Run_FzfLineMapper_ExactLineMapsToLineNumber` | BP-3 | (unit) `Map` → [2] |
| `Run_FzfLineMapper_DuplicateLinesMapOrdinal` | BP-3 | (unit) `Map` → [1,3] |
| `Run_FzfLineMapper_UnknownLineSkipped` | BP-3 | (unit) `Map` → [] |
| `Run_FzfFinder_EmptyQueryReturnsZeroCandidates` | BP-6b | (unit) 0 candidates |
| `Run_FzfFinder_EmptyQueryCleanEmptyNoFailureLog` | BP-6b | (unit) no `[Telescope] fzf hits=` / no `FzfFinder failed to enumerate:` |
| `Run_FzfFinder_EmptyQueryAsyncShortCircuits` | BP-6b | (unit) `FilterCalls == 0`, no `fzf hits=` |
| `Run_FzfFinder_FuzzyMatchReportsHits` | BP-6b | `[Telescope] fzf hits=1` |
| `Run_FzfFinder_DisplayIsFileNameLineText` | BP-6c | (unit) `A.cs:2: NEEDLE here` |
| `Run_FzfFinder_PayloadRoundTripsFzfHit` | BP-2, BP-6c | (unit) payload `FzfHit` round-trips |
| `Run_FzfFinder_OnSelectedOpensHitAtLine` | BP-6c | (unit) opener invoked with hit |
| `Run_FzfFinder_HitCapBounded` | BP-6b | (unit) ≤200 |
| `Run_FzfFinder_CacheEnumeratesOnce` | BP-6b | (unit) enumerate once |
| `Run_FzfFinder_QueryDrivenBehavior` | BP-6a, BP-6b | (unit) `IsQueryDriven` + 1 hit |
| `Run_FzfFinder_OpenPathNoFailureLog` | BP-6b | (unit) no `FzfFinder failed to enumerate:` |
| `Run_FzfFinder_UnavailableFallsBackToLiteralScan` | BP-4, BP-6b | `[Telescope] fzf unavailable — literal fallback`, `FilterCalls == 0`, 2 hits |
| e2e `telescope-fzf` step 1 | BP-6a, BP-8, BP-9 | `[Telescope] open finder=Fzf candidates=0` |
| e2e `telescope-fzf` step 2 | BP-6b, BP-7 | `[Telescope] fzf hits=1$` (anchored — finding 4) |
| e2e `telescope-fzf` step 3 | BP-6b, BP-7 | `[Telescope] preview file=.*FzfProbe\.cs` / `preview caret=\d+ line=4` |
| e2e `telescope-fzf` step 4 | BP-6c | `[Telescope] opened fzf: file=.*FzfProbe\.cs line=4` |
| e2e `telescope-grep` (regression guard) | BP-4, BP-5, BP-7 | `[Telescope] grep hits=2` / `opened grep: ... line=4` |
| `Run_GrepFinder_*` (regression guard) | BP-4, BP-5 | (unit) unchanged |
| doc count sync (spec.md / AGENTS.md / SKILL.md) | BP-10, BP-11, BP-12b | (docs) `172 tests` / `168 tests` / `36 scenarios`; `check-doc-refs.ps1` clean |
| `Run_ActionsRegistry_ContainsAllBuiltins` (NeoVisual.Tests, iteration 1 regression) | BP-13 | (unit) `Actions.Registry.Count == 11` incl. `telescope-fzf`; `Run_ActionsRegistry_TelescopeMapsToFinder` stays GREEN |

**Known-RED allowlist: none.** All 35 existing e2e scenarios are GREEN and both unit suites pass
(157 / 168). The verification-agent must NOT flag any pre-existing scenario as a regression. The
only expected RED is the new `telescope-fzf` scenario + the new `Run_FzfFinder_*` /
`Run_FzfLineMapper_*` tests before the feature exists.

## Execution Log

### Attempt 1 — GREEN (2026-10-03)
- lane feature; 18 delegations, 3 VS boots, 1 iteration; BUILD pass (BP-1…BP-13); VERIFY PASS (full 36-scenario e2e + Telescope 172 + NeoVisual 168); DEVIATION: BP-6b `ThreadHelper.ThrowIfNotOnUIThread()` moved to a synchronous `EnumerateFiles()` helper (VSTHRD109) → ACCEPT (behavior-preserving); iteration 1: `Run_ActionsRegistry_ContainsAllBuiltins` → BP-13 (test-only).
- failure-log sweep: 10 entries read, 0 fixed, 0 queued, 1 annotated.
