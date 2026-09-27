---
name: vs-extension-dev
description: Use when working on this Visual Studio extension (VSIX) project — "MyExtension", "CardinalNavigation", window navigation, VS window matrix, global keyboard hook, leader key bindings, InputHandler, IVsWindowFrame, DTE, IVsUIShell. Covers the architecture and conventions of the MyExtension codebase.
---

# MyExtension (VSIX) Development

This is a Visual Studio extension (VSIX) built with the C#/.NET SDK-style project
format. It implements **Cardinal-style window navigation**, a **leader-key
keyboard binding system**, a **Telescope-style fuzzy finder overlay**, and
**tool-window navigation** (hjkl + per-window controllers).

> **Read `AGENTS.md` first** — it is the up-to-date source of truth: live/offline
> test commands, the 34 live E2E scenarios (no known-RED; a few flake on retry), feature
> status/roadmap, and the hard requirements. This file covers the durable
> architecture.

## Trailmark (structural queries)

Structural questions — "who calls X", "what reaches Y", "what breaks if I change Z",
call paths, blast radius, complexity hotspots — MUST be answered with Trailmark, not
`grep`/manual reading. `AGENTS.md` holds the mandatory pre-flight and the
**repo-specific traps** (verified 2026-09-27): parse with `language="c_sharp"`;
cross-class calls resolve to `proxy.unresolved:<Type>.<Member>` nodes, so a bare
`callers_of` can return 0 for a heavily-called member (never report "dead code" from
it); and this VSIX has **no detected entrypoints**, so taint / privilege-boundary /
attack-surface / `trailmark-review-gate` carry no signal — use callers/callees,
`paths_between`, `reachable_from`, hotspots, and blast radius instead.

## Architecture

The extension is a single `AsyncPackage` (`MyExtensionPackage`) that installs a
Win32 low-level keyboard hook on load and disposes it on package dispose.

Data flow (key press → window move):

```
GlobalKeyboardHook (Win32 LL hook)
  → InputHandler.HandleKey(key, ctrl, shift, alt)
      → _bindings[sequence] → Navigate(direction)
          → WindowMatrix.NavigateInDirection(direction)
              → reduce/filter candidate windows
              → activate best match
```

### Key files

