# MyExtension — Architecture Consolidation Findings (input for the planning hub)

- **Date:** 2026-09-28
- **Scope:** whole repo, architecture-only dimension — **cleaner architecture, ease of use, deduplication, simplification, what can be combined**. This is the second, deeper pass (the first full code review is in `docs/reviews/code-review.md`; the filed backlog is in `docs/progress.md` under "Code review backlog").
- **Method:** 4 parallel `arch-auditor` deep-dives (slices A core / B CardinalMovment / C ToolWindows / D Telescope) + hub cross-cutting analysis. Every structural claim verified with Trailmark (`language="c_sharp"`, proxy-aware `callers_of`). No file under `MyExtension/`, `Telescope/`, `tests/`, `tools/` was modified.
- **How to use this document:** each finding is written so a planning hub can turn it into a precise implementation task — it carries exact `file:line`, a **concrete target design** (the merged API), the **blast radius** (what changes), the **coverage** (which e2e/unit tests gate it), and an **effort/risk** rating. The recommended lane order is in §7.

## 1. Executive summary — the target architecture

The repo already has the right *seams* (pure state machines `OverlayKeyHandler`/`TextMotionNavigator`, the `IToolWindowController` contract, the `IFinder` seam, the diagnostics-as-contract log). The problem is that **every seam is under-factored**: each "add a feature" task today costs 2–6 edits across 2–4 files, and the same logic is copy-pasted 2–6×. The target is to make each seam own its whole concern:

- **One `NativeMethods`** for all Win32 P/Invoke (12 declarations in 4 files today).
- **One `TelescopeLauncher`** for opening finders (7 near-identical methods today).
- **One `ActionRegistry`** so a keybinding action = one dictionary entry, not a switch case + JSON line.
- **One `VsServices`** for MEF/DTE resolution (5 `SComponentModel` blocks + 12 `GetDTE` sites today).
- **One `WindowAdapter`** replacing `IVsFrameView` + `WindowControlAdapter` + `IVsUIWindowFrameExtractor` (~420 lines → 1 type).
- **One pure `WindowNavigationEngine`** (a `Direction` enum + a single-pass filter) replacing the 5× 4-way switches and the 6-step COM-bound pipeline.
- **One `TextMotionEngine`** (TextMotionHelper owns the `TextMotion` enum + `MapMotion` + the WPF/editor/WinForms surface dispatch) so a new vim motion = 2 edits, not 4–5.
- **One `FinderBase<THit>` + `FileLocation` + `PreviewRenderer` + `DteFileOpener`** so a new finder = ~20 lines, not ~100 + a bespoke test seam.
- **One log path** (`NeoVisualLog` + a `TelescopeLog` prefix helper) so a new diagnostic = one line with the prefix guaranteed.

**Ease-of-use headline (developer):** today adding a finder ≈ 100 lines + a bespoke test seam; adding a vim motion ≈ 4–5 sites; adding an action key ≈ 2 edits; adding a controller ≈ 1 class + 3 `IsTextInputType` sites; adding a diagnostic ≈ remembering a prefix literal. After the merges below: finder ≈ 20 lines, motion ≈ 2 sites, action key ≈ 1 edit, controller ≈ 1 class + 1 registration, diagnostic ≈ 1 line.

**Ease-of-use headline (end user):** `keybindings.json` is never auto-created, has no open/validate command, and a typo'd action name is silently ignored (only a `Debug.WriteLine`). This is the one user-facing gap worth fixing alongside the code merges.

## 2. Master merge list

