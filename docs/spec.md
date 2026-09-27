# MyExtension — Architecture Specification

This document is the **architecture contract** for the MyExtension Visual Studio
extension (VSIX). It must never drift from reality. Source-of-truth inputs:
`AGENTS.md` (build/test commands, hard requirements, roadmap) and
`.opencode/skills/vs-extension-dev/SKILL.md` (durable architecture, gotchas).

## 1. Overview

MyExtension is a Visual Studio extension that brings **Cardinal-style window
navigation**, a **leader-key (Space) keyboard binding system**, a
**Telescope-style fuzzy-finder overlay**, and **vim-mode tool-window controllers**
to Visual Studio. It is a single `AsyncPackage` (`MyExtensionPackage`) that
installs a Win32 low-level keyboard hook on load and disposes it on package
dispose. It is opened via `MyExtension.slnx` and consists of three projects:

| Project | Role |
|---------|------|
| `MyExtension/MyExtension.csproj` | The VSIX package: package, keyboard hook, input handler, keybinding config, window navigation, tool-window controllers. |
| `Telescope/Telescope.csproj` | The Telescope library: WPF modal overlay, pure vim state machines, fzf filter, finders, syntax highlighter, logging. |

Target framework: **net472** (VS Community 17.14+, amd64). `LangVersion` 14,
`Nullable` enabled. `MyExtension.slnx` declares four projects (the two test
projects above are separate).

## 2. Architecture

### 2.1 Key-press data flow

```
GlobalKeyboardHook (Win32 WH_KEYBOARD_LL, runs ON the UI thread)
  → IsInteresting pre-filter (cheap; skips plain typing keys)
  → InputHandler.HandleKey(key, ctrl, shift, alt)
      → _bindings lookup (leader sequences / modifier shortcuts)
      → ResolveAction → controller/action
```

The hook marshals to the main thread via
`ThreadHelper.JoinableTaskFactory.Run(...)` + `SwitchToMainThreadAsync()`. The
hook runs on the UI thread, NOT a dedicated thread. `IsLeaderActive` is a volatile
bool read by the pre-filter, mutated only on the UI thread. Handled keys are
blocked from VS by returning `(IntPtr)1` from the hook callback.

### 2.2 Key files