| File | Responsibility |
|------|----------------|
| `MyExtensionPackage.cs` | `AsyncPackage` entry point. Installs/disposes the keyboard hook; registers the `SolutionExplorerController`; auto-opens a test solution from `NEOVISUAL_TEST_SOLUTION`. |
| `GlobalKeyboardHook.cs` | Win32 low-level keyboard hook (`WH_KEYBOARD_LL`). Owns the P/Invoke surface and output-window logging. `IsInteresting` pre-filter; routes `HasToolWindowActionKeys`. |
| `InputHandler.cs` | Maps key sequences to actions. Where you add/change key bindings. Routes tool-window keys through the controller (`hjkl` + `controller.ActionKeys`). |
| `KeybindingConfig.cs` | Loads leader key + bindings from embedded `default-keybindings.json` merged with optional `%APPDATA%\MyExtension\keybindings.json`. Has `LoadFromJson` test seam. |
| `ToolWindows/IToolWindowController.cs` | Tool-window normal/input mode contract: `TryMove`, `Enter/ExitInputMode`, `ActionKeys`. |
| `ToolWindows/GeneralToolWindowController.cs` | Default controller: hjkl→arrow injection; `IsTextInputType` decides initial mode. |
| `ToolWindows/TextInputToolWindowController.cs` | Text-input windows (CommandWindow/FindReplace/...): normal-mode h/l/w/b/e caret motions + a/A/I insert placements over the focused text box (WPF TextBox, editor `IWpfTextView`, or WinForms). |
| `ToolWindows/TextMotionHelper.cs` | Shared vim-caret helper for WPF TextBox surfaces in tool windows (Solution Explorer search box + text-input windows): find the focused box, apply a motion via `TextMotionNavigator`, toggle the white block/line caret. |
| `ToolWindows/SolutionExplorerController.cs` | Solution Explorer actions: o/Enter open, r rename, m move, a add, g programmatically select the first source file (expand → walk → DTE `UIHierarchyItem.Select`, direct `ItemOperations.OpenFile`, ~1.5s re-select/refocus keeper to defeat the hover-preview focus steal), h/l fold expand/collapse, j/k navigate, `i` focuses the search box (with search-box vim motions via `TextMotionHelper`). |
| `ToolWindows/HierarchyResolver.cs` | Pure, dependency-free tree-walk seam (`HierarchyNode` + `FirstSourceFilePath`) behind the `g` selection action: Kind-GUID physical-file/physical-folder classification + in-order folder recursion; unit-tested without DTE. |
| `BlockCaretAdornment.cs` | Draws a block caret over an editor-view text-input window in normal mode (predefined "Caret" adornment layer — do NOT export a custom `AdornmentLayerDefinition`, it breaks the editor's MEF composition). |
| `WindowManager.cs` | Tracks the focused window frame; classifies `ToolWindowType`; dispatches to controllers. |
| `CardinalMovment/WindowMatrix.cs` | Core navigation algorithm: filters windows by direction, alignment, adjacency, and closest distance. |
| `CardinalMovment/WindowControlAdapter.cs` | Bridges an `IVsWindowFrame` (IVs shell) to an `EnvDTE.Window` (DTE automation). |
| `CardinalMovment/IVsFrameView.cs` | Wraps `IVsWindowFrame` (+ `IVsWindowFrame4`) for screen-rect / visibility queries. |
| `CardinalMovment/IVsUIWindowFrameExtractor.cs` | Enumerates tool + document window frames from `IVsUIShell`. |
| `CardinalMovment/UtilityMethods.cs` | DTE / `IVsUIShell` service access and window comparison/linking helpers. |
| `CardinalMovment/CardinalNavigationConstants.cs` | Direction chars, DPI/divide tuning constants, repeated strings. |
| `CardinalMovment/RectCoordinate.cs` | Simple int `x, y, width, height` rect value object. |
| `CardinalMovment/LinqExtensionMethods.cs` | `DistinctBy` LINQ helper (used because target framework lacks it). |
| `Telescope/` | The Telescope library (separate project `Telescope.csproj`): `TelescopeController`, `TelescopeOverlay` (WPF modal), `OverlayKeyHandler` (pure vim state machine), `TextMotionNavigator` (shared pure vim motions for preview + text-input windows), `SyntaxHighlighter` (preview syntax coloring), `FzfFilter` (fzf `--filter` subprocess — input must be explicit UTF-8 bytes or non-ASCII display breaks the payload lookup), `FileFinder`, `CodeIssuesFinder` (warnings/errors/TODO), `ReferencesFinder` + `ReferenceHit` (symbol-at-caret find-references with read/write access — the Roslyn gatherer is host-injected so the finder stays hermetic-testable), `GrepFinder` + `GrepHit` + `IQueryFinder` (query-driven grep over `ProjectFiles.Enumerate` — the overlay re-gathers per keystroke with a ~200ms debounce and skips fzf for query finders), `ImplementationFinder` + `ImplementationHit` (symbol-at-caret `FindImplementationsAsync`, first in-source declaring location, deterministic type-before-member ordering — host-injected gatherer keeps it hermetic-testable), `ProjectFiles` (shared DTE enumeration), `ResultsFormatter`, `NeoVisualLog`/`LogFileWriter` (two-file per-run logs). |

## Tool-window controller pattern (newer than the window matrix)

Each tool window gets an `IToolWindowController`. In **normal mode** `InputHandler`
routes `hjkl` + `controller.ActionKeys` to `TryMove`; `i` enters input mode; Esc
exits it. The hook pre-filter (`IsInteresting`) must return true for any key while
a tool window with action keys is in normal mode — `InputHandler.HasToolWindowActionKeys`
handles that. **net472 has no `IReadOnlySet<T>`** — `ActionKeys` is
`IReadOnlyCollection<Keys>`. Extract pure logic into dependency-free classes
(like `OverlayKeyHandler` / `TextMotionNavigator`) so it stays unit-testable.

## Telescope overlay (newer)

`TelescopeOverlay` is a WPF modal Window: title bar (finder + mode), prompt
TextBox (insert filter), results list, and a read-only **preview pane** on the
right. Keys route via `OverlayKeyHandler` (list navigation/modes) when focus is
on the list, or `TextMotionNavigator` (vim motions h/l/j/k/w/b/e/0/$/gg/G) when
focus is on the preview. **Ctrl+H / Ctrl+L switch `_focusTarget` between List and
Preview.** The overlay **closes on focus loss** (`Deactivated` → `CloseOverlay`).

## Non-obvious facts & gotchas

- **Two window APIs are used together.** The IVs shell API (`IVsWindowFrame`,
  `IVsUIShell`) provides precise on-screen geometry via `GetWindowScreenRect`;
  the DTE automation (`EnvDTE.Window`) provides activation (`window.Activate()`)
  and framing (`LinkedWindowFrame`). `WindowControlAdapter` pairs them.
- **Thread affinity is mandatory.** Almost every IVs/DTE call must be on the UI
  thread. The hook callback marshals to the main thread with
  `ThreadHelper.JoinableTaskFactory.Run(...)` + `SwitchToMainThreadAsync()`, and
  nearly every method starts with `ThreadHelper.ThrowIfNotOnUIThread()`. Keep
  this discipline — add it to any new VS API method. Never call VS objects from
  a background thread.
- **`coordinates` (on `WindowControlAdapter`) is recomputed each access** via
  `GetScreenDisplayCoordinates()`
  — it is not a cached snapshot. Reading it repeatedly reflects live window
  positions.
- **Hidden/tabbed windows are filtered out** before distance computation
  (`RemoveHiddenOrTabbedWindows`), since their screen rect reads `0,0,0,0`.
- **DPR/DPI matters.** `SetWindowDivideSelectionSizes()` scales the
  "divide" tolerance constants by the system DPI scale factor
  (`DpiAwareness.SystemDpiX / DefaultLogicalDpi`). Tune the logical constants in
  `CardinalNavigationConstants` (`DefaultLogicalXWindowDivide`,
  `DefaultLogicalYWindowDivide`, `DefaultLogicalTabPaneDivide`,
  `DefaultLogicalSelectorScale`), not the raw pixel values.
- **The leader key is Space.** `InputHandler.LeaderKey = Keys.Space`. Flat
  (`KeyDown`) handlers consume the key by making `HookCallback` return `(IntPtr)1`
  — that **blocks the key** from reaching VS. Returning `0`/`CallNextHookEx`
  lets it fall through.
- **`IsVisualStudioFocused()`** gates the hook: it only processes keys when the
  foreground process belongs to this VS instance.
- **The hook runs on the UI thread**, not a dedicated thread. A cheap pre-filter
  (`IsInteresting`) skips the handler for plain typing keys.
- **Injected keys never re-trigger the controller.** A key synthesized by
  `KeyInjection.Press` (Enter/F2/arrows) re-enters the low-level hook, and action
  keys like Enter would re-route to the controller and loop forever (the F1
  "Enter-storm": `solution-explorer open` firing ~30x in ~100ms). `KeyInjection.Press`
  records the VK in the `InjectedKeyGuard` (per-VK consume-once counter), and
  `GlobalKeyboardHook.HookCallback` passes any matching key-down through with
  `CallNextHookEx` (no handle, no swallow) so it reaches the focused control
  natively. Do NOT bail on `LLKHF_INJECTED` — the e2e harness injects every test
  key via `keybd_event`, so that would break all scenarios. Both the guard's
  writer (`Press`) and reader (`HookCallback`) run on the UI thread — no locking.

## Adding a key binding

Bindings live in `InputHandler` (built from `KeybindingConfig`): leader sequences
matched only after the leader key (e.g. `W`, `F,F`), and simple modifier
shortcuts (e.g. `Ctrl+H`, distinguished by a `+`). Action names resolve in
`InputHandler.ResolveAction`: `navigate-left/right/up/down`, `telescope`,
`telescope-issues`, `telescope-references`, `telescope-grep`,
`telescope-implementation`, `toggle-solution-explorer`, or
`command:<VsCommandName>`. To add a *new built-in
action*, add a case in `ResolveAction` and a line in `default-keybindings.json`.

## The navigation algorithm (WindowMatrix)

`NavigateInDirection` → `ReduceWindowsAndSelectActive`, which, in order:

1. `RemoveHiddenOrTabbedWindows()` — drop windows at rect `0,0,0,0`.
2. `RemoveWindowsInWrongDirection(direction)` — keep only windows strictly in the
   requested direction.
3. `RemoveWindowsNotAligned(direction)` — axis overlap with the active window.
4. `RemoveWindowsByClosestAdjacency(direction)` — nearest window within the DPI
   divide.
5. `SortByLargestAdjacency(direction)` — tie-break by largest shared edge.
6. Activate `m_ActiveWindows.First()`.

All four "Remove..." steps are direction-parameterized with a local
`filterFunction`. When adding a filter, follow that pattern and guard with
`ThreadHelper.ThrowIfNotOnUIThread()` inside the predicate.

> The algorithm currently takes only the **closest** window. It does not
> implement "jump" or chained-movement behavior — extend `WindowMatrix` here if
> needed.

## Build / toolchain

- **Target framework:** `net472` (.NET Framework 4.7.2) — modern .NET APIs may be
  unavailable; watch for missing LINQ/BCL members (hence the hand-rolled
  `DistinctBy`, and `IReadOnlySet<T>` is unavailable).
- **LangVersion:** `14`, `Nullable: enable`.
- **Packages:** `Microsoft.VisualStudio.SDK` (17.14), `Microsoft.VSSDK.BuildTools`
  (18.8), `MessagePack` (3.1.8). `ExcludeAssets="runtime"` on the SDK package
  (VS provides the runtime at load time).
- **VSIX target:** Visual Studio Community 17.14+ (see
  `source.extension.vsixmanifest`).
- **Namespaces:** `MyExtension` (package/hook/handler), `CardinalNavigation`
  (window logic — note the typo `CardinalMovment` folder/namespace is intentional),
  and `Telescope` (separate library).

## External skills (on-demand, token-conscious)

This file is the authoritative repo-specific source. Companion **pointer** skills live
in `.opencode/skills/` — load them ONLY when a task matches their trigger; they fold
reusable method and point at their upstream repos for deep reference. They
**complement, never override**, the conventions here:
- `dotnet-build-test-diag` — MSBuild failure diagnosis + testability + .NET perf.
- `dotnet-code-review` — .NET correctness/perf/conventions/architectural-drift review.
- `dotnet-pinvoke` — P/Invoke signatures, marshalling, memory lifetime (this repo is P/Invoke-heavy).
- `review-duplication` — structured duplication / missed-reuse investigation.
- `planning-and-task-breakdown` — decompose a spec into verifiable, dependency-ordered tasks.
- `audit-verification-gates` — can the agent's "done" be trusted (catch a false-green).
- `verify-tests-fail-without-fix` — prove a RED test really catches the bug.
- `code-testing-agent` — write meaningful .NET unit tests for the offline suites.
- `systematic-debugging` — reproduce → isolate → root-cause → fix → regression.
- `debugging-and-error-recovery` — isolate WHICH layer failed (overlay/hook/controller/VsVim mode/harness) when an e2e scenario or unit test fails.
- `test-driven-development` — red-green-refactor, aligned to the e2e loop.
- `sprint-plan-gate` — intent → spec/plan → approve → dispatch → lifecycle gate.
- `dispatching-parallel-agents` — parallel subagent fan-out + reconcile.
- `perf-investigation` — measurement-first; never optimize without a named bottleneck.

Keep the installed count deliberate: opencode advertises each skill's name/description
every request (and a known issue double-injects the list), so load the deep `references/`
of any of these only when the task needs it.

## Testing the extension

See **AGENTS.md** for the full picture. Summary:
- Offline unit tests: `dotnet run --project tests/Telescope.Tests` (56) and
  `dotnet run --project tests/NeoVisual.Tests` (31), with substring filter +
  `--list`.
- Live E2E: `pwsh tools/test-e2e.ps1` (35 scenarios against the experimental
  instance), `-Tests <name>` to run a subset. The last scenario, `seed-leak`,
  is an end-of-run filesystem guard that fails if any scenario wrote into a seeded
  file (baseline SHA-256 snapshot taken at bootstrap; expected writes allowlisted).