| id | Merge | Copies → target | Blast radius | Coverage | Effort/Risk |
|----|-------|-----------------|--------------|----------|-------------|
| C1 | **NativeMethods** | 12 P/Invoke decls in 4 files → 1 `internal static class NativeMethods` | GlobalKeyboardHook, KeyInjection, InputHandler, MyExtensionPackage, TextMotionHelper, TextInputToolWindowController | hook install + navigation + telescope-open e2e | S / L |
| C2 | **TelescopeLauncher** | 7 open-finder methods (`InputHandler.cs:465-565` + `MyExtensionPackage.cs:410-424`) → `class TelescopeLauncher { void Open(string finderName) }` | InputHandler.ResolveAction + package command | all telescope-* e2e | S / L |
| C3 | **ActionRegistry** | `ResolveAction` cx14 switch (`InputHandler.cs:180-194`) + `default-keybindings.json` names → `static IReadOnlyDictionary<string,Action>` | InputHandler only | neovisual-leader e2e + KeybindingConfig units | S / L |
| C4 | **VsServices** | 5 `SComponentModel` blocks + 12 `GetDTE` sites → `static class VsServices` (cached) | InputHandler, VimModeTracker, MyExtensionPackage | references/implementation e2e | S / L |
| C5 | **RoslynCaretContext** | `GatherReferences`/`GatherImplementations` ~50-line prologue (`MyExtensionPackage.cs:488-536,570-612`) → `bool TryGetCaretSymbol(out ws, out doc, out symbol)` | MyExtensionPackage | telescope-references/implementation e2e | M / M |
| C6 | **OpenFileAtLine** | `OpenReference`/`OpenImplementation` byte-identical (`MyExtensionPackage.cs:445-458,466-479`) → `void OpenFileAtLine(path, line)` | MyExtensionPackage | telescope-references/implementation e2e | S / L |
| C7 | **ControllerRegistry** | split dispatch (`WindowManager.cs:68-72,89-98` + `MyExtensionPackage.cs:85`) → explicit registration, `GetController` = `TryGetValue` | WindowManager + package init | neovisual-toolwindow/explorer-* e2e | M / L |
| C8 | **HookInterest** | `IsInteresting` hardcodes H/J/K/L/I/Space/Escape/Ctrl (`GlobalKeyboardHook.cs:174-189`) → `InputHandler.IsKeyOfInterest(key)` | hook + InputHandler | neovisual-toolwindow/textinput e2e | M / M |
| N1 | **WindowAdapter** | `IVsFrameView` (13 zero-caller forwards + dead `internalFrame`/`IsTabbedAndInvisible`) + `WindowControlAdapter` + `IVsUIWindowFrameExtractor` → 1 `sealed class WindowAdapter` (~420 lines → 1) | WindowMatrix ctor only | neovisual-window-nav + neovisual-toolwindow e2e | L / M |
| N2 | **RectCoordinate** | ~12 inline `x+width`/`y+height` (`WindowMatrix.cs:213-330`) + `IsEmpty` + `AdjacencySize` → `readonly struct RectCoordinate { Right, Bottom, IsEmpty, Adjacency, GapTo }` | WindowMatrix, WindowControlAdapter, `Run_RectCoordinate_StoresFields` test | neovisual-window-nav e2e + new unit tests | M / L |
| N3 | **NavigationEngine** | 5× 4-way direction switches (`WindowMatrix.cs:155-409`) + hardcoded 6-step pipeline → `enum Direction` + pure `WindowNavigationEngine.SelectTarget(active, candidates, direction, settings)` (one O(n) pass) | WindowMatrix only | NEW unit tests + neovisual-window-nav e2e | L / M |
| N4 | **NavigationSettings** | `CardinalNavigationConstants.cs:10-19` (4 of 9 constants dead) + `SetWindowDivideSelectionSizes` → `sealed NavigationSettings { XDivide, YDivide; FromSystemDpi() }` | WindowMatrix only | neovisual-window-nav e2e | S / L |
| T1 | **TextMotionEngine** | `TextInputToolWindowController.cs:114-221` re-implements `TextMotionHelper.cs:25-171`; helper has an **inverted dependency** (calls `TextInputToolWindowController.MapMotion`) → TextMotionHelper owns `TextMotion` enum + `MapMotion` + `TryMoveFocusedSurface` + `ApplyEditorViewCaret`; controller delegates | TextMotionHelper, TextInputToolWindowController, SolutionExplorerController | neovisual-textinput-motions + explorer-open-searchbox e2e; NeoVisual units | L / M |
| T2 | **ActionTable** | `ActionKeys` list + `TryMove` switch are 2 edits per key (`SolutionExplorerController.cs:168-180,196-242`; `TextInputToolWindowController.cs:86-87,114-172`) → `Dictionary<Keys,Func<bool>> _actions`; `ActionKeys => _actions.Keys` | InputHandler (ActionKeys/TryMove consumers) | all neovisual-explorer-* + textinput e2e | M / L |
| T3 | **ResolvePipeline** | expand→BuildForest→resolve→Select duplicated (`SolutionExplorerController.cs:284-318` vs `:93-112`) → `ResolveTreeItem(DTE2, Func<HierarchyNode,string?> pick)` | SolutionExplorerController only | explorer-open-navigation + explorer-open-searchbox e2e | M / L |
| T4 | **ControllerBase** | `_isInputMode` + Enter/ExitInputMode boilerplate triplicated (`GeneralToolWindowController.cs:26-43`, `TextInputToolWindowController.cs:59-83`, `SolutionExplorerController.cs:26-46`) → `abstract ToolWindowControllerBase` with virtual `OnModeChanged()` | 3 controllers; WindowManager unchanged | existing e2e suite | M / L |
| T5 | **GuardOwnsVeto** | veto composed in InputHandler (`InputHandler.cs:82-85`), passed as pre-baked bool → `FocusGuard.ShouldRouteToolWindowKey(isToolWindow, editorFocused, isInputMode, isTextInputSurface)` | FocusGuard + InputHandler | FocusGuard truth-table units + neovisual-explorer-move-editor-focus e2e | S / L |
| L1 | **PreviewRenderer** | 5 near-identical preview branches (`TelescopeOverlay.cs:442-539`) → `IFileLocation { FilePath, LineNumber }` + `PreviewRenderer.Show(box, navigator, loc)` | TelescopeOverlay only | telescope-preview/issues/grep/references/implementation e2e | M / M |
| L2 | **FinderBase\<THit\>** | 5 finder skeletons (`FileFinder.cs:18-191`, `CodeIssuesFinder.cs:25-231`, `GrepFinder.cs:20-182`, `ReferencesFinder.cs:24-99`, `ImplementationFinder.cs:25-97`) → `abstract FinderBase<THit> : IFinder` (owns GetCandidates/OnSelected/AssertUiThread/error-handling/test-seam) | MyExtensionPackage registration + finder tests | telescope-open-file/issues/grep/references/implementation e2e + units | L / M |
| L3 | **FileLocation** | 4 hit models hand-roll FilePath+LineNumber (`CodeIssue.cs:24-38`, `GrepHit.cs:8-20`, `ReferenceHit.cs:10-28`, `ImplementationHit.cs:10-24`) → `abstract FileLocation : IFileLocation` | finder ctors + host gatherers (signatures unchanged) | telescope-issues/grep/references/implementation e2e | S / L |
| L4 | **DteFileOpener** | `GotoLine` byte-identical in 2 finders + 2 host openers (`CodeIssuesFinder.cs:122-137`, `GrepFinder.cs:136-151`, `MyExtensionPackage.cs:445-479`) → `internal static class DteFileOpener.OpenAtLine(dte, path, line)` | CodeIssuesFinder, GrepFinder, MyExtensionPackage | telescope-issues/grep/references/implementation e2e | S / L |
| L5 | **TelescopeLog** | prefix literal repeated ~31× across 8 files (`NeoVisualLog.cs:90-99` + 31 callers) → `internal static class TelescopeLog { Log(msg) => NeoVisualLog.Log(DiagnosticLog.Telescope + msg) }` | all Telescope log call sites | e2e (log lines are the contract) | S / L |
| L6 | **IFinder query fold** | `IQueryFinder` forces a meaningless no-arg `GetCandidates()` stub + an `is IQueryFinder` overlay branch (`IQueryFinder.cs:11-14`, `GrepFinder.cs:47-50`, `TelescopeOverlay.cs:341`) → `GetCandidates(string query = "")`; delete `IQueryFinder` | GrepFinder + overlay | telescope-grep e2e | S / L |
| L7 | **CaretPlacement** | magic ints 0/1/2 in `OverlayKeyHandler.cs:176-189` + `TelescopeOverlay.cs:891-911` → shared `CaretPlacement` enum (delegate to `TextMotionNavigator` insert methods) | OverlayKeyHandler + overlay | telescope-prompt-motions e2e + units | S / L |
| X1 | **LogPath** | `NeoVisualLog`/`LogFileWriter`/`NeoVisualTraceListener` + ~27 `Debug.WriteLine` bypasses → one path (`NeoVisualLog.Debug(msg)` = Debug + Log; buffer `LogFileWriter` writes) | whole repo | e2e (log lines are the contract) | M / M |
| X2 | **HarnessHelpers** | `test-e2e.ps1` / `iterate-telescope.ps1` / `dte-command.ps1` triplicate helpers → `tools/harness/harness-common.ps1` | tools/ | e2e suite | M / M |
| X3 | **TestRunner** | reflection runner + `Assert` byte-identical in both test `Program.cs` → one shared source file | both test projects | unit suites | S / L |
| X4 | **ProjectLayering** | host (`MyExtension`) depends on `Telescope` for core infra (`NeoVisualLog`, `TelescopeController`); Telescope is itself VS-coupled (WPF overlay + VS SDK) — the "pure library" framing overstates the seam | project structure | build + unit suites | L / M (decision, not a code change) |

