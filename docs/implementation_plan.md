# Implementation Plan — Item: Telescope `implementation` finder (preview line-jump)

> **Lane: feature** — new capability (a new Telescope finder) AND a new
> `[Telescope] implementations gathered count=...` + `[Telescope] opened implementation: ...`
> diagnostic + a new default keybinding (`F,I`). **M-M7 HARD TRIGGER:** ADDS
> `[Telescope]` diagnostics → full feature pipeline: initial-plan REVIEW, e2e RED
> booting VS, post-GREEN spec re-review.

---

**Goal:** Add the third (and last well-defined) Telescope finders roadmap item — an
**implementation finder** (`Space+F I`) that lists the **implementations/overrides
of the symbol at the caret** in the active document (interfaces → implementing
types/members, abstract/virtual members → overrides, classes → derived classes),
previews each hit file with the caret jumped to the implementation line, and opens
the file at that line on Enter. Mirrors the just-completed `ReferencesFinder`
architecture (symbol-at-caret resolution + host-injected gatherer/opener seams).

---

## Approach

1. **`ImplementationFinder : IFinder`** in the `Telescope` project (mirror the
   `ReferencesFinder` shape — host-injected gatherer/opener seams so the finder
   is hermetic-testable):
   - `Name => "Implementation"`.
   - **Data source:** Roslyn `SymbolFinder.FindImplementationsAsync(symbol, solution)`
     — the inverse of find-references; the host resolves the caret symbol (same
     path as `GatherReferences`: MEF `VisualStudioWorkspace`, active-document
     mapping, caret offset) and gathers each implementation symbol's **declaring
     source location** (file + line). **NOTE — the gatherer must NOT copy the
     references gatherer's `foreach rs.Locations` mapping verbatim:**
     `FindImplementationsAsync` returns implementation *symbols* (`ISymbol`), not
     reference locations. Resolve each returned symbol's source location via
     `symbol.Locations`/`DeclaringSyntaxReferences`, SKIP symbols whose location
     is not `Location.IsInSource` (metadata types from referenced assemblies),
     and take the FIRST declaring syntax reference (partial types may have
     several). The declaring line is the correct preview/open jump target
     (type-decl line for a type, override-decl line for a member).
   - **`ImplementationHit`** payload (new pure class): `FilePath`, `LineNumber`
     (1-based), `SymbolName`, `Kind` (e.g. `Class`/`Method`/`Property` — for the
     display row). Deterministic `Display` format (builder's choice, e.g.
     `{Kind} {SymbolName} — {file}:{line}`), MUST be deterministic for e2e.
   - `GetCandidates()`: calls the injected gatherer (UI thread —
     `ThreadHelper.ThrowIfNotOnUIThread()`), maps hits to `FinderEntry`s
     (payload = `ImplementationHit`), logs a gather summary
     `[Telescope] implementations gathered count=N`; swallows exceptions
     (the `CodeIssuesFinder` pattern).
   - `OnSelected(entry)`: open the hit file (`dte.ItemOperations.OpenFile`),
     jump to the line (`TextSelection.GotoLine`), log
     `[Telescope] opened implementation: file=... line=...` (col is NOT logged —
     no column metadata needed here).
   - Hermetic test ctor mirroring `ReferencesFinder`/`CodeIssuesFinder` (injected
     `Func<IReadOnlyList<ImplementationHit>>` gatherer + `Action<ImplementationHit>`
     opener) so display/payload/opener/line-mapping logic is unit-testable
     without DTE/Roslyn.
2. **Overlay preview support:** extend `TelescopeOverlay.LoadPreviewForSelection`
   with an `ImplementationHit` branch (load file content +
   `_previewNavigator.MoveToLine(hit.LineNumber)`, reusing the existing
   `preview file=` / `preview caret=... line=...` logs) — mirroring the
   `ReferenceHit` branch.
3. **Keybinding + action:** add `"F,I": "telescope-implementation"` to
   `MyExtension/default-keybindings.json` (F,I is free — verified; nothing pins
   it). Add `case "telescope-implementation"` in `InputHandler.ResolveAction` +
   `OpenTelescopeImplementation()` mirroring `OpenTelescopeReferences()`; the host
   constructs `ImplementationFinder` with the real gatherer/opener (mirror
   `GatherReferences` — resolve caret symbol → `FindImplementationsAsync` →
   map declaring locations) and registers it (`TelescopeController.RegisterFinder`).
