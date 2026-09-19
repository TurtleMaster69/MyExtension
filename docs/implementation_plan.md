# Implementation Plan — Item: Telescope `grep` finder (query-driven, with preview pane)

> **Lane: feature** — new capability (a query-driven Telescope finder) AND a new
> `[Telescope] grep hits=...` + `[Telescope] opened grep: ...` diagnostic + a new
> default keybinding (`F,G` rebind). **M-M7 HARD TRIGGER:** ADDS `[Telescope]`
> diagnostics → full feature pipeline: initial-plan REVIEW, e2e RED booting VS,
> post-GREEN spec re-review.

---

**Goal:** Add the second Telescope finders roadmap item — a **grep finder**
(`Space+F G`) that searches the solution's project files for the typed query
(query-driven — each keystroke re-runs the search, unlike the fzf-filtered
finders), shows each hit's file/line/text, previews the hit file with the caret
jumped to the hit line, and opens the file at that line on Enter.

---

## Approach

1. **Query-driven finder seam (minimal, F11-adjacent):** new capability interface
   `IQueryFinder` in `Telescope/` (`IReadOnlyList<FinderEntry> GetCandidates(string query)`)
   alongside `IFinder`. `TelescopeOverlay.FilterAndUpdateAsync` gains a branch:
   when the active finder is an `IQueryFinder`, re-gather candidates from
   `GetCandidates(query)` and render them DIRECTLY — **skipping fzf** (grep
   semantics are literal, not fuzzy). The static fzf-filter path for
   `Files`/`Issues`/`References` is untouched.
   **Debounce is a FIRST-CLASS task of this item (NOT pre-existing):**
   `RefreshResults` currently runs the filter immediately per keystroke (no
   debounce — only a CTS that cancels the *fzf await*). A query-driven gather
   does a synchronous full-solution scan on the UI thread (DTE file
   enumeration), so each keystroke would stall VS. The item must add a small
   debounce gate (e.g. a ~200ms `DispatcherTimer`/delay in `RefreshResults`,
   armed only while the active finder is an `IQueryFinder`) so the scan runs
   after typing settles; the UI-thread cost of each settle-scan is bounded by
   the debounce + the hit cap. The e2e harness polls log lines for up to
   seconds, so ~200ms is safe for assertions. The Build Plan must include the
   debounce as an explicit BP step with its own Verify-with (typed-then-settled
   query still yields `grep hits=N`).
2. **`GrepFinder : IFinder, IQueryFinder`** in the `Telescope` project (mirror
   `CodeIssuesFinder`'s hermetic seams + the references-finder host-injection
   pattern):
   - `Name => "Grep"`.
   - `GetCandidates(query)` (the query seam): empty query → **empty result set**
     (deterministic initial state); otherwise scan the solution's project files
     (`ProjectFiles.Enumerate(dte)` — the shared walker) for case-insensitive
     substring matches, build one `FinderEntry` per hit — display
     `{fileName}:{line}: {lineText}` (exact format chosen by the builder, must be
     deterministic), payload = a new pure `GrepHit(filePath, lineNumber, lineText)`.
     Cap total hits (e.g. ≤200) for responsiveness. Log a gather summary
     `[Telescope] grep hits=N` per gather.
   - `OnSelected(entry)`: open the hit file (`dte.ItemOperations.OpenFile`),
     jump to the line (`TextSelection.GotoLine`), log the new diagnostic
     `[Telescope] opened grep: file=... line=...`.
   - Hermetic test ctor mirroring `CodeIssuesFinder` (injected
     `Func<IReadOnlyList<string>>` file-**path** source + `Action<GrepHit>`
     opener): the finder reads file CONTENT off disk from the injected paths
     (the `Run_Issues_*` pattern — tests create real temp files with known
     content and inject their paths), so the line-scan/matching/display/opener
     logic is unit-testable without DTE.