## 3. Detailed findings by area

### 3.1 Core — `MyExtension/` (slice A)

**C1 NativeMethods (major).** 12 Win32 P/Invoke declarations are scattered across `GlobalKeyboardHook.cs:257-277`, `KeyInjection.cs:50-51`, `InputHandler.cs:606-617`, `MyExtensionPackage.cs:764-775`, `TextMotionHelper.cs:169`, `TextInputToolWindowController.cs:339` — and `GetWindowRect` + `NativeRect` are duplicated verbatim in two files. A 5th consumer re-declares the same `DllImport`s; a marshalling/lifetime fix must be applied in N places. **Target:** one `internal static class NativeMethods` with `GetWindowRect(IntPtr)→Rectangle?`, `keybd_event`, `SetWindowsHookEx`, `GetAsyncKeyState`, `GetForegroundWindow`, `GetWindowThreadProcessId`, `GetModuleHandle`, `CallNextHookEx`, `UnhookWindowsHookEx`. Mechanical rename of ~12 call sites.

**C2 TelescopeLauncher (major).** Seven near-identical "open finder" methods — `InputHandler.cs:465,486,508,530,551` + `MyExtensionPackage.cs:410-424` — differ only in the finder-name string and log text; `ResolveAction`'s 5 telescope cases (`:186-190`) are a parallel table. Adding a finder = 4–5 edits across 3 files, and each copy re-resolves DTE + `GetWindowRect`. **Target:** `class TelescopeLauncher { void Open(string finderName) }` = GetDTE + GetWindowRect + `_telescope.Open(name, rect, hwnd)`; `ResolveAction` maps `"telescope-issues" → () => launcher.Open("Issues")` via a `Dictionary<string,string>`; the package command → `launcher.Open("Files")`. 7 methods deleted.