4. **Diagnostics contract (new — M-M7):**
   - `[Telescope] open finder=Implementation candidates=(\d+)` — existing generic
     finder-open log (no format change; new finder name).
   - `[Telescope] implementations gathered count=\d+` — **NEW** (gather summary).
   - `[Telescope] opened implementation: file=... line=\d+` — **NEW** (OnSelected).
   - Preview reuses `[Telescope] preview file=...` + `[Telescope] preview caret=\d+ line=\d+`.

---

## Acceptance criteria (each maps to a diagnostic line + a test)

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `Space+F I` opens the Implementation finder with ≥1 candidate for a seeded interface | `[Telescope] open finder=Implementation candidates=\d+` (≥1) | e2e `telescope-implementation` |
| A2 | Each candidate row shows kind/symbol/file/line deterministically | (display asserted via unit test on hit→entry mapping) | `Run_ImplementationFinder_*` (Telescope.Tests) |
| A3 | Gather summary proves implementations found | `[Telescope] implementations gathered count=\d+` (≥1) | e2e scenario |
| A4 | Preview loads the hit file and jumps the caret to the implementation line | `[Telescope] preview file=.*\.cs` + `[Telescope] preview caret=\d+ line=\d+` | e2e scenario |
| A5 | Enter opens the file at the SPECIFIC pinned implementation line | `[Telescope] opened implementation: file=.* line=<pinned 1-based line>` | e2e scenario |
| A6 | Unit coverage of display/payload/opener/line-mapping (hermetic) | n/a | `Run_ImplementationFinder_*` in Telescope.Tests |
| A7 | Existing suites stay green (Telescope 52+N, NeoVisual 25) | — | full-suite final gate |

---

## Tests

### Offline unit tests (tests/Telescope.Tests, new — RED at unit level)

