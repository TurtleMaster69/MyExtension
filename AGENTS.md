# AGENTS.md

> **Resume checkpoint:** if the user says "continue" to resume prior work, read
> `docs/progress.md` first — it holds the in-flight task state, the pending queue,
> and next steps. (`.opencode/PROGRESS.md` was superseded by it — do not read or
> recreate that file.)

Visual Studio extension (VSIX) implementing Cardinal-style window navigation,
leader-key keyboard bindings, a Telescope-style fuzzy finder overlay, and
tool-window navigation. Single project: `MyExtension/MyExtension.csproj`, opened
via `MyExtension.slnx`.

More detailed architecture lives in `.opencode/skills/vs-extension-dev/SKILL.md`;
read it before making changes. This file only covers what's easy to get wrong.

## Trailmark is mandatory for structural code questions

This repo vendors the [Trail of Bits Trailmark](https://github.com/trailofbits/trailmark)
plugin (skills `trailmark`, `trailmark-structural`, `trailmark-summary`,
`trailmark-finding-triage`, `trailmark-review-gate`, `graph-evolution`, etc. — see
`.opencode/skills/trailmark`). Trailmark parses C# (net472) code into a queryable
graph of functions/calls. **Every agent, hub, and subagent MUST use Trailmark instead
of `grep`/`glob`/manual reading for anything it can answer better.**

- **Required** (do NOT hand-trace with grep): call paths (`paths_between`),
  callers/callees (`callers_of`/`callees_of`), transitive reach
  (`ancestors_of`/`reachable_from`), entrypoint reachability
  (`entrypoint_paths_to`), blast radius, taint propagation, privilege boundaries,
  complexity hotspots, subgraph/edge queries, structural diffs, attack surface,
  "who calls X" / "what does Y reach" / "what breaks if I change Z".
- **`grep`/`glob`/`Read` are only for what Trailmark cannot do**: literal text and
  strings; non-source files (JSON, Markdown, `.csproj`, docs, scripts); a single known
  file/line lookup where a graph adds nothing.
- **Pre-flight**: `trailmark --version` (or `uv run trailmark --version`). If missing,
  install with `uv tool install trailmark` — **never silently fall back to manual
  code reading** (the `trailmark` skill's "Rationalizations to Reject" table forbids it).
- **Python snippets** run via `uv run --with trailmark python -` (a `uv tool` env is
  not importable). Always run `engine.preanalysis()` before consuming blast-radius /
  taint / privilege-boundary / subgraph data.
- **Version gate**: this repo's CLI is 0.5.0. Gate v0.4+/v0.5+ APIs by reported
  version (note: v0.5 adds no new QueryEngine methods, so `hasattr()` cannot detect
  it). Read the full `trailmark` SKILL.md body before relying on a version-gated call.
- **Repo-specific traps (verified 2026-09-27 — do not report these as findings):**
  1. **Always parse with `language="c_sharp"`.** The CLI defaults `--language`
     to `python`, so a bare `trailmark analyze`/`trailmark diff` silently returns an
     empty graph/diff on this repo.
  2. **Cross-class calls become `proxy` nodes.** 485 of 1083 nodes are
     `proxy.unresolved:<Type>.<Member>`; callers attach to the proxy, not the real
     method. `callers_of("KeyInjection.Press")` returns **0** even though 6 in-repo
     callers exist. A `0`-caller result on a public/static member is **SUSPECT** —
     query the `proxy.unresolved:<Type>.<Member>` id, or cross-check `callees_of`
     from the caller side. **Never report "dead code / no callers" from a bare
     `callers_of` 0.**
  3. **No detected entrypoints.** This is a VSIX, so `entrypoints`,
     `entrypoint_paths_to`, `tainted`, `privilege_boundary`, and `attack_surface` are
     all empty. Do NOT load `trailmark-finding-triage` / `trailmark-review-gate` /
     `graph-evolution` expecting signal here. Use `callers_of`/`callees_of`,
     `paths_between`, `reachable_from`, `complexity_hotspots`, and blast radius.

## Build & run

- `dotnet build` (or build in VS). This is a VSIX — a plain `dotnet run` does not work.
- **Test by running in the VS Experimental Instance**: F5 (or `Start` with the
  csproj) launches VS with the extension loaded. Unit tests live in the two
  `tests/` projects (see below); there is no `test`/`lint`/`typecheck` target.
- Debug log output: `Debug.WriteLine` plus a custom **"NeoVisual"** VS Output
  window pane (created in `GlobalKeyboardHook`). Look there for hook/input logs.

## Offline unit tests (no VS needed)

Two hermetic test projects, both run with `dotnet run` and both supporting a
**substring filter** as the first arg (and `--list` to print tests):

- `dotnet run --project tests/Telescope.Tests` — Telescope overlay logic.
  Covers overlay navigation + insert/normal mode (`OverlayKeyHandler`, extracted
  pure state machine), file search (`FzfFilter`), file open (`FileFinder`
  hermetic seam), results formatting, log writer (buffered `LogFileWriter`),
  the preview-pane vim motions (`TextMotionNavigator`), the finder base
  (`FinderBase<THit>` + `FileLocation`/`IFileLocation`/`FileHit` hit models),
  the shared preview index (`LineIndex`), the focus-target state machine
  (`FocusTargetModel`), the shared vim-motion dispatch (`TextMotionDispatcher` —
  `TryDispatch` was merged into it, n11), the prompt routing seam
  (`PromptMotionRouter`), the syntax tokenizer (`SyntaxHighlighter.Tokenize`),
  and the pane-failure fallback (`PaneFailureTracker`).
  `-- KeyHandler`, `-- Preview`, `-- FileFinder`, `-- Fzf`, `-- TextMotionDispatcher`,
  `-- LineIndex`, `-- FocusTarget`, `-- Syntax` run subsets.
  Currently **157 tests, all passing**.
- `dotnet run --project tests/NeoVisual.Tests` — NeoVisual pure logic: keybinding
  parsing (`KeybindingConfig`), tool-window type + mode classification
  (`ToolWindowTypeResolver`, `GeneralToolWindowController`, `SolutionExplorerController`),
  the injected-key re-entry guard (`InjectedKeyGuard`), the shared vim-motion
  engine (`TextMotionHelper`), the action-table controllers (`ActionKeys`),
  the focus guard (`FocusGuard`), the navigation engine
  (`WindowRect`, `NavigationSettings`, `WindowNavigationEngine`), the
  leader/shortcut matchers (`LeaderSequenceMatcher`, `SimpleShortcutMatcher`),
  the vim-mode classifier (`VimModeClassifier` + `IVimModeSource`), the
  init orchestrator (`InitSteps`), the navigation snapshot (`NavigationSnapshot`),
  and the focus-keeper schedule (`FocusKeeperSchedule`).
  `-- Keybinding`, `-- ToolWindow`, `-- SolutionExplorer`, `-- InjectedKeyGuard`,
  `-- SimpleShortcutMatcher`, `-- VimModeClassifier`, `-- InitSteps`,
  `-- NavigationSnapshot`, `-- FocusKeeperSchedule`, etc.
  run subsets. Currently **168 tests, all passing**.

`InternalsVisibleTo` is set in both `Telescope.csproj` and `MyExtension.csproj`
for these test assemblies. If you extract pure logic out of a VS/WPF-coupled
class, mirror the `OverlayKeyHandler` / `TextMotionNavigator` pattern
(dependency-free state machine the UI delegates to) so it stays unit-testable.
The E2E behavior is verified by the live harness (`tools/harness/test-e2e.ps1`).

## Live E2E tests (experimental instance)

`tools/harness/test-e2e.ps1` boots the VS Experimental Instance with the extension deployed and a real
solution open, then runs every functionality scenario against that **live** instance, asserting on
the runtime log (with per-scenario focus verification so keys are never typed into the wrong
window):

```
pwsh tools/harness/test-e2e.ps1                              # all 35 scenarios
pwsh tools/harness/test-e2e.ps1 -Tests telescope-open        # a single scenario
pwsh tools/harness/test-e2e.ps1 -Tests telescope-search,telescope-navigate
pwsh tools/harness/test-e2e.ps1 -List                        # list scenarios
```

Scenarios (35 total; no known-RED remaining — `explorer-open-searchbox` was GREened
2026-09-27 and `telescope-implementation`'s intermittent Enter-delivery issue was
fixed in `7c6569b`; a few scenarios are flaky on retry):
- `telescope-open` — Space F T opens overlay, prompt focused insert
- `telescope-search` — typing filters candidates (promptChanged + results)
- `telescope-navigate` — normal-mode j/k move selection across ≥4 files; i returns to search
- `telescope-wrap` — selection wraps around the result list (k at 0 -> last, j at last -> 0)
- `telescope-mode` — insert <-> normal toggling (Esc/i/a)
- `telescope-open-file` — Enter opens the matched file in the editor
- `telescope-issues` — Space F D: warnings/errors/TODO finder filters, previews, opens at line
- `telescope-references` — Space F R: references to the symbol at the caret (read/write access), previews, opens at line
- `telescope-grep` — Space F G: query-driven search of the solution's files (grep hits per typed query), previews, opens at line
- `telescope-implementation` — Space F I: implementations/overrides of the symbol at the caret, previews, opens at line
- `telescope-open-file-searchbox` — insert-mode query, wait for the filtered result, Enter opens it
- `telescope-open-file-navigation` — Esc to normal, j/k move the selection, Enter opens the moved-to row
- `explorer-open-navigation` — `g` programmatically selects the first source file (`solution-explorer select file=...`) then `o` opens it
- `explorer-open-searchbox` — i focuses the search box, query filters the tree, o opens (GREened 2026-09-27 via `ReturnFocusToTree`)
- `telescope-prompt-motions` — normal-mode prompt h/l/w/b/e/0/$ caret motions over the query
- `telescope-preview-motions` — preview pane h/l/j/k/w/b/e/0/$/g/G motions over the seeded Motions.cs
- `telescope-q-close` — q closes the overlay in normal mode
- `telescope-open-file-normal` — Enter selects the match in NORMAL mode
- `telescope-no-selection` — j/k on an empty result list is a no-op (selection stays 0)
- `telescope-preview` — preview shows selected file; Ctrl+L/Ctrl+H switch list<->preview; vim motions in preview; syntax-highlighted tokens
- `neovisual-window-nav` — Ctrl+H/J/K/L fire Cardinal navigation (shortcut-binding + navigate)
- `neovisual-leader` — Space+W/Space+E fire leader bindings
- `neovisual-toolwindow` — Solution Explorer hjkl navigation + i/Esc input-mode
- `neovisual-explorer-toggle` — Space+E opens/closes Solution Explorer (toggle)
- `neovisual-explorer-open` — l expands fold, j/k navigate, Enter opens a file
- `neovisual-explorer-open-o` — o opens the selected file
- `neovisual-explorer-collapse` — h collapses the fold
- `neovisual-explorer-rename` — r starts rename (F2), Escape cancels
- `neovisual-explorer-add` — a runs the Add Item command
- `neovisual-explorer-move` — m runs the Move command (tree focused)
- `neovisual-explorer-move-editor-focus` — with the stale-frame fault injected, an editor-focused m must NOT fire a tree action (FocusGuard leak guard)
- `neovisual-editor-insert` — insert-mode typing reaches the editor (hook must not swallow text)
- `neovisual-textinput-motions` — Command Window: h/l/w/b/e/a/A/I caret/insert motions + block caret in normal mode
- `seed-reset` — seeding always resets the scratch solution (a stale edit is removed) and every seeded file has uniform EOL (no "normalize line endings?" focus-steal)
- `seed-leak` — filesystem-only leak guard: the bootstrap snapshots every seeded file's SHA-256 and this last scenario proves NO seeded file was added/removed/modified during the run (no scenario may write into the seed)

Exit code 0 = all selected passed.

Key facts that make this reliable:
- The scratch solution (`%TEMP%\telescope_scratch`) is seeded with many source files including
  nested folders (`Models/`, `Services/`), so navigation/search scenarios exercise 10 candidates.
  Seeding is **always reset** each run (`Reset-ScratchSolution`) and every seeded file is written
  with **uniform** line endings; a bootstrap `Assert-SeedConsistent` self-check fails fast on a
  mixed-EOL/drifted seed so VS never shows the "normalize line endings?" modal (which would steal
  focus and break a test). `Motions.cs` stays pure LF to preserve preview caret-position pins.
- The overlay **closes on focus loss** (`TelescopeOverlay.Deactivated` → `CloseOverlay`), so a
  stale open overlay can never swallow the next leader sequence. This also means the harness's
  "is the overlay still open?" check is deterministic (asserts on the `[Telescope] overlay closed`
  log line).
- The harness verifies the foreground window belongs to the experimental VS instance
  (`Assert-VsFocused` / `Assert-OverlayFocused`) before every key sequence, and hammers Escape
  before leader sequences to guarantee the editor is not in VsVim insert mode (where Space would
  type a literal space instead of starting a leader).
- Per-scenario assertions use a **fixed log baseline** (`Reset-LogBaseline`, `Wait-NewLogLine`
  searches lines after the baseline WITHOUT advancing) so stale lines from an earlier scenario can
  never satisfy a later assertion. Do NOT reintroduce a cursor that advances on match.
- `Open-Telescope` waits for BOTH `open finder` AND `Focus prompt => True, mode=insert` before
  returning, so injected keys are guaranteed to land in the overlay, not the editor. It hammers
  Escape first to get VsVim out of insert mode.
- `Close-Telescope` does NOT call `Bring-ToForeground` (that would deactivate the modal overlay
  and trigger the Deactivated->close); it just sends Escapes until `overlay closed` is seen.
- Diagnostics added so the harness can assert each feature: `[NeoVisual] navigate direction=...`,
  `[NeoVisual] navigate activated index=...` / `[NeoVisual] navigate no-op: <reason>` (m47 — outcome
  diagnostic: the navigation fired vs was a no-op and why),
  `[NeoVisual] leader-binding executed: ...`, `[NeoVisual] shortcut-binding executed: ...`,
  `[NeoVisual] toolwindow-move key=... -> arrow vk=...` (bare contract — the arrow-fallback log was
  aligned to it, n10), `[NeoVisual] toolwindow-move failed: {msg}`
  (controller exception passes the key through — never crashes the hook), `[NeoVisual] toolwindow-enter-input` /
  `toolwindow-exit-input`, `[NeoVisual] solution-explorer toggled open/closed`,
  `[NeoVisual] solution-explorer open/rename/move/add/expand/collapse`,
  `[NeoVisual] solution-explorer select file=...` (programmatic first-source-file
  selection via DTE `UIHierarchyItem.Select`; `select none` when none reachable),
  `[NeoVisual] editor-view-opened file=...` (logged from `VimModeTracker.TextViewCreated`),
  `[NeoVisual] vim-mode=Insert|Normal|Replace` (logged from `VimModeTracker.UpdateTypingFromMode`),
  `[NeoVisual] text-motion key=... caret=...` / `[NeoVisual] textinput-enter-input start|end|after caret=...`
  (text-input window motions + Solution Explorer search-box motions via the shared `TextMotionHelper`),
  `[NeoVisual] block-caret active=True|False` (editor-view block caret),
  `[NeoVisual] solution-explorer search-focus` (i focused the search box),
  `[Telescope] opened file: ...`, `[Telescope] overlay closed`, `[Telescope] preview file=...`,
  `[Telescope] preview tokens=...` (syntax-highlighted segment count),
  `[Telescope] opened issue: ... line=...` / `[Telescope] goto line=...` (code-issues finder),
  `[Telescope] references gathered reads=... writes=...` / `[Telescope] opened reference: file=... line=... col=... access=read|write`
  (references finder — read/write access from Roslyn FindReferences),
  `[Telescope] grep hits=...` / `[Telescope] opened grep: file=... line=...`
  (grep finder — query-driven, per-query gather summary),
  `[Telescope] implementations gathered count=...` / `[Telescope] opened implementation: file=... line=...`
  (implementation finder — Roslyn FindImplementationsAsync, deterministic type-before-member order),
  `[Telescope] focus target=List|Preview`, `[Telescope] result-mapper unknown display: {display}`
  (unknown-match warning when a display string has no payload), `[Telescope] preview caret=... line=...`,
  `[Telescope] prompt-motion key=... caret=...` (normal-mode prompt h/l/w/b/e/0/$ motions),
  `[NeoVisual] stale-toolwindow sentinel active` (M3 — the injected stale-frame fault is
  active, not skipped), `[NeoVisual] leader-binding failed: {seq}: {msg}` /
  `[NeoVisual] shortcut-binding failed: {simple}: {msg}` (M15 — binding action exceptions
  are caught and logged, never escaping the hook path), `[NeoVisual] output pane unavailable: {reason}`
  (M18 — one-time fallback when the VS Output pane cannot be created), `[MyExtension] init <step> ok/failed: {msg}`
  (M33 — per-step package-init orchestration; `[MyExtension] init failed: {ex}` is the last-resort net),
  `[Telescope] fzf filter failed: {msg}` / `[Telescope] fzf filter failed: timeout after {ms}ms` (M6),
  `[Telescope] fzf unavailable — showing unfiltered list` (M6 — once at overlay open when fzf is missing),
  `[Telescope] filter failed: {msg}` (M8 — `FilterAndUpdateAsync` fault path),
  `[NeoVisual] window rect unavailable; using empty rect` (n19 — logged once per adapter when the
  window rect cannot be read), `[NeoVisual] IVsUIShell unavailable: package is not an IServiceProvider.` /
  `[NeoVisual] IVsUIShell unavailable: SVsUIShell service returned null.` (m14 — null-guard fallbacks),
  `[Hook] SetHook MainModule failed: {ex.Message}` (n18 — `SetHook` guards
  `Process.GetCurrentProcess().MainModule` and falls back to `IntPtr.Zero` for `hMod`). `[Hook]` lines
  are single-stamped (m6 — `LogFileWriter.FormatLine` is the only stamper).

## Feature status / roadmap (work in progress)

Done and tested (live + unit):
- Leader-key binding system; user-configurable `keybindings.json`.
- Cardinal window navigation (Ctrl+H/J/K/L).
- Telescope overlay: open, search, navigate, insert/normal mode, open-file, wrap, preview pane.
- Solution Explorer controller: `o`/`Enter` open, `r` rename, `m` move, `a` add, `h`/`l` collapse/expand folds, j/k navigate, i/Esc input mode, `g` programmatically selects the first source file (`solution-explorer select file=...`, DTE `UIHierarchyItem.Select` — escapes the injected-key csproj-open trap; the tree is expanded first, and the file is opened + the selection re-asserted for ~1.5s to defeat VS's hover-preview focus steal).
- `Space+E` toggles Solution Explorer open/close (action `toggle-solution-explorer`).
- Telescope preview pane: `TextMotionNavigator` (shared pure vim motions h/l/j/k/w/b/e/0/$/gg/G
  + a/A/I insert placements), Ctrl+H/L focus switch between List/Preview, read-only (no insert),
  and **syntax highlighting** (`SyntaxHighlighter` tokenizer → colored runs in a RichTextBox) —
  `telescope-preview` live test passes and asserts `preview tokens=...`.
- Telescope prompt vim motions: in NORMAL mode the prompt box supports h/l/w/b/e/0/$ caret motions
  (`TryPromptMotion`, logged `prompt-motion key=... caret=...`) and draws a **white block caret**
  (`ApplyPromptCaretStyle`), line caret in insert — matching the tool-window surfaces.
- Editor insert-mode swallowing regression guard: `neovisual-editor-insert` live test types
  h/i/j/k + Space in VsVim INSERT mode and proves every char lands in the saved file (the
  `IsInteresting` pre-filter, leader key, and hjkl/I routing all pass through while
  `VimModeTracker.IsInTypingMode`). `vim-mode=...` diagnostic logs every mode switch.
- Vim text motions in text-input tool windows: `TextInputToolWindowController` (registered for
  `IsTextInputType` windows in `WindowManager.GetController`) gives Command Window / FindReplace /
  Immediate Window ... normal-mode h/l/w/b/e caret motions over the focused text box (WPF TextBox,
  VS editor `IWpfTextView`, or WinForms TextBoxBase), `A`/`I` insert at end/start, `a` after the
  caret, generic `i` at the caret; **block caret in normal mode** (white `CaretBrush` for WPF TextBox,
  white `BlockCaretAdornment` with the caret's character in black for editor views), line caret in
  insert. Shift is read via `GetAsyncKeyState` (not `Keyboard.Modifiers`, which lags injected keys),
  and InputHandler routes `I` through `TryMove` first and no longer gates shift in the tool-window
  branch. The caret is now a **solid white block** (vim-style, not a translucent selection-looking
  rect), and the adornment removes only its own layer tag — it must never `RemoveAllAdornments()`
  (that deletes the editor's native caret too, leaving NO caret in insert mode).
  `Space+C W` (new default binding) opens the Command Window. — `neovisual-textinput-motions` live
  test passes.
- Solution Explorer search box: `i` (normal mode) **focuses the search box** via the native
  `Window.SolutionExplorerSearch` command and enters input mode (so `i` types a query, Escape
  returns focus to the tree). While a WPF TextBox (the search box) is focused, the controller acts
  like a text-input window: h/l/w/b/e/a/A/I move the caret (shared `TextMotionHelper`) and all
  other keys fall through into the box — no tree actions/arrow injection. Exiting input mode
  refocuses the tree (`View.SolutionExplorer`). — `neovisual-explorer-*` live tests pass.
- Code-issues finder: `CodeIssuesFinder` (Telescope, `Name="Issues"`, `Space+F D`) lists the VS
  Error List warnings/errors plus TODO/FIXME/HACK/XXX markers scanned from the solution's project
  files (`ProjectFiles` shared enumeration). Each row shows kind + line + message; the preview
  jumps to the issue's line (`TextMotionNavigator.MoveToLine`); Enter opens the file at the line
  (`TextSelection.GotoLine`). The fzf input is written as explicit UTF-8 bytes (the default ANSI
  StreamWriter mangles non-ASCII display text and breaks the display-keyed payload lookup).
  — `telescope-issues` live test passes.
- References finder: `ReferencesFinder` (Telescope, `Name="References"`, `Space+F R`)
  lists every reference to the symbol at the caret in the active document, gathered
  from Roslyn `SymbolFinder.FindReferencesAsync` (MEF-resolved
  `VisualStudioWorkspace`; the caret symbol resolved via the active editor view,
  DTE `TextSelection` fallback). Each row shows file:line:col + **read/write
  access** (`ReferenceLocation.IsWrittenTo` via reflection — internal in Roslyn
  4.14 — the repo's established interop pattern); the preview jumps to the
  reference line; Enter opens the file at the line. Diagnostics:
  `references gathered reads=... writes=...` (gather summary) and
  `opened reference: file=... line=... col=... access=read|write`. All Roslyn
  async calls run inside `ThreadHelper.JoinableTaskFactory.Run` — never
  `.Result`/`.GetAwaiter().GetResult()` on the UI thread.
  — `telescope-references` live test passes.
- Grep finder: `GrepFinder` (Telescope, `Name="Grep"`, `Space+F G`) searches the
  solution's project files for the typed query — **query-driven** through the
  `IsQueryDriven` seam in the overlay (per-keystroke re-gather with a ~200ms
  debounce; the fzf filter path is skipped for query finders but untouched for
  Files/Issues/References). Empty query → no candidates; case-insensitive
  substring scan over `ProjectFiles.Enumerate(dte)` (shared walker), capped at
  200 hits; the preview jumps to the hit line; Enter opens the file at the line.
  Diagnostics: `grep hits=...` (per-query summary) and
  `opened grep: file=... line=...`. — `telescope-grep` live test passes.
- Implementation finder: `ImplementationFinder` (Telescope, `Name="Implementation"`,
  `Space+F I`) lists the implementations/overrides of the symbol at the caret,
  gathered from Roslyn `SymbolFinder.FindImplementationsAsync` (MEF-resolved
  `VisualStudioWorkspace`; the caret symbol resolved via the active editor view).
  Each hit maps the implementation symbol's first in-source declaring location
  (metadata symbols skipped); results are ordered deterministically
  `OrderBy(FilePath).ThenBy(LineNumber)` so a type implementation sorts before
  its member implementations; the preview jumps to the implementation line;
  Enter opens the file at the line. Diagnostics:
  `implementations gathered count=...` (gather summary) and
  `opened implementation: file=... line=...`. All Roslyn async calls run inside
  `ThreadHelper.JoinableTaskFactory.Run`. — `telescope-implementation` live test passes.

Pending (user-requested, NOT yet implemented):
- **Telescope finder**: fzf — with preview pane. (Scope DECIDED 2026-09-28:
  fuzzy content finder + fuzzy file finder; status PLANNED, not executed. The
  overlay already uses fzf internally as its filter engine.)

## Hard requirements that are easy to violate

- **UI-thread affinity is mandatory.** Nearly every `IVs*` / `DTE` / `EnvDTE` call
  must run on the main thread. Almost every method begins with
  `ThreadHelper.ThrowIfNotOnUIThread()`. The keyboard hook marshals to the UI
  thread via `ThreadHelper.JoinableTaskFactory.Run(...)` +
  `SwitchToMainThreadAsync()`. Never touch VS objects from a background thread;
  add `ThrowIfNotOnUIThread()` to any new VS API method.
- **Target framework is `net472`** (not modern .NET). Avoid .NET 5+/BCL-only APIs;
  `IReadOnlySet<T>` is NOT available — use `IReadOnlyCollection<Keys>` for controller
  action keys. `LangVersion` 14, `Nullable` enabled.
- `Microsoft.VisualStudio.SDK` is referenced with `ExcludeAssets="runtime"` — VS
  supplies it at load time; do not expect SDK assemblies in the build output.
  The test projects add `Microsoft.VisualStudio.Interop` + `Shell.Framework` with
  runtime assets so they can resolve DTE/VS types.

## Key architecture / gotchas

- Data flow: `GlobalKeyboardHook` (Win32 `WH_KEYBOARD_LL`) → `InputHandler.HandleKey`
  → `_bindings` lookup → `WindowNavigator.NavigateInDirection`.
- **Leader key is Space by default**, and bindings are **user-configurable** via an
  external file at `%APPDATA%\MyExtension\keybindings.json`. It is **not created
  automatically** — it's only read if it exists, and merged over the built-in
  defaults in `MyExtension/Resources/default-keybindings.json` (embedded resource). Action
  names are resolved in `InputHandler.ResolveAction` (`navigate-left` etc.);
  `command:<VsCommandName>` runs any VS command by name (this is how the
  LazyVim-style leader bindings like `w`→save are wired). To add a *new built-in
  action*, add a case there and a line in `default-keybindings.json`.
- **`toggle-solution-explorer`** is a built-in action (`InputHandler.ToggleSolutionExplorer`)
  that opens/focuses Solution Explorer when hidden and closes it when visible, via
  `dte.Windows.Item(vsWindowKindSolutionExplorer)`. `Space+E` is bound to it.
- **Tool-window controllers**: `WindowManager.GetController(type)` returns a registered
  controller or a per-type `GeneralToolWindowController`. `SolutionExplorerController` is
  registered in `MyExtensionPackage` for `ToolWindowType.SolutionExplorer` and adds action keys
  (`o`/Enter open, `r` rename, `m` move, `a` add, `g` select-first-source-file) plus
  `h`/`l` fold expand/collapse. The
  controller interface exposes `ActionKeys` (`IReadOnlyCollection<Keys>`, net472 has no
  `IReadOnlySet<T>`): `InputHandler` routes hjkl + `controller.ActionKeys` to `TryMove`, and the
  hook's `IsInteresting` pre-filter returns true for any key while a tool window with action keys
  is in normal mode (`InputHandler.IsKeyOfInterest` — hjkl + `controller.ActionKeys` while
  `ShouldRouteToolWindowKey()` is true).
- **Tool-window action keys never leak into a focused editor (the `FocusGuard`).** `IsToolWindow`
  comes from VS's `SEID_WindowFrame` selection event and goes STALE when the user moves to an editor,
  so routing/addressing it raw made `o`/`r`/`m`/`a` fire tree actions (and consume the key) while
  the editor held focus. `VimModeTracker.IsEditorFocused` (event-driven `Got/LostAggregateFocus`)
  is fed into the pure `MyExtension/ToolWindows/Utils/FocusGuard.cs`
  (`ShouldRouteToolWindowKey`/`IsTyping`) via `InputHandler`. The boolean
  passed is `EditorFocusedVeto` = `IsEditorFocused && CurrentController?.IsInputMode != true &&
  !GeneralToolWindowController.IsTextInputType(Type)` — a genuine text-input tool window (Command
  Window) or an input-mode controller OWNS the keyboard and is never vetoed (the raw flag is stale
  for those surfaces). `WindowManager` also has a test-only `stale-toolwindow` sentinel so the
  fault can be injected deterministically in e2e.
- Handled keys are *blocked* from VS by returning `(IntPtr)1` from the hook callback.
- VsVim 2022 mode-awareness: `VimModeTracker` (a shared MEF part) tracks the focused
  editor's mode **event-driven** — no per-keystroke polling. It is an
  `IWpfTextViewCreationListener` (`[ContentType("text")]`), so VS calls `TextViewCreated`
  for every code view; it subscribes to each view's `Got/LostAggregateFocus`/`Closed` and
  to the focused buffer's `IVimBuffer.SwitchedMode` event, updating a cached
  `IsInTypingMode` boolean (`InputHandler.IsTyping()` reads it; Insert=2/Replace=7 gate
  the leader key). `InputHandler` resolves the same singleton from the MEF container
  (`IComponentModel.DefaultExportProvider.GetExportedValue<VimModeTracker>()`).
  Interop: resolve `Vim.IVim` via MEF contract `"Vim.IVim"` + reflection (assembly
  `Vim.Core.dll`; no compile-time dependency and no committed third-party binaries). Get
  the buffer with the **non-creating** `IVim.TryGetVimBuffer(view, out buffer)`; read mode
  via `get_ModeKind()`/`IMode.get_ModeKind()`; subscribe via `add_SwitchedMode`.
  Because VsVim implements its interfaces **explicitly**, reflection must resolve members
  via `Type.GetInterfaceMap` (plain `GetMethod(name)` returns null), and event delegates
  must be built from the interface's `EventHandlerType` (an `EventHandler<object>` won't
  bind to the add method). Do **not** rely on `IVim.FocusedBuffer` (not tracked reliably),
  `GetActiveView2(fMustHaveFocus:1)` (true even when a tool window has focus), or UI
  Automation (`AutomationElement.FocusedElement`, which throws "application is in a broken
  state").
- Editor list-navigation: `PopupNavigation` maps `Ctrl+N`/`Ctrl+P` to injected
  Down/Up arrow keys (which the completion/quick-action/peek lists consume — not
  `Edit.LineDown`, which moves the caret), gated on `_windowManager.IsToolWindow()`. To
  stop VS dimming the completion list while Ctrl is held, `InputHandler` swallows the
  Ctrl key-down itself when `IsCompletionActive()` (via MEF `ICompletionBroker`).
- Tool-window `hjkl` navigation: `GeneralToolWindowController` translates `j/k/h/l`→arrow
  keys (via `KeyInjection`/`keybd_event`). Focus is classified in-process with WPF
  `Keyboard.FocusedElement` (inject unless focus is a `TextBoxBase` text input or a
  VsVim editor). Never mid leader-sequence.
- **The hook runs on the UI thread** (`GlobalKeyboardHook`), NOT a dedicated thread.
  A cheap pre-filter (`IsInteresting`) skips the handler for plain typing keys;
  only interesting keys (modifier chords, leader/Escape, hjkl, Ctrl keys, or any key
  while `InputHandler.IsLeaderActive` is true) reach `HandleKey`, which runs inline
  on the UI thread. Do **not** move the hook off the UI thread: a dedicated-thread
  variant caused the injected arrow keys to interleave badly with the physical key
  stream (Solution Explorer type-ahead double-fired), and a marshal at default
  Dispatcher priority got starved by input (keys drained seconds behind typing).
  `IsLeaderActive` is a volatile bool read by the pre-filter, mutated only on the
  UI thread. `Log` uses `OutputStringThreadSafe` (pane created eagerly on the UI
  thread); never add per-key logging back to the callback.
- Namespaces: `MyExtension` (package/hook/handler) and `MyExtension.Navigation`
  (window logic). The window-logic sources live in `MyExtension/Navigation/`
  (renamed from the old `CardinalMovment/` folder by the 2026-09-30 restructure;
  the namespace is `MyExtension.Navigation`, not `CardinalNavigation`).
- Two window APIs are used together: `IVsWindowFrame`/`IVsUIShell` for on-screen
  geometry (`GetWindowScreenRect`), `EnvDTE.Window` for activation
  (`window.Activate()`) and framing (`LinkedWindowFrame`). `WindowFrameAdapter`
  pairs them; don't assume the DTE object identity matches the IVs frame.
- Navigation tolerance divides are DPI-scaled; tune the logical constants in
  `NavigationConstants`, not raw pixel values.
- VSIX install target: Visual Studio Community 17.14+ (amd64).