| File | Responsibility |
|------|----------------|
| `MyExtension/MyExtensionPackage.cs` | `AsyncPackage` entry point; installs/disposes the keyboard hook; registers `SolutionExplorerController`; auto-opens a test solution from `NEOVISUAL_TEST_SOLUTION`. |
| `MyExtension/GlobalKeyboardHook.cs` | Win32 low-level keyboard hook; owns P/Invoke and output-window logging; `IsInteresting` pre-filter. |
| `MyExtension/InputHandler.cs` | Maps key sequences to actions; routes tool-window keys through controllers; `IsTyping()` via `VimModeTracker`. |
| `MyExtension/KeybindingConfig.cs` | Loads leader + bindings from embedded `default-keybindings.json` merged with optional `%APPDATA%\MyExtension\keybindings.json`. |
| `MyExtension/KeyInjection.cs` | `keybd_event` / `Press` helpers for injecting arrow/Return keys into tool windows. |
| `MyExtension/PopupNavigation.cs` | Maps `Ctrl+N`/`Ctrl+P` to injected Down/Up for completion/peek lists. |
| `MyExtension/VimModeTracker.cs` | Shared MEF part tracking the focused editor's VsVim mode (Insert/Replace gating). |
| `MyExtension/WindowManager.cs` | Tracks the focused window frame; classifies `ToolWindowType`; dispatches to controllers. |
| `MyExtension/ToolWindowTypeResolver.cs` | Maps window frames to `ToolWindowType` (known GUIDs / unknown). |
| `MyExtension/ToolWindows/IToolWindowController.cs` | Tool-window normal/input mode contract. |
| `MyExtension/ToolWindows/GeneralToolWindowController.cs` | Default controller: hjkl→arrow injection; `IsTextInputType` decides initial mode. |
| `MyExtension/ToolWindows/TextInputToolWindowController.cs` | Text-input windows: normal-mode h/l/w/b/e caret motions + a/A/I insert placements. |
| `MyExtension/ToolWindows/TextMotionHelper.cs` | Shared vim-caret helper for WPF TextBox surfaces (Solution Explorer search box + text-input windows). |
| `MyExtension/ToolWindows/SolutionExplorerController.cs` | Solution Explorer actions: o/Enter open, r rename, m move, a add, g select-first-source-file, h/l fold expand/collapse, j/k navigate, i focuses the search box. |
| `MyExtension/ToolWindows/HierarchyResolver.cs` | Pure, dependency-free tree-walk seam: `HierarchyNode` + `FirstSourceFilePath` (physical-file/folder Kind-GUID classification, folder recursion) used by `SolutionExplorerController`'s `g` action. |
| `MyExtension/BlockCaretAdornment.cs` | Draws a block caret over an editor-view text-input window in normal mode. |
| `MyExtension/CardinalMovment/WindowMatrix.cs` | Core navigation algorithm. |
| `MyExtension/CardinalMovment/WindowControlAdapter.cs` | Bridges `IVsWindowFrame` (IVs shell) to `EnvDTE.Window` (DTE). |
| `MyExtension/CardinalMovment/IVsFrameView.cs` | Wraps `IVsWindowFrame` (+ `IVsWindowFrame4`) for screen-rect / visibility. |
| `MyExtension/CardinalMovment/IVsUIWindowFrameExtractor.cs` | Enumerates tool + document window frames from `IVsUIShell`. |
| `MyExtension/CardinalMovment/UtilityMethods.cs` | DTE / `IVsUIShell` service access and window comparison/linking helpers. |
| `MyExtension/CardinalMovment/CardinalNavigationConstants.cs` | Direction chars, DPI/divide tuning constants. |
| `MyExtension/CardinalMovment/RectCoordinate.cs` | Simple int rect value object. |
| `MyExtension/CardinalMovment/LinqExtensionMethods.cs` | Hand-rolled `DistinctBy` (net472 lacks it). |
| `Telescope/TelescopeController.cs` | Opens the overlay / dispatches finder selection. |
| `Telescope/TelescopeOverlay.cs` | WPF modal: title bar, prompt TextBox, results list, read-only preview pane. Closes on focus loss (`Deactivated`). |
| `Telescope/OverlayKeyHandler.cs` | Pure vim state machine for overlay list navigation/modes. |
| `Telescope/TextMotionNavigator.cs` | Shared pure vim motions (h/l/j/k/w/b/e/0/$/gg/G + a/A/I). |
| `Telescope/FzfFilter.cs` | fzf `--filter` subprocess; input written as explicit UTF-8 bytes. |
| `Telescope/FileFinder.cs` | File finder (hermetic open seam). |
| `Telescope/CodeIssuesFinder.cs` | Warnings/errors + TODO/FIXME/HACK/XXX marker finder. |
| `Telescope/ProjectFiles.cs` | Shared DTE project-file enumeration. |
| `Telescope/CodeIssue.cs` | Issue row model. |
| `Telescope/SyntaxHighlighter.cs` | Preview syntax tokenizer → colored runs. |
| `Telescope/ResultsFormatter.cs` | Formats candidate results for the list. |
| `Telescope/TelescopeFinder.cs` | Finder base/registry abstraction. |
| `Telescope/NeoVisualLog.cs`, `LogFileWriter.cs`, `NeoVisualTraceListener.cs` | Per-run two-file logs (`*-exp.log`, `*-main.log`). |

**Note:** the source folder is spelled `CardinalMovment` (intentional typo); the
namespace remains `CardinalNavigation`. Never "fix" the folder spelling.

### 2.3 Cardinal navigation algorithm (`WindowMatrix`)

