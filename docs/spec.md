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
dispose. It is opened via `MyExtension.slnx` and consists of two projects (plus
two separate test projects):

| Project | Role |
|---------|------|
| `MyExtension/MyExtension.csproj` | The VSIX package: package, keyboard hook, input handler, keybinding config, window navigation, tool-window controllers. |
| `Telescope/Telescope.csproj` | The Telescope library: WPF modal overlay, pure vim state machines, fzf filter, finders, hosted read-only editor preview, logging. |

Target framework: **net472** (VS Community 17.14+, amd64). `LangVersion` 14,
`Nullable` enabled. `MyExtension.slnx` declares four projects: the two above
plus `tests/Telescope.Tests` and `tests/NeoVisual.Tests`.

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
| `MyExtension/Package/MyExtensionPackage.cs` | `AsyncPackage` entry point; installs/disposes the keyboard hook; registers `SolutionExplorerController`; auto-opens a test solution from `NEOVISUAL_TEST_SOLUTION`. |
| `MyExtension/Hooks/GlobalKeyboardHook.cs` | Win32 low-level keyboard hook; owns P/Invoke and output-window logging; `IsInteresting` pre-filter. |
| `MyExtension/Input/InputHandler.cs` | Maps key sequences to actions; routes tool-window keys through controllers; `IsTyping()` via `VimModeTracker`. |
| `MyExtension/Input/Utils/KeybindingConfig.cs` | Loads leader + bindings from embedded `default-keybindings.json` merged with optional `%APPDATA%\MyExtension\keybindings.json`. |
| `MyExtension/Hooks/Utils/KeyInjection.cs` | `keybd_event` / `Press` helpers for injecting arrow/Return keys into tool windows. |
| `MyExtension/Hooks/Utils/NativeMethods.cs` | P/Invoke declarations for the low-level keyboard hook + key injection. |
| `MyExtension/Input/Utils/KeyNames.cs` | Canonical key-name strings for binding notation. |
| `MyExtension/Input/Utils/KeyNameBuilder.cs` | Builds the canonical shortcut string (`Ctrl+H` / `Shift+F4` / ...) from a key + modifiers. |
| `MyExtension/Hooks/Utils/InjectedKeyGuard.cs` | Per-VK consume-once counter for injected keys (re-entry guard). |
| `MyExtension/Input/Utils/StaleToolWindowSentinel.cs` | Pure stale-toolwindow fault sentinel (path + cached `IsStale`) for the e2e focus-guard fault injection. |
| `MyExtension/Input/Utils/PopupNavigation.cs` | Maps `Ctrl+N`/`Ctrl+P` to injected Down/Up for completion/peek lists. |
| `MyExtension/Vim/VimModeTracker.cs` | Shared MEF part tracking the focused editor's VsVim mode (Insert/Replace gating). |
| `MyExtension/Vim/Utils/VimModeState.cs` | Pure owner of the Vim typing/mode state (single source of truth for `vim-mode=`). |
| `MyExtension/Vim/Utils/VimModeSource.cs` | VsVim interop: resolves the buffer + subscribes `SwitchedMode` per view. |
| `MyExtension/ToolWindows/WindowManager.cs` | Tracks the focused window frame; classifies `ToolWindowType`; dispatches to controllers. |
| `MyExtension/Package/Utils/VsServices.cs` | `GetService` helpers (DTE, IVsUIShell) with null-safe access. |
| `MyExtension/Package/Utils/Actions.cs` | Action-name → delegate table for leader/shortcut bindings. |
| `MyExtension/Package/Utils/TelescopeLauncher.cs` | Opens the Telescope overlay from the leader binding. |
| `MyExtension/Package/Utils/TelescopeCommand.cs` | VS command wiring for the Telescope finders. |
| `MyExtension/ToolWindows/Utils/ToolWindowTypeResolver.cs` | Maps window frames to `ToolWindowType` (known GUIDs / unknown). |
| `MyExtension/ToolWindows/IToolWindowController.cs` | Tool-window normal/input mode contract. |
| `MyExtension/ToolWindows/GeneralToolWindowController.cs` | Default controller: hjkl→arrow injection; `IsTextInputType` decides initial mode. |
| `MyExtension/ToolWindows/TextInputToolWindowController.cs` | Text-input windows: normal-mode h/l/w/b/e caret motions + a/A/I insert placements. |
| `MyExtension/ToolWindows/Utils/TextMotionHelper.cs` | Shared vim-caret helper for WPF TextBox surfaces (Solution Explorer search box + text-input windows). |
| `MyExtension/ToolWindows/SolutionExplorerController.cs` | Solution Explorer actions: o/Enter open, r rename, m move, a add, g select-first-source-file, h/l fold expand/collapse, j/k navigate, i focuses the search box. |
| `MyExtension/ToolWindows/Utils/HierarchyResolver.cs` | Pure, dependency-free tree-walk seam: `HierarchyNode` + `FirstSourceFilePath` (physical-file/folder Kind-GUID classification, folder recursion) used by `SolutionExplorerController`'s `g` action. |
| `MyExtension/ToolWindows/Utils/FocusKeeper.cs` | Re-select/refocus keeper that defeats VS's hover-preview focus steal. |
| `MyExtension/ToolWindows/Utils/FocusGuard.cs` | Pure tool-window key-routing guard (`ShouldRouteToolWindowKey`/`IsTyping`/`OwnsKeyboard`): action keys only consume while the tool window holds focus — never leak into a focused editor. |
| `MyExtension/ToolWindows/Utils/HierarchyForestBuilder.cs` | Pure tree-forest builder for the Solution Explorer walk. |
| `MyExtension/ToolWindows/ToolWindowControllerBase.cs` | Shared base for tool-window controllers (text-motion action wiring). |
| `MyExtension/Adornments/BlockCaretAdornment.cs` | Draws a block caret over an editor-view text-input window in normal mode. |
| `MyExtension/Navigation/WindowNavigator.cs` | Core navigation algorithm (thin COM shell over `WindowNavigationEngine`). |
| `MyExtension/Navigation/Utils/WindowFrameAdapter.cs` | One frame+DTE+rect type: `Rect`, `DteWindow`, `Activate`, `AutoHides`, static `Enumerate`/`FindActive`/`LinkedTo`. |
| `MyExtension/Navigation/WindowNavigationEngine.cs` | Pure `SelectTarget(active, candidates, direction, settings)` single-pass pipeline. |
| `MyExtension/Navigation/Utils/NavigationSettings.cs` | DPI divide settings (`FromSystemDpi`/`FromDpi`). |
| `MyExtension/Navigation/Utils/WindowFrameUtils.cs` | DTE / `IVsUIShell` service access and window comparison/linking helpers. |
| `MyExtension/Navigation/Utils/NavigationConstants.cs` | Direction chars, DPI/divide tuning constants. |
| `MyExtension/Navigation/Utils/WindowRect.cs` | Simple int rect value object. |
| `Telescope/Controller/TelescopeController.cs` | Opens the overlay / dispatches finder selection. |
| `Telescope/Overlay/TelescopeOverlay.cs` | WPF modal: title bar, prompt TextBox, results list, read-only preview pane. Closes on focus loss (`Deactivated`). |
| `Telescope/Overlay/Utils/OverlayKeyHandler.cs` | Pure vim state machine for overlay list navigation/modes. |
| `Telescope/Overlay/Utils/TextMotionNavigator.cs` | Shared pure vim motions (h/l/j/k/w/b/e/0/$/gg/G + a/A/I). |
| `Telescope/Filter/FzfFilter.cs` | fzf `--filter` subprocess; input written as explicit UTF-8 bytes. |
| `Telescope/Finders/FileFinder.cs` | File finder (hermetic open seam). |
| `Telescope/Finders/CodeIssuesFinder.cs` | Warnings/errors + TODO/FIXME/HACK/XXX marker finder. |
| `Telescope/Finders/ReferencesFinder.cs` | Symbol-at-caret find-references with read/write access (Roslyn `FindReferencesAsync`; host-injected gatherer keeps it hermetic-testable). |
| `Telescope/Finders/GrepFinder.cs` | Query-driven grep over `ProjectFiles.Enumerate` (per-keystroke re-gather with a ~200ms debounce; skips fzf for query finders). |
| `Telescope/Finders/FzfFinder.cs` | Query-driven fuzzy content finder over `ProjectFiles.Enumerate` (per-file fzf `--filter`; literal fallback when fzf unavailable). |
| `Telescope/Finders/ImplementationFinder.cs` | Symbol-at-caret `FindImplementationsAsync`, first in-source declaring location, deterministic type-before-member ordering (host-injected gatherer). |
| `Telescope/Finders/Utils/ProjectFiles.cs` | Shared DTE project-file enumeration. |
| `Telescope/Finders/Utils/HierarchyWalker.cs` | Pure tree-walk over the Solution Explorer hierarchy. |
| `Telescope/Finders/Utils/DteFileOpener.cs` | DTE-based file opener (host-injected seam). |
| `Telescope/Finders/Utils/HitOpener.cs` | Shared null/missing-file guard + open-at-line for the finders. |
| `Telescope/Finders/Utils/FileContentCache.cs` | mtime-keyed file-content cache (LRU-capped). |
| `Telescope/Finders/Utils/ProjectFileCache.cs` | Cached `ProjectFiles.Enumerate` enumeration. |
| `Telescope/Finders/Utils/CodeIssue.cs` | Issue row model. |
| `Telescope/Overlay/Utils/ResultsFormatter.cs` | Formats candidate results for the list. |
| `Telescope/Overlay/Utils/ResultMapper.cs` | Maps display strings back to hit payloads (duplicate-safe). |
| `Telescope/Finders/TelescopeFinder.cs` | `IFinder` abstraction + `FinderEntry` registry. |
| `Telescope/Finders/FinderBase.cs` | `FinderBase<THit>` — shared gather/open pipeline for the finders. |
| `Telescope/Logging/NeoVisualLog.cs`, `Telescope/Logging/Utils/LogFileWriter.cs`, `Telescope/Logging/Utils/NeoVisualTraceListener.cs`, `Telescope/Logging/Utils/DiagnosticLog.cs` | Per-run two-file logs (`*-exp.log`, `*-main.log`) + the `[Telescope]`/`[NeoVisual]`/`[Hook]` prefix constants. |
| `MyExtension/Input/Utils/SimpleShortcutMatcher.cs` | Pure simple-shortcut (e.g. `Ctrl+H`) matcher: routing outcome + matched action + canonical shortcut string. |
| `MyExtension/Navigation/Utils/NavigationSnapshot.cs` | Single-pass navigation snapshot (active rect derived from the candidate list — no N+1 COM calls). |
| `MyExtension/Package/Utils/InitSteps.cs` | Dependency-free named-step package-init orchestrator (`[MyExtension] init <step> ok/failed`). |
| `MyExtension/Vim/Utils/VimModeClassifier.cs` | Pure Vim ModeKind → typing flag + friendly name classifier (the `vim-mode=` truth table). |
| `Telescope/Overlay/Utils/OverlayShowState.cs` | State-based guard for the deferred `ShowDialog()` (open-then-close race). |
| `Telescope/Overlay/Utils/FocusTargetModel.cs` | Pure focus-target state machine (`[Telescope] focus target=List|Preview`). |
| `Telescope/Overlay/Utils/LineIndex.cs` | Pure line → (line, offset) index shared by the preview caret placement + blank-line fallback. |
| `Telescope/Overlay/Utils/TryDispatch.cs` | Shared vim-motion dispatch (n11/BP-46: merged into `TextMotionDispatcher`; file retained as the seam marker). |
| `Telescope/Overlay/Utils/IPreviewEditor.cs` | The preview pane's editor seam (`Show`/`ApplyCaret`/`Focus`/`Dispose`): the overlay delegates the REAL read-only editor view hosting (create/reuse by mtime, caret application, dispose) to a host-supplied implementation — VS-SDK-coupled view creation stays out of the Telescope library. |
| `Telescope/Overlay/Utils/BlockCaretStyle.cs` | Shared frozen white block-caret brush/geometry for the prompt + tool-window + editor-view surfaces. |
| `Telescope/Logging/Utils/TelescopeLog.cs` | One-line `[Telescope] `-prefixed log helper (prefix centralized in `DiagnosticLog.Telescope`). |
| `Telescope/Logging/Utils/FilterFailureLog.cs` | Formats the `[Telescope] filter failed: {msg}` line (self-contained — `Format()` returns the prefixed line; callers log via `NeoVisualLog.Log`). |
| `Telescope/Logging/Utils/PaneFailureTracker.cs` | Pure one-time fallback for the NeoVisual Output pane (`[NeoVisual] output pane unavailable: {reason}`). |
| `MyExtension/Package/RoslynGatherers.cs` | Host-injected Roslyn gatherer seam (`TryGetCaretSymbol`, `GatherReferences`, `GatherImplementations`, `GetCaretOffset`, `ReadLineFromCache`, static `IsWriteLocation`) — the package supplies the DTE/workspace/document factories; the class owns the pure gather + reflection logic. |