**C3 ActionRegistry (major).** `ResolveAction` (`InputHandler.cs:180-194`, cx14) is a hardcoded switch whose action names must stay in sync with `default-keybindings.json` by hand — a typo'd JSON action is silently ignored (only `Debug.WriteLine`). **Target:** `static class Actions { static readonly IReadOnlyDictionary<string,Action> Registry = new(){ ["navigate-left"]=()=>Navigate(LEFT), ["telescope"]=()=>launcher.Open("Files"), ... }; }`; `ResolveAction = Registry.TryGetValue(lower, out a) ? a : ParseCommand(lower)`. Adding an action = 1 dictionary entry.

**C4 VsServices (major).** `SComponentModel` is resolved 5 different ways (`GetService` on package, `Package.GetGlobalService`, `GetService` on `this`) and `GetDTE` (`UtilityMethods.cs:17`, uncached) is called ~12×. **Target:** `static class VsServices { static IComponentModel? ComponentModel(IServiceProvider sp); static T? Mef<T>(IServiceProvider sp); static DTE Dte(IServiceProvider sp); }` with per-package caching. Removes 5 duplicate blocks.

**C5 RoslynCaretContext (major).** `GatherReferences` and `GatherImplementations` share an identical ~50-line prologue (GetDTE → active → `SComponentModel` → workspace → docId → document → `GetCaretOffset` → root → semanticModel → `FindSymbolAtPositionAsync`); they already drifted (references leaves a `GetSyntaxRootAsync` result unused). **Target:** `bool TryGetCaretSymbol(out VisualStudioWorkspace ws, out Document doc, out ISymbol symbol)`; both gatherers call it then diverge at `FindReferencesAsync`/`FindImplementationsAsync`.

**C6 OpenFileAtLine (major).** `OpenReference` (`:445-458`) and `OpenImplementation` (`:466-479`) are byte-identical (File.Exists + GetDTE + OpenFile + GotoLine). **Target:** `void OpenFileAtLine(string path, int line)`; both `OnSelected` wrappers call it. (Overlaps L4 `DteFileOpener` — do them together.)

**C7 ControllerRegistry (minor).** Controller dispatch is split: explicit `RegisterController` for SolutionExplorer, implicit lazy branches in `GetController` for text-input/general. Adding a controller means knowing whether to register or add a branch. **Target:** register ALL controllers explicitly in `InitializeAsync`; `GetController` becomes pure `_controllers.TryGetValue`.

**C8 HookInterest (major).** `IsInteresting` (`GlobalKeyboardHook.cs:174-189`) hardcodes H/J/K/L/I/Space/Escape/Ctrl — the default controller's key set duplicated in the hook. Adding a key to the default controller silently requires editing the hook too. **Target:** `InputHandler.IsKeyOfInterest(key)` = leader/Escape/Ctrl-swallow OR (tool-window normal mode AND key ∈ DefaultControllerKeys ∪ controller.ActionKeys); the hook switch shrinks to a delegation.

**Ease of use (end user) — keybindings UX (minor).** `KeybindingConfig.cs:75-107` loads once at construction (restart required to change), never auto-creates the file, has no runtime discoverability of action names, and invalid actions are silently `Debug.WriteLine`'d. **Fix:** add a `MyExtension:OpenKeybindings` command that creates+opens the file, and log unknown actions to the NeoVisual pane (not just Debug) so they surface.

**Simplification (minor/nit).** `HandleKey` is cx18 (`InputHandler.cs:236-375`) — extract `TryHandleToolWindowKey` + `TryHandleLeaderSequence` so it becomes a 5-line dispatcher (mirrors the `OverlayKeyHandler` seam). `ReadLine` (`MyExtensionPackage.cs:731-741`) does `File.ReadLines(path).Skip(line-1).FirstOrDefault()` — O(n) per preview line; hoist the enumeration. `_package` field (`GlobalKeyboardHook.cs:49`) assigned but never read. The `else` `JoinableTaskFactory` marshal (`:133-140`) is documented "normally never runs" — remove and assert `ThreadHelper.CheckAccess()`. `File.Exists(path)` evaluated twice in `KeybindingConfig.Load`. `BlockCaretAdornment.Update()` (`:130-133`) swallows all exceptions silently.

### 3.2 CardinalMovment — `MyExtension/Navigation/` (slice B)

**N1 WindowAdapter (major).** `IVsFrameView.cs:51-127` has 13 zero-caller forwards (Show/Hide/IsVisible/ShowNoActivate/CloseFrame/SetFramePos/GetFramePos/GetProperty/SetProperty/GetGuidProperty/SetGuidProperty/QueryViewInterface/IsOnScreen — Trailmark `callers_of` each = 0) plus dead `internalFrame` (`:22`) and dead `IsTabbedAndInvisible` (`:43`); `WindowControlAdapter` and `IVsUIWindowFrameExtractor` complete the trio. **Target:** one `sealed class WindowAdapter { IVsWindowFrame _frame; EnvDTE.Window _dte; RectCoordinate _rect; RectCoordinate Rect; EnvDTE.Window DteWindow; void Activate(); bool AutoHides(); bool IsOnScreen(); static List<WindowAdapter> Enumerate(AsyncPackage); static WindowAdapter? FindActive(...); static IEnumerable<WindowAdapter> LinkedTo(...); }` — 3 types (~420 lines) collapse to 1; `IVsWindowFrameNotify` (the `NotImplementedException` landmine) dies with it. Only `WindowMatrix` consumes these.