Mirror the `Run_ReferencesFinder_*` hermetic tests (injected gatherer/opener).
Add a `Run_ImplementationFinder_*` family (names at the builder's discretion):
- **Display formatting:** a hit renders kind/symbol/file/line deterministically.
- **Payload round-trip:** `FinderEntry.Payload` is the exact `ImplementationHit`.
- **Opener invocation:** `OnSelected` calls the injected opener with the correct file/line.
- **Line mapping:** the hit's line drives the preview jump (pure mapping).
RED: tests reference a non-existent `ImplementationFinder`/`ImplementationHit` →
Telescope.Tests build fails (missing symbol) — the right-reason unit RED.

### E2E scenario: `telescope-implementation` (new, via `Register-Scenario`)

Seeding: extend the deterministic symbol graph with **NEW file + NEW type names
that DO NOT collide with the existing references-finder seed** — `Models/Shared.cs`
is byte-pinned in `$canonical` AND `telescope-references` moves the caret onto its
`Shared` token, so it MUST NOT be edited or shadowed. Use e.g. `Models/IShape.cs`
(interface `IShape`) + `Shape.cs` (`class Shape : IShape` implementing it), or an
abstract/virtual member with an override — the interface name must sit at a
DETERMINISTIC caret position, and the implementation's declaring line must be
PINNED (1-based) for A5. New seed files MUST be added byte-identically to the
`$canonical` map (uniform CRLF).

Scenario flow (each step asserting on the log):
1. Open the file containing the interface via the overlay (`Space F T`, filter,
   Enter) → deterministic caret. Esc to normal mode; move the caret onto the
   interface name (deterministic motions, pinned by the builder).
2. `Space+F I` → assert `[Telescope] open finder=Implementation candidates=\d+`
   (≥1) + `[Telescope] implementations gathered count=\d+` (≥1).
3. Assert preview jumped: `[Telescope] preview file=.*\.cs` +
   `[Telescope] preview caret=\d+ line=\d+` (line = the pinned implementation line).
4. Enter → assert `[Telescope] opened implementation: file=.* line=<pinned 1-based line>`.
5. `Close-Telescope`.

Diagnostics depended on: `[Telescope] open finder=Implementation candidates=...`,
`[Telescope] implementations gathered count=...`, `[Telescope] preview file=...`,
`[Telescope] preview caret=... line=...`, `[Telescope] opened implementation: ...`.
Scenario count 28 → 29.

---

## RED evidence plan (e2e-test-builder, feature lane)

1. **Unit RED (no VS boot):** Telescope.Tests build fails — `ImplementationFinder` /
   `ImplementationHit` missing (missing symbol). Right-reason unit RED for A6.
2. **E2E RED (boots VS):** with the scenario registered but NO finder/binding
   implemented, `telescope-implementation` FAILS — `Space+F I` does nothing (no
   binding) or no `open finder=Implementation` / `implementations gathered` /
   `opened implementation` lines appear. Right-reason RED for A1/A3/A4/A5.
3. The builder does NOT implement the finder (build-agent's job).

---

## Known-RED allowlist (for VERIFY)

- **None** of the affected scenarios/tests are allowlisted — `telescope-implementation`
  and the `Run_ImplementationFinder_*` tests are this item's targets and must be GREEN.
- Loop-time VERIFY runs `telescope-implementation` + Telescope.Tests; the final gate
  runs the FULL suite (now 29 scenarios) + both unit projects. The pre-existing
  `neovisual-editor-insert` flake is on record (retry-pass, 2/3) — apply the
  retry-once policy; it must not be treated as a regression.

---

## Execution Log

_To be appended by the hub on each attempt: attempt #, per-BP-step status,
debug/verifier verdict, capped evidence, and the cost line_
`delegations: N | VS boots: M | iterations: K`.

---

## Build Plan

> **Phase note:** this item is ~8 steps; no mid-plan phase split needed. Steps are
> ordered bottom-up (pure payload/finder → overlay → host wiring → binding → verify).
> All diagnostics use the existing `Telescope.DiagnosticLog.Telescope` = `"[Telescope] "`
> prefix. **No existing diagnostic format is changed.**

### BP-1 — Add `ImplementationHit` payload (pure, dependency-free)

- **Files:** create `Telescope/ImplementationHit.cs` (edit nothing else).
- **Change:** `namespace Telescope`, `public sealed class ImplementationHit` with a
  single ctor `ImplementationHit(string filePath, int lineNumber, string symbolName, string kind)`
  and four get-only props `string FilePath`, `int LineNumber`, `string SymbolName`,
  `string Kind` (null-coalesce filePath/symbolName/kind to `string.Empty`, mirroring
  `ReferenceHit`). Properties are read-only auto-properties; the class carries no DTE/Roslyn.
  This EXACT member set is mandated by the RED tests (see tests lines 739/750/767/786).
- **Verify-with:** compiles into the `Telescope` project; `dotnet build` no `CS0246 'ImplementationHit'`.
- **Fails-if:** `CS0246: 'ImplementationHit' could not be found`, or
  `CS1061: 'ImplementationHit?' has no 'FilePath'/'LineNumber'/'SymbolName'/'Kind'`
  (mis-named or missing prop), or ctor arity mismatch.

### BP-2 — Add `ImplementationFinder : IFinder`

- **Files:** create `Telescope/ImplementationFinder.cs` (mirror `ReferencesFinder.cs` precisely).
- **Change:**
  - Fields `Func<IReadOnlyList<ImplementationHit>> _gatherer`, `Action<ImplementationHit> _opener`.
  - `public string Name => "Implementation";`
  - `public ImplementationFinder(Func<IReadOnlyList<ImplementationHit>> gatherer, Action<ImplementationHit> opener)` — null-check throw, same as `ReferencesFinder` (public ctor — no internal DTE ctor; the gatherer/opener ARE the hermetic seams).
  - `GetCandidates()`: `ThreadHelper.ThrowIfNotOnUIThread()` guarded by `if (ThreadHelper.JoinableTaskContext != null)` (parity with `ReferencesFinder` so the offline test host does not throw); call `_gatherer() ?? Array.Empty<ImplementationHit>()` in try/catch (log `references gather`-style `implementations gather failed` via `System.Diagnostics.Debug.WriteLine` on exception); log `NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}implementations gathered count={hits.Count}")`; return `hits.Select(ToEntry).ToList()`.
  - `OnSelected(FinderEntry entry)`: same guard; `if (entry.Payload is not ImplementationHit hit) return;` then try `_opener(hit); NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}opened implementation: file={hit.FilePath} line={hit.LineNumber}");` catch → log `open implementation failed`.
  - `ToEntry(ImplementationHit hit)`: display = `$"{hit.Kind} {hit.SymbolName} — {Path.GetFileName(hit.FilePath)}:{hit.LineNumber}"` (em-dash `—` U+2014 with surrounding spaces; `Path.GetFileName` only, NOT the full path). Return `new FinderEntry(display, hit)`.
- **Verify-with:**
  - `Run_ImplementationFinder_DisplayFormatting` → `entries[0].Display == "Class Shape — Shape.cs:2"`.
  - `Run_ImplementationFinder_PayloadRoundTrips` → `ReferenceEquals(hit, entry.Payload)` + `payload.FilePath/LineNumber/SymbolName/Kind` values.
  - `Run_ImplementationFinder_OnSelectedOpensHitAtLine` → `opened != null` and `FilePath/LineNumber/SymbolName/Kind` preserved.
- **Fails-if:** `CS0246 'ImplementationFinder'`, or `CS1729` (ctor shape wrong), or `Assert.Equal` on `Display` (wrong separator/file-name formatting), or missing `implementations gathered count=...` line in e2e log.

### BP-3 — Overlay preview branch for `ImplementationHit`

- **Files:** edit `Telescope/TelescopeOverlay.cs` (`LoadPreviewForSelection`, ~line 463).
- **Change:** insert a new `if (payload is ImplementationHit init && System.IO.File.Exists(init.FilePath))` branch immediately after the `ReferenceHit` branch (before `GrepHit`), cloning the `ReferenceHit` body verbatim but using `init.LineNumber`: `SetPreviewContent(content)`; if `init.LineNumber > 0` → `_previewNavigator.MoveToLine(init.LineNumber); ApplyPreviewCaret(); Log preview caret={Caret} line={LineNumber}`; then `Log preview file={FilePath} chars={content.Length}`. **No change to the existing branches' log formats.**
- **Verify-with:** e2e `telescope-implementation` step 3 produces `[Telescope] preview file=.*Shape\.cs` + `[Telescope] preview caret=\d+ line=2`.
- **Fails-if:** preview stays empty / no `preview file=` for `Shape.cs` (branch absent or `payload is` cast misses because it was placed after the `string path` branch — keep it BEFORE the generic `string` branch), or `preview caret` line never appears.

### BP-4 — Host gatherer/opener + registration (`MyExtensionPackage`)

- **Files:** edit `MyExtension/MyExtensionPackage.cs`.
- **Change:**
  - Registration (after the existing `ReferencesFinder` registration at ~line 74): `_telescope.RegisterFinder(new ImplementationFinder(() => GatherImplementations(), hit => OpenImplementation(hit)));`
  - `private IReadOnlyList<ImplementationHit> GatherImplementations()` — mirrors `GatherReferences`'s symbol-at-caret resolution (workspace, `GetDocumentIdsWithFilePath`, `GetCaretOffset`, `FindSymbolAtPositionAsync`), then **diverges**: replace the `FindReferencesAsync` + `foreach rs.Locations` block with
    `var impls = ThreadHelper.JoinableTaskFactory.Run(() => Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindImplementationsAsync(symbol, solution));`
    and map each `ISymbol impl` to ONE `ImplementationHit`:
    - Take the FIRST in-source declaring position: `var src = impl.DeclaringSyntaxReferences.FirstOrDefault();` → `src.SyntaxTree.GetLineSpan(src.Span)` for path + 0-based line (+1 → 1-based). If `DeclaringSyntaxReferences` is empty, fall back to `impl.Locations.FirstOrDefault(l => l.IsInSource)` (skip if none). **SKIP any `impl` with no in-source location** (metadata types from referenced assemblies / `IsInSource == false`).
     - `Kind` = `impl is Microsoft.CodeAnalysis.INamedTypeSymbol nts ? nts.TypeKind.ToString() : impl.Kind.ToString()` (yields `Class`/`Interface`/`Struct`/`Method`/`Property`/`Event`); `SymbolName` = `impl.Name`; `LineNumber` = declaring line (1-based); `FilePath` = full path (mirroring `ReferenceHit.FilePath` — the opener/preview use full paths).
     - Do NOT iterate `rs.Locations` (that is the references mapping, and `FindImplementationsAsync` returns symbols, not reference locations — per the plan note).
     - **Deterministic ordering (REQUIRED — this is what makes the pinned `line=2` assertions deterministic):** after building the `ImplementationHit` list, return `hits.OrderBy(h => h.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.LineNumber).ToList()`. The seeded `Shape` type (declaring line 2) and its member `Shape.Draw` (declaring line 4) live in the SAME file `Shape.cs`, so `OrderBy(FilePath)` is a tie and `ThenBy(LineNumber)` is the discriminator: the type-level implementation (line 2) **sorts BEFORE any member implementation (`Shape.Draw`, line 4)**. Because the overlay defaults `selectedIndex=0`, this guarantees the first hit is always `Shape` line 2 — independent of Roslyn's non-deterministic internal `IEnumerable<ISymbol>` enumeration order. Do NOT rely on the returned enumeration order, and do NOT skip the member hits (they are legitimate candidates and must merely sort after the type).
  - `private void OpenImplementation(ImplementationHit hit)` — clone `OpenReference` verbatim but parameter type `ImplementationHit` (uses `hit.FilePath`, `hit.LineNumber`; `ThreadHelper.ThrowIfNotOnUIThread()`, `File.Exists` guard, `ItemOperations.OpenFile`, `TextSelection.GotoLine`). Does NOT log — the finder's `OnSelected` emits `opened implementation`.
- **Verify-with:** e2e `telescope-implementation` step 2 logs `[Telescope] implementations gathered count=\d+` (≥1) and step 4 logs `[Telescope] opened implementation: file=.*Shape\.cs line=2`.
- **Fails-if:** no `implementations gathered` line (gatherer throws or returns empty — verify caret resolves `IShape`), or the first hit is a member implementation (e.g. `Shape.Draw` at line 4) sorted BEFORE the type (`Shape` at line 2) so `line` reports `4` instead of `2` — meaning the deterministic `OrderBy(FilePath).ThenBy(LineNumber)` was omitted or the tie-break is wrong, or hits listed with `LineNumber`≠2 (metadata symbols NOT skipped, or first declaring ref not taken — e.g. a partial/`object` base location picked), or `opened implementation` never fires (opener not wired).

### BP-5 — Keybinding + action + open method (`InputHandler` + `default-keybindings.json`)

- **Files:** edit `MyExtension/default-keybindings.json` (add `"F,I": "telescope-implementation",` after `"F,R": "telescope-references",` line 18), edit `MyExtension/InputHandler.cs`.
- **Change:**
  - `default-keybindings.json`: one new line `"F,I": "telescope-implementation",` (preserve existing entries; trailing commas correct).
  - `InputHandler.ResolveAction` (line ~165): add `case "telescope-implementation": return () => OpenTelescopeImplementation();`.
  - `OpenTelescopeImplementation()`: clone `OpenTelescopeReferences()` verbatim (name + `_telescope.Open("Implementation", ...)` + `GetDebug`. log on failure `Failed to open Telescope implementation`). `ThreadHelper.ThrowIfNotOnUIThread()`.
- **Verify-with:** e2e `telescope-implementation` step 1/2 — `Space+F I` runs the binding and the overlay opens with `[Telescope] open finder=Implementation candidates=\d+`.
- **Fails-if:** `Space+F I` fall-through (no `open finder=Implementation` after 3 retries — missing binding line or missing `ResolveAction` case), or `open finder=` shows a DIFFERENT finder name (open-method opens the wrong finder).

### BP-6 — Build the solution

- **Files:** none (build only).
- **Change:** `dotnet build` (repo root).
- **Verify-with:** exit code 0; no warnings-as-errors from `CS0246`/`CS1061`/`CS0019`/`CS1729`.
- **Fails-if:** any of the RED compile errors persist (BP-1/BP-2 incomplete), or a `LangVersion`/`net472` API complaint (e.g. `IReadOnlySet<T>` sneaked in — do not use it).

### BP-7 — Run the 4 new unit tests

- **Files:** none (test exists at `tests/Telescope.Tests/Program.cs` ~737-792; no edit).
- **Change:** none — run `dotnet run --project tests/Telescope.Tests -- -- ImplementationFinder`.
- **Verify-with:** 4 `Run_ImplementationFinder_*` methods pass (exit code 0).
- **Fails-if:** `No tests matched.` (filter typo — the tests use prefix `ImplementationFinder`), or any assertion failure (display/round-trip/opener/line-mapping contract drift).

### BP-8 — Full offline suite + e2e gate

- **Files:** none.
- **Change:** `dotnet run --project tests/Telescope.Tests` (expect 56 = 52+N) and `dotnet run --project tests/NeoVisual.Tests` (25); then `pwsh tools/test-e2e.ps1 -Tests telescope-implementation` (this item's scenario) and finally the full `pwsh tools/test-e2e.ps1` (29 scenarios) at lifecycle.
- **Verify-with:** both unit suites exit 0; `telescope-implementation` passes (asserts `open finder=Implementation candidates=(\d+)` ≥1, `implementations gathered count=(\d+)` ≥1, `preview file=.*Shape\.cs` + `preview caret=\d+ line=2`, `opened implementation: file=.*Shape\.cs line=2`).
- **Fails-if:** any pre-existing scenario regresses (pay attention to `neovisual-editor-insert` — retry-once per allowlist), or the new scenario's `opened implementation` reports a line other than `2` (seed drift in `Shape.cs`).

---

## Verification Trace

| failing test/scenario | implicated steps | expected diagnostic |
|---|---|---|
| `Run_ImplementationFinder_DisplayFormatting` (unit) | BP-1, BP-2 | `entries[0].Display == "Class Shape — Shape.cs:2"` |
| `Run_ImplementationFinder_PayloadRoundTrips` (unit) | BP-1, BP-2 | `ReferenceEquals(hit, entry.Payload)` + `FilePath/LineNumber/SymbolName/Kind` intact |
| `Run_ImplementationFinder_OnSelectedOpensHitAtLine` (unit) | BP-1, BP-2 | `opened!.FilePath=="C:\p\Shape.cs"`, `LineNumber==2`, `SymbolName=="Shape"`, `Kind=="Class"` |
| `Run_ImplementationFinder_LineNumberDrivesPreviewJump` (unit) | BP-1 (shared `TextMotionNavigator.MoveToLine`) | `nav.LineNumber==3`, `nav.Caret==8` |
| `telescope-implementation` step 1-2 (open + gather) | BP-2, BP-4, BP-5 | `[Telescope] open finder=Implementation candidates=(\d+)` (≥1) + `[Telescope] implementations gathered count=\d+` (≥1) |
| `telescope-implementation` step 3 (preview jump) | BP-1, BP-3, BP-4 | `[Telescope] preview file=.*Shape\.cs` + `[Telescope] preview caret=\d+ line=2` — **deterministic via BP-4 `OrderBy(FilePath).ThenBy(LineNumber)`: type `Shape` (line 2) sorts before member `Shape.Draw` (line 4), so `selectedIndex=0` always previews line 2 regardless of Roslyn's enumeration order** |
| `telescope-implementation` step 4 (open at line) | BP-2, BP-4 | `[Telescope] opened implementation: file=.*Shape\.cs line=2` — **same BP-4 ordering guarantee: the first hit (type `Shape`, line 2) is what `selectedIndex=0` opens, never `Shape.Draw` line 4** |

**Known-RED allowlist (carried from the item plan → do NOT flag as regressions):**
- `neovisual-editor-insert` — pre-existing flake (retry-pass, 2/3); apply retry-once; not a regression.
- **No** allowlist on this item's own targets: `telescope-implementation` and `Run_ImplementationFinder_*` must be GREEN.