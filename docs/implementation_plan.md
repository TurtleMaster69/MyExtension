# Implementation Plan — Item: Telescope `references` finder (with preview line-jump + read/write access)

> **Lane: feature** — new capability (a new Telescope finder) AND a new
> `[Telescope] opened reference: ...` diagnostic line + a new default keybinding
> (`F,R`). **M-M7 HARD TRIGGER:** this plan ADDS a `[Telescope]` diagnostic and a
> diagnostic-observable contract → the FULL feature pipeline applies: initial-plan
> REVIEW, e2e RED booting VS, post-GREEN spec re-review.

---

**Goal:** Add the first of the user-requested Telescope finders roadmap items — a
**references finder** (`Space+F R`) that lists all references to the symbol at the
caret in the active document, shows each hit's **read/write access** (from VS's
find-references engine), previews the hit file with the caret jumped to the
reference line, and opens the file at that line on Enter.

---

## Approach

1. **`ReferencesFinder : IFinder`** in the `Telescope` project (mirror the
   `CodeIssuesFinder` shape — the closest precedent):
   - `Name => "References"`.
   - **Data source (read/write access):** VS's Roslyn "Find All References"
     engine — `IFindAllReferencesService` (Roslyn service in
     `Microsoft.VisualStudio.LanguageServices.dll`, provided by VS at runtime).
     Each reference reports `IsWrittenTo` (read vs write access) + file + line +
     column. The **host** (`MyExtensionPackage`/`InputHandler`) resolves the
     service — via MEF (`IComponentModel`) OR the active Roslyn workspace's
     `Services` (the implementation-planner/build-agent confirm the exact
     mechanism; do not over-commit the plan to one path) — and injects a
     `Func<IReadOnlyList<ReferenceHit>>` gatherer + a
     `Action<ReferenceHit>` opener into the finder, keeping the finder
     **hermetic-testable** (the `CodeIssuesFinder` seam). Any new package
     reference (e.g. `Microsoft.VisualStudio.LanguageServices`) follows the
     existing `ExcludeAssets="runtime"` pattern (VS supplies it at load time).
   - **`ReferenceHit` payload** (new pure class): `FilePath`, `LineNumber`
     (1-based), `Column` (1-based), `IsWrite` (read/write), `Symbol`,
     `LineText` (source line for display). Deterministic `Display` format,
     e.g. `{file}:{line}:{col} (read|write) — {symbol}` (exact format chosen by
     the builder; MUST be deterministic for the e2e assertions).
   - `GetCandidates()`: calls the injected gatherer (UI thread —
     `ThreadHelper.ThrowIfNotOnUIThread()`), maps hits to `FinderEntry`s
     (payload = `ReferenceHit`), swallows exceptions like `CodeIssuesFinder`.
     Logs a **gather summary diagnostic** (new):
     `[Telescope] references gathered reads=\d+ writes=\d+` so the harness can
     assert read/write coverage live (the results list itself is never
     per-candidate-logged).
   - `OnSelected(entry)`: opens the hit file (`dte.ItemOperations.OpenFile`),
     jumps to the line (`TextSelection.GotoLine` — **line-level navigation only;
     `col` is reported metadata, not a column jump**, matching
     `CodeIssuesFinder.GotoLine`), and logs the new diagnostic
     `[Telescope] opened reference: file=... line=... col=... access=read|write`.
   - **Gatherer contract (host side):** capture the ACTIVE document + caret
     symbol at gather time (Roslyn: `CurrentSolution` → active document →
     caret position → symbol at position → `IFindAllReferencesService.FindReferences`),
     return all reference hits (definition + references), each with read/write
     from `ReferenceLocation.IsWrittenTo`. The host logs the candidate count.

2. **Overlay preview support (new payload type):** extend
   `TelescopeOverlay.LoadPreviewForSelection` (`TelescopeOverlay.cs` ~line 402)
   with a `ReferenceHit` branch mirroring the `CodeIssue` branch: load the file
   content, `_previewNavigator.MoveToLine(hit.LineNumber)`, log the existing
   `[Telescope] preview file=...` + `[Telescope] preview caret=... line=...`
   lines. (Minimal seam extension — the F11 IFinder-preview refactor is a
   separate later item.)

3. **Keybinding + action:** rebind `"F,R": "command:File.OpenFile"` →
   `"F,R": "telescope-references"` in `MyExtension/default-keybindings.json`
   (no test pins the old binding — verified); add `case "telescope-references"`
   in `InputHandler.ResolveAction` + an `OpenTelescopeReferences()` method
   mirroring `OpenTelescopeIssues()` (opens the `"References"` finder centered
   over the VS main window). The host constructs the finder with the real
   gatherer/opener and registers it (`TelescopeController.RegisterFinder`) —
   finder construction lives with the other finders (check
   `MyExtensionPackage`/`InputHandler` wiring).