**N2 RectCoordinate (major).** ~12 inline `x+width`/`y+height` computations (`WindowMatrix.cs:213,216,227,230,255,262,269,277,306,314,322,330`) + an `IsEmpty` check (`:415-418`) + `AdjacencySize` (`:141-147`). **Target:** `readonly struct RectCoordinate { readonly int X,Y,Width,Height; int Right => X+Width; int Bottom => Y+Height; bool IsEmpty; int Adjacency(RectCoordinate, Axis); int GapTo(RectCoordinate, Direction); }` — every filter lambda becomes `win.Rect.Adjacency(active.Rect, axis)`. Deepens m25 (mutability). **Note:** `Run_RectCoordinate_StoresFields` (`tests/NeoVisual.Tests/Program.cs:489-496`) asserts the public fields — update it.

**N3 NavigationEngine (major).** 5× 4-way direction switches (`WindowMatrix.cs:155-193, 200-239, 245-283, 289-355, 361-409`) — adding a direction today = edit 5 switches; adding a filter = edit the hardcoded 6-step sequence (`:435-441`); unit-testing is impossible (needs AsyncPackage+DTE+IVsUIShell). **Target:** `enum Direction { Up, Down, Left, Right }` + `Axis()/Sign()` ext + `sealed pure WindowNavigationEngine { int? SelectTarget(RectCoordinate active, IReadOnlyList<RectCoordinate> candidates, Direction, NavigationSettings) }` running a `List<Func<RectCoordinate,Direction,bool>>` pipeline as ONE O(n) pass (no 5 list-mutations + sort; deepens M8). `WindowMatrix` becomes a thin COM shell. **This is the highest-value testability win in the repo** — it makes the core navigation algorithm unit-testable (the `OverlayKeyHandler` pattern).

**N4 NavigationSettings (minor).** `CardinalNavigationConstants.cs:10-19` — 4 of 9 constants are dead (`DefaultLogicalYWindowDivide`, `DOCUMENT`, `TOOL`, `GithubMessage`); `SetWindowDivideSelectionSizes` (`WindowMatrix.cs:101-115`) is the only DPI logic. **Target:** `sealed NavigationSettings { int XDivide, YDivide; static FromSystemDpi(); }` injected into the engine; delete the dead constants.

**Simplification (minor/nit).** `GetWindowControlAdapters` (`WindowControlAdapter.cs:143`) takes `dteWindows` but never uses it (callers build the list for nothing — wasted per-keystroke DTE enumeration). `RemoveWindowsNotAdjacent` (`WindowMatrix.cs:245`, 38 lines) is never called (Trailmark `callers_of`=0). Private `ActivateWindow(EnvDTE.Window)` (`:479`) never called. `m_IVsFrames` field (`:15`) is ctor-only — make it a local. `DistinctBy` (`LinqExtensionMethods.cs:19`) has zero production callers (only the test project exercises it) — use it in the engine or delete it + its test. The `filterFunction = (win) => false` default initializers (`:202,247,365`) are always overwritten — an invalid direction silently filters everything to empty; the `Direction` enum (N3) fixes this by compiler-enforced exhaustiveness.

**Perf (major, new this pass).** Every Ctrl+H/J/K/L rebuilds the whole matrix: `WindowMatrix` ctor enumerates ALL tool+document frames, wraps each in a `GetWindowScreenRect` COM call, pairs via `GetWindowObject`, then O(n²) linked-window `CompareWindows` pairing — all on the UI thread per keystroke. **Fix:** cache a `List<WindowAdapter>` in `WindowManager` and refresh rects lazily (active + candidates only) on window events; at minimum reuse the `IVsUIShell` enumeration across keystrokes.

### 3.3 ToolWindows — `MyExtension/ToolWindows/` (slice C)

**T1 TextMotionEngine (major).** `TextInputToolWindowController.cs:114-221` re-implements the entire `TextMotionHelper` pipeline (focus probe, motion dispatch, caret style, `MotionName`, log) instead of delegating — Trailmark confirms `TextMotionHelper.TryMoveFocusedTextBox` is called ONLY by `SolutionExplorerController`, so the "shared" helper serves one controller while the other carries a private copy. Worse, `TextMotionHelper.cs:62` has an **inverted dependency**: the shared helper calls `TextInputToolWindowController.MapMotion`. **Target:** TextMotionHelper owns `TextMotion` enum + `MapMotion(Keys,bool)` + `TryMoveFocusedSurface(Keys, ref bool)` (WPF box → editor view → WinForms → arrow fallback) + `ApplyEditorViewCaret(IWpfTextView,bool)`; `TextInputToolWindowController.TryMove => TextMotionHelper.TryMoveFocusedSurface(key, ref _isInputMode)`; Enter/ExitInputMode => `StyleFocusedSurface(bool)`. A new vim motion becomes 2 edits (MapMotion + TextMotionNavigator) instead of 4–5.

