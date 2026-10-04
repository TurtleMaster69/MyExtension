# Plan — Gap 4: recent-files finder (Telescope-style, `Name="Recent"`)

> **Lane: feature (e2e ENABLED).** New finder + likely new diagnostics → **M-M7** (the new
> literals are declared). Full feature-lane loop.
>
> **Source:** the queue's Gap 4 (the user's 2026-09-28 BUILD decision: "a Telescope-style
> recent-files finder").
>
> **Research:** the primary data source is `EnvDTE._DTE.RecentFiles` (the automation MRU —
> `RecentFile.Path` / `Open()`; MEDIUM confidence — the MS-Learn page 404'd, so the planner
> verifies the API shape against the installed interop via LSP). The registry hive is FRAGILE
> (VS 2017+ keeps the MRU in a private registry — do NOT read it). Fallback: an
> extension-maintained MRU from the extension's own `[Telescope] opened file:` events (only
> knows files opened since load). PIN: `DTE.RecentFiles` primary; the MRU fallback documented
> as a future enhancement, not built.
>
> **Ground truth (recon):** the finder skeleton is established — `IFinder`/`FinderEntry`
> (TelescopeFinder.cs:15-67), `FinderBase<THit>` (FinderBase.cs:16-79: the guarded gather,
> ToEntry, OpenHit), the exemplar `ReferencesFinder.cs:25-65` (the ctor-injected gatherer
> `Func` + opener `Action`, Name, display/payload, open), the registration chain
> (MyExtensionPackage.cs:105-110 → TelescopeController.RegisterFinder → FinderNames
> TelescopeLauncher.cs:26-35 + the derived Actions.Registry entry + the equality fixture).
> The goto plan (SECOND in queue) adds DefinitionFinder the same way — Gap 4 follows that
> precedent (FIFTH in queue: columns → goto → gap 11 → feature 7 → gap 4).
>
> **Leader key:** `f,r` is TAKEN (references). PROPOSED: **`f,e`** (rEcent) — flagged for the
> user at handoff (the alternatives: `f,m` most-recent, `f,h` history).
>
> **Column model dependency:** the columns plan (FIRST in queue) builds the per-finder column
> sets for the 6 EXISTING finders; a NEW finder needs its own column set (File | Dir — the
> Files finder's shape) added to `FinderColumns` — a small post-columns addition THIS plan
> owns.

## Goal

A Telescope-style recent-files finder: `Space+f,e` opens an overlay listing the solution's
recently-opened files (the VS MRU), filterable, previewable, Enter opens the file — the
established finder pipeline end to end.

## Approach

**D1 — `RecentFileHit` + `RecentFilesFinder`.** NEW `Telescope/Finders/RecentFileHit.cs`
(extends `FileLocation`; maybe `Title`? — the MRU entries are paths; PIN: path only, the
columns model derives name/dir). NEW `Telescope/Finders/RecentFilesFinder.cs`
(`Name="Recent"`): the ctor-injected gather seam `Func<IReadOnlyList<string>>` (the MRU paths,
most-recent-first) + the opener (the `DteFileOpener` pattern) — hermetic-testable. The
display: the file name (+ the dir via the column model).

**D2 — The gatherer.** `MyExtension/Package/RoslynGatherers.cs`-adjacent (or a small
`MyExtension/Package/Utils/RecentFilesGatherer.cs`): `dte.RecentFiles` → the paths
(most-recent-first; skip non-existent files — `File.Exists` filter; the count cap ~200 like
the query finders). UI thread. The planner verifies the `RecentFiles`/`RecentFile` API shape
(`EnvDTE` interop — LSP against the installed assembly) and pins it.

**D3 — Registration + the leader binding.** `MyExtensionPackage` registers the finder;
`TelescopeLauncher.FinderNames["telescope-recent"] = "Recent"` (auto-derives the
`telescope-recent` registry entry — the equality fixture stays green); the leader binding
`"f,e": "telescope-recent"` in `default-keybindings.json`. The registry count +1 (an
`<ACTUAL>` re-read).

**D4 — The column set.** `FinderColumns` gains the Recent catalog (File ON, Dir ON — the
Files finder's shape; Full path off) — a post-columns addition (the dependency binds).

**D5 — Diagnostics (M-M7).** The finder pattern's gather summary + open lines — PIN:
`[Telescope] recent files gathered count=...` (the gather summary) + the open reuses the
EXISTING `[Telescope] opened file: ...` (the FileFinder's open line — the same HitOpener
path) — the planner verifies which open line fires and pins the minimal new-literal set.

**D6 — Tests.** `tests/Telescope.Tests`: `Run_RecentFilesFinder_*` (the hermetic gather/
display/open/preview-jump/determinism — the DefinitionFinder test pattern) + the column-set
test. RED CS0246. Suite delta: +N (an `<ACTUAL>` re-read).

**D7 — e2e (ENABLED).** A NEW scenario `telescope-recent`: `Space+f,e` → the overlay opens
with the MRU (the harness's earlier scenarios opened files, so the MRU is populated) → type a
query → Enter opens. Created RED; executed at VERIFY.

**D8 — Docs.** spec.md (§2.2 the key-files row, §3 the binding, §4 the literals, §5 counts,
§7 a feature bullet), AGENTS.md, SKILL.md, progress.md (the item → DONE at GREEN).

## Acceptance criteria

| # | Criterion | Diagnostic | Test |
|---|-----------|-----------|------|
| AC1 | `Space+f,e` opens the recent-files overlay with the MRU | `open finder=Recent candidates=...` | unit `Run_RecentFilesFinder_*`; e2e `telescope-recent` |
| AC2 | The results are most-recent-first, existing files only | the gather summary | unit (the order + the filter pinned) |
| AC3 | Enter opens the file; the preview jumps | the existing `opened file:` line | unit; e2e |
| AC4 | The column set (File/Dir) renders in the columned list | `results columns=file,dir` | unit (the Recent catalog) |
| AC5 | The registry/leader wiring is complete | `leader-binding executed: f,e` | e2e is the proof (the registry count is an observed effect, not asserted — `Run_ActionsRegistry_TelescopeMapsToFinder` derives both sides and cannot detect a missing BP-5 entry) |

## Files to be touched

- **Created:** `Telescope/Finders/RecentFileHit.cs`, `Telescope/Finders/RecentFilesFinder.cs`,
  the gatherer (placement the planner pins).
- **Modified:** `MyExtension/Package/MyExtensionPackage.cs`, `MyExtension/Package/Utils/TelescopeLauncher.cs`,
  `MyExtension/Package/Utils/Actions.cs` (derived — verify), the column model's `FinderColumns`
  (post-columns), `MyExtension/Resources/default-keybindings.json`, `tests/Telescope.Tests/Program.cs`,
  `tools/harness/test-e2e.ps1`, the docs.
- **Not touched:** the overlay (the finder pipeline is host-agnostic), the in-flight plans' files.

## Open risks

1. **`DTE.RecentFiles` shape (medium).** The doc 404'd — the planner verifies via LSP against
   the installed EnvDTE interop; if the API differs, the gatherer adapts (the seam isolates it).
2. **The MRU semantics (low).** `DTE.RecentFiles` is VS's MRU (across solutions) — the finder
   shows what VS shows in File▸Recent; the planner pins whether to filter to the current
   solution's files (RECOMMEND: no filter — show the MRU as-is, matching VS).
3. **The queue position (process).** Fifth — the counts are re-reads; the column model lands
   first.

## Build Plan

> **Aggregated (Stage 3)** from `artifacts/gap4-section-{a,b}.md` — the authoritative full
> detail (the finder/gatherer code, the test code, the scenario body, the doc edits) lives
> there; the steps below are the contract. **e2e ENABLED.**
>
> **Pinned corrections (binding):** (1) **`EnvDTE.RecentFiles` does NOT exist in the installed
> 17.x interop** (envdte.dll is pure type-forwarding → Microsoft.VisualStudio.Interop, zero
> Recent types) — the gatherer uses a REFLECTION PROBE on the live DTE COM object
> (`InvokeMember("RecentFiles",…)`, best-effort, a failure swallowed by design) + a
> **session-MRU fallback** (DocumentEvents-driven) as the guaranteed floor — both behind the
> unchanged `Func<IReadOnlyList<string>>` seam; (2) NO `Actions.cs` edit (the registry entry
> is derived from FinderNames); (3) the column tests are named `Run_ResultsColumns_Recent_*`
> (the existing family); (4) the MRU-timing strategy: the scenario opens `Models/Order.cs`
> first, then fires `f,e` and asserts it is the TOP match; (5) the M-M7 literals:
> `[Telescope] recent files gathered count={n}` (NEW) + the REUSED `[Telescope] opened file:
> {path}` + `[Telescope] recent files gather failed: {msg}` / `[Telescope] open file failed:
> {msg}`.

- **BP-1** — `RecentFileHit` (NEW — distinct from `FileHit` for the column type-disjointness).
- **BP-2** — `RecentFilesFinder` (NEW — the dedupe→`File.Exists`→`Take(200)` order contract;
  the finder-owned open log).
- **BP-3** — `RecentFilesGatherer` (NEW — the reflection probe + the DocumentEvents session-MRU
  floor).
- **BP-4** — the package "finders"-step registration + the `OpenRecentFile` opener (never logs).
- **BP-5** — `FinderNames["telescope-recent"]="Recent"` (Actions.cs untouched — derived).
- **BP-6** — the keybinding `"f,e": "telescope-recent"` (PROPOSED — flagged at handoff).
- **BP-7** — `FinderColumns.Recent()` + the switch case (the full-dir cells, no root trim).
- **BP-8** — the ten tests + the `WidthKinds` extension (RED CS0246).
- **BP-B1** — the e2e scenario `telescope-recent` (the header line + the registration after
  `telescope-results-columns`, before `seed-reset`).
- **BP-B2..B5** — the docs: spec (§2.2, §3, §4, §5, §7, §8); AGENTS.md; SKILL.md; progress.md
  at GREEN (the DOC-66-3 attribution constraint pinned).
- **BP-B6** — the suite arithmetic (the `<ACTUAL>` re-reads; the pinned deltas: e2e +1,
  Telescope.Tests +10) + the 9-step final gate (the staggered suites, a fresh boot,
  `-TimeoutSec`).

## Verification Trace

| failing test / gate (RED before the change) | implicated steps | expected diagnostic / proof |
|---|---|---|
| `Run_RecentFilesFinder_*` (the hermetic set) | BP-1/2/3, BP-8 | RED CS0246 → GREEN: the gather/display/open/preview-jump/determinism |
| `Run_ResultsColumns_Recent_*` | BP-7, BP-8 | RED → GREEN: the Recent catalog (file,dir) |
| `Run_ActionsRegistry_TelescopeMapsToFinder` (stays GREEN) | BP-5 | the derived entry keeps the equality fixture green |
| e2e `telescope-recent` (RED then GREEN) | BP-B1, BP-1..7 | RED before the source lands; GREEN: `open finder=Recent candidates=...` + `results columns=file,dir` + the Order.cs top match + `opened file:` |
| unit gate | BP-8, BP-B6 | Telescope 209 (`<ACTUAL>`); the build 0 errors |
| lints + `-List` | BP-B6 | 0 unresolved; PASS; 42 scenarios |

**Known-RED allowlist: NONE.** Expected RED = the new unit tests + the new e2e scenario before
the source lands. The verifier must NOT flag the reflection-probe's best-effort nature (the
MRU floor is the guaranteed path) or the in-flight plans' count drift (re-reads).