`NavigateInDirection` → `ReduceWindowsAndSelectActive`, in order:

1. `RemoveHiddenOrTabbedWindows()` — drop windows at rect `0,0,0,0`.
2. `RemoveWindowsInWrongDirection(direction)` — keep only windows strictly in the
   requested direction.
3. `RemoveWindowsNotAligned(direction)` — axis overlap with the active window.
4. `RemoveWindowsByClosestAdjacency(direction)` — nearest window within the DPI divide.
5. `SortByLargestAdjacency(direction)` — tie-break by largest shared edge.
6. Activate `m_ActiveWindows.First()`.

All four "Remove..." steps are direction-parameterized with a local
`filterFunction`. Tune DPI-scaled constants in `CardinalNavigationConstants`, not
raw pixels. The algorithm currently takes only the **closest** window (no
chained-movement behavior).

### 2.4 Tool-window controller pattern

Each tool window gets an `IToolWindowController`. In **normal mode** `InputHandler`
routes `hjkl` + `controller.ActionKeys` to `TryMove`; `i` enters input mode; Esc
exits it. The hook pre-filter (`IsInteresting`) returns true for any key while a
tool window with action keys is in normal mode (`InputHandler.HasToolWindowActionKeys`).

**net472 has no `IReadOnlySet<T>`** — `ActionKeys` is `IReadOnlyCollection<Keys>`.
Pure logic is extracted into dependency-free classes (the `OverlayKeyHandler` /
`TextMotionNavigator` pattern) so the UI delegates to a unit-testable state
machine.

### 2.5 Telescope overlay

`TelescopeOverlay` is a WPF modal Window: title bar (finder + mode), prompt
TextBox (insert filter), results list, and a read-only **preview pane** on the
right. Keys route via `OverlayKeyHandler` (list navigation/modes) when focus is
on the list, or `TextMotionNavigator` (vim motions) when focus is on the preview.
**Ctrl+H / Ctrl+L switch `_focusTarget` between List and Preview.** The overlay
**closes on focus loss** (`Deactivated` → `CloseOverlay`).

## 3. Keybindings

- **Leader key is Space** by default. Bindings are user-configurable via
  `%APPDATA%\MyExtension\keybindings.json` (read only if it exists; merged over
  the embedded `MyExtension/default-keybindings.json`).
- Simple modifier shortcuts are distinguished by a `+` (e.g. `Ctrl+H`); leader
  sequences are matched after the leader key (e.g. `W`, `F,F`).
- Action names resolve in `InputHandler.ResolveAction`: `navigate-left/right/up/down`,
  `telescope`, `telescope-issues`, `telescope-references`, `telescope-grep`,
  `telescope-implementation`, `toggle-solution-explorer`, or `command:<VsCommandName>`.
- To add a *new built-in action*, add a case in `ResolveAction` and a line in
  `default-keybindings.json`.

Built-in defaults (`MyExtension/default-keybindings.json`): `Ctrl+H/J/K/L` →
navigate; `Space+B,D` close; `Space+W` save; `Space+Q` exit; `Space+E`
toggle-solution-explorer; `Space+F,F` GoToFile; `Space+F,T` telescope;
`Space+F,D` telescope-issues; `Space+F,R` telescope-references; `Space+F,G` telescope-grep;
`Space+F,I` telescope-implementation; `Space+C,W` Command Window;
plus Git/build/terminal
`command:` bindings.

## 4. Diagnostics = test contract

The e2e harness asserts on deterministic runtime log lines. Every feature that
needs a test must emit a deterministic diagnostic. The canonical lines are:

- `[NeoVisual] navigate direction=...`
- `[NeoVisual] leader-binding executed: ...`
- `[NeoVisual] shortcut-binding executed: ...`
- `[NeoVisual] toolwindow-move key=... -> arrow vk=...`
- `[NeoVisual] toolwindow-enter-input` / `toolwindow-exit-input`
- `[NeoVisual] solution-explorer toggled open/closed`
- `[NeoVisual] solution-explorer open/rename/move/add/expand/collapse`
- `[NeoVisual] solution-explorer select file=...` / `select none` (programmatic first-source-file selection via DTE `UIHierarchyItem.Select`)
- `[NeoVisual] editor-view-opened file=...` (from `VimModeTracker.TextViewCreated`)
- `[NeoVisual] vim-mode=Insert|Normal|Replace` (from `VimModeTracker.UpdateTypingFromMode`)
- `[NeoVisual] text-motion key=... caret=...` / `[NeoVisual] textinput-enter-input start|end|after caret=...`
- `[NeoVisual] block-caret active=True|False`
- `[NeoVisual] solution-explorer search-focus`
- `[Telescope] opened file: ...`
- `[Telescope] overlay closed`
- `[Telescope] preview file=...` / `[Telescope] preview tokens=...`
- `[Telescope] opened issue: ... line=...` / `[Telescope] goto line=...`
- `[Telescope] references gathered reads=... writes=...` / `[Telescope] opened reference: file=... line=... col=... access=read|write`
- `[Telescope] grep hits=...` / `[Telescope] opened grep: file=... line=...`
- `[Telescope] implementations gathered count=...` / `[Telescope] opened implementation: file=... line=...`
- `[Telescope] focus target=List|Preview`
- `[Telescope] preview caret=... line=...`
- `[Telescope] prompt-motion key=... caret=...`

## 5. Testing

### 5.1 Offline unit tests (no VS needed)

Two hermetic test projects, both run with `dotnet run`, both supporting a
**substring filter** as the first arg and `--list`:

- `dotnet run --project tests/Telescope.Tests` — **56 tests**. Telescope overlay
  navigation + insert/normal mode (`OverlayKeyHandler`), file search
  (`FzfFilter`), file open (`FileFinder`), results formatting, log writer,
  preview-pane vim motions (`TextMotionNavigator`), syntax highlighting
  (`SyntaxHighlighter`), prompt motions, references finder
  (`ReferencesFinder`/`ReferenceHit`), grep finder (`GrepFinder`/`GrepHit`),
  implementation finder (`ImplementationFinder`/`ImplementationHit`).
- `dotnet run --project tests/NeoVisual.Tests` — **31 tests**. Keybinding parsing
  (`KeybindingConfig`), tool-window type + mode classification
  (`ToolWindowTypeResolver`, `GeneralToolWindowController`,
  `SolutionExplorerController`, `TextInputToolWindowController`), the injected-key
  re-entry guard (`InjectedKeyGuard`), the pure Explorer tree-walk seam
  (`HierarchyResolver`), `DistinctBy`, `RectCoordinate`.

`InternalsVisibleTo` is set for these assemblies. Extract pure logic into
dependency-free classes (the `OverlayKeyHandler` / `TextMotionNavigator` pattern) so
it stays unit-testable.

### 5.2 Live E2E tests (experimental instance)

`tools/test-e2e.ps1` boots the VS Experimental Instance with the extension
deployed and a real solution open, then runs functionality scenarios against that
live instance, asserting on the runtime log (with per-scenario focus
verification):

```
pwsh tools/test-e2e.ps1                              # all 35 scenarios
pwsh tools/test-e2e.ps1 -Tests telescope-open        # a single scenario
pwsh tools/test-e2e.ps1 -List                        # list scenarios
```