**T2 ActionTable (major).** `ActionKeys` list and the `TryMove` switch are two separate edits per action key; `InputHandler` gates on `controller.ActionKeys.Contains(key)` (`InputHandler.cs:310`), so a key added to one but not the other silently never routes. **Target:** `Dictionary<Keys,Func<bool>> _actions`; `ActionKeys => _actions.Keys`; `TryMove(key) => _actions.TryGetValue(key, out f) && f()`; hjkl also in the table. Adding an action key = 1 edit.

**T3 ResolvePipeline (major).** The expand-project → BuildForest → resolve → Select pipeline is duplicated between `SelectFirstSourceFile` (`SolutionExplorerController.cs:284-318`) and `ReturnFocusToTree` (`:93-112`) — only the pick function differs; the copies already diverge in null-handling. **Target:** `ResolveTreeItem(DTE2, Func<HierarchyNode,string?> pick) -> (UIHierarchyItem?, string?)` shared by both.

**T4 ControllerBase (minor).** `_isInputMode` + Enter/ExitInputMode boilerplate is triplicated (`GeneralToolWindowController.cs:26-43`, `TextInputToolWindowController.cs:59-83`, `SolutionExplorerController.cs:26-46`); mode logic drifts (TextInput styles on both enter+exit, SolutionExplorer only on enter). **Target:** `abstract ToolWindowControllerBase : IToolWindowController` with protected `_isInputMode`, virtual Enter/ExitInputMode + `protected virtual OnModeChanged()`; TextInput overrides OnModeChanged → caret style; SolutionExplorer overrides ExitInputMode → return-focus.

**T5 GuardOwnsVeto (minor).** The veto composition (`IsEditorFocused && !IsInputMode && !IsTextInputType`) is computed in `InputHandler.cs:82-85` and passed as a pre-baked bool, so `FocusGuard` cannot reason about the rule and its truth table is incomplete — the "text-input surface owns the keyboard" exception is invisible to the guard and untested there. **Target:** `FocusGuard.ShouldRouteToolWindowKey(isToolWindow, editorFocused, isInputMode, isTextInputSurface)`; `InputHandler.EditorFocusedVeto` collapses into the call.

**Simplification (minor/nit).** `IsTextInputType` is consulted in 3 layers (`WindowManager.cs:89`, `GeneralToolWindowController.cs:34` — dead, `InputHandler.cs:85`) — single `ToolWindowTypeClassifier.IsTextInputSurface` consumed by all 3. The catch-all + `Debug.WriteLine` swallow idiom is repeated 3× (`SolutionExplorerController.cs:156-162,368-371,476-479`) — one `Try(Action, string label)` helper logging via `NeoVisualLog`. `MotionName`/`FindFocusedTextBox`/`GetParent` duplicated verbatim (fixed by T1).

### 3.4 Telescope — `Telescope/` (slice D)

**L1 PreviewRenderer (major).** `LoadPreviewForSelection` (`TelescopeOverlay.cs:433-542`) is 5 near-identical branches (CodeIssue/ReferenceHit/ImplementationHit/GrepHit/string), each ~20 lines: File.Exists → ReadAllText → SetPreviewContent → MoveToLine → ApplyPreviewCaret → 2 logs. The 4 hit models already share FilePath+LineNumber, so the type-switch is pure boilerplate. **Target:** `IFileLocation { string FilePath; int LineNumber; }`; all 4 hit models + a new `FileHit` implement it; collapse to ONE branch; `PreviewRenderer.Show(RichTextBox, TextMotionNavigator, IFileLocation)` owns read/tokenize/caret/log. Any fix to the read/tokenize path (M14) becomes 1 place, not 5.

**L2 FinderBase\<THit\> (major).** The 5 finders each re-implement the same skeleton: Name, UI-thread assert (3 variants — the DTE trio use `ThrowIfNotOnUIThread`, References/Implementation use a `JoinableTaskContext`-guarded copy), try/catch gather → Debug.WriteLine + NeoVisualLog.Log, test-seam fields + test ctor (~10 lines ×3), ToEntry, OnSelected try/catch. ReferencesFinder and ImplementationFinder are near-identical. **Target:** `abstract class FinderBase<THit> : IFinder where THit : IFileLocation` with `protected abstract IReadOnlyList<THit> GatherHits(); protected abstract FinderEntry ToEntry(THit); protected abstract void OpenHit(THit);` — base owns GetCandidates/OnSelected/AssertUiThread/error-handling/test-seam. Subclasses keep their current public ctors so package registration is unchanged. Adding a finder ≈ 20 lines.

**L3 FileLocation (major).** `CodeIssue.cs:24-38`, `GrepHit.cs:8-20`, `ReferenceHit.cs:10-28`, `ImplementationHit.cs:10-24` each hand-roll the same FilePath+LineNumber pair with identical null-coalescing ctors. **Target:** `abstract class FileLocation : IFileLocation`; subclasses add their extra fields (Kind/Text, LineText, Column/IsWrite/Symbol/LineText, SymbolName/Kind). Signatures unchanged.

**L4 DteFileOpener (major).** `GotoLine` is byte-identical in `CodeIssuesFinder.cs:122-137` and `GrepFinder.cs:136-151`, AND References/Implementation delegate to host `_opener`s that re-implement the same open-file-at-line. **Target:** `internal static class DteFileOpener { OpenAtLine(DTE, path, line) }`; all four call it. (Do with C6.)