3. **Overlay preview support:** extend `TelescopeOverlay.LoadPreviewForSelection`
   with a `GrepHit` branch (load file content + `_previewNavigator.MoveToLine`,
   reusing the existing `preview file=` / `preview caret=... line=...` logs) —
   mirroring the `CodeIssue`/`ReferenceHit` branches.
4. **Keybinding + action:** rebind `"F,G": "command:Edit.FindinFiles"` →
   `"F,G": "telescope-grep"` in `MyExtension/default-keybindings.json` (nothing
   pins the old binding — verified; it is VS's own Find-in-Files, which this
   finder replaces). Add `case "telescope-grep"` in `InputHandler.ResolveAction` +
   `OpenTelescopeGrep()` mirroring `OpenTelescopeReferences()`; the host
   constructs `GrepFinder` with the real DTE file source/opener and registers it
   (`TelescopeController.RegisterFinder`).
5. **Diagnostics contract (new — M-M7):**
   - `[Telescope] open finder=Grep candidates=(\d+)` — existing generic
     finder-open log (empty-query gather → candidates=0, deterministic).
   - `[Telescope] grep hits=\d+` — **NEW** (per-query gather summary).
   - `[Telescope] opened grep: file=... line=\d+` — **NEW** (OnSelected).
   - Preview reuses `[Telescope] preview file=...` + `[Telescope] preview caret=\d+ line=\d+`.

---

## Acceptance criteria (each maps to a diagnostic line + a test)

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `Space+F G` opens the Grep finder (empty query → no candidates) | `[Telescope] open finder=Grep candidates=0` | e2e `telescope-grep` |
| A2 | Typing a distinctive seeded token returns the expected hits (query-driven, no fzf) | `[Telescope] grep hits=\d+` = the seeded hit count | e2e scenario |
| A3 | Each hit row shows file/line/text (deterministic display) | (display asserted via unit test on the scan→entry mapping) | `Run_GrepFinder_*` (Telescope.Tests) |
| A4 | Preview jumps to the hit line | `[Telescope] preview file=.*\.cs` + `[Telescope] preview caret=\d+ line=\d+` | e2e scenario |
| A5 | Enter opens the file at the SPECIFIC seeded hit line | `[Telescope] opened grep: file=.* line=<the pinned 1-based hit line>` | e2e scenario |
| A6 | Existing fzf finders (Files/Issues/References) unaffected by the query seam | existing scenarios stay green | full-suite final gate |
| A7 | Unit coverage of scan/matching/display/opener (hermetic) | n/a | `Run_GrepFinder_*` in Telescope.Tests |
| A8 | Existing suites stay green (Telescope 46+N, NeoVisual 25) | — | full-suite final gate |

---

## Tests

### Offline unit tests (tests/Telescope.Tests, new — RED at unit level)