The **35 scenarios** (no known-RED remaining — `explorer-open-searchbox` was GREened
2026-09-27; a few scenarios flake on retry) are: `telescope-open`,
`telescope-search`, `telescope-navigate`, `telescope-wrap`, `telescope-mode`,
`telescope-open-file`, `telescope-issues`, `telescope-references`,
`telescope-grep`, `telescope-implementation`, `telescope-open-file-searchbox`,
`telescope-open-file-navigation`, `telescope-prompt-motions`,
`telescope-preview-motions`, `telescope-q-close`, `telescope-open-file-normal`,
`telescope-no-selection`, `telescope-preview`, `neovisual-window-nav`,
`neovisual-leader`, `neovisual-toolwindow`, `neovisual-explorer-toggle`,
`neovisual-explorer-open`, `neovisual-explorer-open-o`,
`neovisual-explorer-collapse`, `neovisual-explorer-rename`,
`neovisual-explorer-add`, `neovisual-explorer-move`, `neovisual-explorer-move-editor-focus`,
`neovisual-editor-insert`, `neovisual-textinput-motions`, `seed-reset`,
`seed-leak`, `explorer-open-navigation`, `explorer-open-searchbox`.

### 5.3 E2E harness gotchas

- Scratch solution (`%TEMP%\telescope_scratch`) is seeded with many source files
  including nested folders (`Models/`, `Services/`). Seeding is **always reset**
  each run (`Reset-ScratchSolution` deletes + recreates the dir) so stale edits
  never leak, and every seeded file is written with **uniform** line endings
  (explicit CRLF; `Motions.cs` stays pure LF to preserve preview caret-position
  assertions). A bootstrap `Assert-SeedConsistent` self-check fails fast if a seed
  file has mixed EOL or drifted content — this prevents VS's "normalize line
  endings?" modal from stealing focus mid-test.
- **Seed-leak guard (end-of-run):** the bootstrap generates an **expected-result copy**
  of every seeded file (`log/seed-expected/`) right after the fresh reseed, and the LAST
  scenario (`seed-leak`) byte-compares the seed tree to that expected-result tree at the
  end of the run, failing on any seeded file that was **added / removed / modified** —
  proving no scenario wrote into the seed. There is **no ignorelist**: the one scenario
  that intentionally writes a seed (`neovisual-editor-insert` saves typed text into
  `Beta.cs`) refreshes that file's expected copy (`Update-SeedExpected`) after it
  validates the write, so any *other* or *later* change to any seed still fails.
  `obj/`+`bin/` build outputs are excluded from the seed set (they are not seeds).
  Skips gracefully under `-NoBootstrap` reuse mode.
- The overlay **closes on focus loss** (`Deactivated` → `CloseOverlay`), so a
  stale open overlay never swallows the next leader sequence.
- The harness verifies the foreground window before every key sequence and
  hammers Escape before leader sequences to avoid VsVim insert mode.
- Per-scenario assertions use a **fixed log baseline** (`Reset-LogBaseline`,
  `Wait-NewLogLine` searches lines after the baseline WITHOUT advancing a cursor).
- `Open-Telescope` waits for BOTH `open finder` AND `Focus prompt => True,
  mode=insert` before returning; `Close-Telescope` does NOT call
  `Bring-ToForeground` (that would deactivate the modal overlay) — it just sends
  Escapes until `overlay closed`.
- `Key.Return` (not `Key.Enter`) is the WPF enum for Enter.
- Enter on Solution Explorer can re-inject through the hook (the "Enter storm") —
  controllers must not re-inject a Return they themselves triggered.

## 6. Hard requirements

- **UI-thread affinity is mandatory.** Nearly every `IVs*` / `DTE` / `EnvDTE` call
  must run on the main thread. Almost every method begins with
  `ThreadHelper.ThrowIfNotOnUIThread()`. Never touch VS objects from a background
  thread.
- **Target framework is `net472`.** Avoid .NET 5+/BCL-only APIs; the repo
  hand-rolls `DistinctBy`. `IReadOnlySet<T>` is NOT available — use
  `IReadOnlyCollection<Keys>`. `LangVersion` 14, `Nullable` enabled.
- `Microsoft.VisualStudio.SDK` is referenced with `ExcludeAssets="runtime"` — VS
  supplies it at load time; no runtime SDK deps in build output.