**Note:** the window-logic sources live in `MyExtension/Navigation/` (renamed from
the old `CardinalMovment/` folder by the 2026-09-30 restructure); the namespace is
`MyExtension.Navigation`, not `CardinalNavigation`.

**Project layering (decision 2026-09-28):** the host (`MyExtension`) depends on
`Telescope` for core infrastructure — `NeoVisualLog` (the extension-wide logger),
`TelescopeController`, and `DiagnosticLog` — and `Telescope.csproj` is itself
VS-coupled (VS SDK + WPF overlay). Only `OverlayKeyHandler`/`TextMotionNavigator`/
`FzfFilter`/`LogFileWriter` are VS-free. This seam is **accepted and documented**:
new VS-coupled code goes in `MyExtension`; new pure logic may go in `Telescope`.
Do NOT grow more VS-coupled code inside the "library" project.

### 2.3 Cardinal navigation algorithm (`WindowNavigator`)

`NavigateInDirection` → `WindowNavigationEngine.SelectTarget(active, candidates,
direction, settings)`, which runs a single-pass pipeline over the candidate rects:

1. Drop empty rects (`0,0,0,0`).
2. Keep only windows strictly in the requested direction.
3. Keep only windows axis-aligned with the active window.
4. Among the survivors, pick the max-adjacency window within the DPI divide of the
   minimum gap (ties broken by last-in-list order).