Mirror the `CodeIssuesFinder` hermetic tests. Add a `Run_GrepFinder_*` family
(names at the builder's discretion) covering:
- **Empty query:** `GetCandidates("")` → 0 entries (deterministic initial state).
- **Line scanning:** temp files with known content (the `Run_Issues_*` pattern —
  real temp files, injected paths) return exactly the lines containing the
  (case-insensitive) token; non-matching lines excluded.
- **Display format:** `{fileName}:{line}: {text}` deterministic; payload round-trips the `GrepHit`.
- **Opener invocation:** `OnSelected` calls the injected opener with the right file/line.
- **Hit cap:** a file with >cap matches is capped (≤200).
RED: the tests reference a non-existent `GrepFinder`/`GrepHit` → Telescope.Tests
build fails (missing symbol) — the right-reason unit RED.

### E2E scenario: `telescope-grep` (new, added via `Register-Scenario`)

Seeding: reuse the existing deterministic symbol graph from the references item
(`Shared` in `Models/Shared.cs` + `Reader.cs` + `Writer.cs` — already in
`$canonical`) OR add one distinctive seeded token (e.g. a `// GREPME` marker in
≥2 known files). Exact token + expected hit count pinned by the builder — the
token must appear in a KNOWN number of lines so `grep hits=N` is exact.

Scenario flow (each step asserting on the log):
1. `Space+F G` → assert `[Telescope] open finder=Grep candidates=0` (empty query).
2. Type the distinctive token → assert `[Telescope] grep hits=N` with the exact
   seeded count (query-driven gather).
3. Assert preview jumped: `[Telescope] preview file=.*\.cs` +
   `[Telescope] preview caret=\d+ line=\d+` (line = a seeded hit line).
4. Enter → assert `[Telescope] opened grep: file=.* line=<pinned 1-based hit line>`
   — the line number pinned to the exact seeded hit line (like the references
   item's `preview caret=... line=...` pins), so the "at the hit line" criterion
   is genuinely proven end-to-end.
5. `Close-Telescope`.

Diagnostics depended on: `[Telescope] open finder=Grep candidates=...`,
`[Telescope] grep hits=...`, `[Telescope] preview file=...`,
`[Telescope] preview caret=... line=...`, `[Telescope] opened grep: ...`.
Scenario count 27 → 28.

---

## RED evidence plan (e2e-test-builder, feature lane)

1. **Unit RED (no VS boot):** Telescope.Tests build fails — `GrepFinder`/`GrepHit`
   missing (missing symbol). Right-reason unit RED for A7.
2. **E2E RED (boots VS):** with the scenario registered but NO finder/binding
   implemented, `telescope-grep` FAILS — `Space+F G` still runs the old
   `Edit.FindinFiles` command (Find-in-Files dialog) or nothing; no
   `open finder=Grep`, `grep hits`, or `opened grep` lines appear. The failing
   assertions are exactly the missing-contract assertions — right-reason RED for
   A1/A2/A4/A5.
3. The builder does NOT implement the finder (build-agent's job).

---

## Known-RED allowlist (for VERIFY)

- **None** of the affected scenarios/tests are allowlisted — `telescope-grep` and
  the `Run_GrepFinder_*` tests are this item's targets and must be GREEN.
- Loop-time VERIFY runs `telescope-grep` + Telescope.Tests; the final gate runs
  the FULL suite (now 28 scenarios) + both unit projects. One pre-existing flake
  is on record: `neovisual-editor-insert` (retry-pass, flaky count 1/3) — the
  verifier applies the standard retry-once policy; it must not be treated as a
  regression.

---

## Build Plan

> **KEY DECISIONS (do not second-guess):**
> 1. `GrepFinder` mirrors **`CodeIssuesFinder`**, not `ReferencesFinder`: public
>    `GrepFinder(Func<DTE> dteFactory)` ctor + `internal GrepFinder(Func<IReadOnlyList<string>>, Action<GrepHit>)`
>    test ctor. The finder is self-contained — `GetCandidates(query)` calls
>    `ProjectFiles.Enumerate(dte)` and reads file CONTENT internally, and
>    `OnSelected` does its own `dte.ItemOperations.OpenFile` + `GotoLine`. The host
>    therefore registers the SAME one-liner as FileFinder/CodeIssuesFinder
>    (`new GrepFinder(() => UtilityMethods.GetDTE(this))`) — there is **no separate
>    host-side opener**, despite the item-plan wording.
> 2. The `(Func<IReadOnlyList<string>> fileSource, Action<GrepHit> opener)` ctor is
>    the **internal test ctor** (mirrors `CodeIssuesFinder._testFileSource`/
>    `_testOpener`); the `fileSource` yields **full file paths** and the finder reads
>    their CONTENT off disk (the `Run_Issues_*` real-temp-file pattern).
> 3. The debounce is a **generation counter + `Task.Delay(200)`** (`QueryDebounceMs`)
>    in `TelescopeOverlay` — no `DispatcherTimer` to dispose; the await captures the
>    WPF `SynchronizationContext` so the synchronous scan resumes on the UI thread.
> 4. Diagnostic formats are byte-for-byte: `open finder=Grep candidates=0` (existing
>    generic log), `grep hits={count}`, `opened grep: file={path} line={line}`, plus
>    the existing `preview file=... chars=...` / `preview caret=... line=...`.
> 5. `IQueryFinder` and `GrepFinder`/`GrepHit` are **public**; `InternalsVisibleTo` is
>    already set for `Telescope.Tests`, so the internal test ctor is reachable.

---

### BP-1 — Create `GrepHit` (pure payload model)

- **Files:** create `Telescope/GrepHit.cs`.
- **Change:** `public sealed class GrepHit` (namespace `Telescope`) with a ctor
  `GrepHit(string filePath, int lineNumber, string lineText)` and three get-only
  properties — `string FilePath` (default `""`), `int LineNumber`, `string LineText`
  (default `""`). This exactly matches `ReferenceHit`/`CodeIssue` conventions. No VS
  or WPF dependencies.
- **Verify-with:** `dotnet build` compiles (the 6 RED tests still fail on the
  MISSING `GrepFinder`, but `GrepHit` symbol now resolves — the `CS0246:
  'GrepHit' could not be found` errors disappear).
- **Fails-if:** `dotnet build` still reports `CS0246: 'GrepHit' could not be found`;
  or a compile error inside `GrepHit` (property/ctor mismatch with the tests'
  `hit.FilePath` / `.LineNumber` / `.LineText` accesses and
  `new GrepHit(...)` usages in the test ctor seams).

### BP-2 — Create `IQueryFinder` capability interface

- **Files:** create `Telescope/IQueryFinder.cs`.
- **Change:** `public interface IQueryFinder { IReadOnlyList<FinderEntry> GetCandidates(string query); }`
  (namespace `Telescope`). This is the query-driven seam the overlay branches on.
- **Verify-with:** `dotnet build` compiles (no tests target this directly).
- **Fails-if:** `dotnet build` errors on a missing `IQueryFinder` symbol when
  `GrepFinder` (BP-3) or the overlay branch (BP-4) references it.

### BP-3 — Create `GrepFinder` (`IFinder` + `IQueryFinder`)

- **Files:** create `Telescope/GrepFinder.cs`.
- **Change:** `public sealed class GrepFinder : IFinder, IQueryFinder`:
  - `public string Name => "Grep"`.
  - `public GrepFinder(Func<DTE> dteFactory)` — production ctor (throws on null).
  - `internal GrepFinder(Func<IReadOnlyList<string>> fileSource, Action<GrepHit> opener)`
    — test ctor (mirrors `CodeIssuesFinder`; stores as `_testFileSource`/`_testOpener`,
    sets `_dteFactory = () => null!`).
  - `public IReadOnlyList<FinderEntry> GetCandidates()` (IFinder) → `return GetCandidates(string.Empty);`.
  - `public IReadOnlyList<FinderEntry> GetCandidates(string query)` (IQueryFinder):
    - `string.IsNullOrEmpty(query)` → `return Array.Empty<FinderEntry>();` (NO log,
      so the empty open emits only `open finder=Grep candidates=0`).
    - Resolve the file list: test path → `_testFileSource()`; production → assert
      UI thread, `ProjectFiles.Enumerate(_dteFactory())` (empty if no solution).
    - For each path, `File.ReadAllLines` and scan each line for `line.IndexOf(query,
      StringComparison.OrdinalIgnoreCase) >= 0`; add a `GrepHit(path, i + 1, line)`;
      stop at **`HitCap = 200`** total hits (break loop + early-return in the scan).
    - Log `NeoVisualLog.Log($"{DiagnosticLog.Telescope}grep hits={hits.Count}")`
      AFTER the scan.
    - Return `hits.Select(ToEntry).ToList()`; `ToEntry` produces
      `$"{Path.GetFileName(hit.FilePath)}:{hit.LineNumber}: {hit.LineText}"` with
      `new FinderEntry(display, hit)`.
  - `public void OnSelected(FinderEntry entry)`:
    - `entry.Payload is not GrepHit hit` → return.
    - test path → `_testOpener(hit); return;`.
    - production → assert UI thread; `if (!File.Exists(hit.FilePath)) return;`;
      `_dteFactory().ItemOperations.OpenFile(hit.FilePath)`; `GotoLine(dte, hit.LineNumber)`
      (private static, mirrors `CodeIssuesFinder.GotoLine`); log
      `$"{DiagnosticLog.Telescope}opened grep: file={hit.FilePath} line={hit.LineNumber}"`.
      Wrap in try/catch logging `open grep failed: ...`.
- **Verify-with:** `dotnet run --project tests/Telescope.Tests -- -- GrepFinder` →
  the 6 tests pass: `Run_GrepFinder_EmptyQueryReturnsZeroCandidates`,
  `Run_GrepFinder_LineScanMatchesCaseInsensitive`,
  `Run_GrepFinder_DisplayIsFileNameLineText`,
  `Run_GrepFinder_PayloadRoundTripsGrepHit`,
  `Run_GrepFinder_OnSelectedOpensHitAtLine`, `Run_GrepFinder_HitCapBounded`.
- **Fails-if:** any of those 6 fail — specifically: `DisplayIsFileNameLineText` fails
  if display ≠ `"A.cs:2: NEEDLE here"` (format `{file}:{line}: {text}` broken);
  `PayloadRoundTripsGrepHit` fails if `FilePath`/`LineNumber`/`LineText` don't
  round-trip exactly; `HitCapBounded` fails if >200 entries (cap not enforced);
  `LineScanMatchesCaseInsensitive` fails if `OrdinalIgnoreCase` isn't used or
  non-matching lines leak; `EmptyQueryReturnsZeroCandidates` fails if the empty
  query returns any entry; `OnSelectedOpensHitAtLine` fails if the opener isn't
  invoked with the exact hit.

### BP-4 — Overlay query-driven branch + debounce (skip fzf)

- **Files:** modify `Telescope/TelescopeOverlay.cs`.
- **Change:**
  - Add field `private const int QueryDebounceMs = 200;` and `private int _queryGeneration;`.
  - In `RefreshResults(string query)` (currently lines 332-340), after
    `CancelFilter();` insert the branch:
    ```csharp
    if (_activeFinder is IQueryFinder queryFinder)
    {
        _ = RefreshQueryDrivenAsync(queryFinder, query);
        return;
    }
    ```
    (the existing fzf `_filterCts` + `FilterAndUpdateAsync` path stays untouched for
    Files/Issues/References).
  - Add method:
    ```csharp
    private async Task RefreshQueryDrivenAsync(IQueryFinder finder, string query)
    {
        int gen = ++_queryGeneration;
        await Task.Delay(QueryDebounceMs); // resumes on the UI thread (SynchronizationContext)
        if (gen != _queryGeneration || !IsOpen) return;
        IReadOnlyList<FinderEntry> results;
        try { results = finder.GetCandidates(query) ?? Array.Empty<FinderEntry>(); }
        catch (Exception ex) { NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}query gather failed: {ex.Message}"); results = Array.Empty<FinderEntry>(); }
        if (gen != _queryGeneration || !IsOpen) return;
        _results = results;
        _keyHandler.SetResults(results.Count);
        RenderResults();
    }
    ```
- **Verify-with:** e2e `telescope-grep` — after typing `GREPME`, the log shows
  `[Telescope] grep hits=2` exactly once (the settle-scan), proving the query-driven
  path runs (skipping fzf) AFTER the ~200ms debounce.
- **Fails-if:** no `grep hits=2` line appears after typing (branch never fires);
  OR `grep hits=1`/`grep hits=...` only for a partial prefix then nothing (debounce
  fires per keystroke → generation counter broken); OR the fzf path is still hit for
  Grep (you'd see fzf `results count` without `grep hits=`); OR the UI hangs because
  `GetCandidates(query)` ran on a background thread (must resume on UI thread).

### BP-5 — Overlay preview: `GrepHit` branch in `LoadPreviewForSelection`

- **Files:** modify `Telescope/TelescopeOverlay.cs` (`LoadPreviewForSelection`,
  currently lines 402-469).
- **Change:** insert a `GrepHit` branch (mirroring the `CodeIssue`/`ReferenceHit`
  branches) right after the `ReferenceHit` branch and before the `payload is string`
  branch:
  ```csharp
  if (payload is GrepHit gh && System.IO.File.Exists(gh.FilePath))
  {
      try
      {
          string content = System.IO.File.ReadAllText(gh.FilePath);
          SetPreviewContent(content);
          if (gh.LineNumber > 0)
          {
              _previewNavigator.MoveToLine(gh.LineNumber);
              ApplyPreviewCaret();
              NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview caret={_previewNavigator.Caret} line={_previewNavigator.LineNumber}");
          }
          NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview file={gh.FilePath} chars={content.Length}");
      }
      catch (Exception ex)
      {
          NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}preview load failed: {ex.Message}");
      }
      return;
  }
  ```
- **Verify-with:** e2e `telescope-grep` — assert
  `[Telescope] preview file=.*GrepProbe\.cs` AND
  `[Telescope] preview caret=\d+ line=4` (selection index 0 = the line-4 hit).
- **Fails-if:** `preview file=...GrepProbe.cs` never appears (branch not reached,
  or payload type mismatch); or `preview caret=... line=4` shows a different line
  (the payload's `LineNumber` not driving `MoveToLine`).

### BP-6 — `InputHandler` action case + `OpenTelescopeGrep()`

- **Files:** modify `MyExtension/InputHandler.cs`.
- **Change:**
  - In `ResolveAction` (line ~152-183), add
    `case "telescope-grep": return () => OpenTelescopeGrep();` alongside
    `telescope-references`.
  - Add (mirror `OpenTelescopeReferences`, lines 476-490):
    ```csharp
    private void OpenTelescopeGrep()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            var dte = CardinalNavigation.UtilityMethods.GetDTE(_package);
            var centerRect = GetWindowRect(dte.MainWindow.HWnd);
            _telescope.Open("Grep", centerRect, dte.MainWindow.HWnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{Telescope.DiagnosticLog.NeoVisual}Failed to open Telescope grep: {ex.Message}");
        }
    }
    ```
- **Verify-with:** `dotnet build` compiles; e2e `telescope-grep` step 1 shows the
  overlay opens with `[NeoVisual] leader-binding executed: F,G` followed by
  `[Telescope] open finder=Grep candidates=0` (the `ResolveAction` case resolves to
  a non-null action; an unknown action would log
  `Unknown action 'telescope-grep'` and be ignored).
- **Fails-if:** `[NeoVisual] leader-binding executed: F,G` fires but no
  `open finder=Grep` line (the action is null/not wired); or a build error from a
  typo in the case label.

### BP-7 — Register `GrepFinder` in `MyExtensionPackage`

- **Files:** modify `MyExtension/MyExtensionPackage.cs` (finder registration block,
  lines ~70-75).
- **Change:** add one line after the `CodeIssuesFinder` registration:
  `_telescope.RegisterFinder(new GrepFinder(() => CardinalNavigation.UtilityMethods.GetDTE(this)));`
  (GrepFinder's own `GetCandidates(query)`/`OnSelected` drive the DTE work; no extra
  host opener/gatherer needed).
- **Verify-with:** `dotnet build` compiles; e2e `telescope-grep` step 1 asserts
  `[Telescope] open finder=Grep candidates=0` (the controller finds the registered
  `"Grep"` finder; an unregistered name logs `Unknown finder 'Grep'.`).
- **Fails-if:** `open finder=Grep` never appears and the log shows
  `Unknown finder 'Grep'.`; or `MyExtensionPackage` build error on the GrepFinder ctor
  argument.

### BP-8 — Rebind `F,G` in `default-keybindings.json`

- **Files:** modify `MyExtension/default-keybindings.json` (line 16).
- **Change:** change `"F,G": "command:Edit.FindinFiles"` → `"F,G": "telescope-grep"`.
- **Verify-with:** e2e `telescope-grep` step 1 — `Space+F G` opens the Grep finder
  (`open finder=Grep candidates=0`) and does NOT run `Edit.FindinFiles` (the Find-in-
  Files dialog would swallow the overlay). The RED log showed
  `leader-binding executed: F,G` still bound to Find-in-Files; after this it must
  route to `telescope-grep`.
- **Fails-if:** `leader-binding executed: F,G` still triggers `command:Edit.FindinFiles`
  (no `open finder=Grep`); or the JSON is malformed (config load logs an error and
  the binding is dropped).

### BP-9 — Build + unit-test gate

- **Files:** none (verify only).
- **Change:** none.
- **Verify-with:** `dotnet build` succeeds (whole solution); then
  `dotnet run --project tests/Telescope.Tests -- -- GrepFinder` → 6/6 pass, and the
  full `dotnet run --project tests/Telescope.Tests` stays green at 52 tests
  (46 + 6) with no regression in the existing 46; `dotnet run --project tests/NeoVisual.Tests`
  stays 25/25.
- **Fails-if:** any GrepFinder test fails (returns non-zero exit); or an existing
  Telescope/NeoVisual test regresses.

---

## Verification Trace

| Failing test / scenario | Implicated steps | Expected diagnostic |
|---|---|---|
| `Run_GrepFinder_EmptyQueryReturnsZeroCandidates` (unit, CS0246 `GrepFinder`) | BP-1, BP-2, BP-3 | `Assert.Equal(0, finder.GetCandidates("").Count)` passes (empty query → 0 entries) |
| `Run_GrepFinder_LineScanMatchesCaseInsensitive` (unit) | BP-1, BP-3 | 2 entries, all `Display` contain `A.cs`; `IndexOf(..., OrdinalIgnoreCase)` |
| `Run_GrepFinder_DisplayIsFileNameLineText` (unit) | BP-1, BP-3 | `entry.Display == "A.cs:2: NEEDLE here"` (`{file}:{line}: {text}`) |
| `Run_GrepFinder_PayloadRoundTripsGrepHit` (unit, CS1061 / CS0019 on `GrepHit?`) | BP-1, BP-3 | `payload.FilePath`/`.LineNumber`/`.LineText` round-trip + `payload != null` |
| `Run_GrepFinder_OnSelectedOpensHitAtLine` (unit) | BP-1, BP-3 | injected opener receives the exact `GrepHit(path, 2, "// NEEDLE x")` |
| `Run_GrepFinder_HitCapBounded` (unit) | BP-3 | `entries.Count > 0 && entries.Count <= 200` |
| `telescope-grep` step 1 (open) | BP-2, BP-3, BP-6, BP-7, BP-8 | `[NeoVisual] leader-binding executed: F,G` → `[Telescope] open finder=Grep candidates=0` |
| `telescope-grep` step 2 (type token) | BP-3, BP-4 | `[Telescope] grep hits=2` |
| `telescope-grep` step 3 (preview) | BP-5 | `[Telescope] preview file=.*GrepProbe\.cs` + `[Telescope] preview caret=\d+ line=4` |
| `telescope-grep` step 4 (open) | BP-3 | `[Telescope] opened grep: file=.*GrepProbe\.cs line=4` |
| `telescope-grep` step 5 (close) | (existing) | `[Telescope] overlay closed` |

**Known-RED allowlist (do NOT flag as regression):**
- **None** of the grep targets are allowlisted — `telescope-grep` and the 6
  `Run_GrepFinder_*` tests must be GREEN.
- Pre-existing flake on record: `neovisual-editor-insert` (retry-pass, flaky count
  1/3) — the verifier applies retry-once, not a regression classification.

---

## Execution Log

_To be appended by the hub on each attempt: attempt #, per-BP-step status,
debug/verifier verdict, capped evidence, and the cost line_
`delegations: N | VS boots: M | iterations: K`.