**L5 TelescopeLog (minor).** Every diagnostic hand-writes `NeoVisualLog.Log($"{Telescope.DiagnosticLog.Telescope}...")` — the prefix is repeated ~31× across 8 files (Trailmark: 31 callers). A missing/mistyped prefix is a silent contract break (e2e greps for `[Telescope]`). **Target:** `internal static class TelescopeLog { Log(msg) => NeoVisualLog.Log(DiagnosticLog.Telescope + msg) }` — one line per diagnostic, prefix guaranteed.

**L6 IFinder query fold (minor).** Query-driven finders implement BOTH `IFinder` and `IQueryFinder`, forcing a meaningless no-arg `GetCandidates()` stub (returns empty) and an `is IQueryFinder` branch in the overlay (`TelescopeOverlay.cs:341`). The empty no-arg result is a trap. **Target:** `IReadOnlyList<FinderEntry> GetCandidates(string query = "")`; delete `IQueryFinder` and the `is` check.

**L7 CaretPlacement (minor).** Insert-caret placement is implemented twice: the overlay's `ApplyInsertCaret` uses magic ints 0/1/2 (`TelescopeOverlay.cs:891-911`), and `TextMotionNavigator` already exposes InsertAfter/InsertEnd/InsertStart (used by tool windows). `OverlayKeyHandler.EnterInsertMode(int)` (`:176-189`) mirrors the magic ints. **Target:** shared `CaretPlacement` enum; collapse the 3 `ApplyAction` insert branches into one `EnterInsert(CaretPlacement)`.

**Simplification (minor/nit).** `TryPromptMotion` (`TelescopeOverlay.cs:653-674`) allocates `new TextMotionNavigator()` + SetText on every keystroke — cache a `_promptNavigator` field (mirror `_previewNavigator`); better, give `TextMotionNavigator` a single `Apply(MotionKey)` so prompt + preview share one dispatch. `TextMotionNavigator.LineNumber`/`ColumnNumber`/`MoveToLine` each rescan from the start (O(n) per call) — cache line-start offsets on SetText, binary-search on MoveToLine. `TelescopeController.Open()` (`:50-71`) returns true when already open without switching finders — a second `Open("Grep")` while "Files" shows silently no-ops; either switch the active finder or log `already open, ignoring finder=...`.

### 3.5 Cross-cutting (hub-conducted)

**X1 LogPath (major).** The log trio (`NeoVisualLog` facade / `LogFileWriter` pure sink / `NeoVisualTraceListener` Debug bridge) is well-factored, but ~27 `Debug.WriteLine` sites bypass it (only `GlobalKeyboardHook.Log` routes to the pane), and `LogFileWriter` does `File.AppendAllText` per call on the typing hot path. **Target:** `NeoVisualLog.Debug(msg)` = Debug.WriteLine + NeoVisualLog.Log; replace all 27 sites; buffer `LogFileWriter` writes (kept-open StreamWriter, flush on timer/close). Also: the hook uses two prefixes (`[Hook]` per-key vs `[GlobalKeyboard]` install/uninstall) — use one `[Hook]` prefix for all hook diagnostics.

**X2 HarnessHelpers (minor).** `test-e2e.ps1` / `iterate-telescope.ps1` / `dte-command.ps1` triplicate helpers (KbInject/Wait-LogContains/Bring-ToForeground/Write-*); `iterate-telescope.ps1` has a divergent overlay-close and a stale doc comment. **Target:** `tools/harness/harness-common.ps1`; align the close with `Close-Telescope`.

**X3 TestRunner (minor).** The reflection runner + `Assert` helper are byte-identical in both test `Program.cs`. **Target:** one shared `<Compile Include>` source file.