4. **Diagnostics contract (new — M-M7):**
   - `[Telescope] open finder=References candidates=(\d+)` — existing generic
     finder-open log (no format change; new finder name).
   - `[Telescope] references gathered reads=\d+ writes=\d+` — **NEW** diagnostic
     (gatherer summary; the harness's live read/write proof).
   - `[Telescope] opened reference: file=... line=... col=... access=(read|write)`
     — **NEW** diagnostic (the item's hard-trigger line; the regex in the harness
     must be `access=(read|write)` — `\|` would be a literal pipe).
   - Preview reuses `[Telescope] preview file=...` + `[Telescope] preview caret=\d+ line=\d+`.

---

## Acceptance criteria (each maps to a diagnostic line + a test)

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `Space+F R` opens the References finder with ≥2 candidates for a seeded multi-reference symbol | `[Telescope] open finder=References candidates=\d+` (≥2) | e2e `telescope-references` |
| A2 | Each candidate row shows the reference file/line and read/write access | (display format — asserted via unit test on `ReferenceHit` → `FinderEntry` mapping) | `Run_ReferencesFinder_*` (Telescope.Tests) |
| A3 | Preview loads the hit file and jumps the caret to the reference line | `[Telescope] preview file=.*\.cs` + `[Telescope] preview caret=\d+ line=\d+` | e2e scenario |
| A4 | Enter opens the file at the reference line and logs read/write access | `[Telescope] opened reference: file=.* line=\d+ col=\d+ access=(read\|write)` | e2e scenario |
| A5 | Read AND write hits both appear (seeded write site) — proven LIVE via the gather summary, not the unlogged results list | `[Telescope] references gathered reads=\d+ writes=\d+` with `writes≥1` (and unit tests cover per-hit read/write classification) | e2e scenario + unit test |
| A6 | Unit coverage of pure logic (display, payload, opener, line mapping) | n/a | `Run_ReferencesFinder_*` in Telescope.Tests |
| A7 | Existing suites stay green (Telescope 42 + new, NeoVisual 25) | — | full-suite final gate |

---

## Tests

### Offline unit tests (tests/Telescope.Tests, new — RED at unit level)

Mirror the `CodeIssuesFinder` hermetic-seam tests. Add a `Run_ReferencesFinder_*`
family (names at the builder's discretion) covering:
- **Display formatting:** a `ReferenceHit(file, line, col, isWrite, symbol, text)`
  renders `(read)` vs `(write)` deterministically; file/line/col/symbol present.
- **Payload passthrough:** `FinderEntry.Payload` round-trips the `ReferenceHit`.
- **Opener invocation:** `OnSelected` calls the injected opener with the correct
  hit (file/line/col/access).
- **Line mapping:** the hit's line number drives the preview jump (pure mapping).
RED: the tests reference a non-existent `ReferencesFinder`/`ReferenceHit` → the
Telescope.Tests build fails (missing symbol) — the right-reason unit RED.

Also check: no existing NeoVisual keybinding test asserts `F,R` (verified none) —
no test change needed there.

### E2E scenario: `telescope-references` (new, added via `Register-Scenario`)

Seeding (harness seeding must stay uniform-EOL per the hardening item):
- **Real compilable C#** (NOT comment stubs — Roslyn find-references needs an
  actual symbol graph): a `Shared` class with a public field, defined in one
  file (e.g. `Models/Shared.cs`) and **referenced from ≥2 other files**, with at
  least **one write site** (`shared.Value = 1`) and ≥1 read site — so the
  read/write contract (A5) is exercised deterministically.
- **Every new seeded file MUST be added to the `$canonical` map** in
  `Reset-ScratchSolution` + `Assert-SeedConsistent` (byte-exact, uniform CRLF —
  or pure LF if caret positions are pinned), or the seed-consistency self-check
  fails the run.

Scenario flow (each step asserting on the log):
1. Open the defining file via the overlay (`Space F T`, filter, Enter) → caret
   lands deterministically (e.g. line 1 col 0). Esc to normal mode.
2. Position the caret on the symbol name (deterministic motion, e.g. `0` +
   `w`/`e` — exact steps pinned by the builder against the seeded file content).
3. `Space+F R` → assert `[Telescope] open finder=References candidates=(\d+)`
   with count ≥ 2.
4. Assert the gather summary proves read+write coverage:
   `[Telescope] references gathered reads=\d+ writes=\d+` with `writes` ≥ 1.
5. Assert preview jumped: `[Telescope] preview file=.*\.cs` +
   `[Telescope] preview caret=\d+ line=\d+` (line = a seeded reference line).
6. Enter → assert `[Telescope] opened reference: file=.* line=\d+ col=\d+ access=(read|write)`.
7. `Close-Telescope`.

Diagnostics depended on: `[Telescope] open finder=References candidates=...`,
`[Telescope] references gathered reads=... writes=...`,
`[Telescope] preview file=...`, `[Telescope] preview caret=... line=...`,
`[Telescope] opened reference: ... access=(read|write)` (regex: `access=(read|write)` —
NOT `\|`, which is a literal pipe). Scenario count 26 → 27.

---

## RED evidence plan (e2e-test-builder, feature lane)

1. **Unit RED (no VS boot):** Telescope.Tests build fails — `ReferencesFinder` /
   `ReferenceHit` missing (missing symbol). Right-reason unit RED for A6.
2. **E2E RED (boots VS):** with the scenario registered but NO finder/binding
   implemented, `telescope-references` FAILS — `Space+F R` either opens the old
   `File.OpenFile` dialog (binding not yet rebound) or produces no
   `open finder=References` log line, and no `opened reference` line can appear.
   The failing assertions are exactly the missing-contract assertions — the
   right-reason RED for A1/A3/A4/A5.
3. The builder does NOT implement the finder (build-agent's job).

---

## Known-RED allowlist (for VERIFY)

- **None** of the affected scenarios/tests are allowlisted — `telescope-references`
  and the `Run_ReferencesFinder_*` tests are this item's targets and must be GREEN.
- Loop-time VERIFY runs `telescope-references` + Telescope.Tests; the final gate
  runs the FULL suite (now 27 scenarios) + both unit projects.
- All existing known-RED backlog items are FIXED (previous item) — the full suite
  has no known-RED scenarios as of this item's start.

---

## Execution Log

_To be appended by the hub on each attempt: attempt #, per-BP-step status,
debug/verifier verdict, capped evidence, and the cost line_
`delegations: N | VS boots: M | iterations: K`.

---

## Build Plan

> **Contract derived verbatim from the RED tests (`tests/Telescope.Tests/Program.cs`,
> lines 668–729) and the frozen harness (`tools/test-e2e.ps1`, scenario
> `telescope-references` + `Open-TelescopeReferences`).** The two NEW diagnostics MUST be
> byte-for-byte as below (the harness regexes `references gathered reads=(\d+) writes=(\d+)`
> and `opened reference: file=.*\.cs line=\d+ col=\d+ access=(read|write)` are pinned and frozen).

### KEY DECISIONS (do not second-guess)

1. **`ReferenceHit` + `ReferencesFinder` live in namespace `Telescope`, `public`** — the test
   project `Telescope.Tests` has NO `using Telescope;`; it relies on C# enclosing-namespace
   resolution, so the types must be in the `Telescope` namespace.
2. **`ReferencesFinder` has ONE constructor** `(Func<IReadOnlyList<ReferenceHit>> gatherer,
   Action<ReferenceHit> opener)` — the injected gatherer/opener are the finder's ONLY data
   source and action (there is NO DTE path inside the finder, unlike `CodeIssuesFinder`, so no
   `internal` test-only ctor is needed and the existing `internal`/`InternalsVisibleTo` split
   is irrelevant here).
3. **Find-references = MEF `IComponentModel` → `VisualStudioWorkspace` →
   `SymbolFinder.FindReferencesAsync`** (see BP-9 for exact type/method names). This mirrors
   `InputHandler.ResolveVimModeTracker`'s MEF resolution. Do not use the workspace
   `Services.GetRequiredLanguageService<IFindAllReferencesService>` route — `SymbolFinder` is
   the stable public API and returns the read/write (`ReferenceLocation.IsWrittenTo`) flag we
   need directly.
4. **The gatherer's exact caret→symbol mapping is the one BP that may need a small discovery
   iteration** (Roslyn version + active-view caret). Every other step is deterministic; do not
   let a Roslyn version bump ripple into the pure `Telescope` project or the diagnostics.
5. **`opened reference` col is metadata only** — the opener jumps LINE-level
   (`TextSelection.GotoLine(line, false)`), never column-level (matches `CodeIssuesFinder`).

### ReferenceHit API (derived from the 4 RED unit tests — implement EXACTLY)

Namespace `Telescope`. New file `Telescope/ReferenceHit.cs`:

```csharp
public sealed class ReferenceHit
{
    public ReferenceHit(string filePath, int lineNumber, int column, bool isWrite, string symbol, string lineText)
    {
        FilePath = filePath ?? string.Empty;
        LineNumber = lineNumber;
        Column = column;
        IsWrite = isWrite;
        Symbol = symbol ?? string.Empty;
        LineText = lineText ?? string.Empty;
    }
    public string FilePath { get; }   // full path (test: @"C:\p\Reader.cs")
    public int LineNumber { get; }    // 1-based (test asserts == 5)
    public int Column { get; }        // 1-based (test asserts == 5 / == 16)
    public bool IsWrite { get; }      // read=false, write=true (test asserts .IsWrite == true)
    public string Symbol { get; }     // "Value" (test asserts Display contains it)
    public string LineText { get; }   // source line (test passes "return Shared.Value;")
}
```

Constructor arg ORDER is fixed by the tests: `(filePath, lineNumber, column, isWrite, symbol,
lineText)`. All six are read-only auto-properties named exactly as above.

### ReferencesFinder API (derived — implement EXACTLY)

Namespace `Telescope`. New file `Telescope/ReferencesFinder.cs`:

```csharp
public sealed class ReferencesFinder : IFinder
{
    public string Name => "References";
    public ReferencesFinder(Func<IReadOnlyList<ReferenceHit>> gatherer, Action<ReferenceHit> opener); // null-check both (ArgumentNullException)
    public IReadOnlyList<FinderEntry> GetCandidates();   // ThreadHelper.ThrowIfNotOnUIThread(); calls _gatherer(), maps to FinderEntry(display, hit), logs gather summary
    public void OnSelected(FinderEntry entry);            // ThreadHelper.ThrowIfNotOnUIThread(); casts Payload to ReferenceHit, calls _opener(hit), logs opened reference
}
```

Display format (satisfies the tests' `.Contains("(read)")`, `"(write)"`, `"Reader.cs"`, `"Value"`):

```
$"{hit.Symbol} ({access}) {Path.GetFileName(hit.FilePath)}:{hit.LineNumber}:{hit.Column} — {hit.LineText}"
```

where `access = hit.IsWrite ? "write" : "read"`. Payload is the exact `ReferenceHit` instance
(`new FinderEntry(display, hit)`) — the test asserts `ReferenceEquals(hit, entry.Payload)`.

---

## Phase 1 — Pure Telescope library (unit-test GREEN, no VS boot)

### BP-1 — Add `ReferenceHit` payload class

- **Files:** create `Telescope/ReferenceHit.cs`.
- **Change:** the `public sealed class ReferenceHit` in `namespace Telescope` exactly as the
  "ReferenceHit API" block above. 6-arg ctor, 6 read-only auto-properties. No VS types, no
  `using` beyond `System` (none needed).
- **Verify-with:** `dotnet build` of `Telescope` + `tests/Telescope.Tests` compiles (clears
  `CS0246: ReferenceHit`); later `Run_ReferencesFinder_*` construct it.
- **Fails-if:** `CS0246: 'ReferenceHit' could not be found` persists after this step (the
  tests' `new ReferenceHit(...)` still won't resolve).

### BP-2 — Add `ReferencesFinder` + the two NEW diagnostics

- **Files:** create `Telescope/ReferencesFinder.cs`.
- **Change:** `public sealed class ReferencesFinder : IFinder` exactly per the
  "ReferencesFinder API" block. `GetCandidates()`:
  1. `ThreadHelper.ThrowIfNotOnUIThread();` (first line — parity with `CodeIssuesFinder` /
     `FileFinder` and the `IFinder` contract. **Harmless in the unit tests**: with
     `ThreadHelper` uninitialized outside VS its null `JoinableTaskContext` is treated as
     on-UI-thread, so it does not throw). Then
     `var hits = _gatherer() ?? Array.Empty<ReferenceHit>();` (wrap the call in try/catch that
     logs failure via `System.Diagnostics.Debug.WriteLine` and yields an empty list — mirror
     `CodeIssuesFinder`'s swallow).
  2. `int reads = hits.Count(h => !h.IsWrite); int writes = hits.Count(h => h.IsWrite);`
  3. `NeoVisualLog.Log($"{DiagnosticLog.Telescope}references gathered reads={reads} writes={writes}");`
     — **EXACT string** (no leading `file=`; `reads=` + `writes=` with single spaces).
  4. `return hits.Select(ToEntry).ToList();`
  `OnSelected(entry)`:
  1. `ThreadHelper.ThrowIfNotOnUIThread();` (same harmless-in-test reasoning as `GetCandidates`).
     `if (entry.Payload is not ReferenceHit hit) return;`
  2. try `_opener(hit);` then
     `NeoVisualLog.Log($"{DiagnosticLog.Telescope}opened reference: file={hit.FilePath} line={hit.LineNumber} col={hit.Column} access={(hit.IsWrite ? "write" : "read")}");`
     — **EXACT string**, `file=` (full path), `line=`, `col=`, `access=(read|write)`. catch → log `open reference failed: …`.
  `private static FinderEntry ToEntry(ReferenceHit hit)` with the display format above.
- **Verify-with:**
  - `dotnet run --project tests/Telescope.Tests -- ReferencesFinder` → all 4 pass:
    `Run_ReferencesFinder_DisplayShowsAccessMarker`, `Run_ReferencesFinder_PayloadRoundTrips`,
    `Run_ReferencesFinder_OnSelectedOpensHitWithAccess`,
    `Run_ReferencesFinder_LineNumberDrivesPreviewJump`.
  - Diagnostic contract pinned by the e2e regexes (see Verification Trace row for A5/A4).
- **Fails-if:** unit test asserts `display.Contains("(read)")` / `"(write)"` fail (wrong
  display string); `ReferenceEquals(hit, entry.Payload)` fails (payload re-wrapped); the
  `opened reference: … access=…` line never appears in the run log on Enter.

### BP-3 — `ReferenceHit` preview branch in `LoadPreviewForSelection`

- **Files:** modify `Telescope/TelescopeOverlay.cs` (`LoadPreviewForSelection`, ~line 402).
- **Change:** add an `else if` branch immediately after the `CodeIssue` branch (before the
  `payload is string path` branch), mirroring it byte-for-byte:
  ```csharp
  if (payload is ReferenceHit hit && System.IO.File.Exists(hit.FilePath))
  {
      try {
          string content = System.IO.File.ReadAllText(hit.FilePath);
          SetPreviewContent(content);
          if (hit.LineNumber > 0) {
              _previewNavigator.MoveToLine(hit.LineNumber);
              ApplyPreviewCaret();
              NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
          }
          NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={hit.FilePath} chars={content.Length}");
      }
      catch (Exception ex) { NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}"); }
      return;
  }
  ```
  Reuse the EXISTING `preview caret=… line=…` and `preview file=… chars=…` log formats
  (do NOT change them).
- **Verify-with:** `dotnet build`; e2e scenario Step 5 (`preview file=.*\.cs` +
  `preview caret=\d+ line=\d+`).
- **Fails-if:** step 5 assertion "preview load failed" or no `preview caret=` line when a
  `ReferenceHit` is selected (the `CodeIssue` branch fired but not the new one).

### BP-4 — Phase-1 gate: build + pure unit suite

- **Files:** none (verify only).
- **Change:** none.
- **Verify-with:** `dotnet build` (solution) clean; `dotnet run --project tests/Telescope.Tests`
  → 42 existing + 4 new all pass; `dotnet run --project tests/NeoVisual.Tests` → 25 pass.
- **Fails-if:** any compile error or a unit-test failure not caused by BP-9's Roslyn work.

---

## Phase 2 — Host wiring (keybinding → finder registration)

### BP-5 — Rebind `F,R` → `telescope-references`

- **Files:** modify `MyExtension/default-keybindings.json`.
- **Change:** replace the line `"F,R": "command:File.OpenFile",` with `"F,R": "telescope-references",`.
- **Verify-with:** live: `[NeoVisual] leader-binding executed: F,R` followed by
  `[Telescope] open finder=References` (instead of the Open File dialog). No offline test pins
  the old binding (verified).
- **Fails-if:** the Open File dialog still opens (old binding survived) — the log still shows
  `command:File.OpenFile` routing and no `open finder=References`.

### BP-6 — `ResolveAction` case + `OpenTelescopeReferences()`

- **Files:** modify `MyExtension/InputHandler.cs`.
- **Change:**
  1. In `ResolveAction` (switch, ~line 164) add
     `case "telescope-references": return () => OpenTelescopeReferences();` (next to
     `telescope-issues`).
  2. Add `private void OpenTelescopeReferences()` mirroring `OpenTelescopeIssues()`
     (lines 453–467): `ThreadHelper.ThrowIfNotOnUIThread();` → `var dte =
     CardinalNavigation.UtilityMethods.GetDTE(_package);` → `var centerRect =
     GetWindowRect(dte.MainWindow.HWnd);` → `_telescope.Open("References", centerRect,
     dte.MainWindow.HWnd);` → catch → `Debug.WriteLine`. The `"References"` name string must
     match `ReferencesFinder.Name`.
- **Verify-with:** `dotnet build`; live `Space+F R` resolves to action (no "Unknown action
  'telescope-references'" log) and opens the finder registered in BP-7.
- **Fails-if:** `[NeoVisual] Unknown action 'telescope-references' for binding 'F,R' - ignored.`
  logged (the `ResolveAction` case is missing → the binding is dropped).

### BP-7 — Construct + register `ReferencesFinder` in the package

- **Files:** modify `MyExtension/MyExtensionPackage.cs`.
- **Change:** in `InitializeAsync` (UI-thread block ~lines 68–71), after the `CodeIssuesFinder`
  registration add:
  ```csharp
  _telescope.RegisterFinder(new ReferencesFinder(
      () => GatherReferences(),
      hit => OpenReference(hit)));
  ```
  `GatherReferences()` and `OpenReference(hit)` are the private host methods implemented in
  BP-8 / BP-9 (declare them as `private IReadOnlyList<ReferenceHit> GatherReferences()` and
  `private void OpenReference(ReferenceHit hit)`). `using Telescope;` is already present.
- **Verify-with:** `dotnet build`; live `open finder=References candidates=(\d+)` (finder is
  reachable by `"References"`).
- **Fails-if:** `[Telescope] Unknown finder 'References'.` logged (finder not registered) even
  though `OpenTelescopeReferences` runs.

### BP-8 — Host opener `OpenReference(hit)` (DTE open + line-level goto)

- **Files:** modify `MyExtension/MyExtensionPackage.cs` (or a new `MyExtension/ReferencesFinderHost.cs`).
- **Change:** `private void OpenReference(ReferenceHit hit)`:
  ```csharp
  ThreadHelper.ThrowIfNotOnUIThread();
  if (!System.IO.File.Exists(hit.FilePath)) return;
  var dte = CardinalNavigation.UtilityMethods.GetDTE(this);
  dte.ItemOperations.OpenFile(hit.FilePath);
  if (dte.ActiveDocument?.Selection is EnvDTE.TextSelection sel && hit.LineNumber > 0)
      sel.GotoLine(hit.LineNumber, false);   // line-level ONLY; col is metadata
  ```
  No logging here — the `opened reference: …` line is emitted by `ReferencesFinder.OnSelected`
  (BP-2), which wraps this call.
- **Verify-with:** e2e Step 6 → `[Telescope] opened reference: file=.*\.cs line=\d+ col=\d+
  access=(read|write)` (the `opened file`+`opened reference` lines appear; the editor opens at
  the hit line).
- **Fails-if:** Enter opens the file but the `opened reference` line is absent or lacks
  `access=…` (opener wired incorrectly, or the finder's `OnSelected` logging was skipped).

### BP-9 — Host gatherer `GatherReferences()` (Roslyn find-references → `ReferenceHit[]`)

- **Files:** modify `MyExtension/MyExtension.csproj` (add package ref) + the gatherer method in
  `MyExtension/MyExtensionPackage.cs` (or new `MyExtension/ReferencesFinderHost.cs`).
- **Change (resolve mechanism — MEF, confirmed):**
  1. **Package reference** (compile-time only, VS supplies runtime):
     `<PackageReference Include="Microsoft.VisualStudio.LanguageServices" Version="17.14.*" ExcludeAssets="runtime" />`
     — this transitively pulls `Microsoft.CodeAnalysis.Common/CSharp/Workspaces` (for
     `Solution`, `Document`, `SemanticModel`, `SymbolFinder`, `ReferencedSymbol`,
     `ReferenceLocation`) and `Microsoft.VisualStudio.LanguageServices` (for
     `VisualStudioWorkspace`). **DISCOVERY:** if `17.14.*` doesn't restore, run
     `dotnet add package Microsoft.VisualStudio.LanguageServices` and pick the version whose
     Roslyn matches the installed VS 17.14 (the runtime DLL comes from the VS install, so a
     mismatched major → `FileLoadException` in `GatherReferences` — bump to match and rebuild).
  2. **Gatherer body** (`private IReadOnlyList<ReferenceHit> GatherReferences()`):
     ```csharp
     ThreadHelper.ThrowIfNotOnUIThread();
     var dte = CardinalNavigation.UtilityMethods.GetDTE(this);
     var active = dte?.ActiveDocument;
     if (active == null) return Array.Empty<ReferenceHit>();

     var componentModel = ((System.IServiceProvider)this).GetService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel))
         as Microsoft.VisualStudio.ComponentModelHost.IComponentModel;
     var workspace = componentModel?.GetService<Microsoft.VisualStudio.LanguageServices.VisualStudioWorkspace>();
     if (workspace == null) return Array.Empty<ReferenceHit>();

     var solution = workspace.CurrentSolution;
     var filePath = active.FullName;
     var docId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault();
     if (docId == null) return Array.Empty<ReferenceHit>();
     var document = solution.GetDocument(docId);
     if (document == null) return Array.Empty<ReferenceHit>();

     // Caret offset: prefer the active editor text view (robust under VsVim). Fall back to
     // DTE TextSelection line/col -> SourceText offset if the view is unavailable.
     int caret = GetCaretOffset(dte, active, document);   // see note below; -1 => give up
     if (caret < 0) return Array.Empty<ReferenceHit>();

     var root = ThreadHelper.JoinableTaskFactory.Run(
         () => document.GetSyntaxRootAsync(System.Threading.CancellationToken.None));
     var semanticModel = ThreadHelper.JoinableTaskFactory.Run(
         () => document.GetSemanticModelAsync(System.Threading.CancellationToken.None));
     var symbol = ThreadHelper.JoinableTaskFactory.Run(() =>
         Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindSymbolAtPositionAsync(semanticModel, caret, workspace));
     if (symbol == null) return Array.Empty<ReferenceHit>();

     var refs = ThreadHelper.JoinableTaskFactory.Run(() =>
         Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindReferencesAsync(symbol, solution));

     var hits = new List<ReferenceHit>();
     foreach (var rs in refs)
         foreach (var loc in rs.Locations)
         {
             var span = loc.Location.GetLineSpan();
             if (!span.IsValid) continue;
             string path = span.Path;
             int line = span.StartLinePosition.Line + 1;       // 0-based -> 1-based
             int col  = span.StartLinePosition.Character + 1; // 0-based -> 1-based
             hits.Add(new ReferenceHit(path, line, col, loc.IsWrittenTo, symbol.Name, ReadLine(path, line)));
         }
     return hits;
     ```
      `ReferenceLocation.IsWrittenTo` (`loc.IsWrittenTo`) is the read/write source of truth.
      `symbol.Name` = the `Value` name for the display. `ReadLine(path, line)` reads that source
      line via `System.IO.File.ReadLines` (defensive try/catch → `string.Empty`). **NOTE:**
      `SymbolFinder.FindReferencesAsync` returns a `ReferencedSymbol` whose `.Locations` hold
      REFERENCES ONLY — the `Shared.Value` declaration/definition site is NOT present in
      `.Locations` (find-references does not return the definition itself). The expected result
      is therefore exactly **2 candidates**: `Reader.cs` (read, `IsWrittenTo=false`) +
      `Writer.cs` (write, `IsWrittenTo=true`) → `reads=1 writes=1`. This satisfies the harness
      (`candidates ≥ 2`, `reads ≥ 1`, `writes ≥ 1`) — do NOT mis-diagnose a correct 2-candidate
      result as "missing the definition"; the definition is correctly absent.
  3. **Caret offset note (`GetCaretOffset`):** resolve the active editor view —
     `IVsTextManager` (`SVsTextManager`) → `GetActiveView(1, null, out IVsTextView)` →
     `componentModel.GetService<Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService>()
     .GetWpfTextView(view)` → `.Caret.Position.BufferPosition.Position` (0-based). If that chain
     returns null, fall back to DTE `TextSelection.ActivePoint` `Line`/`DisplayColumn` (both
     1-based) mapped to an offset via `ThreadHelper.JoinableTaskFactory.Run(() => document.GetTextAsync(…)).Lines`
     (safe for the space-indented seed file). This is the semi-discovery sub-step.

     **UI-thread discipline (CRITICAL):** NO blocking sync-waits anywhere in
     `GatherReferences()` — never `.Result`, never `.GetAwaiter().GetResult()` (those deadlock /
     starve the VS UI thread on Roslyn async APIs). Every async Roslyn call (`GetSyntaxRootAsync`,
     `GetSemanticModelAsync`, `FindSymbolAtPositionAsync`, `FindReferencesAsync`, `GetTextAsync`)
     is wrapped in `ThreadHelper.JoinableTaskFactory.Run(() => …)` as shown above.
- **Verify-with:** e2e Steps 3–4 → `open finder=References candidates=(\d+)` count ≥ 2 and
  `references gathered reads=(\d+) writes=(\d+)` with `writes ≥ 1`; Step 5 preview jumps to a
  reference line.
- **Fails-if:** `references gathered reads=0 writes=0` (no symbol resolved — caret offset wrong
  or `FindReferencesAsync` returned nothing); `FileLoadException`/`MissingMethodException` on
  Roslyn types (package/version mismatch — fix the reference, do NOT change the finder).

### BP-10 — Full build + both unit suites + live `telescope-references`

- **Files:** none (verify only).
- **Change:** none.
- **Verify-with:**
  - `dotnet build` (whole solution) clean.
  - `dotnet run --project tests/Telescope.Tests` → 46 pass (42 + 4 new).
  - `dotnet run --project tests/NeoVisual.Tests` → 25 pass.
  - `pwsh tools/test-e2e.ps1 -Tests telescope-references` → exit 0 (all 7 scenario asserts).
  - Final gate: `pwsh tools/test-e2e.ps1` full suite (now 27 scenarios) clean.
- **Fails-if:** any of the above RED. NOTE: `tools/test-e2e.ps1` is FROZEN — never edit it; any
  mismatch is a finder/diagnostic bug, not a harness bug.

---

## Verification Trace

`$PfxTel` = `[Telescope] ` (the `DiagnosticLog.Telescope` constant). The harness uses a fixed
per-scenario log baseline, so ordering of the summary vs `open finder` line does not matter —
each assert scans lines appended after the baseline.

| failing test / scenario | implicated BP steps | expected diagnostic (exact) |
|---|---|---|
| `Run_ReferencesFinder_DisplayShowsAccessMarker` (unit) | BP-1, BP-2 | n/a — asserts `Display.Contains("(read)")`, `"(write)"`, `"Reader.cs"`, `"Value"` |
| `Run_ReferencesFinder_PayloadRoundTrips` (unit) | BP-1, BP-2 | n/a — asserts `ReferenceEquals(hit, entry.Payload)` + `FilePath/LineNumber/IsWrite` |
| `Run_ReferencesFinder_OnSelectedOpensHitWithAccess` (unit) | BP-1, BP-2 | n/a — asserts injected opener receives hit w/ `FilePath=… LineNumber=5 Column=5 IsWrite=true` |
| `Run_ReferencesFinder_LineNumberDrivesPreviewJump` (unit) | BP-1 | n/a — asserts `MoveToLine(3)` → `LineNumber=3`, `Caret=8` |
| `telescope-references` — step 1 (open Shared.cs) | none — existing `telescope`/`F,T` Files finder path (already green, untouched by this item; a regression here is pre-existing) | `[Telescope] opened file: .*Shared\.cs` |
| `telescope-references` — step 3 (finder opens ≥2) | BP-5, BP-6, BP-7, BP-9 | `[Telescope] open finder=References candidates=\d+` (count ≥2) |
| `telescope-references` — step 4 (read+write summary) | BP-2, BP-9 | `[Telescope] references gathered reads=\d+ writes=\d+` (reads≥1, writes≥1) |
| `telescope-references` — step 5 (preview line-jump) | BP-3, BP-9 | `[Telescope] preview file=.*\.cs` + `[Telescope] preview caret=\d+ line=\d+` |
| `telescope-references` — step 6 (Enter opens w/ access) | BP-2, BP-8 | `[Telescope] opened reference: file=.*\.cs line=\d+ col=\d+ access=(read\|write)` |

**Known-RED allowlist (carried from the item plan — VERIFY must not flag these as regressions):**
- **None.** `telescope-references` and the four `Run_ReferencesFinder_*` tests are this item's
  GREEN targets. All prior known-RED backlog items are FIXED as of this item's start; the final
  gate is the full 27-scenario e2e suite + both unit suites, all green.

---

## DEVIATIONS — adjudicated (hub, M-M3, before VERIFY)

BUILD reported 3 mechanism-level adaptations. **No diagnostic / API / contract change** — the
four frozen unit tests and all diagnostic format strings are byte-exact as planned; the
Verification Trace is unchanged.

- `DEVIATION-1 -> ACCEPT`: `Microsoft.VisualStudio.LanguageServices` package is
  Roslyn-versioned — `17.14.*` does not exist on nuget.org; used **`4.14.0`** (Roslyn 4.14 =
  VS 17.14). This was BP-9's explicit discovery fallback. (Reason: no contract impact.)
- `DEVIATION-2 -> ACCEPT`: `ReferenceLocation.IsWrittenTo` is **internal** in Roslyn 4.14;
  adapted with `IsWriteLocation(loc)` using reflection on the stable `IsWrittenTo` property name
  (the repo's established VsVim-interop reflection pattern — no committed third-party binaries).
  The observable `access=read|write` classification is unchanged. (Reason: contract-preserving
  mechanism change.)
- `DEVIATION-3 -> ACCEPT`: `ThreadHelper.ThrowIfNotOnUIThread()` throws in the offline unit-test
  host (JoinableTaskContext never touched); guarded with `if (ThreadHelper.JoinableTaskContext != null)`
  — implementing the plan's stated intent (null context treated as on-UI-thread; the real assert
  runs inside VS). (Reason: test-host-only guard, no API/signature change.)

---

## Execution Log

### Attempt 1 (2026-09-19)

- RED (e2e-test-builder): unit RED = 15 missing-symbol errors (`ReferenceHit`/`ReferencesFinder`);
  e2e RED = `telescope-references` fails (F,R still bound to File.OpenFile; no
  `open finder=References` / `references gathered` / `opened reference`). Right-reason, matches plan.
- PLAN (implementation-planner): BP-1..BP-10 + 9-row Verification Trace. PLAN REVIEW round 1 =
  REVISE (5 findings); round 2 = APPROVE.
- BUILD (build-agent): all BP-1..BP-10 **done**. `dotnet build` exit 0 (87 pre-existing warnings);
  Telescope.Tests **46/46** (42+4), NeoVisual.Tests **25/25**. 3 DEVIATIONS adjudicated ACCEPT (above).
- VERIFY: pending.
- Cost: `delegations: 6 | VS boots: 1 (RED) | iterations: 0 (no regression yet)`