- Two window APIs are used together: `IVsWindowFrame`/`IVsUIShell` for on-screen
  geometry, `EnvDTE.Window` for activation/framing.
- VsVim interop uses MEF contract `"Vim.IVim"` + reflection; resolve members via
  `Type.GetInterfaceMap` (VsVim implements interfaces explicitly); build event
  delegates from the interface's `EventHandlerType`. Get the buffer with the
  non-creating `IVim.TryGetVimBuffer(view, out buffer)`. Do not rely on
  `IVim.FocusedBuffer`, `GetActiveView2(fMustHaveFocus:1)`, or UI Automation.

## 7. Feature status

### Done and tested (live + unit)

- Leader-key binding system; user-configurable `keybindings.json`.
- Cardinal window navigation (Ctrl+H/J/K/L).
- Telescope overlay: open, search, navigate, insert/normal mode, open-file, wrap,
  preview pane.
- Solution Explorer controller: `o`/`Enter` open, `r` rename, `m` move, `a` add,
  `g` programmatically select the first source file (`solution-explorer select
  file=...` via DTE `UIHierarchyItem.Select`, escaping the injected-key csproj-open
  trap; tree expanded first, file opened + selection re-asserted ~1.5s to defeat
  VS's hover-preview focus steal), `h`/`l` collapse/expand folds, j/k navigate,
  i/Esc input mode.
- `Space+E` toggles Solution Explorer open/close.
- Telescope preview pane: `TextMotionNavigator` (shared pure vim motions +
  a/A/I insert placements), Ctrl+H/L focus switch between List/Preview, read-only,
  syntax highlighting (`SyntaxHighlighter`).
- Telescope prompt vim motions + white block caret in normal mode.
- Editor insert-mode swallowing regression guard (`neovisual-editor-insert`).
- Tool-window action-key leak guard (`FocusGuard`): `o`/`r`/`m`/`a`/hjkl are routed to a
  tool-window controller only when that surface actually owns focus — `VimModeTracker.IsEditorFocused`
  (event-driven) vetoes the routing/`ExitToolWindowInputMode`/`IsTyping` paths via a pure
  `FocusGuard`, except for genuine text-input tool windows or input-mode controllers
  (`neovisual-explorer-move-editor-focus`, deterministic stale-frame sentinel).
- Vim text motions in text-input tool windows (`TextInputToolWindowController`)
  + block caret in normal mode, line caret in insert.
- Solution Explorer search box: `i` (normal mode) focuses the search box; WPF
  TextBox motions via shared `TextMotionHelper`.
- Code-issues finder (`Space+F D`): VS Error List warnings/errors + TODO markers,
  preview jumps to line, Enter opens file at line.
- References finder (`Space+F R`): every reference to the symbol at the caret,
  with read/write access from Roslyn find-references; preview jumps to the
  reference line; Enter opens the file at the line.
- Grep finder (`Space+F G`): query-driven search of the solution's project
  files (`IQueryFinder` seam + ~200ms debounce, fzf skipped for query finders);
  preview jumps to the hit line; Enter opens the file at the line.
- Implementation finder (`Space+F I`): implementations/overrides of the symbol
  at the caret via Roslyn `FindImplementationsAsync` (first in-source declaring
  location, deterministic type-before-member ordering); preview jumps to the
  implementation line; Enter opens the file at the line.

### Pending (user-requested, NOT yet implemented)

- **Telescope finder**: fzf — with preview pane. (Scope deferred by user
  2026-09-19; the overlay already uses fzf internally as its filter engine.)

## 8. Build & test commands

- Build: `dotnet build` (VSIX — no `dotnet run`).
- Offline units: `dotnet run --project tests/Telescope.Tests` (56) and
  `dotnet run --project tests/NeoVisual.Tests` (31).
- Live E2E: `pwsh tools/test-e2e.ps1` (35 scenarios; no known-RED; a few flake on retry);
  subset with `-Tests a,b,c`; list with `-List`.