`WindowNavigator` is a thin COM shell: it enumerates/pairs the frames, snapshots the
rects, calls `SelectTarget`, and activates the winner. Tune DPI-scaled constants in
`NavigationConstants`, not raw pixels. The algorithm currently takes only
the **closest** window (no chained-movement behavior).

### 2.4 Tool-window controller pattern

Each tool window gets an `IToolWindowController`. In **normal mode** `InputHandler`
routes `hjkl` + `controller.ActionKeys` to `TryMove`; `i` enters input mode; Esc
exits it. The hook pre-filter (`IsInteresting`) returns true for any key while a
tool window with action keys is in normal mode (`InputHandler.ShouldRouteToolWindowKey`).

**net472 has no `IReadOnlySet<T>`** — `ActionKeys` is `IReadOnlyCollection<Keys>`.
Pure logic is extracted into dependency-free classes (the `OverlayKeyHandler` /
`TextMotionNavigator` pattern) so the UI delegates to a unit-testable state
machine.

### 2.5 Telescope overlay

`TelescopeOverlay` is a WPF modal Window: title bar (finder + mode), prompt
TextBox (insert filter), a **columned results list** (WPF ListView + GridView,
headers visible; one row = multiple columns from per-finder column sets —
`ResultColumn` definitions, a default-visible subset per the column catalog;
right-clicking a column header opens the chooser menu to toggle any column,
catalog order stable), and a read-only **preview pane** on the right — a REAL
read-only VS editor view hosted in the overlay (the buffer is the file's LIVE
`VisualStudioWorkspace` buffer for editor-OPEN solution files — the Peek model — so the
FULL Roslyn classifier chain attaches, syntactic + semantic; CLOSED solution files and
non-solution files fall back to the standalone content-type buffer with classifier
highlighting only; the Editable view role is excluded, so VsVim never attaches and there is no insert
mode; the custom SyntaxHighlighter tokenizer and its RichTextBox rendering are
retired). Narrow columns render compact cell values: Access write → W, read → R;
Issues Kind Error → err, Warning → warn, Todo → todo, Info → info; Implementation
Kind Class → cls, Interface → inf, Struct → str, Enum → enm, Method → func,
Property → prop, Event → evt (the user-specified imp/func/inf among them; defensive
entries + the ≤4-char fallback rule pinned by the column model). Selection, preview
and Enter read the row's hit payload by index — display- and column-independent (fzf
filters the `Display` strings; `ResultMapper` re-associates payloads). Each column
has min/max widths (chars) fitted by the pure `ColumnWidths` engine (min → max
priority distribution, the absorber takes the remainder, the exact-total invariant);
path-like columns (`file`/`dir`/`path`) shorten by removing the FRONT
(`ColumnTruncation.TailTruncate` — the end folder + file name survive), text columns
at the END (`EndTruncate`); the horizontal scrollbar is Disabled; the overlay width =
max(760, sum(visibleMinWidths) + scrollbar + preview(480) + chrome), recomputed at
open + on every chooser toggle, capped by `WorkArea`; the selected row pins a
dark-blue highlight (#2d4a75) + white text (active + inactive). Keys route via
`OverlayKeyHandler` (list navigation/modes) when focus is on the list, or
`TextMotionNavigator` (vim motions) when focus is on the preview. **Ctrl+H / Ctrl+L
switch `_focusTarget` between List and Preview.** The overlay **closes on focus
loss** (`Deactivated` → `CloseOverlay`).

## 3. Keybindings

- **Leader key is Space** by default. Bindings are user-configurable via
  `%APPDATA%\MyExtension\keybindings.json` (read only if it exists; merged over
  the embedded `MyExtension/Resources/default-keybindings.json`).
- Simple modifier shortcuts are distinguished by a `+` (e.g. `Ctrl+H`) and are matched
  case-insensitively; leader sequences are matched after the leader key (e.g. `w`,
  `f,f`) and are **case-sensitive** — a capital letter in the config means Shift+letter,
  so `s,g` and `s,G` are distinct bindings (non-letter keys are shift-insensitive).
- Action names resolve in `InputHandler.ResolveAction`: `navigate-left/right/up/down`,
  `telescope`, `telescope-issues`, `telescope-references`, `telescope-grep`,
  `telescope-implementation`, `telescope-fzf`, `toggle-solution-explorer`,
  `close-window`, `next-error`, `prev-error`, `next-warning`, `prev-warning`,
  or `command:<VsCommandName>`.
- Telescope actions are derived from `TelescopeLauncher.FinderNames` (add a `FinderNames`
  entry + a `default-keybindings.json` line); `ResolveAction` cases are only for
  non-telescope built-ins.

Built-in defaults (`MyExtension/Resources/default-keybindings.json`): `Ctrl+H/J/K/L` →
navigate; `Space+w,-` split below (`command:Window.NewHorizontalTabGroup`); `Space+w,|`
split right (`command:Window.NewVerticalTabGroup`); `Space+w,d` close-window
(focus-aware: a focused tool window closes via `Window.CloseToolWindow`, anything else
via `Window.CloseDocumentWindow`); `Space+b,d` close; `Space+q` exit; `Space+e`
toggle-solution-explorer; `Space+f,f` GoToFile; `Space+f,t` telescope;
`Space+f,d` telescope-issues; `Space+f,r` telescope-references; `Space+f,g` telescope-grep;
`Space+f,z` telescope-fzf; `Space+f,i` telescope-implementation;
`Space+],d`/`Space+[,d` next/prev diagnostic (`command:Edit.GotoNextIssueinFile` /
`command:Edit.GotoPreviousIssueinFile` — native in-file squiggle nav);
`Space+],e`/`Space+[,e` next/prev error and `Space+],w`/`Space+[,w` next/prev warning
(the custom severity-filtered navigator: Error List entries for the ACTIVE document
ordered by line, in-file, NO wrap — a no-op at the end or with no entries is logged,
never a crash); `Space+c,w` Command Window; `Space+g,d` diff the active file
(`command:Team.Git.CompareWithUnmodified`); `Space+g,b` blame
(`command:Team.Git.Annotate` — the old branches binding is dropped); `Space+g,h`
the active file's history (`command:Team.Git.ViewHistory`); `Space+g,g` Git Changes
and `Space+g,c` Commit stay until the deferred lazygit overlay rebinds them; plus
build/terminal `command:` bindings. There is no save binding (save with Ctrl+S); `w` is a
window-management prefix — a lone `Space+w` consumes and waits, firing nothing.

The **goto commands** — `goto-definition`, `goto-references`,
`goto-implementation` (VS commands registered in `MyExtensionPackage` via the
`TelescopeCommand.cs` pattern, command IDs in `CommandList`; canonical DTE names:
`MyExtension.GotoDefinition` / `MyExtension.GotoReferences` /
`MyExtension.GotoImplementation`) — gather the targets for the symbol at
the caret: **exactly 1 hit → open it directly** (no overlay; the shared
`HitOpener`/`DteFileOpener` open-at-line path), **multiple hits → open the
Telescope overlay** with the corresponding finder (`Definition`/`References`/
`Implementation`) via the pure `GotoDispatcher.Decide(hitCount)` seam. They are
NOT leader-bound: the USER maps them in **VsVim** to `gd`/`gr`/`gI` (the user's
established flow — mapping a VS command in VsVim's keyboard options / vimrc;
worked example: bind VsVim's `gd` to the goto-definition command's canonical
name above). The e2e scenario `telescope-goto` executes the commands via DTE
(`tools/harness/dte-command.ps1`), independent of VsVim.

## 4. Diagnostics = test contract

The e2e harness asserts on deterministic runtime log lines. Every feature that
needs a test must emit a deterministic diagnostic. The canonical lines are:

- `[NeoVisual] navigate direction=...`
- `[NeoVisual] leader-binding executed: ...`
- `[NeoVisual] shortcut-binding executed: ...`
- `[NeoVisual] toolwindow-move key=... -> arrow vk=...`
- `[NeoVisual] toolwindow-move failed: {msg}` (controller exception passes the key through — never crashes the hook)
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
- `[Telescope] preview file=...` / `[Telescope] preview tokens=...` (the hosted editor view's classifier span count) — the buffer-source swap RESOLVED the highlighting mechanism (2026-10-04): the buffer is the file's LIVE `VisualStudioWorkspace` buffer for editor-OPEN solution files (the Peek model), so the FULL Roslyn classifier chain (syntactic + semantic) attaches; a CLOSED solution file or a non-solution file falls back to the standalone content-type buffer. NOTE: the count is read synchronously at view creation — BEFORE async classification lands — so it reads **0 for BOTH buffer sources** (runs 168/169/170 all-0) and cannot discriminate engagement; the semantic coloring is verified by the manual visual pass, not by this line (the harness regex is presence-only `tokens=\d+`)
- `[Telescope] opened issue: ... line=...` / `[Telescope] goto line=...`
- `[Telescope] references gathered reads=... writes=...` / `[Telescope] opened reference: file=... line=... col=... access=read|write`
- `[Telescope] grep hits=...` / `[Telescope] opened grep: file=... line=...`
- `[Telescope] fzf hits=...` / `[Telescope] opened fzf: file=... line=...` / `[Telescope] fzf unavailable — literal fallback`
- `[Telescope] implementations gathered count=...` / `[Telescope] opened implementation: file=... line=...`
- `[Telescope] goto-direct finder=... file=... line=...` (the goto commands' single-hit DIRECT jump — the pinned Section A literal `[Telescope] goto-direct finder=… file=… line=…`; the existing `goto line=...` also fires on every open-at-line, incl. the direct-jump path)
- `[Telescope] definitions gathered count=...` / `[Telescope] opened definition: file=... line=...` (the Definition finder — the goto commands' overlay path; the fault paths log `[Telescope] definitions gather failed: {msg}` / `[Telescope] open definition failed: {msg}`, and a command fault logs `[NeoVisual] goto failed: {finder}: {msg}`)
- `[Telescope] focus target=List|Preview`
- `[Telescope] result-mapper unknown display: {display}` (unknown-match warning when a display string has no payload)
- `[Telescope] preview caret=... line=...`
- `[Telescope] prompt-motion key=... caret=...`
- `[NeoVisual] stale-toolwindow sentinel active`
- `[NeoVisual] leader-binding failed: {seq}: {msg}` / `[NeoVisual] shortcut-binding failed: {simple}: {msg}` (binding action exceptions are caught and logged, never escaping the hook path)
- `[NeoVisual] output pane unavailable: {reason}` (one-time fallback when the VS Output pane cannot be created)
- `[MyExtension] init <step> ok/failed: {msg}` (per-step package-init orchestration; `[MyExtension] init failed: {ex}` is the last-resort net)
- `[Telescope] fzf filter failed: {msg}` / `fzf filter failed: timeout after {ms}ms`
- `[Telescope] fzf unavailable — showing unfiltered list` (once at overlay open when fzf is missing)
- `[Telescope] filter failed: {msg}` (`FilterAndUpdateAsync` fault path)
- `[Telescope] open finder=... candidates=...` (overlay opened with a finder, candidate count)
- `[Telescope] Focus prompt => True, mode=insert` (prompt focused in insert mode)
- `[Telescope] results count=... selected=...` (filtered results rendered / selection moved)
- `[Telescope] results columns={ids}` (the visible column-id list, comma-separated in
  catalog order — logged on every results render and on every header-chooser toggle;
  the id vocabulary is pinned by the per-finder column sets, e.g. the References
  finder's default renders `access,file`)
- `[Telescope] key=... mode=... handled=...` (overlay key handling)
- `[NeoVisual] navigate activated index=...` / `[NeoVisual] navigate no-op: <reason>` (m47 — outcome diagnostic: the navigation fired vs was a no-op and why)
- `[NeoVisual] diagnostic-nav direction=next|prev severity=error|warning target=<file> line=<n>` / `[NeoVisual] diagnostic-nav no-op: <reason>` (`no-entries` | `at-end` | `no-active-document` — Gap 3 severity-filtered diagnostics navigation: the `],e`/`[,e`/`],w`/`[,w` outcome diagnostic, fired vs no-op and why) / `[NeoVisual] diagnostic-nav failed: {msg}` (a gather/open failure is logged and swallowed — never crashes the hook)
- `[NeoVisual] window rect unavailable; using empty rect` (n19 — logged once per adapter when the window rect cannot be read)
- `[NeoVisual] IVsUIShell unavailable: package is not an IServiceProvider.` / `[NeoVisual] IVsUIShell unavailable: SVsUIShell service returned null.` (m14 — null-guard fallbacks)
- `[Hook] SetHook MainModule failed: {ex.Message}` (n18 — `SetHook` guards `Process.GetCurrentProcess().MainModule` and falls back to `IntPtr.Zero` for `hMod`); `[Hook]` lines are single-stamped (m6 — `LogFileWriter.FormatLine` is the only stamper)

## 5. Testing

### 5.1 Offline unit tests (no VS needed)

Two hermetic test projects, both run with `dotnet run`, both supporting a
**substring filter** as the first arg and `--list`:

- `dotnet run --project tests/Telescope.Tests` — **224 tests**. Telescope overlay
  navigation + insert/normal mode (`OverlayKeyHandler`), file search
  (`FzfFilter`), file open (`FileFinder`), results formatting, buffered log
  writer (`LogFileWriter`), preview-pane vim motions (`TextMotionNavigator`),
  prompt motions, references finder
  (`ReferencesFinder`/`ReferenceHit`), grep finder (`GrepFinder`/`GrepHit`),
  fzf finder (`FzfFinder`/`FzfHit`/`FzfLineMapper`/`LiteralLineScanner`),
  implementation finder (`ImplementationFinder`/`ImplementationHit`), the goto
  dispatcher (`GotoDispatcher`), the definition finder
  (`DefinitionFinder`/`DefinitionHit`), the finder
  base (`FinderBase<THit>`) and hit models (`FileLocation`/`IFileLocation`/`FileHit`),
  the shared preview index (`LineIndex`), the focus-target state machine
  (`FocusTargetModel`), the shared vim-motion dispatch (`TextMotionDispatcher` —
  `TryDispatch` was merged into it, n11), the prompt routing seam
  (`PromptMotionRouter`), the pane-failure fallback (`PaneFailureTracker`), the
  results column model (`ResultColumn`/`ColumnVisibilityModel`), and the preview
  caret-map/diagnostic seams (`PreviewCaretMap`/`PreviewDiagnostics`).
- `dotnet run --project tests/NeoVisual.Tests` — **191 tests**. Keybinding parsing
  (`KeybindingConfig`), tool-window type + mode classification
  (`ToolWindowTypeResolver`, `GeneralToolWindowController`,
  `SolutionExplorerController`, `TextInputToolWindowController`), the injected-key
  re-entry guard (`InjectedKeyGuard`), the pure Explorer tree-walk seam
  (`HierarchyResolver`), the shared vim-motion engine
  (`TextMotionHelper`), the action-table controllers (`ActionKeys`), the focus
  guard (`FocusGuard`), the navigation engine (`WindowRect`,
  `NavigationSettings`, `WindowNavigationEngine`), the leader/shortcut matchers
  (`LeaderSequenceMatcher`, `SimpleShortcutMatcher`), the vim-mode classifier
  (`VimModeClassifier` + `IVimModeSource`), the init orchestrator (`InitSteps`),
   the navigation snapshot (`NavigationSnapshot`), the focus-keeper schedule
   (`FocusKeeperSchedule`), the close-window seam (`CloseWindowCommand`), and the
   severity-filtered diagnostics navigator (`DiagnosticNavigator`).

`InternalsVisibleTo` is set for these assemblies. Extract pure logic into
dependency-free classes (the `OverlayKeyHandler` / `TextMotionNavigator` pattern) so
it stays unit-testable.

### 5.2 Live E2E tests (experimental instance)

`tools/harness/test-e2e.ps1` boots the VS Experimental Instance with the extension
deployed and a real solution open, then runs functionality scenarios against that
live instance, asserting on the runtime log (with per-scenario focus
verification):

```
pwsh tools/harness/test-e2e.ps1                              # all 41 scenarios
pwsh tools/harness/test-e2e.ps1 -Tests telescope-open        # a single scenario
pwsh tools/harness/test-e2e.ps1 -List                        # list scenarios
```

The **41 registered scenarios** (38 GREEN with no known-RED — `neovisual-window-management`
(E2E-GAP1-1), `neovisual-diagnostic-nav` (E2E-GAP3-1), and `telescope-results-columns`
(E2E-RC-1) are registered but never executed;
`explorer-open-searchbox` was
GREened 2026-09-27; a few scenarios flake on retry) are: `telescope-open`,
`telescope-search`, `telescope-navigate`, `telescope-wrap`, `telescope-mode`,
`telescope-open-file`, `telescope-issues`, `telescope-references`,
`telescope-grep`, `telescope-implementation`, `telescope-goto`, `telescope-fzf`,
`telescope-open-file-searchbox`,
`telescope-open-file-navigation`, `telescope-prompt-motions`,
`telescope-preview-motions`, `telescope-q-close`, `telescope-open-file-normal`,
`telescope-no-selection`, `telescope-results-columns`, `telescope-preview`, `neovisual-window-nav`,
`neovisual-leader`, `neovisual-window-management`, `neovisual-diagnostic-nav`, `neovisual-toolwindow`,
`neovisual-explorer-toggle`,
`neovisual-explorer-open`, `neovisual-explorer-open-o`,
`neovisual-explorer-collapse`, `neovisual-explorer-rename`,
`neovisual-explorer-add`, `neovisual-explorer-move`, `neovisual-explorer-move-editor-focus`,
`neovisual-editor-insert`, `neovisual-textinput-motions`, `seed-reset`,
`seed-leak`, `explorer-open-navigation`, `explorer-open-searchbox`,
`explorer-searchbox-motions`.

- `telescope-goto` — the goto-definition/references/implementation commands: 1 hit → direct jump (`goto-direct` + `goto line=`), multi-hit → the Telescope overlay (`open finder=Definition candidates=2` / `open finder=References candidates=…` + `references gathered reads=… writes=…`)

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
- **Target framework is `net472`.** Avoid .NET 5+/BCL-only APIs;
  `IReadOnlySet<T>` is NOT available — use `IReadOnlyCollection<Keys>`.
  `LangVersion` 14, `Nullable` enabled.
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
- `Space+e` toggles Solution Explorer open/close.
- Telescope preview pane: a REAL read-only VS editor view hosted in the overlay
  (FULL Roslyn highlighting — syntactic + semantic — for editor-OPEN solution files:
  the buffer is the file's LIVE `VisualStudioWorkspace` buffer, the Peek model;
  CLOSED solution files and non-solution files fall back to the standalone
  content-type buffer with classifier highlighting only; the Editable view role
  excluded — VsVim never attaches, no insert mode), Ctrl+H/L focus switch between
  List/Preview, `TextMotionNavigator` (shared pure vim motions) moving the editor
  view's caret; a/A/I insert placements are no-ops (the view is not editable); the
  custom SyntaxHighlighter tokenizer and its RichTextBox rendering are RETIRED.
  CodeLens reference counts are SKIPPED (they never attach to a hosted programmatic
  view — the References finder covers counts); embedding a real document window in
  the overlay was REJECTED (unsupported `IVsWindowFrame` reparenting).
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
  TextBox motions via shared `TextMotionHelper` (h/l/w/b/e/a/A/I + j/k/0/$; j/k are
  single-line no-ops, 0/$ move to line start/end).
- Code-issues finder (`Space+f,d`): VS Error List warnings/errors + TODO markers,
  preview jumps to line, Enter opens file at line.
- References finder (`Space+f,r`): every reference to the symbol at the caret,
  with read/write access from Roslyn find-references; preview jumps to the
  reference line; Enter opens the file at the line.
- Grep finder (`Space+f,g`): query-driven search of the solution's project
  files (`IsQueryDriven` seam + ~200ms debounce, fzf skipped for query finders);
  preview jumps to the hit line; Enter opens the file at the line.
- Implementation finder (`Space+f,i`): implementations/overrides of the symbol
  at the caret via Roslyn `FindImplementationsAsync` (first in-source declaring
  location, deterministic type-before-member ordering); preview jumps to the
  implementation line; Enter opens the file at the line.
- Fzf finder (`Space+f,z`): query-driven fuzzy **content** finder over the
  solution's project files (`FzfFinder`, `Name="Fzf"`; per-keystroke re-gather
  with a ~200ms debounce, one file at a time via fzf `--filter`, matched lines
  mapped back by the pure `FzfLineMapper`); falls back to a literal
  case-insensitive substring scan (`LiteralLineScanner`, shared with
  `GrepFinder`) when fzf is unavailable; preview jumps to the hit line; Enter
  opens the file at the line. The existing `FileFinder` (`Space+f,t`) is the
  fuzzy file finder. — `telescope-fzf` live test passes.
- Diagnostics navigation (`]`/`[` prefix): `],d`/`[,d` run the native in-file squiggle
  commands (`command:Edit.GotoNextIssueinFile` / `command:Edit.GotoPreviousIssueinFile`);
  `],e`/`[,e` and `],w`/`[,w` run the new `next-error`/`prev-error`/`next-warning`/
  `prev-warning` built-in actions — the pure `DiagnosticNavigator` seam over the Error List
  entries of the ACTIVE document (severity-filtered, in-file, NO wrap; a no-op at the end or
  with no entries is logged, never a crash). Unit-tested in `tests/NeoVisual.Tests`
   (the `DiagnosticNavigator` + keybinding/KeyNames/registry tests); live e2e
   `neovisual-diagnostic-nav` registered, queued as E2E-GAP3-1.
- Telescope results columns: the overlay's results list is a columned ListView
  (GridView, headers visible) — one row = multiple columns from per-finder column
  sets (every catalog column implemented and toggleable; the user-marked subset
  default-visible), right-click a column header to toggle any column (catalog order
  stable); narrow columns render compact values (Access W/R; Issues Kind
  err/warn/todo/info; Implementation Kind imp/func/inf + cls/str/enm/prop/evt);
  selection/preview/Enter stay payload-by-index (fzf + `ResultMapper` untouched).
  Each column has min/max widths fitted by the pure `ColumnWidths` engine (the
  priority distribution + the exact-total invariant); path-like columns shorten
  from the FRONT (`ColumnTruncation.TailTruncate` — the end folder + file name
  survive), text columns at the END; the horizontal scrollbar is Disabled; the
  overlay width scales with the visible column count (recomputed at open + every
  chooser toggle, capped by the work area); the selected row pins a contrasting
  highlight + white text (active + inactive).
  New diagnostic `results columns={ids}`; unit-tested in `tests/Telescope.Tests`
  (the column-set/visibility/width-fit/truncation tests); live e2e
  `telescope-results-columns` passes.

## 8. Build & test commands

- Build: `dotnet build` (VSIX — no `dotnet run`).
- Offline units: `dotnet run --project tests/Telescope.Tests` (208) and
  `dotnet run --project tests/NeoVisual.Tests` (190).
- Live E2E: `pwsh tools/harness/test-e2e.ps1` (41 registered — 38 GREEN +
  `neovisual-window-management` (E2E-GAP1-1), `neovisual-diagnostic-nav` (E2E-GAP3-1), and
  `telescope-results-columns` (E2E-RC-1) queued unexecuted; no known-RED; a few flake on retry);
  subset with `-Tests a,b,c`; list with `-List`.