**X4 ProjectLayering (decision).** The host (`MyExtension`) depends on `Telescope` for core infrastructure (`NeoVisualLog` — the extension-wide logger — and `TelescopeController`), and `Telescope.csproj` is itself VS-coupled (VS SDK + WPF overlay). The "pure-logic library" framing overstates the seam; only `OverlayKeyHandler`/`TextMotionNavigator`/`FzfFilter`/`LogFileWriter` are actually VS-free. **DECISION (2026-09-28): option (a) — accept + document the seam explicitly** (F46's recommendation). The seam is accepted as-is: the host legitimately depends on `Telescope` for its logger and controller, and `Telescope` is a VS-coupled project with a few VS-free pure-logic classes. The roadmap must NOT grow more VS-coupled code inside the "library" — new VS-coupled code goes in `MyExtension`; new pure logic may go in `Telescope`. Option (b) — moving the log trio + `DiagnosticLog` into a truly shared location — is **deferred** (a structural change that ripples through project references; out of scope for a behavior-preserving consolidation).

## 4. Ease-of-use findings (developer ergonomics)

| "Add a …" | Today | After the merges | Fix |
|-----------|-------|------------------|-----|
| new finder | ~100 lines + bespoke test seam + 4-5 edits across 3 files | ~20 lines (subclass `FinderBase<THit>`) | L2, L3, L1, L4 |
| new vim motion | 4–5 sites (TextMotion enum, MapMotion, ApplyMotionToBox switch, TextMotionHelper switch, TextMotionNavigator) | 2 sites (MapMotion + TextMotionNavigator) | T1, L7 |
| new action key | 2 edits (ActionKeys + TryMove switch) — easy to miss | 1 edit (dictionary entry) | T2 |
| new tool-window controller | 1 class + registration + 3 `IsTextInputType` sites | 1 class + 1 registration | C7, T4, classifier |
| new diagnostic | remember the prefix literal (~31 hand-written sites) | 1 line via `TelescopeLog` | L5, X1 |
| new keybinding action | 2 edits (ResolveAction switch + JSON) + silent drift risk | 1 dictionary entry | C3 |
| new navigation direction | edit 5 switches + the 6-step pipeline | add an enum member (compiler-enforced) | N3 |
| new Solution Explorer action | ActionKeys + switch + method + log | 1 dictionary entry + 1 method | T2 + `LogAction` helper |

## 5. Ease-of-use findings (end user)

- **`keybindings.json` is not discoverable or validated.** It is never auto-created, there is no command to open it, and a typo'd action name is silently ignored (only a `Debug.WriteLine`). **Fix:** a `MyExtension:OpenKeybindings` command that creates+opens the file, and surface unknown actions in the NeoVisual pane. (arch2-A)
- **No way to see the active binding set.** Consider a `MyExtension:ShowBindings` command that dumps the merged binding table to the pane. (proposal)
- **`TelescopeController.Open()` silently no-ops on a second open with a different finder** — the user believes the requested finder is active. Either switch the active finder or log `already open, ignoring finder=...`. (arch2-D)

## 6. Simplification / dead code / over-engineering (delete list)

- `IVsFrameView` 13 zero-caller forwards + dead `internalFrame` + dead `IsTabbedAndInvisible` (N1).
- `WindowMatrix.RemoveWindowsNotAdjacent` (38 lines, never called) + private `ActivateWindow(EnvDTE.Window)` + ctor-only `m_IVsFrames` field + `CheckDte` redundant warm-up (N3/N1, first pass m20).
- `CardinalNavigationConstants`: `DefaultLogicalYWindowDivide`, `DOCUMENT`, `TOOL`, `GithubMessage` (N4).
- `LinqExtensionMethods.DistinctBy` — zero production callers (use it in the engine or delete it + its test).
- `GeneralToolWindowController` ctor `IsTextInputType` branch (dead — WindowManager only builds General for non-text-input types).
- `IQueryFinder` + the `is IQueryFinder` overlay branch (L6).
- `GlobalKeyboardHook._package` field (never read); the `else` `JoinableTaskFactory` marshal ("normally never runs").
- `IVsWindowFrameNotify` `NotImplementedException` members (dies with N1).
- `WindowManager` `if (guid != null)` on a `Guid` struct (always true) — check the HRESULT instead.
- `GetWindowControlAdapters` unused `dteWindows` parameter.

## 7. Recommended implementation order (lanes)

**Lane 1 — mechanical dedup, no behavior change (do first; each is independently verifiable):**
C1 NativeMethods → C6 OpenFileAtLine (+L4 DteFileOpener) → C2 TelescopeLauncher → C3 ActionRegistry → C4 VsServices → L5 TelescopeLog → L7 CaretPlacement → L6 IFinder fold → X3 TestRunner.

**Lane 2 — the vim-motion/caret cluster (highest-value dedup; one lane, e2e-gated):**
T1 TextMotionEngine → T2 ActionTable → T4 ControllerBase → T5 GuardOwnsVeto → T3 ResolvePipeline. Gate: `neovisual-textinput-motions`, `explorer-open-searchbox`, all `neovisual-explorer-*`.

**Lane 3 — the finder cluster (prerequisite for the fzf finder roadmap):**
L3 FileLocation → L2 FinderBase\<THit\> → L1 PreviewRenderer → C5 RoslynCaretContext → C7 ControllerRegistry. Gate: all `telescope-*` finder scenarios + unit suites.

**Lane 4 — the navigation cluster (highest testability win):**
N2 RectCoordinate → N4 NavigationSettings → N3 NavigationEngine (pure seam + new unit tests) → N1 WindowAdapter → the per-keystroke matrix-rebuild cache. Gate: `neovisual-window-nav` + new unit tests.

**Lane 5 — cross-cutting:**
X1 LogPath (last — touches the e2e log contract) → X2 HarnessHelpers → C8 HookInterest → X4 ProjectLayering decision.

## 8. Verification contract for every merge

- The affected e2e scenarios + the two offline unit suites must stay green.
- `git diff` must show **no change to any `[Telescope]`/`[NeoVisual]`/`[Hook]` log literal** — a diagnostic change forces the feature lane (M-M7).
- Every merge that changes a public/internal signature must update the affected unit tests in the same commit.
- The pure seams (N3 `WindowNavigationEngine`, T1 `TextMotionEngine`, L2 `FinderBase`, T5 `FocusGuard`) must ship with NEW unit tests (the `OverlayKeyHandler`/`TextMotionNavigator` pattern) — that is the point of extracting them.
- `pwsh tools/lint/check-doc-refs.ps1` must pass after any doc touch.
