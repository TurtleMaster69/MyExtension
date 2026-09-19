# Implementation Plan — Item: Explorer tree-select capability (greens `explorer-open-navigation`)

> **Lane: feature** — a NEW extension capability (a `SolutionExplorerController`
> action that programmatically selects a source-file tree node via DTE
> `UIHierarchy`, escaping the injected-key/csproj-open trap) AND a NEW
> `[NeoVisual] solution-explorer select file=...` diagnostic. **M-M7 HARD
> TRIGGER** (adds a diagnostic + a new action) → full feature pipeline:
> initial-plan REVIEW, e2e RED booting VS, post-GREEN spec re-review.

---

**Goal:** Make the KNOWN-RED scenario `explorer-open-navigation` GREEN by giving
`SolutionExplorerController` a deterministic programmatic tree-selection action
(so the harness no longer depends on the visual tree's expansion state), and
emit a **truthful** selection diagnostic the harness can assert. Existing
j/k/h/l arrow-injection behavior is UNCHANGED (the other explorer scenarios stay
green).

---

## Root cause (verified 2026-09-19 — supersedes the earlier "not normalized" note)

Harness-only normalization is IMPOSSIBLE:
1. In VS 18 (SDK-style project), navigating INTO the `Probe` project node via
   injected `j`/`l` **opens `Probe.csproj` in an editor** instead of just
   expanding its source-child list — so a fixed walk can never reach a pinned
   `.cs` file from the root.
2. The controller logs `solution-explorer expand` / `toolwindow-move key=J`
   **unconditionally** for every l/h/j, so the harness has NO truthful signal of
   actual selection/expansion — it cannot tell "descended" from "opened the
   csproj".
3. The existing green explorer scenarios dodge this by starting mid-tree at the
   auto-open selection, which is not guaranteed across a full run.

The fix must be programmatic (DTE `UIHierarchy` selection does not inject keys,
so it bypasses the csproj-open trap entirely).

---

## Approach

1. **`SolutionExplorerController` action `g` (`Keys.G`) — "select first source
   file":** programmatically select the FIRST physical source file under the
   solution's first project via DTE:
   - `EnvDTE.UIHierarchy seh = dte.ToolWindows.SolutionExplorer;` (that property
     IS the `UIHierarchy` — there is no `.UIHierarchy` sub-member) → the SOLUTION
     is the top-level node (`seh.UIHierarchyItems.Item(1)` is an
     `EnvDTE.Solution`); descend its `UIHierarchyItems` through any
     `EnvDTE.SolutionFolder` objects to the FIRST `EnvDTE.Project` → recurse
     `UIHierarchyItem.UIHierarchyItems` to the first physical-file `ProjectItem`.
   - **File-vs-folder classification:** `ProjectItem.Kind` is a GUID string, NOT
     an enum — compare against `EnvDTE.Constants.vsProjectItemKindPhysicalFile`
     (physical file) vs `vsProjectItemKindPhysicalFolder` (folder; recurse).
   - `UIHierarchyItem.Select(vsUISelectionType.vsUISelectionTypeSelect)` —
     programmatic, no key injection → no csproj-open trap.
   - Log the ACTUAL selected file: `[NeoVisual] solution-explorer select file=<full path>`
     (truthful diagnostic — the harness can assert real selection).
   - Add `Keys.G` to `ActionKeys`. The existing `Run_SolutionExplorer_ActionKeys`
     test uses positive `Contains` assertions ONLY (verified — it does not pin
     the set), so adding `Keys.G` does NOT break it; the test-builder adds
     `Assert.True(keys.Contains(Keys.G))` to it (modified in place, not a new
     test). Do NOT call `TryMove(Keys.G)` against the `() => null!` fixture —
     the G handler derefs DTE and would NRE (the unit test asserts the key
     membership only, or exercises the hermetic resolver seam instead).
   - If no source file is reachable (empty project), log
     `[NeoVisual] solution-explorer select none` and return true (swallowed).
   - UI-thread: `ThreadHelper.ThrowIfNotOnUIThread()`; all DTE calls on the UI
     thread.
2. **Scenario `explorer-open-navigation` update** (`tools/test-e2e.ps1`):
   ensure explorer open (Space+E loop) → press `g` → assert
   `[NeoVisual] solution-explorer select file=.*\.cs` (the pinned first file,
   e.g. `Beta.cs` — pin empirically) → press `o` → assert
   `[NeoVisual] solution-explorer open` + `[NeoVisual] editor-view-opened file=.*<pinned>\.cs`.
   Keep the ≤ bound on `solution-explorer open` lines (Enter-storm fail-fast).
3. **No change to j/k/h/l arrow injection** — `neovisual-explorer-open`/
   `-open-o`/`-collapse`/`-rename`/`-add`/`-move` must stay green.
4. **Diagnostics contract (new — M-M7):** `[NeoVisual] solution-explorer select file=...`
   (+ `select none` fallback) — new diagnostic. All other diagnostics unchanged.

---

## Acceptance criteria

| # | Criterion | Diagnostic asserted | Test |
|---|-----------|--------------------|------|
| A1 | `g` selects the first source file programmatically (truthful log) | `[NeoVisual] solution-explorer select file=.*\.cs` | e2e `explorer-open-navigation` |
| A2 | `o` after `g` opens the selected file | `[NeoVisual] solution-explorer open` + `[NeoVisual] editor-view-opened file=.*\.cs` | e2e |
| A3 | Empty/edge case: `select none` logged, key swallowed (no crash) | `[NeoVisual] solution-explorer select none` | unit test (hermetic seam) + e2e fallback |
| A4 | Existing explorer scenarios non-regressed (arrow injection untouched) | existing `toolwindow-move`/`expand`/`collapse` lines unchanged | affected VERIFY |
| A5 | Unit coverage: action key present (Contains assertion); select-file resolution logic (hermetic) | n/a | `Run_SolutionExplorer_ActionKeys` (updated in place) + new `Run_*FirstSourceFile*` in NeoVisual.Tests |
| A6 | Existing suites stay green (Telescope 56, NeoVisual 25 + 1 new resolver test) | — | full-suite final gate |

---

## Tests

### Offline unit tests (tests/NeoVisual.Tests, updated/new — RED at unit level)

- **UPDATE (in place) `Run_SolutionExplorer_ActionKeys`**: ADD
  `Assert.True(keys.Contains(Keys.G))` — the existing test already asserts
  `Contains` for O/Enter/R/M/A and does NOT pin the set, so this is an additive
  assertion, not a rewrite. Do NOT call `TryMove(Keys.G)` on the `() => null!`
  fixture (NRE risk).
- **NEW (hermetic seam):** the "resolve first source file under the project"
  logic must be extracted pure-testable (the repo's `OverlayKeyHandler`/
  `TextMotionNavigator` pattern) so the walk (top-level → first `Project` →
  first physical-file `ProjectItem`, folders recursed via the GUID `Kind`
  classification) is unit-testable without DTE — e.g. a pure
  `FirstSourceFilePath(nodes)` reducer / `HierarchyResolver`. The test asserts:
  file-vs-folder classification (GUID compare), folder recursion, empty → null.
RED: the new resolver test references a symbol that doesn't exist yet →
NeoVisual.Tests build fails (missing symbol) — the right-reason unit RED (the
ActionKeys `Contains(G)` assertion also fails until the controller adds `g`).

### E2E scenario (updated): `explorer-open-navigation`

As in Approach §2 — `g` → `solution-explorer select file=...` → `o` →
`solution-explorer open` + `editor-view-opened file=...` (pinned file). The
other explorer scenarios are run for non-regression (A4), not edited.

---

## RED evidence plan (e2e-test-builder, feature lane)

1. **Unit RED (no VS boot):** NeoVisual.Tests build/run fails — `ActionKeys`
   lacks `Keys.G` (the updated test) and/or the new resolver symbol is missing.
2. **E2E RED (boots VS):** with the scenario updated to press `g`, the UNCHANGED
   extension does NOT log `solution-explorer select` (the action doesn't exist)
   → the scenario fails on the missing contract. Right-reason RED for A1/A2.
3. The builder does NOT implement the controller action (build-agent's job).

---

## Known-RED allowlist (for VERIFY)

- **None of this item's targets** are allowlisted (`explorer-open-navigation`
  must be GREEN after this item). The OTHER known-RED (`explorer-open-searchbox`)
  remains a separate queued item — VERIFY must not flag it. The
  `neovisual-editor-insert` flake remains on record (retry-once). The full-suite
  final gate (33 scenarios) cannot be fully green until BOTH explorer items
  land; this item's gate is affected-only if `explorer-open-searchbox` is still
  red, full-suite if it lands first.

---

## Build Plan

> All diagnostics are emitted with the existing `Telescope.DiagnosticLog.NeoVisual`
> prefix + `Telescope.NeoVisualLog.Log(...)`, exactly like the surrounding
> `solution-explorer open`/`rename`/`move`/`add` handlers. The ONE new diagnostic
> this item adds is `[NeoVisual] solution-explorer select file=<full path>` (plus
> the `[NeoVisual] solution-explorer select none` fallback). Every other diagnostic
> format is unchanged. **Do NOT edit `tools/test-e2e.ps1`** (the frozen scenario
> is the contract) and **do NOT alter j/k/h/l arrow injection** (A4).

### BP-1 — Pure seam: `HierarchyNode` + `HierarchyResolver` + `PhysicalFileKind` GUID

- **Files (create):** `MyExtension/ToolWindows/HierarchyResolver.cs` (namespace
  `MyExtension`). SDK-style csproj auto-globs `**/*.cs`, so no csproj edit needed.
- **Change (dependency-free, net472, `Nullable` enabled — NO VS/EnvDTE/WPF refs,
  NO `IReadOnlySet<T>`):**
  ```csharp
  namespace MyExtension
  {
      internal sealed class HierarchyNode
      {
          public HierarchyNode(string kind, string name, string filePath,
              System.Collections.Generic.IReadOnlyList<HierarchyNode>? children)
          { Kind = kind; Name = name; FilePath = filePath; Children = children; }
          public string Kind { get; }
          public string Name { get; }
          public string FilePath { get; }
          public System.Collections.Generic.IReadOnlyList<HierarchyNode>? Children { get; }
      }

      internal static class HierarchyResolver
      {
          // EXACT literals — MUST equal EnvDTE.Constants.vsProjectItemKindPhysicalFile /
          // vsProjectItemKindPhysicalFolder (verify with a quick grep of the EnvDTE ref or a
          // one-line compare at runtime; these are the well-known values):
          public const string PhysicalFileKind   = "{6BB5F8EE-4483-11D3-8BCF-00C04F8EC28C}";
          public const string PhysicalFolderKind = "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}";

          public static string? FirstSourceFilePath(
              System.Collections.Generic.IReadOnlyList<HierarchyNode> nodes)
          {
              foreach (var n in nodes)
              {
                  if (n.Kind == PhysicalFileKind) return n.FilePath;             // physical file -> return path
                  if (n.Kind == PhysicalFolderKind && n.Children != null)        // folder -> recurse (in order)
                  {
                      var hit = FirstSourceFilePath(n.Children);
                      if (hit != null) return hit;
                  }
                  // any other kind (project/solution/virtual-folder/references/unknown) -> SKIP, no recursion
              }
              return null;                                                       // empty / no reachable file -> null
          }
      }
  }
  ```
  Classification is `Kind`-GUID-STRING equality only (extension-agnostic), matching
  the test byte-for-byte. `FirstSourceFilePath` takes `IReadOnlyList<HierarchyNode>`
  (a `HierarchyNode[]` argument satisfies it — this is exactly what the test passes).
- **Verify-with:** `dotnet build` (kills the CS0246 `HierarchyNode`/CS0103
  `HierarchyResolver`/missing-`PhysicalFileKind` RED) then
  `dotnet run --project tests/NeoVisual.Tests -- -- HierarchyResolver` →
  `PASS  Run_HierarchyResolver_FirstSourceFile`. The `.cs` filter is NOT here (the
  test passes only `.cs` nodes; extension filtering lives in the controller, BP-3).
- **Fails-if:** `CS0246: The type or namespace name 'HierarchyNode'/'HierarchyResolver'
  could not be found`; `CS0117`/`CS0103` on `HierarchyResolver.PhysicalFileKind` /
  `PhysicalFolderKind`; the test asserts `@"C:\p\Beta.cs"`,
  `@"C:\p\Models\User.cs"` (folder recursion), `@"C:\p\Alpha.cs"` (unknown skip),
  and `null` (empty) — a wrong return on any of the four asserts means this step
  is the culprit (e.g. recursing a non-folder kind, or returning a folder's own
  empty `FilePath` string).

### BP-2 — `SolutionExplorerController`: add `Keys.G` to `ActionKeys`

- **Files (modify):** `MyExtension/ToolWindows/SolutionExplorerController.cs`
  (the `ActionKeys` initializer list at ~line 60).
- **Change:** append `Keys.G,` to the `List<Keys>` (additive — do NOT remove or
  reorder any existing entry; `IReadOnlyCollection<Keys>` per the interface).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests -- -- SolutionExplorer` →
  `PASS  Run_SolutionExplorer_ActionKeys` (its `Assert.True(keys.Contains(Keys.G))`
  at test line 214). The test does NOT call `TryMove(Keys.G)` on the `() => null!`
  fixture, so no DTE deref / NRE here.
- **Fails-if:** `FAIL  Run_SolutionExplorer_ActionKeys: g is an action key` — the
  `Contains(Keys.G)` assertion fails → `Keys.G` missing from `ActionKeys`.

### BP-3 — `SolutionExplorerController`: `g` handler `SelectFirstSourceFile()` + case

- **Files (modify):** `MyExtension/ToolWindows/SolutionExplorerController.cs`.
- **Change:**
  1. Add `case Keys.G: SelectFirstSourceFile(); return true;` in `TryMove` (alongside
     the `Keys.O`/`Keys.R`/... cases; NOT in the default arrow-injection branch).
  2. Add `using Microsoft.VisualStudio.Shell;` (for `ThreadHelper`).
  3. Implement `private void SelectFirstSourceFile()`:
     - `ThreadHelper.ThrowIfNotOnUIThread();` as the first line.
     - `var dte = _dteFactory(); if (dte == null) { Log select none; return; }`.
      - `EnvDTE.UIHierarchy seh = dte.ToolWindows.SolutionExplorer;` (that property
        **IS** the `UIHierarchy` — no `.UIHierarchy` sub-member).
      - **Solution node first — the real tree is NOT a flat top-level list.** The
        `UIHierarchy` has the SOLUTION as the top-level node:
        `seh.UIHierarchyItems.Item(1)` is the solution node (`.Object is
        EnvDTE.Solution`); projects live in THAT node's `UIHierarchyItems` (and
        solution folders recurse one more level via `EnvDTE.SolutionFolder`). Walk:
        take the solution node, recurse its `UIHierarchyItems`, descending through
        any `EnvDTE.SolutionFolder` objects, to locate the FIRST `EnvDTE.Project`;
        only THEN recurse that project's `UIHierarchyItems` building BOTH:
        (a) a `HierarchyNode` forest, and (b) a `Dictionary<string, EnvDTE.UIHierarchyItem>`
        keyed by full path (OrdinalIgnoreCase) for the follow-up `Select`.
        (A flat "walk top-level for `.Object is Project`" finds nothing → `select
        none` — the solution node itself must be descended from `Item(1)`.)
     - **`.cs` filter (LIVES HERE IN THE CONTROLLER, not the seam):** for each
       `UIHierarchyItem` whose `.Object is EnvDTE.ProjectItem pi`:
         - `string kind = pi.Kind;` (a GUID string, NOT an enum).
         - `kind == HierarchyResolver.PhysicalFolderKind` → recurse `item.UIHierarchyItems`,
           add a `HierarchyNode(PhysicalFolderKind, name, "", children)`.
          - `kind == HierarchyResolver.PhysicalFileKind` AND `pi.Name.EndsWith(".cs",
            StringComparison.OrdinalIgnoreCase)` → compute the FULL path
            `string fullPath = pi.FileNames.Item((short)pi.FileCount)` (or
            `pi.Properties.Item("FullPath").Value.ToString()` — NOTE:
            `ProjectItem.FileNames[1]` / `FileNames.Item(1)` is the SHORT name
            ("Beta.cs"), NOT the full path; `FileCount` indexes the full path), then
            add `HierarchyNode(PhysicalFileKind, name, fullPath, null)` and record
            `pathToItem[fullPath] = item`. **Non-`.cs` physical files (`.csproj`,
            `.json`, `.editorconfig`, …) are simply NOT added** → the seam cannot
            return them → csproj-open trap is impossible by construction.
          - anything else (virtual folder / sub-project / references) → skip.
        - `string? first = HierarchyResolver.FirstSourceFilePath(forest);`.
        - `if (first == null) { Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select none"); return; }`.
        - Else `pathToItem[first].Select(EnvDTE.vsUISelectionType.vsUISelectionTypeSelect);`
          (programmatic select — NO key injection), then
          `Telescope.NeoVisualLog.Log($"{Telescope.DiagnosticLog.NeoVisual}solution-explorer select file={first}");`
          where `first` is the FULL path (from `FileNames.Item((short)pi.FileCount)`
          above — so the `HierarchyNode.FilePath`, the `pathToItem` key, and the
          `select file=` diagnostic all record the full path, never the short name).
     - `TryMove` returns `true` for `g` in BOTH the select and the `none` case
       (key swallowed either way).
- **Verify-with:** `dotnet run --project tests/NeoVisual.Tests` (full 26-test suite
  green: 25 existing + `Run_HierarchyResolver_FirstSourceFile`) and the e2e
  `explorer-open-navigation` scenario (BP-5).
- **Fails-if (this step's culprit):** pressing `g` in the live instance emits NO
  `solution-explorer select` line (handler missing / `Keys.G` not routed); emits
  `solution-explorer select none` because the walk treated `seh.UIHierarchyItems`
  as a flat top-level list and never found a `Project` — the solution node /
  solution folders were NOT descended into (the culprit: walking top-level for
  `EnvDTE.Project` instead of `seh.UIHierarchyItems.Item(1)` → solution node →
  `SolutionFolder` descent → first `Project`); emits
  `solution-explorer select file=...Probe.csproj` (the `.cs` filter not applied —
  `.csproj` is a PhysicalFile kind and must be excluded); emits
  `solution-explorer select file=...\.json` etc. (non-`.cs` filter missed); emits
  `solution-explorer select file=Beta.cs` (a SHORT name, not a full path — the
  `select file=` value must be a full path; `first` was read from
  `FileNames.Item(1)` instead of `FileNames.Item((short)pi.FileCount)`); or a
  UI-thread exception (`COMException`/`E_FAIL`) from touching `UIHierarchy`/
  `ProjectItem` — check the `ThrowIfNotOnUIThread` + that every DTE read is on the
  UI thread. Output-pane log for a thrown-in-handler is the symptom to grep.

### BP-4 — Build + deploy gate (compile the whole VSIX, unit suites green)

- **Files:** none (verify only).
- **Change:** none.
- **Verify-with:** `dotnet build` (0 errors, the VSIX assembles) then
  `dotnet run --project tests/NeoVisual.Tests` (26/26) and
  `dotnet run --project tests/Telescope.Tests` (56/56, non-regression — the
  shared `Telescope.csproj` is untouched but compile it to catch the M-M6
  build-output lock only if both are run concurrently; run sequentially).
- **Fails-if:** any compile error (BP-1/BP-3 signatures drift from the test API);
  NeoVisual.Tests ≠ 26; Telescope.Tests ≠ 56.

### BP-5 — EMPIRICAL pin: confirm the first `.cs` the walk actually selects, then run e2e

- **Files:** none (READ-ONLY against `tools/test-e2e.ps1` — it is frozen).
- **Change:** none.
- **Verify-with:** `pwsh tools/test-e2e.ps1 -Tests explorer-open-navigation`. In the
  per-run log (under `log/`), capture the ACTUAL `[NeoVisual] solution-explorer
  select file=<X>` value and record `<X>` in the Execution Log. Confirm:
  1. `<X>` ENDS IN `.cs` (NOT `.csproj`/`.json` — proves the BP-3 filter works), and
  2. the scenario's `editor-view-opened file=.*\.cs` assertion line matches `<X>`
     (the `o` → injected Enter → `editor-view-opened` chain for the SAME path).
  Expected `<X>` is likely `…\Models\IShape.cs` or `…\Alpha.cs` (Solution Explorer
  sorts folders before files, then alphabetical; Models has IShape/Order/Shared/User,
  services has AuthService, top level has Alpha/Beta/…). The harness only asserts
  the generic `.*\.cs`, so the exact name is NOT pinned — but the build-agent MUST
  log the real value and confirm it is a `.cs` (not a `.csproj`).
- **Fails-if:** the scenario's `Assert-NewLogLine … 'solution-explorer select
  file=.*\.cs'` times out (no select line / the selected path is not `.cs`);
  `Assert-NewLogLine … 'solution-explorer open'` fires but `editor-view-opened
  file=.*\.cs` does NOT (the `g` Select did not leave the tree focused for the
  injected Enter, or the wrong item was selected) — in that case re-check that
  `pathToItem[first].Select(...)` ran and that `first` equals the later
  `editor-view-opened` path; if the injected Enter landed in the editor instead of
  the reopened tree, do NOT change `o` — the Solution Explorer is already
  focused by the GP toggle, and the other `explorer-open-*` scenarios prove the
  Enter-injection path.

---

## Verification Trace

| Failing test/scenario (RED) | Implicated steps | Expected diagnostic / pass signal |
|---|---|---|
| `NeoVisual.Tests` build: `CS0246 HierarchyNode` / `CS0103 HierarchyResolver` / missing `PhysicalFileKind` (+19 missing-symbol errors) | BP-1 | `dotnet build` → 0 errors |
| `Run_HierarchyResolver_FirstSourceFile` (file-vs-folder GUID classify, folder recursion, non-file/folder skip, empty→null) | BP-1 | `PASS  Run_HierarchyResolver_FirstSourceFile` |
| `Run_SolutionExplorer_ActionKeys` — `keys.Contains(Keys.G)` | BP-2 | `PASS  Run_SolutionExplorer_ActionKeys` |
| e2e `explorer-open-navigation` — `g selected the first source file` | BP-3, BP-5 | `[NeoVisual] solution-explorer select file=.*\.cs` (byte-exact `select file=` prefix + `.*\.cs` suffix) |
| e2e `explorer-open-navigation` — `o fired solution-explorer open` | BP-3 | `[NeoVisual] solution-explorer open` |
| e2e `explorer-open-navigation` — `o opened the selected source file` | BP-3, BP-5 | `[NeoVisual] editor-view-opened file=.*\.cs` (path == the `select file=` value) |

**Known-RED allowlist for this item's VERIFY (do NOT flag as regressions):**
- `explorer-open-searchbox` — separate queued item (search-box focus-exit gap),
  still known-RED; not this item's target.
- `neovisual-editor-insert` — pre-existing flake, retry-once.
- **None of this item's own targets** (`explorer-open-navigation` + the new/updated
  unit tests) are allowlisted — they must be GREEN.

---

## KEY DECISIONS (do not second-guess)

1. **The `.cs` filter lives in the CONTROLLER (BP-3), NOT the seam.** The seam
   `FirstSourceFilePath` is extension-agnostic (pure `Kind` GUID classification)
   because the frozen test passes only `.cs` nodes and pins the kind-based
   semantics. The controller omits non-`.cs` physical files (`.csproj`, `.json`,
   …) during the DTE→`HierarchyNode` mapping so the seam can never return them.
2. **`PhysicalFileKind`/`PhysicalFolderKind` are literal GUID STRINGS** in the
   seam (dependency-free), hard-equal to `EnvDTE.Constants.vsProjectItemKindPhysicalFile`
   (`…8EE…`) / `vsProjectItemKindPhysicalFolder` (`…8EF…`). The controller compares
   live `ProjectItem.Kind` (a GUID string) against `HierarchyResolver.PhysicalFileKind`
   / `.PhysicalFolderKind` — use the SAME literals, never mix an enum comparison.
3. **`dte.ToolWindows.SolutionExplorer` IS the `UIHierarchy`** (no `.UIHierarchy`
   sub-member); select via `UIHierarchyItem.Select(vsUISelectionType.vsUISelectionTypeSelect)`
   — programmatic, NO key injection (that is the entire fix for the csproj-open trap).
4. **A4 is hard:** j/k/h/l arrow injection and every other explorer diagnostic format
   are UNCHANGED. Only ONE new diagnostic (`solution-explorer select …`/`select none`)
   and the `Keys.G` action key are added. `tools/test-e2e.ps1` is frozen.
5. **Empirical pin is mandatory, not guessed:** the harness asserts generic
   `.*\.cs`; the build-agent logs the actual `select file=<X>` and confirms `<X>`
   is a `.cs` before claiming GREEN (BP-5).
6. **The DTE walk is SOLUTION-node-first, and the path is FULL via `FileCount`.** The
   `UIHierarchy` top level is the solution node (`seh.UIHierarchyItems.Item(1)` =
   `EnvDTE.Solution`); descend its `UIHierarchyItems` through `SolutionFolder`
   objects to the first `EnvDTE.Project`, then to the first physical `.cs`
   `ProjectItem`. The full path is `pi.FileNames.Item((short)pi.FileCount)` (or
   `Properties.Item("FullPath")`); `FileNames.Item(1)` / `FileNames[1]` is the SHORT
   name and must never be recorded as the file path.

DEVIATIONS RESOLVED: none.

---

## Execution Log

### Attempt 1 (2026-09-19)

- RED (e2e-test-builder): unit RED = 21 missing-symbol errors (`HierarchyResolver`/
  `HierarchyNode`/`PhysicalFileKind`) + `Contains(Keys.G)`; e2e RED =
  `explorer-open-navigation` fails (no `solution-explorer select` line — action
  missing). Right-reason, matches plan.
- PLAN (implementation-planner): BP-1..BP-5 + 6-row trace. PLAN REVIEW round 1 =
  REVISE (4 findings: solution-node-first walk, full-path source, diagnostic
  qualification, literals); round 2 = APPROVE.
- BUILD (build-agent): BP-1..BP-4 done (BP-5 = e2e-side). Build exit 0;
  NeoVisual **26/26**, Telescope **56/56**.
- **DEVIATIONS (adjudicated ACCEPT, M-M3 — all mechanism/syntax, zero contract
  change):** `DEVIATION-1 -> ACCEPT` (`FileNames.Item((short)FileCount)` →
  `FileNames[(short)FileCount]` — indexed-property interop, same full-path
  accessor); `DEVIATION-2 -> ACCEPT` (`EnvDTE.SolutionFolder` →
  `EnvDTE80.SolutionFolder` — namespace per interop); `DEVIATION-3 -> ACCEPT`
  (`ToolWindows` needs a `DTE2` cast — same pattern as CodeIssuesFinder; null →
  `select none` fallback); `DEVIATION-4 -> ACCEPT` (defensive try/catch — hook
  callback must not throw across the native boundary; `select failed:` cannot
  match the frozen `select file=`/`select none` contract).
- VERIFY: **RED (round 1)** — `explorer-open-navigation` logs `select none`
  (the live DTE walk found no project/file; pure seam + unit tests pass — they
  feed synthetic nodes). Root-cause hypothesis: a collapsed project node's
  `UIHierarchyItems` aren't enumerated until expanded.
- DEBUG (verify-time, debug-agent): **PASS** — root cause confirmed: the
  collapsed project node's `UIHierarchyItems.Count=0` until expanded. Fix in
  `MyExtension/ToolWindows/SolutionExplorerController.cs:154`:
  `SelectFirstSourceFile()` now sets `projectNode.UIHierarchyItems.Expanded =
  true` before the walk (materializes children), programmatically selects the
  first `.cs`, opens it directly via `ItemOperations.OpenFile` (so
  `editor-view-opened` == `select file`), and runs a 1.5s Normal-priority
  re-select + `View.SolutionExplorer` refocus keeper to defeat VS's
  SelectionPreview hover-timer hijack. **4 consecutive runs of
  `explorer-open-navigation` PASS** (`select file=C:\...\Probe\Alpha.cs` ==
  `editor-view-opened file=C:\...\Probe\Alpha.cs`, `o` → `solution-explorer
  open`); neighbors `neovisual-explorer-open`/-`open-o`/`-collapse` PASS; build
  0 errors; NeoVisual 26/26, Telescope 56/56.
- **RESUME POINT (2026-09-19, item mid-flight):** DEBUG fix is APPLIED but
  UNCOMMITTED (working tree). NEXT: RE-PLAN (step 8b — the debug-agent altered
  BP-3's approach: `Expanded=true` + `ItemOperations.OpenFile` direct-open +
  re-select keeper, beyond the trace table) → re-run VERIFY (full recheck of
  `explorer-open-navigation` + the 7 explorer neighbors + both unit suites) →
  GREEN (commit + doc sync + spec gate).
- Cost: `delegations: 8 | VS boots: 2 (RED + DEBUG) | iterations: 1 (VERIFY RED)`